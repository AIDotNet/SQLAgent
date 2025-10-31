using System.IO;

var builder = DistributedApplication.CreateBuilder(args);

var sqlboxHosting = builder.AddProject<Projects.SQLBox_Hosting>("sqlbox-server");
//
// builder.AddNpmApp("sqlbox-web", Path.Combine("..", "..", "web"), "dev")
//     .WithReference(sqlboxHosting)
//     .WithHttpEndpoint(port: 18081)
//     .WithEnvironment("VITE_API_BASE_URL", "http://sqlbox-server:18080/api");

await builder.Build().RunAsync();