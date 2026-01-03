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
            .ToListAsync();

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

            // Update sentiment scores
            var existingScore = await dbContext.UserSentimentScores
                .FirstOrDefaultAsync(s => s.UserId == userId);

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

            // Update alignment scores
            var alignmentCounts = sentiments.GroupBy(s => s.Alignment)
                .ToDictionary(g => g.Key, g => g.Count());

            var existingAlignmentScore = await dbContext.UserAlignmentScores
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (existingAlignmentScore is not null)
            {
                // Increment counts for each alignment
                existingAlignmentScore.LawfulGoodCount += alignmentCounts.GetValueOrDefault(nameof(AlignmentType.LawfulGood), 0);
                existingAlignmentScore.NeutralGoodCount += alignmentCounts.GetValueOrDefault(nameof(AlignmentType.NeutralGood), 0);
                existingAlignmentScore.ChaoticGoodCount += alignmentCounts.GetValueOrDefault(nameof(AlignmentType.ChaoticGood), 0);
                existingAlignmentScore.LawfulNeutralCount += alignmentCounts.GetValueOrDefault(nameof(AlignmentType.LawfulNeutral), 0);
                existingAlignmentScore.TrueNeutralCount += alignmentCounts.GetValueOrDefault(nameof(AlignmentType.TrueNeutral), 0);
                existingAlignmentScore.ChaoticNeutralCount += alignmentCounts.GetValueOrDefault(nameof(AlignmentType.ChaoticNeutral), 0);
                existingAlignmentScore.LawfulEvilCount += alignmentCounts.GetValueOrDefault(nameof(AlignmentType.LawfulEvil), 0);
                existingAlignmentScore.NeutralEvilCount += alignmentCounts.GetValueOrDefault(nameof(AlignmentType.NeutralEvil), 0);
                existingAlignmentScore.ChaoticEvilCount += alignmentCounts.GetValueOrDefault(nameof(AlignmentType.ChaoticEvil), 0);
                existingAlignmentScore.DominantAlignment = GetDominantAlignment(existingAlignmentScore);
                existingAlignmentScore.SummarizedAt = DateTime.UtcNow;
            }
            else
            {
                var alignmentScore = new UserAlignmentScore
                {
                    UserId = userId,
                    LawfulGoodCount = alignmentCounts.GetValueOrDefault(nameof(AlignmentType.LawfulGood), 0),
                    NeutralGoodCount = alignmentCounts.GetValueOrDefault(nameof(AlignmentType.NeutralGood), 0),
                    ChaoticGoodCount = alignmentCounts.GetValueOrDefault(nameof(AlignmentType.ChaoticGood), 0),
                    LawfulNeutralCount = alignmentCounts.GetValueOrDefault(nameof(AlignmentType.LawfulNeutral), 0),
                    TrueNeutralCount = alignmentCounts.GetValueOrDefault(nameof(AlignmentType.TrueNeutral), 0),
                    ChaoticNeutralCount = alignmentCounts.GetValueOrDefault(nameof(AlignmentType.ChaoticNeutral), 0),
                    LawfulEvilCount = alignmentCounts.GetValueOrDefault(nameof(AlignmentType.LawfulEvil), 0),
                    NeutralEvilCount = alignmentCounts.GetValueOrDefault(nameof(AlignmentType.NeutralEvil), 0),
                    ChaoticEvilCount = alignmentCounts.GetValueOrDefault(nameof(AlignmentType.ChaoticEvil), 0)
                };
                alignmentScore.DominantAlignment = GetDominantAlignment(alignmentScore);
                
                dbContext.UserAlignmentScores.Add(alignmentScore);
            }

            foreach (var sentiment in sentiments)
            {
                sentiment.IsSummarized = true;
            }
        }

        await dbContext.SaveChangesAsync();

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

    private static string GetDominantAlignment(UserAlignmentScore score)
    {
        var alignments = new Dictionary<string, int>
        {
            [nameof(AlignmentType.LawfulGood)] = score.LawfulGoodCount,
            [nameof(AlignmentType.NeutralGood)] = score.NeutralGoodCount,
            [nameof(AlignmentType.ChaoticGood)] = score.ChaoticGoodCount,
            [nameof(AlignmentType.LawfulNeutral)] = score.LawfulNeutralCount,
            [nameof(AlignmentType.TrueNeutral)] = score.TrueNeutralCount,
            [nameof(AlignmentType.ChaoticNeutral)] = score.ChaoticNeutralCount,
            [nameof(AlignmentType.LawfulEvil)] = score.LawfulEvilCount,
            [nameof(AlignmentType.NeutralEvil)] = score.NeutralEvilCount,
            [nameof(AlignmentType.ChaoticEvil)] = score.ChaoticEvilCount
        };

        return alignments.OrderByDescending(kvp => kvp.Value).First().Key;
    }
}
