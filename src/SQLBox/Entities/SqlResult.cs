using System.Collections.Generic;

namespace SQLBox.Entities;

/// <summary>
/// SQL 生成和执行的结果
/// Result of SQL generation and execution
/// </summary>
/// <param name="Sql">
/// 生成的 SQL 查询语句
/// Generated SQL query statement
/// </param>
/// <param name="Parameters">
/// SQL 参数字典（用于参数化查询）
/// Dictionary of SQL parameters (for parameterized queries)
/// </param>
/// <param name="Dialect">
/// 使用的 SQL 方言
/// SQL dialect used
/// </param>
/// <param name="TouchedTables">
/// 查询涉及的表名数组
/// Array of table names touched by the query
/// </param>
/// <param name="Explanation">
/// 对生成的 SQL 的解释说明，包括查询意图、使用的表和逻辑
/// Explanation of the generated SQL, including query intent, tables used, and logic
/// </param>
/// <param name="Confidence">
/// 生成结果的置信度评估（如 "high", "medium", "low"）
/// Confidence assessment of the generated result (e.g., "high", "medium", "low")
/// </param>
/// <param name="Warnings">
/// 警告信息数组（如潜在的性能问题、数据安全问题等）
/// Array of warning messages (e.g., potential performance issues, data safety concerns, etc.)
/// </param>
/// <param name="ExecutionPreview">
/// 执行预览或结果摘要（如果执行了查询）
/// Execution preview or result summary (if query was executed)
/// </param>
public sealed record SqlResult(
    string[] Sql,
    IReadOnlyDictionary<string, object?> Parameters,
    string Dialect,
    string[] TouchedTables,
    string Explanation,
    string Confidence,
    string[] Warnings,
    string? ExecutionPreview
);

