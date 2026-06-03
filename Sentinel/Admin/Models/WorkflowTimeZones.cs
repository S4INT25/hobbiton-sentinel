namespace Sentinel.Admin.Models;

public static class WorkflowTimeZones
{
    public const string CatIanaId = "Africa/Lusaka";
    public const string CatWindowsId = "South Africa Standard Time";
    public const string UtcId = "UTC";
    public const string DefaultId = CatIanaId;

    public static readonly IReadOnlyList<WorkflowTimeZoneOption> Options =
    [
        new(DefaultId, "Central Africa Time (CAT)"),
        new(UtcId, "UTC")
    ];

    public static bool IsSupported(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) &&
        Options.Any(o => string.Equals(o.Id, timeZoneId, StringComparison.OrdinalIgnoreCase));

    public static TimeZoneInfo ResolveOrDefault(string? timeZoneId)
    {
        var resolved = Resolve(timeZoneId);
        if (resolved != null) return resolved;

        return Resolve(CatIanaId)
               ?? Resolve(CatWindowsId)
               ?? TimeZoneInfo.Utc;
    }

    public static string NormalizeOrDefaultId(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && Resolve(timeZoneId) != null)
        {
            if (string.Equals(timeZoneId, CatWindowsId, StringComparison.OrdinalIgnoreCase))
                return CatIanaId;
            return timeZoneId.Trim();
        }

        return DefaultId;
    }

    private static TimeZoneInfo? Resolve(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch
        {
            // Cross-platform fallback between Windows and IANA CAT identifiers.
            if (string.Equals(timeZoneId, CatIanaId, StringComparison.OrdinalIgnoreCase))
                return Resolve(CatWindowsId);
            if (string.Equals(timeZoneId, CatWindowsId, StringComparison.OrdinalIgnoreCase))
                return Resolve(CatIanaId);
            return null;
        }
    }
}

public record WorkflowTimeZoneOption(string Id, string Label);
