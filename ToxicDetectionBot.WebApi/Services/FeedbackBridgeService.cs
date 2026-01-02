using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Constants;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services;

public class FeedbackBridgeService : IFeedbackBridgeService
{
    private readonly ILogger<FeedbackBridgeService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<DiscordSettings> _discordSettings;
    private DiscordSocketClient? _client;

    public FeedbackBridgeService(
        ILogger<FeedbackBridgeService> logger,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<DiscordSettings> discordSettings)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _discordSettings = discordSettings;
    }

    public void SetClient(DiscordSocketClient client)
    {
        _client = client;
    }

    public async Task HandleFeedbackBridgeAsync(SocketMessage message, SocketDMChannel dmChannel)
    {
        if (_client is null) return;

        var senderId = message.Author.Id.ToString();
        var isAdmin = _discordSettings.Value.AdminList.Contains(senderId);

        // Check if this is a reply to an embed (admin replying to feedback)
        if (isAdmin && message.Reference?.MessageId.IsSpecified == true)
        {
            await HandleAdminReplyAsync(message, dmChannel).ConfigureAwait(false);
            return;
        }

        // Check if this is a user message that should be bridged to admins
        if (!isAdmin)
        {
            await HandleUserMessageAsync(message, dmChannel).ConfigureAwait(false);
        }
    }

    public async Task CleanupExpiredBridgesAsync()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expiredBridges = await dbContext.FeedbackBridges
            .Where(b => b.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync().ConfigureAwait(false);

        if (expiredBridges.Count > 0)
        {
            dbContext.FeedbackBridges.RemoveRange(expiredBridges);
            await dbContext.SaveChangesAsync().ConfigureAwait(false);
            _logger.LogInformation("Cleaned up {Count} expired feedback bridges", expiredBridges.Count);
        }
    }

    private async Task HandleAdminReplyAsync(SocketMessage message, SocketDMChannel dmChannel)
    {
        var referencedMessageId = message.Reference!.MessageId.Value.ToString();
        var adminId = message.Author.Id.ToString();

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Find the feedback bridge by the referenced embed message ID
        var bridge = await dbContext.FeedbackBridges
            .Where(b => b.AdminEmbedMessageId == referencedMessageId && b.AdminId == adminId && b.ExpiresAt > DateTime.UtcNow)
            .FirstOrDefaultAsync().ConfigureAwait(false);

        if (bridge is null)
        {
            _logger.LogDebug("Admin {AdminId} replied to message {MessageId} but no active feedback bridge found", adminId, referencedMessageId);
            return;
        }

        try
        {
            var user = _client!.GetUser(ulong.Parse(bridge.UserId));
            if (user is null)
            {
                _logger.LogWarning("Could not find user {UserId} for feedback bridge", bridge.UserId);
                return;
            }

            var userDM = await user.CreateDMChannelAsync().ConfigureAwait(false);

            var embed = new EmbedBuilder()
                .WithTitle($"{DiscordConstants.ResponseEmoji} Response from Developer")
                .WithDescription(message.CleanContent)
                .WithColor(DiscordConstants.BrandColor)
                .WithCurrentTimestamp()
                .Build();

            await userDM.SendMessageAsync(embed: embed).ConfigureAwait(false);

            // Update last message timestamp and extend expiration
            bridge.LastMessageAt = DateTime.UtcNow;
            bridge.ExpiresAt = DateTime.UtcNow.AddDays(_discordSettings.Value.FeedbackBridgeRetentionDays);
            await dbContext.SaveChangesAsync().ConfigureAwait(false);

            _logger.LogInformation("Bridged admin {AdminId} reply to user {UserId}", adminId, bridge.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bridge admin reply to user {UserId}", bridge.UserId);
        }
    }

    private async Task HandleUserMessageAsync(SocketMessage message, SocketDMChannel dmChannel)
    {
        var userId = message.Author.Id.ToString();

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Find existing bridges for this user
        var existingBridges = await dbContext.FeedbackBridges
            .Where(b => b.UserId == userId && b.ExpiresAt > DateTime.UtcNow)
            .ToListAsync().ConfigureAwait(false);

        if (existingBridges.Count == 0)
        {
            // No active feedback bridge - this is just a regular DM, ignore
            _logger.LogDebug("DM received from {UserId} but no active feedback bridge found", userId);
            return;
        }

        var user = _client!.GetUser(ulong.Parse(userId));
        var username = user?.Username ?? "Unknown User";

        // Send embed to each admin and update their bridge with the new embed message ID
        foreach (var bridge in existingBridges)
        {
            try
            {
                var admin = _client.GetUser(ulong.Parse(bridge.AdminId));
                if (admin is null)
                {
                    _logger.LogWarning("Could not find admin {AdminId} for feedback bridge", bridge.AdminId);
                    continue;
                }

                var adminDM = await admin.CreateDMChannelAsync().ConfigureAwait(false);

                var embed = new EmbedBuilder()
                    .WithTitle($"{DiscordConstants.ResponseEmoji} Reply from {username}")
                    .WithDescription(message.CleanContent)
                    .WithColor(DiscordConstants.BrandColor)
                    .AddField("User", $"{username} ({userId})", inline: true)
                    .AddField("Original Feedback", TruncateString(bridge.LatestFeedbackContent, 200), inline: false)
                    .WithFooter("Reply to this message to respond to the user")
                    .WithCurrentTimestamp()
                    .Build();

                var adminMessage = await adminDM.SendMessageAsync(embed: embed).ConfigureAwait(false);

                // Update the bridge with the new embed message ID so admin can reply to this one
                bridge.AdminEmbedMessageId = adminMessage.Id.ToString();
                bridge.LastMessageAt = DateTime.UtcNow;
                bridge.ExpiresAt = DateTime.UtcNow.AddDays(_discordSettings.Value.FeedbackBridgeRetentionDays);

                _logger.LogInformation("Bridged user {UserId} message to admin {AdminId}", userId, bridge.AdminId);
            }
            catch (Discord.Net.HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
            {
                _logger.LogDebug("Cannot send message to admin {AdminId}, silently failing", bridge.AdminId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to bridge user message to admin {AdminId}, silently failing", bridge.AdminId);
            }
        }

        await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }
}
