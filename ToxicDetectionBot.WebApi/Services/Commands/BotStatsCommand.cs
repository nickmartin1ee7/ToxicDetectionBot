using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using ToxicDetectionBot.WebApi.Data;
using ToxicDetectionBot.WebApi.Services.Commands.Helpers;

namespace ToxicDetectionBot.WebApi.Services.Commands;

public class BotStatsCommand : ISlashCommand
{
    private readonly ILogger<BotStatsCommand> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public BotStatsCommand(
        ILogger<BotStatsCommand> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task HandleAsync(SocketSlashCommand command, DiscordSocketClient? client)
    {
        await command.DeferAsync(ephemeral: true).ConfigureAwait(false);

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Get process information
            var process = Process.GetCurrentProcess();
            var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
            var memoryMb = process.WorkingSet64 / 1024.0 / 1024.0;
            var cpuTime = process.TotalProcessorTime;
            var threadCount = process.Threads.Count;

            // Get database stats (non-PII)
            var totalSentiments = await dbContext.UserSentiments.CountAsync().ConfigureAwait(false);
            var totalUsers = await dbContext.UserSentimentScores.CountAsync().ConfigureAwait(false);
            var totalAlignmentUsers = await dbContext.UserAlignmentScores.CountAsync().ConfigureAwait(false);
            var totalOptOuts = await dbContext.UserOptOuts.CountAsync(o => o.IsOptedOut).ConfigureAwait(false);

            // Get guild count
            var guildCount = client?.Guilds.Count ?? 0;

            // Get database size (if supported)
            string? dbSize = null;
            try
            {
                var connection = dbContext.Database.GetDbConnection();
                var idx = Math.Max(connection.ConnectionString.LastIndexOf('\\'), connection.ConnectionString.LastIndexOf('/')) + 1;
                var fileName = connection.ConnectionString[idx..];
                dbSize = $"{new FileInfo(fileName).Length / 1024.0 / 1024.0:F2} MB";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to retrieve database size");
            }

            var embed = EmbedHelper.BuildBotStatsEmbed(
                uptime,
                memoryMb,
                cpuTime,
                threadCount,
                guildCount,
                totalSentiments,
                totalUsers,
                dbSize);

            await command.FollowupAsync(embed: embed, ephemeral: true).ConfigureAwait(false);

            _logger.LogInformation(
                "BotStats command used by user {UserId} ({Username})",
                command.User.Id,
                command.User.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving bot stats for user {UserId} ({Username})",
                command.User.Id,
                command.User.Username);

            await command.FollowupAsync("? An error occurred while retrieving bot statistics. Please try again later.", ephemeral: true).ConfigureAwait(false);
        }
    }
}
