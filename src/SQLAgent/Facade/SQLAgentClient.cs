using AIDotNet.Toon;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SQLAgent.Infrastructure;
using SQLAgent.Model;
using SQLAgent.Prompts;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SQLAgent.Facade;

public class SQLAgentClient
{
    private static readonly ActivitySource ActivitySource = new("SQLAgent");

    private readonly SQLAgentOptions _options;
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<SQLAgentClient> _logger;
    private readonly SqlTool _sqlResult;

    /// <summary>
    /// 是否启用向量检索
    /// </summary>
    /// <returns></returns>
    private readonly bool _useVectorDatabaseIndex = false;

    internal SQLAgentClient(SQLAgentOptions options, IDatabaseService databaseService, ILogger<SQLAgentClient> logger)
    {
        _options = options;
        _databaseService = databaseService;
        _logger = logger;

        _useVectorDatabaseIndex = options.UseVectorDatabaseIndex;

        _sqlResult = new SqlTool(this);
    }

    /// <summary>
    /// 执行 SQL 代理请求
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<List<SQLAgentResult>> ExecuteAsync(ExecuteInput input)
    {
        using var activity = ActivitySource.StartActivity("SQLAgent.Execute", ActivityKind.Internal);
        activity?.SetTag("sqlagent.query", input.Query);

        _logger.LogInformation("Starting SQL Agent execution for query: {Query}", input.Query);

        var kernel = KernelFactory.CreateKernel(_options.Model, _options.APIKey, _options.Endpoint,
            (builder => { builder.Plugins.AddFromObject(_sqlResult, "sql"); }));
        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddSystemMessage(_options.SqlBotSystemPrompt);

        history.AddUserMessage([
            new TextContent(input.Query),
            new TextContent(PromptConstants.SQLGeneratorSystemRemindPrompt),
            new TextContent($"""
                             <user-env>
                             {(_options.AllowWrite ? "The user has granted you the permission to directly manipulate the database, including creating, updating and deleting records." : "Database write operations are NOT allowed.")}
                             The database type is {_options.SqlType}.
                             CRITICAL: When calling sql-Write with executeType=Query or executeType=EChart, 
                             you MUST provide the 'columns' parameter with all SELECT columns.
                             <user-env>
                             """)
        ]);

        _logger.LogInformation("Calling AI model to generate SQL for query: {Query}", input.Query);

        await chatCompletion.GetChatMessageContentsAsync(history,
            new OpenAIPromptExecutionSettings()
            {
                MaxTokens = _options.MaxOutputTokens,
                Temperature = 0.2f,
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            }, kernel);

        _logger.LogInformation("AI model call completed, processing {Count} SQL results",
            _sqlResult.SqlBoxResult.Count);

        foreach (var _sqlTool in _sqlResult.SqlBoxResult)
        {
            _logger.LogInformation("Processing SQL result: executeType={executeType}, SQL={Sql}", _sqlTool.ExecuteType,
                _sqlTool.Sql);

            switch (_sqlTool.ExecuteType)
            {
                // 判断SQL是否是查询
                case SqlBoxExecuteType.EChart:
                {
                    var echartsTool = new EchartsTool();
                    var value = await ExecuteSqliteQueryAsync(_sqlTool);

                    kernel = KernelFactory.CreateKernel(_options.Model, _options.APIKey, _options.Endpoint,
                        (builder => { builder.Plugins.AddFromObject(echartsTool, "echarts"); }), _options.AIProvider);
                    chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

                    var echartsHistory = new ChatHistory();
                    echartsHistory.AddSystemMessage(PromptConstants.SQLGeneratorEchartsDataPrompt);

                    bool? any = _sqlTool.Parameters.Count != 0;

                    var userMessageText = $$"""
                                            Generate an ECharts option configuration for the following SQL query results.

                                            # User's Original Query
                                            "{{input.Query}}"

                                            # SQL Query Context
                                            ```sql
                                            {{_sqlTool.Sql}}
                                            ```

                                            # Query Parameters
                                            {{(any == true
                                                ? string.Join("\n", _sqlTool.Parameters.Select(p => $"- {p.Name}: {p.Value}"))
                                                : "No parameters")}}

                                            # Data Structure Analysis
                                            The query returns the following result set that needs visualization.
                                            Analyze the SQL structure to infer:
                                            1. Column names and data types
                                            2. Aggregation patterns (SUM, COUNT, AVG, etc.)
                                            3. Grouping dimensions
                                            4. Temporal patterns (dates, timestamps)

                                            # Language Requirement (CRITICAL)
                                            DETECT the language from the user's original query above and use THE SAME LANGUAGE for ALL text in the chart:
                                            - Title, subtitle
                                            - Axis names and labels
                                            - Legend items
                                            - Tooltip content
                                            - All other text elements
                                            Example: If user query is in Chinese, generate Chinese title like "销售数据分析"; if English, use "Sales Data Analysis"

                                            # Output Requirements
                                            Generate a complete ECharts option object with:
                                            - Appropriate chart type based on data characteristics
                                            - Complete axis configurations with proper styling (if applicable)
                                            - Series definitions with `{DATA_PLACEHOLDER}` for data injection
                                            - Modern, beautiful visual design (colors, shadows, rounded corners, gradients)
                                            - Professional styling and interaction settings
                                            - All text elements in the SAME language as user's query

                                            # Visual Styling Requirements
                                            Apply modern design principles:
                                            - Use vibrant color palette with gradients where appropriate
                                            - Add subtle shadows (shadowBlur: 8, shadowColor: 'rgba(0,0,0,0.1)')
                                            - Apply borderRadius (6-8) to bars for rounded appearance
                                            - Use smooth curves (smooth: true) for line charts
                                            - Configure rich tooltips with background styling
                                            - Set proper grid margins (60-80px) for labels
                                            - Include animation settings (duration: 1000-1200ms)

                                            # Data Injection Format
                                            Use `{DATA_PLACEHOLDER}` where the C# code will inject actual data:
                                            ```js
                                            {
                                            "tooltip": {
                                              "trigger": "axis",
                                              "formatter": function(params) { return params[0].name + ': ' + params[0].value; }
                                            },
                                            "xAxis": {
                                              "data": {DATA_PLACEHOLDER_X}
                                            },
                                            "series": [
                                              {
                                                "data": {DATA_PLACEHOLDER_Y}
                                              }
                                            ]
                                            }
                                            ```
                                            Return ONLY the JSON option object, no additional text.
                                            """;
                    echartsHistory.AddUserMessage([
                        new TextContent(userMessageText),
                        new TextContent(
                            """
                            <system-remind>
                            This is a reminder. Your job is merely to assist users in generating ECharts options. If the task has nothing to do with ECharts, please respond politely with a rejection.
                            - Always generate complete and valid ECharts option JSON.
                            - Use the `{DATA_PLACEHOLDER}` format for data injection points.
                            - It is necessary to use `echarts-Write` to store the generated ECharts options.
                            </system-remind>
                            """)
                    ]);

                    _logger.LogInformation("Generating ECharts option for SQL query");

                    var result = await chatCompletion.GetChatMessageContentAsync(echartsHistory,
                        new OpenAIPromptExecutionSettings()
                        {
                            MaxTokens = _options.MaxOutputTokens,
                            Temperature = 0.2f,
                            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                        }, kernel);

                    // 获取生成的 ECharts option 并注入实际数据
                    if (!string.IsNullOrWhiteSpace(echartsTool.EchartsOption) && value is { Length: > 0 })
                    {
                        var processedOption = InjectDataIntoEchartsOption(echartsTool.EchartsOption, value);
                        echartsTool.EchartsOption = processedOption;

                        // 将 ECharts option 保存到结果对象中
                        _sqlTool.EchartsOption = processedOption;

                        _logger.LogInformation("ECharts option generated and data injected successfully");
                    }
                    else
                    {
                        _logger.LogWarning("No ECharts option generated or no query results to inject");
                    }

                    break;
                }
                case SqlBoxExecuteType.Query:
                {
                    var value = await ExecuteSqliteQueryAsync(_sqlTool);

                    _sqlTool.Result = value;
                    break;
                }
                default:
                    await ExecuteSqliteNonQueryAsync(_sqlTool);
                    break;
            }
        }

        _logger.LogInformation("SQL Agent execution completed, returning {Count} results",
            _sqlResult.SqlBoxResult.Count);

        return _sqlResult.SqlBoxResult;
    }

    /// <summary>
    /// 使用 Dapper 执行 SQLite 参数化查询
    /// </summary>
    private async Task<dynamic[]?> ExecuteSqliteQueryAsync(SQLAgentResult result)
    {
        using var activity = ActivitySource.StartActivity("SQLAgent.ExecuteQuery", ActivityKind.Internal);
        activity?.SetTag("sqlagent.sql", result.Sql);

        _logger.LogInformation("Executing SQL query: {Sql}", result.Sql);

        try
        {
            // 使用 Dapper 执行参数化查询
            var queryResult = await _databaseService.ExecuteSqliteQueryAsync(result.Sql, result.Parameters);
            _logger.LogInformation("Query executed successfully, returned {Count} rows", queryResult?.Count() ?? 0);

            return queryResult?.ToArray();
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"查询执行失败: {ex.Message}";
            _logger.LogError(ex, "Query execution failed: {ErrorMessage}", result.ErrorMessage);

            throw;
        }
    }

    /// <summary>
    /// 使用 Dapper 执行 SQLite 参数化非查询操作（INSERT, UPDATE, DELETE, CREATE, DROP 等）
    /// </summary>
    private async Task<int> ExecuteSqliteNonQueryAsync(SQLAgentResult result)
    {
        using var activity = ActivitySource.StartActivity("SQLAgent.ExecuteNonQuery", ActivityKind.Internal);
        activity?.SetTag("sqlagent.sql", result.Sql);

        _logger.LogInformation("Executing SQL non-query: {Sql}", result.Sql);

        // 检查是否允许写操作
        if (!_options.AllowWrite)
        {
            result.ErrorMessage = "写操作已被禁用。请在配置中启用 AllowWrite 选项。";
            _logger.LogWarning("Write operation denied: {ErrorMessage}", result.ErrorMessage);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            result.ErrorMessage = "数据库连接字符串未配置";
            _logger.LogError("Database connection string not configured");
            return 0;
        }

        try
        {
            // 使用 Dapper 执行参数化非查询操作
            var affectedRows = await _databaseService.ExecuteSqliteNonQueryAsync(result.Sql, result.Parameters);

            _logger.LogInformation("Non-query operation executed successfully, affected {AffectedRows} rows",
                affectedRows);
            return affectedRows;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"非查询操作执行失败: {ex.Message}";
            _logger.LogError(ex, "Non-query operation failed: {ErrorMessage}", result.ErrorMessage);
            throw;
        }
    }

    /// <summary>
    /// 将查询结果数据注入到 ECharts option 字符串中,替换占位符
    /// </summary>
    private string InjectDataIntoEchartsOption(string optionTemplate, dynamic[] queryResults)
    {
        using var activity = ActivitySource.StartActivity("SQLAgent.InjectData", ActivityKind.Internal);
        activity?.SetTag("sqlagent.data_count", queryResults?.Length ?? 0);

        _logger.LogInformation("Injecting data into ECharts option template");

        if (string.IsNullOrWhiteSpace(optionTemplate) || queryResults == null || queryResults.Length == 0)
        {
            _logger.LogWarning(
                "Invalid input for data injection: optionTemplate is empty or queryResults is null/empty");
            return optionTemplate;
        }

        try
        {
            // 将动态结果转换为可序列化的格式
            var dataJson = JsonSerializer.Serialize(queryResults, SQLAgentJsonOptions.DefaultOptions);

            // 替换各种可能的占位符
            var result = optionTemplate;

            // 替换 {{DATA_PLACEHOLDER}}
            result = result.Replace("{{DATA_PLACEHOLDER}}", dataJson);
            result = result.Replace("{DATA_PLACEHOLDER}", dataJson);

            // 如果需要分别处理 X 轴和 Y 轴数据
            if (queryResults.Length > 0)
            {
                if (queryResults[0] is IDictionary<string, object> { Count: >= 2 } firstItem)
                {
                    var keys = firstItem.Keys.ToArray();

                    // 提取 X 轴数据 (通常是第一列)
                    var xAxisData = queryResults.Select(row =>
                    {
                        var dict = row as IDictionary<string, object>;
                        return dict?[keys[0]];
                    }).ToArray();

                    var xAxisJson = JsonSerializer.Serialize(xAxisData, new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });

                    // 提取 Y 轴数据 (通常是第二列或后续列)
                    var yAxisData = queryResults.Select(row =>
                    {
                        var dict = row as IDictionary<string, object>;
                        return dict?[keys[1]];
                    }).ToArray();

                    var yAxisJson = JsonSerializer.Serialize(yAxisData, new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });

                    result = result.Replace("{{DATA_PLACEHOLDER_X}}", xAxisJson);
                    result = result.Replace("{DATA_PLACEHOLDER_X}", xAxisJson);
                    result = result.Replace("{{DATA_PLACEHOLDER_Y}}", yAxisJson);
                    result = result.Replace("{DATA_PLACEHOLDER_Y}", yAxisJson);

                    _logger.LogInformation("Data injection completed for X and Y axes");
                }
                else
                {
                    _logger.LogInformation("Data injection completed for single data placeholder");
                }
            }

            _logger.LogInformation("Data injection into ECharts option completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Data injection failed: {Message}", ex.Message);
            return optionTemplate;
        }
    }

    public class EchartsTool
    {
        public string EchartsOption = string.Empty;

        [KernelFunction("Write"), Description(
             """
             Writes the generated Echarts option.

             Usage:
             - This tool should be called when you have generated the final Echarts option.
             - The option will be directly written and used.
             - Ensure the Echarts option is correct and complete before calling this tool.
             """)]
        public string Write(string option)
        {
            EchartsOption = option;
            return """
                   <system-remind>
                   The Echarts option has been written and completed.
                   </system-remind>
                   """;
        }
    }

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
                var tableInfoJson = await sqlAgentClient._databaseService.SearchTables(keywords, maxResults);

                return tableInfoJson;
            }
            catch (Exception ex)
            {
                return ToonSerializer.Serialize(new { error = ex.Message });
            }
        }
    }
}