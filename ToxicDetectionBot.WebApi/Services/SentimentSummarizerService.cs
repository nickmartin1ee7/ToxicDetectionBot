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

        _logger.LogDebug("Starting sentiment summarization...");

        var unsummarizedSentiments = await dbContext.UserSentiments
            .Where(us => !us.IsSummarized)
            .ToListAsync()
            .ConfigureAwait(false);

        if (unsummarizedSentiments.Count == 0)
        {
            _logger.LogDebug("No unsummarized sentiments found");
            return;
        }

        var userGroups = unsummarizedSentiments.GroupBy(us => us.UserId);
        var totalToxicMessages = 0;
        var totalNonToxicMessages = 0;

        foreach (var userGroup in userGroups)
        {
            var userId = userGroup.Key;
            var sentiments = userGroup.ToList();

            var messageCount = sentiments.Count;
            var toxicMessages = sentiments.Count(s => s.IsToxic);
            var nonToxicMessages = messageCount - toxicMessages;

            totalToxicMessages += toxicMessages;
            totalNonToxicMessages += nonToxicMessages;

            var existingScore = await dbContext.UserSentimentScores
                .FirstOrDefaultAsync(s => s.UserId == userId)
                .ConfigureAwait(false);

            if (existingScore is not null)
            {
                existingScore.TotalMessages += messageCount;
                existingScore.ToxicMessages += toxicMessages;
                existingScore.NonToxicMessages += nonToxicMessages;
                existingScore.ToxicityPercentage = existingScore.TotalMessages > 0
                    ? (double)existingScore.ToxicMessages / existingScore.TotalMessages * 100
                    : 0;
                existingScore.SummarizedAt = DateTime.UtcNow;
            }
            else
            {
                var toxicityPercentage = messageCount > 0
                    ? (double)toxicMessages / messageCount * 100
                    : 0;

                var sentimentScore = new UserSentimentScore
                {
                    UserId = userId,
                    TotalMessages = messageCount,
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
        }

        await dbContext.SaveChangesAsync()
            .ConfigureAwait(false);

        var totalMessages = totalToxicMessages + totalNonToxicMessages;
        var overallToxicityPercentage = totalMessages > 0
            ? (double)totalToxicMessages / totalMessages * 100
            : 0;

        _logger.LogInformation(
            "Sentiment summarization completed. Processed {UserCount} users with {TotalMessages} messages ({ToxicMessages} toxic, {NonToxicMessages} non-toxic, {ToxicityPercentage:F2}% toxic overall)",
            userGroups.Count(),
            totalMessages,
            totalToxicMessages,
            totalNonToxicMessages,
            overallToxicityPercentage);
    }
}
