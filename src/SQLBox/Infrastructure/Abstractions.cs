using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SQLBox.Entities;

namespace SQLBox.Infrastructure;

public interface IInputNormalizer
{
    Task<string> NormalizeAsync(string question, CancellationToken ct = default);
}

public interface ISchemaProvider
{
    Task<DatabaseSchema> LoadAsync(CancellationToken ct = default);
}

public interface ISchemaIndexer
{
    Task<SchemaIndex> BuildAsync(DatabaseSchema schema, CancellationToken ct = default);
}

public interface ISchemaRetriever
{
    Task<SchemaContext> RetrieveAsync(string question, DatabaseSchema schema, SchemaIndex index, int topK, CancellationToken ct = default);
}

public interface IPromptAssembler
{
    Task<string> AssembleAsync(string question, string dialect, SchemaContext context, AskOptions options, CancellationToken ct = default);
}

public sealed record GeneratedSql(string[] Sql, IReadOnlyDictionary<string, object?> Parameters, string[] Tables);

public interface ILlmClient
{
    Task<GeneratedSql> GenerateAsync(string prompt, string dialect, SchemaContext schemaContext, CancellationToken ct = default);
}

public interface ISqlPostProcessor
{
    Task<GeneratedSql> PostProcessAsync(GeneratedSql input, string dialect, CancellationToken ct = default);
}

public interface ISqlValidator
{
    Task<ValidationReport> ValidateAsync(string[] sql, SchemaContext context, AskOptions options, CancellationToken ct = default);
}

public interface IAutoRepair
{
    Task<GeneratedSql?> TryRepairAsync(string question, string dialect, SchemaContext context, GeneratedSql lastAttempt, ValidationReport report, CancellationToken ct = default);
}

public interface IExecutorSandbox
{
    Task<string?> ExplainAsync(string sql, string dialect, CancellationToken ct = default);
}

public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
}

