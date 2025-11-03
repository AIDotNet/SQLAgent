using System.Collections.Generic;

namespace SQLAgent.Entities;

/// <summary>
/// SQL 生成时使用的架构上下文，包含相关的表文档
/// Schema context used during SQL generation, containing relevant table documents
/// </summary>
public sealed class SchemaContext
{
    /// <summary>
    /// 关联的数据库连接ID
    /// Associated database connection ID
    /// </summary>
    public string ConnectionId { get; init; } = string.Empty;
    
    /// <summary>
    /// 与当前查询相关的表文档集合
    /// 这些表是通过语义检索或关键字匹配从完整数据库架构中筛选出来的
    /// Collection of table documents relevant to the current query
    /// These tables are filtered from the complete database schema through semantic retrieval or keyword matching
    /// </summary>
    public IReadOnlyList<TableDoc> Tables { get; init; } = new List<TableDoc>();
}

