using System.Collections.Generic;

namespace SQLAgent.Entities;

/// <summary>
/// 数据库表的文档描述，包含结构、关系和元数据
/// Document description of a database table, including structure, relationships, and metadata
/// </summary>
public sealed class TableDoc
{
    /// <summary>
    /// 关联的数据库连接ID
    /// Associated database connection ID
    /// </summary>
    public string ConnectionId { get; init; } = string.Empty;
    
    /// <summary>
    /// 表所属的架构/模式名称（如 "dbo", "public" 等）
    /// Schema/namespace name the table belongs to (e.g., "dbo", "public", etc.)
    /// </summary>
    public string Schema { get; init; } = string.Empty;
    
    /// <summary>
    /// 表名
    /// Table name
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// 表的别名列表，用于自然语言查询匹配
    /// List of table aliases for natural language query matching
    /// </summary>
    public string[] Aliases { get; init; } = System.Array.Empty<string>();
    
    /// <summary>
    /// 表的描述信息，说明表的用途和业务含义
    /// Description of the table, explaining its purpose and business meaning
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// 表中所有列的文档集合
    /// Collection of all column documents in the table
    /// </summary>
    public IReadOnlyList<ColumnDoc> Columns { get; init; } = new List<ColumnDoc>();
    
    /// <summary>
    /// 主键列名列表
    /// List of primary key column names
    /// </summary>
    public IReadOnlyList<string> PrimaryKey { get; init; } = new List<string>();

    /// <summary>
    /// 外键关系列表：本地列 -> (引用表, 引用列)
    /// Foreign key relationships: local column -> (referenced table, referenced column)
    /// </summary>
    public IReadOnlyList<(string Column, string RefTable, string RefColumn)> ForeignKeys { get; init; }
        = new List<(string Column, string RefTable, string RefColumn)>();

    /// <summary>
    /// 表级统计信息（如行数、大小等）
    /// Table-level statistics (e.g., row count, size, etc.)
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Stats { get; init; }
    
    /// <summary>
    /// 表的向量嵌入，用于语义相似度搜索
    /// Vector embedding of the table for semantic similarity search
    /// </summary>
    public float[]? Vector { get; set; }
}
