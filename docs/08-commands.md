# Command System

## Overview
ToxicDetectionBot implements a flexible command system that supports both slash commands and user commands (right-click commands). The system uses a handler-based approach where each command is implemented as a separate class that implements a specific interface.

## Architecture

### Interfaces
- `ISlashCommand` - Defines contract for slash command handlers
- `IUserCommand` - Defines contract for user command handlers
- `IDiscordCommandHandler` - Central command routing and registration

### Implementation Pattern
Each command follows this structure:
1. Implement the appropriate interface (`ISlashCommand` or `IUserCommand`)
2. Register the command type in `DiscordCommandHandler` constructor
3. Implement the `HandleAsync` method with command-specific logic
4. Use dependency injection for required services
5. Return appropriate responses to users

## Command Categories

### Slash Commands
These are invoked via `/command-name` syntax:

1. **`/showstats`** - Shows statistics for a specific user
   - Parameter: `user` (required) - Discord user to show stats for
   - Scope: Server-specific data only
   - Response: Public embed with user statistics

2. **`/showleaderboard`** - Shows toxicity leaderboard
   - Parameters: None
   - Scope: 
     - Regular users: Server-specific top 10
     - Admin users: Global top 50 across all servers
   - Response: 
     - Public for regular users
     - Ephemeral (private) for admin users

3. **`/opt`** - Manage opt-in/opt-out status
   - Parameter: `choice` (required) - "Out" or "In"
   - Response: Ephemeral confirmation message
   - Effects: 
     - Opting out stops analysis and deletes historical data
     - Opting in resumes analysis

4. **`/check`** - Check if a user is opted out
   - Parameter: `user` (optional) - User to check (defaults to command user)
   - Response: Ephemeral message showing opt-out status

5. **`/feedback`** - Submit feedback about the bot
   - Parameter: `message` (required) - Feedback text
   - Response: Ephemeral confirmation
   - Effect: Sends feedback to configured webhook URL

6. **`/botstats`** - Shows overall bot statistics
   - Parameters: None
   - Response: Ephemeral message with bot-wide statistics
   - Scope: Available to all users

### User Commands
These are invoked via right-click on a user -> Apps -> command-name:

1. **`Show Stats`** - Shows statistics for the targeted user
   - Target: Single user (via right-click)
   - Response: Public embed with user statistics
   - Equivalent to: `/showstats user:<targeted-user>`

## Command Registration
Commands are registered automatically through:
1. `CommandBuilder.BuildSlashCommands()` - Creates slash command definitions
2. `CommandBuilder.BuildUserCommands()` - Creates user command definitions
3. `DiscordCommandHandler.RegisterCommandsAsync()` - Registers with Discord API
4. Debug command support for `-debug` variants in specific guilds

## Execution Flow
1. User invokes command via Discord interface
2. Discord gateway sends interaction event to bot
3. `DiscordService` receives event and routes to `DiscordCommandHandler`
4. Handler looks up command type in its dictionaries
5. Handler creates scope and resolves command implementation via DI
6. Command's `HandleAsync` method executes with command context
7. Command performs its logic and responds to user
8. Scope is disposed after command completion

## Dependencies
Commands typically depend on:
- `ILogger<T>` - For logging command execution
- `IServiceScopeFactory` - For creating scopes to access scoped services
- `AppDbContext` - For database access (via scoped services)
- Other specialized services as needed

## Response Types
- **Public responses** - Visible to everyone in the channel
- **Ephemeral responses** - Only visible to the command invoker
- Used for privacy-sensitive information like opt-out status or personal statistics

## Error Handling
- Commands should catch and log exceptions
- Unhandled exceptions are caught by `DiscordCommandHandler`
- Error responses are sent as ephemeral messages when possible
- Logging includes command name, user ID, and contextual information

## Extending Commands
To add a new command:
1. Create new class implementing `ISlashCommand` or `IUserCommand`
2. Add command type to appropriate dictionary in `DiscordCommandHandler` constructor
3. Implement `HandleAsync` method with command logic
4. Register command properties in `CommandBuilder` if needed
5. Inject required services via constructor
6. Build and deploy - commands will be registered automatically

## Special Features
- **Debug Commands** - Commands can be registered with `-debug` suffix for testing in specific guilds
- **Parameter Handling** - Built-in support for Discord's parameter system
- **Response Types** - Easy distinction between public and ephemeral responses
- **Localization Ready** - Responses can be easily adapted for multiple languages