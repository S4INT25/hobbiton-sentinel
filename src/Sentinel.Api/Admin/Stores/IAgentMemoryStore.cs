using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IAgentMemoryStore
{
    Task<List<AgentMemory>> GetAllAsync();
    Task<List<AgentMemory>> GetEnabledAsync(string? database = null, string? workflowId = null);
    Task<List<AgentMemory>> GetByWorkflowAsync(string workflowId);
    Task<AgentMemory?> GetByIdAsync(int id);
    Task SaveAsync(AgentMemory memory);
    Task DeleteAsync(int id);
}