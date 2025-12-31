using Discord.WebSocket;

namespace ToxicDetectionBot.WebApi.Services;

public class DiscordService : IDiscordService
{
    private DiscordSocketClient? _client;

    public bool IsRunning => _client is not null;

    public async Task StartAsync(string token, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Discord client is already running.");
        }

        _client = new DiscordSocketClient();

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;

        await _client.LoginAsync(Discord.TokenType.Bot, token);
        await _client.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Discord client is not running.");
        }

        await _client.StopAsync();
        _client.Dispose();
        _client = null;
    }

    private Task LogAsync(Discord.LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        Console.WriteLine("Discord client is ready!");
        return Task.CompletedTask;
    }
}
