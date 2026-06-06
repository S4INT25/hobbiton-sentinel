using System.Collections.Concurrent;
using System.Threading.Channels;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;
using Sentinel.Agent;

namespace Sentinel.Admin;

public class AnalyticsQueryWorker(
    IServiceScopeFactory scopeFactory,
    IAnalyticsJobStore jobStore,
    IAnalyticsChatStore chatStore,
    ILogger<AnalyticsQueryWorker> logger) : BackgroundService
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    // In-memory live streaming text per job — not persisted, just for real-time UI updates
    private readonly ConcurrentDictionary<string, string> _liveText = new();

    public string? GetLiveText(string jobId) =>
        _liveText.TryGetValue(jobId, out var t) ? t : null;

    public async ValueTask EnqueueAsync(string jobId)
    {
        await _channel.Writer.WriteAsync(jobId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AnalyticsQueryWorker started");

        await foreach (var jobId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(jobId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error processing job {JobId}", jobId);
            }
        }
    }

    private async Task ProcessJobAsync(string jobId)
    {
        var job = await jobStore.GetAsync(jobId);
        if (job == null)
        {
            logger.LogWarning("Job {JobId} not found", jobId);
            return;
        }

        job.Status = "running";
        await jobStore.UpdateAsync(job);
        logger.LogInformation("Job {JobId} running: {Prompt}", jobId, job.Prompt[..Math.Min(80, job.Prompt.Length)]);

        try
        {
            // Load conversation history for multi-turn context
            List<ChatEntry>? history = null;
            AnalyticsConversation? conversation = null;

            if (!string.IsNullOrEmpty(job.ConversationId))
            {
                conversation = await chatStore.GetConversationAsync(job.UserId, job.ConversationId);
                history = conversation?.Messages;
            }

            using var scope = scopeFactory.CreateScope();
            var agent = scope.ServiceProvider.GetRequiredService<ChatAnalyticsAgent>();

            // Load memories scoped to the selected database
            List<AgentMemory>? memories = null;
            var memoryStore = scope.ServiceProvider.GetService<IAgentMemoryStore>();
            if (memoryStore != null)
                memories = await memoryStore.GetEnabledAsync(job.Database);

            var result = await agent.AskAsync(job.Prompt, job.Database, history,
                memories: memories,
                onEvent: async evt =>
                {
                    if (evt.Type == "token")
                    {
                        // Token events update live text only — no cache write per token
                        _liveText[jobId] = evt.Message;
                        return;
                    }

                    job.StreamEvents.Add(evt);
                    await jobStore.UpdateAsync(job);
                });

            _liveText.TryRemove(jobId, out _);
            job.Status = "completed";
            job.CompletedAt = DateTime.UtcNow;
            job.Result = result;
            await jobStore.UpdateAsync(job);

            // Append to conversation
            if (conversation == null)
            {
                conversation = new AnalyticsConversation
                {
                    Id = job.ConversationId,
                    Database = job.Database,
                    Mode = job.Mode,
                    UserId = job.UserId,
                    Title = GenerateTitle(job.Prompt)
                };
            }
            else
            {
                conversation.Mode = string.IsNullOrWhiteSpace(job.Mode) ? conversation.Mode : job.Mode;
            }

            var latestUserMessage = conversation.Messages.LastOrDefault(m => m.Role == "user");
            if (!string.Equals(latestUserMessage?.Content, job.Prompt, StringComparison.Ordinal))
                conversation.Messages.Add(new ChatEntry { Role = "user", Content = job.Prompt });
            conversation.Messages.Add(new ChatEntry { Role = "assistant", Content = job.Prompt, Response = result });

            if (conversation.Title == "New Conversation" && conversation.Messages.Count >= 2)
                conversation.Title = GenerateTitle(job.Prompt);

            await chatStore.SaveConversationAsync(conversation);
            logger.LogInformation("Job {JobId} completed", jobId);
        }
        catch (Exception ex)
        {
            _liveText.TryRemove(jobId, out _);
            job.Status = "failed";
            job.CompletedAt = DateTime.UtcNow;
            job.Error = ex.Message;
            await jobStore.UpdateAsync(job);

            if (!string.IsNullOrWhiteSpace(job.ConversationId))
            {
                var conversation = await chatStore.GetConversationAsync(job.UserId, job.ConversationId)
                                   ?? new AnalyticsConversation
                                   {
                                       Id = job.ConversationId,
                                       Database = job.Database,
                                       Mode = string.IsNullOrWhiteSpace(job.Mode) ? "general" : job.Mode,
                                       UserId = job.UserId,
                                       Title = GenerateTitle(job.Prompt)
                                   };

                var latestUserMessage = conversation.Messages.LastOrDefault(m => m.Role == "user");
                if (!string.Equals(latestUserMessage?.Content, job.Prompt, StringComparison.Ordinal))
                    conversation.Messages.Add(new ChatEntry { Role = "user", Content = job.Prompt });

                conversation.Messages.Add(new ChatEntry
                {
                    Role = "assistant",
                    Content = job.Prompt,
                    Response = new AnalyticsResponse { Success = false, Error = ex.Message }
                });
                await chatStore.SaveConversationAsync(conversation);
            }

            logger.LogError(ex, "Job {JobId} failed", jobId);
        }
    }

    private static string GenerateTitle(string message)
    {
        var title = message.Trim();
        if (title.Length > 50)
            title = title[..47] + "...";
        return title;
    }
}