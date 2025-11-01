using System.ComponentModel;
using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SQLBox.Infrastructure;
using SQLBox.Model;

namespace SQLBox.Facade;

public class SqlBoxClient
{
    private readonly SqlBoxOptions _options;
    private readonly string _systemPrompt;
    private readonly Kernel _kernel = null!;
    private readonly SqlTool _sqlTool;

    /// <summary>
    /// 是否启用向量检索
    /// </summary>
    /// <returns></returns>
    private readonly bool _useVectorSearch = false;

    internal SqlBoxClient(SqlBoxOptions options, string systemPrompt)
    {
        _options = options;
        _systemPrompt = systemPrompt;

        _useVectorSearch = !string.IsNullOrWhiteSpace(options.EmbeddingModel) &&
                           !string.IsNullOrWhiteSpace(options.DatabaseIndexConnectionString);

        _sqlTool = new SqlTool(this);
    }

    public async Task<SqlBoxResult> ExecuteAsync(ExecuteInput input)
    {
        var kernel = KernelFactory.CreateKernel(_options.Model, _options.APIKey, _options.Endpoint,
            (builder => { builder.Plugins.AddFromObject(_sqlTool, "sql"); }));
        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddSystemMessage(_systemPrompt);

        history.AddUserMessage([
            new TextContent(input.Query),
            new TextContent("""
                            <system-remind>
                            This is a reminder. Your job is merely to assist users in generating SQL. If the task has nothing to do with SQL, please respond politely with a rejection.
                            - When operating on the tables of the database, if you are not familiar with its structure, please retrieve the information first before proceeding with the operation.
                            - Always use parameterized queries to prevent SQL injection.
                            </system-remind>
                            """)
        ]);

        await foreach (var item in chatCompletion.GetStreamingChatMessageContentsAsync(history,
                           new OpenAIPromptExecutionSettings()
                           {
                               MaxTokens = _options.MaxOutputTokens,
                               Temperature = 0.2f,
                               ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                           }, kernel))
        {
            Console.Write(item.Content);
        }

        // 判断SQL是否是查询
        if (_sqlTool.SqlBoxResult.IsQuery)
        {
            var echartsTool = new EchartsTool();
            var value = await ExecuteSqliteQueryAsync(_sqlTool.SqlBoxResult);

            kernel = KernelFactory.CreateKernel(_options.Model, _options.APIKey, _options.Endpoint,
                (builder => { builder.Plugins.AddFromObject(echartsTool, "echarts"); }));
            chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

            var echartsHistory = new ChatHistory();
            echartsHistory.AddSystemMessage("""
                                            You are a professional data visualization specialist with expertise in Apache ECharts.

                                            IMPORTANT: Generate production-ready, semantically appropriate ECharts configurations. Automatically infer the best chart type from data patterns.

                                            # Core Requirements
                                            - Analyze SQL query structure and result patterns to determine optimal visualization
                                            - Generate complete, executable ECharts option objects in valid JSON format
                                            - Design responsive, accessible, and visually appealing charts
                                            - Follow ECharts best practices and conventions

                                            # Chart Type Selection Strategy
                                            Automatically select chart types based on:
                                            - **Line Chart**: Time series data, trends over continuous intervals
                                            - **Bar Chart**: Categorical comparisons, rankings, grouped data
                                            - **Pie Chart**: Proportions, percentages, composition (limit to 2-8 segments)
                                            - **Scatter Chart**: Correlation analysis, distribution patterns
                                            - **Table**: Complex multi-column data, detailed records

                                            # Data Integration Pattern
                                            CRITICAL: Generate placeholder structure using `{{DATA_PLACEHOLDER}}` where query results will be injected:
                                            ```json
                                            {
                                              "series": [{
                                                "data": {{DATA_PLACEHOLDER}}
                                              }]
                                            }
                                            ```

                                            # Configuration Standards
                                            - Include responsive `grid` settings with proper margins
                                            - Add interactive `tooltip` with formatted display
                                            - Provide clear `title` with subtitle if needed
                                            - Use semantic color schemes from ECharts palette
                                            - Enable `dataZoom` for large datasets (>50 points)
                                            - Add `legend` for multi-series charts

                                            # Quality Requirements
                                            - Ensure all property names follow ECharts API exactly
                                            - Use camelCase for property names consistently
                                            - Include `animation` configuration for smooth transitions
                                            - Set appropriate `emphasis` states for interactivity
                                            - Add `axisLabel` formatters for dates, currencies, percentages

                                            # Automatic Optimizations
                                            - Apply `sampling` for datasets >1000 points
                                            - Use `progressive` rendering for complex visualizations
                                            - Include `aria` settings for accessibility
                                            - Set reasonable `animationDuration` (750-1500ms)

                                            Generate complete ECharts option JSON without explanations or confirmations.
                                            """);

            bool? any = _sqlTool.SqlBoxResult.Parameters.Any();

            var userMessageText = $"""
                                   Generate an ECharts option configuration for the following SQL query results.

                                   # SQL Query Context
                                   ```sql
                                   {_sqlTool.SqlBoxResult.Sql}
                                   ```

                                   # Query Parameters
                                   {(any == true
                                       ? string.Join("\n", _sqlTool.SqlBoxResult.Parameters.Select(p => $"- {p.Name}: {p.Value}"))
                                       : "No parameters")}

                                   # Data Structure Analysis
                                   The query returns the following result set that needs visualization.
                                   Analyze the SQL structure to infer:
                                   1. Column names and data types
                                   2. Aggregation patterns (SUM, COUNT, AVG, etc.)
                                   3. Grouping dimensions
                                   4. Temporal patterns (dates, timestamps)

                                   # Output Requirements
                                   Generate a complete ECharts option object with:
                                   - Appropriate chart type based on data characteristics
                                   - Complete axis configurations (if applicable)
                                   - Series definitions with `{"{DATA_PLACEHOLDER}"}` for data injection
                                   - Professional styling and interaction settings

                                   # Data Injection Format
                                   Use `{"{DATA_PLACEHOLDER}"}` where the C# code will inject actual data:
                                   ```json
                                   {"{"}
                                     "xAxis": {"{"} "data": {"{DATA_PLACEHOLDER_X}"} {"}"},
                                     "series": [{"{"} "data": {"{DATA_PLACEHOLDER_Y}"} {"}"}]
                                   {"}"}
                                   ```

                                   Return ONLY the JSON option object, no additional text.
                                   """;
            echartsHistory.AddUserMessage(userMessageText);

            await foreach (var item in chatCompletion.GetStreamingChatMessageContentsAsync(echartsHistory,
                               new OpenAIPromptExecutionSettings()
                               {
                                   MaxTokens = _options.MaxOutputTokens,
                                   Temperature = 0.2f,
                                   ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                               }, kernel))
            {
                Console.Write(item.Content);
            }

            // 获取生成的 ECharts option 并注入实际数据
            if (!string.IsNullOrWhiteSpace(echartsTool.EchartsOption) && value is { Length: > 0 })
            {
                var processedOption = InjectDataIntoEchartsOption(echartsTool.EchartsOption, value);
                echartsTool.EchartsOption = processedOption;

                // 将 ECharts option 保存到结果对象中
                _sqlTool.SqlBoxResult.EchartsOption = processedOption;
            }
        }
        else
        {
            // 执行非查询操作（INSERT, UPDATE, DELETE, CREATE, DROP 等）
            if (_options.SqlType == SqlType.Sqlite)
            {
                await ExecuteSqliteNonQueryAsync(_sqlTool.SqlBoxResult);
            }
        }

        return _sqlTool.SqlBoxResult;
    }

    /// <summary>
    /// 使用 Dapper 执行 SQLite 参数化查询
    /// </summary>
    private async Task<dynamic[]?> ExecuteSqliteQueryAsync(SqlBoxResult result)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var param = new List<KeyValuePair<string, object>>();
            foreach (var parameter in result.Parameters)
            {
                param.Add(new KeyValuePair<string, object>(parameter.Name, parameter.Value));
            }

            // 使用 Dapper 执行参数化查询
            var queryResult = await connection.QueryAsync(
                result.Sql, param,
                commandType: CommandType.Text
            );

            // 将查询结果存储到 result 对象中（如果需要的话）
            // 这里可以根据需要处理查询结果
            // 例如: result.Data = queryResult.ToList();

            Console.WriteLine($"\n查询成功执行，返回 {queryResult.Count()} 行数据");

            return queryResult.ToArray();
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"查询执行失败: {ex.Message}";
            Console.WriteLine($"\n错误: {result.ErrorMessage}");

            throw;
        }
    }

    /// <summary>
    /// 使用 Dapper 执行 SQLite 参数化非查询操作（INSERT, UPDATE, DELETE, CREATE, DROP 等）
    /// </summary>
    private async Task<int> ExecuteSqliteNonQueryAsync(SqlBoxResult result)
    {
        // 检查是否允许写操作
        if (!_options.AllowWrite)
        {
            result.ErrorMessage = "写操作已被禁用。请在配置中启用 AllowWrite 选项。";
            Console.WriteLine($"\n错误: {result.ErrorMessage}");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            result.ErrorMessage = "数据库连接字符串未配置";
            Console.WriteLine($"\n错误: {result.ErrorMessage}");
            return 0;
        }

        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var param = new List<KeyValuePair<string, object>>();
            foreach (var parameter in result.Parameters)
            {
                if (!parameter.Name.StartsWith("@"))
                {
                    parameter.Name = "@" + parameter.Name;
                }

                param.Add(new KeyValuePair<string, object>(parameter.Name, parameter.Value));
            }

            // 使用 Dapper 执行参数化非查询操作
            var affectedRows = await connection.ExecuteAsync(
                result.Sql,
                param,
                commandType: CommandType.Text
            );

            Console.WriteLine($"\n非查询操作成功执行，影响了 {affectedRows} 行数据");
            return affectedRows;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"非查询操作执行失败: {ex.Message}";
            Console.WriteLine($"\n错误: {result.ErrorMessage}");
            throw;
        }
    }

    /// <summary>
    /// 将查询结果数据注入到 ECharts option 字符串中,替换占位符
    /// </summary>
    private string InjectDataIntoEchartsOption(string optionTemplate, dynamic[] queryResults)
    {
        if (string.IsNullOrWhiteSpace(optionTemplate) || queryResults == null || queryResults.Length == 0)
        {
            return optionTemplate;
        }

        try
        {
            // 将动态结果转换为可序列化的格式
            var dataJson = JsonSerializer.Serialize(queryResults, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

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
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n警告: 数据注入失败 - {ex.Message}");
            return optionTemplate;
        }
    }

    class EchartsTool
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

    class SqlTool(SqlBoxClient sqlBoxClient)
    {
        public SqlBoxResult SqlBoxResult = null!;

        [KernelFunction("Write"), Description(
             """
             Writes the generated SQL statement.

             Usage:
             - This tool should be called when you have generated the final SQL statement.
             - The SQL will be directly written and executed.
             - Ensure the SQL statement is correct and complete before calling this tool.
             """)]
        public string Write(SqlBoxResult items)
        {
            SqlBoxResult = items;
            return """
                   <system-remind>
                   The SQL has been written and completed.
                   </system-remind>
                   """;
        }

        /// <summary>
        /// 检索数据库架构信息
        /// </summary>
        /// <returns></returns>
        [KernelFunction("GetSchemaContext"), Description(
             """
             Retrieves the database schema context based on provided keywords.

             Usage:
             - This tool is used to fetch relevant table and column information from the database schema.
             - Provide keywords to filter the tables and columns that are most relevant to the current query.
             - The maximum number of tables to return can be specified.

             Parameters:
             - keywords: Keywords used to filter relevant tables and columns.
             - maxTables: Maximum number of tables to return (default is 5).
             - Returns: A string containing the schema information of the relevant tables.
             - Note: This is for searching the schema tables of the database. Please use the appropriate keywords.For example, table names, column names, etc.
             """)]
        public async Task<string> GetSchemaContext(
            [Description(
                "Keywords, used for filtering relevant tables and columns. Note that this is for searching the schema tables of the database. Please use the appropriate keywords.")]
            string keywords,
            [Description("The maximum number of tables with returns")]
            int maxTables = 5)
        {
            // 生成查询sqlite的查询架构SQL
            string schemaQuery = $"""
                                  SELECT name, sql
                                  FROM sqlite_master
                                  WHERE type='table' AND name NOT LIKE 'sqlite_%'
                                  AND (name LIKE '%' || @keywords || '%' OR sql LIKE '%' || @keywords || '%')
                                  LIMIT {maxTables};
                                  """;

            var value = await sqlBoxClient.ExecuteSqliteQueryAsync(new SqlBoxResult()
            {
                Sql = schemaQuery,
                Parameters = new List<SqlBoxParameter>()
                {
                    new SqlBoxParameter()
                    {
                        Name = "@keywords",
                        Value = keywords
                    }
                }
            });

            // 简单拼接表结构信息返回
            var schemaInfo = string.Empty;
            if (value is { Length: > 0 })
            {
                foreach (var row in value)
                {
                    if (row is IDictionary<string, object> dict && dict.ContainsKey("name") && dict.ContainsKey("sql"))
                    {
                        var tableName = dict["name"]?.ToString();
                        var tableSql = dict["sql"]?.ToString();
                        schemaInfo += $"Table: {tableName}\nDefinition: {tableSql}\n\n";
                    }
                }
            }

            if (string.IsNullOrEmpty(schemaInfo))
            {
                return "No relevant schema information found.";
            }

            return schemaInfo;
        }
    }
}