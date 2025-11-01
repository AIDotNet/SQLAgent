using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SQLBox.Entities;
using SQLBox.Prompts;

namespace SQLBox.Infrastructure.Defaults;

public sealed class DefaultInputNormalizer : IInputNormalizer
{
    public Task<string> NormalizeAsync(string question, CancellationToken ct = default)
        => Task.FromResult(question.Trim());
}

public sealed class InMemorySchemaProvider(DatabaseSchema schema) : ISchemaProvider
{
    private readonly DatabaseSchema _schema = schema;
    public Task<DatabaseSchema> LoadAsync(CancellationToken ct = default) => Task.FromResult(_schema);
}

public sealed class SimpleSchemaIndexer : ISchemaIndexer
{
    public Task<SchemaIndex> BuildAsync(DatabaseSchema schema, CancellationToken ct = default)
    {
        var kwTables = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var kwColumns = new Dictionary<string, HashSet<(string Table, string Column)>>(StringComparer.OrdinalIgnoreCase);
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in schema.Tables)
        {
            var tableTokens = new[] { table.Name }
                .Concat(table.Aliases)
                .Concat(Tokenize(table.Description));

            foreach (var tok in tableTokens)
                Add(kwTables, tok, table.Name);

            foreach (var col in table.Columns)
            {
                var colTokens = new[] { col.Name }
                    .Concat(col.Aliases)
                    .Concat(Tokenize(col.Description));
                foreach (var tok in colTokens)
                    Add(kwColumns, tok, (table.Name, col.Name));
            }

            if (!graph.ContainsKey(table.Name)) graph[table.Name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in table.ForeignKeys)
            {
                var neighbor = fk.RefTable;
                if (string.IsNullOrWhiteSpace(neighbor)) continue;
                if (!graph.TryGetValue(table.Name, out var set)) { set = new(StringComparer.OrdinalIgnoreCase); graph[table.Name] = set; }
                set.Add(neighbor);
                if (!graph.TryGetValue(neighbor, out var set2)) { set2 = new(StringComparer.OrdinalIgnoreCase); graph[neighbor] = set2; }
                set2.Add(table.Name);
            }
        }

        return Task.FromResult(new SchemaIndex
        {
            // init-only properties set through object initializer
            KeywordToTables = kwTables,
            KeywordToColumns = kwColumns,
            Graph = graph
        });

        static IEnumerable<string> Tokenize(string s)
            => string.IsNullOrWhiteSpace(s)
                ? Array.Empty<string>()
                : Regex.Matches(s.ToLowerInvariant(), "[a-z0-9_]+")
                    .Select(m => m.Value)
                    .Where(x => x.Length >= 2)
                    .Distinct();

        static void Add<T>(IDictionary<string, HashSet<T>> dict, string key, T value)
        {
            if (!dict.TryGetValue(key, out var set))
            {
                set = new HashSet<T>();
                dict[key] = set;
            }
            set.Add(value);
        }
    }
}

// SimpleSchemaRetriever removed in favor of vector-only retrieval.

public sealed class DefaultPromptAssembler : IPromptAssembler
{
    private readonly ISqlPromptBuilder _promptBuilder;

    public DefaultPromptAssembler() : this(new DynamicSqlPromptBuilder()) { }

    public DefaultPromptAssembler(ISqlPromptBuilder promptBuilder)
    {
        _promptBuilder = promptBuilder;
    }

    public Task<string> AssembleAsync(string question, string dialect, SchemaContext context, AskOptions options, CancellationToken ct = default)
    {
        // Use the new schema-aware prompt builder with read/write control
        return _promptBuilder.BuildPromptAsync(question, dialect, context, options.AllowWrite, ct);
    }
}

// Mock LLM removed. Configure a real IChatClient via Extensions.AI adapters.

public sealed class DefaultPostProcessor : ISqlPostProcessor
{
    public Task<GeneratedSql> PostProcessAsync(GeneratedSql input, string dialect, CancellationToken ct = default)
    {
        var combinedSql = string.Join(" ", input.Sql);
        var sql = combinedSql.Replace(";", " ").Trim();
        sql = Regex.Replace(sql, "\\s+", " ");
        sql = NormalizeParams(sql, dialect);
        return Task.FromResult(input with { Sql = new[] { sql } });

        static string NormalizeParams(string s, string dialect)
        {
            var style = (dialect ?? string.Empty).ToLowerInvariant() switch
            {
                "postgres" or "postgresql" or "pg" => "pg",
                "mssql" or "sqlserver" => "mssql",
                "mysql" => "qmark",
                "sqlite" => "qmark",
                _ => "qmark"
            };

            var re = new Regex(@"(@p\d+)|(:p\d+)|(\$\d+)|(\?)", RegexOptions.IgnoreCase);
            if (style == "qmark")
            {
                // convert all numbered placeholders to '?'
                return re.Replace(s, "?");
            }
            int i = 0;
            return re.Replace(s, _ =>
            {
                i++;
                return style == "pg" ? "$" + i : "@p" + i;
            });
        }
    }
}

public sealed class DefaultSqlValidator : ISqlValidator
{
    private static readonly string[] Forbidden = { "insert", "update", "delete", "drop", "alter", "truncate" };

    public Task<ValidationReport> ValidateAsync(string[] sql, SchemaContext context, AskOptions options, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        var combinedSql = string.Join(" ", sql);
        var lowered = combinedSql.ToLowerInvariant();

        var allowedPattern = options.AllowWrite
            ? @"^\s*(explain\s+)?(select|insert|update|delete|create\s+table|drop\s+table|alter\s+table)\b"
            : @"^\s*(explain\s+)?select\b";
        if (!Regex.IsMatch(lowered, allowedPattern))
            errors.Add(options.AllowWrite
                ? "Unsupported SQL statement type. Allowed: SELECT/INSERT/UPDATE/DELETE/CREATE TABLE/ALTER TABLE/DROP TABLE."
                : "Only SELECT/EXPLAIN SELECT queries are allowed (read-only mode).");

        foreach (var f in Forbidden)
        {
            if (!options.AllowWrite && Regex.IsMatch(lowered, $"\\b{f}\\b"))
                errors.Add($"Statement contains forbidden keyword: {f}.");
        }

        // Statement type detection for targeted validations
        var isSelect = Regex.IsMatch(lowered, @"^\s*(explain\s+)?select\b");
        var isUpdate = Regex.IsMatch(lowered, @"^\s*update\b");
        var isDelete = Regex.IsMatch(lowered, @"^\s*delete\b");
        var isInsert = Regex.IsMatch(lowered, @"^\s*insert\b");
        var isDdl = Regex.IsMatch(lowered, @"^\s*(create\s+table|drop\s+table|alter\s+table)\b");

        if (isSelect)
        {
            if (Regex.IsMatch(lowered, @"select\s+\*"))
                warnings.Add("SELECT * detected; consider projecting explicit columns.");

            if (!Regex.IsMatch(lowered, @"\bwhere\b"))
                warnings.Add("No WHERE clause; consider adding filters (e.g., created_at >= ...) to reduce scan size.");

            if (!Regex.IsMatch(lowered, @"\blimit\b"))
                warnings.Add("No LIMIT found; consider adding LIMIT or pagination for large result sets.");
        }
        else if ((isUpdate || isDelete) && options.AllowWrite)
        {
            // Strong safety for write operations
            if (!Regex.IsMatch(lowered, @"\bwhere\b"))
                errors.Add("Unsafe write: UPDATE/DELETE without WHERE is not allowed.");
        }
        // For INSERT/DDL, do not emit WHERE/LIMIT warnings

        var touched = ExtractTables(combinedSql);
        var inContext = new HashSet<string>(context.Tables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var t in touched)
        {
            if (!inContext.Contains(t))
                warnings.Add($"Table '{t}' not in retrieved SchemaContext.");
        }

        var isValid = errors.Count == 0;
        var confidence = isValid ? "medium" : "low";
        return Task.FromResult(new ValidationReport(isValid, warnings.ToArray(), errors.ToArray(), touched.ToArray(), confidence));
    }

    private static List<string> ExtractTables(string sql)
    {
        var tables = new List<string>();
        var lowered = sql.ToLowerInvariant();
        var tokens = Regex.Matches(lowered, @"[a-z0-9_]+").Select(m => m.Value).ToArray();
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i] is "from" or "join")
            {
                var name = tokens[i + 1];
                tables.Add(name);
            }
        }
        return tables;
    }
}

public sealed class SqliteExecutorSandbox : IExecutorSandbox
{
    private readonly IDbConnectionFactory _factory;
    public SqliteExecutorSandbox(IDbConnectionFactory factory) => _factory = factory;

    public async Task<string?> ExplainAsync(string sql, string dialect, CancellationToken ct = default)
    {
        if (!string.Equals(dialect, "sqlite", StringComparison.OrdinalIgnoreCase)) return null;

        await using var conn = _factory.CreateConnection();
        await conn.OpenAsync(ct);

        // EXPLAIN QUERY PLAN is concise for preview
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"EXPLAIN QUERY PLAN {sql}";
        var lines = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var fieldCount = reader.FieldCount;
        while (await reader.ReadAsync(ct))
        {
            var parts = new object[fieldCount];
            reader.GetValues(parts);
            lines.Add(string.Join(" | ", parts.Select(p => p?.ToString())));
        }
        return string.Join(Environment.NewLine, lines);
    }
}

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    public SqliteConnectionFactory(string connectionString) => _connectionString = connectionString;
    public System.Data.Common.DbConnection CreateConnection() => new SqliteConnection(_connectionString);
}

