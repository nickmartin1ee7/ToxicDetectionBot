using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Text.Json;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services;

public interface IDiscordCommandHandler
{
    Task RegisterCommandsAsync(DiscordSocketClient client);
    Task HandleSlashCommandAsync(SocketSlashCommand command);
    Task HandleUserCommandAsync(SocketUserCommand command);
}

public class DiscordCommandHandler : IDiscordCommandHandler
{
    private const uint BrandColor = 0x83b670;
    
    private readonly ILogger<DiscordCommandHandler> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<DiscordSettings> _discordSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IChatClient _chatClient;
    private readonly Dictionary<string, Func<SocketSlashCommand, Task>> _commandHandlers;
    private readonly Dictionary<string, Func<SocketUserCommand, Task>> _userCommandHandlers;
    private readonly ChatOptions? _chatOptions;

    private JsonDocument SchemaDoc => 
        JsonDocument.Parse(_discordSettings.Value.JsonSchema
            ?? throw new ArgumentNullException(nameof(_discordSettings.Value.JsonSchema)));

    public DiscordCommandHandler(
        ILogger<DiscordCommandHandler> logger,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<DiscordSettings> discordSettings,
        IHttpClientFactory httpClientFactory,
        IChatClient chatClient)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _discordSettings = discordSettings;
        _httpClientFactory = httpClientFactory;
        _chatClient = chatClient;
        _commandHandlers = new()
        {
            ["showstats"] = HandleShowStatsAsync,
            ["showleaderboard"] = HandleShowLeaderboardAsync,
            ["opt"] = HandleOptAsync,
            ["feedback"] = HandleFeedbackAsync,
            ["check"] = HandleCheckAsync
        };
        _userCommandHandlers = new()
        {
            ["Show Stats"] = HandleShowStatsUserCommandAsync
        };

        _chatOptions ??= new ChatOptions
        {
            Instructions = _discordSettings.Value.SentimentSystemPrompt
                ?? throw new ArgumentNullException(nameof(_discordSettings.Value.SentimentSystemPrompt)),
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                SchemaDoc.RootElement,
                schemaName: "SentimentAnalysisResult",
                schemaDescription: "Schema to classify a message's sentiment. IsToxic: False represents that the message was toxic/mean. True represents that the message was nice/polite.")
        };
    }

    public async Task RegisterCommandsAsync(DiscordSocketClient client)
    {
        var commands = BuildSlashCommands();
        var userCommands = BuildUserCommands();
        _ = Task.Run(async () => await client.BulkOverwriteGlobalApplicationCommandsAsync([.. commands, .. userCommands]));
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

    public async Task HandleUserCommandAsync(SocketUserCommand command)
    {
        try
        {
            if (_userCommandHandlers.TryGetValue(command.Data.Name, out var handler))
            {
                _logger.LogInformation(
                    "Handling user command {CommandName} from user {Username} ({UserId}) targeting {TargetUsername} ({TargetUserId}) in channel {ChannelName} ({ChannelId} in {GuildId})",
                    command.Data.Name,
                    command.User.Username,
                    command.User.Id,
                    command.Data.Member.Username,
                    command.Data.Member.Id,
                    command.Channel.Name,
                    command.Channel.Id,
                    command.GuildId);

                _ = Task.Run(async () => await handler(command));
            }
            else
            {
                _logger.LogWarning("Received unknown user command {CommandName}", command.Data.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling user command {CommandName}", command.Data.Name);

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
            .Build(),

        new SlashCommandBuilder()
            .WithName("feedback")
            .WithDescription("Send feedback to the developer")
            .AddOption("message", ApplicationCommandOptionType.String, "Your feedback message", isRequired: true, minLength: 10, maxLength: 1000)
            .Build(),

        new SlashCommandBuilder()
            .WithName("check")
            .WithDescription("Check if a message would be considered toxic (doesn't count against anyone)")
            .AddOption("message", ApplicationCommandOptionType.String, "The message to check", isRequired: true, minLength: 1, maxLength: 2000)
            .Build()
    ];

    private static ApplicationCommandProperties[] BuildUserCommands() =>
    [
        new UserCommandBuilder()
            .WithName("Show Stats")
            .Build()
    ];

    private async Task HandleShowStatsUserCommandAsync(SocketUserCommand command)
    {
        var user = command.Data.Member;

        if (command.Channel is not SocketGuildChannel { Guild: var guild })
        {
            await command.RespondAsync("This command can only be used in a server.", ephemeral: true);
            return;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId = user.Id.ToString();
        var guildId = guild.Id.ToString();
        
        // Calculate guild-specific stats from UserSentiments
        var sentiments = await dbContext.UserSentiments
            .Where(s => s.UserId == userId && s.GuildId == guildId)
            .ToListAsync();

        var optOut = await dbContext.UserOptOuts.FirstOrDefaultAsync(o => o.UserId == userId);

        var embed = BuildUserStatsEmbed(user, sentiments, optOut);
        await command.RespondAsync(embed: embed);
    }

    private async Task HandleShowStatsAsync(SocketSlashCommand command)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not SocketUser user)
        {
            await command.RespondAsync("User has no sentiment yet.", ephemeral: true);
            return;
        }

        if (command.Channel is not SocketGuildChannel { Guild: var guild })
        {
            await command.RespondAsync("This command can only be used in a server.", ephemeral: true);
            return;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId = user.Id.ToString();
        var guildId = guild.Id.ToString();
        
        // Calculate guild-specific stats from UserSentiments
        var sentiments = await dbContext.UserSentiments
            .Where(s => s.UserId == userId && s.GuildId == guildId)
            .ToListAsync();

        var optOut = await dbContext.UserOptOuts.FirstOrDefaultAsync(o => o.UserId == userId);

        var embed = BuildUserStatsEmbed(user, sentiments, optOut);
        await command.RespondAsync(embed: embed);
    }

    private static Embed BuildUserStatsEmbed(SocketUser user, List<UserSentiment> sentiments, UserOptOut? optOut)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"Sentiment Stats for {user.Username}")
            .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
            .WithColor(BrandColor)
            .WithCurrentTimestamp();

        if (optOut?.IsOptedOut == true)
        {
            embed.WithDescription("⚠️ This user has opted out of sentiment analysis.");
        }
        else if (sentiments.Count == 0)
        {
            embed.WithDescription("No stats available for this user in this server yet.");
        }
        else
        {
            var totalMessages = sentiments.Count;
            var toxicMessages = sentiments.Count(s => s.IsToxic);
            var nonToxicMessages = totalMessages - toxicMessages;
            var toxicityPercentage = totalMessages > 0 ? (double)toxicMessages / totalMessages * 100 : 0;
            var lastUpdated = sentiments.Max(s => s.CreatedAt);
            var timestamp = new DateTimeOffset(lastUpdated).ToUnixTimeSeconds();
            
            embed
                .AddField("Total Messages", totalMessages.ToString(), inline: true)
                .AddField("Toxic Messages", toxicMessages.ToString(), inline: true)
                .AddField("Non-Toxic Messages", nonToxicMessages.ToString(), inline: true)
                .AddField("Toxicity Percentage", $"{toxicityPercentage:F2}%", inline: true)
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
            .OrderByDescending(s => s.TotalMessages)
            .ThenByDescending(s => s.ToxicityPercentage)
            .Take(isAdmin ? 50 : 10)
            .ToListAsync();

        // For global view, get the most recent sentiment data for each user to display username, guild, and channel
        Dictionary<string, UserSentiment?>? sentimentDetails = null;
        if (isAdmin && leaderboard.Count > 0)
        {
            var userIds = leaderboard.Select(l => l.UserId).ToList();
            var recentSentiments = await dbContext.UserSentiments
                .Where(s => userIds.Contains(s.UserId))
                .GroupBy(s => s.UserId)
                .Select(g => g.OrderByDescending(s => s.CreatedAt).FirstOrDefault())
                .ToListAsync();

            sentimentDetails = recentSentiments
                .Where(s => s != null)
                .ToDictionary(s => s!.UserId, s => s);
        }

        var embed = BuildLeaderboardEmbed(guild, leaderboard, isAdmin, sentimentDetails);
        await command.RespondAsync(embed: embed, ephemeral: isAdmin);
    }

    private static Embed BuildLeaderboardEmbed(SocketGuild guild, List<UserSentimentScore> leaderboard, bool isGlobalView, Dictionary<string, UserSentiment?>? sentimentDetails = null)
    {
        var title = isGlobalView 
            ? "🐍 Global Toxicity Leaderboard"
            : $"🐍 Toxicity Leaderboard - {guild.Name}";

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(BrandColor)
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

    private async Task HandleFeedbackAsync(SocketSlashCommand command)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not string message)
        {
            await command.RespondAsync("Please provide a feedback message.", ephemeral: true);
            return;
        }

        var webhookUrl = _discordSettings.Value.FeedbackWebhookUrl;
        
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogWarning("Feedback command used but FeedbackWebhookUrl is not configured");
            await command.RespondAsync("Feedback system is not configured. Please contact the administrator.", ephemeral: true);
            return;
        }

        try
        {
            var guildName = command.Channel is SocketGuildChannel { Guild: var guild } 
                ? guild.Name 
                : "DM";
            
            var embed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "📬 New Feedback",
                        description = message,
                        color = BrandColor,
                        fields = new[]
                        {
                            new { name = "User", value = $"{command.User.Username} ({command.User.Id})", inline = true },
                            new { name = "Server", value = guildName, inline = true },
                            new { name = "Channel", value = command.Channel.Name, inline = true }
                        },
                        timestamp = DateTimeOffset.UtcNow.ToString("o")
                    }
                }
            };

            var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.PostAsJsonAsync(webhookUrl, embed);
            
            if (response.IsSuccessStatusCode)
            {
                await command.RespondAsync("✅ Thank you for your feedback! Your message has been sent to the developer.", ephemeral: true);
                
                _logger.LogInformation(
                    "Feedback submitted by user {UserId} ({Username}) from server {GuildName}: {Message}",
                    command.User.Id,
                    command.User.Username,
                    guildName,
                    message);
            }
            else
            {
                _logger.LogError(
                    "Failed to send feedback webhook. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode,
                    await response.Content.ReadAsStringAsync());
                
                await command.RespondAsync("❌ Failed to send feedback. Please try again later.", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending feedback from user {UserId} ({Username})", 
                command.User.Id, 
                command.User.Username);
            
            await command.RespondAsync("❌ An error occurred while sending feedback. Please try again later.", ephemeral: true);
        }
    }

    private async Task HandleCheckAsync(SocketSlashCommand command)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not string message)
        {
            await command.RespondAsync("Please provide a message to check.", ephemeral: true);
            return;
        }

        await command.DeferAsync(ephemeral: true);

        try
        {
            var result = await _chatClient.GetResponseAsync(
                chatMessage: message,
                options: _chatOptions);

            var resultText = result.Text.Trim();
            var classificationResult = JsonSerializer.Deserialize<ClassificationResult>(resultText);

            var embedColor = classificationResult?.IsToxic == true ? 0xFF6B6Bu : BrandColor;

            var embed = new EmbedBuilder()
                .WithTitle("🔍 Toxicity Check Result")
                .WithDescription($"**Message:** {message}")
                .WithColor(embedColor)
                .AddField("Classification", classificationResult?.IsToxic == true ? "🐍 Toxic" : "😇 Nice", inline: true)
                .WithFooter("This check does not count against any user's stats")
                .WithCurrentTimestamp()
                .Build();

            await command.FollowupAsync(embed: embed, ephemeral: true);

            _logger.LogInformation(
                "Check command used by user {UserId} ({Username}). Message: '{Message}', Result: {IsToxic}",
                command.User.Id,
                command.User.Username,
                message,
                classificationResult?.IsToxic ?? false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking message toxicity for user {UserId} ({Username})",
                command.User.Id,
                command.User.Username);

            await command.FollowupAsync("❌ An error occurred while checking the message. Please try again later.", ephemeral: true);
        }
    }
}
