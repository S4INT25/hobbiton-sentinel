using System.Text;
using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class PostgresCaseOutcomeStore(IDbContextFactory<SentinelDbContext> dbFactory) : ICaseOutcomeStore
{
    public async Task SaveAsync(CaseOutcome outcome)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Upsert by case_id (a case can only have one outcome)
        var existing = await db.CaseOutcomes.FirstOrDefaultAsync(o => o.CaseId == outcome.CaseId);
        if (existing is null)
        {
            db.CaseOutcomes.Add(outcome);
        }
        else
        {
            existing.Outcome = outcome.Outcome;
            existing.Resolution = outcome.Resolution;
            existing.ResolvedBy = outcome.ResolvedBy;
            existing.ResolvedAt = outcome.ResolvedAt;
            existing.OccurrenceCount = outcome.OccurrenceCount;
            existing.Confidence = outcome.Confidence;
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<CaseOutcome>> FindByEntitiesAsync(IEnumerable<string> entities, int limit = 20)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entityList = entities.ToList();
        if (entityList.Count == 0) return [];

        // Search for any entity appearing in the comma-separated affected_entities field
        var query = db.CaseOutcomes.AsNoTracking().AsQueryable();

        // Build OR conditions: affected_entities LIKE '%entity1%' OR LIKE '%entity2%'
        query = query.Where(o =>
            entityList.Any(e => EF.Functions.ILike(o.AffectedEntities, $"%{e}%")));

        return await query
            .OrderByDescending(o => o.ResolvedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<CaseOutcome>> FindByCategoryAsync(string category, int limit = 20)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.CaseOutcomes
            .AsNoTracking()
            .Where(o => o.Category == category)
            .OrderByDescending(o => o.ResolvedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<CaseOutcome>> FindByPatternAsync(int patternId, int limit = 20)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.CaseOutcomes
            .AsNoTracking()
            .Where(o => o.PatternId == patternId)
            .OrderByDescending(o => o.ResolvedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<OutcomeStats>> GetStatsAsync(string? database = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.CaseOutcomes.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(database))
            query = query.Where(o => o.Database == database);

        return await query
            .GroupBy(o => o.Category)
            .Select(g => new OutcomeStats
            {
                Category = g.Key,
                TotalCases = g.Count(),
                ConfirmedFraud = g.Count(o => o.Outcome == "confirmed_fraud"),
                FalsePositives = g.Count(o => o.Outcome == "false_positive"),
                Inconclusive = g.Count(o => o.Outcome == "inconclusive"),
                AutoResolved = g.Count(o => o.Outcome == "auto_resolved")
            })
            .ToListAsync();
    }

    public async Task<string> GetLearningsSummaryAsync(string? database = null, int recentDays = 90)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow.AddDays(-recentDays);

        var query = db.CaseOutcomes
            .AsNoTracking()
            .Where(o => o.ResolvedAt >= cutoff);

        if (!string.IsNullOrEmpty(database))
            query = query.Where(o => o.Database == database);

        var outcomes = await query.ToListAsync();
        if (outcomes.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("## Historical Case Learnings (last 90 days)");
        sb.AppendLine();

        // Overall stats. FP% is computed over human-reviewed cases only (confirmed + false_positive) —
        // "inconclusive" is the agent's own non-committal auto-resolve default, not a real verdict, and
        // including it in the denominator understated the true FP rate by ~9x against analyst review.
        var total = outcomes.Count;
        var confirmed = outcomes.Count(o => o.Outcome == "confirmed_fraud");
        var fp = outcomes.Count(o => o.Outcome == "false_positive");
        var inconclusive = outcomes.Count(o => o.Outcome == "inconclusive");
        var autoResolved = outcomes.Count(o => o.Outcome == "auto_resolved");
        var reviewed = confirmed + fp;

        sb.AppendLine(
            $"Overall: {total} cases resolved — {confirmed} confirmed fraud, {fp} false positives out of {reviewed} human-reviewed ({(reviewed > 0 ? fp * 100 / reviewed : 0)}% FP rate), {inconclusive} inconclusive (agent auto-resolve, no analyst review — not a verdict), {autoResolved} auto-resolved.");
        sb.AppendLine();

        // Per-category breakdown
        var byCategory = outcomes.GroupBy(o => o.Category).OrderByDescending(g => g.Count());
        sb.AppendLine("### By Category");
        foreach (var g in byCategory)
        {
            var catFp = g.Count(o => o.Outcome == "false_positive");
            var catConfirmed = g.Count(o => o.Outcome == "confirmed_fraud");
            var catTotal = g.Count();
            var catReviewed = catConfirmed + catFp;
            sb.AppendLine(
                $"- **{g.Key}**: {catTotal} cases — {catConfirmed} confirmed, {catFp} false positives out of {catReviewed} reviewed ({(catReviewed > 0 ? catFp * 100 / catReviewed : 0)}% FP rate)");
        }

        sb.AppendLine();

        // Recent false positives — teach the agent what to avoid
        var recentFps = outcomes
            .Where(o => o.Outcome == "false_positive")
            .OrderByDescending(o => o.ResolvedAt)
            .Take(10)
            .ToList();

        if (recentFps.Count > 0)
        {
            sb.AppendLine("### Recent False Positives (learn from these)");
            sb.AppendLine(
                "These were flagged but turned out to be legitimate. Be more cautious with similar patterns:");
            foreach (var f in recentFps)
            {
                sb.AppendLine(
                    $"- Case {f.CaseId}: \"{f.Title}\" (category: {f.Category}, confidence: {f.Confidence}%)");
                sb.AppendLine($"  Entities: {f.AffectedEntities}");
                sb.AppendLine($"  Resolution: {f.Resolution ?? "No reason given"}");
            }

            sb.AppendLine();
        }

        // High-confidence confirmed fraud — reinforce good signals
        var recentConfirmed = outcomes
            .Where(o => o.Outcome == "confirmed_fraud")
            .OrderByDescending(o => o.ResolvedAt)
            .Take(5)
            .ToList();

        if (recentConfirmed.Count > 0)
        {
            sb.AppendLine("### Recently Confirmed Fraud (good signals)");
            foreach (var f in recentConfirmed)
            {
                sb.AppendLine(
                    $"- Case {f.CaseId}: \"{f.Title}\" (category: {f.Category}, confidence: {f.Confidence}%)");
                sb.AppendLine($"  Entities: {f.AffectedEntities}");
            }

            sb.AppendLine();
        }

        sb.AppendLine(
            "**Guidance:** Use these learnings to calibrate your confidence. If a pattern has a high historical FP rate, require stronger evidence before creating a case. Always call `check_history` before creating a case to see if similar entities/patterns were previously flagged as false positives.");

        return sb.ToString();
    }
}