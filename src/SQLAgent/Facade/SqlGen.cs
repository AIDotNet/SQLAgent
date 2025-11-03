using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SQLAgent.Entities;
using SQLAgent.Infrastructure;
using SQLAgent.Infrastructure.Defaults;

namespace SQLAgent.Facade;

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

    // 初始化或更新指定连接的表向量索引
    // forceRebuild = true 将清空该连接下旧向量并全量重建；false 则仅增量更新过期或缺失的表向量
    public static async Task<int> BuildOrUpdateTableVectorIndexAsync(
        string connectionId,
        bool forceRebuild = false,
        Dictionary<string, string>? metaData = null,
        CancellationToken ct = default)
    {
        var engine = EnsureDefault();
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("connectionId is required.", nameof(connectionId));
        if (engine.SchemaProvider == null)
            throw new InvalidOperationException("No ISchemaProvider configured.");
        if (engine.TableVectorStore == null)
            throw new InvalidOperationException("No ITableVectorStore configured. Please configure a table vector store via SqlGenBuilder.WithTableVectorStore.");

        var schema = await engine.SchemaProvider.LoadAsync(ct);
        metaData ??= new Dictionary<string, string>();

        // 仅处理与目标 connectionId 匹配的表；未标注 ConnectionId 的表默认纳入
        var tables = schema.Tables
            .Where(t => string.IsNullOrWhiteSpace(t.ConnectionId) || string.Equals(t.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (forceRebuild)
        {
            await engine.TableVectorStore.DeleteConnectionVectorsAsync(connectionId, ct);
            await engine.TableVectorStore.SaveTableVectorsBatchAsync(connectionId, tables, metaData, ct);
            return tables.Count;
        }

        var toUpdate = new List<TableDoc>();
        foreach (var t in tables)
        {
            var upToDate = await engine.TableVectorStore.IsTableVectorUpToDateAsync(connectionId, t, ct);
            if (!upToDate) toUpdate.Add(t);
        }

        if (toUpdate.Count > 0)
        {
            await engine.TableVectorStore.SaveTableVectorsBatchAsync(connectionId, toUpdate, metaData, ct);
        }
        return toUpdate.Count;
    }

    // 仅用于首次初始化：删除旧索引并全量重建
    public static Task<int> InitializeTableVectorIndexAsync(
        string connectionId,
        CancellationToken ct = default)
        => BuildOrUpdateTableVectorIndexAsync(connectionId, forceRebuild: true, metaData: null, ct: ct);

    // 增量更新：仅处理缺失或过期的表向量
    public static Task<int> UpdateTableVectorIndexAsync(
        string connectionId,
        CancellationToken ct = default)
        => BuildOrUpdateTableVectorIndexAsync(connectionId, forceRebuild: false, metaData: null, ct: ct);

    // 检查指定连接是否已有表向量索引
    public static async Task<bool> HasTableVectorIndexAsync(string connectionId, CancellationToken ct = default)
    {
        var engine = EnsureDefault();
        if (engine.TableVectorStore == null)
            throw new InvalidOperationException("No ITableVectorStore configured.");
        return await engine.TableVectorStore.HasConnectionVectorsAsync(connectionId, ct);
    }

    // 获取指定连接下的表向量数量
    public static async Task<int> GetTableVectorIndexCountAsync(string connectionId, CancellationToken ct = default)
    {
        var engine = EnsureDefault();
        if (engine.TableVectorStore == null)
            throw new InvalidOperationException("No ITableVectorStore configured.");
        return await engine.TableVectorStore.CountConnectionVectorsAsync(connectionId, ct);
    }

    /// <summary>
    /// 直接在目标数据库上执行SQL查询并返回结果
    /// </summary>
    private static async Task<string> ExecuteSqlDirectlyAsync(
        DatabaseConnection dbConn,
        GeneratedSql sql,
        string? dialect,
        CancellationToken ct)
    {
        var dbType = (dialect ?? dbConn.DatabaseType ?? string.Empty).ToLowerInvariant();

        // 根据数据库类型选择合适的执行方式
        if (dbType == "sqlite")
        {
            return await ExecuteSqliteAsync(dbConn.ConnectionString, sql, ct);
        }
        // 其他数据库类型可以在这里扩展
        else
        {
            throw new NotSupportedException($"Direct SQL execution is not yet supported for database type: {dbType}");
        }
    }

    /// <summary>
    /// 执行SQLite查询并返回格式化的结果
    /// </summary>
    private static async Task<string> ExecuteSqliteAsync(string connectionString, GeneratedSql sql, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = string.Join(';', sql.Sql);

        // 动态插入参数到命令中
        if (sql.Parameters != null && sql.Parameters.Count > 0)
        {
            foreach (var (key, value) in sql.Parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = key.StartsWith("@") ? key : $"@{key}";
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }

        var result = new StringBuilder();

        // 判断是否是查询语句
        var trimmedSql = string.Join(';', sql.Sql);
        var isQuery = trimmedSql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                      trimmedSql.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase) ||
                      trimmedSql.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase);

        if (isQuery)
        {
            await using var reader = await command.ExecuteReaderAsync(ct);

            // 获取列信息
            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            result.AppendLine($"Columns: {string.Join(", ", columns)}");
            result.AppendLine();

            // 读取数据行
            int rowCount = 0;
            const int maxRows = 100; // 限制最多返回100行

            while (await reader.ReadAsync(ct) && rowCount < maxRows)
            {
                var values = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "NULL";
                    values.Add(value);
                }
                result.AppendLine($"Row {rowCount + 1}: {string.Join(" | ", values)}");
                rowCount++;
            }

            if (rowCount == 0)
            {
                result.AppendLine("No rows returned.");
            }
            else
            {
                result.AppendLine();
                result.AppendLine($"Total rows returned: {rowCount}");
                if (rowCount == maxRows)
                {
                    result.AppendLine($"(Limited to first {maxRows} rows)");
                }
            }
        }
        else
        {
            // 对于非查询语句（INSERT, UPDATE, DELETE等）
            var affectedRows = await command.ExecuteNonQueryAsync(ct);
            result.AppendLine($"Command executed successfully.");
            result.AppendLine($"Rows affected: {affectedRows}");
        }

        return result.ToString();
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
    internal ISqlPostProcessor PostProcessor { get; private set; } = new DefaultPostProcessor();
    internal ISqlValidator Validator { get; private set; } = new DefaultSqlValidator();
    internal IAutoRepair? Repair { get; private set; }
    internal IExecutorSandbox? ExecutorSandbox { get; private set; }
    internal ISemanticCache? Cache { get; private set; }
    internal IDatabaseConnectionManager? ConnectionManager { get; private set; }
    internal ITableVectorStore? TableVectorStore { get; private set; }

    public SqlGenBuilder WithInputNormalizer(IInputNormalizer x) { InputNormalizer = x; return this; }
    public SqlGenBuilder WithSchemaProvider(ISchemaProvider x) { SchemaProvider = x; return this; }
    public SqlGenBuilder WithSchemaIndexer(ISchemaIndexer x) { SchemaIndexer = x; return this; }
    public SqlGenBuilder WithSchemaRetriever(ISchemaRetriever x) { SchemaRetriever = x; return this; }
    public SqlGenBuilder WithPromptAssembler(IPromptAssembler x) { PromptAssembler = x; return this; }
    public SqlGenBuilder WithPostProcessor(ISqlPostProcessor x) { PostProcessor = x; return this; }
    public SqlGenBuilder WithValidator(ISqlValidator x) { Validator = x; return this; }
    public SqlGenBuilder WithRepair(IAutoRepair? x) { Repair = x; return this; }
    public SqlGenBuilder WithExecutor(IExecutorSandbox? x) { ExecutorSandbox = x; return this; }
    public SqlGenBuilder WithCache(ISemanticCache? x) { Cache = x; return this; }
    public SqlGenBuilder WithConnectionManager(IDatabaseConnectionManager x) { ConnectionManager = x; return this; }
    public SqlGenBuilder WithTableVectorStore(ITableVectorStore x) { TableVectorStore = x; return this; }

    public SqlGenEngine Build() => new(
        InputNormalizer,
        SchemaProvider,
        SchemaIndexer,
        SchemaRetriever,
        PromptAssembler,
        PostProcessor,
        Validator,
        Repair,
        ConnectionManager,
        ExecutorSandbox,
        TableVectorStore,
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
    public ISqlPostProcessor PostProcessor { get; }
    public ISqlValidator Validator { get; }
    public IAutoRepair? Repair { get; }
    public IDatabaseConnectionManager? ConnectionManager { get; }
    public IExecutorSandbox? ExecutorSandbox { get; }
    public ITableVectorStore? TableVectorStore { get; }
    public ISemanticCache? Cache { get; }

    public SqlGenEngine(
        IInputNormalizer inputNormalizer,
        ISchemaProvider schemaProvider,
        ISchemaIndexer schemaIndexer,
        ISchemaRetriever schemaRetriever,
        IPromptAssembler promptAssembler,
        ISqlPostProcessor postProcessor,
        ISqlValidator validator,
        IAutoRepair? repair,
        IDatabaseConnectionManager? connectionManager,
        IExecutorSandbox? executorSandbox,
        ITableVectorStore? tableVectorStore,
        ISemanticCache? cache)
    {
        InputNormalizer = inputNormalizer;
        SchemaProvider = schemaProvider;
        SchemaIndexer = schemaIndexer;
        SchemaRetriever = schemaRetriever;
        PromptAssembler = promptAssembler;
        PostProcessor = postProcessor;
        Validator = validator;
        Repair = repair;
        ConnectionManager = connectionManager;
        ExecutorSandbox = executorSandbox;
        TableVectorStore = tableVectorStore;
        Cache = cache;
    }
}
