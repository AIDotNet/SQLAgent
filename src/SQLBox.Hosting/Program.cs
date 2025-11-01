using Serilog;
using Scalar.AspNetCore;
using SQLBox.Entities;
using SQLBox.Facade;
using SQLBox.Hosting.Dto;
using SQLBox.Infrastructure;
using SQLBox.Infrastructure.Defaults;
using System.Text.Json;
using SQLBox.Hosting.Extensions;

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
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
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

//// 注册连接管理器（持久化到 connections.json / providers.json）
var dataRoot = builder.Environment.ContentRootPath;
var connectionsFile = Path.Combine(dataRoot, "connections.json");
var providersFile = Path.Combine(dataRoot, "providers.json");

builder.Services.AddSingleton<IDatabaseConnectionManager>(sp => new InMemoryDatabaseConnectionManager(connectionsFile));

// 注册 AI 提供商管理器和 LLM 客户端工厂
builder.Services.AddSingleton<IAIProviderManager>(sp => new InMemoryAIProviderManager(providersFile));
builder.Services.AddSingleton<ILlmClientFactory, DefaultLlmClientFactory>();

// 注册服务
builder.Services.AddScoped<SQLBox.Hosting.Services.ConnectionService>();
builder.Services.AddScoped<SQLBox.Hosting.Services.ProvidersService>();
builder.Services.AddScoped<SQLBox.Hosting.Services.ChatService>();
builder.Services.AddScoped<SQLBox.Hosting.Services.VectorIndexService>();

// 绑定系统设置（提供默认参数），并尝试从 settings.json 覆盖（实现持久化加载）
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
            systemSettings.DistanceMetric = fileSettings.DistanceMetric;
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

// Wire SqlGen engine to use the same connection manager instance
SqlGen.Configure(b => b.WithConnectionManager(app.Services.GetRequiredService<IDatabaseConnectionManager>()));

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