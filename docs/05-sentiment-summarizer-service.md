# Sentiment Summarizer Service

## Purpose
The SentimentSummarizerService processes raw sentiment data and aggregates it into summary statistics for efficient querying and reporting.

## Key Responsibilities
- Processing unsummarized UserSentiment records
- Calculating aggregated statistics per user per guild
- Updating or creating UserSentimentScore records
- Marking processed sentiments as summarized
- Logging summary statistics for monitoring

## Dependencies
- `ILogger<SentimentSummarizerService>` - Logging functionality
- `IServiceScopeFactory` - Creates scopes for database access
- `AppDbContext` - Entity Framework Core database context

## Processing Flow
1. Query all UserSentiment records where IsSummarized = false
2. Group records by UserId and GuildId
3. For each group:
   - Calculate totals: total messages, toxic messages, non-toxic messages
   - Compute toxicity percentage (toxic/total * 100)
   - Update existing UserSentimentScore or create new record
   - Mark all processed sentiments as summarized (IsSummarized = true)
4. Log overall statistics including total processed users and toxicity percentages

## Database Operations
- Reads from UserSentiment table (raw message classifications)
- Writes to UserSentimentScore table (aggregated statistics)
- Updates UserSentiment.IsSummarized flag to prevent double-processing

## Aggregation Logic
- TotalMessages: Count of all processed messages
- ToxicMessages: Count of messages where IsToxic = false (note: reversed semantics)
- NonToxicMessages: Count of messages where IsToxic = true (note: reversed semantics)
- ToxicityPercentage: (ToxicMessages / TotalMessages) * 100

## Important Notes
- The service runs on a scheduled basis (every minute via Hangfire)
- Uses transactions to ensure data consistency
- Handles edge cases like zero-division when calculating percentages
- Processes data in batches to avoid memory issues with large datasets
- The reversed toxicity semantics (false = toxic, true = non-toxic) are handled consistently

## Usage
Typically invoked by a Hangfire recurring job configured in Program.cs