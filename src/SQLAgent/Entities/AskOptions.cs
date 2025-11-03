using System.Collections.Generic;

namespace SQLAgent.Entities;

/// <summary>
/// SQL 查询生成和执行的配置选项
/// Configuration options for SQL query generation and execution
/// </summary>
public sealed record AskOptions(
    /// <summary>
    /// 数据库连接ID（必需），用于指定要查询的数据库
    /// Database connection ID (required) to specify which database to query
    /// </summary>
    string ConnectionId,
    
    /// <summary>
    /// SQL 方言（如 "sqlite", "mssql", "postgresql", "mysql" 等）
    /// 如果为 null，将使用数据库架构中定义的默认方言
    /// SQL dialect (e.g., "sqlite", "mssql", "postgresql", "mysql", etc.)
    /// If null, the default dialect from the database schema will be used
    /// </summary>
    string? Dialect = null,
    
    /// <summary>
    /// 是否实际执行生成的 SQL 查询
    /// 如果为 false，仅生成 SQL 而不执行
    /// Whether to actually execute the generated SQL query
    /// If false, only generates SQL without executing it
    /// </summary>
    bool Execute = false,
    
    /// <summary>
    /// 检索相关架构元素时返回的最大结果数
    /// 用于向量搜索或语义检索以找到最相关的表和列
    /// Maximum number of relevant schema elements to retrieve
    /// Used for vector search or semantic retrieval to find the most relevant tables and columns
    /// </summary>
    int TopK = 8,
    
    /// <summary>
    /// 是否在结果中返回 SQL 生成的解释说明
    /// 包含查询意图、使用的表、置信度等信息
    /// Whether to return an explanation of the SQL generation in the result
    /// Includes query intent, tables used, confidence level, etc.
    /// </summary>
    bool ReturnExplanation = true,
    
    /// <summary>
    /// 是否允许生成写操作（INSERT、UPDATE、DELETE）的 SQL
    /// 如果为 false，仅允许只读查询（SELECT）以确保数据安全
    /// Whether to allow generation of write operations (INSERT, UPDATE, DELETE)
    /// If false, only read-only queries (SELECT) are allowed to ensure data safety
    /// </summary>
    bool AllowWrite = true
);

