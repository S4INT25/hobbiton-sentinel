using System.Text.Json;
using FraudDetector.Infrastructure;
using FraudDetector.Memory;
using OpenAI;
using OpenAI.Chat;

namespace FraudDetector.Agent;

public class FraudAgent(
    OpenAIClient ai,
    ClickHouseClient ch,
    EmailClient email,
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

        ## Payment Gateway Fraud Patterns

        {FraudPatternRegistry.ToPromptBlock()}

        ## Your Memory — Open Cases

        You have {casesContext}

        {openCasesSummary}

        ## Your Task This Run

        Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC (Zambia = UTC+2)
        Look back: {lookbackMinutes} minutes

        **Step 0 — Discover schema.** SHOW DATABASES → SHOW TABLES → DESCRIBE key tables.

        **Step 1 — Follow up on open cases.**
        Run follow-up queries for each open case. Escalate if worsening, watching if stable, resolve if stopped.

        **Step 2 — Fresh investigation.**
        Check all {FraudPatternRegistry.GetEnabled().Count()} patterns above against recent data using actual column names from discovery.

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

        **Step 5 — Send alert.**
        Always call send_alert at the end. Even if nothing suspicious, send a clean report.
        You MUST follow the exact report structure defined in the send_alert tool description — every section,
        every table, every run. Do not invent your own format. Do not skip sections.
        Use "None this run." / "No open cases." / "No actions required." for empty sections.
        All timestamps must be in CAT (UTC+2). All ZMW amounts must use comma separators.

        ## Tone Guidelines
        - Findings are observations, not verdicts. Use "appears to", "may indicate", "pattern suggests".
        - Never state that fraud has definitely occurred — you are flagging anomalies for human review.
        - Recommended Actions must be framed as suggestions: "consider", "may be worth", "if confirmed".
        - A finding can be suspicious without being confirmed fraud — label it clearly.
        - The reader will decide what action to take. Your job is to surface patterns accurately.

        ## Query Guidelines
        - Always qualify: `<database>.<table>`
        - Time filter: `created_at >= now() - INTERVAL {lookbackMinutes} MINUTE`
        - Max 50 rows per query
        - ClickHouse uses single quotes for strings
        - On query error: check DESCRIBE output and retry with correct column names
        """;
    }

    public async Task RunAsync()
    {
        var runId = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var lookback = config.GetValue("FraudDetector:LookbackMinutes", 70);
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
        var maxIterations = config.GetValue("FraudDetector:MaxIterations", 60);

        while (iteration++ < maxIterations)
        {
            var options = new ChatCompletionOptions { MaxOutputTokenCount = 4096, Temperature = 0.1f };
            foreach (var tool in tools) options.Tools.Add(tool);

            var response = await chatClient.CompleteChatAsync(messages, options);

            var completion = response.Value;
            messages.Add(new AssistantChatMessage(completion));

            logger.LogInformation("Iteration {N}: {Reason}, {ToolCount} tool calls",
                iteration, completion.FinishReason, completion.ToolCalls.Count);

            if (completion.FinishReason == ChatFinishReason.Stop)
                break;

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                foreach (var toolCall in completion.ToolCalls)
                {
                    logger.LogInformation("Tool: {Name}", toolCall.FunctionName);
                    var result = await ExecuteToolAsync(toolCall, runId);
                    messages.Add(new ToolChatMessage(toolCall.Id, result));
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
                "Based only on what you have already found in this run, call send_alert now with " +
                "a complete report using the EXACT structure defined in the send_alert tool: " +
                "Run Summary table, Fraud Findings, Interesting Observations, Open Cases, Recommended Actions. " +
                "Follow every formatting rule. Mark severity accurately. This is the final report for this run."));

            foreach (var tool in tools) options.Tools.Add(tool);
            var summaryResponse = await chatClient.CompleteChatAsync(messages, options);
            var summaryCompletion = summaryResponse.Value;
            messages.Add(new AssistantChatMessage(summaryCompletion));

            // Execute any tool calls (should be send_alert)
            foreach (var toolCall in summaryCompletion.ToolCalls)
            {
                var result = await ExecuteToolAsync(toolCall, runId);
                logger.LogInformation("Max-iteration summary tool: {Tool} → {Result}", toolCall.FunctionName, result);
            }

            // Fallback if agent still didn't send an alert
            if (summaryCompletion.ToolCalls.All(t => t.FunctionName != "send_alert"))
            {
                await email.SendAsync(
                    "Fraud Detector: Partial Run Summary",
                    $"Run {runId} reached the iteration limit ({maxIterations} steps). " +
                    "The investigation was incomplete. Review Hangfire logs for partial findings.",
                    "warning");
            }
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
            fraudCase.AffectedEntities = entities.EnumerateArray()
                .Select(e => e.GetString()!).ToList();

        if (root.TryGetProperty("follow_up_queries", out var queries))
            fraudCase.FollowUpQueries = queries.EnumerateArray()
                .Select(q => q.GetString()!).ToList();

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
            fraudCase.FollowUpQueries = queries.EnumerateArray()
                .Select(q => q.GetString()!).ToList();

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
