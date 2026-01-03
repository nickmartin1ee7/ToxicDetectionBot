using Discord;
using Discord.WebSocket;
using Hangfire;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System;
using System.Text.Json;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Data;

namespace ToxicDetectionBot.WebApi.Services;

public class DiscordService : IDiscordService
{
    private readonly ILogger<DiscordService> _logger;
    private readonly IChatClient _chatClient;
    private readonly IOptions<DiscordSettings> _options;
    private readonly IBackgroundJobClient _bg;
    private readonly IDiscordCommandHandler _discordCommandHandler;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private static DiscordSocketClient? s_client;
    private static ChatOptions? s_chatOptions;

    private JsonDocument SchemaDoc => 
        JsonDocument.Parse(_options.Value.JsonSchema
            ?? throw new ArgumentNullException(nameof(_options.Value.JsonSchema)));

    public DiscordService(
        ILogger<DiscordService> logger,
        IChatClient chatClient,
        IOptions<DiscordSettings> options,
        IDiscordCommandHandler discordCommandHandler,
        IServiceScopeFactory serviceScopeFactory,
         IBackgroundJobClient bg)
    {
        _logger = logger;
        _chatClient = chatClient;
        _options = options;
        _discordCommandHandler = discordCommandHandler;
        _serviceScopeFactory = serviceScopeFactory;
        _bg = bg;
    }

    public bool IsRunning => s_client is not null;

    public async Task StartAsync()
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
        s_client.SlashCommandExecuted += SlashCommandExecutedAsync;
        s_client.UserCommandExecuted += UserCommandExecutedAsync;

        await s_client.LoginAsync(TokenType.Bot, _options.Value.Token
            ?? throw new ArgumentNullException(nameof(_options.Value.Token))).ConfigureAwait(false);
        await s_client.StartAsync().ConfigureAwait(false);

        _logger.LogInformation("Discord client started.");
    }

    public async Task StopAsync()
    {
        if (s_client is null)
        {
            throw new InvalidOperationException("Discord client is not running.");
        }

        _logger.LogInformation("Stopping Discord client...");

        await s_client.StopAsync().ConfigureAwait(false);
        s_client.Dispose();
        s_client = null;

        _logger.LogInformation("Discord client stopped.");
    }

    public async Task ClassifyMessage(string messageId, string userId, string messageContent, string username, string guildId, string guildName, string channelName)
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


        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        dbContext.UserSentiments.Add(new UserSentiment
        {
            UserId = userId,
            GuildId = guildId,
            MessageId = messageId,
            MessageContent = messageContent,
            Username = username,
            GuildName = guildName,
            ChannelName = channelName,
            IsToxic = cResult?.IsToxic ?? false
        });
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    private void Initialize()
    {
        s_chatOptions ??= new ChatOptions
        {
            Instructions = _options.Value.SentimentSystemPrompt
                ?? throw new ArgumentNullException(nameof(_options.Value.SentimentSystemPrompt)),
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

    private async Task ReadyAsync()
    {
        _logger.LogInformation("Discord client is ready for {GuildCount} servers and {GuildUserCount} users!",
            s_client?.Guilds.Count ?? 0,
            s_client?.Guilds.Sum(g => g.Users.Count));
        
        if (s_client is not null)
        {
            await _discordCommandHandler.RegisterCommandsAsync(s_client).ConfigureAwait(false);
        }
    }

    private async Task SlashCommandExecutedAsync(SocketSlashCommand command)
    {
        _ = _discordCommandHandler.HandleSlashCommandAsync(command).ConfigureAwait(false);
    }

    private async Task UserCommandExecutedAsync(SocketUserCommand command)
    {
        _ = _discordCommandHandler.HandleUserCommandAsync(command).ConfigureAwait(false);
    }

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        if (message.Author.IsBot)
        {
            return;
        }

        var messageContent = message.CleanContent;

        if (string.IsNullOrWhiteSpace(messageContent))
        {
            return;
        }

        var channel = message.Channel;
        var guildChannel = channel as SocketGuildChannel;
        var guildId = guildChannel?.Guild.Id.ToString() ?? "0";
        var guildName = guildChannel?.Guild.Name ?? "DM";
        var channelName = channel.Name ?? "Unknown";
        var username = message.Author.Username;


        _logger.LogInformation(
            "Message {MessageId} received from user '{Username}' (ID: {UserId}) in channel '{ChannelName}' (ID: {ChannelId}) in guild '{GuildName}' (ID: {GuildId}). Message: {MessageContent}",
            message.Id,
            username,
            message.Author.Id,
            channelName,
            channel.Id,
            guildName,
            guildId,
            messageContent);

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Check if user has opted out
        var userId = message.Author.Id.ToString();
        var optOut = await dbContext.UserOptOuts.FindAsync(userId).ConfigureAwait(false);
        
        if (optOut?.IsOptedOut == true)
        {
            _logger.LogInformation("User {UserId} has opted out, skipping sentiment analysis", userId);
            return;
        }

        _bg.Enqueue<DiscordService>(ds => ds.ClassifyMessage(message.Id.ToString(), userId, messageContent, username, guildId, guildName, channelName));
    }
}

/// <summary>
/// Bound to the JSON Schema
/// </summary>
internal record ClassificationResult(bool IsToxic);
