using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;
using Sentinel.Memory;

namespace Sentinel.Admin.Pages;

[Authorize(Policy = AuthConstants.Policy)]
public class IndexModel(
    ICaseStore caseStore,
    IRunLogStore runLogStore,
    IFeedbackRuleStore ruleStore) : PageModel
{
    public int OpenCases { get; set; }
    public int RecentRuns { get; set; }
    public int ActiveRules { get; set; }
    public long TokensUsed { get; set; }
    public List<RunSummary> Runs { get; set; } = [];

    public async Task OnGetAsync()
    {
        var cases = await caseStore.GetOpenCasesAsync();
        OpenCases = cases.Count;

        var rules = await ruleStore.GetActiveRulesAsync();
        ActiveRules = rules.Count;

        Runs = await runLogStore.GetRecentRunsAsync(10);
        RecentRuns = Runs.Count(r => r.StartedAt >= DateTime.UtcNow.AddHours(-24));
        TokensUsed = Runs
            .Where(r => r.StartedAt >= DateTime.UtcNow.AddHours(-24))
            .Sum(r => (long)(r.InputTokens + r.OutputTokens));
    }
}
