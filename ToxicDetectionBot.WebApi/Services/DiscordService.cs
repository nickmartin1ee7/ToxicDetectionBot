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

        s_client = new DiscordSocketClient();

        s_client.Log += LogAsync;
        s_client.Ready += ReadyAsync;

        await s_client.LoginAsync(Discord.TokenType.Bot, token);
        await s_client.StartAsync();
    }

    public async Task StopAsync()
    {
        if (s_client is null)
        {
            throw new InvalidOperationException("Discord client is not running.");
        }

        await s_client.StopAsync();
        s_client.Dispose();
        s_client = null;
    }

    private Task LogAsync(Discord.LogMessage log)
    {
        _logger.LogInformation("Discord: {Message}", log.ToString());
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        _logger.LogInformation("Discord client is ready!");
        return Task.CompletedTask;
    }
}
