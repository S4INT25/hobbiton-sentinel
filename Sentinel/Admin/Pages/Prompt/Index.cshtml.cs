using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;

namespace Sentinel.Admin.Pages.Prompt;

[Authorize(Policy = AuthConstants.Policy)]
public class IndexModel(ISystemPromptStore promptStore, IAuditLogStore auditStore) : PageModel
{
    public SystemPromptOverride? Current { get; set; }
    public string? PromptText { get; set; }
    public List<SystemPromptOverride> History { get; set; } = [];

    public async Task OnGetAsync()
    {
        Current = await promptStore.GetOverrideAsync();
        PromptText = Current?.PromptText;
        History = await promptStore.GetHistoryAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync(string promptText)
    {
        var username = User.Identity?.Name ?? "unknown";
        await promptStore.SaveOverrideAsync(promptText, username);

        await auditStore.LogAsync(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = username,
            Action = "update",
            ResourceType = "prompt",
            ResourceId = "system_prompt",
            Details = "Prompt override saved",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        var username = User.Identity?.Name ?? "unknown";
        await promptStore.ClearOverrideAsync();

        await auditStore.LogAsync(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = username,
            Action = "reset",
            ResourceType = "prompt",
            ResourceId = "system_prompt",
            Details = "Reverted to default prompt",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return RedirectToPage();
    }
}
