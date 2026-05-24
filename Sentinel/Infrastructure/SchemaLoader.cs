using System.Text;
using System.Text.Json;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Infrastructure;

public class SchemaLoader(ClickHouseClient ch, IFusionCache cache, ILogger<SchemaLoader> logger)
{
    private const string SchemaCacheKey = "sentinel:schema:full_block";

    public async Task<string> GetSchemaBlockAsync() =>
        await cache.GetOrSetAsync(SchemaCacheKey,
            _ =>
            {
                logger.LogInformation("Schema cache miss — fetching live schema from ClickHouse");
                return FetchSchemaBlockAsync();
            },
            options => options.SetDuration(TimeSpan.FromDays(7)));

    public Task InvalidateAsync() => cache.RemoveAsync(SchemaCacheKey).AsTask();

    private async Task<string> FetchSchemaBlockAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Live Database Schema (lipila_blaze)");

        var tablesJson = await ch.QueryAsync("SHOW TABLES FROM lipila_blaze");
        var tables = ParseStringColumn(tablesJson, "name");

        if (tables.Count == 0)
        {
            sb.AppendLine("(schema unavailable — discovery failed)");
            return sb.ToString();
        }

        sb.AppendLine($"Tables: {string.Join(", ", tables)}");
        sb.AppendLine();

        foreach (var table in tables)
        {
            var descJson = await ch.QueryAsync($"DESCRIBE lipila_blaze.{table}");
            var columns = ParseDescribe(descJson);

            if (columns.Count == 0) continue;

            sb.AppendLine($"**{table}**: {string.Join(", ", columns)}");
        }

        sb.AppendLine();
        sb.AppendLine("Schema is pre-loaded — do NOT run SHOW TABLES or DESCRIBE unless you find an unexpected table.");

        return sb.ToString();
    }

    /// <summary>Parses a ClickHouse JSON result and extracts a single named string column.</summary>
    private static List<string> ParseStringColumn(string json, string column)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return [];

            return data.EnumerateArray()
                .Where(r => r.TryGetProperty(column, out var v) && v.ValueKind == JsonValueKind.String)
                .Select(r => r.GetProperty(column).GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>Parses DESCRIBE output into "name (type)" column summaries.</summary>
    private static List<string> ParseDescribe(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return [];

            return data.EnumerateArray()
                .Where(r => r.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                .Select(r =>
                {
                    var name = r.GetProperty("name").GetString()!;
                    var type = r.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                        ? t.GetString()! : "?";
                    return $"{name} ({type})";
                })
                .ToList();
        }
        catch { return []; }
    }
}
