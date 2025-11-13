using System.ComponentModel;
using AIDotNet.Toon;
using Microsoft.SemanticKernel;
using SQLAgent.Model;

namespace SQLAgent.Facade;

public class SqlTool(SQLAgentClient sqlAgentClient)
{
    public readonly List<SQLAgentResult> SqlBoxResult = new();

    [KernelFunction("Write"), Description(
         """
         Writes the generated SQL statement.

         Usage:
         - This tool should be called when you have generated the final SQL statement.
         - The SQL will be directly written and executed.
         - Ensure the SQL statement is correct and complete before calling this tool.
         """)]
    public string Write(
        [Description("""
                     Generated SQL statement: If parameterized query is used, it will be an SQL statement with parameters. 
                     <example>
                     SELECT * FROM Users WHERE Age > @AgeParam
                     </example>
                     """)]
        string sql,
        [Description("If it is not possible to generate a SQL-friendly version, inform the user accordingly.")]
        string? errorMessage,
        [Description("Indicate the type of SQL currently being executed.")]
        SqlBoxExecuteType executeType,
        [Description("Columns involved in the SQL statement, if any")]
        Dictionary<string, string>? columns = null,
        [Description("Parameters for the SQL statement, if any")]
        SqlBoxParameter[]? parameters = null)
    {
        // 验证：Query 和 EChart 类型必须提供 columns
        if (executeType is SqlBoxExecuteType.Query or SqlBoxExecuteType.EChart
            && (columns == null || columns.Count == 0))
        {
            return """
                   <system-error>
                   ERROR: When executeType is Query or EChart, the 'columns' parameter is REQUIRED.
                   Please specify all columns that appear in the SELECT clause.
                   </system-error>
                   """;
        }

        var items = new SQLAgentResult
        {
            Sql = sql,
            Columns = columns,
            ExecuteType = executeType,
            ErrorMessage = errorMessage,
            Parameters = parameters?.ToList() ?? new List<SqlBoxParameter>()
        };
        SqlBoxResult.Add(items);
        return """
               <system-remind>
               The SQL has been written and completed.
               </system-remind>
               """;
    }

    /// <summary>
    /// 模糊搜索表名（返回表名和表的详细信息）
    /// </summary>
    [KernelFunction("SearchTables"), Description(
         """
         Fuzzy search table names using one or more keywords. Returns a JSON array of matching tables with detailed information.

         Parameters:
         - keywords: An array of keywords to search for in table names, schema names, comments, or column names.
         - maxResults: Maximum number of tables to return.

         Returns:
         A JSON array of table objects, each containing:
         - name: Full qualified table name (schema.table)
         - schema: Schema/database name
         - table: Table name
         - type: Table type (e.g., "BASE TABLE", "VIEW", "MATERIALIZED VIEW")
         - comment: Table comment/description (if available)
         - createSql: CREATE TABLE statement (for SQLite only)
         """)]
    public async Task<string> SearchTables(
        [Description("Array of keywords for fuzzy search")]
        string[] keywords,
        [Description("Maximum number of results to return")]
        int maxResults = 20)
    {
        maxResults = Math.Clamp(maxResults, 1, 100);
        if (keywords == null) keywords = [];

        try
        {
            var tableInfoJson = await sqlAgentClient.DatabaseService.SearchTables(keywords, maxResults);

            return tableInfoJson;
        }
        catch (Exception ex)
        {
            return ToonSerializer.Serialize(new { error = ex.Message });
        }
    }
}