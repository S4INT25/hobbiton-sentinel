using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface ICaseOutcomeStore
{
    /// <summary>Record the outcome of a resolved case.</summary>
    Task SaveAsync(CaseOutcome outcome);

    /// <summary>Get outcomes matching any of the given entity identifiers (merchant, IP, wallet, phone).</summary>
    Task<List<CaseOutcome>> FindByEntitiesAsync(IEnumerable<string> entities, int limit = 20);

    /// <summary>Get outcomes for a specific fraud category.</summary>
    Task<List<CaseOutcome>> FindByCategoryAsync(string category, int limit = 20);

    /// <summary>Get outcomes for a specific pattern ID.</summary>
    Task<List<CaseOutcome>> FindByPatternAsync(int patternId, int limit = 20);

    /// <summary>Get aggregate stats: how many confirmed vs false positive by category.</summary>
    Task<List<OutcomeStats>> GetStatsAsync(string? database = null);

    /// <summary>Build a summary block for the agent system prompt.</summary>
    Task<string> GetLearningsSummaryAsync(string? database = null, int recentDays = 90);
}

/// <summary>Aggregated outcome statistics per category.</summary>
public class OutcomeStats
{
    public string Category { get; set; } = "";
    public int TotalCases { get; set; }
    public int ConfirmedFraud { get; set; }
    public int FalsePositives { get; set; }
    public int Inconclusive { get; set; }
    public int AutoResolved { get; set; }
    public decimal FalsePositiveRate => TotalCases > 0 ? (decimal)FalsePositives / TotalCases * 100 : 0;
}