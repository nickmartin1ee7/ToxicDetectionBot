using Discord;
using Discord.WebSocket;

namespace ToxicDetectionBot.WebApi.Services;

public class DiscordService : IDiscordService
{
    private static DiscordSocketClient? s_client;
    private readonly ILogger<DiscordService> _logger;

    public DiscordService(ILogger<DiscordService> logger)
    {
        _logger = logger;
    }

    public bool IsRunning => s_client is not null;

    public async Task StartAsync(string token, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Discord client is already running.");
        }

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
        };

        _logger.LogInformation("Starting Discord client...");

        s_client = new DiscordSocketClient(config);

        s_client.Log += LogAsync;
        s_client.Ready += ReadyAsync;
        s_client.MessageReceived += MessageReceivedAsync;

        await s_client.LoginAsync(TokenType.Bot, token);
        await s_client.StartAsync();

        _logger.LogInformation("Discord client started.");
    }

    public async Task StopAsync()
    {
        if (s_client is null)
        {
            throw new InvalidOperationException("Discord client is not running.");
        }

        _logger.LogInformation("Stopping Discord client...");

        await s_client.StopAsync();
        s_client.Dispose();
        s_client = null;

        _logger.LogInformation("Discord client stopped.");
    }

    private Task LogAsync(LogMessage log)
    {
        _logger.LogInformation("Discord client: {Message}", log.ToString());
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        _logger.LogInformation("Discord client is ready!");
        return Task.CompletedTask;
    }

    private Task MessageReceivedAsync(SocketMessage message)
    {
        if (message.Author.IsBot)
        {
            return Task.CompletedTask;
        }

        var channel = message.Channel;
        var guildChannel = channel as SocketGuildChannel;
        var guildName = guildChannel?.Guild.Name ?? "DM";
        var channelName = channel.Name ?? "Unknown";

        _logger.LogInformation(
            "Message received from user '{Username}' (ID: {UserId}) in channel '{ChannelName}' (ID: {ChannelId}) in guild '{GuildName}'",
            message.Author.Username,
            message.Author.Id,
            channelName,
            channel.Id,
            guildName);

        return Task.CompletedTask;
    }
}
