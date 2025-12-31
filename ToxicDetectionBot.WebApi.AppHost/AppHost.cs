var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.ToxicDetectionBot_WebApi>("toxicdetectionbot-webapi")
    .WithUrl("/swagger/index.html", "Swagger");

builder.Build().Run();
