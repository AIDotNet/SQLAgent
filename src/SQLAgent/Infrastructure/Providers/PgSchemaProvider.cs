using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SQLAgent.Entities;

namespace SQLAgent.Infrastructure.Providers;

public sealed class PgSchemaProvider : ISchemaProvider
{
    private readonly IDbConnectionFactory _factory;
    private readonly string[] _schemas;

    public PgSchemaProvider(IDbConnectionFactory factory, params string[] schemas)
    {
        _factory = factory;
        _schemas = schemas is { Length: > 0 } ? schemas : new[] { "public" };
    }

    public async Task<DatabaseSchema> LoadAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.CreateConnection();
        await conn.OpenAsync(ct);

        var tables = new List<(string Schema, string Name, string Type)>();
        foreach (var s in _schemas)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT table_schema, table_name, table_type FROM information_schema.tables WHERE table_schema = '{Escape(s)}' ORDER BY table_name";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tables.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var tableDocs = new List<TableDoc>();

        foreach (var t in tables)
        {
            var columns = await LoadColumnsAsync(conn, t.Schema, t.Name, ct);
            var pks = await LoadPrimaryKeyAsync(conn, t.Schema, t.Name, ct);
            var fks = await LoadForeignKeysAsync(conn, t.Schema, t.Name, ct);

            tableDocs.Add(new TableDoc
            {
                Schema = t.Schema,
                Name = t.Name,
                Aliases = Array.Empty<string>(),
                Description = string.Empty,
                Columns = columns,
                PrimaryKey = pks,
                ForeignKeys = fks,
                Stats = null
            });
        }

        return new DatabaseSchema
        {
            Name = "postgres",
            Dialect = "postgres",
            Tables = tableDocs
        };
    }

    private static async Task<List<ColumnDoc>> LoadColumnsAsync(DbConnection conn, string schema, string table, CancellationToken ct)
    {
        var list = new List<ColumnDoc>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT column_name, data_type, is_nullable, column_default FROM information_schema.columns WHERE table_schema='{Escape(schema)}' AND table_name='{Escape(table)}' ORDER BY ordinal_position";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            var type = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var isNullable = reader.IsDBNull(2) ? true : (reader.GetString(2).Equals("YES", StringComparison.OrdinalIgnoreCase));
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

    private static async Task<List<string>> LoadPrimaryKeyAsync(DbConnection conn, string schema, string table, CancellationToken ct)
    {
        var pks = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT kcu.column_name
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage kcu ON kcu.constraint_name = tc.constraint_name AND kcu.table_schema = tc.table_schema AND kcu.table_name = tc.table_name
WHERE tc.table_schema = '{Escape(schema)}' AND tc.table_name = '{Escape(table)}' AND tc.constraint_type = 'PRIMARY KEY'
ORDER BY kcu.ordinal_position";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) pks.Add(reader.GetString(0));
        return pks;
    }

    private static async Task<List<(string Column, string RefTable, string RefColumn)>> LoadForeignKeysAsync(DbConnection conn, string schema, string table, CancellationToken ct)
    {
        var list = new List<(string, string, string)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT kcu.column_name, ccu.table_name AS foreign_table_name, ccu.column_name AS foreign_column_name
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage kcu ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = tc.constraint_name AND ccu.table_schema = tc.table_schema
WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = '{Escape(schema)}' AND tc.table_name = '{Escape(table)}'";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var col = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var rtab = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var rcol = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            if (!string.IsNullOrWhiteSpace(col) && !string.IsNullOrWhiteSpace(rtab))
                list.Add((col, rtab, rcol));
        }
        return list;
    }

    private static string Escape(string ident) => ident.Replace("'", "''");
}

