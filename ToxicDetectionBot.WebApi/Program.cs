using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.EntityFrameworkCore;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Data;
using ToxicDetectionBot.WebApi.Services;
using ToxicDetectionBot.WebApi.Services.Commands;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSeqEndpoint("seq");

var dbPathSentiment = Path.Combine(builder.Environment.ContentRootPath, $"{nameof(ToxicDetectionBot)}-sentiment.db");
var dbPathHangfire = Path.Combine(builder.Environment.ContentRootPath, $"{nameof(ToxicDetectionBot)}-hangfire.db");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPathSentiment}");
});

// Add services to the container.
builder.Services.AddProblemDetails();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Add Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add HttpClient
builder.Services.AddHttpClient();

// Configure Discord settings
builder.Services.Configure<DiscordSettings>(builder.Configuration.GetSection(DiscordSettings.ConfigKey));

// Add Discord and Hangfire services
builder.Services.AddScoped<IDiscordService, DiscordService>();
builder.Services.AddScoped<IBackgroundJobService, BackgroundJobService>();
builder.Services.AddScoped<ISentimentSummarizerService, SentimentSummarizerService>();
builder.Services.AddScoped<IRetentionService, RetentionService>();
builder.Services.AddScoped<IDiscordCommandHandler, DiscordCommandHandler>();

// Register command classes
builder.Services.AddScoped<ShowStatsCommand>();
builder.Services.AddScoped<ShowLeaderboardCommand>();
builder.Services.AddScoped<OptCommand>();
builder.Services.AddScoped<FeedbackCommand>();
builder.Services.AddScoped<CheckCommand>();
builder.Services.AddScoped<BotStatsCommand>();
builder.Services.AddScoped<ShowStatsUserCommand>();

builder.Services.AddHangfire(configuration => 
    configuration.UseSQLiteStorage(
        Path.Combine(builder.Environment.ContentRootPath, dbPathHangfire)));
builder.Services.AddHangfireServer();

builder.AddOllamaApiClient("chat")
    .AddChatClient();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();
app.MapHangfireDashboard();

// Ensure database is created/migrated on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Start discord client on startup
using (var scope = app.Services.CreateScope())
{
    var bgService = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>();

    // Hangfire
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

    var sentimentSummarizerService = scope.ServiceProvider.GetRequiredService<ISentimentSummarizerService>();
    var retentionService = scope.ServiceProvider.GetRequiredService<IRetentionService>();

    _ = bgService.StartDiscordClient();
    recurringJobManager.AddOrUpdate("sentiment-summarizer", () => sentimentSummarizerService.SummarizeUserSentiments(), "*/1 * * * *");
    recurringJobManager.AddOrUpdate("sentiment-retention", () => retentionService.PurgeOldSentiments(), "*/10 * * * *");
}

app.Run();
