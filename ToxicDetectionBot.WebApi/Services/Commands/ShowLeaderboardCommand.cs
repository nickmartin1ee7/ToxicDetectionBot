using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Data;
using ToxicDetectionBot.WebApi.Services.Commands.Helpers;

namespace ToxicDetectionBot.WebApi.Services.Commands;

public class ShowLeaderboardCommand : ISlashCommand
{
    private readonly ILogger<ShowLeaderboardCommand> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<DiscordSettings> _discordSettings;

    public ShowLeaderboardCommand(
        ILogger<ShowLeaderboardCommand> logger,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<DiscordSettings> discordSettings)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _discordSettings = discordSettings;
    }

    public async Task HandleAsync(SocketSlashCommand command, DiscordSocketClient? client)
    {
        if (command.Channel is not SocketGuildChannel { Guild: var guild })
        {
            await command.RespondAsync("This command can only be used in a server.").ConfigureAwait(false);
            return;
        }

        await command.DeferAsync().ConfigureAwait(false);

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId = command.User.Id.ToString();
        var isAdmin = _discordSettings.Value.AdminList.Contains(userId);

        // Get sort parameter (default to toxicity)
        var sortBy = command.Data.Options.FirstOrDefault(o => o.Name == "sort")?.Value as string ?? "toxicity";
        var isToxicitySort = sortBy.Equals("toxicity", StringComparison.OrdinalIgnoreCase);

        var guildId = guild.Id.ToString();

        List<(string UserId, double ToxicityPercentage, string Alignment, int TotalMessages, string? Username, string? GuildName, string? ChannelName)> leaderboard;

        if (isAdmin)
        {
            leaderboard = await BuildGlobalLeaderboardAsync(dbContext, isToxicitySort).ConfigureAwait(false);
        }
        else
        {
            leaderboard = await BuildGuildLeaderboardAsync(dbContext, guildId, isToxicitySort).ConfigureAwait(false);
        }

        var embed = EmbedHelper.BuildLeaderboardEmbed(guild, leaderboard, isAdmin, isToxicitySort);
        await command.FollowupAsync(embed: embed, ephemeral: isAdmin).ConfigureAwait(false);
    }

    private static async Task<List<(string UserId, double ToxicityPercentage, string Alignment, int TotalMessages, string? Username, string? GuildName, string? ChannelName)>> BuildGlobalLeaderboardAsync(
        AppDbContext dbContext,
        bool isToxicitySort)
    {
        // Admin: aggregate across all guilds
        var sentimentScores = await dbContext.UserSentimentScores.ToListAsync().ConfigureAwait(false);
        var alignmentScores = await dbContext.UserAlignmentScores.ToListAsync().ConfigureAwait(false);

        // Aggregate by UserId across all guilds
        var aggregatedData = sentimentScores
            .GroupBy(s => s.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                TotalMessages = g.Sum(s => s.TotalMessages),
                ToxicMessages = g.Sum(s => s.ToxicMessages),
                ToxicityPercentage = g.Sum(s => s.TotalMessages) > 0
                    ? (double)g.Sum(s => s.ToxicMessages) / g.Sum(s => s.TotalMessages) * 100
                    : 0,
                // Get dominant alignment across all guilds
                Alignments = alignmentScores.Where(a => a.UserId == g.Key).ToList()
            })
            .Select(x => new
            {
                x.UserId,
                x.TotalMessages,
                x.ToxicityPercentage,
                Alignment = AlignmentHelper.GetGlobalDominantAlignment(x.Alignments)
            })
            .ToList();

        // Sort and take top 50
        List<(string UserId, double ToxicityPercentage, string Alignment, int TotalMessages)> topUsers;

        if (isToxicitySort)
        {
            topUsers = [.. aggregatedData
                .OrderByDescending(x => x.TotalMessages)
                .ThenByDescending(x => x.ToxicityPercentage)
                .Take(50)
                .Select(x => (x.UserId, x.ToxicityPercentage, x.Alignment, x.TotalMessages))];
        }
        else
        {
            topUsers = [.. aggregatedData
                .OrderByDescending(x => x.TotalMessages)
                .ThenByDescending(x => Enum.TryParse<AlignmentType>(x.Alignment, out var alignmentEnum) ? (int)alignmentEnum : 0)
                .Take(50)
                .Select(x => (x.UserId, x.ToxicityPercentage, x.Alignment, x.TotalMessages))];
        }

        // Get user details from most recent sentiment in any guild
        var userIds = topUsers.Select(t => t.UserId).ToList();
        var recentSentiments = await dbContext.UserSentiments
            .Where(s => userIds.Contains(s.UserId))
            .GroupBy(s => s.UserId)
            .Select(g => g.OrderByDescending(s => s.CreatedAt).FirstOrDefault())
            .ToListAsync()
            .ConfigureAwait(false);

        var userDetailsDict = recentSentiments
            .Where(s => s != null)
            .ToDictionary(s => s!.UserId, s => (s!.Username, s.GuildName, s.ChannelName));

        return topUsers.Select(t =>
        {
            userDetailsDict.TryGetValue(t.UserId, out var details);
            return (t.UserId, t.ToxicityPercentage, t.Alignment, t.TotalMessages, details.Username, details.GuildName, details.ChannelName);
        }).ToList();
    }

    private static async Task<List<(string UserId, double ToxicityPercentage, string Alignment, int TotalMessages, string? Username, string? GuildName, string? ChannelName)>> BuildGuildLeaderboardAsync(
        AppDbContext dbContext,
        string guildId,
        bool isToxicitySort)
    {
        var sentimentScores = await dbContext.UserSentimentScores
            .Where(s => s.GuildId == guildId)
            .ToListAsync()
            .ConfigureAwait(false);

        var alignmentScores = await dbContext.UserAlignmentScores
            .Where(s => s.GuildId == guildId)
            .ToListAsync()
            .ConfigureAwait(false);

        var combinedData = sentimentScores
            .Select(s => new
            {
                UserId = s.UserId,
                TotalMessages = s.TotalMessages,
                ToxicityPercentage = s.ToxicityPercentage,
                Alignment = alignmentScores.FirstOrDefault(a => a.UserId == s.UserId && a.GuildId == guildId)?.DominantAlignment ?? nameof(AlignmentType.TrueNeutral)
            })
            .ToList();

        List<(string UserId, double ToxicityPercentage, string Alignment, int TotalMessages)> topUsers;

        if (isToxicitySort)
        {
            topUsers = [.. combinedData
                .OrderByDescending(x => x.TotalMessages)
                .ThenByDescending(x => x.ToxicityPercentage)
                .Take(10)
                .Select(x => (x.UserId, x.ToxicityPercentage, x.Alignment, x.TotalMessages))];
        }
        else
        {
            topUsers = [.. combinedData
                .OrderByDescending(x => x.TotalMessages)
                .ThenByDescending(x => Enum.TryParse<AlignmentType>(x.Alignment, out var alignmentEnum) ? (int)alignmentEnum : 0)
                .Take(10)
                .Select(x => (x.UserId, x.ToxicityPercentage, x.Alignment, x.TotalMessages))];
        }

        // Get usernames from most recent sentiment in this guild
        var userIds = topUsers.Select(t => t.UserId).ToList();
        var recentSentiments = await dbContext.UserSentiments
            .Where(s => userIds.Contains(s.UserId) && s.GuildId == guildId)
            .GroupBy(s => s.UserId)
            .Select(g => g.OrderByDescending(s => s.CreatedAt).FirstOrDefault())
            .ToListAsync()
            .ConfigureAwait(false);

        var userDetailsDict = recentSentiments
            .Where(s => s != null)
            .ToDictionary(s => s!.UserId, s => s!.Username);

        return topUsers.Select(t =>
        {
            userDetailsDict.TryGetValue(t.UserId, out var username);
            return (t.UserId, t.ToxicityPercentage, t.Alignment, t.TotalMessages, username, (string?)null, (string?)null);
        }).ToList();
    }
}
