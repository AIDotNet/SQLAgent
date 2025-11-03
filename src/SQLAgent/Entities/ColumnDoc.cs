using System.Collections.Generic;

namespace SQLAgent.Entities;

/// <summary>
/// 数据库列的文档描述，包含类型、约束和统计信息
/// Document description of a database column, including type, constraints, and statistics
/// </summary>
public sealed class ColumnDoc
{
    /// <summary>
    /// 列名
    /// Column name
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// 列的别名列表，用于自然语言查询匹配
    /// List of column aliases for natural language query matching
    /// </summary>
    public string[] Aliases { get; init; } = System.Array.Empty<string>();
    
    /// <summary>
    /// 列的描述信息，说明列的用途和业务含义
    /// Description of the column, explaining its purpose and business meaning
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// 列的数据类型（如 "int", "varchar(50)", "datetime" 等）
    /// Data type of the column (e.g., "int", "varchar(50)", "datetime", etc.)
    /// </summary>
    public string DataType { get; init; } = string.Empty;
    
    /// <summary>
    /// 列是否允许 NULL 值
    /// Whether the column allows NULL values
    /// </summary>
    public bool Nullable { get; init; }
    
    /// <summary>
    /// 列的默认值表达式
    /// Default value expression for the column
    /// </summary>
    public string? Default { get; init; }
    
    /// <summary>
    /// 列级统计信息（如唯一值数量、最小值、最大值、平均值等）
    /// Column-level statistics (e.g., distinct count, min, max, average, etc.)
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Stats { get; init; }
    
    /// <summary>
    /// 列的向量嵌入，用于语义相似度搜索
    /// Vector embedding of the column for semantic similarity search
    /// </summary>
    public float[]? Vector { get; set; }
}
