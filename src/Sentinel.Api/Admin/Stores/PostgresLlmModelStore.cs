using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class PostgresLlmModelStore(
    IDbContextFactory<SentinelDbContext> dbFactory,
    IProviderStore providerStore) : ILlmModelStore
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
    /// Keep the llm_models table in sync with <see cref="LlmModelDefaults"/>. New models
    /// are inserted, existing ones are updated when their properties change, and models
    /// no longer in the defaults list are removed. Runs on every startup.
    /// </summary>
    public async Task SeedDefaultsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var seeded = LlmModelDefaults.Seeded;
        var defaults = LlmModelDefaults.All;
        var existing = await db.LlmModels.ToListAsync();
        var now = DateTime.UtcNow;
        var anyChanged = false;

        // Each model names its own provider — a DeepSeek model id is not valid on OpenRouter and
        // vice versa, so they cannot all be pointed at one provider.
        var providerIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in seeded.Select(m => m.ProviderSlug).Distinct(StringComparer.OrdinalIgnoreCase))
            providerIds[slug] = (await providerStore.GetBySlugAsync(slug))?.Id ?? 0;

        foreach (var entry in seeded)
        {
            var def = entry.Model;
            var defaultProviderId = providerIds.GetValueOrDefault(entry.ProviderSlug);
            var match = existing.FirstOrDefault(e =>
                string.Equals(e.ModelId, def.ModelId, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                db.LlmModels.Add(new LlmModel
                {
                    DisplayName = def.DisplayName,
                    ModelId = def.ModelId,
                    Description = def.Description,
                    ProviderId = defaultProviderId,
                    Enabled = def.Enabled,
                    IsDefault = def.IsDefault,
                    SortOrder = def.SortOrder,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                anyChanged = true;
            }
            else
            {
                if (match.DisplayName != def.DisplayName || match.Description != def.Description ||
                    match.Enabled != def.Enabled || match.IsDefault != def.IsDefault ||
                    match.SortOrder != def.SortOrder || match.ProviderId != defaultProviderId)
                {
                    match.DisplayName = def.DisplayName;
                    match.Description = def.Description;
                    match.Enabled = def.Enabled;
                    match.IsDefault = def.IsDefault;
                    match.SortOrder = def.SortOrder;
                    // Re-point whenever the seed says a different provider, not only when unset.
                    // The old guard (== 0) meant the change-detection above would flag a provider
                    // mismatch on every boot and then never fix it.
                    if (defaultProviderId != 0) match.ProviderId = defaultProviderId;
                    match.UpdatedAt = now;
                    anyChanged = true;
                }
                // Ensure exactly one default across the table
                if (def.IsDefault)
                {
                    foreach (var other in existing.Where(e =>
                                 e.Id != match.Id && e.IsDefault &&
                                 !string.Equals(e.ModelId, def.ModelId, StringComparison.OrdinalIgnoreCase)))
                    {
                        other.IsDefault = false;
                        other.UpdatedAt = now;
                        anyChanged = true;
                    }
                }
            }
        }

        var defaultIds = defaults.Select(d => d.ModelId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var model in existing.Where(e => !defaultIds.Contains(e.ModelId)))
        {
            db.LlmModels.Remove(model);
            anyChanged = true;
        }

        if (anyChanged)
            await db.SaveChangesAsync();
    }
}
