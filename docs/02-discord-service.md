# Discord Service

## Purpose
The DiscordService is responsible for managing the connection to the Discord API, handling incoming messages, and coordinating with other services for message classification.

## Key Responsibilities
- Establishing and maintaining Discord WebSocket connection
- Processing incoming messages through Discord gateway events
- Enqueuing messages for sentiment analysis via Hangfire
- Handling slash and user commands via DiscordCommandHandler
- Managing bot lifecycle (start/stop operations)

## Important Properties
- `IsRunning` - Indicates whether the Discord client is currently connected
- `s_client` - Static reference to the DiscordSocketClient instance
- `s_chatOptions` - Static configuration for AI chat interactions

## Dependencies
- `ILogger<DiscordService>` - Logging functionality
- `IChatClient` - Interface for AI chat model interaction (Ollama)
- `IOptions<DiscordSettings>` - Configuration access
- `IDiscordCommandHandler` - Command processing delegation
- `IServiceScopeFactory` - Creates scopes for database access
- `IBackgroundJobClient` - Hangfire job enqueuing

## Event Handlers
1. `LogAsync` - Logs Discord client activity
2. `ReadyAsync` - Called when bot is ready, registers commands
3. `SlashCommandExecutedAsync` - Handles slash commands
4. `UserCommandExecutedAsync` - Handles user commands (right-click)
5. `MessageReceivedAsync` - Main message processing pipeline

## Message Processing Flow
1. Receive message via `MessageReceivedAsync`
2. Ignore bot messages and empty content
3. Extract message metadata (user, guild, channel info)
4. Skip processing for messages containing URLs
5. Check if user has opted out of analysis
6. Enqueue message for classification via Hangfire

## Classification Process
Messages are classified asynchronously through the `ClassifyMessage` method:
1. Sends message content to Ollama AI model via `IChatClient`
2. Parses JSON response into `ClassificationResult`
3. Stores sentiment data in database via Entity Framework Core
4. Logs classification results for monitoring

## Configuration Requirements
- Discord bot token (`DiscordSettings:Token`)
- JSON schema for AI response format (`DiscordSettings:JsonSchema`)
- System prompt for sentiment analysis (`DiscordSettings:SentimentSystemPrompt`)