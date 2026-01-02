namespace ToxicDetectionBot.WebApi.Data;

/// <summary>
/// Represents a feedback bridge between a user and an admin.
/// Tracks the admin's embed message ID so replies to that embed can be bridged back to the user.
/// </summary>
public class FeedbackBridge
{
    public int Id { get; set; }

    /// <summary>
    /// The user who submitted the feedback
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// The admin who receives the feedback
    /// </summary>
    public required string AdminId { get; set; }

    /// <summary>
    /// The message ID of the feedback embed sent to the admin (used to identify replies)
    /// </summary>
    public required string AdminEmbedMessageId { get; set; }

    /// <summary>
    /// The latest feedback message content from the user (for context in admin embeds)
    /// </summary>
    public required string LatestFeedbackContent { get; set; }

    /// <summary>
    /// When the feedback bridge was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the feedback bridge expires and should be cleaned up
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the feedback bridge was last used (message sent)
    /// </summary>
    public DateTime? LastMessageAt { get; set; }
}
