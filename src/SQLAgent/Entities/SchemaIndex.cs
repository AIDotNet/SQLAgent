using System.Collections.Generic;

namespace SQLAgent.Entities;

/// <summary>
/// 数据库架构的索引结构，用于快速检索和关系分析
/// Index structure for database schema, used for fast retrieval and relationship analysis
/// </summary>
public sealed class SchemaIndex
{
    /// <summary>
    /// 关联的数据库连接ID
    /// Associated database connection ID
    /// </summary>
    public string ConnectionId { get; init; } = string.Empty;
    
    /// <summary>
    /// 关键字到表名的倒排索引，用于快速查找包含特定关键字的表
    /// Inverted index from keywords to table names for quick lookup of tables containing specific keywords
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<string>> KeywordToTables { get; init; }
        = new Dictionary<string, HashSet<string>>();

    /// <summary>
    /// 关键字到列的倒排索引，用于快速查找包含特定关键字的列及其所属表
    /// Inverted index from keywords to columns for quick lookup of columns containing specific keywords and their tables
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<(string Table, string Column)>> KeywordToColumns { get; init; }
        = new Dictionary<string, HashSet<(string Table, string Column)>>();

    /// <summary>
    /// 表关系的无向图：表名 -> 相邻表集合（通过外键或推断的连接关系）
    /// 用于查找表之间的连接路径和构建多表查询
    /// Undirected graph of table relationships: table -> neighbor tables (via foreign keys or inferred joins)
    /// Used for finding join paths between tables and constructing multi-table queries
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<string>> Graph { get; init; }
        = new Dictionary<string, HashSet<string>>();
}
