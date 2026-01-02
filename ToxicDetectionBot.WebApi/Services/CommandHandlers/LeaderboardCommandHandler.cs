using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Constants;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services.CommandHandlers;

public interface ILeaderboardCommandHandler
{
    Task HandleShowLeaderboardAsync(SocketSlashCommand command);
}

public class LeaderboardCommandHandler : ILeaderboardCommandHandler
{
    private readonly ILogger<LeaderboardCommandHandler> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<DiscordSettings> _discordSettings;

    public LeaderboardCommandHandler(
        ILogger<LeaderboardCommandHandler> logger,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<DiscordSettings> discordSettings)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _discordSettings = discordSettings;
    }

    public async Task HandleShowLeaderboardAsync(SocketSlashCommand command)
    {
        if (command.Channel is not SocketGuildChannel { Guild: var guild })
        {
            await command.RespondAsync("This command can only be used in a server.").ConfigureAwait(false);
            return;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId = command.User.Id.ToString();
        var isAdmin = _discordSettings.Value.AdminList.Contains(userId);

        IQueryable<UserSentimentScore> query = dbContext.UserSentimentScores;

        if (!isAdmin)
        {
            var guildUserIds = guild.Users.Select(u => u.Id.ToString()).ToHashSet();
            query = query.Where(s => guildUserIds.Contains(s.UserId));
        }

        var leaderboard = await query
            .OrderByDescending(s => s.TotalMessages)
            .ThenByDescending(s => s.ToxicityPercentage)
            .Take(isAdmin ? 50 : 10)
            .ToListAsync().ConfigureAwait(false);

        // For global view, get the most recent sentiment data for each user to display username, guild, and channel
        Dictionary<string, UserSentiment?>? sentimentDetails = null;
        if (isAdmin && leaderboard.Count > 0)
        {
            var userIds = leaderboard.Select(l => l.UserId).ToList();
            var recentSentiments = await dbContext.UserSentiments
                .Where(s => userIds.Contains(s.UserId))
                .GroupBy(s => s.UserId)
                .Select(g => g.OrderByDescending(s => s.CreatedAt).FirstOrDefault())
                .ToListAsync().ConfigureAwait(false);

            sentimentDetails = recentSentiments
                .Where(s => s != null)
                .ToDictionary(s => s!.UserId, s => s);
        }

        var embed = BuildLeaderboardEmbed(guild, leaderboard, isAdmin, sentimentDetails);
        await command.RespondAsync(embed: embed, ephemeral: isAdmin).ConfigureAwait(false);
    }

    private static Embed BuildLeaderboardEmbed(SocketGuild guild, List<UserSentimentScore> leaderboard, bool isGlobalView, Dictionary<string, UserSentiment?>? sentimentDetails = null)
    {
        var title = isGlobalView
            ? $"{DiscordConstants.ToxicEmoji} Global Toxicity Leaderboard"
            : $"{DiscordConstants.ToxicEmoji} Toxicity Leaderboard - {guild.Name}";

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(DiscordConstants.BrandColor)
            .WithCurrentTimestamp();

        if (leaderboard.Count == 0)
        {
            embed.WithDescription(isGlobalView
                ? "No global stats available yet."
                : "No stats available for this server yet.");
            return embed.Build();
        }

        var description = string.Join('\n', leaderboard.Select((stat, index) =>
        {
            var medal = GetRankMedal(index);

            if (isGlobalView && sentimentDetails?.TryGetValue(stat.UserId, out var sentiment) == true && sentiment != null)
            {
                return $"{medal} **{sentiment.Username}** (Guild: {sentiment.GuildName}, Channel: {sentiment.ChannelName}) - {stat.ToxicityPercentage:F2}% toxic ({stat.ToxicMessages}/{stat.TotalMessages} messages)";
            }
            else
            {
                var user = guild.GetUser(ulong.Parse(stat.UserId));
                var username = user?.Username ?? $"Unknown User ({stat.UserId})";
                return $"{medal} **{username}** - {stat.ToxicityPercentage:F2}% toxic ({stat.ToxicMessages}/{stat.TotalMessages} messages)";
            }
        }));

        embed.WithDescription(description);
        return embed.Build();
    }

    private static string GetRankMedal(int index) => index switch
    {
        0 => DiscordConstants.GoldMedal,
        1 => DiscordConstants.SilverMedal,
        2 => DiscordConstants.BronzeMedal,
        _ => $"{index + 1}."
    };
}
