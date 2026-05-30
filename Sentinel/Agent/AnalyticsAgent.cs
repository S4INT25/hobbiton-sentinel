using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAI;
using OpenAI.Chat;
using Sentinel.Admin;
using Sentinel.Infrastructure;

namespace Sentinel.Agent;

public class AnalyticsAgent(
    OpenAIClient ai,
    ClickHouseClient ch,
    SchemaLoader schemaLoader,
    IConfiguration config,
    ILogger<AnalyticsAgent> logger)
{
    private const int MaxHistoryExchanges = 10;
    private const int MaxRetries = 4;

    public async Task<AnalyticsResponse> AskAsync(
        string prompt,
        string database = "lipila_blaze",
        List<ChatEntry>? history = null,
        string mode = "general",
        Func<AnalyticsStreamEvent, Task>? onEvent = null)
    {
        var modelName = config["DigitalOcean:ModelName"]!;
        var schema = await schemaLoader.GetSchemaBlockAsync(database);
        var isFraudMode = string.Equals(mode, "fraud", StringComparison.OrdinalIgnoreCase);

        var systemPrompt = BuildSystemPrompt(database, schema, isFraudMode);
        var messages = new List<ChatMessage> { new SystemChatMessage(systemPrompt) };

        if (history is { Count: > 0 })
        {
            foreach (var entry in history.TakeLast(MaxHistoryExchanges * 2))
            {
                if (entry.Role == "user")
                    messages.Add(new UserChatMessage(entry.Content));
                else
                    messages.Add(new AssistantChatMessage(entry.Response != null
                        ? JsonSerializer.Serialize(entry.Response)
                        : entry.Content));
            }
        }

        messages.Add(new UserChatMessage(prompt));

        var chatClient = ai.GetChatClient(modelName);
        var options = new ChatCompletionOptions { MaxOutputTokenCount = 2048, Temperature = 0.1f };
        int totalInput = 0, totalOutput = 0;
        string? lastError = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 1 && lastError != null)
            {
                await Emit(onEvent, "fixing", $"Correcting SQL (attempt {attempt}/{MaxRetries})…", attempt);
                messages.Add(new UserChatMessage(
                    $"CORRECTION NEEDED:\n\n{lastError}\n\nFix the SQL using only the schema and values provided. Do not guess."));
            }

            await Emit(onEvent, "thinking",
                attempt == 1 ? "Analysing your question…" : $"Rethinking (attempt {attempt})…", attempt);

            ChatCompletion response;
            try
            {
                response = (await chatClient.CompleteChatAsync(messages, options)).Value;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Analytics] LLM call failed on attempt {Attempt}", attempt);
                return new AnalyticsResponse
                {
                    Success = false,
                    Error = $"LLM error: {ex.Message}",
                    InputTokens = totalInput,
                    OutputTokens = totalOutput
                };
            }

            var text = string.Concat(response.Content
                .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                .Select(p => p.Text));

            totalInput += response.Usage?.InputTokenCount ?? 0;
            totalOutput += response.Usage?.OutputTokenCount ?? 0;
            logger.LogInformation("[Analytics] attempt={Attempt} tokens in={In} out={Out}",
                attempt, totalInput, totalOutput);

            text = StripMarkdownFences(text);

            var parsed = ParseLlmResponse(text, isFraudMode);
            if (parsed == null)
            {
                return new AnalyticsResponse
                {
                    Success = true, Explanation = text,
                    InputTokens = totalInput, OutputTokens = totalOutput
                };
            }

            if (!string.IsNullOrEmpty(parsed.Thinking))
                await Emit(onEvent, "thinking", parsed.Thinking, attempt);

            if (parsed.Sql == null)
            {
                return new AnalyticsResponse
                {
                    Success = true,
                    Explanation = parsed.Explanation ?? text,
                    Thinking = parsed.Thinking,
                    Summary = parsed.Summary,
                    RiskLevel = parsed.RiskLevel,
                    Findings = parsed.Findings,
                    RecommendedActions = parsed.RecommendedActions,
                    ChartType = parsed.ChartType ?? "none",
                    InputTokens = totalInput,
                    OutputTokens = totalOutput
                };
            }

            var validation = await ValidateCategoricalFiltersAsync(parsed.Sql, database);
            if (validation != null)
            {
                await Emit(onEvent, "fixing",
                    "Invalid filter values detected — requesting correction with known values…", attempt,
                    parsed.Sql);

                messages.Add(new AssistantChatMessage(text));
                lastError = validation;
                continue;
            }

            await Emit(onEvent, "sql", "Executing SQL…", attempt, parsed.Sql);
            var result = await ch.QueryAsync(parsed.Sql);

            if (IsClickHouseError(result))
            {
                lastError = BuildErrorFeedback(result, parsed.Sql);
                await Emit(onEvent, "error", result, attempt, parsed.Sql);
                messages.Add(new AssistantChatMessage(text));

                if (attempt == MaxRetries)
                {
                    return new AnalyticsResponse
                    {
                        Success = false,
                        Sql = parsed.Sql,
                        Error = $"Failed after {MaxRetries} attempts. Last error: {result}",
                        Thinking = parsed.Thinking,
                        InputTokens = totalInput,
                        OutputTokens = totalOutput
                    };
                }
                continue;
            }

            var tableData = ParseQueryResult(result);

            if (tableData.Rows.Count == 0)
            {
                if (attempt < MaxRetries)
                {
                    await Emit(onEvent, "fixing",
                        $"Query returned 0 rows (attempt {attempt}). Retrying with adjusted filters…",
                        attempt, parsed.Sql);

                    messages.Add(new AssistantChatMessage(text));
                    lastError = BuildZeroRowsFeedback(parsed.Sql, database);
                    continue;
                }

                return new AnalyticsResponse
                {
                    Success = true,
                    Sql = parsed.Sql,
                    Explanation = parsed.Explanation ??
                        "Query returned no results. Try broadening the date range or removing strict filters.",
                    Thinking = parsed.Thinking,
                    ChartType = "none",
                    Columns = tableData.Columns,
                    Rows = tableData.Rows,
                    RowCount = 0,
                    InputTokens = totalInput,
                    OutputTokens = totalOutput
                };
            }

            await Emit(onEvent, "done", $"Got {tableData.Rows.Count} rows", attempt);

            return new AnalyticsResponse
            {
                Success = true,
                Sql = parsed.Sql,
                Explanation = parsed.Explanation,
                Thinking = parsed.Thinking,
                Summary = parsed.Summary,
                RiskLevel = parsed.RiskLevel,
                Findings = parsed.Findings,
                RecommendedActions = parsed.RecommendedActions,
                ChartType = parsed.ChartType ?? "none",
                RawResult = result,
                Columns = tableData.Columns,
                Rows = tableData.Rows,
                RowCount = tableData.Rows.Count,
                InputTokens = totalInput,
                OutputTokens = totalOutput
            };
        }

        return new AnalyticsResponse
        {
            Success = false, Error = "Unexpected agent exit",
            InputTokens = totalInput, OutputTokens = totalOutput
        };
    }

    private static string BuildSystemPrompt(string database, string schema, bool isFraudMode)
    {
        var rules = $$"""
            ## ClickHouse SQL Rules
            - ONLY produce SELECT/WITH queries. Never INSERT/UPDATE/DELETE/DROP.
            - Always qualify tables: `{{database}}.<table>`.
            - Use native ClickHouse functions: ifNull(), countIf(), sumIf(), toStartOfDay(), toStartOfHour(), formatReadableQuantity(), dateDiff('unit', start, end).
            - BANNED functions (do NOT use): COALESCE, ISNULL, DATEDIFF (use dateDiff), IIF, NVL, TOP, DATEADD.
            - Use single quotes for string literals.
            - Add LIMIT 50 unless the user specifies otherwise.
            - For time filtering use: created_at >= now() - INTERVAL 7 DAY (not DATE_SUB or similar).

            ## CRITICAL: Filter Values
            - The schema above lists **every allowed value** for each LowCardinality column.
            - You MUST use these exact values (case-sensitive, lowercase unless shown otherwise).
            - NEVER invent, guess, or capitalize filter values. If 'successful' is listed, do NOT use 'Successful' or 'SUCCESS'.
            - If you are unsure which value to use, pick from the listed values that best matches the user's intent.

            ## Currency
            - All monetary amounts are in Zambian Kwacha (ZMW). Prefix amounts with "K" (e.g. K 1,250.00) in explanations.
            - Never use "$", "USD", or any other currency symbol.
            - In SQL, return raw numeric values — the UI handles display formatting.
            """;

        if (isFraudMode)
        {
            return $$"""
                You are a fraud-detection analytics assistant for ClickHouse. Analyse data and return findings in strict JSON format.

                {{schema}}

                {{rules}}

                ## Response Format (strict JSON, no markdown fences)
                {
                  "thinking": "Brief internal reasoning about your approach",
                  "sql": "SELECT ... or null if no query needed",
                  "summary": "Analyst-facing summary (use K prefix for amounts)",
                  "risk_level": "low|medium|high|critical",
                  "findings": ["Finding 1", "Finding 2"],
                  "recommended_actions": ["Action 1", "Action 2"],
                  "explanation": "Detailed explanation (use K prefix for amounts)",
                  "chart": "bar|line|pie|none"
                }
                """;
        }

        return $$"""
            You are a SQL analytics assistant for ClickHouse. Translate natural-language questions into ClickHouse SQL, execute them, and explain results clearly.

            {{schema}}

            {{rules}}

            ## Response Format (strict JSON, no markdown fences)
            {
              "thinking": "Brief explanation of your approach and which tables/columns you'll use",
              "sql": "SELECT ...",
              "explanation": "Plain English summary of results (use K prefix for amounts)",
              "chart": "bar|line|area|pie|scatter|none"
            }

            Chart guide:
            - "bar": comparisons between categories (top merchants, counts by type)
            - "line": time series trends (daily/hourly over time)
            - "area": cumulative or volume trends over time
            - "pie": proportions with few categories (≤ 8 slices)
            - "scatter": correlation between two numeric columns
            - "none": raw records, too many columns, or non-visual data

            If no query is needed:
            {
              "thinking": "...",
              "sql": null,
              "explanation": "Your text answer",
              "chart": "none"
            }
            """;
    }

    private async Task<string?> ValidateCategoricalFiltersAsync(string sql, string database)
    {
        var tables = ExtractReferencedTables(sql, database);
        if (tables.Count == 0) return null;

        var allInvalid = new List<string>();

        foreach (var table in tables)
        {
            var knownValues = await schemaLoader.GetCategoricalValuesAsync(database, table);
            if (knownValues.Count == 0) continue;

            var filters = ExtractStringLiteralFilters(sql);

            foreach (var (column, requestedValues) in filters)
            {
                // Check if this column is a known categorical column in this table
                var matchingCol = knownValues.Keys
                    .FirstOrDefault(k => k.Equals(column, StringComparison.OrdinalIgnoreCase));

                if (matchingCol == null) continue;

                var allowed = knownValues[matchingCol].ToHashSet(StringComparer.OrdinalIgnoreCase);
                var invalid = requestedValues
                    .Where(v => !allowed.Contains(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (invalid.Count > 0)
                {
                    allInvalid.Add(
                        $"- Column `{matchingCol}` in table `{table}`: " +
                        $"you used [{string.Join(", ", invalid.Select(v => $"'{v}'"))}] " +
                        $"but allowed values are: [{string.Join(", ", knownValues[matchingCol].Select(v => $"'{v}'"))}]");
                }
            }
        }

        if (allInvalid.Count == 0) return null;

        return
            "INVALID FILTER VALUES DETECTED — the following values do not exist in the data:\n\n" +
            string.Join("\n", allInvalid) + "\n\n" +
            "Rewrite your SQL using ONLY the allowed values listed above. " +
            "Do not guess or invent values. Pick the closest match from the allowed set.\n\n" +
            $"Original SQL:\n{sql}";
    }

    private static string BuildErrorFeedback(string error, string sql)
    {
        var feedback = $"ClickHouse execution error:\n{error}\n\nFailed SQL:\n{sql}\n\n";

        if (error.Contains("Unknown table", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            feedback += "The table name is wrong. Use only tables listed in the schema above.";
        }
        else if (error.Contains("Unknown column", StringComparison.OrdinalIgnoreCase) ||
                 error.Contains("Missing columns", StringComparison.OrdinalIgnoreCase) ||
                 error.Contains("Unknown expression identifier", StringComparison.OrdinalIgnoreCase))
        {
            feedback += "A column name is wrong. Use only column names listed in the schema above — they are case-sensitive.";
        }
        else if (error.Contains("COALESCE", StringComparison.OrdinalIgnoreCase) ||
                 error.Contains("ISNULL", StringComparison.OrdinalIgnoreCase))
        {
            feedback += "You used a banned function. Use ifNull() instead of COALESCE/ISNULL.";
        }
        else
        {
            feedback += "Review the error message and fix the SQL syntax for ClickHouse.";
        }

        return feedback;
    }

    private static string BuildZeroRowsFeedback(string sql, string database)
    {
        return
            $"The query returned 0 rows. This usually means:\n" +
            $"1. Filter values don't match actual data (check the allowed values in schema)\n" +
            $"2. Time window is too narrow (try removing date filters or using a wider range)\n" +
            $"3. JOIN conditions are too restrictive\n\n" +
            $"Refer back to the schema's allowed filter values and try again.\n\n" +
            $"Failed SQL:\n{sql}";
    }

    private static readonly Regex FromTableRegex = new(
        @"(?i)\bFROM\s+`?(\w+)`?\.`?(\w+)`?", RegexOptions.Compiled);

    private static readonly Regex JoinTableRegex = new(
        @"(?i)\bJOIN\s+`?(\w+)`?\.`?(\w+)`?", RegexOptions.Compiled);

    private static readonly Regex EqualsFilterRegex = new(
        @"(?i)\b`?(\w+)`?\s*=\s*'([^']+)'", RegexOptions.Compiled);

    private static readonly Regex InFilterRegex = new(
        @"(?i)\b`?(\w+)`?\s+IN\s*\(([^)]+)\)", RegexOptions.Compiled);

    private static readonly Regex StringLiteralInList = new(
        @"'([^']+)'", RegexOptions.Compiled);

    private static List<string> ExtractReferencedTables(string sql, string database)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in FromTableRegex.Matches(sql))
        {
            if (m.Groups[1].Value.Equals(database, StringComparison.OrdinalIgnoreCase))
                tables.Add(m.Groups[2].Value);
        }

        foreach (Match m in JoinTableRegex.Matches(sql))
        {
            if (m.Groups[1].Value.Equals(database, StringComparison.OrdinalIgnoreCase))
                tables.Add(m.Groups[2].Value);
        }

        return tables.ToList();
    }

    private static List<(string Column, List<string> Values)> ExtractStringLiteralFilters(string sql)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in EqualsFilterRegex.Matches(sql))
        {
            var col = m.Groups[1].Value;
            var val = m.Groups[2].Value;
            if (IsReservedKeyword(col)) continue;

            if (!map.TryGetValue(col, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[col] = set;
            }
            set.Add(val);
        }

        foreach (Match m in InFilterRegex.Matches(sql))
        {
            var col = m.Groups[1].Value;
            if (IsReservedKeyword(col)) continue;

            if (!map.TryGetValue(col, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[col] = set;
            }

            foreach (Match lit in StringLiteralInList.Matches(m.Groups[2].Value))
                set.Add(lit.Groups[1].Value);
        }

        return map
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => (kv.Key, kv.Value.ToList()))
            .ToList();
    }

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "LIKE", "BETWEEN",
        "ORDER", "GROUP", "BY", "HAVING", "LIMIT", "OFFSET", "JOIN", "ON",
        "LEFT", "RIGHT", "INNER", "OUTER", "AS", "WITH", "UNION", "ALL",
        "DISTINCT", "CASE", "WHEN", "THEN", "ELSE", "END", "NULL", "IS",
        "FORMAT", "INTERVAL", "DAY", "HOUR", "WEEK", "MONTH", "YEAR"
    };

    private static bool IsReservedKeyword(string word) => SqlKeywords.Contains(word);

    private static string StripMarkdownFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
            text = text[text.IndexOf('\n')..];
        if (text.EndsWith("```"))
            text = text[..text.LastIndexOf("```")];
        return text.Trim();
    }

    private static ParsedLlmResponse? ParseLlmResponse(string text, bool isFraudMode)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var result = new ParsedLlmResponse
            {
                Thinking = root.TryGetProperty("thinking", out var t) ? t.GetString() : null,
                Sql = root.TryGetProperty("sql", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString() : null,
                Explanation = root.TryGetProperty("explanation", out var e) ? e.GetString() : null,
                ChartType = root.TryGetProperty("chart", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString() : "none",
                Summary = root.TryGetProperty("summary", out var sm) && sm.ValueKind == JsonValueKind.String
                    ? sm.GetString() : null,
                RiskLevel = root.TryGetProperty("risk_level", out var rl) && rl.ValueKind == JsonValueKind.String
                    ? rl.GetString() : null,
            };

            if (root.TryGetProperty("findings", out var f) && f.ValueKind == JsonValueKind.Array)
            {
                result.Findings = f.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            }

            if (root.TryGetProperty("recommended_actions", out var ra) && ra.ValueKind == JsonValueKind.Array)
            {
                result.RecommendedActions = ra.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TableData ParseQueryResult(string json)
    {
        var data = new TableData();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("meta", out var meta))
                data.Columns = meta.EnumerateArray()
                    .Select(m => m.GetProperty("name").GetString()!)
                    .ToList();

            if (root.TryGetProperty("data", out var rows))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    var rowDict = new Dictionary<string, string>();
                    foreach (var col in data.Columns)
                    {
                        if (row.TryGetProperty(col, out var val))
                        {
                            rowDict[col] = val.ValueKind switch
                            {
                                JsonValueKind.String => val.GetString()!,
                                JsonValueKind.Number => val.GetRawText(),
                                JsonValueKind.True => "true",
                                JsonValueKind.False => "false",
                                JsonValueKind.Null => "",
                                _ => val.GetRawText()
                            };
                        }
                        else rowDict[col] = "";
                    }
                    data.Rows.Add(rowDict);
                }
            }
        }
        catch { /* return empty table */ }
        return data;
    }

    private static bool IsClickHouseError(string result) =>
        result.StartsWith("ClickHouse error") ||
        result.StartsWith("Error:") ||
        result.StartsWith("Query failed:");

    private static async Task Emit(Func<AnalyticsStreamEvent, Task>? onEvent,
        string type, string message, int attempt, string? sql = null)
    {
        if (onEvent != null)
            await onEvent(new AnalyticsStreamEvent
            {
                Type = type,
                Message = message,
                Sql = sql,
                Attempt = attempt
            });
    }

    private sealed class ParsedLlmResponse
    {
        public string? Thinking { get; set; }
        public string? Sql { get; set; }
        public string? Explanation { get; set; }
        public string? ChartType { get; set; }
        public string? Summary { get; set; }
        public string? RiskLevel { get; set; }
        public List<string> Findings { get; set; } = [];
        public List<string> RecommendedActions { get; set; } = [];
    }
}

public class AnalyticsResponse
{
    public bool Success { get; set; }
    public string? Sql { get; set; }
    public string? Explanation { get; set; }
    public string? Thinking { get; set; }
    public string? Summary { get; set; }
    public string? RiskLevel { get; set; }
    public List<string> Findings { get; set; } = [];
    public List<string> RecommendedActions { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public string? RawResult { get; set; }

    public string? Error { get; set; }
    public string ChartType { get; set; } = "none";
    public List<string> Columns { get; set; } = [];
    public List<Dictionary<string, string>> Rows { get; set; } = [];
    public int RowCount { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

public class TableData
{
    public List<string> Columns { get; set; } = [];
    public List<Dictionary<string, string>> Rows { get; set; } = [];
}