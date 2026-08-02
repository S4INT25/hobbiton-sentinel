using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class PostgresProviderStore(IDbContextFactory<SentinelDbContext> dbFactory) : IProviderStore
{
    public async Task<List<ProviderConfig>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Providers
            .AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.DisplayName)
            .ToListAsync();
    }

    public async Task<List<ProviderConfig>> GetEnabledAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Providers
            .AsNoTracking()
            .Where(p => p.Enabled)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.DisplayName)
            .ToListAsync();
    }

    public async Task<ProviderConfig?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Providers.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<ProviderConfig?> GetBySlugAsync(string slug)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Providers
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug);
    }

    public async Task UpsertAsync(ProviderConfig provider)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        provider.UpdatedAt = DateTime.UtcNow;

        var existing = await db.Providers.FirstOrDefaultAsync(p => p.Id == provider.Id);
        if (existing is null)
        {
            provider.CreatedAt = DateTime.UtcNow;
            db.Providers.Add(provider);
        }
        else
        {
            existing.DisplayName = provider.DisplayName;
            existing.Slug = provider.Slug;
            existing.ApiKey = provider.ApiKey;
            existing.Endpoint = provider.Endpoint;
            existing.Enabled = provider.Enabled;
            existing.IsDefault = provider.IsDefault;
            existing.SortOrder = provider.SortOrder;
            existing.UpdatedAt = provider.UpdatedAt;
        }

        if (provider.IsDefault)
        {
            var others = await db.Providers
                .Where(p => p.Id != provider.Id && p.IsDefault)
                .ToListAsync();
            foreach (var other in others) other.IsDefault = false;
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var defaultProvider = await db.Providers
            .FirstOrDefaultAsync(p => p.IsDefault && p.Id != id);
        if (defaultProvider is null)
            throw new InvalidOperationException("Cannot delete the only/default provider. Mark another provider as default first.");

        var orphaned = await db.LlmModels
            .Where(m => m.ProviderId == id)
            .ToListAsync();
        foreach (var model in orphaned)
            model.ProviderId = defaultProvider.Id;

        var entity = await db.Providers.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is not null)
        {
            db.Providers.Remove(entity);
            await db.SaveChangesAsync();
        }
    }

    public async Task SeedDefaultsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var defaults = ProviderDefaults.All;
        var existing = await db.Providers.ToListAsync();
        var now = DateTime.UtcNow;
        var anyChanged = false;

        foreach (var def in defaults)
        {
            var match = existing.FirstOrDefault(e =>
                string.Equals(e.Slug, def.Slug, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                db.Providers.Add(new ProviderConfig
                {
                    DisplayName = def.DisplayName,
                    Slug = def.Slug,
                    ApiKey = def.ApiKey,
                    Endpoint = def.Endpoint,
                    Enabled = def.Enabled,
                    SortOrder = def.SortOrder,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                anyChanged = true;
            }
            else if (string.IsNullOrWhiteSpace(match.DisplayName))
            {
                // Repair a blank label, nothing more.
                //
                // Enabled, IsDefault, Endpoint and ApiKey are all admin-owned: they are edited on
                // the Models & Providers page and must survive a restart. Re-applying the seed
                // values here would silently switch a provider the admin turned on back off, and
                // hand "default" back to OpenRouter, on every deploy.
                match.DisplayName = def.DisplayName;
                match.UpdatedAt = now;
                anyChanged = true;
            }
        }

        // Only ensure defaults exist — never delete non-default providers that the admin added
        if (anyChanged)
            await db.SaveChangesAsync();
    }
}
