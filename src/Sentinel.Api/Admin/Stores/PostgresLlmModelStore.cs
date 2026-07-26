using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class PostgresLlmModelStore(IDbContextFactory<SentinelDbContext> dbFactory) : ILlmModelStore
{
    public async Task<List<LlmModel>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.LlmModels
            .AsNoTracking()
            .OrderBy(m => m.SortOrder).ThenBy(m => m.DisplayName)
            .ToListAsync();
    }

    public async Task<List<LlmModel>> GetEnabledAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.LlmModels
            .AsNoTracking()
            .Where(m => m.Enabled)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.DisplayName)
            .ToListAsync();
    }

    public async Task<LlmModel?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.LlmModels.FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task UpsertAsync(LlmModel model)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        model.UpdatedAt = DateTime.UtcNow;

        var existing = await db.LlmModels.FirstOrDefaultAsync(m => m.Id == model.Id);
        if (existing is null)
        {
            model.CreatedAt = DateTime.UtcNow;
            db.LlmModels.Add(model);
        }
        else
        {
            existing.DisplayName = model.DisplayName;
            existing.ModelId = model.ModelId;
            existing.Description = model.Description;
            existing.Enabled = model.Enabled;
            existing.IsDefault = model.IsDefault;
            existing.SortOrder = model.SortOrder;
            existing.UpdatedAt = model.UpdatedAt;
        }

        // Only one default at a time
        if (model.IsDefault)
        {
            var others = await db.LlmModels
                .Where(m => m.Id != model.Id && m.IsDefault)
                .ToListAsync();
            foreach (var other in others) other.IsDefault = false;
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.LlmModels.FirstOrDefaultAsync(m => m.Id == id);
        if (entity is not null)
        {
            db.LlmModels.Remove(entity);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seed the default OpenRouter models if the table is empty. Called once at startup.
    /// </summary>
    public async Task SeedDefaultsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.LlmModels.AnyAsync()) return;

        db.LlmModels.AddRange(LlmModelDefaults.All);
        await db.SaveChangesAsync();
    }
}
