using System.Collections.Concurrent;
using Sentinel.Admin.Models;
using Sentinel.Agent;

namespace Sentinel.Admin.Stores;

public class InMemoryEvidenceSourceStore : IEvidenceSourceStore
{
    private readonly ConcurrentDictionary<int, EvidenceSource> _sources = new();
    private int _nextId = 100;

    public Task<List<EvidenceSource>> GetAllAsync() =>
        Task.FromResult(_sources.Values.OrderBy(e => e.Id).ToList());

    public Task<List<EvidenceSource>> GetEnabledAsync() =>
        Task.FromResult(_sources.Values.Where(e => e.Enabled).OrderBy(e => e.Id).ToList());

    public Task<List<EvidenceSource>> GetEnabledForWorkflowAsync(string workflowId) =>
        Task.FromResult(_sources.Values
            .Where(e => e.Enabled && (string.IsNullOrEmpty(e.WorkflowId) || e.WorkflowId == workflowId))
            .OrderBy(e => e.Id).ToList());

    public Task<List<EvidenceSource>> GetByWorkflowAsync(string workflowId) =>
        Task.FromResult(_sources.Values
            .Where(e => e.WorkflowId == workflowId)
            .OrderBy(e => e.Id).ToList());

    public Task<EvidenceSource?> GetByIdAsync(int id) =>
        Task.FromResult(_sources.TryGetValue(id, out var s) ? s : null);

    public Task UpsertAsync(EvidenceSource source)
    {
        if (source.Id == 0)
            source.Id = Interlocked.Increment(ref _nextId);
        source.UpdatedAt = DateTime.UtcNow;
        _sources[source.Id] = source;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id)
    {
        _sources.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task SeedDefaultsAsync()
    {
        if (!_sources.IsEmpty)
        {
            foreach (var source in _sources.Values.Where(s => string.IsNullOrWhiteSpace(s.WorkflowId)))
                source.WorkflowId = WorkflowDefaults.FraudRunWorkflowId;
            return Task.CompletedTask;
        }
        foreach (var s in EvidenceSourceDefaults.GetDefaults())
        {
            s.WorkflowId = WorkflowDefaults.FraudRunWorkflowId;
            _sources[s.Id] = s;
        }
        return Task.CompletedTask;
    }
}
