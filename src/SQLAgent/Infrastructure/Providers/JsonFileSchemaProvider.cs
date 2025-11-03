using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SQLAgent.Entities;

namespace SQLAgent.Infrastructure.Providers;

public static class SchemaJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string Serialize(DatabaseSchema schema) => JsonSerializer.Serialize(schema, Options);
    public static DatabaseSchema Deserialize(string json) => JsonSerializer.Deserialize<DatabaseSchema>(json, Options)!;
}

public sealed class JsonFileSchemaProvider : ISchemaProvider
{
    private readonly string _path;
    public JsonFileSchemaProvider(string path) => _path = path;

    public async Task<DatabaseSchema> LoadAsync(CancellationToken ct = default)
    {
        using var stream = File.OpenRead(_path);
        var schema = await JsonSerializer.DeserializeAsync<DatabaseSchema>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }, ct);
        return schema ?? new DatabaseSchema();
    }

    public async Task SaveAsync(DatabaseSchema schema, CancellationToken ct = default)
    {
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, schema, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }, ct);
    }
}

