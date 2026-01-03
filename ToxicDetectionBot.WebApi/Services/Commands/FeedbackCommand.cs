using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Services.Commands.Helpers;

namespace ToxicDetectionBot.WebApi.Services.Commands;

public class FeedbackCommand : ISlashCommand
{
    private readonly ILogger<FeedbackCommand> _logger;
    private readonly IOptions<DiscordSettings> _discordSettings;
    private readonly IHttpClientFactory _httpClientFactory;

    public FeedbackCommand(
        ILogger<FeedbackCommand> logger,
        IOptions<DiscordSettings> discordSettings,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _discordSettings = discordSettings;
        _httpClientFactory = httpClientFactory;
    }

    public async Task HandleAsync(SocketSlashCommand command, DiscordSocketClient? client)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not string message)
        {
            await command.RespondAsync("Please provide a feedback message.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var webhookUrl = _discordSettings.Value.FeedbackWebhookUrl;

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogWarning("Feedback command used but FeedbackWebhookUrl is not configured");
            await command.RespondAsync("Feedback system is not configured. Please contact the administrator.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            var guildName = command.Channel is SocketGuildChannel { Guild: var guild }
                ? guild.Name
                : "DM";

            var embed = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"{DiscordConstants.FeedbackEmoji} New Feedback",
                        description = message,
                        color = DiscordConstants.BrandColor,
                        fields = new[]
                        {
                            new { name = "User", value = $"{command.User.Username} ({command.User.Id})", inline = true },
                            new { name = "Server", value = guildName, inline = true },
                            new { name = "Channel", value = command.Channel.Name, inline = true }
                        },
                        timestamp = DateTimeOffset.UtcNow.ToString("o")
                    }
                }
            };

            var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.PostAsJsonAsync(webhookUrl, embed).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                await command.RespondAsync($"{DiscordConstants.ThankYouEmoji} Thank you for your feedback! Your message has been sent to the developer.", ephemeral: true).ConfigureAwait(false);

                _logger.LogInformation(
                    "Feedback submitted by user {UserId} ({Username}) from server {GuildName}: {Message}",
                    command.User.Id,
                    command.User.Username,
                    guildName,
                    message);
            }
            else
            {
                _logger.LogError(
                    "Failed to send feedback webhook. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode,
                    await response.Content.ReadAsStringAsync());

                await command.RespondAsync($"{DiscordConstants.ErrorEmoji} Failed to send feedback. Please try again later.", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending feedback from user {UserId} ({Username})",
                command.User.Id,
                command.User.Username);

            await command.RespondAsync($"{DiscordConstants.ErrorEmoji} An error occurred while sending feedback. Please try again later.", ephemeral: true);
        }
    }
}
