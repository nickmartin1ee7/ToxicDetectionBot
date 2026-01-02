using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services.CommandHandlers;

public interface IOptCommandHandler
{
    Task HandleOptAsync(SocketSlashCommand command);
}

public class OptCommandHandler : IOptCommandHandler
{
    private readonly ILogger<OptCommandHandler> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public OptCommandHandler(
        ILogger<OptCommandHandler> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task HandleOptAsync(SocketSlashCommand command)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not string choice)
        {
            await command.RespondAsync("Invalid choice.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var userId = command.User.Id.ToString();
        var isOptingOut = choice.Equals("out", StringComparison.OrdinalIgnoreCase);

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var optOut = await dbContext.UserOptOuts.FindAsync(userId).ConfigureAwait(false);

        if (optOut is null)
        {
            optOut = new UserOptOut
            {
                UserId = userId,
                IsOptedOut = isOptingOut,
                LastChangedAt = DateTime.UtcNow
            };
            dbContext.UserOptOuts.Add(optOut);
        }
        else
        {
            optOut.IsOptedOut = isOptingOut;
            optOut.LastChangedAt = DateTime.UtcNow;
        }

        // Delete user data when opting out
        if (isOptingOut)
        {
            await dbContext.UserSentiments
                .Where(s => s.UserId == userId)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            await dbContext.UserSentimentScores
                .Where(s => s.UserId == userId)
                .ExecuteDeleteAsync().ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        var message = isOptingOut
            ? "? You have opted **OUT** of sentiment analysis. Your messages will no longer be evaluated and your existing data has been deleted."
            : "? You have opted **IN** to sentiment analysis. Your messages will now be evaluated.";

        await command.RespondAsync(message, ephemeral: true).ConfigureAwait(false);

        _logger.LogInformation(
            "User {UserId} ({Username}) opted {OptStatus} of sentiment analysis",
            userId, command.User.Username, isOptingOut ? "OUT" : "IN");
    }
}
