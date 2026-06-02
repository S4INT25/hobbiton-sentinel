using System.Collections.Concurrent;
using Sentinel.Admin.Models;
using Sentinel.Agent;

namespace Sentinel.Admin.Stores;

public class InMemoryFraudPatternStore : IFraudPatternStore
{
    private readonly ConcurrentDictionary<int, FraudPatternEntity> _patterns = new();
    private int _nextId = 100;

    public Task<List<FraudPatternEntity>> GetAllAsync() =>
        Task.FromResult(_patterns.Values.OrderBy(p => p.Id).ToList());

    public Task<List<FraudPatternEntity>> GetEnabledAsync() =>
        Task.FromResult(_patterns.Values.Where(p => p.Enabled).OrderBy(p => p.Id).ToList());

    public Task<List<FraudPatternEntity>> GetEnabledForWorkflowAsync(string workflowId) =>
        Task.FromResult(_patterns.Values
            .Where(p => p.Enabled && (string.IsNullOrEmpty(p.WorkflowId) || p.WorkflowId == workflowId))
            .OrderBy(p => p.Id).ToList());

    public Task<List<FraudPatternEntity>> GetByWorkflowAsync(string workflowId) =>
        Task.FromResult(_patterns.Values
            .Where(p => p.WorkflowId == workflowId)
            .OrderBy(p => p.Id).ToList());

    public Task<FraudPatternEntity?> GetByIdAsync(int id) =>
        Task.FromResult(_patterns.TryGetValue(id, out var p) ? p : null);

    public Task UpsertAsync(FraudPatternEntity pattern)
    {
        if (pattern.Id == 0)
            pattern.Id = Interlocked.Increment(ref _nextId);
        pattern.UpdatedAt = DateTime.UtcNow;
        _patterns[pattern.Id] = pattern;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id)
    {
        _patterns.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task EnsureTableAsync() => Task.CompletedTask;

    public Task SeedDefaultsAsync()
    {
        if (!_patterns.IsEmpty) return Task.CompletedTask;
        foreach (var p in FraudPatternRegistry.GetDefaults())
        {
            _patterns[p.Id] = new FraudPatternEntity
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Category = p.Category.ToString(),
                Enabled = p.EnabledByDefault,
                CreatedBy = "system",
                WorkflowId = WorkflowDefaults.FraudRunWorkflowId
            };
        }
        return Task.CompletedTask;
    }
}
