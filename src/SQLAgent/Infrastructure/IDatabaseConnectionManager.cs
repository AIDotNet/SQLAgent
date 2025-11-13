using SQLAgent.Entities;

namespace SQLAgent.Infrastructure;

/// <summary>
/// 数据库连接管理器接口
/// Database connection manager interface
/// </summary>
public interface IDatabaseConnectionManager
{
    /// <summary>
    /// 添加新的数据库连接
    /// Add a new database connection
    /// </summary>
    /// <param name="connection">数据库连接信息 / Database connection information</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>添加的连接 / The added connection</returns>
    Task<DatabaseConnection> AddConnectionAsync(DatabaseConnection connection, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 更新现有的数据库连接
    /// Update an existing database connection
    /// </summary>
    /// <param name="connection">更新后的连接信息 / Updated connection information</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>更新后的连接 / The updated connection</returns>
    Task<DatabaseConnection> UpdateConnectionAsync(DatabaseConnection connection, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 删除数据库连接
    /// Delete a database connection
    /// </summary>
    /// <param name="connectionId">连接ID / Connection ID</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    Task DeleteConnectionAsync(string connectionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 根据ID获取数据库连接
    /// Get a database connection by ID
    /// </summary>
    /// <param name="connectionId">连接ID / Connection ID</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>数据库连接，如果未找到则返回 null / Database connection, or null if not found</returns>
    Task<DatabaseConnection?> GetConnectionAsync(string connectionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取所有数据库连接
    /// Get all database connections
    /// </summary>
    /// <param name="includeDisabled">是否包含已禁用的连接 / Whether to include disabled connections</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>所有数据库连接列表 / List of all database connections</returns>
    Task<IReadOnlyList<DatabaseConnection>> GetAllConnectionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 测试数据库连接
    /// Test a database connection
    /// </summary>
    /// <param name="connectionId">连接ID / Connection ID</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>连接是否成功 / Whether the connection was successful</returns>
    Task<bool> TestConnectionAsync(string connectionId, CancellationToken cancellationToken = default);
    
    Task UpdateAgentAsync(string connectionId, string agent, CancellationToken cancellationToken = default);
}
