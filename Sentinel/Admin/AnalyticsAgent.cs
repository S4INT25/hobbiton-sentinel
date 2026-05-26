using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using Sentinel.Infrastructure;

namespace Sentinel.Admin;

public class AnalyticsAgent(
    OpenAIClient ai,
    ClickHouseClient ch,
    SchemaLoader schemaLoader,
    IConfiguration config,
    ILogger<AnalyticsAgent> logger)
{
    private const int MaxHistoryExchanges = 10;
    private const int MaxRetries = 3;

    /// <param name="onEvent">Optional callback invoked for each streaming event (thinking, retrying, etc.).</param>
    public async Task<AnalyticsResponse> AskAsync(
        string prompt,
        string database = "lipila_blaze",
        List<ChatEntry>? history = null,
        Func<AnalyticsStreamEvent, Task>? onEvent = null)
    {
        var modelName = config["DigitalOcean:ModelName"]!;
        var schema = await schemaLoader.GetSchemaBlockAsync(database);

        var systemPrompt = $$"""
                             You are a SQL analytics assistant for ClickHouse. The user asks questions in natural language and you translate them into ClickHouse SQL queries, execute them, and explain the results clearly.

                             ## Database: {{database}}
                             {{schema}}

                             ## Rules
                             - ONLY produce SELECT/WITH queries. Never INSERT/UPDATE/DELETE/DROP.
                             - Always qualify tables: `{{database}}.<table>`.
                             - Use native ClickHouse functions: `ifNull()`, `countIf()`, `sumIf()`, `toStartOfHour()`, `formatReadableQuantity()`.
                             - Banned: `COALESCE`, `ISNULL`, `DATEDIFF`, `IIF`, `NVL`, `TOP`.
                             - Add `LIMIT 50` unless the user specifies otherwise.
                             - Use single quotes for string literals.
                             - Name the first column as the label/category and numeric columns as values for clear chart rendering.

                             ## Response Format
                             Respond with a JSON object (no markdown fences):
                             {
                               "thinking": "Brief explanation of your approach",
                               "sql": "SELECT ...",
                               "explanation": "Plain English summary of what the results mean",
                               "chart": "bar" | "line" | "doughnut" | "none"
                             }

                             Chart selection guide:
                             - "bar": comparisons between categories (top merchants, counts by type)
                             - "line": time series data (hourly/daily trends)
                             - "doughnut": proportions/percentages (status breakdown, share)
                             - "none": raw data tables, detailed records, or too many columns

                             If the user asks a follow-up or clarification that doesn't need a query:
                             {
                               "thinking": "...",
                               "sql": null,
                               "explanation": "Your text answer here",
                               "chart": "none"
                             }

                             If you previously produced SQL that caused an error, the user will send back the error.
                             Carefully analyse the error, fix the SQL, and try again.
                             """;

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
            // If this is a retry, tell the LLM about the previous error
            if (attempt > 1 && lastError != null)
            {
                await Emit(onEvent, new AnalyticsStreamEvent
                {
                    Type = "fixing",
                    Message = $"Fixing SQL (attempt {attempt}/{MaxRetries})…",
                    Attempt = attempt
                });
                messages.Add(new UserChatMessage(
                    $"The SQL you generated returned this error from ClickHouse:\n\n{lastError}\n\nPlease analyse the error carefully and produce corrected SQL."));
            }

            await Emit(onEvent, new AnalyticsStreamEvent
            {
                Type = "thinking",
                Message = attempt == 1 ? "Analysing your question…" : $"Rethinking query (attempt {attempt}/{MaxRetries})…",
                Attempt = attempt
            });

            // Call the LLM
            ChatCompletion response;
            try
            {
                response = (await chatClient.CompleteChatAsync(messages, options)).Value;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Analytics] LLM call failed on attempt {Attempt}", attempt);
                return new AnalyticsResponse { Success = false, Error = $"LLM error: {ex.Message}", InputTokens = totalInput, OutputTokens = totalOutput };
            }

            var text = string.Concat(response.Content
                .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                .Select(p => p.Text));

            totalInput += response.Usage?.InputTokenCount ?? 0;
            totalOutput += response.Usage?.OutputTokenCount ?? 0;
            logger.LogInformation("[Analytics] attempt={Attempt} tokens in={In} out={Out}", attempt, totalInput, totalOutput);

            // Strip markdown fences
            text = text.Trim();
            if (text.StartsWith("```")) text = text[text.IndexOf('\n')..];
            if (text.EndsWith("```")) text = text[..text.LastIndexOf("```")];
            text = text.Trim();

            // Parse LLM response
            string? thinking, sql, explanation, chartType;
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                thinking = root.TryGetProperty("thinking", out var t) ? t.GetString() : null;
                sql = root.TryGetProperty("sql", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                explanation = root.TryGetProperty("explanation", out var e) ? e.GetString() : null;
                chartType = root.TryGetProperty("chart", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : "none";
            }
            catch (JsonException)
            {
                // Plain text — no SQL to execute
                return new AnalyticsResponse { Success = true, Explanation = text, InputTokens = totalInput, OutputTokens = totalOutput };
            }

            if (!string.IsNullOrEmpty(thinking))
            {
                await Emit(onEvent, new AnalyticsStreamEvent
                {
                    Type = "thinking",
                    Message = thinking,
                    Attempt = attempt
                });
            }

            // No SQL — conversational answer
            if (sql == null)
            {
                return new AnalyticsResponse
                {
                    Success = true,
                    Explanation = explanation ?? text,
                    Thinking = thinking,
                    ChartType = chartType ?? "none",
                    InputTokens = totalInput,
                    OutputTokens = totalOutput
                };
            }

            await Emit(onEvent, new AnalyticsStreamEvent
            {
                Type = "sql",
                Message = "Executing SQL…",
                Sql = sql,
                Attempt = attempt
            });

            // Execute the query
            var result = await ch.QueryAsync(sql);

            if (result.StartsWith("ClickHouse error") || result.StartsWith("Error:") || result.StartsWith("Query failed:"))
            {
                lastError = result;
                await Emit(onEvent, new AnalyticsStreamEvent
                {
                    Type = "error",
                    Message = result,
                    Sql = sql,
                    Attempt = attempt
                });

                // Add the failed SQL + error to message history so the LLM can self-correct
                messages.Add(new AssistantChatMessage(text));
                // next loop iteration adds the error message

                if (attempt == MaxRetries)
                {
                    logger.LogWarning("[Analytics] All {MaxRetries} attempts failed. Last error: {Error}", MaxRetries, result);
                    return new AnalyticsResponse
                    {
                        Success = false,
                        Sql = sql,
                        Error = $"Failed after {MaxRetries} attempts. Last error: {result}",
                        Thinking = thinking,
                        InputTokens = totalInput,
                        OutputTokens = totalOutput
                    };
                }

                continue; // retry
            }

            // Success
            var tableData = ParseQueryResult(result);
            await Emit(onEvent, new AnalyticsStreamEvent
            {
                Type = "done",
                Message = $"Got {tableData.Rows.Count} rows",
                Attempt = attempt
            });

            return new AnalyticsResponse
            {
                Success = true,
                Sql = sql,
                Explanation = explanation,
                Thinking = thinking,
                ChartType = chartType ?? "none",
                RawResult = result,
                Columns = tableData.Columns,
                Rows = tableData.Rows,
                RowCount = tableData.Rows.Count,
                InputTokens = totalInput,
                OutputTokens = totalOutput
            };
        }

        // Should never reach here
        return new AnalyticsResponse { Success = false, Error = "Unexpected agent exit", InputTokens = totalInput, OutputTokens = totalOutput };
    }

    private static async Task Emit(Func<AnalyticsStreamEvent, Task>? onEvent, AnalyticsStreamEvent evt)
    {
        if (onEvent != null)
            await onEvent(evt);
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
}

public class AnalyticsResponse
{
    public bool Success { get; set; }
    public string? Sql { get; set; }
    public string? Explanation { get; set; }
    public string? Thinking { get; set; }

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
