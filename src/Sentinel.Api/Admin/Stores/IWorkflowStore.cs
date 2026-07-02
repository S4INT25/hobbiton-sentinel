using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IWorkflowStore
{
    Task<List<WorkflowDefinition>> GetAllAsync();
    Task<List<WorkflowDefinition>> GetEnabledAsync();
    Task<WorkflowDefinition?> GetByIdAsync(string id);
    Task UpsertAsync(WorkflowDefinition workflow);
    Task DeleteAsync(string id);
    Task SeedDefaultsAsync();
}