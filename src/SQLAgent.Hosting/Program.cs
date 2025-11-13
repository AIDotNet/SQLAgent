using Scalar.AspNetCore;
using Serilog;
using SQLAgent.Hosting.Dto;
using SQLAgent.Hosting.Extensions;
using SQLAgent.Hosting.Services;
using SQLAgent.Infrastructure;
using SQLAgent.Infrastructure.Defaults;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// 配置 Serilog
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", context.HostingEnvironment.ApplicationName)
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // 支持字符串枚举
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Add services to the container
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var dataRoot = builder.Environment.ContentRootPath;
var connectionsFile = Path.Combine(dataRoot, "connections.json");
var providersFile = Path.Combine(dataRoot, "providers.json");

builder.Services.AddSingleton<IDatabaseConnectionManager>(sp => new InMemoryDatabaseConnectionManager(connectionsFile));

// 注册 AI 提供商管理器和 LLM 客户端工厂
builder.Services.AddSingleton<IAIProviderManager>(sp => new InMemoryAIProviderManager(providersFile));

// 注册服务
builder.Services.AddScoped<ConnectionService>();
builder.Services.AddScoped<ProvidersService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<ConnectionAgentService>();

var systemSettings = builder.Configuration.GetSection("SystemSettings").Get<SystemSettings>() ?? new SystemSettings();

var settingsFile = Path.Combine(builder.Environment.ContentRootPath, "settings.json");
try
{
    if (File.Exists(settingsFile))
    {
        var json = await File.ReadAllTextAsync(settingsFile);
        var fileSettings = JsonSerializer.Deserialize<SystemSettings>(json);
        if (fileSettings != null)
        {
            systemSettings.EmbeddingProviderId = fileSettings.EmbeddingProviderId;
            systemSettings.EmbeddingModel = fileSettings.EmbeddingModel;
            systemSettings.VectorDbPath = fileSettings.VectorDbPath;
            systemSettings.VectorCollection = fileSettings.VectorCollection;
            systemSettings.AutoCreateCollection = fileSettings.AutoCreateCollection;
            systemSettings.VectorCacheExpireMinutes = fileSettings.VectorCacheExpireMinutes;
            systemSettings.DefaultChatProviderId = fileSettings.DefaultChatProviderId;
            systemSettings.DefaultChatModel = fileSettings.DefaultChatModel;
        }
    }
}
catch
{
    // 忽略加载失败，继续使用默认/配置中的设置
}

builder.Services.AddSingleton(systemSettings);

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar");
}


app.UseCors();
app.UseSerilogRequestLogging(); // 记录HTTP请求日志

/* ==================== API 路由映射（Extensions） ==================== */
app.MapAllApis();

// 添加静态文件支持（用于前端）
app.UseStaticFiles();

// 默认路由到index.html
app.MapFallbackToFile("index.html");

try
{
    await app.RunAsync();
}
finally
{
    Log.CloseAndFlush();
}