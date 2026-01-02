# ToxicDetectionBot

A Discord bot that monitors server messages and performs sentiment analysis to detect toxic behavior using AI-powered classification.

## Overview

ToxicDetectionBot is an ASP.NET Core web application that integrates with Discord to automatically analyze the sentiment of messages sent in Discord servers. It uses Ollama's local LLM capabilities to classify messages as toxic or non-toxic, maintaining statistical records to help server moderators identify patterns of negative behavior.

## Core Functionality

### Automated Message Analysis

The bot monitors all messages sent in Discord servers where it's installed. For each message:

1. **Message Capture**: The bot receives every non-bot message through Discord's gateway
2. **Opt-Out Check**: Verifies if the user has opted out of sentiment analysis
3. **AI Classification**: Sends the message to an Ollama-powered chat model for sentiment evaluation
4. **Database Storage**: Records the message metadata, content, and toxicity classification
5. **Background Processing**: Message classification happens asynchronously via Hangfire jobs

### Privacy Controls

Users have complete control over their participation:

- **Opt-Out System**: Users can opt out of sentiment analysis at any time
- **Data Deletion**: Opting out immediately deletes all historical sentiment data for that user
- **Opt-In**: Users who previously opted out can opt back in to resume analysis
- **Ephemeral Responses**: Privacy-related commands respond privately to the user

### Statistics and Leaderboards

#### User Statistics

Individual user statistics are calculated on a per-server basis and include:

- Total number of messages analyzed
- Count of toxic messages
- Count of non-toxic messages  
- Toxicity percentage (toxic messages / total messages × 100)
- Last update timestamp

Statistics are displayed in rich Discord embeds with user avatars and formatted data fields.

#### Server Leaderboards

The leaderboard shows top users ranked by:

1. **Primary Sort**: Total message count (descending)
2. **Secondary Sort**: Toxicity percentage (descending)

**Standard Users**: See a leaderboard of up to 10 users who are currently in their server

**Admin Users**: See a global leaderboard of up to 50 users across all servers, including:
- Username
- Guild (server) name
- Channel name where they were last active
- Complete statistics

### Background Jobs

The bot uses Hangfire to run scheduled maintenance tasks:

#### Sentiment Summarization (Every Minute)

- Processes all unsummarized sentiment records
- Aggregates statistics per user
- Updates or creates `UserSentimentScore` records with running totals
- Marks processed sentiments as summarized
- Logs overall toxicity statistics across all processed users

#### Data Retention Purge (Every 10 Minutes)

- Removes sentiment data older than the configured retention period (default: 28 days)
- Helps maintain database size and respects data minimization principles
- Configurable through `DiscordSettings.RetentionInDays`

## Slash Commands

All slash commands are registered globally and available in all servers:

### `/showstats`

Displays sentiment statistics for a specific user.

- **Parameter**: `user` (required) - The Discord user to show stats for
- **Scope**: Server-specific (only shows data from the current server)
- **Response**: Public embed with user statistics
- **Special Cases**:
  - Shows opt-out notice if user has opted out
  - Shows "no data" message if user has no analyzed messages in the server
  - Only works in server channels (not DMs)

### `/showleaderboard`

Displays the toxicity leaderboard.

- **Parameters**: None
- **Scope**: 
  - Regular users: Server-specific top 10
  - Admin users: Global top 50 across all servers
- **Response**: 
  - Public for regular users
  - Ephemeral (private) for admin users
- **Ranking**: Medals (🥇🥈🥉) for top 3, numbered for the rest
- **Display Format**:
  - Regular: `{rank} {username} - {percentage}% toxic ({toxic}/{total} messages)`
  - Admin: `{rank} {username} (Guild: {guild}, Channel: {channel}) - {percentage}% toxic ({toxic}/{total} messages)`

### `/opt`

Allows users to opt in or out of sentiment analysis.

- **Parameter**: `choice` (required) - "Out" or "In"
- **Response**: Ephemeral (private) confirmation message
- **Effects**:
  - **Opting Out**: 
    - Stops future message analysis
    - Deletes all existing `UserSentiment` records
    - Deletes all `UserSentimentScore` records
    - Records opt-out status with timestamp
  - **Opting In**:
    - Resumes message analysis
    - Updates opt-out status to false
    - Records opt-in timestamp

## Data Model

### UserSentiment

Individual message analysis records:

- `Id`: Auto-incrementing primary key
- `UserId`: Discord user ID (string)
- `GuildId`: Discord server ID (string)
- `MessageId`: Discord message ID (string)
- `MessageContent`: The analyzed message text
- `Username`: Discord username at time of message
- `GuildName`: Server name
- `ChannelName`: Channel name
- `IsToxic`: Classification result (boolean)
- `IsSummarized`: Whether included in aggregate statistics (boolean)
- `CreatedAt`: Timestamp (UTC)

### UserSentimentScore

Aggregated statistics per user:

- `Id`: Auto-incrementing primary key
- `UserId`: Discord user ID (string)
- `TotalMessages`: Running total of analyzed messages
- `ToxicMessages`: Running total of toxic messages
- `NonToxicMessages`: Running total of non-toxic messages
- `ToxicityPercentage`: Calculated percentage (0-100)
- `SummarizedAt`: Last update timestamp (UTC)

### UserOptOut

Privacy preferences:

- `UserId`: Discord user ID (string, primary key)
- `IsOptedOut`: Current opt-out status (boolean)
- `LastChangedAt`: Timestamp of last status change (UTC)

## AI Classification

### Chat Model Integration

The bot uses Microsoft's `IChatClient` abstraction with Ollama backend:

- **Model Purpose**: Sentiment analysis and classification
- **Instructions**: Configurable via `SentimentSystemPrompt` setting
- **Response Format**: Structured JSON conforming to a predefined schema
- **Schema**: `{ "IsToxic": boolean }`

### Classification Logic

The AI model evaluates messages with reversed polarity semantics:

- `IsToxic: false` → Message was **toxic/mean**
- `IsToxic: true` → Message was **nice/polite**

**Note**: The schema description in configuration uses reversed semantics, which affects how the model interprets toxicity. The actual storage and display logic may need verification for semantic accuracy.

## Architecture

### Technology Stack

- **.NET 10**: Core framework
- **ASP.NET Core**: Web host and dependency injection
- **Discord.Net 3.18**: Discord API integration
- **Hangfire 1.8**: Background job scheduling
- **Entity Framework Core 10**: ORM and database management
- **SQLite**: Local database storage
- **Ollama Integration**: Via CommunityToolkit.Aspire.OllamaSharp
- **Aspire ServiceDefaults**: Cloud-native application patterns

### Service Architecture

- **DiscordService**: Main bot lifecycle and message handling
- **SlashCommandHandler**: Slash command routing and execution
- **SentimentSummarizerService**: Periodic statistics aggregation
- **RetentionService**: Data lifecycle management
- **BackgroundJobService**: Hangfire job coordination

### Logging

Comprehensive logging throughout all services:

- Message reception with full metadata
- Command execution tracking
- Classification results
- Opt-in/opt-out events
- Background job summaries
- Error handling with context

## Admin Features

Users in the `DiscordSettings.AdminList` configuration array receive:

- **Global Leaderboard Access**: View statistics across all servers (up to 50 users)
- **Enhanced Context**: See guild and channel information in leaderboards
- **Private Responses**: Admin leaderboard responses are ephemeral

## Configuration

Key settings in `appsettings.json`:

```json
{
  "DiscordSettings": {
    "Token": "",                           // Discord bot token
    "JsonSchema": "...",                   // AI response schema definition
    "SentimentSystemPrompt": "",           // System prompt for AI sentiment analysis
    "AdminList": [],                       // Array of Discord user IDs with admin privileges
    "RetentionInDays": 28,                 // Data retention period in days
    "FeedbackWebhookUrl": ""               // Optional Discord webhook URL for feedback
  }
}
```

### Configuration Details

- **Token**: Your Discord bot token from the Discord Developer Portal
- **JsonSchema**: JSON schema defining the structure of AI responses (must include `IsToxic` boolean property)
- **SentimentSystemPrompt**: Instructions for the AI model on how to evaluate message sentiment (e.g., "Evaluate and classify the user sentiment of the message whether it is toxic, rude, or mean")
- **AdminList**: List of Discord user IDs (as strings) that have elevated privileges
- **RetentionInDays**: Number of days to retain sentiment data before automatic deletion (default: 28)
- **FeedbackWebhookUrl**: Optional webhook URL for sending feedback or notifications

## Data Flow

1. **Message Reception** → Discord Gateway Event
2. **User Validation** → Opt-out check
3. **Job Enqueue** → Hangfire background job
4. **AI Classification** → Ollama LLM evaluation using configured system prompt
5. **Storage** → SQLite database via EF Core
6. **Summarization** → Periodic aggregation (every minute)
7. **Retention** → Old data purge (every 10 minutes)
8. **User Interaction** → Slash commands query aggregated data

## Database Management

- **SQLite Database**: `ToxicDetectionBot.db` in application content root
- **Auto-Migration**: Database created and migrated on startup
- **Schema Management**: EF Core handles schema evolution
- **Storage Location**: Relative to the application's ContentRootPath

## Monitoring

- **Hangfire Dashboard**: Available at `/hangfire` endpoint
- **Swagger UI**: Available in development mode at `/swagger`
- **Comprehensive Logging**: All major operations logged with structured data
- **Health Endpoints**: Aspire ServiceDefaults provide health check endpoints

## Design Principles

- **Privacy-First**: Users control their participation and data
- **Asynchronous Processing**: Non-blocking message analysis
- **Scalable Architecture**: Background jobs prevent Discord API rate limiting
- **Data Minimization**: Automatic purging of old data
- **Observability**: Extensive logging for debugging and monitoring
- **Modular Design**: Clear separation of concerns across services
- **Configurable AI**: System prompts can be customized without code changes
