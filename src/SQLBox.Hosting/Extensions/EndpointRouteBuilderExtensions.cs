using Microsoft.AspNetCore.Mvc;
using SQLBox.Hosting.Dto;
using SQLBox.Hosting.Services;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;

namespace SQLBox.Hosting.Extensions;

/// <summary>
/// 端点路由构建器扩展方法
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// 映射连接管理 API
    /// </summary>
    public static IEndpointRouteBuilder MapConnectionApis(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/connections", 
            async (ConnectionService service, bool includeDisabled = false) =>
                await service.GetAllAsync(includeDisabled))
            .WithName("GetAllConnections");

        app.MapGet("/api/connections/{id}", 
            async (string id, ConnectionService service) =>
                await service.GetByIdAsync(id))
            .WithName("GetConnectionById");

        app.MapPost("/api/connections", 
            async (CreateConnectionRequest request, ConnectionService service) =>
                await service.CreateAsync(request))
            .WithName("CreateConnection");

        app.MapPut("/api/connections/{id}", 
            async (string id, UpdateConnectionRequest request, ConnectionService service) =>
                await service.UpdateAsync(id, request))
            .WithName("UpdateConnection");

        app.MapDelete("/api/connections/{id}", 
            async (string id, ConnectionService service) =>
                await service.DeleteAsync(id))
            .WithName("DeleteConnection");

        app.MapPost("/api/connections/{id}/test", 
            async (string id, ConnectionService service) =>
                await service.TestAsync(id))
            .WithName("TestConnection");

        return app;
    }

    /// <summary>
    /// 映射 AI 提供商管理 API
    /// </summary>
    public static IEndpointRouteBuilder MapProviderApis(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/providers", 
            async (ProvidersService service) =>
                await service.GetAllAsync())
            .WithName("GetAllProviders");

        app.MapGet("/api/providers/{id}", 
            async (string id, ProvidersService service) =>
                await service.GetAsync(id))
            .WithName("GetProviderById");

        app.MapPost("/api/providers", 
            async (AIProviderInput input, ProvidersService service) =>
                await service.CreateAsync(input))
            .WithName("CreateProvider");

        app.MapPut("/api/providers/{id}", 
            async (string id, AIProviderInput input, ProvidersService service) =>
                await service.UpdateAsync(id, input))
            .WithName("UpdateProvider");

        app.MapDelete("/api/providers/{id}", 
            async (string id, ProvidersService service) =>
                await service.DeleteAsync(id))
            .WithName("DeleteProvider");

        app.MapGet("/api/providers/{id}/models", 
            async (string id, ProvidersService service) =>
                await service.GetModelsAsync(id))
            .WithName("GetProviderModels");

        return app;
    }

    /// <summary>
    /// 映射聊天 API
    /// </summary>
    public static IEndpointRouteBuilder MapChatApis(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat/completion", 
            async (HttpContext context, CompletionInput input, ChatService service) =>
                await service.CompletionAsync(context, input))
            .WithName("ChatCompletion");

        return app;
    }

    /// <summary>
    /// 映射系统设置 API
    /// </summary>
    public static IEndpointRouteBuilder MapSettingsApis(this IEndpointRouteBuilder app)
    {
        // GET /api/settings - 返回内存设置；如存在 settings.json，则加载并合并到内存实例后返回
        app.MapGet("/api/settings", async ([FromServices] SystemSettings settings, [FromServices] IWebHostEnvironment env) =>
            {
                try
                {
                    var filePath = Path.Combine(env.ContentRootPath, "settings.json");
                    if (File.Exists(filePath))
                    {
                        var json = await File.ReadAllTextAsync(filePath);
                        var fileSettings = JsonSerializer.Deserialize<SystemSettings>(json);
                        if (fileSettings != null)
                        {
                            settings.EmbeddingProviderId = fileSettings.EmbeddingProviderId;
                            settings.EmbeddingModel = fileSettings.EmbeddingModel;
                            settings.VectorDbPath = fileSettings.VectorDbPath;
                            settings.VectorCollection = fileSettings.VectorCollection;
                            settings.DistanceMetric = fileSettings.DistanceMetric;
                            settings.AutoCreateCollection = fileSettings.AutoCreateCollection;
                            settings.VectorCacheExpireMinutes = fileSettings.VectorCacheExpireMinutes;
                            settings.DefaultChatProviderId = fileSettings.DefaultChatProviderId;
                            settings.DefaultChatModel = fileSettings.DefaultChatModel;
                        }
                    }
                }
                catch
                {
                    // 忽略读取失败，返回内存中的设置
                }
                return Results.Ok(settings);
            })
            .WithName("GetSettings");
    
        // PUT /api/settings - 更新内存设置并持久化到 settings.json
        app.MapPut("/api/settings", async ([FromBody] SystemSettings payload, [FromServices] SystemSettings settings, [FromServices] IWebHostEnvironment env) =>
            {
                settings.EmbeddingProviderId = payload.EmbeddingProviderId;
                settings.EmbeddingModel = payload.EmbeddingModel;
                settings.VectorDbPath = payload.VectorDbPath;
                settings.VectorCollection = payload.VectorCollection;
                settings.DistanceMetric = payload.DistanceMetric;
                settings.AutoCreateCollection = payload.AutoCreateCollection;
                settings.VectorCacheExpireMinutes = payload.VectorCacheExpireMinutes;
                settings.DefaultChatProviderId = payload.DefaultChatProviderId;
                settings.DefaultChatModel = payload.DefaultChatModel;

                try
                {
                    var filePath = Path.Combine(env.ContentRootPath, "settings.json");
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(settings, options);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    await File.WriteAllTextAsync(filePath, json);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"保存设置失败: {ex.Message}");
                }
    
                return Results.Ok(settings);
            })
            .WithName("UpdateSettings");
    
        return app;
    }

    /// <summary>
    /// 映射向量索引 API
    /// </summary>
    public static IEndpointRouteBuilder MapVectorIndexApis(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/connections/{id}/index/init", 
            async (string id, VectorIndexService service) =>
                await service.InitializeIndexAsync(id))
            .WithName("InitializeVectorIndex");

        app.MapPost("/api/connections/{id}/index/update", 
            async (string id, VectorIndexService service) =>
                await service.UpdateIndexAsync(id))
            .WithName("UpdateVectorIndex");

        return app;
    }

    /// <summary>
    /// 映射所有 API 端点
    /// </summary>
    public static IEndpointRouteBuilder MapAllApis(this IEndpointRouteBuilder app)
    {
        app.MapConnectionApis();
        app.MapProviderApis();
        app.MapChatApis();
        app.MapSettingsApis();
        app.MapVectorIndexApis();

        return app;
    }
}
