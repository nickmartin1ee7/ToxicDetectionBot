# Retention Service

## Purpose
The RetentionService manages the lifecycle of sentiment data by periodically removing old records to maintain database size and respect data minimization principles.

## Key Responsibilities
- Removing UserSentiment records older than configured retention period
- Removing associated UserSentimentScore records for cleaned-up users
- Removing UserAlignmentScore records for cleaned-up users
- Logging retention operations for monitoring and auditing
- Ensuring data privacy compliance through automatic deletion

## Dependencies
- `ILogger<RetentionService>` - Logging functionality
- `IServiceScopeFactory` - Creates scopes for database access
- `AppDbContext` - Entity Framework Core database context
- `IOptions<DiscordSettings>` - Configuration access for retention period

## Retention Logic
1. Calculates cutoff date: DateTime.UtcNow - RetentionInDays
2. Deletes UserSentiment records where CreatedAt < cutoffDate
3. Deletes UserSentimentScore records for users no longer having sentiments
4. Deletes UserAlignmentScore records for users no longer having sentiments
5. Logs counts of deleted records for each table

## Configuration
- Retention period configured via `DiscordSettings:RetentionInDays` (default: 28 days)
- Set to 0 or negative to disable retention purging
- Value represents number of days to retain data before deletion

## Database Operations
Performs deletions from three tables:
1. `UserSentiment` - Raw message sentiment records
2. `UserSentimentScore` - Aggregated user statistics
3. `UserAlignmentScore` - User alignment distribution statistics

## Cascading Deletion Strategy
The service manually handles related record deletion since:
- UserSentimentScore and UserAlignmentScore don't have foreign key constraints to UserSentiment
- Deletion is based on UserId/GuildId combinations
- After removing sentiments, scores for users with no remaining sentiments are cleaned up

## Safety Features
- Only deletes data older than the cutoff (preserves recent data)
- Operations performed within service scopes for proper DI
- Comprehensive logging shows exactly what was removed
- Configurable retention period allows adjustment based on needs
- Can be disabled by setting retention days to 0 or negative

## Usage
Typically invoked by a Hangfire recurring job configured in Program.cs (every 10 minutes)