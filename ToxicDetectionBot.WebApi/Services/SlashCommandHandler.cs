using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services;

public interface ISlashCommandHandler
{
    Task RegisterCommandsAsync(DiscordSocketClient client);
    Task HandleSlashCommandAsync(SocketSlashCommand command);
}

public class SlashCommandHandler : ISlashCommandHandler
{
    private readonly ILogger<SlashCommandHandler> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<DiscordSettings> _discordSettings;
    private readonly Dictionary<string, Func<SocketSlashCommand, Task>> _commandHandlers;

    public SlashCommandHandler(
        ILogger<SlashCommandHandler> logger,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<DiscordSettings> discordSettings)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _discordSettings = discordSettings;
        _commandHandlers = new()
        {
            ["showstats"] = HandleShowStatsAsync,
            ["showleaderboard"] = HandleShowLeaderboardAsync,
            ["opt"] = HandleOptAsync
        };
    }

    public async Task RegisterCommandsAsync(DiscordSocketClient client)
    {
        var commands = BuildSlashCommands();
        _ = Task.Run(async () => await client.BulkOverwriteGlobalApplicationCommandsAsync(commands));
    }

    public async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        try
        {
            if (_commandHandlers.TryGetValue(command.Data.Name, out var handler))
            {
                _logger.LogInformation(
                    "Handling slash command {CommandName} from user {Username} ({UserId}) in channel {ChannelName} ({ChannelId} in {GuildId})",
                    command.Data.Name,
                    command.User.Username,
                    command.User.Id,
                    command.Channel.Name,
                    command.Channel.Id,
                    command.GuildId);

                _ = Task.Run(async () => await handler(command));
            }
            else
            {
                _logger.LogWarning("Received unknown slash command {CommandName}", command.Data.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling slash command {CommandName}", command.Data.Name);

            if (!command.HasResponded)
            {
                await command.RespondAsync("An error occurred while processing your command.", ephemeral: true);
            }
        }
    }

    private static SlashCommandProperties[] BuildSlashCommands() =>
    [
        new SlashCommandBuilder()
            .WithName("showstats")
            .WithDescription("Show sentiment stats for a user")
            .AddOption("user", ApplicationCommandOptionType.User, "The user to show stats for", isRequired: true)
            .Build(),

        new SlashCommandBuilder()
            .WithName("showleaderboard")
            .WithDescription("Show the toxicity leaderboard for this server")
            .Build(),

        new SlashCommandBuilder()
            .WithName("opt")
            .WithDescription("Opt in or out of sentiment analysis")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("choice")
                .WithDescription("Choose to opt in or out")
                .WithRequired(true)
                .AddChoice("Out", "out")
                .AddChoice("In", "in")
                .WithType(ApplicationCommandOptionType.String))
            .Build()
    ];

    private async Task HandleShowStatsAsync(SocketSlashCommand command)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not SocketUser user)
        {
            await command.RespondAsync("User has no sentiment yet.", ephemeral: true);
            return;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId = user.Id.ToString();
        var stats = await dbContext.UserSentimentScores.FirstOrDefaultAsync(s => s.UserId == userId);
        var optOut = await dbContext.UserOptOuts.FirstOrDefaultAsync(o => o.UserId == userId);

        var embed = BuildUserStatsEmbed(user, stats, optOut);
        await command.RespondAsync(embed: embed);
    }

    private static Embed BuildUserStatsEmbed(SocketUser user, UserSentimentScore? stats, UserOptOut? optOut)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"Sentiment Stats for {user.Username}")
            .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        if (optOut?.IsOptedOut == true)
        {
            embed.WithDescription("⚠️ This user has opted out of sentiment analysis.");
        }
        else if (stats is null)
        {
            embed.WithDescription("No stats available for this user yet.");
        }
        else
        {
            var timestamp = new DateTimeOffset(stats.SummarizedAt).ToUnixTimeSeconds();
            embed
                .AddField("Total Messages", stats.TotalMessages.ToString(), inline: true)
                .AddField("Toxic Messages", stats.ToxicMessages.ToString(), inline: true)
                .AddField("Non-Toxic Messages", stats.NonToxicMessages.ToString(), inline: true)
                .AddField("Toxicity Percentage", $"{stats.ToxicityPercentage:F2}%", inline: true)
                .AddField("Last Updated (UTC)", $"<t:{timestamp}:R>", inline: true);
        }

        return embed.Build();
    }

    private async Task HandleShowLeaderboardAsync(SocketSlashCommand command)
    {
        if (command.Channel is not SocketGuildChannel { Guild: var guild })
        {
            await command.RespondAsync("This command can only be used in a server.");
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
            .OrderByDescending(s => s.ToxicityPercentage)
            .ThenByDescending(s => s.TotalMessages)
            .Take(isAdmin ? 50 : 10)
            .ToListAsync();

        var embed = BuildLeaderboardEmbed(guild, leaderboard, isAdmin);
        await command.RespondAsync(embed: embed);
    }

    private static Embed BuildLeaderboardEmbed(SocketGuild guild, List<UserSentimentScore> leaderboard, bool isGlobalView)
    {
        var title = isGlobalView 
            ? "🐍 Global Toxicity Leaderboard"
            : $"🐍 Toxicity Leaderboard - {guild.Name}";

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        if (leaderboard.Count == 0)
        {
            embed.WithDescription("No stats available for this server yet.");
            return embed.Build();
        }

        var description = string.Join('\n', leaderboard.Select((stat, index) =>
        {
            var user = guild.GetUser(ulong.Parse(stat.UserId));
            var username = user?.Username ?? $"Unknown User ({stat.UserId})";
            var medal = GetRankMedal(index);
            return $"{medal} **{username}** - {stat.ToxicityPercentage:F2}% toxic ({stat.ToxicMessages}/{stat.TotalMessages} messages)";
        }));

        embed.WithDescription(description);
        return embed.Build();
    }

    private static string GetRankMedal(int index) => index switch
    {
        0 => "🥇",
        1 => "🥈",
        2 => "🥉",
        _ => $"{index + 1}."
    };

    private async Task HandleOptAsync(SocketSlashCommand command)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not string choice)
        {
            await command.RespondAsync("Invalid choice.", ephemeral: true);
            return;
        }

        var userId = command.User.Id.ToString();
        var isOptingOut = choice.Equals("out", StringComparison.OrdinalIgnoreCase);

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var optOut = await dbContext.UserOptOuts.FindAsync(userId);

        if (optOut is null)
        {
            optOut = new UserOptOut
            {
                UserId = userId,
                IsOptedOut = isOptingOut,
                LastChangedAt = DateTime.UtcNow
            };
            dbContext.UserOptOuts.Add(optOut);
        }
        else
        {
            optOut.IsOptedOut = isOptingOut;
            optOut.LastChangedAt = DateTime.UtcNow;
        }

        // Delete user data when opting out
        if (isOptingOut)
        {
            await dbContext.UserSentiments
                .Where(s => s.UserId == userId)
                .ExecuteDeleteAsync();

            await dbContext.UserSentimentScores
                .Where(s => s.UserId == userId)
                .ExecuteDeleteAsync();
        }

        await dbContext.SaveChangesAsync();

        var message = isOptingOut
            ? "✅ You have opted **OUT** of sentiment analysis. Your messages will no longer be evaluated and your existing data has been deleted."
            : "✅ You have opted **IN** to sentiment analysis. Your messages will now be evaluated.";

        await command.RespondAsync(message, ephemeral: true);

        _logger.LogInformation(
            "User {UserId} ({Username}) opted {OptStatus} of sentiment analysis",
            userId, command.User.Username, isOptingOut ? "OUT" : "IN");
    }
}
