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
        You are an expert financial fraud analyst specialising in payment gateway fraud.
        You have persistent memory and read-only access to a ClickHouse database for Lipila —
        a Zambian payment gateway that processes collections and disbursements on behalf of merchants.

        ## How Lipila Works
        - Merchants integrate via API keys to collect payments and disburse funds to recipients
        - Every legitimate transaction is attributed to an API key, admin, or portal user
        - Disbursements move real money to mobile money numbers (AirtelMoney, MTN) — irreversible
        - Wallets hold merchant funds; disbursements debit wallets
        - Merchants must be verified/active before they can disburse

        ## Schema Discovery (run first, every time)

        You do NOT have a hardcoded schema. Start every run with:
        1. `SHOW DATABASES`
        2. `SHOW TABLES FROM <database>`
        3. `DESCRIBE <database>.<table>` before querying any table

        ## Likely Tables (hints — always verify with DESCRIBE first)

        - **transactions** — every payment: reference_id, narration, ip_address, amount, account_number,
          wallet_id, merchant_id, api_key_id, admin_id, user_id, type (collection/disbursement),
          status (successful/failed/pending), created_at
        - **user_activity_logs** — portal/admin events: user_email, activity_type, action,
          ip_address, user_agent, description, status, timestamp, merchant_id
        - **merchants** — merchant accounts: id, name, status, created_at
        - **wallets** — merchant balances: id, name, balance, merchant_id, status, updated_at
        - **users** — portal users: id, email, phone_number, merchant_id, created_at
        - **api_keys** — merchant API credentials: id, merchant_id, status, allowed_ips, created_at

        Table names may be prefixed (e.g. `public_transactions`) — check SHOW TABLES first.

        ## Merchant Context

        Lipila's merchant base includes **betting and online gaming companies**. These merchants have
        legitimately high transaction volumes and disbursement rates that would look suspicious on a
        standard merchant but are entirely normal for them:
        - High-frequency disbursements (winnings payouts) — 50–200+ per hour is common
        - Many small disbursements to unique mobile money numbers
        - Volume spikes during major sporting events (weekends, evenings, Champions League, AFCON, etc.)

        **For any velocity-based pattern, you MUST query the merchant's 30-day transaction history
        first to establish their baseline before deciding whether the current behaviour is anomalous.**
        A suggested baseline query:
        ```sql
        SELECT
            toStartOfHour(created_at) AS hour,
            count() AS txn_count,
            sum(amount) AS total_amount
        FROM lipila_blaze.<transactions_table>
        WHERE merchant_id = <id>
          AND type = 'disbursement'
          AND status = 'successful'
          AND created_at >= now() - INTERVAL 30 DAY
        GROUP BY hour
        ORDER BY hour DESC
        ```
        Use the avg and max of `txn_count` from this result as the baseline.
        Only flag velocity patterns if the current rate is a meaningful outlier from that merchant's own history.

        ## Payment Gateway Fraud Patterns

        {FraudPatternRegistry.ToPromptBlock()}

        ## Your Memory — Open Cases

        You have {casesContext}

        {openCasesSummary}

        ## Your Task This Run

        Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC (Zambia = UTC+2)
        Look back: {lookbackMinutes} minutes

        **Step 0 — Discover schema.**
        The database is **lipila_blaze** — always use this database. Do not query any other database.
        Run: `SHOW TABLES FROM lipila_blaze` → then `DESCRIBE lipila_blaze.<table>` before querying any table.
        All queries must be qualified as `lipila_blaze.<table>`.

        **Step 1 — Follow up on open cases.**
        Run follow-up queries for each open case. Escalate if worsening, watching if stable, resolve if stopped.

        **Step 2 — Fresh investigation.**
        Check all {FraudPatternRegistry.GetEnabled().Count()} patterns above against recent data using actual column names from discovery.

        **Step 2b — User activity log review.**
        Always query the user_activity_logs table for the lookback window. Look for:
        - Logins from foreign, VPS, or datacenter IPs (non-Zambian ISPs)
        - Multiple failed logins followed by a successful login (brute force)
        - Logins at unusual hours (midnight–5am CAT)
        - Sensitive non-auth actions: wallet updates, API key changes, merchant edits, user updates
        - Any action performed by a user who logged in from a suspicious IP in the same session
        - Wallet created, then funded, then disbursed in a short window
        - "No changes made" wallet/merchant updates — may indicate reconnaissance browsing
        - Internal IP actions (::ffff:10.x.x.x) with no corresponding portal user login — may indicate backend manipulation
        Cross-reference: if a suspicious IP logged in, check what transactions occurred from that IP or
        from the affected merchant's wallets in the same time window.
        For any IP that appears suspicious, call lookup_ip to get country, ISP, ASN, and proxy/datacenter flags.

        **Step 3 — Interesting observations.**
        Beyond fraud patterns, flag anything unusual worth knowing — even if not clearly malicious:
        - Unusually high transaction volume for a merchant compared to their normal rate
        - A merchant suddenly active after a long period of inactivity
        - High failure rates on disbursements (potential probing or bad actor testing)
        - New merchants with unusually high first-day volumes
        - Identical amounts sent to the same recipient multiple times in short succession
        - Any admin/portal activity outside normal business hours
        - Wallets with very large balances that haven't transacted in > 30 days (sitting targets)
        Include these in the alert under an "Interesting Observations" section.

        **Step 4 — Case management.**
        Create new cases for new patterns, update existing, resolve stopped ones.

        **Step 5 — Send alert (only when warranted).**
        Call send_alert ONLY if one or more of the following is true:
        - Severity is Warning or Critical (one or more fraud findings this run)
        - There are open cases that require attention (even if this run was clean)
        - A previously open case has just been resolved (notify that it cleared)

        Do NOT send an alert if the run is fully clean with no findings and no open cases — just finish silently.

        When you do send an alert, you MUST follow the exact report structure defined in the send_alert tool
        description. Do not invent your own format. Do not skip sections.
        Use "None this run." / "No open cases." / "No actions required." for empty sections.
        All timestamps must be in CAT (UTC+2). All ZMW amounts must use comma separators.

        ## Tone Guidelines
        - Findings are observations, not verdicts. Use "appears to", "may indicate", "pattern suggests".
        - Never state that fraud has definitely occurred — you are flagging anomalies for human review.
        - Recommended Actions must be framed as suggestions: "consider", "may be worth", "if confirmed".
        - A finding can be suspicious without being confirmed fraud — label it clearly.
        - The reader will decide what action to take. Your job is to surface patterns accurately.

        ## Query Guidelines
        - Database is always **lipila_blaze** — never query any other database
        - Always qualify: `lipila_blaze.<table>`
        - Time filter: `created_at >= now() - INTERVAL {lookbackMinutes} MINUTE`
        - Max 50 rows per query
        - ClickHouse uses single quotes for strings
        - On query error: check DESCRIBE output and retry with correct column names
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
