using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;

namespace Sentinel.Admin.Pages.Rules;

[Authorize(Policy = AuthConstants.Policy)]
public class IndexModel(IFeedbackRuleStore ruleStore, IAuditLogStore auditStore) : PageModel
{
    public List<FeedbackRule> Rules { get; set; } = [];

    public async Task OnGetAsync()
    {
        Rules = await ruleStore.GetAllRulesAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync(
        string ruleType, string matchValue, string action, string scope, string reason)
    {
        var rule = new FeedbackRule
        {
            RuleType = ruleType,
            MatchValue = matchValue,
            Action = action,
            Scope = scope,
            Reason = reason,
            CreatedBy = User.Identity?.Name ?? "unknown"
        };

        await ruleStore.SaveAsync(rule);
        await auditStore.LogAsync(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = User.Identity?.Name ?? "unknown",
            Action = "create",
            ResourceType = "rule",
            ResourceId = rule.Id,
            Details = $"{ruleType}: {matchValue} → {action}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string ruleId)
    {
        await ruleStore.DeleteAsync(ruleId);
        await auditStore.LogAsync(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = User.Identity?.Name ?? "unknown",
            Action = "delete",
            ResourceType = "rule",
            ResourceId = ruleId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return RedirectToPage();
    }
}
