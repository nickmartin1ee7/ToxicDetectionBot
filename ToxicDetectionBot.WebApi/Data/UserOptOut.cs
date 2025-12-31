namespace ToxicDetectionBot.WebApi.Data;

public class UserOptOut
{
    public required string UserId { get; set; }
    public bool IsOptedOut { get; set; }
    public DateTime LastChangedAt { get; set; } = DateTime.UtcNow;
}
