using System.IO;

var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.SQLBox_Hosting>("sqlbox-server");
//
await builder.Build().RunAsync();