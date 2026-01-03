using Discord.WebSocket;

namespace ToxicDetectionBot.WebApi.Services.Commands;

public interface ISlashCommand
{
    Task HandleAsync(SocketSlashCommand command, DiscordSocketClient? client);
}
