namespace ToxicDetectionBot.WebApi.Configuration;

public class DiscordSettings
{
    public static string ConfigKey => nameof(DiscordSettings);
    
    public string? Token { get; set; }
    public string? JsonSchema { get; set; }
    public List<string> AdminList { get; set; } = [];
}
