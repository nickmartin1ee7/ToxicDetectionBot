namespace ToxicDetectionBot.WebApi.Data;

/// <summary>
/// Represents a D&D alignment spectrum for message classification.
/// Values are ordered from most "good" (highest) to most "evil" (lowest) for sorting purposes.
/// </summary>
public enum AlignmentType
{
    LawfulGood = 9,
    NeutralGood = 8,
    ChaoticGood = 7,
    LawfulNeutral = 6,
    TrueNeutral = 5,
    ChaoticNeutral = 4,
    LawfulEvil = 3,
    NeutralEvil = 2,
    ChaoticEvil = 1
}
