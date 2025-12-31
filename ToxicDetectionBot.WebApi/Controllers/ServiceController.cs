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

            return Ok(new StartServiceResponse
            {
                Success = true,
                JobId = jobId,
                Message = "Discord client start job enqueued successfully"
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation when starting Discord client");
            return BadRequest(new StartServiceResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Discord client");
            return StatusCode(500, new StartServiceResponse
            {
                Success = false,
                Message = "An error occurred while starting the Discord client"
            });
        }
    }

    [HttpPost("stop")]
    public ActionResult<StopServiceResponse> Stop()
    {
        try
        {
            var success = _backgroundJobService.StopDiscordClient();
            _logger.LogInformation("Discord client stop requested. Success: {Success}", success);

            return Ok(new StopServiceResponse
            {
                Success = success,
                Message = success ? "Discord client stop job enqueued successfully" : "Discord client is not running"
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation when stopping Discord client");
            return BadRequest(new StopServiceResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Discord client");
            return StatusCode(500, new StopServiceResponse
            {
                Success = false,
                Message = "An error occurred while stopping the Discord client"
            });
        }
    }
}

public class StartServiceResponse
{
    public bool Success { get; set; }
    public string? JobId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class StopServiceResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ServiceStatusResponse
{
    public string Message { get; set; } = string.Empty;
}
