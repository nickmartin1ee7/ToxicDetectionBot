# Discord Command Handler

## Purpose
The DiscordCommandHandler is responsible for registering and handling Discord slash commands and user commands (right-click commands). It acts as a router that directs commands to their respective implementations.

## Key Responsibilities
- Registering slash commands and user commands with Discord
- Routing incoming commands to appropriate command handlers
- Supporting debug command registration for specific guilds
- Logging command execution for monitoring and debugging
- Managing command lifecycle through dependency injection

## Important Properties
- `_slashCommandTypes` - Dictionary mapping command names to their implementation types
- `_userCommandTypes` - Dictionary mapping user command names to their implementation types
- `_logger` - Logging functionality
- `_serviceScopeFactory` - Creates scopes for resolving command dependencies

## Dependencies
- `ILogger<DiscordCommandHandler>` - Logging functionality
- `IServiceScopeFactory` - Creates scopes for dependency resolution
- `DiscordSocketClient` - Discord API client for command registration

## Command Registration
The `RegisterCommandsAsync` method:
1. Builds standard slash and user commands via CommandBuilder
2. Optionally registers debug commands to a configured debug guild
3. Registers commands globally or to specific guild based on configuration
4. Uses bulk overwrite to ensure command registry matches current implementation

## Command Handling
### Slash Commands (`HandleSlashCommandAsync`)
1. Detects if command is a debug variant (ends with "-debug")
2. Validates debug commands against configured debug guild
3. Strips "-debug" suffix to get base command name
4. Looks up command implementation in `_slashCommandTypes`
5. Creates scope and resolves command instance via DI
6. Executes command handling asynchronously

### User Commands (`HandleUserCommandAsync`)
1. Similar to slash command handling but for right-click commands
2. Uses `_userCommandTypes` dictionary for lookup
3. Resolves `IUserCommand` implementations via DI

## Error Handling
- Logs warnings for unknown commands
- Logs errors for execution exceptions
- Sends ephemeral error responses to users when exceptions occur
- Prevents duplicate responses by checking `command.HasResponded`

## Debug Command Support
- Supports parallel command sets for debugging
- Debug commands have "-debug" suffix
- Only registered to configured debug guild
- Allows testing without affecting production command registry

## Command Implementation Resolution
- Uses service scope factory to create scoped service provider
- Resolves command types via `GetRequiredService(commandType)`
- Casts to appropriate interface (`ISlashCommand` or `IUserCommand`)
- Ensures proper disposal of scopes after command execution

## Thread Safety
- Command dictionaries are immutable after construction
- Each command execution gets its own service scope
- Discord.NET handles command invocation threading