using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;
using Sentinel.Memory;

namespace Sentinel.Admin.Pages.Cases;

[Authorize(Policy = AuthConstants.Policy)]
public class IndexModel(ICaseStore caseStore) : PageModel
{
    public List<FraudCase> Cases { get; set; } = [];

    public async Task OnGetAsync()
    {
        Cases = await caseStore.GetOpenCasesAsync();
    }
}
