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
    private const string ColumnsCacheKeyPrefix = "sentinel:schema:columns:";
    private const string TableDescCacheKeyPrefix = "sentinel:schema:table:";

    private static readonly HashSet<string> SystemDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "information_schema", "INFORMATION_SCHEMA", "default", "sentinel", "peerdb"
    };

    // Internal tables replicated by PeerDB — hide from LLM
    private static readonly string[] ExcludedTablePrefixes = ["_peerdb", "mv_"];

    // Core tables to include in the system prompt per database.
    // Only these are loaded upfront; the agent can use describe_table for others.
    private static readonly Dictionary<string, HashSet<string>> CoreTables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lipila_blaze"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "public_transactions", "public_merchants", "public_merchant_wallets",
            "public_wallets", "public_wallet_holders", "public_disbursements",
            "public_collections", "public_settlements", "public_settlement_items",
            "public_partners", "public_partner_float_accounts",
            "public_users", "public_user_activity_logs"
        },
        ["inshuwa"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "public_PolicyTransactions", "public_Payments", "public_Commissions",
            "public_CommissionSettlements", "public_Insurers",
            "public_InsurerLipilaMerchants"
        },
        ["patumba_app"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "public_wallet_transactions", "public_wallets",
            "public_lipila_wallet_transfers"
        }
    };

    private const int MaxCategoricalValues = 20;
    private const int SkipCategoricalIfOver = 50;

    private static readonly Dictionary<string, string> DatabaseDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lipila_blaze"] = "Payment gateway — collections, disbursements, merchant payments, wallets, settlements",
        ["inshuwa"] =
            "Insurance platform — all classes: motor, life, travel, health, general; policies, claims, premiums",
        ["bnpl"] = "Lipila Later — buy-now-pay-later loans, repayments, credit scoring",
        ["patumba_mtn"] = "Patumba investments (MTN) — savings goals, contributions, withdrawals",
        ["patumba_airtel"] = "Patumba investments (Airtel) — savings goals, contributions, withdrawals",
        ["patumba_zamtel"] = "Patumba investments (Zamtel) — savings goals, contributions, withdrawals",
        ["patumba_app"] = "Patumba App — user wallets, transactions, internal transfers",
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

    /// <summary>
    /// On-demand table description for the describe_table tool.
    /// Returns columns + categorical values for a single table. Cached 12h.
    /// </summary>
    public async Task<string> DescribeTableAsync(string database, string table)
    {
        var resolvedDatabase = ResolveDatabaseName(database);
        var cacheKey = $"{TableDescCacheKeyPrefix}{resolvedDatabase}:{table}";
        return await cache.GetOrSetAsync(cacheKey,
            _ => BuildTableDescriptionAsync(resolvedDatabase, table),
            options => options.SetDuration(TimeSpan.FromHours(12)));
    }

    private async Task<string> BuildTableDescriptionAsync(string database, string table)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### {database}.{table}");

        var allColumns = await GetDatabaseColumnsAsync(database);
        if (!allColumns.TryGetValue(table, out var columns) || columns.Count == 0)
        {
            sb.AppendLine("(table not found or has no columns)");
            return sb.ToString();
        }

        foreach (var col in columns)
            sb.AppendLine($"  - {col.Name} ({col.Type})");

        var values = await FetchCategoricalValuesAsync(database, table);
        if (values.Count > 0)
        {
            sb.AppendLine("  **Categorical values:**");
            foreach (var (col, vals) in values)
            {
                if (vals.Count == 0) continue;
                var displayed = vals.Take(MaxCategoricalValues).ToList();
                var suffix = vals.Count > MaxCategoricalValues ? $" … +{vals.Count - MaxCategoricalValues} more" : "";
                sb.AppendLine($"    {col}: {string.Join(" | ", displayed.Select(v => $"'{v}'"))}{suffix}");
            }
        }

        try
        {
            var countJson = await ch.QueryAsync(
                $"SELECT count() as cnt FROM {database}.`{table}` WHERE _peerdb_is_deleted = 0");
            var count = ParseStringColumn(countJson, "cnt").FirstOrDefault() ?? "?";
            sb.AppendLine($"  **Row count (active):** {count}");
        }
        catch
        {
            /* best effort */
        }

        return sb.ToString();
    }

    private async Task<Dictionary<string, List<ColumnInfo>>> GetDatabaseColumnsAsync(string database)
    {
        var cacheKey = $"{ColumnsCacheKeyPrefix}{database}";
        return await cache.GetOrSetAsync(cacheKey,
                   _ => FetchDatabaseColumnsAsync(database),
                   options => options.SetDuration(TimeSpan.FromHours(12)))
               ?? new Dictionary<string, List<ColumnInfo>>();
    }

    private async Task<Dictionary<string, List<ColumnInfo>>> FetchDatabaseColumnsAsync(string database)
    {
        var columnsJson = await ch.QueryAsync(
            $"SELECT table, name, type FROM system.columns WHERE database = '{database}' " +
            $"AND table NOT LIKE '_peerdb%' AND table NOT LIKE 'mv_%' " +
            $"ORDER BY table, position");
        return ParseTableColumns(columnsJson);
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

        // If we have a core tables list for this database, only include those
        var hasCoreList = CoreTables.TryGetValue(database, out var coreSet);
        var includedTables = hasCoreList
            ? tables.Where(t => coreSet!.Contains(t)).ToList()
            : tables;

        if (includedTables.Count == 0)
        {
            sb.AppendLine("(no tables found)");
            return sb.ToString();
        }

        // List excluded tables so agent knows they exist
        if (hasCoreList)
        {
            var excludedCount = tables.Count - includedTables.Count;
            if (excludedCount > 0)
                sb.AppendLine(
                    $"*({excludedCount} additional tables available — use `describe_table` tool to explore)*");
            sb.AppendLine();
        }

        var tableColumns = await GetDatabaseColumnsAsync(database);

        foreach (var table in includedTables)
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
                        if (vals.Count == 0) continue;
                        // Skip very high-cardinality columns — agent should query DISTINCT
                        if (vals.Count > SkipCategoricalIfOver)
                        {
                            sb.AppendLine($"    {col}: ({vals.Count} values — use `SELECT DISTINCT` to explore)");
                            continue;
                        }

                        // Cap at MaxCategoricalValues
                        var displayed = vals.Take(MaxCategoricalValues).ToList();
                        var suffix = vals.Count > MaxCategoricalValues
                            ? $" … +{vals.Count - MaxCategoricalValues} more"
                            : "";
                        sb.AppendLine($"    {col}: {string.Join(" | ", displayed.Select(v => $"'{v}'"))}{suffix}");
                    }
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("RULES FOR FILTER VALUES:");
        sb.AppendLine("- LowCardinality columns above have allowed values listed (truncated if >20).");
        sb.AppendLine("- You MUST use these exact values (case-sensitive). Do NOT invent, guess, or transform them.");
        sb.AppendLine(
            "- If a value is not listed above, query `SELECT DISTINCT col FROM table LIMIT 50` to discover it.");
        sb.AppendLine("- Column names and table names are case-sensitive — use them exactly as shown.");
        sb.AppendLine("- For tables not listed here, use the `describe_table` tool to get their schema on demand.");

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
                catch
                {
                    /* best effort */
                }
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
                    ? c.GetString()
                    : null;
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
        catch
        {
            /* return what we have */
        }

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
        catch
        {
            return [];
        }
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
        catch
        {
            return [];
        }
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
                    ? t.GetString()
                    : null;
                var name = row.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()
                    : null;
                var type = row.TryGetProperty("type", out var tp) && tp.ValueKind == JsonValueKind.String
                    ? tp.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(type))
                    continue;

                if (!result.TryGetValue(table, out var list))
                {
                    list = [];
                    result[table] = list;
                }

                list.Add(new ColumnInfo(name, type));
            }
        }
        catch
        {
            /* return what we have */
        }

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