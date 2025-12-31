namespace ToxicDetectionBot.WebApi.Data;

public class UserSentimentScore
{
    public required string UserId { get; set; }
    public int TotalMessages { get; set; }
    public int ToxicMessages { get; set; }
    public int NonToxicMessages { get; set; }
    public double ToxicityPercentage { get; set; }
    public DateTime SummarizedAt { get; set; } = DateTime.UtcNow;
}
