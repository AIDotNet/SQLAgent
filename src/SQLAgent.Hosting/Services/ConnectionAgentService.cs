using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SQLAgent.Entities;
using SQLAgent.Facade;
using SQLAgent.Hosting.Dto;
using SQLAgent.Infrastructure;

namespace SQLAgent.Hosting.Services;

/// <summary>
/// Agent 生成状态
/// </summary>
public enum AgentGenerationStatus
{
    /// <summary>
    /// 未开始
    /// </summary>
    NotStarted,

    /// <summary>
    /// 进行中
    /// </summary>
    InProgress,

    /// <summary>
    /// 已完成
    /// </summary>
    Completed,

    /// <summary>
    /// 失败
    /// </summary>
    Failed
}

/// <summary>
/// Agent 生成状态信息
/// </summary>
public class AgentGenerationState
{
    /// <summary>
    /// 状态
    /// </summary>
    public AgentGenerationStatus Status { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 连接 Agent 生成服务
/// </summary>
public class ConnectionAgentService
{
    private readonly IDatabaseConnectionManager _connMgr;
    private readonly IAIProviderManager _providerMgr;
    private readonly SystemSettings _settings;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _agentBuildLocks;
    private readonly ConcurrentDictionary<string, AgentGenerationState> _agentGenerationStates;
    private readonly ILogger<ConnectionAgentService> _logger;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConnectionAgentService(
        IDatabaseConnectionManager connMgr,
        IAIProviderManager providerMgr,
        SystemSettings settings,
        ILogger<ConnectionAgentService> logger)
    {
        _connMgr = connMgr;
        _providerMgr = providerMgr;
        _settings = settings;
        _logger = logger;
        _agentBuildLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _agentGenerationStates = new ConcurrentDictionary<string, AgentGenerationState>();
    }

    /// <summary>
    /// 获取 Agent 生成状态
    /// </summary>
    public AgentGenerationState GetGenerationStatus(string connectionId)
    {
        return _agentGenerationStates.GetOrAdd(connectionId, _ => new AgentGenerationState
        {
            Status = AgentGenerationStatus.NotStarted
        });
    }

    /// <summary>
    /// 为指定连接生成 SQL Agent
    /// </summary>
    public async Task<IResult> GenerateAgentAsync(string connectionId)
    {
        var connection = await _connMgr.GetConnectionAsync(connectionId);
        if (connection == null)
            return Results.NotFound(new { message = $"Connection '{connectionId}' not found" });

        if (!connection.IsEnabled)
            return Results.BadRequest(new { message = $"Connection '{connection.Name}' is disabled" });

        // 获取默认的聊天提供商
        var chatProviderId = _settings.DefaultChatProviderId;
        var chatProvider = string.IsNullOrWhiteSpace(chatProviderId)
            ? await _providerMgr.GetDefaultAsync()
            : await _providerMgr.GetAsync(chatProviderId!);

        if (chatProvider == null)
            return Results.BadRequest(new
            {
                message =
                    "Default chat provider is not configured. Please set SystemSettings.DefaultChatProviderId or configure a default provider."
            });

        if (!chatProvider.IsEnabled)
            return Results.BadRequest(new { message = $"Chat provider '{chatProvider.Name}' is disabled" });

        var sem = _agentBuildLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        if (!await sem.WaitAsync(0))
        {
            return Results.Accepted($"/api/connections/{connectionId}/agent/generate",
                new { message = "Agent generation in progress" });
        }

        try
        {
            // 更新状态为进行中
            _agentGenerationStates[connectionId] = new AgentGenerationState
            {
                Status = AgentGenerationStatus.InProgress,
                Message = "Agent generation started",
                StartTime = DateTime.UtcNow
            };

            // 在后台任务中执行Agent生成
            _ = Task.Run(async () =>
            {
                try
                {
                    await GenerateAgentInternalAsync(connection, chatProvider, connectionId);

                    // 更新状态为完成
                    _agentGenerationStates[connectionId] = new AgentGenerationState
                    {
                        Status = AgentGenerationStatus.Completed,
                        Message = "Agent generated successfully",
                        StartTime = _agentGenerationStates[connectionId].StartTime,
                        EndTime = DateTime.UtcNow
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate agent for connection {ConnectionId}", connectionId);

                    // 更新状态为失败
                    _agentGenerationStates[connectionId] = new AgentGenerationState
                    {
                        Status = AgentGenerationStatus.Failed,
                        Message = "Agent generation failed",
                        ErrorMessage = ex.Message,
                        StartTime = _agentGenerationStates[connectionId].StartTime,
                        EndTime = DateTime.UtcNow
                    };
                }
                finally
                {
                    sem.Release();
                }
            });

            return Results.Accepted($"/api/connections/{connectionId}/agent/generate",
                new { message = "Agent generation started", connectionId = connectionId });
        }
        catch (Exception ex)
        {
            sem.Release();
            _logger.LogError(ex, "Failed to start agent generation for connection {ConnectionId}", connectionId);
            return Results.Problem($"Failed to start agent generation: {ex.Message}");
        }
    }

    /// <summary>
    /// 内部方法：实际执行 Agent 生成逻辑
    /// </summary>
    private async Task GenerateAgentInternalAsync(
        DatabaseConnection connection, AIProvider chatProvider, string connectionId)
    {
        _logger.LogInformation("Starting agent generation for connection {ConnectionId}", connection.Id);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        var sqlAgentBuilder = new SQLAgentBuilder(serviceCollection);

        sqlAgentBuilder
            .WithDatabaseType(connection.SqlType, connection.ConnectionString, connection.Id)
            .WithLLMProvider(
                _settings.DefaultChatModel ?? "gpt-4",
                chatProvider.ApiKey,
                chatProvider.Endpoint ?? "",
                chatProvider.Type, 32000);

        sqlAgentBuilder.Build(_connMgr);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var agentClient = serviceProvider.GetRequiredService<SQLAgentClient>();

        _logger.LogInformation("Agent successfully generated for connection {ConnectionId}", connection.Id);

        // 可以在这里添加其他逻辑，比如保存Agent配置、通知前端等
        await agentClient.GenerateAgentDatabaseInfoAsync();
    }
}