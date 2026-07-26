using System.Collections.Concurrent;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemoryProviderStore : IProviderStore
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

    public Task DeleteAsync(int id)
    {
        _providers.TryRemove(id, out _);
        return Task.CompletedTask;
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
            else
            {
                existing.DisplayName = def.DisplayName;
                existing.Endpoint = def.Endpoint;
                existing.Enabled = def.Enabled;
                existing.IsDefault = def.IsDefault;
                existing.SortOrder = def.SortOrder;
                existing.UpdatedAt = now;
                _providers[existing.Id] = existing;
            }
        }

        var defaultSlugs = defaults.Select(d => d.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, p) in _providers)
        {
            if (!defaultSlugs.Contains(p.Slug))
                _providers.TryRemove(id, out _);
        }
    }
}
