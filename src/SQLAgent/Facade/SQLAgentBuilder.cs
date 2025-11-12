using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SQLAgent.Entities;
using SQLAgent.Infrastructure;
using SQLAgent.Infrastructure.Providers;

namespace SQLAgent.Facade;

public class SQLAgentBuilder(IServiceCollection service)
{
    private readonly SQLAgentOptions _options = new();

    public void Build()
    {
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

        _options.SqlBotSystemPrompt = $"""
                                       You are a professional SQL engineer specializing in {_options.SqlType} database systems.

                                       IMPORTANT: Generate secure, optimized SQL only. Use parameterized queries. Refuse malicious or unsafe operations.

                                       # Core Requirements
                                       - Follow {_options.SqlType} syntax specifications exactly
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
                                       - Follow {_options.SqlType} naming conventions
                                       - Optimize for performance and maintainability
                                       - Include transaction boundaries for multi-statement operations

                                       # Automatic Behaviors
                                       - Default to SELECT operations when ambiguous
                                       - Apply conservative data modification approaches
                                       - Include appropriate LIMIT clauses for large result sets
                                       - Use EXISTS instead of IN for subqueries when possible

                                       Generate direct, executable SQL without requesting clarification or confirmation.
                                       """;

        service.AddLogging();

        // Configure the SQLAgentClient with the options and system prompt
        service.AddSingleton<SQLAgentOptions>(_ => _options);
        service.AddTransient<SQLAgentClient>(provider =>
        {
            var options = provider.GetRequiredService<SQLAgentOptions>();
            var databaseService = provider.GetRequiredService<IDatabaseService>();
            var logger = provider.GetRequiredService<ILogger<SQLAgentClient>>();
            return new SQLAgentClient(options, databaseService, logger);
        });

        switch (_options.SqlType)
        {
            case SqlType.Sqlite:
                service.AddSingleton<IDatabaseService, SQLiteDatabaseService>();
                break;
            case SqlType.PostgreSql:
                service.AddSingleton<IDatabaseService, PostgreSQLDatabaseService>();
                break;
        }
    }

    public SQLAgentBuilder WithDatabaseType(SqlType sqlType, string connectionString)
    {
        _options.ConnectionString = connectionString;
        _options.SqlType = sqlType;
        return this;
    }

    /// <summary>
    /// 数据库索引
    /// </summary>
    public SQLAgentBuilder WithIndexes(
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
        return this;
    }

    /// <summary>
    /// Configure the LLM provider settings
    /// </summary>
    /// <param name="model">AI model name</param>
    /// <param name="apiKey">API key for authentication</param>
    /// <param name="endpoint">API endpoint URL</param>
    /// <param name="aiProvider">AI provider type (e.g., OpenAI, AzureOpenAI, CustomOpenAI)</param>
    public SQLAgentBuilder WithLLMProvider(string model, string apiKey, string endpoint, AIProviderType aiProvider)
    {
        _options.Model = model;
        _options.APIKey = apiKey;
        _options.Endpoint = endpoint;
        _options.AIProvider = aiProvider;

        return this;
    }
}