using System.ClientModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI;
using OpenAI.Chat;
using SQLAgent.Infrastructure;
using SQLAgent.Model;
using SQLAgent.Prompts;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using TextContent = Microsoft.Extensions.AI.TextContent;

namespace SQLAgent.Facade;

public class SQLAgentClient
{
    private static readonly ActivitySource ActivitySource = new("SQLAgent");

    private readonly SQLAgentOptions _options;
    internal readonly IDatabaseService DatabaseService;
    private readonly ILogger<SQLAgentClient> _logger;
    private readonly IDatabaseConnectionManager _databaseConnectionManager;
    private readonly SqlTool _sqlResult;

    /// <summary>
    /// 是否启用向量检索
    /// </summary>
    /// <returns></returns>
    private readonly bool _useVectorDatabaseIndex = false;

    private readonly ChatClient _chatClient;

    internal SQLAgentClient(SQLAgentOptions options, IDatabaseService databaseService, ILogger<SQLAgentClient> logger,
        IDatabaseConnectionManager databaseConnectionManager)
    {
        _options = options;
        DatabaseService = databaseService;
        _logger = logger;
        _databaseConnectionManager = databaseConnectionManager;

        _useVectorDatabaseIndex = options.UseVectorDatabaseIndex;

        _sqlResult = new SqlTool(this);

        var apiKey = new ApiKeyCredential(_options.APIKey); // 某些端点可能不需要
        var openAiClient = new OpenAIClient(apiKey, new OpenAIClientOptions
        {
            Endpoint = new Uri(_options.Endpoint) // 您的自定义端点
        });

        _chatClient = openAiClient.GetChatClient(_options.Model);
    }

    /// <summary>
    /// 生成Agent数据库信息
    /// </summary>
    public async Task GenerateAgentDatabaseInfoAsync()
    {
        var options = new ChatClientAgentOptions()
        {
            Instructions = PromptConstants.GlobalDatabaseSchemaAnalysisSystemPrompt,
            ChatOptions = new ChatOptions()
            {
                ToolMode = ChatToolMode.Auto,
                MaxOutputTokens = _options.MaxOutputTokens,
                Temperature = 0.7f,
                Tools = (List<AITool>)[]
            }
        };

        var generateAgentTool = new GenerateAgentTool();

        options.ChatOptions.Tools.Add(AIFunctionFactory.Create(generateAgentTool.WriteAgent,
            new AIFunctionFactoryOptions()
            {
                Name = "Write"
            }));

        var agent = _chatClient.CreateAIAgent(options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User,
            [
                new TextContent("""
                                Please analyze the complete database schema and generate a comprehensive knowledge base document optimized for AI SQL query generation.

                                Your mission is to create structured documentation that will help an AI agent:
                                1. Quickly locate relevant tables for user queries
                                2. Understand column purposes and data types
                                3. Generate correct JOIN statements with proper relationships
                                4. Select appropriate columns for data visualization
                                5. Apply database-specific SQL syntax correctly

                                Focus on actionable, structured information over narrative descriptions.

                                IMPORTANT: You MUST complete the task by calling the `Write` tool - DO NOT just output text directly.
                                """),
                new TextContent($"""
                                 <system-reminder>
                                 # Database Environment
                                 - **Database Type**: {_options.SqlType}
                                 - **Task**: Generate AI-consumable database knowledge base

                                 # Workflow Instructions (MANDATORY - Follow All Steps)
                                 1. **Analyze Schema**: Thoroughly analyze the database schema provided below.
                                 2. **Extract Metadata**: Extract key details for each table: columns, data types, constraints, and indexes.
                                 3. **Identify Patterns**: Identify common query patterns, JOIN relationships, and columns suitable for filtering, aggregation, and visualization.
                                 4. **Generate Documentation**: Generate the comprehensive and structured Markdown documentation following the prescribed format.
                                 5. **Write Output (REQUIRED)**: You MUST call the `Write` tool with the complete knowledge base content. This is a mandatory final step - do not skip it under any circumstances.

                                 CRITICAL: Your response is INCOMPLETE without calling the `Write` tool. Simply outputting text is NOT acceptable.

                                 # Critical Requirements
                                 - Include concrete SQL examples for common query scenarios
                                 - Highlight columns suitable for filtering, grouping, and aggregation
                                 - Document JOIN patterns between related tables
                                 - Mark visualization-friendly columns (avoid IDs, prefer descriptive fields)
                                 - Use database-specific syntax appropriate for {_options.SqlType.ToString()}

                                 The generated document will be embedded in AI prompts, so optimize for:
                                 - Fast retrieval through clear hierarchy
                                 - Structured formats (tables, lists) over paragraphs
                                 - SQL-ready column names and examples
                                 </system-reminder>
                                 """),
                new TextContent($"""
                                 <database-schema-data>
                                 # Complete Database Schema Information

                                 The following contains all table structures, columns, constraints, and relationships in the database.
                                 Analyze this information to generate the comprehensive knowledge base document.

                                 {await DatabaseService.GetAllTableNamesAsync()}
                                 </database-schema-data>
                                 """)
            ])
        };

        var thread = agent.GetNewThread();

        await foreach (var item in agent.RunStreamingAsync(messages, thread))
        {
            _logger.LogInformation(item.Text);
        }

        _logger.LogInformation("Agent database information generation completed.");

        // 更新当前链接的agent
        await _databaseConnectionManager.UpdateAgentAsync(_options.ConnectionId, generateAgentTool.AgentContent);
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

        var agent = await _databaseConnectionManager.GetConnectionAsync(_options.ConnectionId);

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, new List<AIContent>()
            {
                new TextContent($"""
                                 {input.Query}
                                 """),
                new TextContent($"""
                                 The following is comprehensive, pre-analyzed database schema information for your reference.
                                 This information has been validated and contains table structures, relationships, and usage patterns.
                                 ALWAYS prioritize information from this section before considering tool calls.

                                 <database-info>
                                 {agent.Agent}
                                 </database-info>
                                 """),
                new TextContent($"""
                                 <user-env>
                                 - Database Type: {_options.SqlType}
                                 - Write Permissions: {(_options.AllowWrite ? "ENABLED - You can perform INSERT, UPDATE, DELETE, CREATE, DROP operations (with confirmation)" : "DISABLED - Only SELECT queries are allowed")}
                                 {GetSqlPrompt(_options.SqlType)}

                                 # CRITICAL REQUIREMENTS

                                 ## 1. Column Parameter (MANDATORY)
                                 When calling sql-Write with executeType=Query or executeType=EChart, you MUST provide the 'columns' parameter listing all SELECT columns.

                                 ## 2. Parameterized Queries (MANDATORY)
                                 Always use parameterized queries with '@' prefixed parameter names (e.g., @year, @category, @userId).

                                 ## 3. EChart Query Optimization (CRITICAL FOR VISUALIZATION)
                                 When executeType=EChart, generate SQL that ONLY selects visualization-essential columns.

                                 ### MUST INCLUDE in EChart queries:
                                 - **Dimension columns** (1-2 for X-axis/grouping): category, date, region, status, product_name, month, year
                                 - **Measure columns** (1-3 for Y-axis/values): Aggregated values like COUNT(*), SUM(amount), AVG(price), MAX(quantity), MIN(cost)
                                 - **Use meaningful aliases**: SUM(amount) AS total_sales, COUNT(*) AS order_count, AVG(price) AS average_price

                                 ### MUST EXCLUDE from EChart queries (unless explicitly requested):
                                 - **ID columns**: id, user_id, order_id, product_id, customer_id, employee_id
                                 - **Foreign key columns**: Any column ending with _id
                                 - **Timestamps** (unless for time-series): created_at, updated_at, deleted_at
                                 - **Internal metadata**: version, hash, token, internal_notes
                                 - **Redundant columns**: Columns that don't contribute to visualization

                                 ### Column Count Guidelines:
                                 - **Minimum**: 2 columns (1 dimension + 1 measure)
                                 - **Optimal**: 2-3 columns (1 dimension + 1-2 measures)
                                 - **Maximum**: 5 columns (2 dimensions + 3 measures)

                                 ### GOOD EChart SQL Examples:
                                 ```sql
                                 -- Example 1: Category sales (1 dimension + 1 measure)
                                 SELECT category, SUM(amount) AS total_sales
                                 FROM orders
                                 GROUP BY category
                                 ORDER BY total_sales DESC

                                 -- Example 2: Monthly trend (1 time dimension + 1 measure)
                                 SELECT strftime('%Y-%m', order_date) AS month, COUNT(*) AS order_count
                                 FROM orders
                                 WHERE order_date >= date('now', '-12 months')
                                 GROUP BY month
                                 ORDER BY month

                                 -- Example 3: Multi-measure comparison (1 dimension + 2 measures)
                                 SELECT region, SUM(revenue) AS total_revenue, COUNT(DISTINCT customer_id) AS customer_count
                                 FROM sales
                                 GROUP BY region
                                 ORDER BY total_revenue DESC
                                 LIMIT 10
                                 ```

                                 ### BAD EChart SQL Examples (AVOID):
                                 ```sql
                                 -- BAD: Includes unnecessary ID columns
                                 SELECT id, user_id, category, SUM(amount) AS total
                                 FROM orders
                                 GROUP BY id, user_id, category

                                 -- BAD: Too many columns (not suitable for chart)
                                 SELECT id, name, email, phone, address, city, country, created_at
                                 FROM users

                                 -- BAD: No aggregation for visualization
                                 SELECT category, amount
                                 FROM orders
                                 ```

                                 ### Decision Rule:
                                 - IF user asks for "chart", "graph", "visualization", "trend", "distribution" → Use executeType=EChart + Apply strict column selection
                                 - IF user asks for "list", "details", "export", "all data" → Use executeType=Query + Select all relevant columns

                                 ENFORCEMENT: Before calling sql-Write with executeType=EChart, verify your SELECT clause contains ONLY visualization-essential columns (no IDs, no redundant fields).
                                 </user-env>
                                 """),
                new TextContent(PromptConstants.SQLGeneratorSystemRemindPrompt)
            })
        };

        _logger.LogInformation("Calling AI model to generate SQL for query: {Query}", input.Query);

        var agents = _chatClient.CreateAIAgent(new ChatClientAgentOptions()
        {
            Instructions = _options.SqlBotSystemPrompt,
            ChatOptions = new ChatOptions()
            {
                Tools = new List<AITool>()
                {
                    AIFunctionFactory.Create(_sqlResult.SearchTables, new AIFunctionFactoryOptions()
                    {
                        Name = "SearchTables",
                        SerializerOptions = JsonSerializerOptions.Web
                    }),
                    AIFunctionFactory.Create(_sqlResult.Write, new AIFunctionFactoryOptions()
                    {
                        Name = "Write",
                        SerializerOptions = JsonSerializerOptions.Web
                    })
                },
                ToolMode = ChatToolMode.Auto,
                MaxOutputTokens = _options.MaxOutputTokens
            },
        });

        var thread = agents.GetNewThread();

        await foreach (var item in agents.RunStreamingAsync(messages, thread))
        {
            if (item.RawRepresentation is ChatResponseUpdate
                {
                    RawRepresentation: StreamingChatCompletionUpdate completion
                } &&
                completion.ToolCallUpdates.Count > 0)
            {
                var tools = completion.ToolCallUpdates;

                var str = Encoding.UTF8.GetString(tools.FirstOrDefault()?.FunctionArgumentsUpdate);

                Console.Write(str);
            }

            if (!string.IsNullOrWhiteSpace(item.Text))
            {
                Console.Write(item.Text);
            }
        }

        _logger.LogInformation("AI model call completed, processing {Count} SQL results",
            _sqlResult.SqlBoxResult.Count);

        foreach (var sqlTool in _sqlResult.SqlBoxResult)
        {
            _logger.LogInformation("Processing SQL result: executeType={executeType}, SQL={Sql}", sqlTool.ExecuteType,
                sqlTool.Sql);

            // 如果是 EChart 类型，记录列信息以便诊断
            if (sqlTool.ExecuteType == SqlBoxExecuteType.EChart)
            {
                var columns = sqlTool.Columns ?? Array.Empty<string>();
                _logger.LogInformation(
                    "EChart SQL Columns (count={Count}): {Columns}",
                    columns.Length,
                    string.Join(", ", columns));

                // 检查是否包含 ID 列
                var idColumns = columns.Where(c => IsIdColumn(c)).ToList();
                if (idColumns.Any())
                {
                    _logger.LogWarning(
                        "WARNING: EChart SQL contains ID columns: {IdColumns}. This may result in poor visualization. Consider regenerating SQL without these columns.",
                        string.Join(", ", idColumns));
                }

                // 检查列数量
                if (columns.Length > 5)
                {
                    _logger.LogWarning(
                        "WARNING: EChart SQL has {Count} columns (recommended: 2-5). Chart may be overcrowded.",
                        columns.Length);
                }
                else if (columns.Length < 2)
                {
                    _logger.LogWarning(
                        "WARNING: EChart SQL has only {Count} column(s). Need at least 2 columns for visualization.",
                        columns.Length);
                }
            }

            switch (sqlTool.ExecuteType)
            {
                // 判断SQL是否是查询
                case SqlBoxExecuteType.EChart:
                {
                    var echartsTool = new EchartsTool();
                    var value = await ExecuteSqliteQueryAsync(sqlTool);

                    var echartMessages = new List<ChatMessage>();

                    bool? any = sqlTool.Parameters.Count != 0;

                    var userMessageText = $$"""
                                            Generate an ECharts option configuration for the following SQL query results.

                                            # User's Original Query
                                            "{{input.Query}}"

                                            # SQL Query Context
                                            ```sql
                                            {{sqlTool.Sql}}
                                            ```

                                            # Query Parameters
                                            {{(any == true
                                                ? string.Join("\n", sqlTool.Parameters.Select(p => $"- {p.Name}: {p.Value}"))
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

                    echartMessages.Add(new ChatMessage(ChatRole.User, new List<AIContent>()
                    {
                        new TextContent(userMessageText),
                        new TextContent(
                            """
                            <system-remind>
                            This is a reminder. Your job is merely to assist users in generating ECharts options. If the task has nothing to do with ECharts, please respond politely with a rejection.
                            - Always generate complete and valid ECharts option JSON.
                            - Please use {DATA_PLACEHOLDER_Y} and {DATA_PLACEHOLDER_X} as variables to insert data for the user.
                            - It is necessary to use `echarts-Write` to store the generated ECharts options.Using string insertion
                            </system-remind>
                            """)
                    }));

                    _logger.LogInformation("Generating ECharts option for SQL query");

                    agents = _chatClient.CreateAIAgent(new ChatClientAgentOptions()
                    {
                        Instructions = PromptConstants.SQLGeneratorEchartsDataPrompt,
                        ChatOptions = new ChatOptions()
                        {
                            Tools =
                            [
                                AIFunctionFactory.Create(echartsTool.Write, new AIFunctionFactoryOptions()
                                {
                                    Name = "Write",
                                    SerializerOptions = JsonSerializerOptions.Web
                                })
                            ],
                            ToolMode = ChatToolMode.Auto,
                            MaxOutputTokens = _options.MaxOutputTokens
                        },
                    });

                    var echartsThread = agents.GetNewThread();
                    var result = await agents.RunAsync(echartMessages, echartsThread);

                    // 获取生成的 ECharts option 并注入实际数据
                    if (!string.IsNullOrWhiteSpace(echartsTool.EchartsOption) && value is { Length: > 0 })
                    {
                        var processedOption = InjectDataIntoEchartsOption(echartsTool.EchartsOption, value);
                        echartsTool.EchartsOption = processedOption;

                        // 将 ECharts option 保存到结果对象中
                        sqlTool.EchartsOption = processedOption;

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
                    var value = await ExecuteSqliteQueryAsync(sqlTool);

                    sqlTool.Result = value;
                    break;
                }
                default:
                    await ExecuteSqliteNonQueryAsync(sqlTool);
                    break;
            }
        }

        _logger.LogInformation("SQL Agent execution completed, returning {Count} results",
            _sqlResult.SqlBoxResult.Count);

        return _sqlResult.SqlBoxResult;
    }

    private string GetSqlPrompt(SqlType type)
    {
        switch (type)
        {
            case SqlType.PostgreSql:
                return
                    "Note that the generated schema of PostgreSQL requires case sensitivity. Therefore, both fields and tables need to be enclosed in double quotes, including: \"table\"";
            case SqlType.MySql:
                return
                    "Note that in MySQL, identifiers are not case sensitive by default, but case sensitivity can depend on the underlying operating system. Use backticks (`) to enclose table and column names when they are reserved words or contain special characters.";
            case SqlType.Sqlite:
                return
                    "Note that in SQLite, identifiers are case insensitive by default. Use double quotes to preserve case or include special characters in table and column names.";
            case SqlType.Oracle:
                return
                    "Note that in Oracle, unquoted identifiers are converted to uppercase. Use double quotes to preserve the case of table and column names.";
            case SqlType.SqlServer:
                return
                    "Note that in SQL Server, case sensitivity depends on the collation settings. Use square brackets [] to enclose table and column names to avoid conflicts with reserved words.";
            default:
                return "";
        }
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
            var queryResult = await DatabaseService.ExecuteSqliteQueryAsync(result.Sql, result.Parameters);
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
            var affectedRows = await DatabaseService.ExecuteSqliteNonQueryAsync(result.Sql, result.Parameters);

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

                    // 智能识别维度列和度量列
                    var dimensionColumn = FindDimensionColumn(keys, firstItem);
                    var measureColumn = FindMeasureColumn(keys, firstItem, dimensionColumn);

                    _logger.LogInformation(
                        "Smart column detection: Dimension={Dimension}, Measure={Measure}, Total columns={Total}",
                        dimensionColumn ?? "none", measureColumn ?? "none", keys.Length);

                    if (dimensionColumn != null && measureColumn != null)
                    {
                        // 提取 X 轴数据（维度列）
                        var xAxisData = queryResults.Select(row =>
                        {
                            var dict = row as IDictionary<string, object>;
                            return dict?[dimensionColumn];
                        }).ToArray();

                        var xAxisJson = JsonSerializer.Serialize(xAxisData, new JsonSerializerOptions
                        {
                            WriteIndented = false
                        });

                        // 提取 Y 轴数据（度量列）
                        var yAxisData = queryResults.Select(row =>
                        {
                            var dict = row as IDictionary<string, object>;
                            return dict?[measureColumn];
                        }).ToArray();

                        var yAxisJson = JsonSerializer.Serialize(yAxisData, new JsonSerializerOptions
                        {
                            WriteIndented = false
                        });

                        result = result.Replace("{{DATA_PLACEHOLDER_X}}", xAxisJson);
                        result = result.Replace("{DATA_PLACEHOLDER_X}", xAxisJson);
                        result = result.Replace("{{DATA_PLACEHOLDER_Y}}", yAxisJson);
                        result = result.Replace("{DATA_PLACEHOLDER_Y}", yAxisJson);

                        _logger.LogInformation(
                            "Data injection completed: X-axis from '{DimensionColumn}', Y-axis from '{MeasureColumn}'",
                            dimensionColumn, measureColumn);
                    }
                    else
                    {
                        // 回退到原始逻辑（跳过 ID 列）
                        var nonIdKeys = keys.Where(k => !IsIdColumn(k)).ToArray();

                        if (nonIdKeys.Length >= 2)
                        {
                            var xAxisData = queryResults.Select(row =>
                            {
                                var dict = row as IDictionary<string, object>;
                                return dict?[nonIdKeys[0]];
                            }).ToArray();

                            var yAxisData = queryResults.Select(row =>
                            {
                                var dict = row as IDictionary<string, object>;
                                return dict?[nonIdKeys[1]];
                            }).ToArray();

                            var xAxisJson = JsonSerializer.Serialize(xAxisData, new JsonSerializerOptions
                            {
                                WriteIndented = false
                            });

                            var yAxisJson = JsonSerializer.Serialize(yAxisData, new JsonSerializerOptions
                            {
                                WriteIndented = false
                            });

                            result = result.Replace("{{DATA_PLACEHOLDER_X}}", xAxisJson);
                            result = result.Replace("{DATA_PLACEHOLDER_X}", xAxisJson);
                            result = result.Replace("{{DATA_PLACEHOLDER_Y}}", yAxisJson);
                            result = result.Replace("{DATA_PLACEHOLDER_Y}", yAxisJson);

                            _logger.LogInformation(
                                "Data injection completed (fallback): Using first two non-ID columns: '{Col1}', '{Col2}'",
                                nonIdKeys[0], nonIdKeys[1]);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Could not identify suitable columns for visualization. Total columns: {Count}, Non-ID columns: {NonIdCount}",
                                keys.Length, nonIdKeys.Length);
                        }
                    }
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

    /// <summary>
    /// 智能识别维度列（用于 X 轴）
    /// 优先选择非 ID 的文本列，排除描述性字段
    /// </summary>
    private string? FindDimensionColumn(string[] keys, IDictionary<string, object> sampleRow)
    {
        // 排除的描述性字段（不适合作为维度）
        var excludedDescriptiveFields = new[] { "description", "address", "notes", "comment", "remarks", "detail" };

        // 第一轮：查找最佳维度列（非 ID、非描述、短文本）
        foreach (var key in keys)
        {
            // 跳过 ID 列
            if (IsIdColumn(key))
                continue;

            // 跳过描述性字段
            if (excludedDescriptiveFields.Any(field =>
                key.IndexOf(field, StringComparison.OrdinalIgnoreCase) >= 0))
                continue;

            var value = sampleRow[key];

            // 优先选择短文本（类别、名称、类型等）
            if (value is string strValue && strValue.Length < 100)
                return key;
        }

        // 第二轮：查找任何非 ID 的文本列
        foreach (var key in keys)
        {
            if (IsIdColumn(key))
                continue;

            var value = sampleRow[key];
            if (value is string)
                return key;
        }

        // 第三轮：返回第一个非 ID 列
        return keys.FirstOrDefault(k => !IsIdColumn(k));
    }

    /// <summary>
    /// 智能识别度量列（用于 Y 轴）
    /// 优先选择数值类型的列，排除已选的维度列
    /// </summary>
    private string? FindMeasureColumn(string[] keys, IDictionary<string, object> sampleRow, string? excludeDimensionColumn)
    {
        // 第一轮：查找数值列（最理想的度量）
        foreach (var key in keys)
        {
            // 跳过 ID 列
            if (IsIdColumn(key))
                continue;

            // 跳过已选的维度列
            if (key == excludeDimensionColumn)
                continue;

            var value = sampleRow[key];

            // 查找数值类型（整数、浮点数）
            if (value is int || value is long || value is decimal || value is double || value is float)
                return key;
        }

        // 第二轮：如果没有数值列，查找非维度的其他列
        foreach (var key in keys)
        {
            if (IsIdColumn(key))
                continue;

            if (key == excludeDimensionColumn)
                continue;

            return key;
        }

        // 如果只有两列且都不是 ID，返回第二列
        var nonIdKeys = keys.Where(k => !IsIdColumn(k)).ToArray();
        return nonIdKeys.Length >= 2 ? nonIdKeys[1] : null;
    }

    /// <summary>
    /// 判断列名是否是 ID 类型
    /// </summary>
    private bool IsIdColumn(string columnName)
    {
        var lowerName = columnName.ToLower();

        // 精确匹配
        if (lowerName == "id" || lowerName == "uuid" || lowerName == "guid")
            return true;

        // 后缀匹配（如 user_id, order_id, customer_id）
        if (lowerName.EndsWith("_id") || lowerName.EndsWith("id"))
        {
            // 但排除 "valid", "solid" 等非 ID 的词
            if (lowerName.EndsWith("valid") || lowerName.EndsWith("solid") || lowerName.EndsWith("rapid"))
                return false;

            return true;
        }

        return false;
    }
}