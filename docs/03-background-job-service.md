# Background Job Service

## Purpose
The BackgroundJobService manages Hangfire background jobs for Discord bot operations, providing a clean interface for starting and stopping the bot.

## Key Responsibilities
- Enqueuing Hangfire jobs for Discord client start/stop operations
- Providing job status tracking capabilities
- Coordinating with DiscordService for actual bot operations
- Logging job-related activities

## Important Methods
- `StartDiscordClient()` - Enqueues job to start Discord bot
- `StopDiscordClient()` - Enqueues job to stop Discord bot
- `GetJobDetails(string jobId)` - Retrieves status of a specific job

## Dependencies
- `IDiscordService` - Actual Discord bot operations
- `ILogger<BackgroundJobService>` - Logging functionality
- `Hangfire.BackgroundJob` - Job enqueuing mechanism
- `Hangfire.JobStorage.Current` - Job status retrieval

## Implementation Details
### Start Operation
1. Checks if Discord client is already running
2. If not running, enqueues job to call `_discordService.StartAsync()`
3. Returns Hangfire job ID for tracking
4. Returns empty string if already running

### Stop Operation
1. Checks if Discord client is running
2. If running, enqueues job to call `_discordService.StopAsync()`
3. Returns boolean indicating success
4. Returns false if not running

### Job Details
Retrieves job information from Hangfire storage including:
- Job ID
- Current state
- Creation timestamp

## Usage
Called by ServiceController to handle HTTP requests for bot management:
- POST /service/starts → StartDiscordClient()
- POST /service/stop → StopDiscordClient()

## Thread Safety
The service relies on Hangfire's built-in concurrency controls and DiscordService's internal state checking to prevent race conditions.