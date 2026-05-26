using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;

namespace Sentinel.Admin.Pages.Analytics;

[Authorize(Policy = AuthConstants.Policy)]
public class IndexModel : PageModel
{
    public void OnGet() { }
}
