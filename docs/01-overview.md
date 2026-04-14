# ToxicDetectionBot Architecture Overview

## System Description

ToxicDetectionBot is a Discord bot that monitors server messages and performs sentiment analysis to detect toxic behavior using AI-powered classification. It's built as an ASP.NET Core web application that integrates with Discord to automatically analyze message sentiment.

## Core Components

1. **Discord Integration Layer** - Handles connection to Discord API
2. **Message Processing Pipeline** - Captures, classifies, and stores message data
3. **Background Job System** - Manages asynchronous tasks via Hangfire
4. **Data Storage Layer** - Entity Framework Core with SQLite
5. **Command Interface** - Slash commands for user interaction
6. **AI Classification Service** - Integration with Ollama for sentiment analysis

## Technology Stack

- **.NET 10** - Core framework
- **ASP.NET Core** - Web host and dependency injection
- **Discord.Net 3.18** - Discord API integration
- **Hangfire 1.8** - Background job scheduling
- **Entity Framework Core 10** - ORM and database management
- **SQLite** - Local database storage
- **Ollama Integration** - Via CommunityToolkit.Aspire.OllamaSharp
- **Aspire ServiceDefaults** - Cloud-native application patterns

## Key Features

- Automated message analysis with opt-out privacy controls
- User statistics and server leaderboards
- Background jobs for summarization and data retention
- Admin features for privileged users
- Comprehensive logging and monitoring capabilities

## Data Flow

1. Message Reception → Discord Gateway Event
2. User Validation → Opt-out check
3. Job Enqueue → Hangfire background job
4. AI Classification → Ollama LLM evaluation
5. Storage → SQLite database via EF Core
6. Summarization → Periodic aggregation (every minute)
7. Retention → Old data purge (every 10 minutes)
8. User Interaction → Slash commands query aggregated data

## Directory Structure

- `Controllers` - API endpoints for service management
- `Services` - Core business logic (Discord service, command handling, background jobs)
- `Services/Commands` - Individual slash and user command implementations
- `Data` - Entity Framework Core models and database context
- `Configuration` - Settings classes
- `Migrations` - Database migration files