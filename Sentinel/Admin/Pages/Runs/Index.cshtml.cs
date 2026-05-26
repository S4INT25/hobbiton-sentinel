using Microsoft.AspNetCore.Authorization;
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
}
