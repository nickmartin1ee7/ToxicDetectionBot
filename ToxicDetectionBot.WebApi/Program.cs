using Hangfire;
using ToxicDetectionBot.WebApi.Configuration;
using ToxicDetectionBot.WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

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

// Start discord client on startup
using (var scope = app.Services.CreateScope())
{
    var hangfireClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
    var bgService = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>();
    hangfireClient.Enqueue(() => bgService.StartDiscordClient());
}

app.Run();
