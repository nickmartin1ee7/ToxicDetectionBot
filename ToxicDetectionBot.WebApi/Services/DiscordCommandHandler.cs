using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Services.CommandHandlers;

namespace ToxicDetectionBot.WebApi.Services;

public interface IDiscordCommandHandler
{
    Task RegisterCommandsAsync(DiscordSocketClient client);
    Task HandleSlashCommandAsync(SocketSlashCommand command);
    Task HandleUserCommandAsync(SocketUserCommand command);
}

public class DiscordCommandHandler : IDiscordCommandHandler
{
    private readonly ILogger<DiscordCommandHandler> _logger;
    private readonly IShowStatsCommandHandler _showStatsHandler;
    private readonly ILeaderboardCommandHandler _leaderboardHandler;
    private readonly IOptCommandHandler _optHandler;
    private readonly IFeedbackCommandHandler _feedbackHandler;
    private readonly ICheckCommandHandler _checkHandler;
    private readonly IOptions<DiscordSettings> _discordSettings;
    private readonly Dictionary<string, Func<SocketSlashCommand, Task>> _commandHandlers;
    private readonly Dictionary<string, Func<SocketUserCommand, Task>> _userCommandHandlers;
    private DiscordSocketClient? _discordClient;

    public DiscordCommandHandler(
        ILogger<DiscordCommandHandler> logger,
        IShowStatsCommandHandler showStatsHandler,
        ILeaderboardCommandHandler leaderboardHandler,
        IOptCommandHandler optHandler,
        IFeedbackCommandHandler feedbackHandler,
        ICheckCommandHandler checkHandler,
        IOptions<DiscordSettings> discordSettings)
    {
        _logger = logger;
        _showStatsHandler = showStatsHandler;
        _leaderboardHandler = leaderboardHandler;
        _optHandler = optHandler;
        _feedbackHandler = feedbackHandler;
        _checkHandler = checkHandler;
        _discordSettings = discordSettings;
        
        _commandHandlers = new()
        {
            ["showstats"] = _showStatsHandler.HandleShowStatsAsync,
            ["showleaderboard"] = _leaderboardHandler.HandleShowLeaderboardAsync,
            ["opt"] = _optHandler.HandleOptAsync,
            ["feedback"] = cmd => _feedbackHandler.HandleFeedbackAsync(cmd, _discordClient!),
            ["check"] = _checkHandler.HandleCheckAsync
        };
        _userCommandHandlers = new()
        {
            ["Show Stats"] = _showStatsHandler.HandleShowStatsUserCommandAsync
        };
    }

    public async Task RegisterCommandsAsync(DiscordSocketClient client)
    {
        _discordClient = client;
        var commands = BuildSlashCommands();
        var userCommands = BuildUserCommands();
        ApplicationCommandProperties[] allCommands = [.. commands, .. userCommands];

        // Register globally
        _ = Task.Run(async () => await client.BulkOverwriteGlobalApplicationCommandsAsync(allCommands));

        // Additionally register debug commands to debug guild if configured
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
    }

    public async Task HandleSlashCommandAsync(SocketSlashCommand command)
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
                await command.RespondAsync("An error occurred while processing your command.", ephemeral: true).ConfigureAwait(false);
            }
        }
    }

    public async Task HandleUserCommandAsync(SocketUserCommand command)
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
                await command.RespondAsync("An error occurred while processing your command.", ephemeral: true).ConfigureAwait(false);
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
            .WithDescription("Show the toxicity leaderboard for this server")
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
            .Build()
    ];

    private static ApplicationCommandProperties[] BuildUserCommands(string suffix = "") =>
    [
        new UserCommandBuilder()
            .WithName($"Show Stats{suffix}")
            .Build()
    ];
}
