using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;

namespace Sentinel.Admin.Pages.Runs;

[Authorize(Policy = AuthConstants.Policy)]
public class IndexModel(IRunLogStore runLogStore) : PageModel
{
    public List<RunSummary> Runs { get; set; } = [];

    public async Task OnGetAsync()
    {
        Runs = await runLogStore.GetRecentRunsAsync(50);
    }

    public IActionResult OnPostTriggerRun()
    {
        var username = User.Identity?.Name ?? "unknown";
        BackgroundJob.Enqueue<Jobs.SentinelJob>(
            j => j.RunAsync($"manual:{username}"));
        return RedirectToPage();
    }
}
