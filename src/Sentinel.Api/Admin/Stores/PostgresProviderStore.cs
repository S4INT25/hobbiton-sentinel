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
                    // Was omitted, so a newly seeded provider could never become the default
                    // however the seed was written.
                    IsDefault = def.IsDefault,
                    SortOrder = def.SortOrder,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                anyChanged = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(match.DisplayName))
            {
                match.DisplayName = def.DisplayName;
                match.UpdatedAt = now;
                anyChanged = true;
            }

            // Realign a row the admin has never edited to the shipped configuration.
            //
            // Two failure modes to avoid, and this threads between them: always re-applying the
            // seed silently undoes an admin turning a provider on or picking a different default
            // on every deploy; never re-applying it means a shipped change — such as moving the
            // default to DeepSeek — can never reach an install that has already been seeded.
            // An untouched row (UpdatedAt still equal to CreatedAt) carries no admin intent to
            // protect, so it follows the seed. The moment anyone saves it, it is theirs.
            if (match.UpdatedAt == match.CreatedAt &&
                (match.Enabled != def.Enabled || match.IsDefault != def.IsDefault ||
                 match.Endpoint != def.Endpoint || match.SortOrder != def.SortOrder))
            {
                match.Enabled = def.Enabled;
                match.IsDefault = def.IsDefault;
                match.Endpoint = def.Endpoint;
                match.SortOrder = def.SortOrder;
                // Deliberately not ApiKey: the seed has none, and blanking a key an admin
                // entered is the exact failure this whole method exists to avoid.
                anyChanged = true;
            }
        }

        // Guarantee exactly one default among enabled providers.
        //
        // Without this, demoting the previous default while the intended replacement is
        // admin-protected leaves none at all, and the resolver silently falls through to whatever
        // provider happens to be first. Prefer the seeded default, then any enabled provider.
        var candidates = existing.Concat(db.Providers.Local).Distinct().Where(p => p.Enabled).ToList();
        var currentDefaults = candidates.Where(p => p.IsDefault).ToList();

        if (currentDefaults.Count != 1 && candidates.Count > 0)
        {
            var seededDefaultSlug = defaults.FirstOrDefault(d => d.IsDefault)?.Slug;
            var winner = candidates.FirstOrDefault(p =>
                             string.Equals(p.Slug, seededDefaultSlug, StringComparison.OrdinalIgnoreCase))
                         ?? candidates[0];

            foreach (var p in candidates.Where(p => p.IsDefault != (p == winner)))
            {
                p.IsDefault = p == winner;
                p.UpdatedAt = now;
                anyChanged = true;
            }
        }

        // Only ensure defaults exist — never delete non-default providers that the admin added
        if (anyChanged)
            await db.SaveChangesAsync();
    }
}
