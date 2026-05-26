using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;

namespace Sentinel.Admin.Pages.Users;

[Authorize(Policy = AuthConstants.AdminOnlyPolicy)]
public class IndexModel(IUserStore userStore, IAuditLogStore auditStore) : PageModel
{
    public List<AdminUser> Users { get; set; } = [];

    public async Task OnGetAsync()
    {
        Users = await userStore.GetAllAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync(
        string username, string password, string displayName, string role)
    {
        var user = new AdminUser
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            DisplayName = displayName,
            Role = role
        };

        await userStore.SaveAsync(user);
        await auditStore.LogAsync(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = User.Identity?.Name ?? "unknown",
            Action = "create",
            ResourceType = "user",
            ResourceId = user.Id,
            Details = $"Created user: {username} ({role})",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string userId)
    {
        await userStore.DeleteAsync(userId);
        await auditStore.LogAsync(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = User.Identity?.Name ?? "unknown",
            Action = "delete",
            ResourceType = "user",
            ResourceId = userId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return RedirectToPage();
    }
}
