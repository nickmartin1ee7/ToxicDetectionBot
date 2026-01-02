using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Constants;

namespace ToxicDetectionBot.WebApi.Services.CommandHandlers;

public interface ICheckCommandHandler
{
    Task HandleCheckAsync(SocketSlashCommand command);
}

public class CheckCommandHandler : ICheckCommandHandler
{
    private readonly ILogger<CheckCommandHandler> _logger;
    private readonly IChatClient _chatClient;
    private readonly IOptions<DiscordSettings> _discordSettings;
    private readonly ChatOptions? _chatOptions;
    private readonly ChatClientMetadata? _metadata;

    private JsonDocument SchemaDoc =>
        JsonDocument.Parse(_discordSettings.Value.JsonSchema
            ?? throw new ArgumentNullException(nameof(_discordSettings.Value.JsonSchema)));

    public CheckCommandHandler(
        ILogger<CheckCommandHandler> logger,
        IChatClient chatClient,
        IOptions<DiscordSettings> discordSettings)
    {
        _logger = logger;
        _chatClient = chatClient;
        _discordSettings = discordSettings;

        _chatOptions ??= new ChatOptions
        {
            Instructions = _discordSettings.Value.SentimentSystemPrompt
                ?? throw new ArgumentNullException(nameof(_discordSettings.Value.SentimentSystemPrompt)),
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                SchemaDoc.RootElement,
                schemaName: "SentimentAnalysisResult",
                schemaDescription: "Schema to classify a message's sentiment. IsToxic: False represents that the message was toxic/mean. True represents that the message was nice/polite.")
        };

        _metadata = _chatClient.GetService<ChatClientMetadata>();
    }

    public async Task HandleCheckAsync(SocketSlashCommand command)
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
            var embedColor = classificationResult?.IsToxic == true ? DiscordConstants.ToxicColor : DiscordConstants.BrandColor;
            var model = _metadata?.DefaultModelId ?? "Unknown Model";

            var sentimentText = classificationResult?.IsToxic == true 
                ? $"{DiscordConstants.ToxicEmoji} Toxic" 
                : $"{DiscordConstants.NiceEmoji} Nice";

            var embed = new EmbedBuilder()
                .WithTitle($"{DiscordConstants.CheckEmoji} Toxicity Check Result")
                .WithDescription($"**Message:**{Environment.NewLine}{message}")
                .WithColor(embedColor)
                .AddField("Sentiment", sentimentText, inline: true)
                .AddField("Model", $"Text evaluated with `{model}`.", inline: true)
                .WithFooter($"Completed in `{sw.ElapsedMilliseconds} ms`.")
                .WithCurrentTimestamp()
                .Build();

            await command.FollowupAsync(embed: embed, ephemeral: true).ConfigureAwait(false);

            _logger.LogInformation(
                "Check command used by user {UserId} ({Username}). Message: '{Message}', Result: {IsToxic}",
                command.User.Id,
                command.User.Username,
                message,
                classificationResult?.IsToxic ?? false);
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

internal record ClassificationResult(bool IsToxic);
