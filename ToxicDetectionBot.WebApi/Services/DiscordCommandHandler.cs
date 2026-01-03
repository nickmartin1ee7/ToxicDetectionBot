using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ToxicDetectionBot.WebApi.Services.Commands;

namespace ToxicDetectionBot.WebApi.Services;

public interface IDiscordCommandHandler
{
    Task RegisterCommandsAsync(DiscordSocketClient client);
    Task HandleSlashCommandAsync(SocketSlashCommand command, DiscordSocketClient? client);
    Task HandleUserCommandAsync(SocketUserCommand command, DiscordSocketClient? client);
}

public class DiscordCommandHandler : IDiscordCommandHandler
{
    private readonly ILogger<DiscordCommandHandler> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly Dictionary<string, Type> _slashCommandTypes;
    private readonly Dictionary<string, Type> _userCommandTypes;

    public DiscordCommandHandler(
        ILogger<DiscordCommandHandler> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        
        _slashCommandTypes = new()
        {
            ["showstats"] = typeof(ShowStatsCommand),
            ["showleaderboard"] = typeof(ShowLeaderboardCommand),
            ["opt"] = typeof(OptCommand),
            ["feedback"] = typeof(FeedbackCommand),
            ["check"] = typeof(CheckCommand),
            ["botstats"] = typeof(BotStatsCommand)
        };
        
        _userCommandTypes = new()
        {
            ["Show Stats"] = typeof(ShowStatsUserCommand)
        };
    }

    public async Task RegisterCommandsAsync(DiscordSocketClient client)
    {
        var commands = CommandBuilder.BuildSlashCommands();
        var userCommands = CommandBuilder.BuildUserCommands();
        ApplicationCommandProperties[] allCommands = [.. commands, .. userCommands];

        // Register debug commands to debug guild if configured
        using var scope = _serviceScopeFactory.CreateScope();
        var discordSettings = scope.ServiceProvider.GetRequiredService<IOptions<Configuration.DiscordSettings>>();
        
        if (discordSettings.Value.DebugGuildId.HasValue)
        {
            var debugGuildId = discordSettings.Value.DebugGuildId.Value;
            _ = Task.Run(async () =>
            {
                try
                {
                    var guild = client.GetGuild(debugGuildId);
                    if (guild != null)
                    {
                        // Build debug versions with -debug suffix
                        var debugCommands = CommandBuilder.BuildSlashCommands("-debug");
                        var debugUserCommands = CommandBuilder.BuildUserCommands("-debug");
                        ApplicationCommandProperties[] allDebugCommands = [.. debugCommands, .. debugUserCommands];
                        
                        await guild.BulkOverwriteApplicationCommandAsync(allDebugCommands).ConfigureAwait(false);
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
            _ = Task.Run(async () => await client.BulkOverwriteGlobalApplicationCommandsAsync(allCommands).ConfigureAwait(false));
        }
    }

    public async Task HandleSlashCommandAsync(SocketSlashCommand command, DiscordSocketClient? client)
    {
        try
        {
            var isDebugCommand = command.Data.Name.EndsWith("-debug", StringComparison.OrdinalIgnoreCase);

            if (isDebugCommand)
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var discordSettings = scope.ServiceProvider.GetRequiredService<IOptions<Configuration.DiscordSettings>>();
                if (command.GuildId != discordSettings.Value.DebugGuildId)
                {
                    return;
                }
            }

            // Strip -debug suffix to get the base command name
            var commandName = isDebugCommand
                ? command.Data.Name[..^6]
                : command.Data.Name;

            if (_slashCommandTypes.TryGetValue(commandName, out var commandType))
            {
                _logger.LogInformation(
                    "Handling slash command {CommandName} from user {Username} ({UserId}) in channel {ChannelName} ({ChannelId} in {GuildId})",
                    command.Data.Name,
                    command.User.Username,
                    command.User.Id,
                    command.Channel.Name,
                    command.Channel.Id,
                    command.GuildId);

                _ = Task.Run(async () =>
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var commandInstance = (ISlashCommand)scope.ServiceProvider.GetRequiredService(commandType);
                    await commandInstance.HandleAsync(command, client).ConfigureAwait(false);
                });
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

    public async Task HandleUserCommandAsync(SocketUserCommand command, DiscordSocketClient? client)
    {
        try
        {
            // Strip -debug suffix to get the base command name
            var commandName = command.Data.Name.EndsWith("-debug", StringComparison.OrdinalIgnoreCase)
                ? command.Data.Name[..^6]
                : command.Data.Name;

            if (_userCommandTypes.TryGetValue(commandName, out var commandType))
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

                _ = Task.Run(async () =>
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var commandInstance = (IUserCommand)scope.ServiceProvider.GetRequiredService(commandType);
                    await commandInstance.HandleAsync(command, client).ConfigureAwait(false);
                });
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
}
