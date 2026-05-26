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

    public async Task<AnalyticsResponse> AskAsync(string prompt, string database = "lipila_blaze",
        List<ChatEntry>? history = null)
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
                             """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt)
        };

        // Add conversation history for multi-turn context
        if (history is { Count: > 0 })
        {
            var historyToInclude = history.TakeLast(MaxHistoryExchanges * 2).ToList();
            foreach (var entry in historyToInclude)
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
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 2048,
            Temperature = 0.1f,
        };

        var response = await chatClient.CompleteChatAsync(messages, options);
        var text = string.Concat(response.Value.Content
            .Where(p => p.Kind == ChatMessageContentPartKind.Text)
            .Select(p => p.Text));

        var inputTokens = response.Value.Usage?.InputTokenCount ?? 0;
        var outputTokens = response.Value.Usage?.OutputTokenCount ?? 0;

        logger.LogInformation("[Analytics] tokens: in={In} out={Out}", inputTokens, outputTokens);

        // Parse LLM response
        try
        {
            // Strip markdown fences if present
            text = text.Trim();
            if (text.StartsWith("```")) text = text[text.IndexOf('\n')..];
            if (text.EndsWith("```")) text = text[..text.LastIndexOf("```")];
            text = text.Trim();

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var thinking = root.TryGetProperty("thinking", out var t) ? t.GetString() : null;
            var sql = root.TryGetProperty("sql", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;
            var explanation = root.TryGetProperty("explanation", out var e) ? e.GetString() : null;
            var chartType = root.TryGetProperty("chart", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : "none";

            if (sql == null)
            {
                return new AnalyticsResponse
                {
                    Success = true,
                    Explanation = explanation ?? text,
                    Thinking = thinking,
                    ChartType = chartType ?? "none",
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens
                };
            }

            // Execute the query
            var result = await ch.QueryAsync(sql);

            // Check for ClickHouse error
            if (result.StartsWith("ClickHouse error") || result.StartsWith("Error:") ||
                result.StartsWith("Query failed:"))
            {
                return new AnalyticsResponse
                {
                    Success = false,
                    Sql = sql,
                    Error = result,
                    Thinking = thinking,
                    Explanation = explanation,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens
                };
            }

            // Parse into table format
            var tableData = ParseQueryResult(result);

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
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }
        catch (JsonException)
        {
            // LLM didn't return valid JSON — treat as plain text response
            return new AnalyticsResponse
            {
                Success = true,
                Explanation = text,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Analytics] Failed to process query");
            return new AnalyticsResponse
            {
                Success = false,
                Error = ex.Message,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }
    }

    private static TableData ParseQueryResult(string json)
    {
        var data = new TableData();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // ClickHouse JSON format has "meta" (column definitions) and "data" (rows)
            if (root.TryGetProperty("meta", out var meta))
            {
                data.Columns = meta.EnumerateArray()
                    .Select(m => m.GetProperty("name").GetString()!)
                    .ToList();
            }

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
                        else
                        {
                            rowDict[col] = "";
                        }
                    }

                    data.Rows.Add(rowDict);
                }
            }
        }
        catch
        {
            // If parsing fails, return empty table
        }

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
    public string ChartType { get; set; } = "none"; // bar, line, doughnut, none
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