using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemorySystemPromptStore : ISystemPromptStore
{
    private SystemPromptOverride? _current;
    private readonly List<SystemPromptOverride> _history = [];

    public Task<SystemPromptOverride?> GetOverrideAsync() => Task.FromResult(_current);

    public Task SaveOverrideAsync(string promptText, string updatedBy)
    {
        _current = new SystemPromptOverride
        {
            PromptText = promptText,
            UpdatedBy = updatedBy,
            UpdatedAt = DateTime.UtcNow
        };
        _history.Insert(0, _current);
        if (_history.Count > 10) _history.RemoveRange(10, _history.Count - 10);
        return Task.CompletedTask;
    }

    public Task ClearOverrideAsync() { _current = null; return Task.CompletedTask; }

    public Task<List<SystemPromptOverride>> GetHistoryAsync(int limit = 5) =>
        Task.FromResult(_history.Take(limit).ToList());
}
