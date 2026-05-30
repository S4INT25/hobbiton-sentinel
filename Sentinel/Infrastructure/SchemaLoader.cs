using System.Text;
using System.Text.Json;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Infrastructure;

public class SchemaLoader(ClickHouseClient ch, IFusionCache cache, ILogger<SchemaLoader> logger)
{
    private const string DefaultDatabase = "lipila_blaze";
    private const string SchemaCacheKeyPrefix = "sentinel:schema:block:";
    private const string DatabaseListCacheKey = "sentinel:schema:databases";
    private const string ValuesCacheKeyPrefix = "sentinel:schema:values:";

    private static readonly HashSet<string> SystemDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "information_schema", "INFORMATION_SCHEMA", "default", "sentinel", "peerdb"
    };

    // Internal tables replicated by PeerDB — hide from LLM
    private static readonly string[] ExcludedTablePrefixes = ["_peerdb", "mv_"];

    private static readonly Dictionary<string, string> DatabaseDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lipila_blaze"] = "Payment gateway — collections, disbursements, merchant payments, wallets, settlements",
        ["inshuwa"] = "Insurance platform — all classes: motor, life, travel, health, general; policies, claims, premiums",
        ["bnpl"] = "Lipila Later — buy-now-pay-later loans, repayments, credit scoring",
        ["patumba_mtn"] = "Patumba investments (MTN) — savings goals, contributions, withdrawals",
        ["patumba_airtel"] = "Patumba investments (Airtel) — savings goals, contributions, withdrawals",
        ["patumba_zamtel"] = "Patumba investments (Zamtel) — savings goals, contributions, withdrawals",
    };

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
                logger.LogInformation("Schema cache miss — building schema block for {Database}", resolvedDatabase);
                return BuildSchemaBlockAsync(resolvedDatabase);
            },
            options => options.SetDuration(TimeSpan.FromHours(12)));
    }
    

    public async Task<Dictionary<string, List<string>>> GetCategoricalValuesAsync(string database, string table)
    {
        var resolvedDatabase = ResolveDatabaseName(database);
        var cacheKey = $"{ValuesCacheKeyPrefix}{resolvedDatabase}:{table}";
        return await cache.GetOrSetAsync(cacheKey,
            _ => FetchCategoricalValuesAsync(resolvedDatabase, table),
            options => options.SetDuration(TimeSpan.FromHours(6)))
            ?? new Dictionary<string, List<string>>();
    }

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
        {
            await cache.RemoveAsync($"{SchemaCacheKeyPrefix}{db}");
        }
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

    private async Task<string> BuildSchemaBlockAsync(string database)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Database: {database}");
        if (DatabaseDescriptions.TryGetValue(database, out var desc))
            sb.AppendLine($"*{desc}*");
        sb.AppendLine();

        var tablesJson = await ch.QueryAsync(
            $"SELECT name FROM system.tables WHERE database = '{database}' ORDER BY name");
        var allTables = ParseStringColumn(tablesJson, "name");
        var tables = allTables
            .Where(t => !ExcludedTablePrefixes.Any(p => t.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (tables.Count == 0)
        {
            sb.AppendLine("(no tables found)");
            return sb.ToString();
        }

        var columnsJson = await ch.QueryAsync(
            $"SELECT table, name, type FROM system.columns WHERE database = '{database}' " +
            $"AND table NOT LIKE '_peerdb%' AND table NOT LIKE 'mv_%' " +
            $"ORDER BY table, position");

        var tableColumns = ParseTableColumns(columnsJson);

        foreach (var table in tables)
        {
            if (!tableColumns.TryGetValue(table, out var columns) || columns.Count == 0)
                continue;

            var userColumns = columns
                .Where(c => !c.Name.StartsWith("_peerdb", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (userColumns.Count == 0) continue;

            sb.AppendLine($"### {table}");

            foreach (var col in userColumns)
            {
                sb.Append($"  - {col.Name} ({col.Type})");
                sb.AppendLine();
            }

            var categoricalCols = userColumns
                .Where(c => IsCategoricalType(c.Type))
                .Select(c => c.Name)
                .ToList();

            if (categoricalCols.Count > 0)
            {
                var values = await FetchCategoricalValuesAsync(database, table);
                if (values.Count > 0)
                {
                    sb.AppendLine("  **Allowed filter values (use exactly as shown):**");
                    foreach (var (col, vals) in values)
                    {
                        if (vals.Count > 0)
                            sb.AppendLine($"    {col}: {string.Join(" | ", vals.Select(v => $"'{v}'"))}");
                    }
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("RULES FOR FILTER VALUES:");
        sb.AppendLine("- Every LowCardinality column above has its complete set of allowed values listed.");
        sb.AppendLine("- You MUST use these exact values (case-sensitive). Do NOT invent, guess, or transform them.");
        sb.AppendLine("- If a value is not listed above, it does not exist in the data.");
        sb.AppendLine("- Column names and table names are case-sensitive — use them exactly as shown.");

        return sb.ToString();
    }

    private async Task<Dictionary<string, List<string>>> FetchCategoricalValuesAsync(string database, string table)
    {
        var result = new Dictionary<string, List<string>>();

        var columnsJson = await ch.QueryAsync(
            $"SELECT name, type FROM system.columns WHERE database = '{database}' AND table = '{table}' " +
            $"AND (type LIKE '%LowCardinality%' OR type LIKE '%Enum%') " +
            $"AND name NOT LIKE '_peerdb%' ORDER BY position");

        var categoricalCols = ParseColumnList(columnsJson);
        if (categoricalCols.Count == 0) return result;

        var unions = categoricalCols.Select(col =>
            $"SELECT '{EscapeSql(col)}' AS col, groupArray(DISTINCT `{col}`) AS vals " +
            $"FROM {database}.`{table}` WHERE toString(`{col}`) != '' AND `{col}` IS NOT NULL");

        var batchSql = string.Join("\nUNION ALL\n", unions);
        var batchJson = await ch.QueryAsync(batchSql);

        if (IsError(batchJson))
        {
            foreach (var col in categoricalCols)
            {
                try
                {
                    var sql = $"SELECT DISTINCT `{col}` FROM {database}.`{table}` " +
                              $"WHERE `{col}` IS NOT NULL AND toString(`{col}`) != '' LIMIT 30";
                    var json = await ch.QueryAsync(sql);
                    if (!IsError(json))
                    {
                        var values = ParseStringColumn(json, col);
                        if (values.Count > 0) result[col] = values;
                    }
                }
                catch { /* best effort */ }
            }
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(batchJson);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return result;

            foreach (var row in data.EnumerateArray())
            {
                var colName = row.TryGetProperty("col", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString() : null;
                if (string.IsNullOrWhiteSpace(colName)) continue;

                if (row.TryGetProperty("vals", out var vals) && vals.ValueKind == JsonValueKind.Array)
                {
                    var values = vals.EnumerateArray()
                        .Where(v => v.ValueKind == JsonValueKind.String)
                        .Select(v => v.GetString()!)
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct()
                        .OrderBy(v => v)
                        .ToList();

                    if (values.Count > 0) result[colName] = values;
                }
            }
        }
        catch { /* return what we have */ }

        return result;
    }

    private static bool IsCategoricalType(string type) =>
        type.Contains("LowCardinality", StringComparison.OrdinalIgnoreCase) ||
        type.Contains("Enum", StringComparison.OrdinalIgnoreCase);

    private static bool IsError(string result) =>
        result.StartsWith("ClickHouse error") ||
        result.StartsWith("Error:") ||
        result.StartsWith("Query failed:");

    private static string EscapeSql(string value) =>
        value.Replace("'", "\\'");

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

    private static List<string> ParseColumnList(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return [];

            return data.EnumerateArray()
                .Where(r => r.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                .Select(r => r.GetProperty("name").GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch { return []; }
    }

    private static Dictionary<string, List<ColumnInfo>> ParseTableColumns(string json)
    {
        var result = new Dictionary<string, List<ColumnInfo>>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return result;

            foreach (var row in data.EnumerateArray())
            {
                var table = row.TryGetProperty("table", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() : null;
                var name = row.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() : null;
                var type = row.TryGetProperty("type", out var tp) && tp.ValueKind == JsonValueKind.String
                    ? tp.GetString() : null;

                if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
                    continue;

                if (!result.TryGetValue(table, out var list))
                {
                    list = [];
                    result[table] = list;
                }
                list.Add(new ColumnInfo(name, type));
            }
        }
        catch { /* return what we have */ }
        return result;
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