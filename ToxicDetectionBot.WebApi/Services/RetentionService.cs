using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services;

public interface IRetentionService
{
    Task PurgeOldSentiments();
}

public class RetentionService : IRetentionService
{
    private readonly ILogger<RetentionService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly DiscordSettings _discordSettings;

    public RetentionService(
        ILogger<RetentionService> logger,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<DiscordSettings> discordSettings)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _discordSettings = discordSettings.Value;
    }

    public async Task PurgeOldSentiments()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _logger.LogInformation("Starting sentiment retention purge...");

        var cutoffDate = DateTime.UtcNow.AddDays(-_discordSettings.RetentionInDays);

        var oldSentiments = await dbContext.UserSentiments
            .Where(us => us.CreatedAt < cutoffDate)
            .ToListAsync();

        if (oldSentiments.Count == 0)
        {
            _logger.LogInformation("No sentiments older than {RetentionDays} days found", _discordSettings.RetentionInDays);
            return;
        }

        dbContext.UserSentiments.RemoveRange(oldSentiments);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Sentiment retention purge completed. Deleted {Count} sentiments older than {RetentionDays} days (before {CutoffDate:yyyy-MM-dd HH:mm:ss} UTC)",
            oldSentiments.Count,
            _discordSettings.RetentionInDays,
            cutoffDate);
    }
}
