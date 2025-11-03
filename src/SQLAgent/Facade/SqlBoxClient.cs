using System.ComponentModel;
using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SQLAgent.Infrastructure;
using SQLAgent.Model;

namespace SQLAgent.Facade;

public class SqlBoxClient
{
    private readonly SqlBoxOptions _options;
    private readonly string _systemPrompt;
    private readonly Kernel _kernel = null!;
    private readonly SqlTool _sqlResult;

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

        _sqlResult = new SqlTool(this);
    }

    public async Task<List<SqlBoxResult>> ExecuteAsync(ExecuteInput input)
    {
        var kernel = KernelFactory.CreateKernel(_options.Model, _options.APIKey, _options.Endpoint,
            (builder => { builder.Plugins.AddFromObject(_sqlResult, "sql"); }));
        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddSystemMessage(_systemPrompt);

        history.AddUserMessage([
            new TextContent(input.Query),
            new TextContent("""
                            <system-remind>
                            This is a reminder. Your job is to assist users in generating SQL or ECharts configurations. You MUST follow the workflow below exactly and avoid guessing schema information.

                            WORKFLOW (MANDATORY):
                            1) Call `SearchTables(keywords[], maxResults)` to find candidate table names matching the user's intent.
                            2) For each candidate you plan to use, call `GetTableSchema(tableName)` to obtain structured schema JSON (columns, types, indexes).
                            3) Construct parameterized SQL using ONLY the column names returned by `GetTableSchema`.
                            4) Return the final SQL by calling `sql-Write` with a JSON object exactly in this shape:
                               { "Sql": "<sql>", "Parameters": [{ "Name": "@p1", "Value": 123 }, ...], "IsQuery": true|false }
                            - Parameter rules: names MUST start with '@'; do NOT inline literal values into SQL.

                            DATA SELECTION RULES (CRITICAL FOR VISUALIZATION):
                            When generating SELECT queries, the results will be used for chart rendering. Follow these column selection principles:
                            - AVOID selecting ID fields (like id, user_id, order_id) unless explicitly requested by the user, as they provide no visualization value.
                            - PREFER descriptive columns: names, titles, labels, categories, status values that provide meaningful context.
                            - ALWAYS include numeric/aggregate columns: counts, sums, averages, totals, quantities that represent measurable data.
                            - For time-series analysis: include temporal columns (date, timestamp, year, month, etc.) for trend visualization.
                            - For categorical analysis: include grouping dimensions (category, type, region, status, etc.).
                            - Limit columns to 2-5 fields that directly support the visualization goal: typically one dimension (X-axis) and one or more measures (Y-axis).
                            - Use meaningful column aliases with AS to improve chart readability (e.g., "total_amount AS sales", "COUNT(*) AS order_count").

                            Example of good query for charts:
                            SELECT category AS product_category, SUM(amount) AS total_sales FROM orders GROUP BY category

                            Example of poor query for charts:
                            SELECT id, user_id, order_id, amount FROM orders

                            SAFETY RULES:
                            - Do NOT invent table or column names that do not appear in `GetTableSchema` results.
                            - If the SQL performs data-modifying operations (INSERT/UPDATE/DELETE/CREATE/DROP), require human confirmation before execution or refuse if execution is not allowed.
                            - If AllowWrite is false, do not request or perform any write operation; return a polite refusal instead.
                            - Never generate DROP or TRUNCATE unless explicitly instructed and confirmed by a human.

                            SEARCH STRATEGY GUIDANCE:
                            - Use the keywords array for `SearchTables`. Prefer lexical (LIKE) matching first.
                            - If `SearchTables` returns no or low-confidence results and vector search is enabled, use vector fallback. Only trigger vectors when lexical fails.
                            - Provide multiple keywords (table name, column name, sample value) to improve recall.

                            ECHARTS CONSTRAINTS:
                            - When generating ECharts options, return ONLY the JSON option object (no explanation).
                            - Use placeholders: `{{DATA_PLACEHOLDER}}` or `{{DATA_PLACEHOLDER_X}}` / `{{DATA_PLACEHOLDER_Y}}` for injection points.
                            - Save the generated option by calling `echarts-Write(optionJson)`.

                            EXAMPLES (use these formats):
                            - SearchTables call: SearchTables(["orders","customer"], 10)
                            - GetTableSchema call: GetTableSchema("orders") -> returns JSON with columns
                            - Good sql-Write for visualization:
                              { "Sql": "SELECT product_name, SUM(quantity) AS total_sold FROM \"orders\" WHERE year = @year GROUP BY product_name", "Parameters": [{ "Name": "@year", "Value": 2024 }], "IsQuery": true }
                            - Poor sql-Write (includes unnecessary ID):
                              { "Sql": "SELECT id, product_name, quantity FROM \"orders\"", "Parameters": [], "IsQuery": true }

                            If the user request is not about SQL generation or ECharts, politely decline.
                            </system-remind>
                            """)
        ]);

        await chatCompletion.GetChatMessageContentsAsync(history,
            new OpenAIPromptExecutionSettings()
            {
                MaxTokens = _options.MaxOutputTokens,
                Temperature = 0.2f,
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            }, kernel);

        foreach (var _sqlTool in _sqlResult.SqlBoxResult)
        {
            // 判断SQL是否是查询
            if (_sqlTool.IsQuery)
            {
                var echartsTool = new EchartsTool();
                var value = await ExecuteSqliteQueryAsync(_sqlTool);

                kernel = KernelFactory.CreateKernel(_options.Model, _options.APIKey, _options.Endpoint,
                    (builder => { builder.Plugins.AddFromObject(echartsTool, "echarts"); }));
                chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

                var echartsHistory = new ChatHistory();
                echartsHistory.AddSystemMessage("""
                                                You are a professional data visualization specialist with expertise in Apache ECharts.

                                                IMPORTANT: Generate production-ready, semantically appropriate ECharts configurations with modern, beautiful styling. Automatically infer the best chart type from data patterns.

                                                # Core Requirements
                                                - Analyze SQL query structure and result patterns to determine optimal visualization
                                                - Generate complete, executable ECharts option objects in valid JSON format
                                                - Design responsive, accessible, and visually stunning charts
                                                - Follow ECharts best practices and modern UI design principles

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

                                                # Visual Design Standards (CRITICAL)
                                                Apply modern, professional styling to all charts:

                                                ## Color Palette
                                                - Use vibrant, harmonious color schemes: ['#5470c6', '#91cc75', '#fac858', '#ee6666', '#73c0de', '#3ba272', '#fc8452', '#9a60b4', '#ea7ccc']
                                                - For single-series charts, use gradient fills for visual depth
                                                - Ensure sufficient contrast for accessibility (WCAG AA minimum)

                                                ## Typography
                                                - Title: fontSize 18-20, fontWeight 'bold', color '#333'
                                                - Subtitle: fontSize 12-14, color '#999'
                                                - Axis labels: fontSize 12, color '#666'
                                                - Legend: fontSize 12, color '#666'

                                                ## Spacing and Layout
                                                - Grid margins: top 60-80, right 40-60, bottom 60-80, left 60-80
                                                - Increase margins if titles/legends are present
                                                - Use containLabel: true for automatic label space calculation

                                                ## Visual Effects
                                                - Apply borderRadius to bar charts (4-8px) for modern appearance
                                                - Use itemStyle with shadowBlur (5-10), shadowColor 'rgba(0,0,0,0.1)' for depth
                                                - Enable smooth curves for line charts (smooth: true)
                                                - Add areaStyle with gradient for line charts when appropriate

                                                ## Interactive Elements
                                                - Rich tooltip with formatted values, background color 'rgba(50,50,50,0.9)', borderColor transparent
                                                - Emphasis states with scale (1.05-1.1) and deeper shadows
                                                - Subtle animations (animationDuration: 1000-1200ms, animationEasing: 'cubicOut')

                                                # Language Localization (MANDATORY)
                                                CRITICAL: All text in the chart MUST match the user's query language:
                                                - Detect the language from the user's SQL query and question
                                                - If user writes in Chinese, use Chinese for title, axis labels, legend, tooltip, etc.
                                                - If user writes in English, use English for all text elements
                                                - Maintain language consistency across all text elements in the chart
                                                - Examples:
                                                  * Chinese query -> title: "销售趋势分析", xAxis.name: "日期", yAxis.name: "销售额"
                                                  * English query -> title: "Sales Trend Analysis", xAxis.name: "Date", yAxis.name: "Sales Amount"

                                                # Configuration Standards
                                                - Include responsive grid settings with proper margins
                                                - Add interactive tooltip with formatted display and styling
                                                - Provide clear title with subtitle if contextually appropriate
                                                - Enable dataZoom for large datasets (>50 points)
                                                - Add legend for multi-series charts with proper positioning

                                                # Quality Requirements
                                                - Ensure all property names follow ECharts API exactly
                                                - Use camelCase for property names consistently
                                                - Include animation configuration for smooth transitions
                                                - Set appropriate emphasis states for interactivity
                                                - Add axisLabel formatters for dates, currencies, percentages
                                                - Apply professional visual polish (shadows, gradients, rounded corners)

                                                # Automatic Optimizations
                                                - Apply sampling for datasets >1000 points
                                                - Use progressive rendering for complex visualizations
                                                - Include aria settings for accessibility
                                                - Set reasonable animationDuration (1000-1200ms)

                                                Generate complete ECharts option JSON without explanations or confirmations.
                                                """);

                bool? any = _sqlTool.Parameters.Any();

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
                echartsHistory.AddUserMessage(new ChatMessageContentItemCollection()
                {
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
                });

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
                }
            }
            else
            {
                // 执行非查询操作（INSERT, UPDATE, DELETE, CREATE, DROP 等）
                if (_options.SqlType == SqlType.Sqlite)
                {
                    await ExecuteSqliteNonQueryAsync(_sqlTool);
                }
            }
        }


        return _sqlResult.SqlBoxResult;
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
            var option = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                // 中文字符不进行转义
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            // 将动态结果转换为可序列化的格式
            var dataJson = JsonSerializer.Serialize(queryResults, option);

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

    public class SqlTool(SqlBoxClient sqlBoxClient)
    {
        public List<SqlBoxResult> SqlBoxResult = new();

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
            [Description("Indicates whether the SQL is a query statement")]
            bool isQuery,
            [Description("Parameters for the SQL statement, if any")]
            SqlBoxParameter[]? parameters = null)
        {
            var items = new SqlBoxResult
            {
                Sql = sql,
                IsQuery = isQuery,
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
        /// 模糊搜索表名（只返回表名列表）
        /// </summary>
        [KernelFunction("SearchTables"), Description(
             """
             Fuzzy search table names using one or more keywords. Returns a JSON array of matching table names.

             Parameters:
             - keywords: An array of keywords to search for in table names or CREATE SQL.
             - maxResults: Maximum number of table names to return.
             """)]
        public async Task<string> SearchTables(
            [Description("Array of keywords for fuzzy search")]
            string[] keywords,
            [Description("Maximum number of results to return")]
            int maxResults = 20)
        {
            maxResults = Math.Clamp(maxResults, 1, 100);
            if (keywords == null) keywords = Array.Empty<string>();

            try
            {
                await using var connection = new SqliteConnection(sqlBoxClient._options.ConnectionString);
                await connection.OpenAsync();

                string sql;
                var dp = new Dapper.DynamicParameters();

                if (keywords.Length == 0)
                {
                    sql = @"
                        SELECT name
                        FROM sqlite_master
                        WHERE type='table' AND name NOT LIKE 'sqlite_%'
                        LIMIT @maxResults;";
                    dp.Add("maxResults", maxResults);
                }
                else
                {
                    var limitKeys = Math.Min(keywords.Length, 10);
                    var conds = new List<string>();
                    for (int i = 0; i < limitKeys; i++)
                    {
                        var param = $"k{i}";
                        dp.Add(param, keywords[i]);
                        conds.Add($"(name LIKE '%' || @{param} || '%' OR sql LIKE '%' || @{param} || '%')");
                    }

                    sql = $@"
                        SELECT name
                        FROM sqlite_master
                        WHERE type='table' AND name NOT LIKE 'sqlite_%'
                          AND ({string.Join(" OR ", conds)})
                        LIMIT @maxResults;";
                    dp.Add("maxResults", maxResults);
                }

                var rows = (await connection.QueryAsync(sql, dp)).ToArray();
                var names = new List<string>();
                foreach (var r in rows)
                {
                    if (r is IDictionary<string, object> d && d.TryGetValue("name", out var n))
                        names.Add(n?.ToString() ?? string.Empty);
                    else
                    {
                        try
                        {
                            names.Add((r as dynamic)?.name?.ToString() ?? string.Empty);
                        }
                        catch
                        {
                        }
                    }
                }

                return JsonSerializer.Serialize(names,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        /// <summary>
        /// 根据表名获取该表的结构化 schema（columns, indexes, createSql, sampleRows）
        /// </summary>
        [KernelFunction("GetTableSchema"), Description(
             """
             Retrieve structured schema information for a specific table.

             Parameters:
             - tableName: Exact table name to retrieve schema for.

             Returns a JSON object with columns, indexes, createSql and sampleRows.
             """)]
        public async Task<string> GetTableSchema(
            [Description("Exact table name to get schema for")]
            string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return JsonSerializer.Serialize(new { error = "tableName is required" });
            }

            try
            {
                await using var connection = new SqliteConnection(sqlBoxClient._options.ConnectionString);
                await connection.OpenAsync();

                var master = await connection.QueryFirstOrDefaultAsync(
                    "SELECT name, sql FROM sqlite_master WHERE type='table' AND name = @table;",
                    new { table = tableName });

                if (master == null)
                {
                    return JsonSerializer.Serialize(new { error = "table not found" });
                }

                string? createSql = null;
                if (master is IDictionary<string, object> md && md.TryGetValue("sql", out var s))
                    createSql = s?.ToString();
                else
                {
                    try
                    {
                        createSql = (master as dynamic)?.sql?.ToString();
                    }
                    catch
                    {
                    }
                }

                var safeName = tableName.Replace("\"", "\"\"");

                // columns
                var pragmaCols = await connection.QueryAsync($"PRAGMA table_info(\"{safeName}\");");
                var columns = new List<object>();
                foreach (var c in pragmaCols)
                {
                    if (c is IDictionary<string, object> colDict)
                    {
                        colDict.TryGetValue("name", out var colName);
                        colDict.TryGetValue("type", out var colType);
                        colDict.TryGetValue("notnull", out var colNotNull);
                        colDict.TryGetValue("pk", out var colPk);
                        colDict.TryGetValue("dflt_value", out var colDefault);

                        columns.Add(new
                        {
                            name = colName,
                            type = colType,
                            notnull = colNotNull,
                            pk = colPk,
                            defaultValue = colDefault
                        });
                    }
                    else
                    {
                        try
                        {
                            columns.Add(new
                            {
                                name = (c as dynamic)?.name,
                                type = (c as dynamic)?.type,
                                notnull = (c as dynamic)?.notnull,
                                pk = (c as dynamic)?.pk,
                                defaultValue = (c as dynamic)?.dflt_value
                            });
                        }
                        catch
                        {
                        }
                    }
                }

                // indexes
                var indexes = await connection.QueryAsync($"PRAGMA index_list(\"{safeName}\");");

                var result = new
                {
                    table = tableName,
                    createSql = createSql,
                    columns = columns,
                    indexes = indexes,
                };

                return JsonSerializer.Serialize(result,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }
    }
}