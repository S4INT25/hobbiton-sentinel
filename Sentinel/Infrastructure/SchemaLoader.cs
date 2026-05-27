using System.Text;
using System.Text.Json;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Infrastructure;

public class SchemaLoader(ClickHouseClient ch, IFusionCache cache, ILogger<SchemaLoader> logger)
{
    private const string DefaultDatabase = "lipila_blaze";
    private const string SchemaCacheKeyPrefix = "sentinel:schema:full_block:";
    private const string DatabaseListCacheKey = "sentinel:schema:databases";

    // Column name patterns that likely contain enum/status values worth sampling
    private static readonly HashSet<string> EnumColumnHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "state", "type", "category", "kind", "role",
        "payment_method", "channel", "provider", "currency", "country"
    };

    private static readonly HashSet<string> SystemDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "information_schema", "INFORMATION_SCHEMA", "default", "sentinel"
    };

    /// <summary>Returns all non-system databases available in ClickHouse.</summary>
    public async Task<List<string>> GetDatabasesAsync()
    {
        return await cache.GetOrSetAsync(DatabaseListCacheKey,
            _ => FetchDatabaseListAsync(),
            options => options.SetDuration(TimeSpan.FromHours(6))) ?? [];
    }

    public async Task<string> GetSchemaBlockAsync(string? database = null)
    {
        var resolvedDatabase = ResolveDatabaseName(database);
        var cacheKey = $"{SchemaCacheKeyPrefix}{resolvedDatabase}";
        return await cache.GetOrSetAsync(cacheKey,
            _ =>
            {
                logger.LogInformation("Schema cache miss — fetching live schema from ClickHouse for {Database}", resolvedDatabase);
                return FetchSchemaBlockAsync(resolvedDatabase);
            },
            options => options.SetDuration(TimeSpan.FromHours(12)));
    }

    /// <summary>Returns combined schema blocks for ALL databases. Used to inject full context into agent prompts.</summary>
    public async Task<string> GetAllSchemasBlockAsync()
    {
        var databases = await GetDatabasesAsync();
        if (databases.Count == 0)
            return await GetSchemaBlockAsync(DefaultDatabase);

        var sb = new StringBuilder();
        foreach (var db in databases)
        {
            var block = await GetSchemaBlockAsync(db);
            sb.AppendLine(block);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Warms the schema cache for all databases at startup. Call once during app initialization.</summary>
    public async Task WarmAllAsync()
    {
        logger.LogInformation("Warming schema cache for all databases…");
        var databases = await GetDatabasesAsync();
        foreach (var db in databases)
        {
            try
            {
                await GetSchemaBlockAsync(db);
                logger.LogInformation("Schema cached for database: {Database}", db);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to cache schema for database: {Database}", db);
            }
        }
        logger.LogInformation("Schema warm-up complete — {Count} databases cached", databases.Count);
    }

    public async Task InvalidateAsync(string? database = null)
    {
        var resolvedDatabase = ResolveDatabaseName(database);
        await cache.RemoveAsync($"{SchemaCacheKeyPrefix}{resolvedDatabase}");
    }

    public async Task InvalidateAllAsync()
    {
        await cache.RemoveAsync(DatabaseListCacheKey);
        var databases = await FetchDatabaseListAsync();
        foreach (var db in databases)
            await cache.RemoveAsync($"{SchemaCacheKeyPrefix}{db}");
    }

    private async Task<List<string>> FetchDatabaseListAsync()
    {
        try
        {
            var json = await ch.QueryAsync("SHOW DATABASES");
            var all = ParseStringColumn(json, "name");
            return all.Where(d => !SystemDatabases.Contains(d)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch database list from ClickHouse");
            return [DefaultDatabase];
        }
    }

    private async Task<string> FetchSchemaBlockAsync(string database)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Live Database Schema ({database})");

        var tablesJson = await ch.QueryAsync($"SHOW TABLES FROM {database}");
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
            var descJson = await ch.QueryAsync($"DESCRIBE {database}.{table}");
            var columns = ParseDescribeDetailed(descJson);

            if (columns.Count == 0) continue;

            sb.AppendLine($"### {table}");
            sb.AppendLine($"Columns: {string.Join(", ", columns.Select(c => $"{c.Name} ({c.Type})"))}");

            // Sample distinct values for enum-like columns
            var enumCols = columns
                .Where(c => IsEnumLikeColumn(c.Name, c.Type))
                .Select(c => c.Name)
                .Take(5)
                .ToList();

            if (enumCols.Count > 0)
            {
                var sampleValues = await FetchSampleValuesAsync(database, table, enumCols);
                if (sampleValues.Count > 0)
                {
                    sb.AppendLine("Sample values:");
                    foreach (var (col, values) in sampleValues)
                    {
                        sb.AppendLine($"  - {col}: {string.Join(", ", values.Select(v => $"'{v}'"))}");
                    }
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("IMPORTANT: Schema is pre-loaded with actual column values. Use EXACT values shown above for filters (case-sensitive). Do NOT guess status/type values — refer to the sample values listed.");
        sb.AppendLine("Do NOT run SHOW TABLES or DESCRIBE unless you encounter an unexpected table not listed here.");

        return sb.ToString();
    }

    private async Task<Dictionary<string, List<string>>> FetchSampleValuesAsync(
        string database, string table, List<string> columns)
    {
        var result = new Dictionary<string, List<string>>();

        foreach (var col in columns)
        {
            try
            {
                var sql = $"SELECT DISTINCT `{col}` FROM {database}.`{table}` WHERE `{col}` != '' AND `{col}` IS NOT NULL LIMIT 20";
                var json = await ch.QueryAsync(sql);

                if (json.StartsWith("ClickHouse error") || json.StartsWith("Error:") || json.StartsWith("Query failed:"))
                    continue;

                var values = ParseStringColumn(json, col);
                if (values.Count > 0)
                    result[col] = values;
            }
            catch
            {
                // best-effort sampling
            }
        }

        return result;
    }

    private static bool IsEnumLikeColumn(string name, string type)
    {
        // Match columns with enum-like names or LowCardinality/Enum types
        if (type.Contains("Enum", StringComparison.OrdinalIgnoreCase))
            return true;
        if (type.Contains("LowCardinality", StringComparison.OrdinalIgnoreCase))
            return true;

        var lowerName = name.ToLowerInvariant();
        return EnumColumnHints.Any(hint => lowerName.Contains(hint, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>Parses DESCRIBE output into detailed column info.</summary>
    private static List<ColumnInfo> ParseDescribeDetailed(string json)
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
                    return new ColumnInfo(name, type);
                })
                .ToList();
        }
        catch { return []; }
    }

    private static string ResolveDatabaseName(string? database)
    {
        if (string.IsNullOrWhiteSpace(database))
            return DefaultDatabase;

        foreach (var c in database)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return DefaultDatabase;
        }

        return database;
    }

    private record ColumnInfo(string Name, string Type);
}
