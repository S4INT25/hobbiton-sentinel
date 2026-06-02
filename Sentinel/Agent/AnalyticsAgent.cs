using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAI;
using OpenAI.Chat;
using Sentinel.Admin;
using Sentinel.Infrastructure;

namespace Sentinel.Agent;

/// <summary>
/// Iterative tool-calling analytics agent. Operates in interactive (chat) or autonomous (workflow) mode.
/// Emits streaming events for UI consumption and can ask users for clarification.
/// </summary>
public class AnalyticsAgent(
    OpenAIClient ai,
    ClickHouseClient ch,
    SchemaLoader schemaLoader,
    EmailClient emailClient,
    IConfiguration config,
    ILogger<AnalyticsAgent> logger)
{
    private const int MaxHistoryExchanges = 10;
    private const int MaxIterations = 15;

    public async Task<AnalyticsResponse> AskAsync(
        string prompt,
        string database = "lipila_blaze",
        List<ChatEntry>? history = null,
        string mode = "general",
        Func<AnalyticsStreamEvent, Task>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        var modelName = config["DigitalOcean:ModelName"]!;
        var schema = await schemaLoader.GetSchemaBlockAsync(database);
        var isInteractive = !string.Equals(mode, "autonomous", StringComparison.OrdinalIgnoreCase);

        var systemPrompt = BuildSystemPrompt(database, schema, isInteractive);
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

        var allTools = AnalyticsAgentTools.GetToolDefinitions();
        // Only provide emit_chart and ask_user in interactive (chat) mode
        var tools = isInteractive
            ? allTools
            : allTools.Where(t => t.FunctionName != "emit_chart" && t.FunctionName != "ask_user").ToList();
        var chatClient = ai.GetChatClient(modelName);
        int totalInput = 0, totalOutput = 0;
        var iteration = 0;
        var response = new AnalyticsResponse { Success = true };
        var chartResults = new List<QueryResult>();
        string? pendingQuestion = null;
        List<string>? pendingChoices = null;

        await Emit(onEvent, "thinking", "Analysing your question…");

        while (iteration++ < MaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 4096,
                Temperature = 0.1f
            };

            foreach (var tool in tools) options.Tools.Add(tool);

            ChatCompletion completion;
            try
            {
                completion = (await chatClient.CompleteChatAsync(messages, options, cancellationToken)).Value;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Analytics] LLM call failed on iteration {Iteration}", iteration);
                return new AnalyticsResponse
                {
                    Success = false,
                    Error = $"LLM error: {ex.Message}",
                    InputTokens = totalInput,
                    OutputTokens = totalOutput
                };
            }

            messages.Add(new AssistantChatMessage(completion));
            totalInput += completion.Usage?.InputTokenCount ?? 0;
            totalOutput += completion.Usage?.OutputTokenCount ?? 0;

            logger.LogInformation("[Analytics] Iteration {N}: finish={Reason} tools={Tools} tokens in={In} out={Out}",
                iteration, completion.FinishReason, completion.ToolCalls.Count, totalInput, totalOutput);

            // Extract any text content from the response
            var textContent = string.Concat((completion.Content ?? [])
                .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                .Select(p => p.Text));

            if (completion.FinishReason == ChatFinishReason.Stop)
            {
                // Agent is done — extract final explanation
                if (!string.IsNullOrWhiteSpace(textContent))
                    response.Explanation = textContent;

                break;
            }

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                foreach (var toolCall in completion.ToolCalls)
                {
                    var args = toolCall.FunctionArguments.ToString();
                    logger.LogInformation("[Analytics] Tool: {Tool} args={Args}",
                        toolCall.FunctionName, args.Length > 200 ? args[..200] + "…" : args);

                    var result = await ExecuteToolAsync(
                        toolCall, database, isInteractive, onEvent, response, chartResults);

                    // Check if ask_user was invoked — pause iteration
                    if (toolCall.FunctionName == "ask_user" && isInteractive)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(args);
                            var root = doc.RootElement;
                            pendingQuestion = root.TryGetProperty("question", out var q) ? q.GetString() : null;
                            if (root.TryGetProperty("choices", out var c) && c.ValueKind == JsonValueKind.Array)
                                pendingChoices = c.EnumerateArray()
                                    .Where(x => x.ValueKind == JsonValueKind.String)
                                    .Select(x => x.GetString()!)
                                    .ToList();
                        }
                        catch { /* ignore parse errors */ }
                    }

                    messages.Add(new ToolChatMessage(toolCall.Id, result));
                }

                // If ask_user was called in interactive mode, break to let UI handle it
                if (pendingQuestion != null)
                {
                    await Emit(onEvent, "asking", pendingQuestion);
                    break;
                }
            }
        }

        // Populate final response
        response.InputTokens = totalInput;
        response.OutputTokens = totalOutput;
        response.Results = chartResults;
        response.PendingQuestion = pendingQuestion;
        response.PendingChoices = pendingChoices;

        if (chartResults.Count > 0)
        {
            var primary = chartResults[0];
            response.Sql = primary.Sql;
            response.ChartType = primary.ChartType;
            response.Columns = primary.Columns;
            response.Rows = primary.Rows;
            response.RowCount = primary.RowCount;
        }

        await Emit(onEvent, "done", response.Explanation ?? "Analysis complete.");
        return response;
    }

    private async Task<string> ExecuteToolAsync(
        ChatToolCall toolCall,
        string database,
        bool isInteractive,
        Func<AnalyticsStreamEvent, Task>? onEvent,
        AnalyticsResponse response,
        List<QueryResult> chartResults)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolCall.FunctionArguments.ToString());
            var root = doc.RootElement;

            return toolCall.FunctionName switch
            {
                "run_sql" => await HandleRunSql(root, database, onEvent, chartResults),
                "get_schema" => await HandleGetSchema(root),
                "describe_table" => await HandleDescribeTable(root),
                "emit_chart" => HandleEmitChart(root, chartResults, onEvent).Result,
                "send_report" => await HandleSendReport(root, onEvent, response),
                "ask_user" => HandleAskUser(root, isInteractive),
                _ => $"Unknown tool: {toolCall.FunctionName}"
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Analytics] Tool {Tool} failed", toolCall.FunctionName);
            return $"Tool error: {ex.Message}";
        }
    }

    private async Task<string> HandleRunSql(
        JsonElement root, string defaultDb, Func<AnalyticsStreamEvent, Task>? onEvent,
        List<QueryResult> chartResults)
    {
        var queries = new List<string>();
        if (root.TryGetProperty("queries", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var q in arr.EnumerateArray())
            {
                var sql = q.GetString();
                if (!string.IsNullOrWhiteSpace(sql)) queries.Add(sql!);
            }
        }

        if (queries.Count == 0) return "No queries provided.";

        await Emit(onEvent, "executing_sql",
            queries.Count == 1 ? "Running query…" : $"Running {queries.Count} queries…");

        var results = new List<string>();
        for (int i = 0; i < queries.Count; i++)
        {
            var sql = queries[i];

            // Validate categorical filters
            var validation = await ValidateCategoricalFiltersAsync(sql, defaultDb);
            if (validation != null)
            {
                results.Add($"Query {i + 1} validation error:\n{validation}");
                continue;
            }

            var raw = await ch.QueryAsync(sql);

            if (IsClickHouseError(raw))
            {
                var feedback = BuildErrorFeedback(raw, sql);
                results.Add($"Query {i + 1} error:\n{feedback}");
                await Emit(onEvent, "error", $"Query {i + 1} failed", sql);
                continue;
            }

            var tableData = ParseQueryResult(raw);
            await Emit(onEvent, "result",
                $"Query {i + 1}: {tableData.Rows.Count} rows", sql);

            // Truncate result for LLM context (max 50 rows in text)
            var shown = Math.Min(tableData.Rows.Count, 50);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Query {i + 1} ({tableData.Rows.Count} rows):");
            if (tableData.Columns.Count > 0)
            {
                sb.AppendLine(string.Join(" | ", tableData.Columns));
                foreach (var row in tableData.Rows.Take(shown))
                    sb.AppendLine(string.Join(" | ", tableData.Columns.Select(c => row.GetValueOrDefault(c, ""))));
                if (tableData.Rows.Count > shown)
                    sb.AppendLine($"... ({tableData.Rows.Count - shown} more rows)");
            }

            results.Add(sb.ToString());
        }

        return string.Join("\n\n", results);
    }

    private async Task<string> HandleGetSchema(JsonElement root)
    {
        var database = root.TryGetProperty("database", out var d) ? d.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(database)) return "Database name is required.";

        var schema = await schemaLoader.GetSchemaBlockAsync(database);
        return string.IsNullOrWhiteSpace(schema)
            ? $"No schema found for database '{database}'."
            : schema;
    }

    private async Task<string> HandleDescribeTable(JsonElement root)
    {
        var database = root.TryGetProperty("database", out var d) ? d.GetString() ?? "" : "";
        var table = root.TryGetProperty("table", out var t) ? t.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(table))
            return "Both database and table are required.";

        var description = await schemaLoader.DescribeTableAsync(database, table);
        return string.IsNullOrWhiteSpace(description)
            ? $"Table '{database}.{table}' not found."
            : description;
    }

    private async Task<string> HandleEmitChart(
        JsonElement root, List<QueryResult> chartResults, Func<AnalyticsStreamEvent, Task>? onEvent)
    {
        var chartType = root.TryGetProperty("chart_type", out var ct) ? ct.GetString() ?? "none" : "none";
        var title = root.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
        var sql = root.TryGetProperty("sql", out var sq) ? sq.GetString() ?? "" : "";

        var columns = new List<string>();
        if (root.TryGetProperty("columns", out var cols) && cols.ValueKind == JsonValueKind.Array)
            columns = cols.EnumerateArray().Select(c => c.GetString() ?? "").Where(c => c != "").ToList();

        var rows = new List<Dictionary<string, string>>();
        if (root.TryGetProperty("rows", out var rowsArr) && rowsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowsArr.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                var dict = new Dictionary<string, string>();
                foreach (var prop in row.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? "",
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        _ => prop.Value.GetRawText()
                    };
                }
                rows.Add(dict);
            }
        }

        var result = new QueryResult
        {
            Label = title,
            Sql = sql,
            ChartType = chartType,
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count
        };
        chartResults.Add(result);

        await Emit(onEvent, "chart", $"Chart ready: {title} ({chartType}, {rows.Count} points)", sql);
        return $"Chart emitted: {title} ({chartType}, {rows.Count} data points)";
    }

    private async Task<string> HandleSendReport(JsonElement root, Func<AnalyticsStreamEvent, Task>? onEvent, AnalyticsResponse response)
    {
        var template = root.TryGetProperty("template", out var tp) ? tp.GetString() ?? "custom" : "custom";
        var subject = root.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "";
        var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var severity = root.TryGetProperty("severity", out var sv) ? sv.GetString() ?? "watching" : "watching";

        List<string>? recipients = null;
        if (root.TryGetProperty("recipients", out var r) && r.ValueKind == JsonValueKind.Array)
        {
            recipients = r.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            if (recipients.Count == 0) recipients = null;
        }

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
            return "Subject and body are required.";

        await Emit(onEvent, "sending_report", $"Sending {template} report: {subject}");

        var result = await emailClient.SendAsync(subject, body, severity, recipients);
        response.ReportSent = true;
        await Emit(onEvent, "report_sent", result);
        return result;
    }

    private static string HandleAskUser(JsonElement root, bool isInteractive)
    {
        if (!isInteractive)
        {
            var question = root.TryGetProperty("question", out var q) ? q.GetString() ?? "" : "";
            return $"Running in autonomous mode — cannot ask user. Make a reasonable decision and proceed. Original question was: {question}";
        }
        
        return "Question sent to user. Waiting for response.";
    }
    

    private static string BuildSystemPrompt(string database, string schema, bool isInteractive)
    {
        var interactiveBlock = isInteractive
            ? """
              
              ## Interaction
              You are in interactive mode. If the user's request is ambiguous or you need more context,
              use the `ask_user` tool to ask clarifying questions with choices. Examples:
              - "Which time period?" with choices ["Last 24 hours", "Last 7 days", "Last 30 days", "Custom"]
              - "Which database?" with available database options
              Don't over-ask — only when genuinely needed. If you can make a reasonable assumption, do so.
              
              ## Charts
              Use `emit_chart` when visualization would help the user understand patterns or trends.
              Decide the chart type based on data shape (time series → line, categories → bar, proportions → pie).
              """
            : """
              
              ## Mode
              You are in autonomous mode (running from a workflow/scheduled job).
              Do NOT call ask_user or emit_chart — these tools are not available.
              Make reasonable decisions and proceed. Default to last 7 days if no time period specified.
              
              ## Email Reports
              When using `send_report`, produce a PROFESSIONAL executive-quality report suitable for management.
              Structure:
              - **Clear subject line** — concise, actionable (e.g. "Weekly Revenue Summary: K 2.4M (+12% WoW)")
              - **Executive summary** — 2-3 sentences with key takeaways at the top
              - **Key metrics** — use a clean markdown table with well-formatted numbers (K prefix, commas, percentages)
              - **Trends & insights** — specific observations with supporting data points
              - **Recommendations** — clear, actionable next steps (if applicable)
              
              Tone: professional, data-driven, concise. No filler, no hedging, no casual language.
              Format numbers consistently: K 1,250.00 for currency, use percentages for changes.
              Keep it scannable — busy executives should grasp the key points in 10 seconds.
              """;

        return $$"""
                 You are an intelligent analytics agent with full access to ClickHouse databases.
                 You investigate questions by querying data, analysing results, and presenting findings clearly.
                 
                 You have tools: run_sql, get_schema, describe_table, emit_chart, send_report, ask_user.
                 
                 ## How to work
                 1. Think about what data you need to answer the question
                 2. Run SQL queries to get the data (batch related queries together)
                 3. Analyse the results — look for patterns, trends, outliers
                 4. In chat mode: use emit_chart if visualization adds insight
                 5. If asked to send a report, use send_report with well-structured markdown content
                 6. Provide a clear, insightful explanation of your findings
                 
                 ## Decision making
                 - Structure reports however makes most sense for the content — no fixed template
                 - If you find something interesting while investigating, follow up on it
                 - You can run multiple rounds of queries if initial results lead to follow-up questions
                 
                 ## Primary database: `{{database}}`
                 {{schema}}
                 
                 ## ClickHouse SQL Rules
                 - ONLY SELECT/WITH queries. Never INSERT/UPDATE/DELETE/DROP.
                 - Always qualify tables: `{{database}}.<table>`.
                 - Tables have a `public_` prefix (e.g. `public_transactions`, NOT `transactions`).
                 - Use native ClickHouse functions: ifNull(), countIf(), sumIf(), toStartOfDay(), toStartOfHour(), dateDiff('unit', start, end).
                 - BANNED functions: COALESCE, ISNULL, DATEDIFF (use dateDiff), IIF, NVL, TOP, DATEADD.
                 - Use single quotes for string literals. Add LIMIT 50 unless user specifies otherwise.
                 - For time filtering: `created_at >= now() - INTERVAL 7 DAY`.
                 
                 ## Filter Values
                 The schema lists allowed values for LowCardinality columns. Use ONLY these exact values (case-sensitive).
                 Never guess or invent filter values.
                 
                 ## Currency
                 All amounts are Zambian Kwacha (ZMW). Use "K" prefix in explanations (e.g. K 1,250.00). Never use $.
                 {{interactiveBlock}}
                 
                 ## Response style
                 After your tool calls complete, write a clear, insightful final response. Reference specific numbers.
                 Be concise but substantive. Don't repeat the query — focus on what the data means.
                 """;
    }
    

    private async Task<string?> ValidateCategoricalFiltersAsync(string sql, string database)
    {
        var tables = ExtractReferencedTables(sql, database);
        if (tables.Count == 0) return null;

        var filters = ExtractStringLiteralFilters(sql);
        if (filters.Count == 0) return null;

        var allAllowed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            var knownValues = await schemaLoader.GetCategoricalValuesAsync(database, table);
            foreach (var (col, values) in knownValues)
            {
                if (!allAllowed.TryGetValue(col, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    allAllowed[col] = set;
                }
                foreach (var v in values) set.Add(v);
            }
        }

        var allInvalid = new List<string>();
        foreach (var (column, requestedValues) in filters)
        {
            if (!allAllowed.TryGetValue(column, out var allowed)) continue;
            var invalid = requestedValues
                .Where(v => !allowed.Contains(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (invalid.Count > 0)
            {
                allInvalid.Add(
                    $"- Column `{column}`: used [{string.Join(", ", invalid.Select(v => $"'{v}'"))}] " +
                    $"but allowed values are: [{string.Join(", ", allowed.Order().Select(v => $"'{v}'"))}]");
            }
        }

        if (allInvalid.Count == 0) return null;

        return "INVALID FILTER VALUES:\n" + string.Join("\n", allInvalid) +
               "\n\nUse ONLY the allowed values listed above. Pick the closest match.";
    }

    private static string BuildErrorFeedback(string error, string sql)
    {
        var feedback = $"ClickHouse error:\n{error}\n\nSQL:\n{sql}\n\n";
        if (error.Contains("Unknown table", StringComparison.OrdinalIgnoreCase))
            feedback += "Tables are prefixed with `public_`. Use only tables from the schema.";
        else if (error.Contains("Unknown column", StringComparison.OrdinalIgnoreCase) ||
                 error.Contains("Missing columns", StringComparison.OrdinalIgnoreCase))
            feedback += "Column name wrong. Check the schema — names are case-sensitive.";
        else if (error.Contains("COALESCE", StringComparison.OrdinalIgnoreCase))
            feedback += "Banned function. Use ifNull() instead.";
        else
            feedback += "Fix the SQL syntax for ClickHouse.";
        return feedback;
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
            if (m.Groups[1].Value.Equals(database, StringComparison.OrdinalIgnoreCase))
                tables.Add(m.Groups[2].Value);
        foreach (Match m in JoinTableRegex.Matches(sql))
            if (m.Groups[1].Value.Equals(database, StringComparison.OrdinalIgnoreCase))
                tables.Add(m.Groups[2].Value);
        return tables.ToList();
    }

    private static List<(string Column, List<string> Values)> ExtractStringLiteralFilters(string sql)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in EqualsFilterRegex.Matches(sql))
        {
            var col = m.Groups[1].Value;
            if (IsReservedKeyword(col)) continue;
            if (!map.TryGetValue(col, out var set)) { set = []; map[col] = set; }
            set.Add(m.Groups[2].Value);
        }

        foreach (Match m in InFilterRegex.Matches(sql))
        {
            var col = m.Groups[1].Value;
            if (IsReservedKeyword(col)) continue;
            if (!map.TryGetValue(col, out var set)) { set = []; map[col] = set; }
            foreach (Match lit in StringLiteralInList.Matches(m.Groups[2].Value))
                set.Add(lit.Groups[1].Value);
        }

        return map.Where(kv => kv.Value.Count > 0).Select(kv => (kv.Key, kv.Value.ToList())).ToList();
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

    private static bool IsClickHouseError(string result) =>
        result.StartsWith("ClickHouse error") ||
        result.StartsWith("Error:") ||
        result.StartsWith("Query failed:");

    private static TableData ParseQueryResult(string json)
    {
        var data = new TableData();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("meta", out var meta))
                data.Columns = meta.EnumerateArray().Select(m => m.GetProperty("name").GetString()!).ToList();
            if (root.TryGetProperty("data", out var rows))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    var rowDict = new Dictionary<string, string>();
                    foreach (var col in data.Columns)
                    {
                        if (row.TryGetProperty(col, out var val))
                            rowDict[col] = val.ValueKind switch
                            {
                                JsonValueKind.String => val.GetString()!,
                                JsonValueKind.Number => val.GetRawText(),
                                JsonValueKind.True => "true",
                                JsonValueKind.False => "false",
                                JsonValueKind.Null => "",
                                _ => val.GetRawText()
                            };
                        else rowDict[col] = "";
                    }
                    data.Rows.Add(rowDict);
                }
            }
        }
        catch { /* return empty table */ }
        return data;
    }

    private static async Task Emit(Func<AnalyticsStreamEvent, Task>? onEvent, string type, string message, string? sql = null)
    {
        if (onEvent != null)
            await onEvent(new AnalyticsStreamEvent
            {
                Type = type,
                Message = message,
                Sql = sql,
                Attempt = 1
            });
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
    public List<QueryResult> Results { get; set; } = [];

    /// <summary>Non-null when the agent is waiting for user input (interactive mode).</summary>
    public string? PendingQuestion { get; set; }
    /// <summary>Choices offered by the agent (if any).</summary>
    public List<string>? PendingChoices { get; set; }
    /// <summary>True if the agent already sent an email via the send_report tool.</summary>
    public bool ReportSent { get; set; }
}

public class QueryResult
{
    public string? Label { get; set; }
    public string? Sql { get; set; }
    public string ChartType { get; set; } = "none";
    public List<string> Columns { get; set; } = [];
    public List<Dictionary<string, string>> Rows { get; set; } = [];
    public int RowCount { get; set; }
}

public class TableData
{
    public List<string> Columns { get; set; } = [];
    public List<Dictionary<string, string>> Rows { get; set; } = [];
}
