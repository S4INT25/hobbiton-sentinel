using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IEvidenceSourceStore
{
    Task<List<EvidenceSource>> GetAllAsync();
    Task<List<EvidenceSource>> GetEnabledAsync();

    /// <summary>Gets enabled evidence sources scoped to a workflow (includes global sources where WorkflowId is null/empty).</summary>
    Task<List<EvidenceSource>> GetEnabledForWorkflowAsync(string workflowId);

    /// <summary>Gets all evidence sources assigned to a specific workflow.</summary>
    Task<List<EvidenceSource>> GetByWorkflowAsync(string workflowId);

    Task<EvidenceSource?> GetByIdAsync(int id);
    Task UpsertAsync(EvidenceSource source);
    Task DeleteAsync(int id);
    Task EnsureTableAsync();
    Task SeedDefaultsAsync();
}
