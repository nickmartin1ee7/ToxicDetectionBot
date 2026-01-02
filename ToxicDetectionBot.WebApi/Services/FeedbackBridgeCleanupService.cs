namespace ToxicDetectionBot.WebApi.Services;

/// <summary>
/// Service responsible for cleaning up expired feedback bridges
/// </summary>
public class FeedbackBridgeCleanupService
{
    private readonly IFeedbackBridgeService _feedbackBridgeService;
    private readonly ILogger<FeedbackBridgeCleanupService> _logger;

    public FeedbackBridgeCleanupService(
        IFeedbackBridgeService feedbackBridgeService,
        ILogger<FeedbackBridgeCleanupService> logger)
    {
        _feedbackBridgeService = feedbackBridgeService;
        _logger = logger;
    }

    public async Task CleanupExpiredBridgesAsync()
    {
        _logger.LogInformation("Running feedback bridge cleanup...");
        await _feedbackBridgeService.CleanupExpiredBridgesAsync().ConfigureAwait(false);
        _logger.LogInformation("Feedback bridge cleanup completed");
    }
}
