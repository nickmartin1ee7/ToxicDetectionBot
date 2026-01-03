using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Data;
using ToxicDetectionBot.WebApi.Services.Commands.Helpers;

namespace ToxicDetectionBot.WebApi.Services.Commands;

public class CheckCommand : ISlashCommand
{
    private readonly ILogger<CheckCommand> _logger;
    private readonly IChatClient _chatClient;
    private readonly ChatOptions _chatOptions;
    private readonly ChatClientMetadata? _metadata;

    public CheckCommand(
        ILogger<CheckCommand> logger,
        IChatClient chatClient,
        IOptions<DiscordSettings> discordSettings)
    {
        _logger = logger;
        _chatClient = chatClient;

        var schemaDoc = JsonDocument.Parse(discordSettings.Value.JsonSchema
            ?? throw new ArgumentNullException(nameof(discordSettings.Value.JsonSchema)));

        _chatOptions = new ChatOptions
        {
            Instructions = discordSettings.Value.SentimentSystemPrompt
                ?? throw new ArgumentNullException(nameof(discordSettings.Value.SentimentSystemPrompt)),
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                schemaDoc.RootElement,
                schemaName: "SentimentAnalysisResult",
                schemaDescription: "Schema to classify a message's sentiment. IsToxic: False represents that the message was toxic/mean. True represents that the message was nice/polite.")
        };

        _metadata = _chatClient.GetService<ChatClientMetadata>();
    }

    public async Task HandleAsync(SocketSlashCommand command, DiscordSocketClient? client)
    {
        if (command.Data.Options.FirstOrDefault()?.Value is not string message)
        {
            await command.RespondAsync("Please provide a message to check.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await command.DeferAsync(ephemeral: true).ConfigureAwait(false);

        try
        {
            var sw = new Stopwatch();
            sw.Start();
            var result = await _chatClient.GetResponseAsync(
                chatMessage: message,
                options: _chatOptions).ConfigureAwait(false);
            sw.Stop();

            var resultText = result.Text.Trim();
            var classificationResult = JsonSerializer.Deserialize<ClassificationResult>(resultText);
            var model = _metadata?.DefaultModelId ?? "Unknown Model";

            var alignment = classificationResult?.Alignment ?? "TrueNeutral";
            var isToxic = classificationResult?.IsToxic ?? false;

            var embed = EmbedHelper.BuildToxicityCheckEmbed(
                message,
                isToxic,
                alignment,
                model,
                sw.ElapsedMilliseconds);

            await command.FollowupAsync(embed: embed, ephemeral: true).ConfigureAwait(false);

            _logger.LogInformation(
                "Check command used by user {UserId} ({Username}). Message: '{Message}', Result: {IsToxic}, Alignment: {Alignment}",
                command.User.Id,
                command.User.Username,
                message,
                isToxic,
                alignment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking message toxicity for user {UserId} ({Username})",
                command.User.Id,
                command.User.Username);

            await command.FollowupAsync("? An error occurred while checking the message. Please try again later.", ephemeral: true).ConfigureAwait(false);
        }
    }
}
