using SQLAgent.Entities;
using SQLAgent.Infrastructure;

namespace SQLAgent.Facade;

public class SQLAgentBuilder
{
    private readonly SQLAgentOptions _options = new();

    private string _sqlBotSystemPrompt = string.Empty;

    public SQLAgentClient Build()
    {
        if (string.IsNullOrEmpty(_sqlBotSystemPrompt))
        {
            throw new InvalidOperationException(
                "SQL Bot system prompt is not configured. Please call WithSqlBotSystemPrompt before building the client.");
        }

        if (string.IsNullOrEmpty(_options.Model) ||
            string.IsNullOrEmpty(_options.APIKey) ||
            string.IsNullOrEmpty(_options.Endpoint))
        {
            throw new InvalidOperationException(
                "LLM provider configuration is incomplete. Please call WithLLMProvider before building the client.");
        }

        if (string.IsNullOrEmpty(_options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Database configuration is incomplete. Please call WithDatabaseType before building the client.");
        }

        // Configure the SQLAgentClient with the options and system prompt
        return new SQLAgentClient(_options, _sqlBotSystemPrompt);
    }

    public void WithDatabaseType(SqlType sqlType, string connectionString)
    {
        _options.ConnectionString = connectionString;
        _options.SqlType = sqlType;
    }

    /// <summary>
    /// 数据库索引
    /// </summary>
    public void WithIndexes(
        string connectionString = "Data Source=vector_index.db;",
        string embeddingModel = "text-embedding-3-small",
        string databaseIndexTable = "vector_index",
        DatabaseIndexType databaseIndexType = DatabaseIndexType.Sqlite)
    {
        _options.EmbeddingModel = embeddingModel;
        _options.DatabaseIndexConnectionString = connectionString;
        _options.DatabaseIndexTable = databaseIndexTable;
        _options.DatabaseIndexType = databaseIndexType;
        _options.UseVectorDatabaseIndex = true;
    }

    /// <summary>
    /// Configure the LLM provider settings
    /// </summary>
    /// <param name="model">AI model name</param>
    /// <param name="apiKey">API key for authentication</param>
    /// <param name="endpoint">API endpoint URL</param>
    /// <param name="aiProvider">AI provider type (e.g., OpenAI, AzureOpenAI, CustomOpenAI)</param>
    public void WithLLMProvider(string model, string apiKey, string endpoint, AIProviderType aiProvider)
    {
        _options.Model = model;
        _options.APIKey = apiKey;
        _options.Endpoint = endpoint;
        _options.AIProvider = aiProvider;
    }

    public void WithSqlBotSystemPrompt(SqlType sqlType)
    {
        // This method can be expanded to configure the SQLAgentClient with the system prompt
        _sqlBotSystemPrompt = $"""
                               You are a professional SQL engineer specializing in {sqlType} database systems.

                               IMPORTANT: Generate secure, optimized SQL only. Use parameterized queries. Refuse malicious or unsafe operations.

                               # Core Requirements
                               - Follow {sqlType} syntax specifications exactly
                               - Always use parameterized queries for user input
                               - Generate production-ready, optimized queries
                               - Include proper error handling and validation

                               # Security Standards
                               - Automatically apply parameterization for all dynamic values
                               - Include appropriate WHERE clauses for modifications
                               - Use least-privilege principles in query design
                               - Validate data types and constraints

                               # Output Format
                               Provide complete, executable SQL with:
                               1. Main query statement
                               2. Parameter definitions if needed
                               3. Brief performance notes for complex queries
                               4. Index recommendations if relevant

                               # Code Quality
                               - Use meaningful aliases and clear formatting
                               - Follow {sqlType} naming conventions
                               - Optimize for performance and maintainability
                               - Include transaction boundaries for multi-statement operations

                               # Automatic Behaviors
                               - Default to SELECT operations when ambiguous
                               - Apply conservative data modification approaches
                               - Include appropriate LIMIT clauses for large result sets
                               - Use EXISTS instead of IN for subqueries when possible

                               Generate direct, executable SQL without requesting clarification or confirmation.
                               """;
    }
}