namespace Sentinel.Memory;

public interface ICaseStore
{
    Task<List<FraudCase>> GetOpenCasesAsync();
    Task<FraudCase> SaveCaseAsync(FraudCase fraudCase);
    Task<FraudCase?> GetCaseAsync(string id);
    Task ResolveCaseAsync(string id, string resolution);
    Task<string> GetOpenCasesSummaryAsync();
    Task<int> AutoResolveStaleAsync(int thresholdDays);
}
