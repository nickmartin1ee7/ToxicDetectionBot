using Discord;
using Discord.WebSocket;
using Hangfire;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Text.Json;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services;

public class DiscordService : IDiscordService
{
    private readonly ILogger<DiscordService> _logger;
    private readonly IChatClient _chatClient;
    private readonly IOptions<DiscordSettings> _options;
    private readonly IBackgroundJobClient _hangfireBgClient;
    private readonly AppDbContext _appDbContext;

    private static DiscordSocketClient? s_client;
    private static ChatOptions? s_chatOptions;

    private JsonDocument SchemaDoc => 
        JsonDocument.Parse(_options.Value.JsonSchema
            ?? throw new ArgumentNullException(nameof(_options.Value.JsonSchema)));

    public DiscordService(
        ILogger<DiscordService> logger,
        IChatClient chatClient,
        IOptions<DiscordSettings> options,
        IBackgroundJobClient hangfireBgClient,
        AppDbContext appDbContext)
    {
        _logger = logger;
        _chatClient = chatClient;
        _options = options;
        _hangfireBgClient = hangfireBgClient;
        _appDbContext = appDbContext;
    }

    public bool IsRunning => s_client is not null;

    public async Task StartAsync(string token, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Discord client is already running.");
        }

        Initialize();

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
        };

        _logger.LogInformation("Starting Discord client...");

        s_client = new DiscordSocketClient(config);

        s_client.Log += LogAsync;
        s_client.Ready += ReadyAsync;
        s_client.MessageReceived += MessageReceivedAsync;

        await s_client.LoginAsync(TokenType.Bot, token);
        await s_client.StartAsync();

        _logger.LogInformation("Discord client started.");
    }

    public async Task StopAsync()
    {
        if (s_client is null)
        {
            throw new InvalidOperationException("Discord client is not running.");
        }

        _logger.LogInformation("Stopping Discord client...");

        await s_client.StopAsync();
        s_client.Dispose();
        s_client = null;

        _logger.LogInformation("Discord client stopped.");
    }

    public async Task ClassifyMessage(string messageId, string userId, string messageContent)
    {
        var result = _chatClient.GetResponseAsync(
            chatMessage: messageContent,
            options: s_chatOptions)
            .GetAwaiter().GetResult();

        var resultText = result.Text.Trim();

        var cResult = JsonSerializer.Deserialize<ClassificationResult>(resultText);

        _logger.LogInformation("Chat classification for MessageId {MessageId} - {ClassificationResult}. Message: {MessageContent}",
            messageId,
            cResult,
            messageContent);

        _appDbContext.UserSentiments.Add(new UserSentiment
        {
            UserId = userId,
            MessageId = messageId,
            MessageContent = messageContent,
            IsToxic = cResult?.IsToxic ?? false
        });
        await _appDbContext.SaveChangesAsync();
    }

    private void Initialize()
    {
        s_chatOptions ??= new ChatOptions
        {
            Instructions = "Evaluate and classify the user sentiment of the message.",
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                SchemaDoc!.RootElement,
                schemaName: "SentimentAnalysisResult",
                schemaDescription: "Schema to classify a message's sentiment. IsToxic: False represents that the message was toxic/mean. True represents that the message was nice/polite.")
        };
    }

    private Task LogAsync(LogMessage log)
    {
        _logger.LogInformation("Discord client: {Message}", log.ToString());
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        _logger.LogInformation("Discord client is ready for {GuildCount} servers!",
            s_client?.Guilds.Count ?? 0);
        return Task.CompletedTask;
    }

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        if (message.Author.IsBot)
        {
            return;
        }

        var channel = message.Channel;
        var guildChannel = channel as SocketGuildChannel;
        var guildName = guildChannel?.Guild.Name ?? "DM";
        var channelName = channel.Name ?? "Unknown";

        var messageContent = message.CleanContent;

        _logger.LogInformation(
            "Message {MessageId} received from user '{Username}' (ID: {UserId}) in channel '{ChannelName}' (ID: {ChannelId}) in guild '{GuildName}'. Message: {MessageContent}",
            message.Id,
            message.Author.Username,
            message.Author.Id,
            channelName,
            channel.Id,
            guildName,
            messageContent);

        _hangfireBgClient.Enqueue<DiscordService>(ds => ds.ClassifyMessage(message.Id.ToString(), message.Author.Id.ToString(), messageContent));
    }
}

/// <summary>
/// Bound to the JSON Schema
/// </summary>
internal record ClassificationResult(bool IsToxic);
