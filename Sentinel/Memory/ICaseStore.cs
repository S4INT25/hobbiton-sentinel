namespace Sentinel.Memory;

public interface ICaseStore
{
    Task<List<FraudCase>> GetOpenCasesAsync();

    /// <summary>Gets open cases filtered by workflow (null = all cases).</summary>
    Task<List<FraudCase>> GetOpenCasesForWorkflowAsync(string? workflowId);

    Task<FraudCase> SaveCaseAsync(FraudCase fraudCase);
    Task<FraudCase?> GetCaseAsync(string id);
    Task ResolveCaseAsync(string id, string resolution);
    Task<int> ResolveCasesAsync(IReadOnlyList<string> ids, string resolution);
    Task DeleteCaseAsync(string id);
    Task<string> GetOpenCasesSummaryAsync();

    /// <summary>Gets open cases summary filtered by workflow.</summary>
    Task<string> GetOpenCasesSummaryForWorkflowAsync(string? workflowId);

    Task<int> AutoResolveStaleAsync(int thresholdDays);
}