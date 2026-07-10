using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;
using Sentinel.Infrastructure;
using Sentinel.Memory;

namespace Sentinel.Agent;

public class FraudAgent(
    OpenAIClient ai,
    ClickHouseClient ch,
    EmailClient email,
    IpLookupClient ipLookup,
    ICaseStore caseStore,
    SchemaLoader schemaLoader,
    IFeedbackRuleStore feedbackRuleStore,
    IRunLogStore runLogStore,
    IFraudPatternStore fraudPatternStore,
    IEvidenceSourceStore evidenceSourceStore,
    IWorkflowStore workflowStore,
    ICaseOutcomeStore caseOutcomeStore,
    IConfiguration config,
    ILogger<FraudAgent> logger)
{
    private string BuildSystemPrompt(string openCasesSummary, int lookbackMinutes,
        string schemaBlock, string suppressionBlock, string database, string patternsBlock,
        string crossDbBlock, string learningsBlock, string? workflowSystemPrompt = null)
    {
        var casesContext = openCasesSummary == "No open cases."
            ? "no open cases."
            : "the following open cases from previous runs:";

        // Use workflow-specific preamble if provided, otherwise default Lipila context
        var preamble = !string.IsNullOrWhiteSpace(workflowSystemPrompt)
            ? workflowSystemPrompt
            : "You are a financial fraud analyst with read-only ClickHouse access to Lipila — a Zambian payment gateway (collections + disbursements via AirtelMoney/MTN). Disbursements are irreversible.";

        return $"""
                {preamble}

                ## ClickHouse SQL Rules (strict)
                Native ClickHouse only. Banned: `IIIF`, `IIF`, `NVL`, `ISNULL`, `COALESCE` (use `ifNull()`), `DATEDIFF`, `TOP`, `CHARINDEX`, `PATINDEX`, `COUNT(CASE WHEN ...)`.
                Use: `if()`, `multiIf()`, `countIf()`, `sumIf()`, `ifNull()`, `toStartOfHour()`, `now() - INTERVAL N DAY`, `positionCaseInsensitive()`, `match()`.
                If unsure a function exists — don't use it. Always qualify: `{database}.<table>`.

                {schemaBlock}

                {crossDbBlock}

                ## Betting Merchants
                Betting/gaming merchants legitimately disburse 50–200+/hr. Before flagging any velocity pattern, query the merchant's 30-day hourly disbursement history (`toStartOfHour`, `count()`, `INTERVAL 30 DAY`) and use their own avg/max as the baseline. Only flag if the current rate is a meaningful outlier.

                ## Fraud Patterns
                {patternsBlock}
                {suppressionBlock}
                {learningsBlock}
                ## Open Cases
                You have {casesContext}
                {openCasesSummary}

                ## This Run
                UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} (Zambia = UTC+2) | Lookback: {lookbackMinutes} min

                **Step 1 — Follow up open cases.** Re-query each; escalate/watch/resolve as appropriate.

                **Step 2 — Pattern scan.** Check all enabled patterns against data from the last {lookbackMinutes} minutes.

                **Step 2b — Activity log review.** Query user_activity_logs. Flag: datacenter/hosting logins, brute force (failed→success), midnight–5am CAT logins, sensitive actions (wallet/key/merchant edits), internal-IP actions with no portal login. Cross-reference suspicious logins against transactions from same IP/merchant/wallet.

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

                **Step 4 — Case management.**
                BEFORE creating any new case, you MUST call `check_history` with the affected entities and category.
                If history shows similar patterns were false positives, either skip the case or use a low confidence score and "watching" status.
                Always assign a confidence score (0-100%) based on: evidence strength, cross-DB corroboration, historical FP rate for this category/entity, and number of independent signals.
                Cases below 30% confidence should be observations, not cases. Cases at 30-50% should be "watching" status.

                **Step 5 — Alert.** Send alert ONLY if: severity Warning/Critical, open cases needing attention, or a case just resolved. Silent finish if fully clean with no open cases.
                Follow the send_alert tool format exactly. Empty sections → "None this run." / "No open cases." / "No actions required." Timestamps in CAT. Amounts in ZMW with comma separators.

                ## Tone
                Observations only — never verdicts. Use "appears to", "may indicate", "pattern suggests". Findings are for human review.

                ## Query Rules
                - Always qualify: `{database}.<table>`. Time filter: `created_at >= now() - INTERVAL {lookbackMinutes} MINUTE`. Max 50 rows per query. Single quotes for strings. On error: re-DESCRIBE and retry.
                - **ALWAYS use the `queries` array — even for a single query.** Never call `run_sql` more than once in a row when the queries are independent. Batch everything you need into one call. This is mandatory, not optional.
                """;
    }

    private static string BuildPatternsBlock(List<FraudPatternEntity> patterns)
    {
        var sb = new StringBuilder();
        foreach (var p in patterns)
        {
            sb.AppendLine($"        {p.Id}. **{p.Name}**");
            foreach (var line in p.Description.Trim().Split('\n'))
                sb.AppendLine($"           {line.Trim()}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildCrossDbBlock(List<EvidenceSource> sources)
    {
        if (sources.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("## Cross-Database Evidence Sources");
        sb.AppendLine();
        sb.AppendLine(
            "Some Lipila merchants are part of the Hobbiton organisation and share the same ClickHouse cluster.");
        sb.AppendLine(
            "When investigating these merchants, you MUST cross-reference the linked database for corroboration.");
        sb.AppendLine("This gives you a wider view of user behaviour beyond what Lipila alone can show.");
        sb.AppendLine();

        foreach (var source in sources)
        {
            sb.AppendLine($"---");
            sb.AppendLine();
            sb.AppendLine($"### {source.Name}");
            sb.AppendLine($"- **Evidence database:** `{source.EvidenceDatabase}`");

            if (!string.IsNullOrWhiteSpace(source.LipilaMerchantIds))
                sb.AppendLine($"- **Lipila merchant IDs:** {source.LipilaMerchantIds}");
            if (source.LipilaPartnerId > 0)
                sb.AppendLine($"- **Lipila partner_id:** {source.LipilaPartnerId}");

            sb.AppendLine();
            sb.AppendLine("**Join mappings:**");
            sb.AppendLine($"```json");
            sb.AppendLine(source.JoinMappings.Trim());
            sb.AppendLine($"```");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(source.TableDescriptions))
            {
                sb.AppendLine("**Available tables:**");
                sb.AppendLine();
                sb.AppendLine(source.TableDescriptions.Trim());
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(source.EvidenceChecks) && source.EvidenceChecks.Trim() != "[]")
            {
                sb.AppendLine("**Evidence checks to run when these merchants are flagged:**");
                // Parse the JSON array into a numbered list
                try
                {
                    var checks = JsonSerializer.Deserialize<List<string>>(source.EvidenceChecks);
                    if (checks is not null)
                    {
                        for (var i = 0; i < checks.Count; i++)
                            sb.AppendLine($"{i + 1}. {checks[i]}");
                    }
                }
                catch
                {
                    sb.AppendLine(source.EvidenceChecks.Trim());
                }

                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(source.Notes))
            {
                sb.AppendLine("**Notes:**");
                sb.AppendLine(source.Notes.Trim());
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Rules for ALL cross-DB evidence");
        sb.AppendLine("- Use ONLY for corroboration — never as a primary detection source");
        sb.AppendLine("- Always fully qualify tables: `<database>.<table>`");
        sb.AppendLine(
            "- If evidence supports a Lipila finding, note \"Corroborated by <db> data\" and increase confidence");
        sb.AppendLine("- If evidence contradicts (e.g. account has legitimate history), note it as reducing suspicion");
        sb.AppendLine("- Timing tolerance: ±5 minutes when matching transactions across databases");
        sb.AppendLine(
            "- Do NOT run cross-DB checks on every account — only on accounts already flagged by Lipila detection");

        return sb.ToString();
    }

    public async Task RunAsync(FraudAgentRunRequest request, CancellationToken cancellationToken = default)
    {
        var currentRunId = string.IsNullOrWhiteSpace(request.RunId)
            ? DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")
            : request.RunId;
        var effectiveDatabase = string.IsNullOrWhiteSpace(request.Database) ? "lipila_blaze" : request.Database.Trim();
        var runStartedAt = DateTime.UtcNow;
        var lookback = config.GetValue("Sentinel:LookbackMinutes", 70);
        var modelName = config["DigitalOcean:ModelName"]!;

        logger.LogInformation(
            "Fraud agent run {RunId} started (triggered by: {TriggeredBy}, db: {Database}, customPrompt: {HasCustomPrompt})",
            currentRunId, request.TriggeredBy, effectiveDatabase, !string.IsNullOrWhiteSpace(request.CustomPrompt));

        // Load open cases, schema, feedback rules, and historical learnings concurrently
        // Note: patternsTask and evidenceSourcesTask share a DbContext so must not overlap
        var openCasesSummaryTask = caseStore.GetOpenCasesSummaryForWorkflowAsync(request.WorkflowId);
        var openCasesTask = !string.IsNullOrWhiteSpace(request.WorkflowId)
            ? caseStore.GetOpenCasesForWorkflowAsync(request.WorkflowId)
            : caseStore.GetOpenCasesAsync();
        var schemaBlockTask = schemaLoader.GetSchemaBlockAsync(effectiveDatabase);
        var rulesTask = feedbackRuleStore.GetActiveRulesAsync();
        var learningsTask = caseOutcomeStore.GetLearningsSummaryAsync(effectiveDatabase);

        await Task.WhenAll(openCasesSummaryTask, openCasesTask, schemaBlockTask, rulesTask, learningsTask);

        var openCasesSummary = await openCasesSummaryTask;
        var openCases = await openCasesTask;
        var schemaBlock = await schemaBlockTask;
        var activeRules = await rulesTask;
        var learningsBlock = await learningsTask;
        var enabledPatterns = !string.IsNullOrWhiteSpace(request.WorkflowId)
            ? await fraudPatternStore.GetEnabledForWorkflowAsync(request.WorkflowId)
            : await fraudPatternStore.GetEnabledAsync();
        var evidenceSources = !string.IsNullOrWhiteSpace(request.WorkflowId)
            ? await evidenceSourceStore.GetEnabledForWorkflowAsync(request.WorkflowId)
            : await evidenceSourceStore.GetEnabledAsync();

        logger.LogInformation(
            "Loaded {Count} open cases, {RuleCount} suppression rules, {EvidenceCount} evidence sources",
            openCases.Count, activeRules.Count, evidenceSources.Count);

        // Build suppression block from active rules
        var suppressionBlock = activeRules.Count > 0
            ? "\n## Known-Good Exceptions (Analyst Verified)\n" +
              "Do NOT flag these as suspicious. They have been reviewed and confirmed legitimate:\n" +
              string.Join("\n", activeRules.Select(r =>
                  $"- [{r.RuleType}] {r.MatchValue} → {r.Action} | Reason: {r.Reason}"))
              + "\n\n"
            : "\n";

        // Build patterns prompt block from store
        var patternsBlock = BuildPatternsBlock(enabledPatterns);

        // Build cross-database evidence block from store
        var crossDbBlock = BuildCrossDbBlock(evidenceSources);

        // Load workflow-specific system prompt if available
        string? workflowSystemPrompt = null;
        if (!string.IsNullOrWhiteSpace(request.WorkflowId))
        {
            var workflow = await workflowStore.GetByIdAsync(request.WorkflowId);
            if (workflow != null && !string.IsNullOrWhiteSpace(workflow.SystemPrompt))
                workflowSystemPrompt = workflow.SystemPrompt;
        }

        var systemPrompt = BuildSystemPrompt(openCasesSummary, lookback, schemaBlock, suppressionBlock,
            effectiveDatabase, patternsBlock, crossDbBlock, learningsBlock, workflowSystemPrompt);

        // Inject follow-up queries from open cases into the first user message
        var followUpContext = openCases.Count > 0 && openCases.Any(c => c.FollowUpQueries.Count > 0)
            ? "\n\nSuggested follow-up queries from previous runs:\n" +
              string.Join("\n", openCases
                  .Where(c => c.FollowUpQueries.Count > 0)
                  .SelectMany(c => c.FollowUpQueries.Select(q => $"-- Case {c.Id}: {c.Title}\n{q}")))
            : "";

        var customPromptBlock = string.IsNullOrWhiteSpace(request.CustomPrompt)
            ? ""
            : $"\n\nAdmin custom instructions for this run:\n{request.CustomPrompt.Trim()}";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(
                $"Run ID: {currentRunId}. Database: {effectiveDatabase}. Start your investigation now.{followUpContext}{customPromptBlock}")
        };

        var tools = FraudAgentTools.GetToolDefinitions();
        var chatClient = ai.GetChatClient(modelName);
        var iteration = 0;
        var maxIterations = config.GetValue("Sentinel:MaxIterations", 60);
        var alertSent = false;
        string? alertSubject = null;
        string? alertBody = null;
        var casesCreated = 0;
        var casesResolved = 0;
        var earlyWarningSent = false;
        var earlyWarningThreshold = (int)(maxIterations * 0.75);
        var totalInputTokens = 0;
        var totalOutputTokens = 0;

        var compactionInterval = config.GetValue("Sentinel:CompactionInterval", 10);
        var maxToolResultLength = config.GetValue("Sentinel:MaxToolResultLength", 4000);

        while (iteration++ < maxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Compact old tool results to reduce input tokens.
            // Every N iterations, truncate tool results from earlier iterations to keep
            // context lean. The system prompt + recent results stay full.
            if (iteration > 1 && iteration % compactionInterval == 0)
            {
                CompactConversation(messages, maxToolResultLength);
            }

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 4096,
                Temperature = 0.1f,
            };
#pragma warning disable SCME0001
            options.Patch.Set("$.thinking"u8, BinaryData.FromObjectAsJson(new { type = "disabled" }));
#pragma warning restore SCME0001

            foreach (var tool in tools) options.Tools.Add(tool);

            // Early warning at 75% — give the agent a chance to wrap up gracefully
            if (!earlyWarningSent && iteration >= earlyWarningThreshold)
            {
                earlyWarningSent = true;
                logger.LogWarning("[Run:{RunId}] Approaching iteration limit ({N}/{Max}) — injecting wrap-up warning",
                    currentRunId, iteration, maxIterations);
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
            totalInputTokens += inputTokens;
            totalOutputTokens += outputTokens;

            logger.LogInformation(
                "[Run:{RunId}] Iteration {N}: finish={Reason} tools={ToolCount} tokens={Total} (in={In} out={Out})",
                currentRunId, iteration, completion.FinishReason, completion.ToolCalls.Count, totalTokens, inputTokens,
                outputTokens);

            var textContent = string.Concat((completion.Content ?? [])
                .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                .Select(p => p.Text));

            if (!string.IsNullOrWhiteSpace(textContent))
            {
                logger.LogInformation("[Run:{RunId}] LLM text: {Text}", currentRunId, textContent);
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
                        currentRunId, toolCall.FunctionName, argsPreview);

                    // Gate: suppress email on clean runs regardless of what the LLM decides
                    var gatedCleanRun = toolCall.FunctionName == "send_alert"
                                        && casesCreated == 0 && casesResolved == 0;

                    var toolStart = Stopwatch.GetTimestamp();
                    var result = gatedCleanRun
                        ? "Alert skipped — clean run, no actionable findings."
                        : await ExecuteToolAsync(toolCall, currentRunId, request.WorkflowId);
                    var durationMs = (int)Stopwatch.GetElapsedTime(toolStart).TotalMilliseconds;

                    if (gatedCleanRun)
                        logger.LogInformation("[Run:{RunId}] send_alert suppressed — 0 cases created/resolved",
                            currentRunId);

                    // Log to ClickHouse (await to prevent scope disposal issues)
                    await runLogStore.LogToolCallAsync(new RunLog
                    {
                        RunId = currentRunId,
                        Iteration = (ushort)iteration,
                        ToolName = toolCall.FunctionName,
                        Args = args,
                        Result = result.Length > 10_000 ? result[..10_000] : result,
                        StartedAt = DateTime.UtcNow.AddMilliseconds(-durationMs),
                        DurationMs = (uint)durationMs
                    });

                    logger.LogInformation("[Run:{RunId}] Tool result: {Tool} ({Ms}ms)", currentRunId,
                        toolCall.FunctionName,
                        durationMs);

                    // Track case counters for the gate and RunSummary
                    if (toolCall.FunctionName == "create_case") casesCreated++;
                    else if (toolCall.FunctionName == "resolve_case") casesResolved++;

                    // Cap tool result size in the conversation to limit input tokens
                    var contextResult = result.Length > 8000
                        ? result[..8000] + $"\n\n[… result truncated from {result.Length:N0} chars for context]"
                        : result;
                    messages.Add(new ToolChatMessage(toolCall.Id, contextResult));
                    if (toolCall.FunctionName == "send_alert" && !gatedCleanRun)
                    {
                        alertSent = true;
                        try
                        {
                            using var alertDoc = JsonDocument.Parse(toolCall.FunctionArguments);
                            var alertRoot = alertDoc.RootElement;
                            alertSubject = alertRoot.TryGetProperty("subject", out var subj) ? subj.GetString() : null;
                            alertBody = alertRoot.TryGetProperty("body", out var bod) ? bod.GetString() : null;
                        }
                        catch
                        {
                            /* best effort */
                        }
                    }
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
            totalInputTokens += summaryCompletion.Usage?.InputTokenCount ?? 0;
            totalOutputTokens += summaryCompletion.Usage?.OutputTokenCount ?? 0;
            messages.Add(new AssistantChatMessage(summaryCompletion));

            foreach (var toolCall in summaryCompletion.ToolCalls)
            {
                var gatedCleanRun = toolCall.FunctionName == "send_alert"
                                    && casesCreated == 0 && casesResolved == 0;
                var result = gatedCleanRun
                    ? "Alert skipped — clean run, no actionable findings."
                    : await ExecuteToolAsync(toolCall, currentRunId, request.WorkflowId);
                logger.LogInformation("Max-iteration summary tool: {Tool} → {Result}", toolCall.FunctionName, result);
                if (toolCall.FunctionName == "send_alert" && !gatedCleanRun)
                {
                    alertSent = true;
                    try
                    {
                        using var alertDoc = JsonDocument.Parse(toolCall.FunctionArguments);
                        var alertRoot = alertDoc.RootElement;
                        alertSubject = alertRoot.TryGetProperty("subject", out var subj) ? subj.GetString() : null;
                        alertBody = alertRoot.TryGetProperty("body", out var bod) ? bod.GetString() : null;
                    }
                    catch
                    {
                        /* best effort */
                    }

                    break;
                }
            }

            // Only send the fallback email if the run was genuinely incomplete (no alert and hit the ceiling)
            if (!alertSent)
            {
                logger.LogWarning("Run {RunId} hit iteration limit with no alert sent — sending ops notification",
                    currentRunId);
                await email.SendAsync(
                    "Fraud Detector: Run Incomplete",
                    $"Run {currentRunId} reached the iteration limit ({maxIterations} steps) without completing. " +
                    "Review Hangfire logs for partial findings.",
                    "warning",
                    senderName: "Sentinel", subjectPrefix: "[SENTINEL]");
            }
        }
        else if (!alertSent)
        {
            logger.LogInformation("Run {RunId} completed cleanly — no alert warranted", currentRunId);
        }

        logger.LogInformation(
            "[Run:{RunId}] Completed — {N} iterations | tokens: {TotalTokens} total (in={In} out={Out})",
            currentRunId, iteration, totalInputTokens + totalOutputTokens, totalInputTokens, totalOutputTokens);

        // Persist run summary before the job scope is disposed.
        await runLogStore.SaveSummaryAsync(new RunSummary
        {
            RunId = currentRunId,
            StartedAt = runStartedAt,
            FinishedAt = DateTime.UtcNow,
            Iterations = (ushort)iteration,
            InputTokens = (uint)totalInputTokens,
            OutputTokens = (uint)totalOutputTokens,
            CasesCreated = (ushort)casesCreated,
            CasesResolved = (ushort)casesResolved,
            AlertsSent = (ushort)(alertSent ? 1 : 0),
            Status = iteration >= maxIterations ? "max_iterations" : "completed",
            TriggeredBy = request.TriggeredBy,
            EmailSubject = alertSubject,
            EmailBody = alertBody
        });
    }

    /// <summary>
    /// Truncate old tool results in the conversation to reduce input tokens.
    /// Keeps the system prompt and the most recent messages intact.
    /// Old ToolChatMessage results are shortened to a brief summary.
    /// </summary>
    private void CompactConversation(List<ChatMessage> messages, int maxResultLength)
    {
        // Keep the first 2 messages (system + user prompt) and last 6 messages intact
        const int keepTailCount = 6;
        var compactUpTo = Math.Max(2, messages.Count - keepTailCount);
        var compacted = 0;

        for (var i = 2; i < compactUpTo; i++)
        {
            if (messages[i] is not ToolChatMessage toolMsg) continue;

            // Access the content text — if already short, skip
            var content = toolMsg.Content?.FirstOrDefault()?.Text;
            if (content == null || content.Length <= maxResultLength) continue;

            // Replace with a truncated version
            var truncated = content[..maxResultLength] +
                            $"\n\n[… truncated from {content.Length:N0} to {maxResultLength:N0} chars to save tokens]";
            messages[i] = new ToolChatMessage(toolMsg.ToolCallId, truncated);
            compacted++;
        }

        if (compacted > 0)
            logger.LogInformation("Compacted {Count} old tool results to reduce input tokens", compacted);
    }

    private async Task<string> ExecuteToolAsync(ChatToolCall toolCall, string runId, string? workflowId = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolCall.FunctionArguments);
            var root = doc.RootElement;

            return toolCall.FunctionName switch
            {
                "run_sql" => await ExecuteSqlAsync(root),
                "check_history" => await CheckHistoryAsync(root),
                "create_case" => await CreateCaseAsync(root, runId, workflowId),
                "update_case" => await UpdateCaseAsync(root, runId),
                "resolve_case" => await ResolveCaseAsync(root),
                "send_alert" => await email.SendAsync(
                    root.GetProperty("subject").GetString()!,
                    root.GetProperty("body").GetString()!,
                    root.TryGetProperty("severity", out var sev) ? sev.GetString()! : "watching",
                    senderName: "Sentinel", subjectPrefix: "[SENTINEL]"),
                "lookup_ip" => await ipLookup.LookupAsync(
                    JsonHelpers.ToIpList(root.GetProperty("ips"))),
                "describe_table" => await schemaLoader.DescribeTableAsync(
                    root.GetProperty("database").GetString()!,
                    root.GetProperty("table").GetString()!),
                "get_current_time" => $"UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} ({DateTime.UtcNow:dddd})\n" +
                                      $"Server local: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({DateTime.Now:dddd}, {TimeZoneInfo.Local.DisplayName})",
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

    private async Task<string> CheckHistoryAsync(JsonElement root)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Historical Case Outcomes ===");

        var anyResults = false;

        // Check by entities
        if (root.TryGetProperty("entities", out var entitiesEl))
        {
            var entities = JsonHelpers.ToStringList(entitiesEl);
            if (entities.Count > 0)
            {
                var byEntity = await caseOutcomeStore.FindByEntitiesAsync(entities);
                if (byEntity.Count > 0)
                {
                    anyResults = true;
                    sb.AppendLine($"\nFound {byEntity.Count} past cases involving these entities:");
                    foreach (var o in byEntity)
                    {
                        sb.AppendLine(
                            $"- [{o.Outcome.ToUpper()}] Case {o.CaseId}: \"{o.Title}\" (confidence: {o.Confidence}%, category: {o.Category})");
                        sb.AppendLine($"  Entities: {o.AffectedEntities}");
                        sb.AppendLine(
                            $"  Resolution: {o.Resolution ?? "N/A"} | Resolved by: {o.ResolvedBy} | Date: {o.ResolvedAt:yyyy-MM-dd}");
                    }
                }
            }
        }

        // Check by category
        if (root.TryGetProperty("category", out var catEl))
        {
            var category = catEl.GetString()!;
            var byCategory = await caseOutcomeStore.FindByCategoryAsync(category, 10);
            if (byCategory.Count > 0)
            {
                anyResults = true;
                var fp = byCategory.Count(o => o.Outcome == "false_positive");
                var confirmed = byCategory.Count(o => o.Outcome == "confirmed_fraud");
                sb.AppendLine(
                    $"\nCategory '{category}' history: {byCategory.Count} cases — {confirmed} confirmed fraud, {fp} false positives ({(byCategory.Count > 0 ? fp * 100 / byCategory.Count : 0)}% FP rate)");
            }
        }

        // Check by pattern
        if (root.TryGetProperty("pattern_id", out var patEl))
        {
            var patternId = patEl.GetInt32();
            var byPattern = await caseOutcomeStore.FindByPatternAsync(patternId, 10);
            if (byPattern.Count > 0)
            {
                anyResults = true;
                var fp = byPattern.Count(o => o.Outcome == "false_positive");
                var confirmed = byPattern.Count(o => o.Outcome == "confirmed_fraud");
                sb.AppendLine(
                    $"\nPattern #{patternId} history: {byPattern.Count} cases — {confirmed} confirmed fraud, {fp} false positives ({(byPattern.Count > 0 ? fp * 100 / byPattern.Count : 0)}% FP rate)");
            }
        }

        if (!anyResults)
            sb.AppendLine("No prior cases found for these entities/category/pattern. This is a new signal.");

        return sb.ToString();
    }

    private async Task<string> CreateCaseAsync(JsonElement root, string runId, string? workflowId = null)
    {
        var confidence = root.TryGetProperty("confidence", out var confEl) ? confEl.GetInt32() : 50;

        var fraudCase = new FraudCase
        {
            Title = root.GetProperty("title").GetString()!,
            Category = root.GetProperty("category").GetString()!,
            Severity = root.GetProperty("severity").GetString()!,
            Notes = root.GetProperty("notes").GetString()!,
            Confidence = confidence,
            // Auto-set status based on confidence
            Status = confidence >= 70 ? "open" : "watching",
            WorkflowId = workflowId
        };

        if (root.TryGetProperty("affected_entities", out var entities))
            fraudCase.AffectedEntities = JsonHelpers.ToStringList(entities);

        if (root.TryGetProperty("follow_up_queries", out var queries))
            fraudCase.FollowUpQueries = JsonHelpers.ToStringList(queries);

        fraudCase.Evidence.Add(new CaseEvidence
        {
            RunId = runId,
            Summary = $"Case created in run {runId} (confidence: {confidence}%)",
            RawData = fraudCase.Notes[..Math.Min(500, fraudCase.Notes.Length)]
        });

        await caseStore.SaveCaseAsync(fraudCase);

        var statusNote = confidence < 70 ? " (auto-set to 'watching' due to confidence < 70%)" : "";
        return
            $"Case {fraudCase.Id} created: {fraudCase.Title} | Confidence: {confidence}% | Status: {fraudCase.Status}{statusNote}";
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

        // Load case before resolving to capture details for the outcome record
        var fraudCase = await caseStore.GetCaseAsync(caseId);
        await caseStore.ResolveCaseAsync(caseId, resolution);

        // Persist outcome to PostgreSQL for historical learning
        if (fraudCase != null)
        {
            await caseOutcomeStore.SaveAsync(new CaseOutcome
            {
                CaseId = caseId,
                Title = fraudCase.Title,
                Category = fraudCase.Category,
                Outcome = "inconclusive", // agent resolves are inconclusive by default; analysts set confirmed/FP
                OriginalSeverity = fraudCase.Severity,
                Confidence = fraudCase.Confidence,
                AffectedEntities = string.Join(", ", fraudCase.AffectedEntities),
                WorkflowId = fraudCase.WorkflowId,
                Resolution = resolution,
                ResolvedBy = "agent",
                OccurrenceCount = fraudCase.OccurrenceCount
            });
        }

        return $"Case {caseId} resolved: {resolution}";
    }
}