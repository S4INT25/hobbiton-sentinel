using System.Text.Json;

namespace Sentinel.Infrastructure;

/// <summary>
/// JSON parsing utilities for handling the many shapes the LLM produces
/// (proper arrays, string-encoded arrays, comma/newline-separated strings, etc.)
/// </summary>
public static class JsonHelpers
{
    /// <summary>
    /// Converts a JsonElement to a list of non-empty strings.
    /// Handles: JSON array, newline-separated string, or anything else → empty list.
    /// </summary>
    public static List<string> ToStringList(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Array => el.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList(),

        JsonValueKind.String => (el.GetString() ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList(),

        _ => []
    };

    /// <summary>
    /// Like ToStringList but also handles comma/semicolon-separated strings and
    /// string-encoded JSON arrays — all shapes the LLM may produce for IP lists.
    /// </summary>
    public static List<string> ToIpList(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Array => el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList(),

            JsonValueKind.String => ParseDelimitedString(el.GetString() ?? ""),

            _ => []
        };
    }

    /// <summary>
    /// Parses a string that may be a JSON array literal or a comma/newline/semicolon-separated list.
    /// </summary>
    public static List<string> ParseDelimitedString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        // Try JSON array first (LLM sometimes wraps array in a string)
        if (raw.TrimStart().StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    return doc.RootElement.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
            }
            catch { /* fall through */ }
        }

        return raw.Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }
}
