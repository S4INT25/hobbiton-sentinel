using System.Threading.Channels;
using Sentinel.Admin.Stores;

namespace Sentinel.Admin;

public class AnalyticsQueryWorker(
    IServiceScopeFactory scopeFactory,
    IAnalyticsJobStore jobStore,
    IAnalyticsChatStore chatStore,
    ILogger<AnalyticsQueryWorker> logger) : BackgroundService
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

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
                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error processing job {JobId}", jobId);
            }
        }
    }

    private async Task ProcessJobAsync(string jobId, CancellationToken ct)
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
            // Load conversation history for context
            List<ChatEntry>? history = null;
            AnalyticsConversation? conversation = null;

            if (!string.IsNullOrEmpty(job.ConversationId))
            {
                conversation = await chatStore.GetConversationAsync(job.UserId, job.ConversationId);
                history = conversation?.Messages;
            }

            // Create a scoped AnalyticsAgent
            using var scope = scopeFactory.CreateScope();
            var agent = scope.ServiceProvider.GetRequiredService<AnalyticsAgent>();

            var answer = await agent.AskAsync(job.Prompt, job.Database, history);

            job.Status = "completed";
            job.CompletedAt = DateTime.UtcNow;
            job.Result = answer;
            await jobStore.UpdateAsync(job);

            // Append to conversation
            if (conversation == null)
            {
                conversation = new AnalyticsConversation
                {
                    Id = job.ConversationId,
                    Database = job.Database,
                    UserId = job.UserId,
                    Title = GenerateTitle(job.Prompt)
                };
            }

            conversation.Messages.Add(new ChatEntry { Role = "user", Content = job.Prompt });
            conversation.Messages.Add(new ChatEntry { Role = "assistant", Content = answer });

            if (conversation.Title == "New Conversation" && conversation.Messages.Count >= 2)
                conversation.Title = GenerateTitle(job.Prompt);

            await chatStore.SaveConversationAsync(conversation);

            logger.LogInformation("Job {JobId} completed", jobId);
        }
        catch (Exception ex)
        {
            job.Status = "failed";
            job.CompletedAt = DateTime.UtcNow;
            job.Error = ex.Message;
            await jobStore.UpdateAsync(job);
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
