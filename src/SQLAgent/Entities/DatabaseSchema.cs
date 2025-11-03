using System.Collections.Generic;
using System.Linq;

namespace SQLAgent.Entities;

/// <summary>
/// 数据库架构的完整描述
/// Complete description of a database schema
/// </summary>
public sealed class DatabaseSchema
{
    /// <summary>
    /// 关联的数据库连接ID
    /// Associated database connection ID
    /// </summary>
    public string ConnectionId { get; init; } = string.Empty;
    
    /// <summary>
    /// 数据库名称
    /// Database name
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// SQL 方言类型（如 "sqlite", "mssql", "postgresql", "mysql" 等）
    /// SQL dialect type (e.g., "sqlite", "mssql", "postgresql", "mysql", etc.)
    /// </summary>
    public string Dialect { get; init; } = "sqlite";
    
    /// <summary>
    /// 数据库中所有表的文档集合
    /// Collection of all table documents in the database
    /// </summary>
    public IReadOnlyList<TableDoc> Tables { get; init; } = new List<TableDoc>();

    /// <summary>
    /// 根据表名或别名查找表文档（不区分大小写）
    /// Find a table document by name or alias (case-insensitive)
    /// </summary>
    /// <param name="name">表名或别名 / Table name or alias</param>
    /// <returns>匹配的表文档，如果未找到则返回 null / Matching table document, or null if not found</returns>
    public TableDoc? FindTable(string name)
        => Tables.FirstOrDefault(t => string.Equals(t.Name, name, System.StringComparison.OrdinalIgnoreCase)
                                       || t.Aliases.Contains(name, System.StringComparer.OrdinalIgnoreCase));
}

