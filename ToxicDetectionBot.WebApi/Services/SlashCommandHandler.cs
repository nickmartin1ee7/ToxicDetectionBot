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

    public SlashCommandHandler(
        ILogger<SlashCommandHandler> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task RegisterCommandsAsync(DiscordSocketClient client)
    {
        var showStatsCommand = new SlashCommandBuilder()
            .WithName("showstats")
            .WithDescription("Show sentiment stats for a user")
            .AddOption("user", ApplicationCommandOptionType.User, "The user to show stats for", isRequired: true)
            .Build();

        var showLeaderboardCommand = new SlashCommandBuilder()
            .WithName("showleaderboard")
            .WithDescription("Show the toxicity leaderboard for this server")
            .Build();

        var optCommand = new SlashCommandBuilder()
            .WithName("opt")
            .WithDescription("Opt in or out of sentiment analysis")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("choice")
                .WithDescription("Choose to opt in or out")
                .WithRequired(true)
                .AddChoice("Out", "out")
                .AddChoice("In", "in")
                .WithType(ApplicationCommandOptionType.String))
            .Build();

        try
        {
            foreach (var guild in client.Guilds)
            {
                await guild.CreateApplicationCommandAsync(showStatsCommand);
                await guild.CreateApplicationCommandAsync(showLeaderboardCommand);
                await guild.CreateApplicationCommandAsync(optCommand);
                
                _logger.LogInformation("Registered slash commands for guild {GuildName} ({GuildId}) with {GuildUserCount} users", guild.Name, guild.Id, guild.Users.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register slash commands");
        }
    }

    public async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        try
        {
            switch (command.Data.Name)
            {
                case "showstats":
                    await HandleShowStatsAsync(command);
                    break;
                case "showleaderboard":
                    await HandleShowLeaderboardAsync(command);
                    break;
                case "opt":
                    await HandleOptAsync(command);
                    break;
                default:
                    await command.RespondAsync("Unknown command.", ephemeral: true);
                    break;
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

    private async Task HandleShowStatsAsync(SocketSlashCommand command)
    {
        var user = command.Data.Options.FirstOrDefault()?.Value as SocketUser;
        
        if (user is null)
        {
            await command.RespondAsync("User has no sentiment.", ephemeral: true);
            return;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId = user.Id.ToString();
        var stats = await dbContext.UserSentimentScores
            .FirstOrDefaultAsync(s => s.UserId == userId);

        var optOut = await dbContext.UserOptOuts
            .FirstOrDefaultAsync(o => o.UserId == userId);

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
            embed.AddField("Total Messages", stats.TotalMessages.ToString(), inline: true)
                .AddField("Toxic Messages", stats.ToxicMessages.ToString(), inline: true)
                .AddField("Non-Toxic Messages", stats.NonToxicMessages.ToString(), inline: true)
                .AddField("Toxicity Percentage", $"{stats.ToxicityPercentage:F2}%", inline: true)
                .AddField("Last Updated", $"<t:{new DateTimeOffset(stats.SummarizedAt).ToUnixTimeSeconds()}:R>", inline: true);
        }

        await command.RespondAsync(embed: embed.Build(), ephemeral: true);
    }

    private async Task HandleShowLeaderboardAsync(SocketSlashCommand command)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var guild = (command.Channel as SocketGuildChannel)?.Guild;
        
        if (guild is null)
        {
            await command.RespondAsync("This command can only be used in a server.", ephemeral: true);
            return;
        }

        var guildUserIds = guild.Users.Select(u => u.Id.ToString()).ToHashSet();

        var leaderboard = await dbContext.UserSentimentScores
            .Where(s => guildUserIds.Contains(s.UserId))
            .OrderByDescending(s => s.ToxicityPercentage)
            .ThenByDescending(s => s.TotalMessages)
            .Take(10)
            .ToListAsync();

        var embed = new EmbedBuilder()
            .WithTitle($"🐍 Toxicity Leaderboard - {guild.Name}")
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        if (leaderboard.Count == 0)
        {
            embed.WithDescription("No stats available for this server yet.");
        }
        else
        {
            var description = "";
            for (int i = 0; i < leaderboard.Count; i++)
            {
                var stat = leaderboard[i];
                var user = guild.GetUser(ulong.Parse(stat.UserId));
                var username = user?.Username ?? $"Unknown User ({stat.UserId})";
                
                var medal = i switch
                {
                    0 => "🥇",
                    1 => "🥈",
                    2 => "🥉",
                    _ => $"{i + 1}."
                };

                description += $"{medal} **{username}** - {stat.ToxicityPercentage:F2}% toxic ({stat.ToxicMessages}/{stat.TotalMessages} messages)\n";
            }

            embed.WithDescription(description);
        }

        await command.RespondAsync(embed: embed.Build());
    }

    private async Task HandleOptAsync(SocketSlashCommand command)
    {
        var choice = command.Data.Options.FirstOrDefault()?.Value as string;
        
        if (choice is null)
        {
            await command.RespondAsync("Invalid choice.", ephemeral: true);
            return;
        }

        var userId = command.User.Id.ToString();
        var isOptingOut = choice.ToLower() == "out";

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
        
        _logger.LogInformation("User {UserId} ({Username}) opted {OptStatus} of sentiment analysis", 
            userId, command.User.Username, isOptingOut ? "OUT" : "IN");
    }
}
