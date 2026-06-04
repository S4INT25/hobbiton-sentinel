using System.Collections.Concurrent;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemoryAgentMemoryStore : IAgentMemoryStore
{
    private readonly ConcurrentDictionary<int, AgentMemory> _memories = new();
    private int _nextId = 0;

    public Task<List<AgentMemory>> GetAllAsync() =>
        Task.FromResult(_memories.Values
            .OrderBy(m => m.Term)
            .ThenBy(m => m.Database)
            .Select(Clone)
            .ToList());

    public Task<List<AgentMemory>> GetEnabledAsync(string? database = null) =>
        Task.FromResult(_memories.Values
            .Where(m => m.Enabled && (m.Database == null || m.Database == database))
            .OrderBy(m => m.Term)
            .ThenBy(m => m.Database)
            .Select(Clone)
            .ToList());

    public Task<AgentMemory?> GetByIdAsync(int id)
    {
        if (!_memories.TryGetValue(id, out var memory))
            return Task.FromResult<AgentMemory?>(null);
        return Task.FromResult<AgentMemory?>(Clone(memory));
    }

    public Task SaveAsync(AgentMemory memory)
    {
        var now = DateTimeOffset.UtcNow;

        if (memory.Id == 0)
        {
            var id = Interlocked.Increment(ref _nextId);
            memory.Id = id;
            memory.CreatedAt = now;
        }
        else if (_memories.TryGetValue(memory.Id, out var existing))
        {
            memory.CreatedAt = existing.CreatedAt;
        }
        else if (memory.CreatedAt == default)
        {
            memory.CreatedAt = now;
        }

        memory.UpdatedAt = now;
        _memories[memory.Id] = Clone(memory);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id)
    {
        _memories.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    private static AgentMemory Clone(AgentMemory source) =>
        new()
        {
            Id = source.Id,
            Term = source.Term,
            Definition = source.Definition,
            Database = source.Database,
            Enabled = source.Enabled,
            CreatedBy = source.CreatedBy,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
}
