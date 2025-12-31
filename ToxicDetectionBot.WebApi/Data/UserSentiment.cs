namespace ToxicDetectionBot.WebApi.Data;

public class UserSentiment
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string GuildId { get; set; }
    public required string MessageId { get; set; }
    public required string MessageContent { get; set; }
    public required string Username { get; set; }
    public required string GuildName { get; set; }
    public required string ChannelName { get; set; }
    public bool IsToxic { get; set; }
    public bool IsSummarized { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
