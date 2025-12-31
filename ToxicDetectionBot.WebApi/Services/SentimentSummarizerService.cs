using Microsoft.EntityFrameworkCore;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services;

public interface ISentimentSummarizerService
{
    Task SummarizeUserSentiments();
}

public class SentimentSummarizerService : ISentimentSummarizerService
{
    private readonly ILogger<SentimentSummarizerService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public SentimentSummarizerService(
        ILogger<SentimentSummarizerService> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task SummarizeUserSentiments()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _logger.LogInformation("Starting sentiment summarization...");

        var unsummarizedSentiments = await dbContext.UserSentiments
            .Where(us => !us.IsSummarized)
            .ToListAsync();

        if (unsummarizedSentiments.Count == 0)
        {
            _logger.LogInformation("No unsummarized sentiments found");
            return;
        }

        var userGroups = unsummarizedSentiments.GroupBy(us => us.UserId);

        foreach (var userGroup in userGroups)
        {
            var userId = userGroup.Key;
            var sentiments = userGroup.ToList();

            var totalMessages = sentiments.Count;
            var toxicMessages = sentiments.Count(s => s.IsToxic);
            var nonToxicMessages = totalMessages - toxicMessages;

            var existingScore = await dbContext.UserSentimentScores
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (existingScore != null)
            {
                existingScore.TotalMessages += totalMessages;
                existingScore.ToxicMessages += toxicMessages;
                existingScore.NonToxicMessages += nonToxicMessages;
                existingScore.ToxicityPercentage = existingScore.TotalMessages > 0
                    ? (double)existingScore.ToxicMessages / existingScore.TotalMessages * 100
                    : 0;
                existingScore.SummarizedAt = DateTime.UtcNow;
            }
            else
            {
                var toxicityPercentage = totalMessages > 0
                    ? (double)toxicMessages / totalMessages * 100
                    : 0;

                var sentimentScore = new UserSentimentScore
                {
                    UserId = userId,
                    TotalMessages = totalMessages,
                    ToxicMessages = toxicMessages,
                    NonToxicMessages = nonToxicMessages,
                    ToxicityPercentage = toxicityPercentage
                };

                dbContext.UserSentimentScores.Add(sentimentScore);
            }

            foreach (var sentiment in sentiments)
            {
                sentiment.IsSummarized = true;
            }

            _logger.LogInformation(
                "Summarized sentiments for user {UserId}: {TotalMessages} total, {ToxicMessages} toxic ({ToxicityPercentage:F2}%)",
                userId,
                totalMessages,
                toxicMessages,
                existingScore?.ToxicityPercentage ?? (totalMessages > 0 ? (double)toxicMessages / totalMessages * 100 : 0));
        }

        await dbContext.SaveChangesAsync();

        _logger.LogInformation("Sentiment summarization completed. Processed {UserCount} users with {TotalMessages} messages",
            userGroups.Count(),
            unsummarizedSentiments.Count);
    }
}
