using Microsoft.AspNetCore.Mvc;
using ToxicDetectionBot.WebApi.Services;

namespace ToxicDetectionBot.WebApi.Controllers;

[ApiController]
[Route("service")]
public class ServiceController : ControllerBase
{
    private readonly IBackgroundJobService _backgroundJobService;
    private readonly ILogger<ServiceController> _logger;

    public ServiceController(IBackgroundJobService backgroundJobService, ILogger<ServiceController> logger)
    {
        _backgroundJobService = backgroundJobService;
        _logger = logger;
    }

    [HttpPost("start")]
    public ActionResult<StartServiceResponse> Start()
    {
        try
        {
            var jobId = _backgroundJobService.StartDiscordClient();
            _logger.LogInformation("Discord client start requested. Job ID: {JobId}", jobId);

            return Ok(new StartServiceResponse(true, jobId, string.IsNullOrEmpty(jobId)
                ? "Discord client already running"
                : "Discord client start job enqueued successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Discord client");
            return StatusCode(500, new StartServiceResponse(false, null, "An error occurred while starting the Discord client"));
        }
    }

    [HttpPost("stop")]
    public ActionResult<StopServiceResponse> Stop()
    {
        try
        {
            var success = _backgroundJobService.StopDiscordClient();
            _logger.LogInformation("Discord client stop requested. Success: {Success}", success);

            return Ok(new StopServiceResponse(success, success ? "Discord client stop job enqueued successfully" : "Discord client is not running"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Discord client");
            return StatusCode(500, new StopServiceResponse(false, "An error occurred while stopping the Discord client"));
        }
    }
}

public record StartServiceResponse(bool Success, string? JobId, string Message);

public record StopServiceResponse(bool Success, string Message);
