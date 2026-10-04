using System.Text;
using System.Text.Json;

namespace Sentinel.Admin.Auth;

// Admin-configured from the Identity Provider page; persisted in FusionCache alongside users.
public record GoogleIdpSettings(bool Enabled = false, string ClientId = "", string ClientSecret = "",
    string AllowedDomain = "");

public static class GoogleIdp
{
    public const string SettingsKey = "sentinel:idp:google";
    public const string StateCookie = "sentinel_google_state";
    public const string CallbackPath = "/api/auth/google/callback";
    public const string AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenUrl = "https://oauth2.googleapis.com/token";

    public record IdToken(string Aud, string Iss, long Exp, string Email, bool EmailVerified, string? Hd, string? Name);

    // ponytail: no JWKS signature check — the token comes straight from Google's token endpoint over TLS,
    // which OIDC Core §3.1.3.7 allows in place of signature validation. Add JWKS if tokens ever arrive via the browser.
    public static IdToken DecodeIdToken(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var root = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement;
        string? Str(string n) => root.TryGetProperty(n, out var v) ? v.GetString() : null;
        var verified = root.TryGetProperty("email_verified", out var ev) &&
                       (ev.ValueKind == JsonValueKind.True || ev.ValueKind == JsonValueKind.String && ev.GetString() == "true");
        return new IdToken(Str("aud") ?? "", Str("iss") ?? "", root.GetProperty("exp").GetInt64(),
            Str("email") ?? "", verified, Str("hd"), Str("name"));
    }

    // Returns null when the token is acceptable, otherwise the reason it was rejected.
    public static string? Validate(IdToken t, GoogleIdpSettings s, DateTimeOffset now)
    {
        if (t.Aud != s.ClientId) return "audience mismatch";
        if (t.Iss is not ("accounts.google.com" or "https://accounts.google.com")) return "issuer mismatch";
        if (DateTimeOffset.FromUnixTimeSeconds(t.Exp) < now) return "token expired";
        if (!t.EmailVerified || string.IsNullOrEmpty(t.Email)) return "email not verified";
        // hd is Google's own Workspace-domain claim — unlike the email suffix, a personal Gmail can't fake it.
        if (!string.IsNullOrWhiteSpace(s.AllowedDomain) &&
            !string.Equals(t.Hd, s.AllowedDomain.Trim(), StringComparison.OrdinalIgnoreCase))
            return "wrong domain";
        return null;
    }
}
