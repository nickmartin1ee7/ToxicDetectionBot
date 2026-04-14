using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using ToxicDetectionBot.WebApi.Data;
using ToxicDetectionBot.WebApi.Services.Commands.Helpers;

namespace ToxicDetectionBot.WebApi.Services.Commands;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class ShowStatsCommand : ISlashCommand
{
    private readonly ILogger<ShowStatsCommand> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly AlignmentChartService _alignmentChartService;

    public ShowStatsCommand(
        ILogger<ShowStatsCommand> logger,
        IServiceScopeFactory serviceScopeFactory,
        AlignmentChartService alignmentChartService)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _alignmentChartService = alignmentChartService;
    }

    public async Task HandleAsync(SocketSlashCommand command, DiscordSocketClient? client)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not SocketUser user)
        {
            await command.RespondAsync("User has no sentiment yet.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (command.Channel is not SocketGuildChannel { Guild: var guild })
        {
            await command.RespondAsync("This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId = user.Id.ToString();
        var guildId = guild.Id.ToString();

        var optOut = await dbContext.UserOptOuts.FirstOrDefaultAsync(o => o.UserId == userId).ConfigureAwait(false);

        // Get pre-computed scores for this specific guild
        var sentimentScore = await dbContext.UserSentimentScores
            .FirstOrDefaultAsync(s => s.UserId == userId && s.GuildId == guildId).ConfigureAwait(false);

        var alignmentScore = await dbContext.UserAlignmentScores
            .FirstOrDefaultAsync(s => s.UserId == userId && s.GuildId == guildId).ConfigureAwait(false);

        var embed = EmbedHelper.BuildUserStatsEmbed(user, sentimentScore, alignmentScore, optOut);

        // Generate and attach alignment chart image
        if (alignmentScore != null)
        {
            try
            {
                var chartBytes = await _alignmentChartService.GenerateAlignmentChartAsync(alignmentScore, CancellationToken.None).ConfigureAwait(false);
                using (var stream = new MemoryStream(chartBytes))
                {
                    await command.RespondWithFileAsync(
                        new FileAttachment(stream, "alignment-chart.png"),
                        text: $"Stats for {user.Mention}",
                        embeds: new[] { embed }
                    ).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate alignment chart for user {UserId}", userId);
                // Fallback: send embed without chart
                await command.RespondAsync(embed: embed).ConfigureAwait(false);
            }
        }
        else
        {
            // No alignment data, send embed only
            await command.RespondAsync(embed: embed).ConfigureAwait(false);
        }
    }
}
