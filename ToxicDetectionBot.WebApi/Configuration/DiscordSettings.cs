namespace ToxicDetectionBot.WebApi.Configuration;

public class DiscordSettings
{
    public static string ConfigKey => nameof(DiscordSettings);
    
    public string Token { get; set; } = string.Empty;
}
