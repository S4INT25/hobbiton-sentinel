using System.Text.Json;
using Sentinel.Infrastructure;
using Sentinel.Memory;
using OpenAI;
using OpenAI.Chat;

namespace Sentinel.Agent;

public class FraudAgent(
    OpenAIClient ai,
    ClickHouseClient ch,
    EmailClient email,
    IpLookupClient ipLookup,
    ICaseStore caseStore,
    SchemaLoader schemaLoader,
    IConfiguration config,
    ILogger<FraudAgent> logger)
{
    private string BuildSystemPrompt(string openCasesSummary, int lookbackMinutes, string schemaBlock)
    {
        var casesContext = openCasesSummary == "No open cases."
            ? "no open cases."
            : "the following open cases from previous runs:";

        return $"""
                You are a financial fraud analyst with read-only ClickHouse access to Lipila — a Zambian payment gateway (collections + disbursements via AirtelMoney/MTN). Disbursements are irreversible.

                ## ClickHouse SQL Rules (strict)
                Native ClickHouse only. Banned: `IIIF`, `IIF`, `NVL`, `ISNULL`, `COALESCE` (use `ifNull()`), `DATEDIFF`, `TOP`, `CHARINDEX`, `PATINDEX`, `COUNT(CASE WHEN ...)`.
                Use: `if()`, `multiIf()`, `countIf()`, `sumIf()`, `ifNull()`, `toStartOfHour()`, `now() - INTERVAL N DAY`, `positionCaseInsensitive()`, `match()`.
                If unsure a function exists — don't use it. Always qualify: `lipila_blaze.<table>`.

                {schemaBlock}

                ## Betting Merchants
                Betting/gaming merchants legitimately disburse 50–200+/hr. Before flagging any velocity pattern, query the merchant's 30-day hourly disbursement history (`toStartOfHour`, `count()`, `INTERVAL 30 DAY`) and use their own avg/max as the baseline. Only flag if the current rate is a meaningful outlier.

                ## Fraud Patterns
                {FraudPatternRegistry.ToPromptBlock()}

                ## Open Cases
                You have {casesContext}
                {openCasesSummary}

                ## This Run
                UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} (Zambia = UTC+2) | Lookback: {lookbackMinutes} min

                **Step 1 — Follow up open cases.** Re-query each; escalate/watch/resolve as appropriate.

                **Step 2 — Pattern scan.** Check all {FraudPatternRegistry.GetEnabled().Count()} patterns against data from the last {lookbackMinutes} minutes.

                **Step 2b — Activity log review.** Query user_activity_logs. Flag: foreign/datacenter logins, brute force (failed→success), midnight–5am CAT logins, sensitive actions (wallet/key/merchant edits), internal-IP actions with no portal login. Cross-reference suspicious logins against transactions from same IP/merchant/wallet.

                **Step 2c — Free investigation.** The registered patterns are a baseline, not a ceiling. You are free — and encouraged — to follow your own instincts. If a query result looks odd, dig deeper. If you notice a pattern not covered by any registered rule, investigate it anyway and surface it as a finding. Examples of things to explore freely:
                - Unusual merchant behaviour that doesn't fit any named pattern
                - Correlations between tables that seem structurally suspicious
                - Timing anomalies (e.g. disbursements seconds after a collection from the same wallet)
                - Recipient account numbers appearing across multiple unrelated merchants
                - API keys used from multiple geographically distinct IPs in the same hour
                - Transactions with suspiciously round amounts dominating a merchant's volume
                - Any data shape that a human fraud analyst would find worth a second look
                Trust your judgment. If you see something, investigate it.

                **IP rule:** Never display a bare IP. Always call `lookup_ip` first, then show inline: `1.2.3.4 [DATACENTER] (South Africa, Amazon)` or `197.x.x.x (Zambia, Airtel)`. Applies everywhere — query results, cases, alerts.

                **Step 3 — Observations.** Flag unusual but not necessarily fraudulent signals: sudden activity after dormancy, high failure rates, new merchants with outsized first-day volume, large idle wallets, repeated same-amount same-recipient, after-hours admin actions.

                **Step 4 — Case management.** Create/update/resolve cases.

                **Step 5 — Alert.** Send alert ONLY if: severity Warning/Critical, open cases needing attention, or a case just resolved. Silent finish if fully clean with no open cases.
                Follow the send_alert tool format exactly. Empty sections → "None this run." / "No open cases." / "No actions required." Timestamps in CAT. Amounts in ZMW with comma separators.

                ## Tone
                Observations only — never verdicts. Use "appears to", "may indicate", "pattern suggests". Findings are for human review.

                ## Query Rules
                - Always qualify: `lipila_blaze.<table>`. Time filter: `created_at >= now() - INTERVAL {lookbackMinutes} MINUTE`. Max 50 rows per query. Single quotes for strings. On error: re-DESCRIBE and retry.
                - **ALWAYS use the `queries` array — even for a single query.** Never call `run_sql` more than once in a row when the queries are independent. Batch everything you need into one call. This is mandatory, not optional.
                """;
    }

    public async Task RunAsync()
    {
        var runId = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var lookback = config.GetValue("Sentinel:LookbackMinutes", 70);
        var modelName = config["DigitalOcean:ModelName"]!;

        logger.LogInformation("Fraud agent run {RunId} started", runId);

        // Auto-resolve cases that have gone stale (no agent activity for N days)
        var staleDays = config.GetValue("Sentinel:StaleCase:ThresholdDays", 7);
        var staleClosed = await caseStore.AutoResolveStaleAsync(staleDays);
        if (staleClosed > 0)
            logger.LogInformation("Auto-resolved {Count} stale case(s) (threshold: {Days} days)", staleClosed,
                staleDays);

        // Load open cases and schema concurrently
        var openCasesSummaryTask = caseStore.GetOpenCasesSummaryAsync();
        var openCasesTask = caseStore.GetOpenCasesAsync();
        var schemaBlockTask = schemaLoader.GetSchemaBlockAsync();

        await Task.WhenAll(openCasesSummaryTask, openCasesTask, schemaBlockTask);

        var openCasesSummary = await openCasesSummaryTask;
        var openCases = await openCasesTask;
        var schemaBlock = await schemaBlockTask;

        logger.LogInformation("Loaded {Count} open cases", openCases.Count);

        var systemPrompt = BuildSystemPrompt(openCasesSummary, lookback, schemaBlock);

        // Inject follow-up queries from open cases into the first user message
        var followUpContext = openCases.Count > 0 && openCases.Any(c => c.FollowUpQueries.Count > 0)
            ? "\n\nSuggested follow-up queries from previous runs:\n" +
              string.Join("\n", openCases
                  .Where(c => c.FollowUpQueries.Count > 0)
                  .SelectMany(c => c.FollowUpQueries.Select(q => $"-- Case {c.Id}: {c.Title}\n{q}")))
            : "";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(
                $"Run ID: {runId}. Start your investigation now.{followUpContext}")
        };

        var tools = FraudAgentTools.GetToolDefinitions();
        var chatClient = ai.GetChatClient(modelName);
        var iteration = 0;
        var maxIterations = config.GetValue("Sentinel:MaxIterations", 60);
        var alertSent = false;
        var earlyWarningSent = false;
        var earlyWarningThreshold = (int)(maxIterations * 0.75);

        while (iteration++ < maxIterations)
        {
            var options = new ChatCompletionOptions { MaxOutputTokenCount = 4096, Temperature = 0.1f };
            foreach (var tool in tools) options.Tools.Add(tool);

            // Early warning at 75% — give the agent a chance to wrap up gracefully
            if (!earlyWarningSent && iteration >= earlyWarningThreshold)
            {
                earlyWarningSent = true;
                logger.LogWarning("[Run:{RunId}] Approaching iteration limit ({N}/{Max}) — injecting wrap-up warning",
                    runId, iteration, maxIterations);
                messages.Add(new UserChatMessage(
                    $"⚠️ You have used {iteration - 1} of {maxIterations} allowed steps. " +
                    $"You have approximately {maxIterations - iteration + 1} steps remaining. " +
                    "Prioritise completing your current investigation, then call send_alert (if warranted) and stop. " +
                    "Do not start new broad queries — focus only on what is needed to close open threads."));
            }

            var response = await chatClient.CompleteChatAsync(messages, options);

            var completion = response.Value;
            messages.Add(new AssistantChatMessage(completion));

            var inputTokens = completion.Usage?.InputTokenCount ?? 0;
            var outputTokens = completion.Usage?.OutputTokenCount ?? 0;
            var totalTokens = completion.Usage?.TotalTokenCount ?? 0;

            logger.LogInformation(
                "[Run:{RunId}] Iteration {N}: finish={Reason} tools={ToolCount} tokens={Total} (in={In} out={Out})",
                runId, iteration, completion.FinishReason, completion.ToolCalls.Count, totalTokens, inputTokens,
                outputTokens);

            var textContent = string.Concat((completion.Content ?? [])
                .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                .Select(p => p.Text));

            if (!string.IsNullOrWhiteSpace(textContent))
            {
                logger.LogInformation("[Run:{RunId}] LLM text: {Text}", runId, textContent);
            }

            if (completion.FinishReason == ChatFinishReason.Stop)
            {
                break;
            }

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                foreach (var toolCall in completion.ToolCalls)
                {
                    var args = toolCall.FunctionArguments.ToString();
                    var argsPreview = args.Length > 300 ? args[..300] + "…" : args;

                    logger.LogInformation("[Run:{RunId}] Tool call: {Tool} args={Args}",
                        runId, toolCall.FunctionName, argsPreview);

                    var result = await ExecuteToolAsync(toolCall, runId);

                    var resultPreview = result.Length > 300 ? result[..300] + "…" : result;

                    logger.LogInformation("[Run:{RunId}] Tool result: {Tool} → {Result}", runId, toolCall.FunctionName,
                        resultPreview);

                    messages.Add(new ToolChatMessage(toolCall.Id, result));
                    if (toolCall.FunctionName == "send_alert") alertSent = true;
                }
            }
        }

        if (iteration >= maxIterations)
        {
            logger.LogWarning("Agent hit max iterations ({Max}), requesting summary", maxIterations);

            // Ask the agent to summarise whatever it found before hitting the limit
            var options = new ChatCompletionOptions { MaxOutputTokenCount = 2048, Temperature = 0.1f };
            messages.Add(new UserChatMessage(
                "You have reached the iteration limit. Do NOT run any more queries. " +
                "If this run found any suspicious activity, open cases, or resolved cases, call send_alert now " +
                "with a complete report using the EXACT structure defined in the send_alert tool. " +
                "If the run was fully clean with no findings and no open cases, do NOT call send_alert — just stop."));

            foreach (var tool in tools)
            {
                options.Tools.Add(tool);
            }
            var summaryResponse = await chatClient.CompleteChatAsync(messages, options);
            var summaryCompletion = summaryResponse.Value;
            messages.Add(new AssistantChatMessage(summaryCompletion));

            foreach (var toolCall in summaryCompletion.ToolCalls)
            {
                var result = await ExecuteToolAsync(toolCall, runId);
                logger.LogInformation("Max-iteration summary tool: {Tool} → {Result}", toolCall.FunctionName, result);
                if (toolCall.FunctionName == "send_alert")
                {
                    alertSent = true;
                    break;
                }
            }

            // Only send the fallback email if the run was genuinely incomplete (no alert and hit the ceiling)
            if (!alertSent)
            {
                logger.LogWarning("Run {RunId} hit iteration limit with no alert sent — sending ops notification",
                    runId);
                await email.SendAsync(
                    "Fraud Detector: Run Incomplete",
                    $"Run {runId} reached the iteration limit ({maxIterations} steps) without completing. " +
                    "Review Hangfire logs for partial findings.",
                    "warning");
            }
        }
        else if (!alertSent)
        {
            logger.LogInformation("Run {RunId} completed cleanly — no alert warranted", runId);
        }

        logger.LogInformation("Run {RunId} completed after {N} iterations", runId, iteration);
    }

    private async Task<string> ExecuteToolAsync(ChatToolCall toolCall, string runId)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolCall.FunctionArguments);
            var root = doc.RootElement;

            return toolCall.FunctionName switch
            {
                "run_sql" => await ExecuteSqlAsync(root),
                "create_case" => await CreateCaseAsync(root, runId),
                "update_case" => await UpdateCaseAsync(root, runId),
                "resolve_case" => await ResolveCaseAsync(root),
                "send_alert" => await email.SendAsync(
                    root.GetProperty("subject").GetString()!,
                    root.GetProperty("body").GetString()!,
                    root.TryGetProperty("severity", out var sev) ? sev.GetString()! : "watching"),
                "lookup_ip" => await ipLookup.LookupAsync(
                    JsonHelpers.ToIpList(root.GetProperty("ips"))),
                _ => $"Unknown tool: {toolCall.FunctionName}"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool execution failed: {Tool}", toolCall.FunctionName);
            return $"Tool error: {ex.Message}";
        }
    }

    private async Task<string> ExecuteSqlAsync(JsonElement root)
    {
        List<string> queries = [];

        if (root.TryGetProperty("queries", out var queriesEl))
        {
            queries = queriesEl.ValueKind switch
            {
                JsonValueKind.Array => JsonHelpers.ToStringList(queriesEl),
                JsonValueKind.String => JsonHelpers.ParseDelimitedString(queriesEl.GetString()),
                _ => []
            };
        }

        // Fallback: legacy single "query" string
        if (queries.Count == 0 && root.TryGetProperty("query", out var queryEl)
                               && queryEl.ValueKind == JsonValueKind.String)
            queries = [queryEl.GetString()!];

        if (queries.Count == 0)
            return "Error: provide a 'queries' array of SQL strings.";

        if (queries.Count == 1)
            return await ch.QueryAsync(queries[0]);

        var tasks = queries.Select((q, i) => ch.QueryAsync(q).ContinueWith(t =>
            $"--- Query {i + 1} ---\n{t.Result}"));

        var results = await Task.WhenAll(tasks);
        return string.Join("\n\n", results);
    }

    private async Task<string> CreateCaseAsync(JsonElement root, string runId)
    {
        var fraudCase = new FraudCase
        {
            Title = root.GetProperty("title").GetString()!,
            Category = root.GetProperty("category").GetString()!,
            Severity = root.GetProperty("severity").GetString()!,
            Notes = root.GetProperty("notes").GetString()!,
            Status = "open"
        };

        if (root.TryGetProperty("affected_entities", out var entities))
            fraudCase.AffectedEntities = JsonHelpers.ToStringList(entities);

        if (root.TryGetProperty("follow_up_queries", out var queries))
            fraudCase.FollowUpQueries = JsonHelpers.ToStringList(queries);

        fraudCase.Evidence.Add(new CaseEvidence
        {
            RunId = runId,
            Summary = $"Case created in run {runId}",
            RawData = fraudCase.Notes[..Math.Min(500, fraudCase.Notes.Length)]
        });

        await caseStore.SaveCaseAsync(fraudCase);
        return $"Case {fraudCase.Id} created: {fraudCase.Title}";
    }

    private async Task<string> UpdateCaseAsync(JsonElement root, string runId)
    {
        var caseId = root.GetProperty("case_id").GetString()!;
        var fraudCase = await caseStore.GetCaseAsync(caseId);
        if (fraudCase == null) return $"Case {caseId} not found.";

        var newNotes = root.GetProperty("notes").GetString()!;
        fraudCase.Notes += $"\n\n[{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC - Run {runId}]\n{newNotes}";
        fraudCase.OccurrenceCount++;

        if (root.TryGetProperty("severity", out var sev))
            fraudCase.Severity = sev.GetString()!;

        if (root.TryGetProperty("status", out var status))
            fraudCase.Status = status.GetString()!;

        if (root.TryGetProperty("follow_up_queries", out var queries))
            fraudCase.FollowUpQueries = JsonHelpers.ToStringList(queries);

        fraudCase.Evidence.Add(new CaseEvidence
        {
            RunId = runId,
            Summary = newNotes[..Math.Min(200, newNotes.Length)]
        });

        // Keep evidence list bounded
        if (fraudCase.Evidence.Count > 20)
            fraudCase.Evidence = fraudCase.Evidence.TakeLast(20).ToList();

        await caseStore.SaveCaseAsync(fraudCase);
        return $"Case {caseId} updated. Occurrence #{fraudCase.OccurrenceCount}.";
    }

    private async Task<string> ResolveCaseAsync(JsonElement root)
    {
        var caseId = root.GetProperty("case_id").GetString()!;
        var resolution = root.GetProperty("resolution").GetString()!;
        await caseStore.ResolveCaseAsync(caseId, resolution);
        return $"Case {caseId} resolved: {resolution}";
    }
}