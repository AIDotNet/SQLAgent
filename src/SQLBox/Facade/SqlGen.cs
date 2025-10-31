using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SQLBox.Entities;
using SQLBox.Infrastructure;
using SQLBox.Infrastructure.Defaults;

namespace SQLBox.Facade;

public static class SqlGen
{
    private static readonly object InitLock = new();
    private static SqlGenEngine? _engine;

    public static void Configure(Action<SqlGenBuilder> configure)
    {
        var b = new SqlGenBuilder();
        configure(b);
        lock (InitLock)
        {
            _engine = b.Build();
        }
    }

    private static SqlGenEngine EnsureDefault()
    {
        lock (InitLock)
        {
            if (_engine != null) return _engine;
            var builder = new SqlGenBuilder();
            // Default empty schema + default components
            builder.WithSchemaProvider(new InMemorySchemaProvider(new DatabaseSchema { Name = "default", Dialect = "sqlite", Tables = new List<TableDoc>() }));
            builder.WithCache(new InMemorySemanticCache());
            // Default in-memory connection manager so callers can register connections and use ConnectionId
            builder.WithConnectionManager(new InMemoryDatabaseConnectionManager());
            _engine = builder.Build();
            return _engine!;
        }
    }

    public static async Task<SqlResult> AskAsync(string question, AskOptions? options = null, CancellationToken ct = default)
    {
        var engine = EnsureDefault();
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options), "AskOptions is required. Please provide ConnectionId.");
        }
        if (string.IsNullOrWhiteSpace(options.ConnectionId))
        {
            throw new ArgumentException("ConnectionId is required.", nameof(options));
        }
        if (engine.ConnectionManager == null)
        {
            throw new InvalidOperationException("No IDatabaseConnectionManager configured. Please configure a connection manager before calling AskAsync.");
        }

        // 1) 解析连接并据此确定方言
        var dbConn = await engine.ConnectionManager.GetConnectionAsync(options.ConnectionId, ct)
                    ?? throw new InvalidOperationException($"Connection '{options.ConnectionId}' not found.");

        var normalized = await engine.InputNormalizer.NormalizeAsync(question, ct);
        var schema = await engine.SchemaProvider.LoadAsync(ct);
        var dialect = options.Dialect ?? dbConn.DatabaseType ?? schema.Dialect;

        var index = await engine.SchemaIndexer.BuildAsync(schema, ct);
        var context = await engine.SchemaRetriever.RetrieveAsync(normalized, schema, index, options.TopK, ct);

        // 2) 语义缓存（加入 ConnectionId，避免跨连接误命中）
        var cacheKey = ComputeCacheKey(normalized, $"{dialect}|{dbConn.Id}", context);
        if (engine.Cache != null && engine.Cache.TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        var prompt = await engine.PromptAssembler.AssembleAsync(normalized, dialect, context, ct);
        var gen = await engine.LlmClient.GenerateAsync(prompt, dialect, context, ct);
        gen = await engine.PostProcessor.PostProcessAsync(gen, dialect, ct);

        var validation = await engine.Validator.ValidateAsync(gen.Sql, context, options, ct);

        // Optional simple repair loop (single attempt)
        if (!validation.IsValid && engine.Repair != null)
        {
            var repaired = await engine.Repair.TryRepairAsync(normalized, dialect, context, gen, validation, ct);
            if (repaired != null)
            {
                gen = repaired;
                validation = await engine.Validator.ValidateAsync(gen.Sql, context, options, ct);
            }
        }

        var warnings = validation.Warnings.ToList();
        if (!validation.IsValid)
        {
            warnings.AddRange(validation.Errors.Select(e => $"error: {e}"));
        }

        // 3) 可选执行预览：基于连接对象选择执行引擎
        string? preview = null;
        if (options.Execute && validation.IsValid)
        {
            try
            {
                var d = (dialect ?? string.Empty).ToLowerInvariant();
                if (d == "sqlite")
                {
                    // 针对 SQLite，使用内置的轻量执行沙箱
                    var sandbox = new SqliteExecutorSandbox(new SqliteConnectionFactory(dbConn.ConnectionString));
                    preview = await sandbox.ExplainAsync(gen.Sql, d, ct);
                }
                else if (engine.ExecutorSandbox != null)
                {
                    // 其它数据库类型：回退到全局配置的沙箱（若其已预先配置了相应工厂）
                    preview = await engine.ExecutorSandbox.ExplainAsync(gen.Sql, d, ct);
                }
                else
                {
                    warnings.Add($"execution disabled: no executor sandbox configured for database type '{dbConn.DatabaseType}'");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"execution: {ex.Message}");
            }
        }

        var result = new SqlResult(
            Sql: gen.Sql,
            Parameters: gen.Parameters,
            Dialect: dialect,
            TouchedTables: validation.TouchedTables,
            Explanation: options.ReturnExplanation ? BuildExplanation(normalized, context, gen, validation) : string.Empty,
            Confidence: validation.Confidence,
            Warnings: warnings.ToArray(),
            ExecutionPreview: preview
        );

        engine.Cache?.Set(cacheKey, result, TimeSpan.FromMinutes(10));
        return result;
    }

    private static string BuildExplanation(string question, SchemaContext ctx, GeneratedSql gen, ValidationReport report)
    {
        var tables = ctx.Tables.Select(t => t.Name).ToArray();
        return $"Question: {question}\nUsed tables: {string.Join(", ", tables)}\nConfidence: {report.Confidence}";
    }

    private static string ComputeCacheKey(string question, string dialect, SchemaContext ctx)
    {
        var tables = ctx.Tables.Select(t => t.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        var key = $"{dialect}\n{question}\n{string.Join(",", tables)}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash);
    }
}

public sealed class SqlGenBuilder
{
    internal IInputNormalizer InputNormalizer { get; private set; } = new DefaultInputNormalizer();
    internal ISchemaProvider SchemaProvider { get; private set; } = new InMemorySchemaProvider(new DatabaseSchema());
    internal ISchemaIndexer SchemaIndexer { get; private set; }
    internal ISchemaRetriever SchemaRetriever { get; private set; }
    internal IPromptAssembler PromptAssembler { get; private set; } = new DefaultPromptAssembler();
    internal ILlmClient LlmClient { get; private set; }
    internal ISqlPostProcessor PostProcessor { get; private set; } = new DefaultPostProcessor();
    internal ISqlValidator Validator { get; private set; } = new DefaultSqlValidator();
    internal IAutoRepair? Repair { get; private set; }
    internal IExecutorSandbox? ExecutorSandbox { get; private set; }
    internal ISemanticCache? Cache { get; private set; }
    internal IDatabaseConnectionManager? ConnectionManager { get; private set; }

    public SqlGenBuilder WithInputNormalizer(IInputNormalizer x) { InputNormalizer = x; return this; }
    public SqlGenBuilder WithSchemaProvider(ISchemaProvider x) { SchemaProvider = x; return this; }
    public SqlGenBuilder WithSchemaIndexer(ISchemaIndexer x) { SchemaIndexer = x; return this; }
    public SqlGenBuilder WithSchemaRetriever(ISchemaRetriever x) { SchemaRetriever = x; return this; }
    public SqlGenBuilder WithPromptAssembler(IPromptAssembler x) { PromptAssembler = x; return this; }
    public SqlGenBuilder WithLlmClient(ILlmClient x) { LlmClient = x; return this; }
    public SqlGenBuilder WithPostProcessor(ISqlPostProcessor x) { PostProcessor = x; return this; }
    public SqlGenBuilder WithValidator(ISqlValidator x) { Validator = x; return this; }
    public SqlGenBuilder WithRepair(IAutoRepair? x) { Repair = x; return this; }
    public SqlGenBuilder WithExecutor(IExecutorSandbox? x) { ExecutorSandbox = x; return this; }
    public SqlGenBuilder WithCache(ISemanticCache? x) { Cache = x; return this; }
    public SqlGenBuilder WithConnectionManager(IDatabaseConnectionManager x) { ConnectionManager = x; return this; }

    public SqlGenEngine Build() => new(
        InputNormalizer,
        SchemaProvider,
        SchemaIndexer,
        SchemaRetriever,
        PromptAssembler,
        LlmClient,
        PostProcessor,
        Validator,
        Repair,
        ConnectionManager,
        ExecutorSandbox,
        Cache
    );
}

public sealed class SqlGenEngine
{
    public IInputNormalizer InputNormalizer { get; }
    public ISchemaProvider SchemaProvider { get; }
    public ISchemaIndexer SchemaIndexer { get; }
    public ISchemaRetriever SchemaRetriever { get; }
    public IPromptAssembler PromptAssembler { get; }
    public ILlmClient LlmClient { get; }
    public ISqlPostProcessor PostProcessor { get; }
    public ISqlValidator Validator { get; }
    public IAutoRepair? Repair { get; }
    public IDatabaseConnectionManager? ConnectionManager { get; }
    public IExecutorSandbox? ExecutorSandbox { get; }
    public ISemanticCache? Cache { get; }

    public SqlGenEngine(
        IInputNormalizer inputNormalizer,
        ISchemaProvider schemaProvider,
        ISchemaIndexer schemaIndexer,
        ISchemaRetriever schemaRetriever,
        IPromptAssembler promptAssembler,
        ILlmClient llmClient,
        ISqlPostProcessor postProcessor,
        ISqlValidator validator,
        IAutoRepair? repair,
        IDatabaseConnectionManager? connectionManager,
        IExecutorSandbox? executorSandbox,
        ISemanticCache? cache)
    {
        InputNormalizer = inputNormalizer;
        SchemaProvider = schemaProvider;
        SchemaIndexer = schemaIndexer;
        SchemaRetriever = schemaRetriever;
        PromptAssembler = promptAssembler;
        LlmClient = llmClient;
        PostProcessor = postProcessor;
        Validator = validator;
        Repair = repair;
        ConnectionManager = connectionManager;
        ExecutorSandbox = executorSandbox;
        Cache = cache;
    }
}
