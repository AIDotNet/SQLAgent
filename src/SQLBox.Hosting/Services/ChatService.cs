using Microsoft.AspNetCore.Mvc;
using SQLBox.Entities;
using SQLBox.Facade;
using SQLBox.Hosting.Dto;
using SQLBox.Infrastructure;
using SQLBox.Infrastructure.Defaults;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenAI;
using System.ClientModel;
using OpenAI.Chat;
using SQLBox.Model;
using Microsoft.Data.Sqlite;
using Dapper;

namespace SQLBox.Hosting.Services;

public class ChatService(
    IDatabaseConnectionManager connectionManager,
    IAIProviderManager providerManager,
    ILlmClientFactory llmClientFactory,
    SystemSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        // 中文不转义
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// SSE流式对话接口
    /// </summary>
    [HttpPost("completion")]
    public async Task CompletionAsync(HttpContext context, CompletionInput input)
    {
        var sw = Stopwatch.StartNew();

        // 设置SSE响应头
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            // 验证连接
            var connection = await connectionManager.GetConnectionAsync(input.ConnectionId);
            if (connection == null)
            {
                await SendErrorAsync(context, "CONNECTION_NOT_FOUND",
                    $"Connection '{input.ConnectionId}' not found");
                return;
            }

            if (!connection.IsEnabled)
            {
                await SendErrorAsync(context, "CONNECTION_DISABLED",
                    $"Connection '{connection.Name}' is disabled");
                return;
            }

            // 验证 AI 提供商
            if (string.IsNullOrEmpty(input.ProviderId) || string.IsNullOrEmpty(input.Model))
            {
                await SendErrorAsync(context, "PROVIDER_NOT_SPECIFIED",
                    "AI provider and model must be specified");
                return;
            }

            var provider = await providerManager.GetAsync(input.ProviderId);
            if (provider == null)
            {
                await SendErrorAsync(context, "PROVIDER_NOT_FOUND",
                    $"AI Provider '{input.ProviderId}' not found");
                return;
            }

            if (!provider.IsEnabled)
            {
                await SendErrorAsync(context, "PROVIDER_DISABLED",
                    $"AI Provider '{provider.Name}' is disabled");
                return;
            }

            // 创建 LLM 客户端（用于生成SQL）
            var llmClient = llmClientFactory.CreateClient(provider, input.Model);

            // 准备索引所需的嵌入与向量存储（强制使用 Sqlite-Vec）
            var embeddingProviderId = settings.EmbeddingProviderId ?? input.ProviderId;
            var embeddingProvider = string.IsNullOrWhiteSpace(embeddingProviderId)
                ? provider
                : await providerManager.GetAsync(embeddingProviderId);

            if (embeddingProvider == null)
            {
                await SendErrorAsync(context, "EMBED_PROVIDER_NOT_FOUND",
                    $"Embedding provider '{embeddingProviderId}' not found");
                return;
            }

            var embedder = new OpenAIEmbedder(
                embeddingProvider.ApiKey,
                settings.EmbeddingModel,
                embeddingProvider.Endpoint);

            var vecConfig = new VectorStoreConfig
            {
                ConnectionString = settings.VectorDbPath,
                CollectionName = settings.VectorCollection,
                DistanceMetric = settings.DistanceMetric,
                AutoCreateCollection = settings.AutoCreateCollection,
                CacheExpiration = settings.VectorCacheExpireMinutes.HasValue
                    ? TimeSpan.FromMinutes(settings.VectorCacheExpireMinutes.Value)
                    : null
            };

            var vecStore = new SqliteVecTableStore(embedder, vecConfig);

            // 配置 SqlGen：必须使用向量索引器 + 向量检索器 + Sqlite-Vec 存储
            SqlGen.Configure(builder =>
            {
                builder.WithLlmClient(llmClient);
                builder.WithConnectionManager(connectionManager);
                builder.WithSchemaIndexer(new EmbeddingSchemaIndexer(embedder));
                builder.WithTableVectorStore(vecStore);
                builder.WithSchemaRetriever(new VectorSchemaRetriever(vecStore));
            });

            var hasIndex = await SqlGen.HasTableVectorIndexAsync(input.ConnectionId);
            if (hasIndex)
            {
                var count = await SqlGen.GetTableVectorIndexCountAsync(input.ConnectionId);
                await SendDeltaAsync(context, $"已检测到现有向量索引（{count} 条记录）。\n\n");
            }

            // 使用 OpenAI 官方 SDK 的流式Function Calling与用户交互
            // AI会根据对话内容决定何时调用generate_sql函数
            try
            {
                OpenAIClient oaClient;
                if (!string.IsNullOrWhiteSpace(provider.Endpoint))
                {
                    var opts = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(provider.Endpoint) };
                    oaClient = new OpenAIClient(new ApiKeyCredential(provider.ApiKey), opts);
                }
                else
                {
                    oaClient = new OpenAIClient(apiKey: provider.ApiKey);
                }

                var chatClient = oaClient.GetChatClient(input.Model);

                // 定义SqlGen.AskAsync作为function calling工具
                // 只需要question参数，其他参数（connectionId, dialect, execute等）从外部上下文获取
                var generateSqlTool = ChatTool.CreateFunctionTool(
                    functionName: "generate_sql",
                    functionDescription:
                    "Generate SQL query from natural language question based on database schema. Use this when user asks questions about data or wants to query the database.",
                    functionParameters: BinaryData.FromString("""
                                                              {
                                                                  "type": "object",
                                                                  "properties": {
                                                                      "question": {
                                                                          "type": "string",
                                                                          "description": "The natural language question to convert to SQL query"
                                                                      }
                                                                  },
                                                                  "required": ["question"]
                                                              }
                                                              """)
                );

                // 构建完整的对话历史
                var messages = new List<ChatMessage>
                {
                    ChatMessage.CreateSystemMessage(
                        """
                        *** ROLE DEFINITION ***
                        You are a database assistant that helps users interact with their database through the generate_sql function.
                        You are a FUNCTION CALLING AGENT - your primary job is to call the generate_sql function, not to write SQL yourself.

                        *** CORE RULE: WHEN TO CALL generate_sql ***
                        You MUST call the generate_sql function whenever the user's request involves:
                        1. Querying data (SELECT): "show me users", "how many orders", "find customers"
                        2. Analyzing data: "what's the average", "top 10 products", "count by category"
                        3. Creating tables (CREATE TABLE): "create a table for products"
                        4. Inserting data (INSERT): "add a new user", "insert records"
                        5. Updating data (UPDATE): "change the price", "update user email"
                        6. Deleting data (DELETE): "remove old records", "delete user"
                        7. Modifying schema (ALTER, DROP): "add a column", "drop table"
                        8. ANY database operation or data-related question

                        *** MANDATORY WORKFLOW ***
                        Step 1: Understand the user's intent (considering conversation history)
                        Step 2: IMMEDIATELY call generate_sql function with the user's question
                        Step 3: Wait for the SQL generation result
                        Step 4: Explain the generated SQL and results to the user in a clear way

                        *** WHAT YOU MUST NOT DO ***
                        - NEVER write SQL code yourself in your response
                        - NEVER try to answer data questions without calling generate_sql
                        - NEVER say "you can use this SQL" without actually calling the function
                        - DO NOT skip calling the function even if the question seems simple

                        *** RESPONSE STYLE ***
                        - Be concise and helpful
                        - Always call the function for database operations
                        - After getting results, explain them clearly
                        - If there are warnings, bring them to user's attention
                        - Use conversation history to understand context and follow-up questions

                        *** EXAMPLES OF CORRECT BEHAVIOR ***

                        User: "Show me all users"
                        You: I'll query the database for all users.
                        [Call generate_sql with question: "Show me all users"]
                        [After getting results]: Here are all the users from the database...

                        User: "只显示前10个"
                        You: I'll modify the query to show only the first 10 users.
                        [Call generate_sql with question: "Show me the first 10 users"]
                        [After getting results]: Here are the first 10 users...

                        *** REMEMBER ***
                        Your superpower is the generate_sql function. Use it for ALL database-related tasks.
                        Pay attention to conversation history to understand context and follow-up questions.
                        """)
                };

                // 添加前端发送的对话历史
                foreach (var msg in input.Messages)
                {
                    switch (msg.Role.ToLower())
                    {
                        case "user":
                            messages.Add(ChatMessage.CreateUserMessage(msg.Content));
                            break;
                        case "assistant":
                            messages.Add(ChatMessage.CreateAssistantMessage(msg.Content));
                            break;
                        case "system":
                            // 跳过前端的system消息，我们已经有自己的system prompt
                            break;
                    }
                }

                var chatOptions = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 32000,
                    Tools = { generateSqlTool },
                    ToolChoice = ChatToolChoice.CreateAutoChoice()
                };

                bool continueConversation = true;
                while (continueConversation)
                {
                    var streamingResponse = chatClient.CompleteChatStreamingAsync(messages, chatOptions);

                    var currentContent = new StringBuilder();
                    var functionCallId = string.Empty;
                    var functionName = string.Empty;
                    var functionArgs = new StringBuilder();
                    var hasFunctionCall = false;

                    await foreach (var update in streamingResponse)
                    {
                        // 流式输出文本内容（使用 delta 消息）
                        foreach (var contentPart in update.ContentUpdate)
                        {
                            currentContent.Append(contentPart.Text);
                            if (!string.IsNullOrEmpty(contentPart.Text))
                            {
                                await SendDeltaAsync(context, contentPart.Text);
                            }
                        }

                        // 收集工具调用（流式累积）
                        foreach (var toolCallUpdate in update.ToolCallUpdates)
                        {
                            if (toolCallUpdate.Kind == ChatToolCallKind.Function)
                            {
                                hasFunctionCall = true;
                                if (!string.IsNullOrEmpty(toolCallUpdate.ToolCallId))
                                {
                                    functionCallId = toolCallUpdate.ToolCallId;
                                }

                                if (!string.IsNullOrEmpty(toolCallUpdate.FunctionName))
                                {
                                    functionName = toolCallUpdate.FunctionName;
                                }

                                functionArgs.Append(toolCallUpdate.FunctionArgumentsUpdate);
                            }
                        }
                    }

                    // 处理工具调用
                    if (hasFunctionCall && functionName == "generate_sql")
                    {
                        var toolCall = ChatToolCall.CreateFunctionToolCall(
                            functionCallId,
                            functionName,
                            BinaryData.FromString(functionArgs.ToString())
                        );

                        messages.Add(ChatMessage.CreateAssistantMessage([toolCall]));

                        await SendDeltaAsync(context, "\n\n");

                        try
                        {
                            using var argsDoc = System.Text.Json.JsonDocument.Parse(functionArgs.ToString());
                            var args = argsDoc.RootElement;

                            // 从function参数中获取question
                            var question = args.GetProperty("question").GetString() ?? 
                                          input.Messages.LastOrDefault(m => m.Role.ToLower() == "user")?.Content ?? 
                                          "未知查询";

                            var sqlBoxBuilder = new SqlBoxBuilder();
                            sqlBoxBuilder.WithDatabaseType(SqlType.Sqlite, connection.ConnectionString);
                            sqlBoxBuilder.WithLLMProvider(input.Model, provider.ApiKey, provider.Endpoint ?? "", "OpenAI");
                            sqlBoxBuilder.WithSqlBotSystemPrompt(SqlType.Sqlite);

                            var sqlBot = sqlBoxBuilder.Build();

                            var result = await sqlBot.ExecuteAsync(new ExecuteInput()
                            {
                                ConnectionId = connection.Id,
                                Query = question
                            });

                            // 发送 SQL 块
                            await SendSqlBlockAsync(context, [result.Sql], []);

                            // 如果有 ECharts 配置，发送图表块
                            if (!string.IsNullOrEmpty(result.EchartsOption))
                            {
                                var chartBlock = new Dto.ChartBlock
                                {
                                    ChartType = "echarts",
                                    EchartsOption = result.EchartsOption
                                };
                                await SendBlockAsync(context, chartBlock);
                            }

                            messages.Add(ChatMessage.CreateToolMessage(functionCallId,
                                "Carry outCompletion of execution"));
                        }
                        catch (Exception ex)
                        {
                            var errorResult = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                success = false,
                                error = ex.Message
                            }, JsonOptions);
                            messages.Add(ChatMessage.CreateToolMessage(functionCallId, errorResult));
                            await SendDeltaAsync(context, $"\n生成SQL时出错: {ex.Message}\n");
                        }
                    }
                    else
                    {
                        // 没有工具调用，对话结束
                        continueConversation = false;
                    }
                }
            }
            catch (Exception ex)
            {
                await SendDeltaAsync(context, $"\n对话处理出错: {ex.Message}\n");
            }

            sw.Stop();

            // 发送完成消息
            await SendDoneAsync(context, new DoneMessage
            {
                ElapsedMs = sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            await SendErrorAsync(context, "EXECUTION_ERROR", ex.Message, ex.ToString());
        }
    }

    /// <summary>
    /// 发送增量文本（流式输出）
    /// </summary>
    private static async Task SendDeltaAsync(HttpContext context, string delta)
    {
        var message = new Dto.DeltaMessage { Delta = delta };
        await SendMessageAsync(context, message);
    }

    /// <summary>
    /// 发送内容块
    /// </summary>
    private static async Task SendBlockAsync(HttpContext context, Dto.ContentBlock block)
    {
        var message = new Dto.BlockMessage { Block = block };
        await SendMessageAsync(context, message);
    }

    /// <summary>
    /// 发送 SQL 块
    /// </summary>
    private static async Task SendSqlBlockAsync(HttpContext context, string[] sqls, string[] tables,
        string? dialect = null)
    {
        foreach (var sql in sqls)
        {
            var block = new Dto.SqlBlock
            {
                Sql = sql,
                Tables = tables,
                Dialect = dialect
            };
            await SendBlockAsync(context, block);
        }
    }

    /// <summary>
    /// 发送数据块
    /// </summary>
    private static async Task SendDataBlockAsync(HttpContext context, string[] columns, object[][] rows, int totalRows)
    {
        var block = new Dto.DataBlock
        {
            Columns = columns,
            Rows = rows,
            TotalRows = totalRows
        };
        await SendBlockAsync(context, block);
    }

    /// <summary>
    /// 发送错误消息
    /// </summary>
    private static async Task SendErrorAsync(HttpContext context, string code, string message, string? details = null)
    {
        var errorMessage = new Dto.ErrorMessage
        {
            Code = code,
            Message = message,
            Details = details
        };
        await SendMessageAsync(context, errorMessage);
    }

    /// <summary>
    /// 发送完成消息
    /// </summary>
    private static async Task SendDoneAsync(HttpContext context, Dto.DoneMessage message)
    {
        await SendMessageAsync(context, message);
    }

    private static async Task SendMessageAsync(HttpContext context, Dto.SSEMessage message)
    {
        var json = JsonSerializer.Serialize(message, message.GetType(), JsonOptions);
        var data = $"data: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(data);

        await context.Response.Body.WriteAsync(bytes);
        await context.Response.Body.FlushAsync();
    }
}