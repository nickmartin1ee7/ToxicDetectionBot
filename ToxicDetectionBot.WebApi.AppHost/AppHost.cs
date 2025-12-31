var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.ToxicDetectionBot_WebApi>("toxicdetectionbot-webapi");

builder.Build().Run();
