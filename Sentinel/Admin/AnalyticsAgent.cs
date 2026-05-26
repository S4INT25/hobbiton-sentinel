using OpenAI;
using OpenAI.Chat;
using Sentinel.Infrastructure;

namespace Sentinel.Admin;

public class AnalyticsAgent(
    OpenAIClient ai,
    ClickHouseClient ch,
    IConfiguration config,
    ILogger<AnalyticsAgent> logger)
{
    private const int MaxHistoryExchanges = 10;

    public async Task<string> AskAsync(string question, string database, List<ChatEntry>? history = null)
    {
        var modelName = config["DigitalOcean:ModelName"]!;
        var chatClient = ai.GetChatClient(modelName);

        var systemPrompt = $"""
            You are a ClickHouse analytics assistant with read-only access to the "{database}" database.
            You help users explore data, run queries, and understand their database.

            ## Rules
            - Only generate SELECT, WITH, SHOW, or DESCRIBE queries.
            - Always qualify tables as `{database}.<table>`.
            - Use native ClickHouse SQL syntax.
            - When showing results, format them clearly.
            - If a query fails, explain the error and suggest a fix.
            - Be concise but helpful.

            ## Available Tool
            You can run SQL queries by calling the `run_sql` tool with a `query` parameter.
            Always run queries to answer data questions — don't guess at data.
            """;

        var messages = new List<ChatMessage> { new SystemChatMessage(systemPrompt) };

        // Add conversation history for multi-turn context
        if (history is { Count: > 0 })
        {
            var historyToInclude = history.TakeLast(MaxHistoryExchanges * 2).ToList();
            foreach (var entry in historyToInclude)
            {
                if (entry.Role == "user")
                    messages.Add(new UserChatMessage(entry.Content));
                else
                    messages.Add(new AssistantChatMessage(entry.Content));
            }
        }

        messages.Add(new UserChatMessage(question));

        var tools = new List<ChatTool>
        {
            ChatTool.CreateFunctionTool(
                "run_sql",
                "Execute a read-only SQL query against ClickHouse",
                BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "The SQL query to execute" }
                    },
                    required = new[] { "query" }
                }))
        };

        var maxIterations = 10;
        var iteration = 0;

        while (iteration++ < maxIterations)
        {
            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 4096,
                Temperature = 0.1f
            };
            foreach (var tool in tools) options.Tools.Add(tool);

            var response = await chatClient.CompleteChatAsync(messages, options);
            var completion = response.Value;
            messages.Add(new AssistantChatMessage(completion));

            if (completion.FinishReason == ChatFinishReason.Stop)
            {
                var text = string.Concat((completion.Content ?? [])
                    .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                    .Select(p => p.Text));
                return text;
            }

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                foreach (var toolCall in completion.ToolCalls)
                {
                    if (toolCall.FunctionName == "run_sql")
                    {
                        var args = System.Text.Json.JsonDocument.Parse(toolCall.FunctionArguments);
                        var query = args.RootElement.GetProperty("query").GetString()!;
                        logger.LogInformation("Analytics agent executing query: {Query}", query[..Math.Min(200, query.Length)]);

                        var result = await ch.QueryAsync(query);
                        messages.Add(new ToolChatMessage(toolCall.Id, result));
                    }
                    else
                    {
                        messages.Add(new ToolChatMessage(toolCall.Id, $"Unknown tool: {toolCall.FunctionName}"));
                    }
                }
            }
        }

        return "I reached the maximum number of steps. Please try rephrasing your question.";
    }
}
