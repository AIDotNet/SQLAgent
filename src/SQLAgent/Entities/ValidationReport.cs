using System.Collections.Generic;

namespace SQLAgent.Entities;

/// <summary>
/// SQL 验证报告
/// SQL validation report
/// </summary>
/// <param name="IsValid">
/// SQL 是否有效（没有错误）
/// Whether the SQL is valid (no errors)
/// </param>
/// <param name="Warnings">
/// 警告信息数组（不影响执行但需要注意的问题）
/// Array of warning messages (issues that don't prevent execution but need attention)
/// </param>
/// <param name="Errors">
/// 错误信息数组（阻止执行的问题）
/// Array of error messages (issues that prevent execution)
/// </param>
/// <param name="TouchedTables">
/// SQL 涉及的表名数组
/// Array of table names touched by the SQL
/// </param>
/// <param name="Confidence">
/// 验证结果的置信度（如 "high", "medium", "low"）
/// Confidence level of the validation result (e.g., "high", "medium", "low")
/// </param>
public sealed record ValidationReport(
    bool IsValid,
    string[] Warnings,
    string[] Errors,
    string[] TouchedTables,
    string Confidence
);

