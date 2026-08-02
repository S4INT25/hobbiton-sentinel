using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OpenAI.Chat;
using Sentinel.Admin;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;
using Sentinel.Infrastructure;

namespace Sentinel.Agent;

/// <summary>
/// Shared iterative tool-calling analytics engine. Behaviour (tool set, prompt, report policy,
/// token budget) is driven by an <see cref="AgentProfile"/>. Callers should use the
/// <c>ChatAnalyticsAgent</c> or <c>WorkflowAnalyticsAgent</c> facades rather than this directly.
/// </summary>
public class AnalyticsAgentCore(
    ClickHouseClient ch,
    SchemaLoader schemaLoader,
    EmailClient emailClient,
    IAgentMemoryStore memoryStore,
    ModelResolver modelResolver,
    ILogger<AnalyticsAgentCore> logger)
{
    private const int MaxHistoryExchanges = 10;

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

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "LIKE", "BETWEEN",
        "ORDER", "GROUP", "BY", "HAVING", "LIMIT", "OFFSET", "JOIN", "ON",
        "LEFT", "RIGHT", "INNER", "OUTER", "AS", "WITH", "UNION", "ALL",
        "DISTINCT", "CASE", "WHEN", "THEN", "ELSE", "END", "NULL", "IS",
        "FORMAT", "INTERVAL", "DAY", "HOUR", "WEEK", "MONTH", "YEAR"
    };

    public async Task<AnalyticsResponse> AskAsync(
        string prompt,
        string database,
        AgentProfile profile,
        List<ChatEntry>? history = null,
        Func<AnalyticsStreamEvent, Task>? onEvent = null,
        Func<AgentToolCall, Task>? onToolCall = null,
        IEnumerable<AgentMemory>? memories = null,
        IReadOnlyList<string>? additionalDatabases = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = await modelResolver.ResolveAsync(profile.Model);
        var isInteractive = profile.Interactive;

        // Cross-database workflows need to know what is in the other databases, but not in full:
        // the system prompt is re-sent on every iteration, so five complete schema blocks are paid
        // for up to MaxIterations times. Secondary databases get a table index only — the agent
        // pulls the columns it actually needs with describe_table, once.
        var extras = (additionalDatabases ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d) && !d.Equals(database, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var schema = await schemaLoader.GetSchemaBlockAsync(database);
        foreach (var extra in extras)
            schema += "\n\n" + await schemaLoader.GetTableIndexAsync(extra);

        var allowInteractiveReportSending = !isInteractive || HasExplicitEmailIntent(prompt);
        var systemPrompt = BuildSystemPrompt(database, schema, isInteractive, allowInteractiveReportSending,
            memories, extras);
        var messages = new List<ChatMessage> { new SystemChatMessage(systemPrompt) };

        if (history is { Count: > 0 })
        {
            var recent = history.TakeLast(MaxHistoryExchanges * 2).ToList();
            for (var i = 0; i < recent.Count; i++)
            {
                var entry = recent[i];
                if (entry.Role == "user")
                {
                    messages.Add(new UserChatMessage(entry.Content));
                    continue;
                }

                // Last 2 exchanges stay full so the LLM can reference recent results;
                // older ones are compacted to strip data rows and keep only context.
                var keepFull = i >= recent.Count - 4;
                var content = entry.Response != null
                    ? (keepFull ? JsonSerializer.Serialize(entry.Response) : CompactResponse(entry.Response))
                    : entry.Content;
                messages.Add(new AssistantChatMessage(content));
            }
        }

        messages.Add(new UserChatMessage(prompt));

        var allTools = AnalyticsAgentTools.GetToolDefinitions();
        // Only provide emit_chart and ask_user in interactive (chat) mode
        var tools = isInteractive
            ? allTools
            : allTools.Where(t =>
                    t.FunctionName != "emit_chart" && t.FunctionName != "ask_user" && t.FunctionName != "export_csv")
                .ToList();
        var chatClient = resolved.Client.GetChatClient(resolved.ModelId);
        int totalInput = 0, totalOutput = 0;
        var iteration = 0;
        var response = new AnalyticsResponse { Success = true };
        var chartResults = new List<QueryResult>();
        string? pendingQuestion = null;
        List<string>? pendingChoices = null;
        var sendReportNudgeCount = 0;
        const int maxNudges = 3;
        var forceSendReportNextTurn = false;
        var forcedChoiceSupported = true;
        var allExplanationParts = new List<string>();

        // Helper to log a conversation message to the run audit trail
        async Task LogMessage(string role, string content, int iter)
        {
            if (onToolCall == null || string.IsNullOrWhiteSpace(content)) return;
            try
            {
                var truncated = content.Length > 10_000 ? content[..10_000] : content;
                await onToolCall(new AgentToolCall(
                    Iteration: iter,
                    ToolName: role,
                    Args: "",
                    Result: truncated,
                    DurationMs: 0,
                    StartedAt: DateTime.UtcNow,
                    LogType: "message"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Analytics] Failed to log {Role} message", role);
            }
        }

        await Emit(onEvent, "thinking", "Analysing your question…");

        // Log the initial user prompt
        await LogMessage("user", prompt, 0);

        while (iteration++ < profile.MaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = profile.MaxOutputTokens,
                Temperature = 0.1f
            };
            ReasoningEffort.Apply(options, profile.ReasoningEffort);

            foreach (var tool in tools) options.Tools.Add(tool);

            // Last resort for a workflow that has not delivered a report. Two ways to get here:
            // it ignored every plain-English nudge, or it spent the whole iteration budget on
            // tool calls and never stopped to write anything — the nudges only fire on a Stop, so
            // that second path previously ran out silently with nothing to send. Reserving the
            // final iteration and constraining the model to send_report covers both; a forced
            // tool choice cannot be talked out of.
            var outOfRoad = !isInteractive && !response.ReportSent && iteration >= profile.MaxIterations;
            var forcedThisTurn = forcedChoiceSupported && (forceSendReportNextTurn || outOfRoad);
            if (forcedThisTurn)
            {
                options.ToolChoice = ChatToolChoice.CreateFunctionChoice("send_report");
                forceSendReportNextTurn = false;

                if (outOfRoad)
                    messages.Add(new UserChatMessage(
                        "This is your final turn — the iteration budget is spent. Call `send_report` " +
                        "now with whatever you have established so far. If the picture is incomplete, " +
                        "say so plainly in the body rather than omitting the report."));

                logger.LogWarning(
                    "[Analytics] Forcing send_report tool choice on iteration {N} (budget exhausted: {Exhausted})",
                    iteration, outOfRoad);
            }

            // Streaming accumulation
            var streamTextSb = new StringBuilder();
            var tcBuilders = new Dictionary<int, ToolCallBuilder>();
            ChatFinishReason? finishReason = null;
            int iterInput = 0, iterOutput = 0, iterCached = 0;

            try
            {
                await foreach (var update in
                               chatClient.CompleteChatStreamingAsync(messages, options, cancellationToken))
                {
                    foreach (var part in update.ContentUpdate)
                    {
                        if (part.Kind == ChatMessageContentPartKind.Text && !string.IsNullOrEmpty(part.Text))
                        {
                            streamTextSb.Append(part.Text);
                            await Emit(onEvent, "token", streamTextSb.ToString());
                        }
                    }

                    foreach (var tc in update.ToolCallUpdates)
                    {
                        if (!tcBuilders.TryGetValue(tc.Index, out var b))
                        {
                            b = new ToolCallBuilder();
                            tcBuilders[tc.Index] = b;
                        }

                        if (!string.IsNullOrEmpty(tc.ToolCallId)) b.Id = tc.ToolCallId;
                        if (!string.IsNullOrEmpty(tc.FunctionName)) b.Name = tc.FunctionName;
                        if (tc.FunctionArgumentsUpdate != null)
                            b.ArgBytes.AddRange(tc.FunctionArgumentsUpdate.ToArray());
                    }

                    if (update.FinishReason.HasValue) finishReason = update.FinishReason;
                    if (update.Usage != null)
                    {
                        iterInput = update.Usage.InputTokenCount;
                        iterOutput = update.Usage.OutputTokenCount;
                        iterCached = update.Usage.InputTokenDetails?.CachedTokenCount ?? 0;
                    }
                }
            }
            catch (Exception ex)
            {
                // Not every OpenAI-compatible provider accepts a forced tool choice. Losing the
                // whole run because of an optional constraint would be a worse failure than the
                // one it exists to prevent, so drop it and retry this iteration unconstrained.
                if (forcedThisTurn)
                {
                    logger.LogWarning(ex,
                        "[Analytics] Provider rejected a forced send_report tool choice — " +
                        "retrying without it and not forcing again this run");
                    forcedChoiceSupported = false;
                    iteration--; // retry the same step rather than spending budget on the failure
                    continue;
                }

                logger.LogError(ex, "[Analytics] LLM call failed on iteration {Iteration}", iteration);
                return new AnalyticsResponse
                {
                    Success = false,
                    Error = $"LLM error: {ex.Message}",
                    InputTokens = totalInput,
                    OutputTokens = totalOutput
                };
            }

            totalInput += iterInput;
            totalOutput += iterOutput;

            var textContent = streamTextSb.ToString();
            var streamedToolCalls = tcBuilders.OrderBy(x => x.Key)
                .Select(x => ChatToolCall.CreateFunctionToolCall(x.Value.Id, x.Value.Name,
                    BinaryData.FromBytes(x.Value.ArgBytes.ToArray())))
                .ToList();

            // Reconstruct assistant message for conversation history
            if (streamedToolCalls.Count > 0)
                messages.Add(new AssistantChatMessage(streamedToolCalls));
            else
                messages.Add(new AssistantChatMessage(textContent));

            // Per-iteration, not cumulative: the whole message list is resent every turn, so this
            // is what shows the prompt actually growing and whether the provider's prefix cache is
            // absorbing the fixed part. cached=0 on every iteration means it is not.
            logger.LogInformation(
                "[Analytics] Iteration {N}: finish={Reason} tools={Tools} " +
                "in={In} (cached {Cached}) out={Out} | run total in={TotalIn} out={TotalOut}",
                iteration, finishReason, streamedToolCalls.Count,
                iterInput, iterCached, iterOutput, totalInput, totalOutput);

            // Log assistant response to conversation history
            if (!string.IsNullOrWhiteSpace(textContent))
                await LogMessage("assistant", textContent, iteration);

            if (finishReason == ChatFinishReason.Stop)
            {
                // Accumulate explanation text across iterations. The agent may produce its
                // real analysis on one iteration, then a short "sure, sending now" on the next.
                // We keep ALL substantive text so the fallback email path always has content.
                if (!string.IsNullOrWhiteSpace(textContent))
                    allExplanationParts.Add(textContent.Trim());

                // Always surface the longest/best explanation we've seen.
                var bestExplanation = allExplanationParts.OrderByDescending(p => p.Length).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(bestExplanation))
                    response.Explanation = bestExplanation;

                // In autonomous (workflow) mode the agent MUST call send_report before stopping.
                // Nudge it back into the loop, up to maxNudges times with escalating urgency.
                if (!isInteractive && !response.ReportSent && sendReportNudgeCount < maxNudges &&
                    iteration < profile.MaxIterations)
                {
                    sendReportNudgeCount++;
                    logger.LogWarning(
                        "[Analytics] Autonomous agent stopped without calling send_report — nudge {Nudge}/{Max} (iteration {N})",
                        sendReportNudgeCount, maxNudges, iteration);

                    var nudgeMessage = sendReportNudgeCount switch
                    {
                        1 => "You finished without calling `send_report`. This is a scheduled workflow — " +
                             "the ONLY way to deliver results is via `send_report`. " +
                             "Call `send_report` now with the analysis you just produced. " +
                             "Use a valid template (executive, operational, incident, activity or custom), a clear " +
                             "subject line, and the full report body.",
                        2 => "You STILL did not call the `send_report` tool. Do NOT reply with text. " +
                             "You MUST make a tool call to `send_report` right now. " +
                             "Arguments: template=\"operational\", subject=\"<your subject>\", body=\"<your analysis>\", severity=\"watching\".",
                        _ => "FINAL ATTEMPT: Call the send_report tool immediately. " +
                             "Do not output any text — ONLY a tool call. " +
                             "send_report(template=\"operational\", subject=\"Scheduled Report\", body=\"<full analysis>\", severity=\"watching\")"
                    };

                    // On the last nudge stop relying on persuasion and constrain the next turn to
                    // the send_report tool.
                    if (sendReportNudgeCount >= maxNudges) forceSendReportNextTurn = true;

                    messages.Add(new UserChatMessage(nudgeMessage));
                    await LogMessage("system_nudge", nudgeMessage, iteration);
                    await Emit(onEvent, "fixing",
                        $"Agent forgot to send report — nudge attempt {sendReportNudgeCount}/{maxNudges}…");
                    continue;
                }

                break;
            }

            // The model ran out of output tokens mid-answer. Neither the Stop nor the ToolCalls
            // branch matches, so before this the loop simply went round again with the same
            // messages, truncated identically, and burned the whole iteration budget without ever
            // producing text or a tool call — surfacing as "the agent returned no substantive
            // content". Tell it what happened so the retry can actually differ.
            if (finishReason == ChatFinishReason.Length)
            {
                logger.LogWarning(
                    "[Analytics] Output truncated at the token limit on iteration {N} " +
                    "(tool calls so far: {Tools}) — asking the agent to shorten",
                    iteration, streamedToolCalls.Count);

                if (!string.IsNullOrWhiteSpace(textContent))
                    allExplanationParts.Add(textContent.Trim());

                if (streamedToolCalls.Count == 0)
                {
                    messages.Add(new UserChatMessage(
                        "Your previous response was cut off at the output token limit. " +
                        "Be significantly more concise: shorten the report body, drop optional " +
                        "sections, and keep tables to the rows that matter. " +
                        (isInteractive
                            ? "Answer briefly."
                            : "Then deliver it with a single `send_report` tool call.")));
                    await Emit(onEvent, "fixing", "Response hit the token limit — retrying shorter…");
                    continue;
                }
                // Truncated *during* tool calls: fall through and run whatever parsed, so a
                // partially-emitted batch still makes progress rather than being discarded.
            }

            if (finishReason is ChatFinishReason.ToolCalls or ChatFinishReason.Length)
            {
                foreach (var toolCall in streamedToolCalls)
                {
                    var args = toolCall.FunctionArguments.ToString();
                    logger.LogInformation("[Analytics] Tool: {Tool} args={Args}",
                        toolCall.FunctionName, args.Length > 200 ? args[..200] + "…" : args);

                    await Emit(onEvent, "tool_call", toolCall.FunctionName);

                    var toolStart = Stopwatch.GetTimestamp();
                    var result = await ExecuteToolAsync(
                        toolCall, database, isInteractive, allowInteractiveReportSending, onEvent, response,
                        chartResults);
                    var durationMs = (int)Stopwatch.GetElapsedTime(toolStart).TotalMilliseconds;

                    // Persist the tool call for the run audit trail (workflow runs only).
                    if (onToolCall != null)
                    {
                        try
                        {
                            await onToolCall(new AgentToolCall(
                                Iteration: iteration,
                                ToolName: toolCall.FunctionName,
                                Args: args,
                                Result: result,
                                DurationMs: durationMs,
                                StartedAt: DateTime.UtcNow.AddMilliseconds(-durationMs)));
                        }
                        catch (Exception logEx)
                        {
                            logger.LogWarning(logEx, "[Analytics] Failed to log tool call {Tool}",
                                toolCall.FunctionName);
                        }
                    }

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
                        catch
                        {
                            /* ignore parse errors */
                        }
                    }

                    messages.Add(new ToolChatMessage(toolCall.Id, result));
                }

                // If ask_user was called in interactive mode, break to let UI handle it.
                // Keep any text the model wrote alongside the question — this path never reaches
                // the ChatFinishReason.Stop branch, so without this the turn is persisted with an
                // empty Explanation and the UI renders a blank assistant bubble.
                if (pendingQuestion != null)
                {
                    if (!string.IsNullOrWhiteSpace(textContent))
                        allExplanationParts.Add(textContent.Trim());
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

        // Catch-all for every loop exit that isn't ChatFinishReason.Stop (ask_user, iteration
        // budget exhausted). A turn must never be persisted with nothing to display.
        if (string.IsNullOrWhiteSpace(response.Explanation))
            response.Explanation = allExplanationParts.OrderByDescending(p => p.Length).FirstOrDefault();

        if (chartResults.Count > 0)
        {
            var primary = chartResults[0];
            response.Sql = primary.Sql;
            response.ChartType = primary.ChartType;
            response.Columns = primary.Columns;
            response.Rows = primary.Rows;
            response.RowCount = primary.RowCount;
        }

        // ─── Nuclear fallback: the agent exhausted all iterations or nudges without ever
        // calling send_report. If we have accumulated explanation text, send the report
        // programmatically from the engine rather than failing the run. This is the last
        // line of defence — it guarantees email workflows always deliver something.
        if (!isInteractive && !response.ReportSent)
        {
            var bestExplanation = allExplanationParts
                .OrderByDescending(p => p.Length)
                .FirstOrDefault(p => p.Length >= 40);

            if (!string.IsNullOrWhiteSpace(bestExplanation))
            {
                logger.LogWarning(
                    "[Analytics] Agent never called send_report after {Iterations} iterations and {Nudges} nudges — force-sending from engine",
                    iteration - 1, sendReportNudgeCount);

                try
                {
                    // Build a reasonable subject from the first line of the explanation
                    var firstLine = bestExplanation.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()?.Trim() ?? "Scheduled Report";
                    if (firstLine.Length > 80) firstLine = firstLine[..77] + "…";
                    var subject = firstLine.StartsWith('#') ? firstLine.TrimStart('#', ' ') : firstLine;

                    await emailClient.SendAsync(subject, bestExplanation, "watching", wide: true,
                        senderName: "Analytics · Hobbiton", subjectPrefix: "[ANALYTICS]");
                    response.ReportSent = true;
                    response.EmailSubject = subject;
                    response.EmailBody = bestExplanation;

                    await LogMessage("system_fallback",
                        $"Engine force-sent report because agent did not call send_report. Subject: {subject}",
                        iteration - 1);
                    await Emit(onEvent, "report_sent", $"Fallback: sent report — {subject}");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Analytics] Failed to force-send fallback report");
                }
            }
        }

        await Emit(onEvent, "done", response.Explanation ?? "Analysis complete.");
        return response;
    }

    private async Task<string> ExecuteToolAsync(
        ChatToolCall toolCall,
        string database,
        bool isInteractive,
        bool allowInteractiveReportSending,
        Func<AnalyticsStreamEvent, Task>? onEvent,
        AnalyticsResponse response,
        List<QueryResult> chartResults)
    {
        var rawArgs = toolCall.FunctionArguments?.ToString() ?? "{}";

        // The model does not always emit valid JSON (stray backslashes, raw newlines, trailing
        // commas). Parse tolerantly; if it's truly unrecoverable, tell the model how to fix it
        // and let the agent loop retry rather than throwing the run away.
        JsonDocument doc;
        try
        {
            doc = ParseToolArgs(rawArgs);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("[Analytics] Tool {Tool} sent invalid JSON args ({Length} bytes): {Snippet}",
                toolCall.FunctionName, rawArgs.Length,
                rawArgs.Length > 600 ? rawArgs[..600] + "…" : rawArgs);
            return $"Your `{toolCall.FunctionName}` arguments were not valid JSON ({ex.Message}). " +
                   "Re-issue the tool call with strictly valid JSON: escape every backslash as \\\\, " +
                   "escape newlines inside string values as \\n, and do not wrap the JSON in markdown fences.";
        }

        try
        {
            using (doc)
            {
                var root = doc.RootElement;

                return toolCall.FunctionName switch
                {
                    "run_sql" => await HandleRunSql(root, database, onEvent, chartResults),
                    "get_schema" => await HandleGetSchema(root),
                    "describe_table" => await HandleDescribeTable(root),
                    "emit_chart" => HandleEmitChart(root, chartResults, onEvent).Result,
                    "export_csv" => await HandleExportCsv(root, database, onEvent, chartResults),
                    "send_report" => await HandleSendReport(root, isInteractive, allowInteractiveReportSending, onEvent,
                        response),
                    "save_memory" => await HandleSaveMemory(root, database, onEvent),
                    "ask_user" => HandleAskUser(root, isInteractive),
                    "get_current_time" => HandleGetCurrentTime(),
                    _ => $"Unknown tool: {toolCall.FunctionName}"
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Analytics] Tool {Tool} failed", toolCall.FunctionName);
            return $"Tool error: {ex.Message}";
        }
    }

    /// <summary>
    /// Parse tool-call arguments tolerantly. Tries strict JSON first (allowing trailing commas
    /// and comments), then a repaired version that fixes the most common LLM mistakes — lone
    /// backslashes and raw control characters inside string values.
    /// </summary>
    private static JsonDocument ParseToolArgs(string raw)
    {
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        if (string.IsNullOrWhiteSpace(raw))
            return JsonDocument.Parse("{}", options);

        try
        {
            return JsonDocument.Parse(raw, options);
        }
        catch (JsonException)
        {
            // One repair attempt; if it still fails the exception propagates to the caller.
            return JsonDocument.Parse(RepairLooseJson(raw), options);
        }
    }

    /// <summary>
    /// Escapes lone backslashes and raw control characters that appear inside JSON string
    /// values — the most frequent reason model-generated tool arguments fail to parse
    /// (e.g. SQL <c>LIKE '%\_%'</c>, regexes, or multi-line report bodies).
    /// </summary>
    private static string RepairLooseJson(string raw)
    {
        var sb = new StringBuilder(raw.Length + 16);
        var inString = false;

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];

            if (!inString)
            {
                if (c == '"') inString = true;
                sb.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = false;
                    sb.Append(c);
                    break;
                case '\\':
                    var next = i + 1 < raw.Length ? raw[i + 1] : '\0';
                    if (next is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u')
                    {
                        sb.Append(c).Append(next); // already a valid escape
                        i++;
                    }
                    else
                    {
                        sb.Append('\\').Append('\\'); // lone backslash → escape it
                    }

                    break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
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

        // Every tool result stays in the message list for the rest of the run, so one forgotten
        // LIMIT is paid for on every subsequent iteration. Divide the budget across the batch so a
        // ten-query call cannot cost ten times a one-query call.
        var maxResultChars = Math.Max(1000, ch.MaxResultLength / queries.Count);

        await Emit(onEvent, "executing_sql",
            queries.Count == 1 ? "Running query…" : $"Running {queries.Count} queries…");

        var results = new List<string>();
        for (int i = 0; i < queries.Count; i++)
        {
            var sql = queries[i];

            // Validate soft-delete filters before categorical ones — a query that silently counts
            // deleted rows returns plausible-looking numbers that are simply wrong.
            var softDeleteValidation = await ValidateSoftDeleteFilterAsync(sql, defaultDb);
            if (softDeleteValidation != null)
            {
                results.Add($"Query {i + 1} validation error:\n{softDeleteValidation}");
                continue;
            }

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
                var feedback = await BuildErrorFeedbackAsync(raw, sql, defaultDb);
                results.Add($"Query {i + 1} error:\n{feedback}");
                await Emit(onEvent, "error", $"Query {i + 1} failed", sql);
                continue;
            }

            var tableData = ParseQueryResult(raw);
            await Emit(onEvent, "result",
                $"Query {i + 1}: {tableData.Rows.Count} rows", sql);

            results.Add(FormatQueryResult(i + 1, tableData, maxResultChars));
        }

        return string.Join("\n\n", results);
    }

    /// <summary>
    /// Renders a result as a pipe-delimited table, dropping trailing rows once the character
    /// budget is spent. Truncation is stated in the output — a silently shortened result set
    /// would have the agent report a total it can no longer see.
    /// </summary>
    internal static string FormatQueryResult(int index, TableData data, int maxChars)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Query {index} ({data.Rows.Count} rows):");
        if (data.Columns.Count == 0) return sb.ToString();

        sb.AppendLine(string.Join(" | ", data.Columns));

        var shown = 0;
        foreach (var row in data.Rows)
        {
            var line = string.Join(" | ", data.Columns.Select(c => row.GetValueOrDefault(c, "")));
            if (shown > 0 && sb.Length + line.Length > maxChars) break;
            sb.AppendLine(line);
            shown++;
        }

        if (shown < data.Rows.Count)
            sb.AppendLine($"… showing {shown} of {data.Rows.Count} rows — result truncated at the size limit. " +
                          "Re-run with a tighter LIMIT, an aggregate, or a narrower column list if you need the rest.");

        return sb.ToString();
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

    private async Task<string> HandleExportCsv(
        JsonElement root, string defaultDb, Func<AnalyticsStreamEvent, Task>? onEvent,
        List<QueryResult> chartResults)
    {
        var sql = root.TryGetProperty("sql", out var sq) ? sq.GetString() ?? "" : "";
        var title = root.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(sql)) return "sql is required.";
        if (string.IsNullOrWhiteSpace(title)) title = "export";

        await Emit(onEvent, "executing_sql", $"Building CSV: {title}…");

        var validation = await ValidateCategoricalFiltersAsync(sql, defaultDb);
        if (validation != null) return $"Validation error:\n{validation}";

        var raw = await ch.QueryAsync(sql);
        if (IsClickHouseError(raw))
        {
            var feedback = await BuildErrorFeedbackAsync(raw, sql, defaultDb);
            await Emit(onEvent, "error", "CSV query failed", sql);
            return $"Query error:\n{feedback}";
        }

        var tableData = ParseQueryResult(raw);
        chartResults.Add(new QueryResult
        {
            Label = title,
            Sql = sql,
            ChartType = "csv",
            Columns = tableData.Columns,
            Rows = tableData.Rows,
            RowCount = tableData.Rows.Count
        });

        await Emit(onEvent, "chart", $"CSV ready: {title} ({tableData.Rows.Count} rows)", sql);
        return
            $"CSV ready: {title} ({tableData.Rows.Count} rows). Tell the user it's ready to download — do not restate the rows in your reply.";
    }

    private async Task<string> HandleSendReport(
        JsonElement root,
        bool isInteractive,
        bool allowInteractiveReportSending,
        Func<AnalyticsStreamEvent, Task>? onEvent,
        AnalyticsResponse response)
    {
        if (isInteractive && !allowInteractiveReportSending)
            return
                "Email sending is blocked in chat mode unless the user explicitly asks to send an email report in this message.";

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

        var headline = root.TryGetProperty("headline", out var hl) ? hl.GetString() : null;
        var metrics = ParseReportMetrics(root);

        await Emit(onEvent, "sending_report", $"Sending {template} report: {subject}");

        var result = await emailClient.SendAsync(subject, body, severity, recipients,
            wide: true, senderName: "Analytics · Hobbiton", subjectPrefix: "[ANALYTICS]",
            template: template, headline: headline, metrics: metrics);
        response.ReportSent = true;
        response.EmailSubject = subject;
        response.EmailBody = body;
        await Emit(onEvent, "report_sent", result);
        return result;
    }

    private static List<ReportMetric>? ParseReportMetrics(JsonElement root)
    {
        if (!root.TryGetProperty("metrics", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var metrics = new List<ReportMetric>();
        foreach (var el in arr.EnumerateArray())
        {
            var label = el.TryGetProperty("label", out var l) ? l.GetString() : null;
            var value = el.TryGetProperty("value", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value)) continue;

            metrics.Add(new ReportMetric(
                label.Trim(),
                value.Trim(),
                el.TryGetProperty("change", out var c) ? c.GetString() : null,
                el.TryGetProperty("direction", out var d) ? d.GetString() : null));
        }

        return metrics.Count > 0 ? metrics : null;
    }

    private async Task<string> HandleSaveMemory(
        JsonElement root,
        string defaultDatabase,
        Func<AnalyticsStreamEvent, Task>? onEvent)
    {
        var term = root.TryGetProperty("term", out var termEl) ? termEl.GetString() : null;
        var definition = root.TryGetProperty("definition", out var defEl) ? defEl.GetString() : null;
        var database = root.TryGetProperty("database", out var dbEl) ? dbEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(definition))
            return "Both term and definition are required.";

        term = term.Trim();
        definition = definition.Trim();

        database = string.IsNullOrWhiteSpace(database) || database.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : database.Trim();

        // If caller passes "current", scope to active DB.
        if (string.Equals(database, "current", StringComparison.OrdinalIgnoreCase))
            database = defaultDatabase;

        var all = await memoryStore.GetAllAsync();
        var existing = all.FirstOrDefault(m =>
            string.Equals(m.Term, term, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.Database ?? "", database ?? "", StringComparison.OrdinalIgnoreCase));

        var memory = existing ?? new AgentMemory
        {
            Term = term,
            Database = database,
            Enabled = true,
            CreatedBy = "agent:auto"
        };

        memory.Term = term;
        memory.Definition = definition;
        memory.Database = database;

        await memoryStore.SaveAsync(memory);
        await Emit(onEvent, "memory_saved", $"Saved memory: {term}");

        return existing == null
            ? $"Memory saved for '{term}'."
            : $"Memory updated for '{term}'.";
    }

    private static string HandleAskUser(JsonElement root, bool isInteractive)
    {
        if (!isInteractive)
        {
            var question = root.TryGetProperty("question", out var q) ? q.GetString() ?? "" : "";
            return
                $"Running in autonomous mode — cannot ask user. Make a reasonable decision and proceed. Original question was: {question}";
        }

        return "Question sent to user. Waiting for response.";
    }

    private static string HandleGetCurrentTime()
    {
        var utcNow = DateTime.UtcNow;
        var localNow = DateTime.Now;
        return $"UTC: {utcNow:yyyy-MM-dd HH:mm:ss} ({utcNow:dddd})\n" +
               $"Server local: {localNow:yyyy-MM-dd HH:mm:ss} ({localNow:dddd}, {TimeZoneInfo.Local.DisplayName})";
    }

    private static string BuildSystemPrompt(string database, string schema, bool isInteractive,
        bool allowInteractiveReportSending, IEnumerable<AgentMemory>? memories = null,
        IReadOnlyList<string>? additionalDatabases = null)
    {
        var crossDbBlock = additionalDatabases is { Count: > 0 }
            ? $"""


               ## Cross-database access
               This run may also query: {string.Join(", ", additionalDatabases.Select(d => $"`{d}`"))}.
               Only their table *names* are listed above — call `describe_table` to get columns and
               allowed values for the ones you need, batching those calls where you can.
               Qualify every table with its own database — `{database}.<table>` for the primary,
               `<other_db>.<table>` for the rest. Do not assume a table exists in more than one.
               Status values, casing and column naming differ per database; check before filtering.
               """
            : "";

        var reportPolicyBlock = isInteractive
            ? (allowInteractiveReportSending
                ? """

                  ## Email Reports
                  The user explicitly asked to send an email report in this message.
                  You may use `send_report` if it improves the outcome.
                  """
                : """

                  ## Email Reports
                  Do NOT call `send_report` in chat mode unless the user explicitly asks to send an email/report.
                  Focus on analysis in chat and keep results in the conversation.
                  """)
            : "";

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

              ## CSV Export
              Use `export_csv` when the user asks to export, download, or "get a CSV" of data — it runs the
              query directly and gives them a download button, without dumping rows into the chat or your reply.
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

              Tone: professional, data-driven, concise. No filler, no hedging, no casual language. No emojis.
              Format numbers consistently: K 1,250.00 for currency, use percentages for changes.
              Keep it scannable — busy executives should grasp the key points in 10 seconds.

              Emails carry NO charts — they are a text-and-tables medium. Express trends in words and numbers
              ("collections fell for the third straight day, K 412k → K 288k"), not by describing a picture.
              Readers who want visuals follow the dashboard link in the email footer.

              Pick the `template` that matches the report:
              - `executive` — periodic business summaries for leadership (revenue, growth, portfolio)
              - `operational` — day-to-day health: success rates, failures, queues, throughput
              - `incident` — something is broken or a threshold breached and someone must act now
              - `activity` — a digest of notable events and changes across the platform
              - `custom` — anything that fits none of the above
              """;

        var memoriesBlock = BuildMemoriesBlock(memories);

        return $$"""
                 You are an intelligent analytics agent with full access to ClickHouse databases.
                 You investigate questions by querying data, analysing results, and presenting findings clearly.

                 You have tools: run_sql, get_schema, describe_table, emit_chart, export_csv, send_report, ask_user.
                 You can also use save_memory to store durable business definitions for future analyses.

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
                  
                 ## Memory
                 - If the user gives a durable metric/term definition (or asks you to remember one), call `save_memory`.
                 - Save only reusable business knowledge, not temporary one-off context.
                 - Keep terms short and definitions precise.

                 ## Primary database: `{{database}}`
                 {{schema}}
                 {{crossDbBlock}}

                 ## ClickHouse SQL Rules
                 - ONLY SELECT/WITH queries. Never INSERT/UPDATE/DELETE/DROP.
                 - Always qualify tables: `{{database}}.<table>`.
                 - Tables have a `public_` prefix (e.g. `public_transactions`, NOT `transactions`).
                 - Use native ClickHouse functions: ifNull(), countIf(), sumIf(), toStartOfDay(), toStartOfHour(), dateDiff('unit', start, end).
                 - BANNED functions: COALESCE, ISNULL, DATEDIFF (use dateDiff), IIF, NVL, TOP, DATEADD.
                 - Use single quotes for string literals. Add LIMIT 50 unless user specifies otherwise.
                 - For time filtering: `created_at >= now() - INTERVAL 7 DAY`.

                 ## Soft Deletes (read this before every query)
                 Tables marked **CDC replica** in the schema are replicated from the source database and
                 keep rows that were DELETED upstream, flagged with `_peerdb_is_deleted = 1`.
                 - EVERY query touching them must filter `_peerdb_is_deleted = 0` — in the outer WHERE,
                   inside CTEs and subqueries, and on both sides of every JOIN.
                 - Forgetting it does not error. It returns a plausible number that is simply wrong.
                 - Where the schema says the engine is ReplacingMergeTree, also add `FINAL` after the
                   table name so updated rows are not counted once per version.

                 ## Filter Values
                 The schema lists allowed values for LowCardinality columns. Use ONLY these exact values (case-sensitive).
                 Never guess or invent filter values — if one is not listed, run `SELECT DISTINCT col … LIMIT 50`.
                 Table and column names are case-sensitive too. For any table shown by name only, call
                 `describe_table` before querying it rather than guessing its columns.

                 ## Currency
                 All amounts are Zambian Kwacha (ZMW). Use "K" prefix in explanations (e.g. K 1,250.00). Never use $.
                 {{memoriesBlock}}
                 {{interactiveBlock}}
                 {{reportPolicyBlock}}

                 ## Response style
                 After your tool calls complete, write a clear, insightful final response. Reference specific numbers.
                 Be concise but substantive. Don't repeat the query — focus on what the data means.
                 No emojis — headings, bold, and tables carry the structure instead.
                 """;
    }

    private static string BuildMemoriesBlock(IEnumerable<AgentMemory>? memories)
    {
        var list = memories?.ToList();
        if (list is not { Count: > 0 }) return "";

        var sb = new StringBuilder("\n## Business Definitions\n");
        sb.AppendLine("The following terms and calculations have been defined by your organization.");
        sb.AppendLine("Use these definitions exactly when answering questions that involve these metrics:\n");
        foreach (var m in list)
            sb.AppendLine($"**{m.Term}**: {m.Definition}");
        return sb.ToString();
    }

    // ponytail: strips data rows from older history entries to stay under API size limits
    private static string CompactResponse(AnalyticsResponse r)
    {
        var compact = new
        {
            r.Explanation,
            r.Summary,
            r.RiskLevel,
            r.Findings,
            r.RecommendedActions,
            r.ChartType,
            r.Columns,
            r.RowCount,
            r.Error,
            Results = r.Results.Select(q => new { q.Label, q.Columns, q.RowCount }).ToList()
        };
        return JsonSerializer.Serialize(compact);
    }

    private static bool HasExplicitEmailIntent(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return false;
        var normalized = prompt.Trim();

        // Require explicit "email/mail/report" intent with an action verb to avoid accidental sends in chat mode.
        var mentionsEmailOrReport =
            Regex.IsMatch(normalized, @"\b(email|e-mail|mail|report)\b", RegexOptions.IgnoreCase);
        var hasDeliveryVerb =
            Regex.IsMatch(normalized, @"\b(send|share|forward|deliver|notify)\b", RegexOptions.IgnoreCase);

        return mentionsEmailOrReport && hasDeliveryVerb;
    }


    /// <summary>
    /// Rejects queries against PeerDB CDC replica tables that never mention
    /// <c>_peerdb_is_deleted</c>. Those tables keep deleted rows, so an unfiltered count/sum is
    /// silently inflated — the model is told this in the schema block, but instructions get
    /// dropped on long queries, and a wrong number is worse than a retry.
    /// ponytail: a substring check, not SQL parsing. Mentioning the column anywhere counts as
    /// handled — upgrade to per-table clause analysis only if agents start gaming it.
    /// </summary>
    private async Task<string?> ValidateSoftDeleteFilterAsync(string sql, string database)
    {
        if (sql.Contains("_peerdb_is_deleted", StringComparison.OrdinalIgnoreCase)) return null;

        var tables = ExtractReferencedTables(sql, database);
        if (tables.Count == 0) return null;

        var cdcTables = new List<string>();
        foreach (var table in tables)
            if (await schemaLoader.HasSoftDeletesAsync(database, table))
                cdcTables.Add(table);

        if (cdcTables.Count == 0) return null;

        return "MISSING SOFT-DELETE FILTER:\n" +
               string.Join("\n", cdcTables.Select(t => $"- `{database}.{t}` is a PeerDB CDC replica")) +
               "\n\nThese tables retain rows deleted in the source system, so this query would count " +
               "them and report inflated numbers. Add `_peerdb_is_deleted = 0` to the WHERE clause " +
               "of every reference to these tables — including inside CTEs, subqueries and both " +
               "sides of any JOIN — then call run_sql again.";
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

    /// <summary>
    /// Build a corrective feedback message for a failed SQL query. When the error involves
    /// unknown columns/identifiers, we fetch and include the actual table schema so the agent
    /// can self-correct in one retry instead of guessing.
    /// </summary>
    private async Task<string> BuildErrorFeedbackAsync(string error, string sql, string database)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ClickHouse error:\n{error}");
        sb.AppendLine($"\nFailing SQL:\n{sql}\n");

        var isColumnError =
            error.Contains("Unknown expression", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Unknown identifier", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Missing columns", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Unknown column", StringComparison.OrdinalIgnoreCase);

        var isTableError =
            error.Contains("Unknown table", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Table", StringComparison.OrdinalIgnoreCase) &&
            error.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase);

        var isTypeError =
            error.Contains("Illegal type", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("TYPE_MISMATCH", StringComparison.OrdinalIgnoreCase);

        var isFunctionError =
            error.Contains("Unknown function", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("COALESCE", StringComparison.OrdinalIgnoreCase);

        if (isTableError)
        {
            sb.AppendLine(
                "FIX: The table does not exist. Tables are case-sensitive and prefixed with the database name (e.g. `lipila_blaze.public_transactions`). Use only tables from the schema provided.");
        }
        else if (isColumnError)
        {
            sb.AppendLine(
                "FIX: A column name is wrong or does not exist on this table. Column names are CASE-SENSITIVE in ClickHouse.");

            // Fetch the real columns for each table referenced in the query so the agent
            // can see exactly what's available and fix the query in one retry.
            var tables = ExtractReferencedTables(sql, database);
            foreach (var table in tables.Take(3)) // limit to avoid huge payloads
            {
                try
                {
                    var cols = await ch.QueryAsync(
                        $"SELECT name, type FROM system.columns WHERE database = '{database}' AND table = '{table}' ORDER BY position FORMAT TabSeparatedWithNames");
                    if (!IsClickHouseError(cols))
                    {
                        sb.AppendLine($"\nActual columns on `{database}.{table}`:");
                        sb.AppendLine(cols);
                    }
                }
                catch
                {
                    /* best-effort */
                }
            }
        }
        else if (isFunctionError)
        {
            sb.AppendLine("FIX: That function is not available in ClickHouse. Common replacements:");
            sb.AppendLine("  - COALESCE → ifNull(a, b)");
            sb.AppendLine("  - NVL → ifNull(a, b)");
            sb.AppendLine("  - DATE_TRUNC('month', col) → toStartOfMonth(col)");
            sb.AppendLine("  - DATEDIFF → dateDiff('unit', start, end)");
            sb.AppendLine("  - STRING_AGG → groupArray(col)");
        }
        else if (isTypeError)
        {
            sb.AppendLine("FIX: Type mismatch. Common fixes:");
            sb.AppendLine("  - Comparing String to number → use toString(col) or toUInt64(col)");
            sb.AppendLine("  - Date arithmetic → use toDate(col) + INTERVAL N DAY");
        }
        else
        {
            sb.AppendLine(
                "FIX: Review the error above and correct the SQL. Remember ClickHouse syntax differs from MySQL/PostgreSQL.");
        }

        sb.AppendLine(
            "\nRewrite the query using only columns and functions that exist. Call run_sql again with the corrected SQL.");
        return sb.ToString();
    }

    internal static List<string> ExtractReferencedTables(string sql, string database)
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
            if (!map.TryGetValue(col, out var set))
            {
                set = [];
                map[col] = set;
            }

            set.Add(m.Groups[2].Value);
        }

        foreach (Match m in InFilterRegex.Matches(sql))
        {
            var col = m.Groups[1].Value;
            if (IsReservedKeyword(col)) continue;
            if (!map.TryGetValue(col, out var set))
            {
                set = [];
                map[col] = set;
            }

            foreach (Match lit in StringLiteralInList.Matches(m.Groups[2].Value))
                set.Add(lit.Groups[1].Value);
        }

        return map.Where(kv => kv.Value.Count > 0).Select(kv => (kv.Key, kv.Value.ToList())).ToList();
    }

    private static bool IsReservedKeyword(string word) => SqlKeywords.Contains(word);

    private static bool IsClickHouseError(string result) =>
        result.StartsWith("ClickHouse error") ||
        result.StartsWith("Error:") ||
        result.StartsWith("Query failed:");

    /// <summary>Parses a ClickHouse <c>default_format=JSON</c> payload into columns + string rows.</summary>
    public static TableData ParseQueryResult(string json)
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
        catch
        {
            /* return empty table */
        }

        return data;
    }

    private static async Task Emit(Func<AnalyticsStreamEvent, Task>? onEvent, string type, string message,
        string? sql = null)
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

    private sealed class ToolCallBuilder
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        // Accumulate raw UTF-8 bytes and decode once at the end. Decoding each streamed
        // chunk separately corrupts any multi-byte character split across a chunk boundary.
        public List<byte> ArgBytes { get; } = [];
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

    [JsonIgnore] public string? RawResult { get; set; }

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

    /// <summary>Subject of the email sent via send_report (captured for audit storage).</summary>
    public string? EmailSubject { get; set; }

    /// <summary>Body of the email sent via send_report (captured for audit storage).</summary>
    public string? EmailBody { get; set; }
}

/// <summary>
/// A single tool invocation emitted by the agent during a run, for audit-trail persistence.
/// Mirrors the data needed to build a <c>RunLog</c> without coupling the agent to the store.
/// </summary>
public record AgentToolCall(
    int Iteration,
    string ToolName,
    string Args,
    string Result,
    int DurationMs,
    DateTime StartedAt,
    string LogType = "tool_call");

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