using SQLBox.Entities;
using System.ClientModel;
using OpenAI.Chat;
using SQLBox.Prompts;
using System.Text.Json;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace SQLBox.Infrastructure;

/// <summary>
/// LLM 客户端工厂接口
/// </summary>
public interface ILlmClientFactory
{
    ILlmClient CreateClient(AIProvider provider, string model);
}

internal sealed class SqlGenPayload
{
    public string[] sql { get; set; } = Array.Empty<string>();

    public Dictionary<string, object?>? @params { get; set; }

    public string[]? tables { get; set; }
}

/// <summary>
/// 默认的 LLM 客户端工厂实现
/// </summary>
public class DefaultLlmClientFactory : ILlmClientFactory
{
    public ILlmClient CreateClient(AIProvider provider, string model)
    {
        return provider.Type switch
        {
            AIProviderType.OpenAI => CreateOpenAIClient(provider, model),
            AIProviderType.AzureOpenAI => CreateAzureOpenAIClient(provider, model),
            AIProviderType.CustomOpenAI => CreateCustomOpenAIClient(provider, model),
            _ => throw new NotSupportedException($"Provider type '{provider.Type}' is not supported")
        };
    }

    private ILlmClient CreateOpenAIClient(AIProvider provider, string model)
    {
        // 使用官方 OpenAI 端点
        var endpoint = provider.Endpoint ?? "https://api.openai.com/v1";
        return new OpenAILlmClient(endpoint, provider.ApiKey, model);
    }

    private ILlmClient CreateAzureOpenAIClient(AIProvider provider, string model)
    {
        if (string.IsNullOrEmpty(provider.Endpoint))
        {
            throw new InvalidOperationException("Azure OpenAI requires an endpoint");
        }

        return new AzureOpenAILlmClient(provider.Endpoint, provider.ApiKey, model);
    }

    private ILlmClient CreateCustomOpenAIClient(AIProvider provider, string model)
    {
        if (string.IsNullOrEmpty(provider.Endpoint))
        {
            throw new InvalidOperationException("Custom OpenAI requires an endpoint");
        }

        return new OpenAILlmClient(provider.Endpoint, provider.ApiKey, model);
    }
}

/// <summary>
/// OpenAI LLM 客户端（标准实现）
/// </summary>
public class OpenAILlmClient : ILlmClient
{
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAILlmClient(string endpoint, string apiKey, string model)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<GeneratedSql> GenerateAsync(string prompt, string dialect, SchemaContext context, CancellationToken ct = default)
    {
        // 使用 OpenAI 官方 .NET SDK 进行 SQL 生成，要求模型返回 { sql, params, tables } 的 JSON
        // 为了提升稳健性：先构造更严格的 JSON 指令；解析失败时进行正则回退
        var enhancedPrompt = $@"{prompt}

*** OUTPUT FORMAT REQUIREMENTS ***
1. Return ONLY valid JSON (no markdown, no code fences, no commentary)
2. The ""sql"" field MUST be an ARRAY
3. Put EACH SQL statement in a SEPARATE array element
4. Example: 3 INSERT statements = 3 array elements like [""INSERT..."", ""INSERT..."", ""INSERT...""]
5. NEVER combine statements in one string: [""INSERT... INSERT...""] is COMPLETELY WRONG
6. Use @paramName for all parameters";

        try
        {
            // 构建 OpenAI 客户端与 ChatClient（支持自定义兼容端点）
            var kernel = KernelFactory.CreateKernel(_model, _apiKey, _endpoint);
            var history = new ChatHistory();
            history.AddSystemMessage(BuildSystemPrompt(dialect));
            history.AddUserMessage(enhancedPrompt);

            var chat = kernel.GetRequiredService<IChatCompletionService>();

            var response = await chat.GetChatMessageContentAsync(history, new OpenAIPromptExecutionSettings()
            {
                MaxTokens = 3200,
            }, cancellationToken: ct);

            // 优先尝试将 JSON 反序列化为强类型对象，稳健获取 sql/params/tables
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var payload = JsonSerializer.Deserialize<SqlGenPayload>(response.Content ?? string.Empty, options);

                if (payload is not null)
                {
                    var sqlStr = payload.sql;

                    var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    if (payload.@params is not null)
                    {
                        foreach (var kv in payload.@params)
                        {
                            if (kv.Value is JsonElement je)
                            {
                                object? val = je.ValueKind switch
                                {
                                    JsonValueKind.String => je.GetString(),
                                    JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.TryGetDouble(out var d) ? d : je.ToString(),
                                    JsonValueKind.True => true,
                                    JsonValueKind.False => false,
                                    JsonValueKind.Null => null,
                                    _ => je.ToString()
                                };
                                parameters[kv.Key] = val;
                            }
                            else
                            {
                                parameters[kv.Key] = kv.Value;
                            }
                        }
                    }

                    var tables = payload.tables?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>();

                    return new GeneratedSql(sqlStr, parameters, tables);
                }

                throw new NotImplementedException();
            }
            catch
            {
                throw;
            }
        }
        catch
        {
            // 最终回退，避免中断调用链
            return new GeneratedSql(["SELECT 1 AS value"], new Dictionary<string, object?>(), Array.Empty<string>());
        }
    }

    private static string BuildSystemPrompt(string dialect)
    {
        return $$"""
*** CRITICAL OUTPUT FORMAT RULE ***
The "sql" field is an ARRAY. ONE array element = ONE complete SQL statement.
If you need 3 SQL statements, the array MUST have 3 separate elements.
NEVER put multiple SQL statements in one array element.

{{PromptConstants.SystemRoleDescription}}

{{PromptConstants.SchemaAnalysisInstructions}}

{{PromptConstants.SqlGenerationGuidelines}}

Target SQL dialect: {{dialect}}

=== OUTPUT FORMAT (JSON only, no markdown) ===
{
  "sql": ["array of complete SQL statements - ONE statement per element"],
  "params": { "param1": value1, "param2": value2 },
  "tables": ["table1", "table2"]
}

=== MANDATORY RULES ===

Rule #1: SQL ARRAY STRUCTURE
- ONE array element = ONE SQL statement
- If generating CREATE TABLE + 2 INSERT statements = 3 array elements
- Format: ["CREATE TABLE ...", "INSERT INTO ...", "INSERT INTO ..."]
- WRONG: ["CREATE TABLE ... INSERT INTO ..."]
- WRONG: ["INSERT INTO ...; INSERT INTO ..."]

Rule #2: NAMED PARAMETERS (@ prefix)
- Use @paramName in SQL (e.g., @name, @age, @id)
- In params object, keys have NO @ prefix
- Example SQL: "WHERE name = @name"
- Example params: { "name": "John" }

Rule #3: ALL DATABASE TYPES USE @ PREFIX
- SQLite: @paramName (NOT ?)
- PostgreSQL: @paramName (NOT $1, $2)
- MySQL: @paramName (NOT ?)
- SQL Server: @paramName

=== COMPLETE EXAMPLES ===

Example 1 - Single SELECT:
{
  "sql": [
    "SELECT id, name, age FROM users WHERE age > @minAge ORDER BY name LIMIT 10;"
  ],
  "params": { "minAge": 18 },
  "tables": ["users"]
}

Example 2 - Multiple INSERT statements (3 separate array elements):
{
  "sql": [
    "INSERT INTO students (name, age) VALUES (@name1, @age1);",
    "INSERT INTO students (name, age) VALUES (@name2, @age2);",
    "INSERT INTO students (name, age) VALUES (@name3, @age3);"
  ],
  "params": {
    "name1": "Alice", "age1": 20,
    "name2": "Bob", "age2": 22,
    "name3": "Carol", "age3": 21
  },
  "tables": ["students"]
}

Example 3 - CREATE + INSERT (2 separate array elements):
{
  "sql": [
    "CREATE TABLE IF NOT EXISTS products (id INTEGER PRIMARY KEY, name TEXT, price REAL);",
    "INSERT INTO products (name, price) VALUES (@productName, @productPrice);"
  ],
  "params": { "productName": "Widget", "productPrice": 29.99 },
  "tables": ["products"]
}

*** REMEMBER: Each SQL statement needs its own array slot. Think of the array like separate lines in a script file. ***
""";
    }
}

/// <summary>
/// Azure OpenAI LLM 客户端
/// </summary>
public class AzureOpenAILlmClient : ILlmClient
{
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;

    public AzureOpenAILlmClient(string endpoint, string apiKey, string model)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<GeneratedSql> GenerateAsync(string prompt, string dialect, SchemaContext context, CancellationToken ct = default)
    {
        // 使用 OpenAI 官方 .NET SDK 通过自定义 Azure 端点进行调用
        // 要求模型仅返回 { sql, params, tables } 的 JSON
        var enhancedPrompt = $@"{prompt}

*** OUTPUT FORMAT REQUIREMENTS ***
1. Return ONLY valid JSON (no markdown, no code fences, no commentary)
2. The ""sql"" field MUST be an ARRAY
3. Put EACH SQL statement in a SEPARATE array element
4. Example: 3 INSERT statements = 3 array elements like [""INSERT..."", ""INSERT..."", ""INSERT...""]
5. NEVER combine statements in one string: [""INSERT... INSERT...""] is COMPLETELY WRONG
6. Use @paramName for all parameters";

        try
        {
            if (string.IsNullOrWhiteSpace(_endpoint))
                throw new InvalidOperationException("Azure OpenAI requires an endpoint");

            var opts = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(_endpoint) };
            var client = new OpenAI.OpenAIClient(new ApiKeyCredential(_apiKey), opts);
            var chat = client.GetChatClient(_model);

            var messages = new List<ChatMessage>()
            {
                ChatMessage.CreateSystemMessage(BuildSystemPrompt(dialect)),
                ChatMessage.CreateUserMessage(enhancedPrompt)
            };
            var response = await chat.CompleteChatAsync(messages, new ChatCompletionOptions()
            {
                MaxOutputTokenCount = 32000,
            }, cancellationToken: ct);
            var content = response?.Value?.Content?[0]?.Text ?? string.Empty;

            // 优先尝试强类型反序列化，失败则进入下方 JSON 解析回退
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var payload = JsonSerializer.Deserialize<SqlGenPayload>(content, options);
                if (payload is not null)
                {
                    var sqlStr = payload.sql;

                    var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    if (payload.@params is not null)
                    {
                        foreach (var kv in payload.@params)
                        {
                            if (kv.Value is JsonElement je)
                            {
                                object? val = je.ValueKind switch
                                {
                                    JsonValueKind.String => je.GetString(),
                                    JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.TryGetDouble(out var d) ? d : je.ToString(),
                                    JsonValueKind.True => true,
                                    JsonValueKind.False => false,
                                    JsonValueKind.Null => null,
                                    _ => je.ToString()
                                };
                                parameters[kv.Key] = val;
                            }
                            else
                            {
                                parameters[kv.Key] = kv.Value;
                            }
                        }
                    }

                    var tables = payload.tables?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>();

                    return new GeneratedSql(sqlStr, parameters, tables);
                }
            }
            catch
            {
                // ignore typed JSON parse and continue to generic parsing
            }

            // JSON 解析
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(content);
                var root = doc.RootElement;

                var sql = root.TryGetProperty("sql", out var pSql) ? (pSql.GetString() ?? string.Empty) : string.Empty;

                var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("params", out var pParams) && pParams.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var kv in pParams.EnumerateObject())
                    {
                        parameters[kv.Name] = kv.Value.ValueKind switch
                        {
                            System.Text.Json.JsonValueKind.String => kv.Value.GetString(),
                            System.Text.Json.JsonValueKind.Number => kv.Value.ToString(),
                            System.Text.Json.JsonValueKind.True => true,
                            System.Text.Json.JsonValueKind.False => false,
                            _ => kv.Value.ToString()
                        };
                    }
                }
                var tables = Array.Empty<string>();
                if (root.TryGetProperty("tables", out var pTabs) && pTabs.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var v in pTabs.EnumerateArray())
                        list.Add(v.GetString() ?? string.Empty);
                    tables = list.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                }


                return new GeneratedSql(new[] { sql }, parameters, tables);
            }
            catch
            {
                // 忽略 JSON 解析失败，走回退
            }

            // 回退：从文本中提取第一条 SELECT
            var m = System.Text.RegularExpressions.Regex.Match(content, @"(?is)\bselect\b[\s\S]+");
            var sqlFallback = m.Success ? m.Value.Trim() : "SELECT 1 AS value";
            return new GeneratedSql(new[] { sqlFallback }, new Dictionary<string, object?>(), Array.Empty<string>());
        }
        catch
        {
            // 最终回退
            return new GeneratedSql(new[] { "SELECT 1 AS value" }, new Dictionary<string, object?>(), Array.Empty<string>());
        }
    }

    private static string BuildSystemPrompt(string dialect)
    {
        return $@"*** CRITICAL OUTPUT FORMAT RULE ***
The ""sql"" field is an ARRAY. ONE array element = ONE complete SQL statement.
If you need 3 SQL statements, the array MUST have 3 separate elements.
NEVER put multiple SQL statements in one array element.

{PromptConstants.SystemRoleDescription}

{PromptConstants.SchemaAnalysisInstructions}

{PromptConstants.SqlGenerationGuidelines}

{PromptConstants.SecurityRestrictions}

Target SQL dialect: {dialect}

=== OUTPUT FORMAT (JSON only, no markdown) ===
{{
  ""sql"": [""array of complete SQL statements - ONE statement per element""],
  ""params"": {{ ""param1"": value1, ""param2"": value2 }},
  ""tables"": [""table1"", ""table2""]
}}

=== MANDATORY RULES ===

Rule #1: SQL ARRAY STRUCTURE
- ONE array element = ONE SQL statement
- If generating CREATE TABLE + 2 INSERT statements = 3 array elements
- Format: [""CREATE TABLE ..."", ""INSERT INTO ..."", ""INSERT INTO ...""]
- WRONG: [""CREATE TABLE ... INSERT INTO ...""]
- WRONG: [""INSERT INTO ...; INSERT INTO ...""]

Rule #2: NAMED PARAMETERS (@ prefix)
- Use @paramName in SQL (e.g., @name, @age, @id)
- In params object, keys have NO @ prefix
- Example SQL: ""WHERE name = @name""
- Example params: {{ ""name"": ""John"" }}

Rule #3: ALL DATABASE TYPES USE @ PREFIX
- SQLite: @paramName (NOT ?)
- PostgreSQL: @paramName (NOT $1, $2)
- MySQL: @paramName (NOT ?)
- SQL Server: @paramName

=== COMPLETE EXAMPLES ===

Example 1 - Single SELECT:
{{
  ""sql"": [
    ""SELECT id, name, age FROM users WHERE age > @minAge ORDER BY name LIMIT 10;""
  ],
  ""params"": {{ ""minAge"": 18 }},
  ""tables"": [""users""]
}}

Example 2 - Multiple INSERT statements (3 separate array elements):
{{
  ""sql"": [
    ""INSERT INTO students (name, age) VALUES (@name1, @age1);"",
    ""INSERT INTO students (name, age) VALUES (@name2, @age2);"",
    ""INSERT INTO students (name, age) VALUES (@name3, @age3);""
  ],
  ""params"": {{
    ""name1"": ""Alice"", ""age1"": 20,
    ""name2"": ""Bob"", ""age2"": 22,
    ""name3"": ""Carol"", ""age3"": 21
  }},
  ""tables"": [""students""]
}}

Example 3 - CREATE + INSERT (2 separate array elements):
{{
  ""sql"": [
    ""CREATE TABLE IF NOT EXISTS products (id INTEGER PRIMARY KEY, name TEXT, price REAL);"",
    ""INSERT INTO products (name, price) VALUES (@productName, @productPrice);""
  ],
  ""params"": {{ ""productName"": ""Widget"", ""productPrice"": 29.99 }},
  ""tables"": [""products""]
}}

*** REMEMBER: Each SQL statement needs its own array slot. Think of the array like separate lines in a script file. ***";
    }

}
