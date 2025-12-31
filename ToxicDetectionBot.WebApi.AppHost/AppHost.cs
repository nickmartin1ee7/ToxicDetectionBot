var builder = DistributedApplication.CreateBuilder(args);

var ollamaServer = builder.AddOllama("ollama")
    .WithDataVolume()
    .WithOpenWebUI()
    .WithEnvironment("OLLAMA_REQUEST_TIMEOUT", "600s");

var modelName = builder.Configuration["Ollama:ModelName"] ?? throw new ArgumentException("Ollama:ModelName not defined!");
var chatModel = ollamaServer.AddModel(name: "chat", modelName: modelName);

builder.AddProject<Projects.ToxicDetectionBot_WebApi>("toxicdetectionbot-webapi")
    .WithHttpHealthCheck("/swagger")
    .WithHttpHealthCheck("/hangfire")
    //.WithUrl("/swagger/index.html", "Swagger")
    .WithUrl("/hangfire", "Hangfire")
    .WithReference(chatModel);

builder.Build().Run();
