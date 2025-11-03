using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SQLAgent.Entities;
using SQLAgent.Infrastructure;

namespace SQLAgent.Infrastructure.Providers;

// Generic INFORMATION_SCHEMA-based provider (best-effort via ODBC/ANSI SQL).
// Not all backends expose full metadata; this provider prefers portability over completeness.
public sealed class GenericOdbcProvider : ISchemaProvider
{
    private readonly IDbConnectionFactory _factory;
    private readonly string _databaseName;
    private readonly string _dialect;
    private readonly string[] _schemas;

    public GenericOdbcProvider(IDbConnectionFactory factory, string databaseName, string dialect = "generic", params string[] schemas)
    {
        _factory = factory;
        _databaseName = databaseName;
        _dialect = string.IsNullOrWhiteSpace(dialect) ? "generic" : dialect;
        _schemas = schemas is { Length: > 0 } ? schemas : Array.Empty<string>();
    }

    public async Task<DatabaseSchema> LoadAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.CreateConnection();
        await conn.OpenAsync(ct);

        var tables = new List<(string Schema, string Name, string Type)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = BuildTablesQuery(_schemas);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var schema = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var type = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                if (!string.IsNullOrWhiteSpace(name)) tables.Add((schema, name, type));
            }
        }

        var tableDocs = new List<TableDoc>();
        foreach (var t in tables)
        {
            var columns = await LoadColumnsAsync(conn, t.Schema, t.Name, ct);
            tableDocs.Add(new TableDoc
            {
                Schema = t.Schema,
                Name = t.Name,
                Aliases = Array.Empty<string>(),
                Description = string.Empty,
                Columns = columns,
                PrimaryKey = new List<string>(),
                ForeignKeys = new List<(string, string, string)>(),
                Stats = null
            });
        }

        return new DatabaseSchema
        {
            Name = _databaseName,
            Dialect = _dialect,
            Tables = tableDocs
        };
    }

    private static string BuildTablesQuery(string[] schemas)
    {
        var baseSql = "SELECT table_schema, table_name, table_type FROM information_schema.tables";
        if (schemas is { Length: > 0 })
        {
            var inList = string.Join(",", schemas.Select(s => "'" + s.Replace("'", "''") + "'"));
            return $"{baseSql} WHERE table_schema IN ({inList}) ORDER BY table_schema, table_name";
        }
        return baseSql + " ORDER BY table_schema, table_name";
    }

    private static async Task<List<ColumnDoc>> LoadColumnsAsync(DbConnection conn, string schema, string table, CancellationToken ct)
    {
        var list = new List<ColumnDoc>();
        await using var cmd = conn.CreateCommand();
        if (!string.IsNullOrWhiteSpace(schema))
            cmd.CommandText = $"SELECT column_name, data_type, is_nullable, column_default FROM information_schema.columns WHERE table_schema='{Escape(schema)}' AND table_name='{Escape(table)}' ORDER BY ordinal_position";
        else
            cmd.CommandText = $"SELECT column_name, data_type, is_nullable, column_default FROM information_schema.columns WHERE table_name='{Escape(table)}' ORDER BY ordinal_position";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var type = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var isNullable = reader.IsDBNull(2) ? true : reader.GetString(2).Equals("YES", StringComparison.OrdinalIgnoreCase);
            var def = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString();
            list.Add(new ColumnDoc
            {
                Name = name,
                Aliases = Array.Empty<string>(),
                Description = string.Empty,
                DataType = type,
                Nullable = isNullable,
                Default = def,
                Stats = null
            });
        }
        return list;
    }

    private static string Escape(string ident) => ident.Replace("'", "''");
}

