using Discord.WebSocket;

namespace ToxicDetectionBot.WebApi.Services;

public interface IFeedbackBridgeService
{
    /// <summary>
    /// Handles feedback bridging for DM messages.
    /// Admin replies to embeds are bridged to users, user messages are bridged to admins.
    /// </summary>
    Task HandleFeedbackBridgeAsync(SocketMessage message, SocketDMChannel dmChannel);

    /// <summary>
    /// Cleans up expired feedback bridges based on configured retention period
    /// </summary>
    Task CleanupExpiredBridgesAsync();
}
