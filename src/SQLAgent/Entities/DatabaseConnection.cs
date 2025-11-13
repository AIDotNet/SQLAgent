using SQLAgent.Infrastructure;
using System;

namespace SQLAgent.Entities;

/// <summary>
/// 数据库连接信息
/// Database connection information
/// </summary>
public sealed class DatabaseConnection
{
    /// <summary>
    /// 连接的唯一标识符
    /// Unique identifier for the connection
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// 连接名称（用户自定义的友好名称）
    /// Connection name (user-defined friendly name)
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// 数据库类型（如 "sqlite", "mssql", "postgresql", "mysql" 等）
    /// Database type (e.g., "sqlite", "mssql", "postgresql", "mysql", etc.)
    /// </summary>
    public string DatabaseType { get; init; } = string.Empty;

    public SqlType SqlType
    {
        get
        {
            return DatabaseType.ToLower() switch
            {
                "sqlite" => SqlType.Sqlite,
                "sqlserver" => SqlType.SqlServer,
                "mssql" => SqlType.SqlServer,
                "postgresql" => SqlType.PostgreSql,
                "mysql" => SqlType.MySql,
                _ => throw new NotSupportedException()
            };
        }
    }

    /// <summary>
    /// 数据库连接字符串
    /// Database connection string
    /// </summary>
    public string ConnectionString { get; init; } = string.Empty;
    
    /// <summary>
    /// 连接描述信息
    /// Connection description
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// 连接创建时间
    /// Connection creation time
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// 连接最后更新时间
    /// Connection last updated time
    /// </summary>
    public DateTime? UpdatedAt { get; init; }
    
    /// <summary>
    /// 连接是否启用
    /// Whether the connection is enabled
    /// </summary>
    public bool IsEnabled { get; init; } = true;
    
    /// <summary>
    /// 连接的其他元数据
    /// Additional metadata for the connection
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
    
    /// <summary>
    /// 代理信息,包含数据库表信息
    /// </summary>
    public string? Agent { get; set; }
}
