using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using ToxicDetectionBot.WebApi.Constants;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services.CommandHandlers;

public interface IShowStatsCommandHandler
{
    Task HandleShowStatsAsync(SocketSlashCommand command);
    Task HandleShowStatsUserCommandAsync(SocketUserCommand command);
}

public class ShowStatsCommandHandler : IShowStatsCommandHandler
{
    private readonly ILogger<ShowStatsCommandHandler> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public ShowStatsCommandHandler(
        ILogger<ShowStatsCommandHandler> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task HandleShowStatsAsync(SocketSlashCommand command)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not SocketUser user)
        {
            await command.RespondAsync("User has no sentiment yet.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (command.Channel is not SocketGuildChannel { Guild: var guild })
        {
            await command.RespondAsync("This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId = user.Id.ToString();
        var guildId = guild.Id.ToString();

        var sentiments = await dbContext.UserSentiments
            .Where(s => s.UserId == userId && s.GuildId == guildId)
            .ToListAsync().ConfigureAwait(false);

        var optOut = await dbContext.UserOptOuts.FirstOrDefaultAsync(o => o.UserId == userId).ConfigureAwait(false);

        var embed = BuildUserStatsEmbed(user, sentiments, optOut);
        await command.RespondAsync(embed: embed).ConfigureAwait(false);
    }

    public async Task HandleShowStatsUserCommandAsync(SocketUserCommand command)
    {
        var user = command.Data.Member;

        if (command.Channel is not SocketGuildChannel { Guild: var guild })
        {
            await command.RespondAsync("This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId = user.Id.ToString();
        var guildId = guild.Id.ToString();

        var sentiments = await dbContext.UserSentiments
            .Where(s => s.UserId == userId && s.GuildId == guildId)
            .ToListAsync().ConfigureAwait(false);

        var optOut = await dbContext.UserOptOuts.FirstOrDefaultAsync(o => o.UserId == userId).ConfigureAwait(false);

        var embed = BuildUserStatsEmbed(user, sentiments, optOut);
        await command.RespondAsync(embed: embed).ConfigureAwait(false);
    }

    private static Embed BuildUserStatsEmbed(SocketUser user, List<UserSentiment> sentiments, UserOptOut? optOut)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"Sentiment Stats for {user.Username}")
            .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
            .WithColor(DiscordConstants.BrandColor)
            .WithCurrentTimestamp();

        if (optOut?.IsOptedOut == true)
        {
            embed.WithDescription("?? This user has opted out of sentiment analysis.");
        }
        else if (sentiments.Count == 0)
        {
            embed.WithDescription("No stats available for this user in this server yet.");
        }
        else
        {
            var totalMessages = sentiments.Count;
            var toxicMessages = sentiments.Count(s => s.IsToxic);
            var nonToxicMessages = totalMessages - toxicMessages;
            var toxicityPercentage = totalMessages > 0 ? (double)toxicMessages / totalMessages * 100 : 0;
            var lastUpdated = sentiments.Max(s => s.CreatedAt);
            var timestamp = new DateTimeOffset(lastUpdated).ToUnixTimeSeconds();

            embed
                .AddField("Total Messages", totalMessages.ToString(), inline: true)
                .AddField("Toxic Messages", toxicMessages.ToString(), inline: true)
                .AddField("Non-Toxic Messages", nonToxicMessages.ToString(), inline: true)
                .AddField("Toxicity Percentage", $"{toxicityPercentage:F2}%", inline: true)
                .AddField("Last Updated (UTC)", $"<t:{timestamp}:R>", inline: true);
        }

        return embed.Build();
    }
}
