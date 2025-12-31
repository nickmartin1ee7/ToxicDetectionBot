using Hangfire;
using Microsoft.EntityFrameworkCore;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Data;
using ToxicDetectionBot.WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var dbPath = Path.Combine(builder.Environment.ContentRootPath, $"{nameof(ToxicDetectionBot)}.db");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
});

// Add services to the container.
builder.Services.AddProblemDetails();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Add Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Discord settings
builder.Services.Configure<DiscordSettings>(builder.Configuration.GetSection(DiscordSettings.ConfigKey));

// Add Discord and Hangfire services
builder.Services.AddScoped<IDiscordService, DiscordService>();
builder.Services.AddScoped<IBackgroundJobService, BackgroundJobService>();
builder.Services.AddScoped<ISentimentSummarizerService, SentimentSummarizerService>();

builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
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
    db.Database.EnsureCreated();
}

// Start discord client on startup
using (var scope = app.Services.CreateScope())
{
    var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();

    // Hangfire
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    var bgService = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>();
    var sentimentSummarizerService = scope.ServiceProvider.GetRequiredService<ISentimentSummarizerService>();

    backgroundJobClient.Enqueue(() => bgService.StartDiscordClient());
    recurringJobManager.AddOrUpdate("sentiment-summarizer", () => sentimentSummarizerService.SummarizeUserSentiments(), "*/5 * * * *");
}

app.Run();
