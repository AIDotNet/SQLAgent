using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SQLBox.Entities;
using SQLBox.Facade;
using SQLBox.Hosting.Dto;
using SQLBox.Infrastructure;
using SQLBox.Infrastructure.Defaults;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

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

// 注册连接管理器
builder.Services.AddSingleton<IDatabaseConnectionManager, InMemoryDatabaseConnectionManager>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Wire SqlGen engine to use the same connection manager instance
SqlGen.Configure(b => b.WithConnectionManager(app.Services.GetRequiredService<IDatabaseConnectionManager>()));

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// JSON序列化选项
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
};

// ==================== 连接管理 API ====================

// 获取所有连接
app.MapGet("/api/connections", async (IDatabaseConnectionManager connMgr, bool includeDisabled = false) =>
{
    var connections = await connMgr.GetAllConnectionsAsync(includeDisabled);
    var response = connections.Select(c => new ConnectionResponse
    {
        Id = c.Id,
        Name = c.Name,
        DatabaseType = c.DatabaseType,
        ConnectionString = MaskConnectionString(c.ConnectionString),
        Description = c.Description,
        IsEnabled = c.IsEnabled,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    });
    return Results.Ok(response);
});

// 获取单个连接
app.MapGet("/api/connections/{id}", async (string id, IDatabaseConnectionManager connMgr) =>
{
    var connection = await connMgr.GetConnectionAsync(id);
    if (connection == null)
        return Results.NotFound(new { message = $"Connection '{id}' not found" });
    
    var response = new ConnectionResponse
    {
        Id = connection.Id,
        Name = connection.Name,
        DatabaseType = connection.DatabaseType,
        ConnectionString = MaskConnectionString(connection.ConnectionString),
        Description = connection.Description,
        IsEnabled = connection.IsEnabled,
        CreatedAt = connection.CreatedAt,
        UpdatedAt = connection.UpdatedAt
    };
    return Results.Ok(response);
});

// 创建连接
app.MapPost("/api/connections", async (CreateConnectionRequest request, IDatabaseConnectionManager connMgr) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { message = "Name is required" });
    
    if (string.IsNullOrWhiteSpace(request.DatabaseType))
        return Results.BadRequest(new { message = "DatabaseType is required" });
    
    if (string.IsNullOrWhiteSpace(request.ConnectionString))
        return Results.BadRequest(new { message = "ConnectionString is required" });

    var connection = new DatabaseConnection
    {
        Id = Guid.NewGuid().ToString(),
        Name = request.Name,
        DatabaseType = request.DatabaseType.ToLowerInvariant(),
        ConnectionString = request.ConnectionString,
        Description = request.Description,
        CreatedAt = DateTime.UtcNow,
        IsEnabled = true
    };

    var created = await connMgr.AddConnectionAsync(connection);
    var response = new ConnectionResponse
    {
        Id = created.Id,
        Name = created.Name,
        DatabaseType = created.DatabaseType,
        ConnectionString = MaskConnectionString(created.ConnectionString),
        Description = created.Description,
        IsEnabled = created.IsEnabled,
        CreatedAt = created.CreatedAt,
        UpdatedAt = created.UpdatedAt
    };
    return Results.Created($"/api/connections/{created.Id}", response);
});

// 更新连接
app.MapPut("/api/connections/{id}", async (string id, UpdateConnectionRequest request, IDatabaseConnectionManager connMgr) =>
{
    var existing = await connMgr.GetConnectionAsync(id);
    if (existing == null)
        return Results.NotFound(new { message = $"Connection '{id}' not found" });

    var updated = new DatabaseConnection
    {
        Id = existing.Id,
        Name = request.Name ?? existing.Name,
        DatabaseType = request.DatabaseType?.ToLowerInvariant() ?? existing.DatabaseType,
        ConnectionString = request.ConnectionString ?? existing.ConnectionString,
        Description = request.Description ?? existing.Description,
        CreatedAt = existing.CreatedAt,
        UpdatedAt = DateTime.UtcNow,
        IsEnabled = request.IsEnabled ?? existing.IsEnabled,
        Metadata = existing.Metadata
    };

    var result = await connMgr.UpdateConnectionAsync(updated);
    var response = new ConnectionResponse
    {
        Id = result.Id,
        Name = result.Name,
        DatabaseType = result.DatabaseType,
        ConnectionString = MaskConnectionString(result.ConnectionString),
        Description = result.Description,
        IsEnabled = result.IsEnabled,
        CreatedAt = result.CreatedAt,
        UpdatedAt = result.UpdatedAt
    };
    return Results.Ok(response);
});

// 删除连接
app.MapDelete("/api/connections/{id}", async (string id, IDatabaseConnectionManager connMgr) =>
{
    var existing = await connMgr.GetConnectionAsync(id);
    if (existing == null)
        return Results.NotFound(new { message = $"Connection '{id}' not found" });

    await connMgr.DeleteConnectionAsync(id);
    return Results.NoContent();
});

// 测试连接
app.MapPost("/api/connections/{id}/test", async (string id, IDatabaseConnectionManager connMgr) =>
{
    var sw = Stopwatch.StartNew();
    
    try
    {
        var success = await connMgr.TestConnectionAsync(id);
        sw.Stop();

        var response = new TestConnectionResponse
        {
            Success = success,
            Message = success ? "Connection successful" : "Connection failed",
            ElapsedMs = sw.ElapsedMilliseconds
        };

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        sw.Stop();
        
        var response = new TestConnectionResponse
        {
            Success = false,
            Message = ex.Message,
            ElapsedMs = sw.ElapsedMilliseconds
        };

        return Results.Ok(response);
    }
});

// ==================== 聊天 API (SSE) ====================

app.MapPost("/api/chat/completion", async (HttpContext context, CompletionInput input, IDatabaseConnectionManager connMgr) =>
{
    var sw = Stopwatch.StartNew();
    
    // 设置SSE响应头
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("X-Accel-Buffering", "no");
    
    try
    {
        // 验证连接
        var connection = await connMgr.GetConnectionAsync(input.ConnectionId);
        if (connection == null)
        {
            await SendErrorAsync(context, "CONNECTION_NOT_FOUND", 
                $"Connection '{input.ConnectionId}' not found", jsonOptions);
            return;
        }

        if (!connection.IsEnabled)
        {
            await SendErrorAsync(context, "CONNECTION_DISABLED", 
                $"Connection '{connection.Name}' is disabled", jsonOptions);
            return;
        }

        // 发送开始消息
        await SendTextAsync(context, $"正在分析问题: {input.Question}", jsonOptions);

        // 创建查询选项
        var options = new AskOptions(
            ConnectionId: input.ConnectionId,
            Dialect: input.Dialect ?? connection.DatabaseType,
            Execute: input.Execute,
            TopK: 8,
            ReturnExplanation: true,
            AllowWrite: false
        );

        // 执行查询
        await SendTextAsync(context, "正在生成SQL...", jsonOptions);
        
        var result = await SqlGen.AskAsync(input.Question, options);

        // 发送SQL
        await SendSqlAsync(context, new SqlMessage
        {
            Sql = result.Sql,
            Tables = result.TouchedTables,
            Dialect = result.Dialect
        }, jsonOptions);

        if (result.Warnings != null && result.Warnings.Length > 0)
        {
            await SendTextAsync(context, $"警告: {string.Join(", ", result.Warnings)}", jsonOptions);
        }

        // 发送解释
        if (!string.IsNullOrEmpty(result.Explanation))
        {
            await SendTextAsync(context, result.Explanation, jsonOptions);
        }

        // TODO: 如果执行了查询，这里需要实际执行SQL并返回数据
        if (input.Execute)
        {
            await SendTextAsync(context, "SQL 已生成，实际执行功能待实现", jsonOptions);
        }

        sw.Stop();
        
        // 发送完成消息
        await SendDoneAsync(context, new DoneMessage
        {
            ElapsedMs = sw.ElapsedMilliseconds
        }, jsonOptions);
    }
    catch (Exception ex)
    {
        await SendErrorAsync(context, "EXECUTION_ERROR", ex.Message, jsonOptions, ex.ToString());
    }
});

// 添加静态文件支持（用于前端）
app.UseStaticFiles();

// 默认路由到index.html
app.MapFallbackToFile("index.html");

await app.RunAsync();

// ==================== 辅助方法 ====================

static string MaskConnectionString(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        return connectionString;

    var parts = connectionString.Split(';');
    var masked = new List<string>();

    foreach (var part in parts)
    {
        if (part.Trim().StartsWith("Password=", StringComparison.OrdinalIgnoreCase) ||
            part.Trim().StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
        {
            masked.Add("Password=******");
        }
        else
        {
            masked.Add(part);
        }
    }

    return string.Join(";", masked);
}

static async Task SendTextAsync(HttpContext context, string content, JsonSerializerOptions options)
{
    var message = new TextMessage { Content = content };
    await SendMessageAsync(context, message, options);
}

static async Task SendSqlAsync(HttpContext context, SqlMessage message, JsonSerializerOptions options)
{
    await SendMessageAsync(context, message, options);
}

static async Task SendErrorAsync(HttpContext context, string code, string message, JsonSerializerOptions options, string? details = null)
{
    var errorMessage = new ErrorMessage
    {
        Code = code,
        Message = message,
        Details = details
    };
    await SendMessageAsync(context, errorMessage, options);
}

static async Task SendDoneAsync(HttpContext context, DoneMessage message, JsonSerializerOptions options)
{
    await SendMessageAsync(context, message, options);
}

static async Task SendMessageAsync(HttpContext context, SSEMessage message, JsonSerializerOptions options)
{
    var json = JsonSerializer.Serialize(message, message.GetType(), options);
    var data = $"data: {json}\n\n";
    var bytes = Encoding.UTF8.GetBytes(data);
    
    await context.Response.Body.WriteAsync(bytes);
    await context.Response.Body.FlushAsync();
}