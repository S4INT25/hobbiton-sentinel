using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class AgentMemoryStore(IDbContextFactory<SentinelDbContext> dbFactory) : IAgentMemoryStore
{
    public async Task<List<AgentMemory>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.AgentMemories.OrderBy(m => m.Term).ToListAsync();
    }

    public async Task<List<AgentMemory>> GetEnabledAsync(string? database = null, string? workflowId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.AgentMemories
            .Where(m => m.Enabled
                        && (m.Database == null || m.Database == database)
                        && (m.WorkflowId == null || m.WorkflowId == "" || m.WorkflowId == workflowId))
            .OrderBy(m => m.Term)
            .ToListAsync();
    }

    public async Task<List<AgentMemory>> GetByWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.AgentMemories
            .Where(m => (m.WorkflowId ?? "") == workflowId)
            .OrderBy(m => m.Term)
            .ToListAsync();
    }

    public async Task<AgentMemory?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.AgentMemories.FindAsync(id);
    }

    public async Task SaveAsync(AgentMemory memory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        memory.UpdatedAt = DateTimeOffset.UtcNow;
        if (memory.Id == 0)
        {
            memory.CreatedAt = DateTimeOffset.UtcNow;
            db.AgentMemories.Add(memory);
        }
        else
        {
            db.AgentMemories.Update(memory);
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var memory = await db.AgentMemories.FindAsync(id);
        if (memory != null)
        {
            db.AgentMemories.Remove(memory);
            await db.SaveChangesAsync();
        }
    }
}