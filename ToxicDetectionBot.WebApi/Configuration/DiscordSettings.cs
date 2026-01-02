namespace ToxicDetectionBot.WebApi.Configuration;

public class DiscordSettings
{
    public static string ConfigKey => nameof(DiscordSettings);
    
    public string? Token { get; set; }
    public string? JsonSchema { get; set; }
    public string? SentimentSystemPrompt { get; set; }
    public List<string> AdminList { get; set; } = [];
    public int RetentionInDays { get; set; } = 28;
    public string? FeedbackWebhookUrl { get; set; }
    public ulong? DebugGuildId { get; set; }
}
