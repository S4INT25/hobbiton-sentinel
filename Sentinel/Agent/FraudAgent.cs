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
    IConfiguration config,
    ILogger<FraudAgent> logger)
{
    private string BuildSystemPrompt(string openCasesSummary, int lookbackMinutes)
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

        ## Schema (verify with DESCRIBE before querying)
        Always run `SHOW TABLES FROM lipila_blaze` first; table names may be prefixed (e.g. `public_transactions`).
        - **transactions**: reference_id, ip_address, amount, account_number, wallet_id, merchant_id, api_key_id, admin_id, user_id, type (collection/disbursement), status (successful/failed/pending), created_at
        - **user_activity_logs**: user_email, activity_type, action, ip_address, user_agent, description, status, timestamp, merchant_id
        - **merchants**: id, name, status, created_at
        - **wallets**: id, name, balance, merchant_id, status, updated_at
        - **users**: id, email, phone_number, merchant_id, created_at
        - **api_keys**: id, merchant_id, status, allowed_ips, created_at

        ## Betting Merchants
        Betting/gaming merchants legitimately disburse 50–200+/hr. Before flagging any velocity pattern, query the merchant's 30-day hourly disbursement history (`toStartOfHour`, `count()`, `INTERVAL 30 DAY`) and use their own avg/max as the baseline. Only flag if the current rate is a meaningful outlier.

        ## Fraud Patterns
        {FraudPatternRegistry.ToPromptBlock()}

        ## Open Cases
        You have {casesContext}
        {openCasesSummary}

        ## This Run
        UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} (Zambia = UTC+2) | Lookback: {lookbackMinutes} min

        **Step 0 — Schema discovery.** `SHOW TABLES FROM lipila_blaze`, then `DESCRIBE` each table before querying.

        **Step 1 — Follow up open cases.** Re-query each; escalate/watch/resolve as appropriate.

        **Step 2 — Pattern scan.** Check all {FraudPatternRegistry.GetEnabled().Count()} patterns against data from the last {lookbackMinutes} minutes.

        **Step 2b — Activity log review.** Query user_activity_logs. Flag: foreign/datacenter logins, brute force (failed→success), midnight–5am CAT logins, sensitive actions (wallet/key/merchant edits), internal-IP actions with no portal login. Cross-reference suspicious logins against transactions from same IP/merchant/wallet.

        **IP rule:** Never display a bare IP. Always call `lookup_ip` first, then show inline: `1.2.3.4 [DATACENTER] (South Africa, Amazon)` or `197.x.x.x (Zambia, Airtel)`. Applies everywhere — query results, cases, alerts.

        **Step 3 — Observations.** Flag unusual but not necessarily fraudulent signals: sudden activity after dormancy, high failure rates, new merchants with outsized first-day volume, large idle wallets, repeated same-amount same-recipient, after-hours admin actions.

        **Step 4 — Case management.** Create/update/resolve cases.

        **Step 5 — Alert.** Send alert ONLY if: severity Warning/Critical, open cases needing attention, or a case just resolved. Silent finish if fully clean with no open cases.
        Follow the send_alert tool format exactly. Empty sections → "None this run." / "No open cases." / "No actions required." Timestamps in CAT. Amounts in ZMW with comma separators.

        ## Tone
        Observations only — never verdicts. Use "appears to", "may indicate", "pattern suggests". Findings are for human review.

        ## Query Rules
        `lipila_blaze.<table>` always. Time filter: `created_at >= now() - INTERVAL {lookbackMinutes} MINUTE`. Max 50 rows. Single quotes for strings. On error: re-DESCRIBE and retry.
        """;
    }

    public async Task RunAsync()
    {
        var runId = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var lookback = config.GetValue("Sentinel:LookbackMinutes", 70);
        var modelName = config["DigitalOcean:ModelName"]!;

        logger.LogInformation("Fraud agent run {RunId} started", runId);

        // Load open cases from Redis memory
        var openCasesSummary = await caseStore.GetOpenCasesSummaryAsync();
        var openCases = await caseStore.GetOpenCasesAsync();

        logger.LogInformation("Loaded {Count} open cases", openCases.Count);

        var systemPrompt = BuildSystemPrompt(openCasesSummary, lookback);

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

        while (iteration++ < maxIterations)
        {
            var options = new ChatCompletionOptions { MaxOutputTokenCount = 4096, Temperature = 0.1f };
            foreach (var tool in tools) options.Tools.Add(tool);

            var response = await chatClient.CompleteChatAsync(messages, options);

            var completion = response.Value;
            messages.Add(new AssistantChatMessage(completion));

            // ── LLM response logging ──────────────────────────────────────────
            var inputTokens  = completion.Usage?.InputTokenCount  ?? 0;
            var outputTokens = completion.Usage?.OutputTokenCount ?? 0;
            var totalTokens  = completion.Usage?.TotalTokenCount  ?? 0;

            logger.LogInformation(
                "[Run:{RunId}] Iteration {N}: finish={Reason} tools={ToolCount} tokens={Total} (in={In} out={Out})",
                runId, iteration, completion.FinishReason, completion.ToolCalls.Count, totalTokens, inputTokens, outputTokens);

            // Log any text content the LLM produced (reasoning / narration)
            var textContent = string.Concat((completion.Content ?? [])
                .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                .Select(p => p.Text));
            if (!string.IsNullOrWhiteSpace(textContent))
                logger.LogDebug("[Run:{RunId}] LLM text: {Text}", runId, textContent);

            if (completion.FinishReason == ChatFinishReason.Stop)
                break;

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                foreach (var toolCall in completion.ToolCalls)
                {
                    // Truncate large args for readability (SQL queries can be long)
                    var args = toolCall.FunctionArguments.ToString();
                    var argsPreview = args.Length > 300 ? args[..300] + "…" : args;

                    logger.LogInformation("[Run:{RunId}] Tool call: {Tool} args={Args}",
                        runId, toolCall.FunctionName, argsPreview);

                    var result = await ExecuteToolAsync(toolCall, runId);

                    var resultPreview = result.Length > 500 ? result[..500] + "…" : result;
                    logger.LogInformation("[Run:{RunId}] Tool result: {Tool} → {Result}",
                        runId, toolCall.FunctionName, resultPreview);

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

            foreach (var tool in tools) options.Tools.Add(tool);
            var summaryResponse = await chatClient.CompleteChatAsync(messages, options);
            var summaryCompletion = summaryResponse.Value;
            messages.Add(new AssistantChatMessage(summaryCompletion));

            foreach (var toolCall in summaryCompletion.ToolCalls)
            {
                var result = await ExecuteToolAsync(toolCall, runId);
                logger.LogInformation("Max-iteration summary tool: {Tool} → {Result}", toolCall.FunctionName, result);
                if (toolCall.FunctionName == "send_alert") alertSent = true;
            }

            // Only send the fallback email if the run was genuinely incomplete (no alert and hit the ceiling)
            if (!alertSent)
            {
                logger.LogWarning("Run {RunId} hit iteration limit with no alert sent — sending ops notification", runId);
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
                "run_sql" => await ch.QueryAsync(
                    root.GetProperty("query").GetString()!),

                "create_case" => await CreateCaseAsync(root, runId),
                "update_case" => await UpdateCaseAsync(root, runId),
                "resolve_case" => await ResolveCaseAsync(root),

                "send_alert" => await email.SendAsync(
                    root.GetProperty("subject").GetString()!,
                    root.GetProperty("body").GetString()!,
                    root.TryGetProperty("severity", out var sev) ? sev.GetString()! : "watching"),

                "lookup_ip" => await ipLookup.LookupAsync(
                    JsonToStringList(root.GetProperty("ips"))),

                _ => $"Unknown tool: {toolCall.FunctionName}"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool execution failed: {Tool}", toolCall.FunctionName);
            return $"Tool error: {ex.Message}";
        }
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
            fraudCase.AffectedEntities = JsonToStringList(entities);

        if (root.TryGetProperty("follow_up_queries", out var queries))
            fraudCase.FollowUpQueries = JsonToStringList(queries);

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
            fraudCase.FollowUpQueries = JsonToStringList(queries);

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

    /// <summary>
    /// Safely converts a JsonElement to a list of non-empty strings.
    /// Handles: JSON array of strings, plain JSON string (newline-separated), or any other kind (returns empty).
    /// Array elements that are null, non-string, or empty are silently skipped.
    /// </summary>
    private static List<string> JsonToStringList(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Array => el.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList(),

        JsonValueKind.String => (el.GetString() ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList(),

        _ => []
    };
}
