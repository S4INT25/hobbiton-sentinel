using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Stores;

namespace Sentinel.Admin.Pages;

[AllowAnonymous]
public class LoginModel(IUserStore userStore, IAuditLogStore auditStore) : PageModel
{
    public string? Error { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string username, string password)
    {
        var user = await userStore.GetByUsernameAsync(username);
        if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            Error = "Invalid username or password";
            await auditStore.LogAsync(new Models.AuditLog
            {
                Action = "login_failed",
                ResourceType = "auth",
                ResourceId = username,
                Username = username,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
            });
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("display_name", user.DisplayName)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        await userStore.UpdateLastLoginAsync(user.Id);

        await auditStore.LogAsync(new Models.AuditLog
        {
            UserId = user.Id,
            Username = user.Username,
            Action = "login",
            ResourceType = "auth",
            ResourceId = user.Id,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return RedirectToPage("/Index");
    }
}
