using Discord.WebSocket;

namespace ToxicDetectionBot.WebApi.Services.Commands;

public interface IUserCommand
{
    Task HandleAsync(SocketUserCommand command, DiscordSocketClient? client);
}
