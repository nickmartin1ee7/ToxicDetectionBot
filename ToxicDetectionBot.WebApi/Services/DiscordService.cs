using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Text.Json;
using ToxicDetectionBot.WebApi.Configuration;

namespace ToxicDetectionBot.WebApi.Services;

public class DiscordService : IDiscordService
{
    private static DiscordSocketClient? s_client;
    private readonly ILogger<DiscordService> _logger;
    private readonly IChatClient _chatClient;
    private readonly IOptions<DiscordSettings> _options;

    private JsonDocument SchemaDoc => 
        JsonDocument.Parse(_options.Value.JsonSchema
            ?? throw new ArgumentNullException(nameof(_options.Value.JsonSchema)));

    public DiscordService(
        ILogger<DiscordService> logger,
        IChatClient chatClient,
        IOptions<DiscordSettings> options)
    {
        _logger = logger;
        _chatClient = chatClient;
        _options = options;
    }

    public bool IsRunning => s_client is not null;

    public async Task StartAsync(string token, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Discord client is already running.");
        }

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

        _logger.LogInformation(
            "Message {MessageId} received from user '{Username}' (ID: {UserId}) in channel '{ChannelName}' (ID: {ChannelId}) in guild '{GuildName}'. Message: {MessageContent}",
            message.Id,
            message.Author.Username,
            message.Author.Id,
            channelName,
            channel.Id,
            guildName,
            message.CleanContent);

        var chatOptions = new ChatOptions
        {
            Instructions = "Evaluate and classify the user sentiment of the message.",
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                SchemaDoc!.RootElement,
                schemaName: "SentimentAnalysisResult",
                schemaDescription: "Schema to classify a message's sentiment. " +
                "The 'Response' int represents a 0-100 range. 0 = toxic. 1 = nice. The scale is confidence.")
        };

        var result = await _chatClient.GetResponseAsync(
            chatMessage: message.CleanContent,
            options: chatOptions);

        _logger.LogInformation("Chat response to MessageId {MessageId}: {AiMessageContent}",
            message.Id,
            result.Text.Trim());
    }
}
