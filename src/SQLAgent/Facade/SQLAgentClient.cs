using System.ClientModel;
using System.Diagnostics;
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
        options.ChatOptions.Tools.Add(AIFunctionFactory.Create(ThinkTool.Think, new AIFunctionFactoryOptions()
        {
            Name = "Think"
        }));

        var agent = _chatClient.CreateAIAgent(options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, new List<AIContent>()
            {
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
                                 - **Database Type**: {_options.SqlType.ToString()}
                                 - **Task**: Generate AI-consumable database knowledge base

                                 # Workflow Instructions (MANDATORY - Follow All Steps)
                                 1. **Analyze & Think**: First, thoroughly analyze the database schema. Use the `Think` tool to outline the structure of the knowledge base document you will generate. Plan the sections for each table, including metadata, query patterns, and relationships.
                                 2. **Extract Metadata**: Extract key details for each table: columns, data types, constraints, and indexes.
                                 3. **Identify Patterns**: Identify common query patterns, JOIN relationships, and columns suitable for filtering, aggregation, and visualization.
                                 4. **Generate Documentation**: Based on your plan, generate the comprehensive and structured Markdown documentation.
                                 5. **Write Output (REQUIRED)**: You MUST call the `Write` tool with the complete knowledge base content. This is a mandatory final step - do not skip it under any circumstances.
                                 
                                 ⚠️ CRITICAL: Your response is INCOMPLETE without calling the `Write` tool. Simply outputting text is NOT acceptable.

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
            })
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
                                 # User Query
                                 {input.Query}
                                 """),
                new TextContent($"""
                                 <database-info>
                                 The following is comprehensive, pre-analyzed database schema information for your reference.
                                 This information has been validated and contains table structures, relationships, and usage patterns.
                                 ALWAYS prioritize information from this section before considering tool calls.

                                 {agent.Agent}
                                 </database-info>
                                 """),
                new TextContent($"""
                                 <user-env>
                                 - Database Type: {_options.SqlType}
                                 - Write Permissions: {(_options.AllowWrite ? "ENABLED - You can perform INSERT, UPDATE, DELETE, CREATE, DROP operations (with confirmation)" : "DISABLED - Only SELECT queries are allowed")}
                                 - Vector Search: {(_useVectorDatabaseIndex ? "ENABLED" : "DISABLED")}

                                 CRITICAL REQUIREMENTS:
                                 1. When calling sql-Write with executeType=Query or executeType=EChart, you MUST provide the 'columns' parameter listing all SELECT columns
                                 2. Always use parameterized queries with '@' prefixed parameter names
                                 3. For EChart queries, follow DATA SELECTION RULES to exclude unnecessary ID fields
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
                        Name = "SearchTables"
                    }),
                    AIFunctionFactory.Create(_sqlResult.Write, new AIFunctionFactoryOptions()
                    {
                        Name = "Write"
                    })
                },
                ToolMode = new AutoChatToolMode(),
                MaxOutputTokens = _options.MaxOutputTokens,
                Temperature = 0.2f
            },
        });

        var thread = agents.GetNewThread();

        await agents.RunAsync(messages, thread);

        _logger.LogInformation("AI model call completed, processing {Count} SQL results",
            _sqlResult.SqlBoxResult.Count);

        foreach (var sqlTool in _sqlResult.SqlBoxResult)
        {
            _logger.LogInformation("Processing SQL result: executeType={executeType}, SQL={Sql}", sqlTool.ExecuteType,
                sqlTool.Sql);

            switch (sqlTool.ExecuteType)
            {
                // 判断SQL是否是查询
                case SqlBoxExecuteType.EChart:
                    {
                        var echartsTool = new EchartsTool();
                        var value = await ExecuteSqliteQueryAsync(sqlTool);

                        var echartMessages = new List<ChatMessage>();
                        var echartsHistory = new ChatHistory();
                        echartsHistory.AddSystemMessage(PromptConstants.SQLGeneratorEchartsDataPrompt);

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
                            - Use the `{DATA_PLACEHOLDER}` format for data injection points.
                            - It is necessary to use `echarts-Write` to store the generated ECharts options.
                            </system-remind>
                            """)
                    }));

                        _logger.LogInformation("Generating ECharts option for SQL query");

                        var echartsThread = agents.GetNewThread();
                        var result = await agents.RunAsync(messages, echartsThread);

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
}