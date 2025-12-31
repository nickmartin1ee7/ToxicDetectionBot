using Hangfire;
using Microsoft.Extensions.Options;
using ToxicDetectionBot.WebApi.Configuration;

namespace ToxicDetectionBot.WebApi.Services;

public interface IBackgroundJobService
{
    string StartDiscordClient();
    bool StopDiscordClient();
    JobDetails? GetJobDetails(string jobId);
}

public class BackgroundJobService : IBackgroundJobService
{
    private readonly IDiscordService _discordService;
    private readonly ILogger<BackgroundJobService> _logger;
    private readonly IOptions<DiscordSettings> _discordSettings;

    public BackgroundJobService(
        IDiscordService discordService,
        ILogger<BackgroundJobService> logger,
        IOptions<DiscordSettings> discordSettings)
    {
        _discordService = discordService;
        _logger = logger;
        _discordSettings = discordSettings;
    }

    public string StartDiscordClient()
    {
        try
        {
            var token = _discordSettings.Value.Token;
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Discord token is not configured. Please set Discord:Token in configuration.");
            }

            var jobId = BackgroundJob.Enqueue(() => _discordService.StartAsync(token, CancellationToken.None));
            _logger.LogInformation("Discord client start job enqueued with ID: {JobId}", jobId);
            return jobId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue Discord client start job");
            throw;
        }
    }

    public bool StopDiscordClient()
    {
        try
        {
            if (!_discordService.IsRunning)
            {
                _logger.LogWarning("Discord client is not running");
                return false;
            }

            BackgroundJob.Enqueue(() => _discordService.StopAsync());
            _logger.LogInformation("Discord client stop job enqueued");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue Discord client stop job");
            throw;
        }
    }

    public JobDetails? GetJobDetails(string jobId)
    {
        var job = JobStorage.Current.GetConnection().GetJobData(jobId);
        if (job is null)
        {
            return null;
        }

        return new JobDetails
        {
            JobId = jobId,
            State = job.State,
            CreatedAt = job.CreatedAt
        };
    }
}

public class JobDetails
{
    public string JobId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
