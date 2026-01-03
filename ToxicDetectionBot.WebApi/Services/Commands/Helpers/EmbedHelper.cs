using Discord;
using Discord.WebSocket;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services.Commands.Helpers;

public static class EmbedHelper
{
    public static Embed BuildUserStatsEmbed(SocketUser user, UserSentimentScore? sentimentScore, UserAlignmentScore? alignmentScore, UserOptOut? optOut)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"Sentiment Stats for {user.Username}")
            .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
            .WithColor(DiscordConstants.BrandColor)
            .WithCurrentTimestamp();

        if (optOut?.IsOptedOut == true)
        {
            embed.WithDescription("🚫 This user has opted out of sentiment analysis.");
        }
        else if (sentimentScore is null || sentimentScore.TotalMessages == 0)
        {
            embed.WithDescription("No stats available for this user in this server yet.");
        }
        else
        {
            var timestamp = new DateTimeOffset(DateTime.SpecifyKind(sentimentScore.SummarizedAt, DateTimeKind.Utc)).ToUnixTimeSeconds();

            embed
                .AddField("Total Messages", sentimentScore.TotalMessages.ToString(), inline: true)
                .AddField("Toxic Messages", sentimentScore.ToxicMessages.ToString(), inline: true)
                .AddField("Non-Toxic Messages", sentimentScore.NonToxicMessages.ToString(), inline: true)
                .AddField("Toxicity Percentage", $"{sentimentScore.ToxicityPercentage:F2}%", inline: true);

            // Add alignment info if available
            if (alignmentScore is not null)
            {
                var dominantAlignment = alignmentScore.DominantAlignment;
                embed.AddField("Dominant Alignment", $"{AlignmentHelper.GetAlignmentEmoji(dominantAlignment)} {AlignmentHelper.FormatAlignment(dominantAlignment)}", inline: true);

                if (timestamp > 0)
                {
                    embed.AddField("Last Updated", $"<t:{timestamp}:R>", inline: true);
                }

                // Build top 3 alignment distribution
                var alignmentCounts = new Dictionary<string, int>
                {
                    [nameof(AlignmentType.LawfulGood)] = alignmentScore.LawfulGoodCount,
                    [nameof(AlignmentType.NeutralGood)] = alignmentScore.NeutralGoodCount,
                    [nameof(AlignmentType.ChaoticGood)] = alignmentScore.ChaoticGoodCount,
                    [nameof(AlignmentType.LawfulNeutral)] = alignmentScore.LawfulNeutralCount,
                    [nameof(AlignmentType.TrueNeutral)] = alignmentScore.TrueNeutralCount,
                    [nameof(AlignmentType.ChaoticNeutral)] = alignmentScore.ChaoticNeutralCount,
                    [nameof(AlignmentType.LawfulEvil)] = alignmentScore.LawfulEvilCount,
                    [nameof(AlignmentType.NeutralEvil)] = alignmentScore.NeutralEvilCount,
                    [nameof(AlignmentType.ChaoticEvil)] = alignmentScore.ChaoticEvilCount
                };

                var totalAlignmentMessages = alignmentCounts.Values.Sum();
                var topAlignments = alignmentCounts
                    .Where(kvp => kvp.Value > 0)
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(3);

                if (topAlignments.Any())
                {
                    var alignmentDistribution = string.Join("\n", topAlignments.Select(kvp =>
                    {
                        var percentage = totalAlignmentMessages > 0 ? (double)kvp.Value / totalAlignmentMessages * 100 : 0;
                        var emoji = AlignmentHelper.GetAlignmentEmoji(kvp.Key);
                        return $"{emoji} **{AlignmentHelper.FormatAlignment(kvp.Key)}**: {kvp.Value} ({percentage:F1}%)";
                    }));

                    embed.AddField("Alignment Distribution", alignmentDistribution, inline: false);
                }
            }
            else
            {
                embed.AddField("Last Updated", $"<t:{timestamp}:R>", inline: true);
            }
        }

        return embed.Build();
    }

    public static Embed BuildLeaderboardEmbed(
        SocketGuild guild,
        List<(string UserId, double ToxicityPercentage, string Alignment, int TotalMessages, string? Username, string? GuildName, string? ChannelName)> leaderboard,
        bool isGlobalView,
        bool isToxicitySort)
    {
        var sortType = isToxicitySort ? "Toxicity" : "Alignment";
        var title = isGlobalView
            ? $"🌍🐍 Global {sortType} Leaderboard"
            : $"🐍 {sortType} Leaderboard - {guild.Name}";

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(DiscordConstants.BrandColor)
            .WithCurrentTimestamp();

        if (leaderboard.Count == 0)
        {
            embed.WithDescription(isGlobalView
                ? "No global stats available yet."
                : "No stats available for this server yet.");
            return embed.Build();
        }

        var description = string.Join('\n', leaderboard.Select((stat, index) =>
        {
            var medal = GetRankMedal(index);
            var (userId, toxicityPercentage, alignment, totalMessages, username, guildName, channelName) = stat;
            var alignmentEmoji = AlignmentHelper.GetAlignmentEmoji(alignment);
            var alignmentFormatted = AlignmentHelper.FormatAlignment(alignment);
            var displayName = username ?? $"Unknown User ({userId})";

            if (isGlobalView)
            {
                var guildInfo = guildName != null ? $" (Guild: {guildName})" : "";

                return $"{medal} **{displayName}** - {alignmentEmoji} {alignmentFormatted} | {toxicityPercentage:F1}% toxic | {totalMessages} msgs{guildInfo}";
            }
            else
            {
                return $"{medal} **{displayName}** - {alignmentEmoji} {alignmentFormatted} | {toxicityPercentage:F1}% toxic | {totalMessages} msgs";
            }
        }));

        embed.WithDescription(description);
        return embed.Build();
    }

    public static Embed BuildBotStatsEmbed(
        TimeSpan uptime,
        double memoryMb,
        TimeSpan cpuTime,
        int threadCount,
        int guildCount,
        int totalSentiments,
        int totalUsers,
        string? dbSize)
    {
        var embed = new EmbedBuilder()
            .WithTitle("📊 Bot Statistics")
            .WithColor(DiscordConstants.BrandColor)
            .WithCurrentTimestamp();

        // System Information
        var systemInfo = $"**Uptime:** {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m\n" +
                        $"**Memory:** {memoryMb:F2} MB\n" +
                        $"**CPU Time:** {cpuTime.TotalSeconds:F1}s\n" +
                        $"**Threads:** {threadCount}";
        embed.AddField("💻 System", systemInfo, inline: true);

        // Discord Information
        var discordInfo = $"**Guilds:** {guildCount}";
        embed.AddField("🎮 Discord", discordInfo, inline: true);

        // Database Statistics
        var dbStats = $"**Total Messages:** {totalSentiments:N0}\n" +
                     $"**Users:** {totalUsers:N0}\n";

        if (!string.IsNullOrWhiteSpace(dbSize))
        {
            dbStats += $"**Database Size:** {dbSize}";
        }

        embed.AddField("💾 Database", dbStats, inline: false);

        return embed.Build();
    }

    public static Embed BuildToxicityCheckEmbed(
        string message,
        bool isToxic,
        string alignment,
        string model,
        long elapsedMilliseconds)
    {
        var embedColor = isToxic ? DiscordConstants.ErrorColor : DiscordConstants.BrandColor;
        var alignmentEmoji = AlignmentHelper.GetAlignmentEmoji(alignment);
        var alignmentFormatted = AlignmentHelper.FormatAlignment(alignment);

        var embed = new EmbedBuilder()
            .WithTitle("🔍 Toxicity Check Result")
            .WithDescription($"**Message:**{Environment.NewLine}{message}")
            .WithColor(embedColor)
            .AddField("Sentiment", isToxic ? "☠️ Toxic" : "✨ Nice", inline: true)
            .AddField("Alignment", $"{alignmentEmoji} {alignmentFormatted}", inline: true)
            .AddField("Model", $"Text evaluated with `{model}`.", inline: true)
            .WithFooter($"Completed in `{elapsedMilliseconds} ms`.")
            .WithCurrentTimestamp()
            .Build();

        return embed;
    }

    private static string GetRankMedal(int index) => index switch
    {
        0 => "🥇",
        1 => "🥈",
        2 => "🥉",
        _ => $"{index + 1}."
    };
}
