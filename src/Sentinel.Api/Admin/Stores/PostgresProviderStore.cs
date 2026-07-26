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
            else
            {
                if (match.DisplayName != def.DisplayName || match.Endpoint != def.Endpoint ||
                    match.Enabled != def.Enabled || match.IsDefault != def.IsDefault ||
                    match.SortOrder != def.SortOrder)
                {
                    match.DisplayName = def.DisplayName;
                    match.Endpoint = def.Endpoint;
                    match.Enabled = def.Enabled;
                    match.IsDefault = def.IsDefault;
                    match.SortOrder = def.SortOrder;
                    match.UpdatedAt = now;
                    anyChanged = true;
                }
            }
        }

        var defaultSlugs = defaults.Select(d => d.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in existing.Where(e => !defaultSlugs.Contains(e.Slug)))
        {
            db.Providers.Remove(p);
            anyChanged = true;
        }

        if (anyChanged)
            await db.SaveChangesAsync();
    }
}
