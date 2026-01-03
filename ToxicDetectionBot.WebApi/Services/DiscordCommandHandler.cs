using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services;

public interface IDiscordCommandHandler
{
    Task RegisterCommandsAsync(DiscordSocketClient client);
    Task HandleSlashCommandAsync(SocketSlashCommand command, DiscordSocketClient? client);
    Task HandleUserCommandAsync(SocketUserCommand command, DiscordSocketClient? client);
}

public class DiscordCommandHandler : IDiscordCommandHandler
{
    private const uint BrandColor = 0x83b670;

    private readonly ILogger<DiscordCommandHandler> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<DiscordSettings> _discordSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IChatClient _chatClient;
    private readonly Dictionary<string, Func<SocketSlashCommand, DiscordSocketClient?, Task>> _commandHandlers;
    private readonly Dictionary<string, Func<SocketUserCommand, DiscordSocketClient?, Task>> _userCommandHandlers;
    private readonly ChatOptions? _chatOptions;
    private readonly ChatClientMetadata? _metadata;

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
            ["check"] = HandleCheckAsync,
            ["botstats"] = HandleBotStatsAsync
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

        _metadata = _chatClient.GetService<ChatClientMetadata>();
    }

    public async Task RegisterCommandsAsync(DiscordSocketClient client)
    {
        var commands = BuildSlashCommands();
        var userCommands = BuildUserCommands();
        ApplicationCommandProperties[] allCommands = [.. commands, .. userCommands];

        // Register debug commands to debug guild if configured
        if (_discordSettings.Value.DebugGuildId.HasValue)
        {
            var debugGuildId = _discordSettings.Value.DebugGuildId.Value;
            _ = Task.Run(async () =>
            {
                try
                {
                    var guild = client.GetGuild(debugGuildId);
                    if (guild != null)
                    {
                        // Build debug versions with -debug suffix
                        var debugCommands = BuildSlashCommands("-debug");
                        var debugUserCommands = BuildUserCommands("-debug");
                        ApplicationCommandProperties[] allDebugCommands = [.. debugCommands, .. debugUserCommands];
                        
                        await guild.BulkOverwriteApplicationCommandAsync(allDebugCommands);
                        _logger.LogInformation("Successfully registered {Count} debug commands to debug guild {GuildId} ({GuildName})", 
                            allDebugCommands.Length, debugGuildId, guild.Name);
                    }
                    else
                    {
                        _logger.LogWarning("Debug guild {GuildId} not found", debugGuildId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to register commands to debug guild {GuildId}", debugGuildId);
                }
            });
        }
        else
        {
            _ = Task.Run(async () => await client.BulkOverwriteGlobalApplicationCommandsAsync(allCommands));
        }
    }

    public async Task HandleSlashCommandAsync(SocketSlashCommand command, DiscordSocketClient? client)
    {
        try
        {
            // Strip -debug suffix to get the base command name
            var commandName = command.Data.Name.EndsWith("-debug", StringComparison.OrdinalIgnoreCase)
                ? command.Data.Name[..^6]
                : command.Data.Name;

            if (_commandHandlers.TryGetValue(commandName, out var handler))
            {
                _logger.LogInformation(
                    "Handling slash command {CommandName} from user {Username} ({UserId}) in channel {ChannelName} ({ChannelId} in {GuildId})",
                    command.Data.Name,
                    command.User.Username,
                    command.User.Id,
                    command.Channel.Name,
                    command.Channel.Id,
                    command.GuildId);

                _ = Task.Run(async () => await handler(command, client));
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

    public async Task HandleUserCommandAsync(SocketUserCommand command, DiscordSocketClient? client)
    {
        try
        {
            // Strip -debug suffix to get the base command name
            var commandName = command.Data.Name.EndsWith("-debug", StringComparison.OrdinalIgnoreCase)
                ? command.Data.Name[..^6]
                : command.Data.Name;

            if (_userCommandHandlers.TryGetValue(commandName, out var handler))
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

                _ = Task.Run(async () => await handler(command, client));
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

    private static SlashCommandProperties[] BuildSlashCommands(string suffix = "") =>
    [
        new SlashCommandBuilder()
            .WithName($"showstats{suffix}")
            .WithDescription("Show sentiment stats for a user")
            .AddOption("user", ApplicationCommandOptionType.User, "The user to show stats for", isRequired: true)
            .Build(),

        new SlashCommandBuilder()
            .WithName($"showleaderboard{suffix}")
            .WithDescription("Show the leaderboard for this server")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("sort")
                .WithDescription("Choose what to sort by")
                .WithRequired(false)
                .AddChoice("Toxicity", "toxicity")
                .AddChoice("Alignment", "alignment")
                .WithType(ApplicationCommandOptionType.String))
            .Build(),

        new SlashCommandBuilder()
            .WithName($"opt{suffix}")
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
            .WithName($"feedback{suffix}")
            .WithDescription("Send feedback to the developer")
            .AddOption("message", ApplicationCommandOptionType.String, "Your feedback message", isRequired: true, minLength: 10, maxLength: 1000)
            .Build(),

        new SlashCommandBuilder()
            .WithName($"check{suffix}")
            .WithDescription("Check if a message would be considered toxic")
            .AddOption("message", ApplicationCommandOptionType.String, "The message to check", isRequired: true, minLength: 1, maxLength: 2000)
            .Build(),

        new SlashCommandBuilder()
            .WithName($"botstats{suffix}")
            .WithDescription("Show bot system statistics and performance metrics")
            .Build()
    ];

    private static ApplicationCommandProperties[] BuildUserCommands(string suffix = "") =>
    [
        new UserCommandBuilder()
            .WithName($"Show Stats{suffix}")
            .Build()
    ];

    private async Task HandleShowStatsUserCommandAsync(SocketUserCommand command, DiscordSocketClient? client)
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

        // Get pre-computed scores
        var sentimentScore = await dbContext.UserSentimentScores
            .FirstOrDefaultAsync(s => s.UserId == userId);

        var alignmentScore = await dbContext.UserAlignmentScores
            .FirstOrDefaultAsync(s => s.UserId == userId);

        var optOut = await dbContext.UserOptOuts.FirstOrDefaultAsync(o => o.UserId == userId);

        // Get last updated timestamp from UserSentiments
        var lastUpdated = await dbContext.UserSentiments
            .Where(s => s.UserId == userId && s.GuildId == guildId)
            .MaxAsync(s => (DateTime?)s.CreatedAt);

        var embed = BuildUserStatsEmbed(user, sentimentScore, alignmentScore, lastUpdated, optOut);
        await command.RespondAsync(embed: embed);
    }

    private async Task HandleShowStatsAsync(SocketSlashCommand command, DiscordSocketClient? client)
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

        // Get pre-computed scores
        var sentimentScore = await dbContext.UserSentimentScores
            .FirstOrDefaultAsync(s => s.UserId == userId);

        var alignmentScore = await dbContext.UserAlignmentScores
            .FirstOrDefaultAsync(s => s.UserId == userId);

        var optOut = await dbContext.UserOptOuts.FirstOrDefaultAsync(o => o.UserId == userId);

        // Get last updated timestamp from UserSentiments
        var lastUpdated = await dbContext.UserSentiments
            .Where(s => s.UserId == userId && s.GuildId == guildId)
            .MaxAsync(s => (DateTime?)s.CreatedAt);

        var embed = BuildUserStatsEmbed(user, sentimentScore, alignmentScore, lastUpdated, optOut);
        await command.RespondAsync(embed: embed);
    }

    private static Embed BuildUserStatsEmbed(SocketUser user, UserSentimentScore? sentimentScore, UserAlignmentScore? alignmentScore, DateTime? lastUpdated, UserOptOut? optOut)
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
        else if (sentimentScore is null || sentimentScore.TotalMessages == 0)
        {
            embed.WithDescription("No stats available for this user in this server yet.");
        }
        else
        {
            var timestamp = lastUpdated.HasValue 
                ? new DateTimeOffset(DateTime.SpecifyKind(lastUpdated.Value, DateTimeKind.Utc)).ToUnixTimeSeconds() 
                : 0;

            embed
                .AddField("Total Messages", sentimentScore.TotalMessages.ToString(), inline: true)
                .AddField("Toxic Messages", sentimentScore.ToxicMessages.ToString(), inline: true)
                .AddField("Non-Toxic Messages", sentimentScore.NonToxicMessages.ToString(), inline: true)
                .AddField("Toxicity Percentage", $"{sentimentScore.ToxicityPercentage:F2}%", inline: true);

            // Add alignment info if available
            if (alignmentScore is not null)
            {
                var dominantAlignment = alignmentScore.DominantAlignment;
                embed.AddField("Dominant Alignment", $"{GetAlignmentEmoji(dominantAlignment)} {FormatAlignment(dominantAlignment)}", inline: true);

                if (timestamp > 0)
                {
                    embed.AddField("Last Updated", $"<t:{timestamp}:R>", inline: true);
                }

                // Build top 3 alignment distribution
                var alignmentCounts = new Dictionary<string, int>
                {
                    [nameof(AlignmentType.LawfulGood)] = alignmentScore.LawfulGoodCount,
                    [nameof(AlignmentType.NeutralGood)] = alignmentScore.NeutralGoodCount,
                    [nameof(AlignmentType.ChaoticGood)] = alignmentScore.ChaoticGoodCount,
                    [nameof(AlignmentType.LawfulNeutral)] = alignmentScore.LawfulNeutralCount,
                    [nameof(AlignmentType.TrueNeutral)] = alignmentScore.TrueNeutralCount,
                    [nameof(AlignmentType.ChaoticNeutral)] = alignmentScore.ChaoticNeutralCount,
                    [nameof(AlignmentType.LawfulEvil)] = alignmentScore.LawfulEvilCount,
                    [nameof(AlignmentType.NeutralEvil)] = alignmentScore.NeutralEvilCount,
                    [nameof(AlignmentType.ChaoticEvil)] = alignmentScore.ChaoticEvilCount
                };

                var totalAlignmentMessages = alignmentCounts.Values.Sum();
                var topAlignments = alignmentCounts
                    .Where(kvp => kvp.Value > 0)
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(3);

                if (topAlignments.Any())
                {
                    var alignmentDistribution = string.Join("\n", topAlignments.Select(kvp =>
                    {
                        var percentage = totalAlignmentMessages > 0 ? (double)kvp.Value / totalAlignmentMessages * 100 : 0;
                        var emoji = GetAlignmentEmoji(kvp.Key);
                        return $"{emoji} **{FormatAlignment(kvp.Key)}**: {kvp.Value} ({percentage:F1}%)";
                    }));

                    embed.AddField("Alignment Distribution", alignmentDistribution, inline: false);
                }
            }
            else if (timestamp > 0)
            {
                embed.AddField("Last Updated", $"<t:{timestamp}:R>", inline: true);
            }
        }

        return embed.Build();
    }
    
    private static string GetAlignmentEmoji(string alignment) => alignment switch
    {
        nameof(AlignmentType.LawfulGood) => "⚖️",
        nameof(AlignmentType.NeutralGood) => "😇",
        nameof(AlignmentType.ChaoticGood) => "🎭",
        nameof(AlignmentType.LawfulNeutral) => "📜",
        nameof(AlignmentType.TrueNeutral) => "⚖️",
        nameof(AlignmentType.ChaoticNeutral) => "🎲",
        nameof(AlignmentType.LawfulEvil) => "👔",
        nameof(AlignmentType.NeutralEvil) => "😈",
        nameof(AlignmentType.ChaoticEvil) => "💀",
        _ => "❓"
    };
    
    private static string FormatAlignment(string alignment) => alignment switch
    {
        nameof(AlignmentType.LawfulGood) => "Lawful Good",
        nameof(AlignmentType.NeutralGood) => "Neutral Good",
        nameof(AlignmentType.ChaoticGood) => "Chaotic Good",
        nameof(AlignmentType.LawfulNeutral) => "Lawful Neutral",
        nameof(AlignmentType.TrueNeutral) => "True Neutral",
        nameof(AlignmentType.ChaoticNeutral) => "Chaotic Neutral",
        nameof(AlignmentType.LawfulEvil) => "Lawful Evil",
        nameof(AlignmentType.NeutralEvil) => "Neutral Evil",
        nameof(AlignmentType.ChaoticEvil) => "Chaotic Evil",
        _ => alignment
    };

    private async Task HandleShowLeaderboardAsync(SocketSlashCommand command, DiscordSocketClient? client)
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
        
        // Get sort parameter (default to toxicity)
        var sortBy = command.Data.Options.FirstOrDefault(o => o.Name == "sort")?.Value as string ?? "toxicity";
        var isToxicitySort = sortBy.Equals("toxicity", StringComparison.OrdinalIgnoreCase);

        // For non-admin users, scope to current guild
        HashSet<string>? guildUserIds = null;
        if (!isAdmin)
        {
            guildUserIds = [.. guild.Users.Select(u => u.Id.ToString())];
        }

        // Get both sentiment and alignment scores
        IQueryable<UserSentimentScore> sentimentQuery = dbContext.UserSentimentScores;
        IQueryable<UserAlignmentScore> alignmentQuery = dbContext.UserAlignmentScores;
        
        if (!isAdmin && guildUserIds != null)
        {
            sentimentQuery = sentimentQuery.Where(s => guildUserIds.Contains(s.UserId));
            alignmentQuery = alignmentQuery.Where(s => guildUserIds.Contains(s.UserId));
        }

        var sentimentScores = await sentimentQuery.ToListAsync();
        var alignmentScores = await alignmentQuery.ToListAsync();

        // Join the data
        var combinedData = sentimentScores
            .Select(s => new
            {
                UserId = s.UserId,
                TotalMessages = s.TotalMessages,
                ToxicityPercentage = s.ToxicityPercentage,
                Alignment = alignmentScores.FirstOrDefault(a => a.UserId == s.UserId)?.DominantAlignment ?? nameof(AlignmentType.TrueNeutral)
            })
            .ToList();

        // Sort based on user preference
        List<(string UserId, double ToxicityPercentage, string Alignment, int TotalMessages)> leaderboard;
        
        if (isToxicitySort)
        {
            leaderboard = [.. combinedData
                .OrderByDescending(x => x.TotalMessages)
                .ThenByDescending(x => x.ToxicityPercentage)
                .Take(isAdmin ? 50 : 10)
                .Select(x => (x.UserId, x.ToxicityPercentage, x.Alignment, x.TotalMessages))];
        }
        else
        {
            // Sort by most "good" alignment (Lawful Good = 9, Chaotic Evil = 1)
            leaderboard = [.. combinedData
                .OrderByDescending(x => x.TotalMessages)
                .ThenByDescending(x => Enum.TryParse<AlignmentType>(x.Alignment, out var alignmentEnum) ? (int)alignmentEnum : 0)
                .Take(isAdmin ? 50 : 10)
                .Select(x => (x.UserId, x.ToxicityPercentage, x.Alignment, x.TotalMessages))];
        }

        // Get user details for display
        Dictionary<string, (string Username, string? GuildName, string? ChannelName)> userDetails = [];
        
        if (leaderboard.Count > 0)
        {
            var userIds = leaderboard.Select(l => l.UserId).ToList();
            var recentSentiments = await dbContext.UserSentiments
                .Where(s => userIds.Contains(s.UserId))
                .GroupBy(s => s.UserId)
                .Select(g => g.OrderByDescending(s => s.CreatedAt).FirstOrDefault())
                .ToListAsync();

            foreach (var sentiment in recentSentiments.Where(s => s != null))
            {
                userDetails[sentiment!.UserId] = (sentiment.Username, sentiment.GuildName, sentiment.ChannelName);
            }
        }

        var embed = BuildLeaderboardEmbed(guild, leaderboard, isAdmin, isToxicitySort, userDetails);
        await command.RespondAsync(embed: embed, ephemeral: isAdmin);
    }

    private static Embed BuildLeaderboardEmbed(
        SocketGuild guild, 
        List<(string UserId, double ToxicityPercentage, string Alignment, int TotalMessages)> leaderboard, 
        bool isGlobalView, 
        bool isToxicitySort,
        Dictionary<string, (string Username, string? GuildName, string? ChannelName)> userDetails)
    {
        var sortType = isToxicitySort ? "Toxicity" : "Alignment";
        var title = isGlobalView
            ? $"🐍 Global {sortType} Leaderboard"
            : $"🐍 {sortType} Leaderboard - {guild.Name}";

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
            var (userId, toxicityPercentage, alignment, totalMessages) = stat;
            var alignmentEmoji = GetAlignmentEmoji(alignment);
            var alignmentFormatted = FormatAlignment(alignment);

            if (isGlobalView && userDetails.TryGetValue(userId, out var details))
            {
                return $"{medal} **{details.Username}** - {alignmentEmoji} {alignmentFormatted} | {toxicityPercentage:F1}% toxic | {totalMessages} msgs";
            }
            else
            {
                var user = guild.GetUser(ulong.Parse(userId));
                var username = user?.Username ?? $"Unknown User ({userId})";
                
                return $"{medal} **{username}** - {alignmentEmoji} {alignmentFormatted} | {toxicityPercentage:F1}% toxic | {totalMessages} msgs";
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

    private async Task HandleOptAsync(SocketSlashCommand command, DiscordSocketClient? client)
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
            
            await dbContext.UserAlignmentScores
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

    private async Task HandleFeedbackAsync(SocketSlashCommand command, DiscordSocketClient? client)
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

    private async Task HandleCheckAsync(SocketSlashCommand command, DiscordSocketClient? client)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not string message)
        {
            await command.RespondAsync("Please provide a message to check.", ephemeral: true);
            return;
        }

        await command.DeferAsync(ephemeral: true);

        try
        {
            var sw = new Stopwatch();
            sw.Start();
            var result = await _chatClient.GetResponseAsync(
                chatMessage: message,
                options: _chatOptions);
            sw.Stop();

            var resultText = result.Text.Trim();
            var classificationResult = JsonSerializer.Deserialize<ClassificationResult>(resultText);
            var embedColor = classificationResult?.IsToxic == true ? 0xFF6B6Bu : BrandColor;
            var model = _metadata?.DefaultModelId ?? "Unknown Model";

            var alignment = classificationResult?.Alignment ?? "TrueNeutral";
            var alignmentEmoji = GetAlignmentEmoji(alignment);
            var alignmentFormatted = FormatAlignment(alignment);

            var embed = new EmbedBuilder()
                .WithTitle("🔍 Toxicity Check Result")
                .WithDescription($"**Message:**{Environment.NewLine}{message}")
                .WithColor(embedColor)
                .AddField("Sentiment", classificationResult?.IsToxic == true ? "🐍 Toxic" : "😇 Nice", inline: true)
                .AddField("Alignment", $"{alignmentEmoji} {alignmentFormatted}", inline: true)
                .AddField("Model", $"Text evaluated with `{model}`.", inline: true)
                .WithFooter($"Completed in `{sw.ElapsedMilliseconds} ms`.")
                .WithCurrentTimestamp()
                .Build();

            await command.FollowupAsync(embed: embed, ephemeral: true);

            _logger.LogInformation(
                "Check command used by user {UserId} ({Username}). Message: '{Message}', Result: {IsToxic}, Alignment: {Alignment}",
                command.User.Id,
                command.User.Username,
                message,
                classificationResult?.IsToxic ?? false,
                alignment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking message toxicity for user {UserId} ({Username})",
                command.User.Id,
                command.User.Username);

            await command.FollowupAsync("❌ An error occurred while checking the message. Please try again later.", ephemeral: true);
        }
    }

    private async Task HandleBotStatsAsync(SocketSlashCommand command, DiscordSocketClient? client)
    {
        await command.DeferAsync(ephemeral: true);

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
            var totalSentiments = await dbContext.UserSentiments.CountAsync();
            var totalUsers = await dbContext.UserSentimentScores.CountAsync();
            var totalAlignmentUsers = await dbContext.UserAlignmentScores.CountAsync();
            var totalOptOuts = await dbContext.UserOptOuts.CountAsync(o => o.IsOptedOut);

            // Get guild count
            var guildCount = client?.Guilds.Count ?? 0;

            // Get database size (if supported)
            string? dbSize = null;
            try
            {
                var connection = dbContext.Database.GetDbConnection();
                var idx = Math.Max(connection.ConnectionString.LastIndexOf('\\'), connection.ConnectionString.LastIndexOf('/')) + 1;
                var fileName = connection.ConnectionString[idx ..];
                dbSize = $"{new FileInfo(fileName).Length / 1024.0 / 1024.0:F2} MB";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to retrieve database size");
            }

            var embed = BuildBotStatsEmbed(
                uptime,
                memoryMb,
                cpuTime,
                threadCount,
                guildCount,
                totalSentiments,
                totalUsers,
                dbSize);

            await command.FollowupAsync(embed: embed, ephemeral: true);

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

            await command.FollowupAsync("❌ An error occurred while retrieving bot statistics. Please try again later.", ephemeral: true);
        }
    }

    private static Embed BuildBotStatsEmbed(
        TimeSpan uptime,
        double memoryMb,
        TimeSpan cpuTime,
        int threadCount,
        int guildCount,
        int totalSentiments,
        int totalUsers,
        string? dbSize)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🤖 Bot Statistics")
            .WithColor(BrandColor)
            .WithCurrentTimestamp();

        // System Information
        var systemInfo = $"**Uptime:** {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m\n" +
                        $"**Memory:** {memoryMb:F2} MB\n" +
                        $"**CPU Time:** {cpuTime.TotalSeconds:F1}s\n" +
                        $"**Threads:** {threadCount}";
        embed.AddField("📊 System", systemInfo, inline: true);

        // Discord Information
        var discordInfo = $"**Guilds:** {guildCount}";
        embed.AddField("💬 Discord", discordInfo, inline: true);

        // Database Statistics
        var dbStats = $"**Total Messages:** {totalSentiments:N0}\n" +
                     $"**Users:** {totalUsers:N0}\n";
        
        if (!string.IsNullOrWhiteSpace(dbSize))
        {
            dbStats += $"**Database Size:** {dbSize}";
        }

        embed.AddField("🗄️ Database", dbStats, inline: false);

        return embed.Build();
    }
}
