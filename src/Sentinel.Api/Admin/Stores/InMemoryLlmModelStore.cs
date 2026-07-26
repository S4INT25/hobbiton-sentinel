using System.Collections.Concurrent;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemoryLlmModelStore : ILlmModelStore
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
        if (!_models.IsEmpty) return;
        foreach (var model in LlmModelDefaults.All)
            await UpsertAsync(model);
    }
}
