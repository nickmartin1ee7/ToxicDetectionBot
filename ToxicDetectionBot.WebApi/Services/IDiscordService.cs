namespace ToxicDetectionBot.WebApi.Services;

public interface IDiscordService
{
    Task StartAsync();
    Task StopAsync();
    bool IsRunning { get; }
}
