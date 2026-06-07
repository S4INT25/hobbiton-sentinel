using System.Collections.Concurrent;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

/// <summary>In-memory fallback when PostgreSQL is not configured.</summary>
public class InMemoryCaseOutcomeStore : ICaseOutcomeStore
{
    private readonly ConcurrentDictionary<string, CaseOutcome> _outcomes = new();

    public Task SaveAsync(CaseOutcome outcome)
    {
        _outcomes[outcome.CaseId] = outcome;
        return Task.CompletedTask;
    }

    public Task<List<CaseOutcome>> FindByEntitiesAsync(IEnumerable<string> entities, int limit = 20)
    {
        var entityList = entities.ToList();
        var results = _outcomes.Values
            .Where(o => entityList.Any(e => o.AffectedEntities.Contains(e, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(o => o.ResolvedAt)
            .Take(limit)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<List<CaseOutcome>> FindByCategoryAsync(string category, int limit = 20)
    {
        var results = _outcomes.Values
            .Where(o => o.Category == category)
            .OrderByDescending(o => o.ResolvedAt)
            .Take(limit)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<List<CaseOutcome>> FindByPatternAsync(int patternId, int limit = 20)
    {
        var results = _outcomes.Values
            .Where(o => o.PatternId == patternId)
            .OrderByDescending(o => o.ResolvedAt)
            .Take(limit)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<List<OutcomeStats>> GetStatsAsync(string? database = null)
    {
        var query = _outcomes.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(database))
            query = query.Where(o => o.Database == database);

        var stats = query
            .GroupBy(o => o.Category)
            .Select(g => new OutcomeStats
            {
                Category = g.Key,
                TotalCases = g.Count(),
                ConfirmedFraud = g.Count(o => o.Outcome == "confirmed_fraud"),
                FalsePositives = g.Count(o => o.Outcome == "false_positive"),
                Inconclusive = g.Count(o => o.Outcome == "inconclusive"),
                AutoResolved = g.Count(o => o.Outcome == "auto_resolved")
            })
            .ToList();
        return Task.FromResult(stats);
    }

    public Task<string> GetLearningsSummaryAsync(string? database = null, int recentDays = 90)
        => Task.FromResult("");
}