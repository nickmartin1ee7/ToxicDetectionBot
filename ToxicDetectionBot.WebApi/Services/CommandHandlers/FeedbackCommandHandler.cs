using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Constants;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services.CommandHandlers;

public interface IFeedbackCommandHandler
{
    Task HandleFeedbackAsync(SocketSlashCommand command, DiscordSocketClient client);
}

public class FeedbackCommandHandler : IFeedbackCommandHandler
{
    private readonly ILogger<FeedbackCommandHandler> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<DiscordSettings> _discordSettings;

    public FeedbackCommandHandler(
        ILogger<FeedbackCommandHandler> logger,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<DiscordSettings> discordSettings)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _discordSettings = discordSettings;
    }

    public async Task HandleFeedbackAsync(SocketSlashCommand command, DiscordSocketClient client)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not string message)
        {
            await command.RespondAsync("Please provide a feedback message.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            await command.DeferAsync().ConfigureAwait(false);

            var feedbackEmbed = BuildFeedbackEmbed(command.User, message);

            // Send DM to the user
            var userDM = await command.User.CreateDMChannelAsync().ConfigureAwait(false);
            var userResponse = "✅ Thank you for your feedback! A developer may reply to you here.";
            await userDM.SendMessageAsync(text: userResponse, embed: feedbackEmbed).ConfigureAwait(false);

            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var retentionDays = _discordSettings.Value.FeedbackBridgeRetentionDays;
            var expiresAt = DateTime.UtcNow.AddDays(retentionDays);

            // Send DM to each admin and create feedback bridge entries
            foreach (var adminId in _discordSettings.Value.AdminList)
            {
                if (ulong.TryParse(adminId, out var adminUserId))
                {
                    var adminUser = client.GetUser(adminUserId);
                    if (adminUser is not null)
                    {
                        try
                        {
                            var adminDM = await adminUser.CreateDMChannelAsync().ConfigureAwait(false);
                            var adminEmbed = BuildAdminFeedbackEmbed(command.User, message);
                            var adminMessage = await adminDM.SendMessageAsync(embed: adminEmbed).ConfigureAwait(false);

                            // Save the feedback bridge to database
                            var feedbackBridge = new FeedbackBridge
                            {
                                UserId = command.User.Id.ToString(),
                                AdminId = adminId,
                                AdminEmbedMessageId = adminMessage.Id.ToString(),
                                LatestFeedbackContent = message,
                                ExpiresAt = expiresAt
                            };
                            dbContext.FeedbackBridges.Add(feedbackBridge);
                        }
                        catch (Exception dmEx)
                        {
                            _logger.LogWarning(dmEx, "Failed to send feedback DM to admin {AdminId}",
                                adminId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Admin user {AdminId} not found", adminId);
                    }
                }
            }

            await dbContext.SaveChangesAsync().ConfigureAwait(false);

            await command.FollowupAsync("✅ Thank you for your feedback! Your message has been sent to the developer.", ephemeral: true).ConfigureAwait(false);

            _logger.LogInformation(
                "Feedback submitted by user {UserId} ({Username}): {Message}",
                command.User.Id,
                command.User.Username,
                message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending feedback from user {UserId} ({Username})",
                command.User.Id,
                command.User.Username);

            await command.RespondAsync("❌ An error occurred while sending feedback. Please try again later.", ephemeral: true).ConfigureAwait(false);
        }
    }

    private static Embed BuildFeedbackEmbed(SocketUser user, string message)
    {
        return new EmbedBuilder()
            .WithTitle($"{DiscordConstants.FeedbackEmoji} Your Feedback")
            .WithDescription(message)
            .WithColor(DiscordConstants.BrandColor)
            .WithCurrentTimestamp()
            .Build();
    }

    private static Embed BuildAdminFeedbackEmbed(SocketUser user, string message)
    {
        return new EmbedBuilder()
            .WithTitle($"{DiscordConstants.FeedbackEmoji} New Feedback")
            .WithDescription(message)
            .WithColor(DiscordConstants.BrandColor)
            .AddField("User", $"{user.Username} ({user.Id})", inline: true)
            .WithFooter("Reply to this message to respond to the user")
            .WithCurrentTimestamp()
            .Build();
    }
}
