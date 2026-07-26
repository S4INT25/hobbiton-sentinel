using System.Collections.Concurrent;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemoryLlmModelStore(IProviderStore providerStore) : ILlmModelStore
{
    private readonly ConcurrentDictionary<int, LlmModel> _models = new();
    private int _nextId;

    public Task<List<LlmModel>> GetAllAsync() =>
        Task.FromResult(_models.Values
            .OrderBy(m => m.SortOrder).ThenBy(m => m.DisplayName)
            .ToList());

    public Task<List<LlmModel>> GetEnabledAsync() =>
        Task.FromResult(_models.Values
            .Where(m => m.Enabled)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.DisplayName)
            .ToList());

    public Task<LlmModel?> GetByIdAsync(int id) =>
        Task.FromResult(_models.TryGetValue(id, out var model) ? model : null);

    public Task UpsertAsync(LlmModel model)
    {
        var now = DateTime.UtcNow;
        if (model.Id == 0)
            model.Id = Interlocked.Increment(ref _nextId);
        if (model.CreatedAt == default)
            model.CreatedAt = now;
        model.UpdatedAt = now;

        // Only one default at a time
        if (model.IsDefault)
        {
            foreach (var other in _models.Values.Where(m => m.Id != model.Id && m.IsDefault))
                other.IsDefault = false;
        }

        _models[model.Id] = model;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id)
    {
        _models.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public async Task SeedDefaultsAsync()
    {
        var defaults = LlmModelDefaults.All;
        var now = DateTime.UtcNow;
        var existing = _models.Values.ToList();

        var openRouter = await providerStore.GetBySlugAsync("openrouter");
        var defaultProviderId = openRouter?.Id ?? 0;

        foreach (var def in defaults)
        {
            var match = existing.FirstOrDefault(m =>
                string.Equals(m.ModelId, def.ModelId, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                await UpsertAsync(new LlmModel
                {
                    DisplayName = def.DisplayName,
                    ModelId = def.ModelId,
                    Description = def.Description,
                    ProviderId = defaultProviderId,
                    Enabled = def.Enabled,
                    IsDefault = def.IsDefault,
                    SortOrder = def.SortOrder
                });
            }
            else
            {
                match.DisplayName = def.DisplayName;
                match.Description = def.Description;
                match.Enabled = def.Enabled;
                match.IsDefault = def.IsDefault;
                match.SortOrder = def.SortOrder;
                if (match.ProviderId == 0) match.ProviderId = defaultProviderId;
                match.UpdatedAt = now;
                _models[match.Id] = match;
            }
        }

        var defaultIds = defaults.Select(d => d.ModelId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, model) in _models)
        {
            if (!defaultIds.Contains(model.ModelId))
                _models.TryRemove(id, out _);
        }
    }
}
