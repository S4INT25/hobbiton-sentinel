using System.Collections.Concurrent;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemoryProviderStore(ILlmModelStore llmModelStore) : IProviderStore
{
    private readonly ConcurrentDictionary<int, ProviderConfig> _providers = new();
    private int _nextId;

    public Task<List<ProviderConfig>> GetAllAsync() =>
        Task.FromResult(_providers.Values
            .OrderBy(p => p.SortOrder).ThenBy(p => p.DisplayName)
            .ToList());

    public Task<List<ProviderConfig>> GetEnabledAsync() =>
        Task.FromResult(_providers.Values
            .Where(p => p.Enabled)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.DisplayName)
            .ToList());

    public Task<ProviderConfig?> GetByIdAsync(int id) =>
        Task.FromResult(_providers.TryGetValue(id, out var p) ? p : null);

    public Task<ProviderConfig?> GetBySlugAsync(string slug) =>
        Task.FromResult<ProviderConfig?>(
            _providers.Values.FirstOrDefault(p =>
                string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase)));

    public Task UpsertAsync(ProviderConfig provider)
    {
        var now = DateTime.UtcNow;
        if (provider.Id == 0)
            provider.Id = Interlocked.Increment(ref _nextId);
        if (provider.CreatedAt == default)
            provider.CreatedAt = now;
        provider.UpdatedAt = now;

        if (provider.IsDefault)
        {
            foreach (var other in _providers.Values.Where(p => p.Id != provider.Id && p.IsDefault))
                other.IsDefault = false;
        }

        _providers[provider.Id] = provider;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(int id)
    {
        var defaultProvider = _providers.Values.FirstOrDefault(p => p.IsDefault && p.Id != id);
        if (defaultProvider is null)
            throw new InvalidOperationException("Cannot delete the only/default provider. Mark another provider as default first.");

        var allModels = await llmModelStore.GetAllAsync();
        foreach (var model in allModels.Where(m => m.ProviderId == id))
        {
            model.ProviderId = defaultProvider.Id;
            await llmModelStore.UpsertAsync(model);
        }

        _providers.TryRemove(id, out _);
    }

    public async Task SeedDefaultsAsync()
    {
        var defaults = ProviderDefaults.All;
        var now = DateTime.UtcNow;

        foreach (var def in defaults)
        {
            var existing = _providers.Values.FirstOrDefault(p =>
                string.Equals(p.Slug, def.Slug, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                await UpsertAsync(new ProviderConfig
                {
                    DisplayName = def.DisplayName,
                    Slug = def.Slug,
                    ApiKey = def.ApiKey,
                    Endpoint = def.Endpoint,
                    Enabled = def.Enabled,
                    SortOrder = def.SortOrder
                });
            }
            else if (string.IsNullOrWhiteSpace(existing.DisplayName))
            {
                // Repair a blank label only — Enabled, IsDefault, Endpoint and ApiKey are
                // admin-owned and must survive a restart. See PostgresProviderStore for why.
                existing.DisplayName = def.DisplayName;
                existing.UpdatedAt = now;
                _providers[existing.Id] = existing;
            }
        }

        var defaultSlugs = defaults.Select(d => d.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Never delete non-default providers — admins may have added their own
    }
}
