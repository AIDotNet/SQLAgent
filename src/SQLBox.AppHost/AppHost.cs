using Projects;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<SQLBox_Hosting>("sqlbox-server");

await builder.Build().RunAsync();