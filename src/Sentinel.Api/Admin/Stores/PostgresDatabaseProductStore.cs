using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class PostgresDatabaseProductStore(IDbContextFactory<SentinelDbContext> dbFactory) : IDatabaseProductStore
{
    public async Task<List<DatabaseProduct>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.DatabaseProducts
            .AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.DatabaseName)
            .ToListAsync();
    }

    public async Task<List<DatabaseProduct>> GetEnabledAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.DatabaseProducts
            .AsNoTracking()
            .Where(p => p.Enabled)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.DatabaseName)
            .ToListAsync();
    }

    public async Task<DatabaseProduct?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.DatabaseProducts.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task UpsertAsync(DatabaseProduct product)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        product.UpdatedAt = DateTime.UtcNow;

        var existing = await db.DatabaseProducts.FirstOrDefaultAsync(p => p.Id == product.Id);
        if (existing is null)
        {
            product.CreatedAt = DateTime.UtcNow;
            db.DatabaseProducts.Add(product);
        }
        else
        {
            existing.DatabaseName = product.DatabaseName;
            existing.DisplayName = product.DisplayName;
            existing.Description = product.Description;
            existing.Enabled = product.Enabled;
            existing.SortOrder = product.SortOrder;
            existing.UpdatedAt = product.UpdatedAt;
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.DatabaseProducts.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is not null)
        {
            db.DatabaseProducts.Remove(entity);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Add any default database that is not already present. Called once at startup.
    ///
    /// Seeds per-database rather than bailing when the table is non-empty: the all-or-nothing
    /// check meant a newly added platform never reached an existing install. Existing rows are
    /// left untouched — the admin owns their labels and enabled flags.
    /// </summary>
    public async Task SeedDefaultsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var defaults = new List<DatabaseProduct>
        {
            new()
            {
                DatabaseName = "inshuwa", DisplayName = "Insurer",
                Description = "Insurance platform — motor, life, travel, health, general", Enabled = true, SortOrder = 0
            },
            new()
            {
                DatabaseName = "lipila_blaze", DisplayName = "Lipila",
                Description = "Payment gateway — collections, disbursements, settlements", Enabled = true, SortOrder = 1
            },
            new()
            {
                DatabaseName = "bnpl", DisplayName = "BNPL",
                Description = "Lipila Later — buy-now-pay-later loans and repayments", Enabled = true, SortOrder = 2
            },
            new()
            {
                DatabaseName = "patumba_app", DisplayName = "Patumba App",
                Description = "Patumba — user wallets, transactions, transfers", Enabled = true, SortOrder = 3
            },
            new()
            {
                DatabaseName = "gari", DisplayName = "Gari",
                Description = "Motor insurance — quotations, policies, vehicles, agent commissions, claims",
                Enabled = true, SortOrder = 4
            },
        };

        var existing = await db.DatabaseProducts
            .Select(p => p.DatabaseName)
            .ToListAsync();
        var missing = defaults
            .Where(d => !existing.Contains(d.DatabaseName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count == 0) return;

        db.DatabaseProducts.AddRange(missing);
        await db.SaveChangesAsync();
    }
}