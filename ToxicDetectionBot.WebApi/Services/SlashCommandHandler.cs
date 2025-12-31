using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
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
    private readonly Dictionary<string, Func<SocketSlashCommand, Task>> _commandHandlers;

    public SlashCommandHandler(
        ILogger<SlashCommandHandler> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
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

        foreach (var guild in client.Guilds)
        {
            try
            {
                foreach (var command in commands)
                {
                    await guild.CreateApplicationCommandAsync(command);
                }

                _logger.LogInformation(
                    "Registered slash commands for guild {GuildName} ({GuildId}) with {GuildUserCount} users",
                    guild.Name, guild.Id, guild.Users.Count);
            }
            catch (Exception ex)
            {
                // Discord.Net.HttpException: The server responded with error 50001: Missing Access
                if (ex.Message.Contains("50001"))
                {
                    _logger.LogWarning("Failed to register slash commands for guild {GuildName} ({GuildId}) with {GuildUserCount} users ({ErrorMessage})",
                        guild.Name, guild.Id, guild.Users.Count, ex.Message[ex.Message.IndexOf("50001") ..]);
                    return;
                }

                _logger.LogWarning(ex,
                    "Failed to register slash commands for guild {GuildName} ({GuildId}) with {GuildUserCount} users",
                    guild.Name, guild.Id, guild.Users.Count);
            }
        }
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

                await handler(command);
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
        await command.RespondAsync(embed: embed, ephemeral: true);
    }

    private static Embed BuildUserStatsEmbed(SocketUser user, UserSentimentScore? stats, UserOptOut? optOut)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"Sentiment Stats for {user.Username}")
            .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
            .WithColor(Color.Blue)
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
                .AddField("Last Updated", $"<t:{timestamp}:R>", inline: true);
        }

        return embed.Build();
    }

    private async Task HandleShowLeaderboardAsync(SocketSlashCommand command)
    {
        if (command.Channel is not SocketGuildChannel { Guild: var guild })
        {
            await command.RespondAsync("This command can only be used in a server.", ephemeral: true);
            return;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var guildUserIds = guild.Users.Select(u => u.Id.ToString()).ToHashSet();

        var leaderboard = await dbContext.UserSentimentScores
            .Where(s => guildUserIds.Contains(s.UserId))
            .OrderByDescending(s => s.ToxicityPercentage)
            .ThenByDescending(s => s.TotalMessages)
            .Take(10)
            .ToListAsync();

        var embed = BuildLeaderboardEmbed(guild, leaderboard);
        await command.RespondAsync(embed: embed);
    }

    private static Embed BuildLeaderboardEmbed(SocketGuild guild, List<UserSentimentScore> leaderboard)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"🐍 Toxicity Leaderboard - {guild.Name}")
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

        await dbContext.SaveChangesAsync();

        var message = isOptingOut
            ? "✅ You have opted **OUT** of sentiment analysis. Your messages will no longer be evaluated."
            : "✅ You have opted **IN** to sentiment analysis. Your messages will now be evaluated.";

        await command.RespondAsync(message, ephemeral: true);

        _logger.LogInformation(
            "User {UserId} ({Username}) opted {OptStatus} of sentiment analysis",
            userId, command.User.Username, isOptingOut ? "OUT" : "IN");
    }
}
