namespace ToxicDetectionBot.WebApi.Services;

public interface IDiscordService
{
    Task StartAsync(string token, CancellationToken cancellationToken = default);
    Task StopAsync();
    bool IsRunning { get; }
}
