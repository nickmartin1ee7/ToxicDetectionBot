using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services.Commands.Helpers;

public static class AlignmentHelper
{
    public static string GetAlignmentEmoji(string alignment) => alignment switch
    {
        nameof(AlignmentType.LawfulGood) => "??",
        nameof(AlignmentType.NeutralGood) => "???",
        nameof(AlignmentType.ChaoticGood) => "??",
        nameof(AlignmentType.LawfulNeutral) => "??",
        nameof(AlignmentType.TrueNeutral) => "??",
        nameof(AlignmentType.ChaoticNeutral) => "??",
        nameof(AlignmentType.LawfulEvil) => "??",
        nameof(AlignmentType.NeutralEvil) => "??",
        nameof(AlignmentType.ChaoticEvil) => "??",
        _ => "?"
    };

    public static string FormatAlignment(string alignment) => alignment switch
    {
        nameof(AlignmentType.LawfulGood) => "Lawful Good",
        nameof(AlignmentType.NeutralGood) => "Neutral Good",
        nameof(AlignmentType.ChaoticGood) => "Chaotic Good",
        nameof(AlignmentType.LawfulNeutral) => "Lawful Neutral",
        nameof(AlignmentType.TrueNeutral) => "True Neutral",
        nameof(AlignmentType.ChaoticNeutral) => "Chaotic Neutral",
        nameof(AlignmentType.LawfulEvil) => "Lawful Evil",
        nameof(AlignmentType.NeutralEvil) => "Neutral Evil",
        nameof(AlignmentType.ChaoticEvil) => "Chaotic Evil",
        _ => alignment
    };

    public static string GetGlobalDominantAlignment(List<UserAlignmentScore> alignmentScores)
    {
        if (alignmentScores.Count == 0)
            return nameof(AlignmentType.TrueNeutral);

        var totalAlignments = new Dictionary<string, int>
        {
            [nameof(AlignmentType.LawfulGood)] = alignmentScores.Sum(a => a.LawfulGoodCount),
            [nameof(AlignmentType.NeutralGood)] = alignmentScores.Sum(a => a.NeutralGoodCount),
            [nameof(AlignmentType.ChaoticGood)] = alignmentScores.Sum(a => a.ChaoticGoodCount),
            [nameof(AlignmentType.LawfulNeutral)] = alignmentScores.Sum(a => a.LawfulNeutralCount),
            [nameof(AlignmentType.TrueNeutral)] = alignmentScores.Sum(a => a.TrueNeutralCount),
            [nameof(AlignmentType.ChaoticNeutral)] = alignmentScores.Sum(a => a.ChaoticNeutralCount),
            [nameof(AlignmentType.LawfulEvil)] = alignmentScores.Sum(a => a.LawfulEvilCount),
            [nameof(AlignmentType.NeutralEvil)] = alignmentScores.Sum(a => a.NeutralEvilCount),
            [nameof(AlignmentType.ChaoticEvil)] = alignmentScores.Sum(a => a.ChaoticEvilCount)
        };

        return totalAlignments.OrderByDescending(kvp => kvp.Value).First().Key;
    }
}
