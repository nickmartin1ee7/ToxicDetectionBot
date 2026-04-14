# ToxicDetectionBot Documentation

This directory contains detailed documentation for the ToxicDetectionBot project, designed to help developers understand and contribute to the codebase.

## Documentation Structure

| File | Description |
|------|-------------|
| [01-overview.md](01-overview.md) | High-level architecture overview and system description |
| [02-discord-service.md](02-discord-service.md) | Details about the DiscordService which manages the Discord connection and message handling |
| [03-background-job-service.md](03-background-job-service.md) | Information about the BackgroundJobService that manages Hangfire jobs |
| [04-discord-command-handler.md](04-discord-command-handler.md) | Explanation of the command routing and handling system |
| [05-sentiment-summarizer-service.md](05-sentiment-summarizer-service.md) | Details about the sentiment data aggregation service |
| [06-retention-service.md](06-retention-service.md) | Information about data lifecycle management and cleanup |
| [07-data-models.md](07-data-models.md) | Comprehensive guide to the Entity Framework Core data models |
| [08-commands.md](08-commands.md) | Detailed explanation of the slash commands and user commands system |

## Getting Started

For a quick introduction to the project's purpose and features, see the [main README.md](../README.md) in the project root.

## Contributing

When making changes to the codebase, please refer to the relevant documentation files above to understand how different components interact. Keep this documentation updated as you modify the system.

## Architecture Summary

ToxicDetectionBot follows a modular architecture with clearly separated concerns:

1. **Discord Integration Layer** - Handles real-time communication with Discord
2. **Message Processing Pipeline** - Captures, validates, and classifies messages
3. **Background Job System** - Manages asynchronous tasks via Hangfire
4. **Data Storage Layer** - Entity Framework Core with SQLite persistence
5. **Command Interface** - Slash commands for user interaction and administration
6. **AI Classification Service** - Integration with Ollama for sentiment analysis

Each service is designed to be independently testable and replaceable while maintaining clear interfaces with other components.