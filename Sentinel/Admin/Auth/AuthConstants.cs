using Microsoft.AspNetCore.Authentication.Cookies;

namespace Sentinel.Admin.Auth;

public static class AuthConstants
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string CookieName = "sentinel_auth";
    public const string AdminRole = "admin";
    public const string AnalystRole = "analyst";
    public const string DeveloperRole = "developer";
    public const string Policy = "SentinelAdminPolicy";
    public const string AdminOnlyPolicy = "SentinelAdminOnly";
}
