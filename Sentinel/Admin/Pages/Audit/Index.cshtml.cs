using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;

namespace Sentinel.Admin.Pages.Audit;

[Authorize(Policy = AuthConstants.Policy)]
public class IndexModel(IAuditLogStore auditStore) : PageModel
{
    public List<AuditLog> Logs { get; set; } = [];

    public async Task OnGetAsync()
    {
        Logs = await auditStore.GetRecentAsync(200);
    }
}
