namespace ToxicDetectionBot.WebApi.Data;

public class UserAlignmentScore
{
    public required string UserId { get; set; }
    public required string GuildId { get; set; }
    public int LawfulGoodCount { get; set; }
    public int NeutralGoodCount { get; set; }
    public int ChaoticGoodCount { get; set; }
    public int LawfulNeutralCount { get; set; }
    public int TrueNeutralCount { get; set; }
    public int ChaoticNeutralCount { get; set; }
    public int LawfulEvilCount { get; set; }
    public int NeutralEvilCount { get; set; }
    public int ChaoticEvilCount { get; set; }
    public string DominantAlignment { get; set; } = nameof(AlignmentType.TrueNeutral);
    public DateTime SummarizedAt { get; set; } = DateTime.UtcNow;
}
