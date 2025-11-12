var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.SQLAgent_Hosting>("sqlagent-hosting");

builder.Build().Run();
