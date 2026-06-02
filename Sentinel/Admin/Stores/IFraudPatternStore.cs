using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IFraudPatternStore
{
    Task<List<FraudPatternEntity>> GetAllAsync();
    Task<List<FraudPatternEntity>> GetEnabledAsync();

    /// <summary>Gets enabled patterns scoped to a workflow (includes global patterns where WorkflowId is null/empty).</summary>
    Task<List<FraudPatternEntity>> GetEnabledForWorkflowAsync(string workflowId);

    /// <summary>Gets all patterns assigned to a specific workflow.</summary>
    Task<List<FraudPatternEntity>> GetByWorkflowAsync(string workflowId);

    Task<FraudPatternEntity?> GetByIdAsync(int id);
    Task UpsertAsync(FraudPatternEntity pattern);
    Task DeleteAsync(int id);
    Task EnsureTableAsync();
    Task SeedDefaultsAsync();
}
