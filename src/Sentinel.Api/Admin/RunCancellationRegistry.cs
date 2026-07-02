using System.Collections.Concurrent;

namespace Sentinel.Admin;

/// <summary>
/// Holds CancellationTokenSources keyed by runId so active runs can be stopped from the UI.
/// Singleton — shared between SentinelJob and API endpoints.
/// </summary>
public sealed class RunCancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sources = new();

    public CancellationToken Register(string runId)
    {
        var cts = new CancellationTokenSource();
        _sources[runId] = cts;
        return cts.Token;
    }

    public bool Cancel(string runId)
    {
        if (_sources.TryRemove(runId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            return true;
        }

        return false;
    }

    public void Remove(string runId)
    {
        if (_sources.TryRemove(runId, out var cts))
            cts.Dispose();
    }

    public bool IsRegistered(string runId) => _sources.ContainsKey(runId);
}