using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;
using Sentinel.Memory;

namespace Sentinel.Admin.Pages.Cases;

[Authorize(Policy = AuthConstants.Policy)]
public class DetailModel(ICaseStore caseStore, IFeedbackRuleStore ruleStore, IAuditLogStore auditStore) : PageModel
{
    public FraudCase? Case { get; set; }

    public async Task OnGetAsync(string id)
    {
        Case = await caseStore.GetCaseAsync(id);
    }

    public async Task<IActionResult> OnPostFeedbackAsync(string caseId, string action)
    {
        switch (action)
        {
            case "escalate":
                var c = await caseStore.GetCaseAsync(caseId);
                if (c != null) { c.Severity = "critical"; await caseStore.SaveCaseAsync(c); }
                break;
            case "resolve":
                await caseStore.ResolveCaseAsync(caseId, "Manually resolved by analyst");
                break;
        }

        await auditStore.LogAsync(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = User.Identity?.Name ?? "unknown",
            Action = $"case_{action}",
            ResourceType = "case",
            ResourceId = caseId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return RedirectToPage(new { id = caseId });
    }

    public async Task<IActionResult> OnPostFalsePositiveAsync(
        string caseId, string reason, bool createRule, string? ruleType, string? matchValue)
    {
        // Resolve the case
        await caseStore.ResolveCaseAsync(caseId, $"False positive: {reason}");

        // Optionally create a suppression rule
        if (createRule && !string.IsNullOrWhiteSpace(ruleType) && !string.IsNullOrWhiteSpace(matchValue))
        {
            var rule = new FeedbackRule
            {
                RuleType = ruleType,
                MatchValue = matchValue,
                Action = "suppress",
                Reason = reason,
                CreatedBy = User.Identity?.Name ?? "unknown",
                SourceCaseId = caseId
            };
            await ruleStore.SaveAsync(rule);

            await auditStore.LogAsync(new AuditLog
            {
                UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
                Username = User.Identity?.Name ?? "unknown",
                Action = "create",
                ResourceType = "rule",
                ResourceId = rule.Id,
                Details = $"From case {caseId}: {ruleType}={matchValue}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
            });
        }

        await auditStore.LogAsync(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = User.Identity?.Name ?? "unknown",
            Action = "case_false_positive",
            ResourceType = "case",
            ResourceId = caseId,
            Details = reason,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return RedirectToPage("/Cases/Index");
    }
}
