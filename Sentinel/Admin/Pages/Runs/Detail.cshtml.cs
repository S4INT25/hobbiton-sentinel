using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;

namespace Sentinel.Admin.Pages.Runs;

[Authorize(Policy = AuthConstants.Policy)]
public class DetailModel(IRunLogStore runLogStore) : PageModel
{
    public string RunId { get; set; } = "";
    public RunSummary? Summary { get; set; }
    public List<RunLog> Logs { get; set; } = [];

    public async Task OnGetAsync(string runId)
    {
        RunId = runId;
        Summary = await runLogStore.GetRunSummaryAsync(runId);
        Logs = await runLogStore.GetRunLogsAsync(runId);
    }
}
