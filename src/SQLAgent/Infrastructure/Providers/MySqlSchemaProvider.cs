using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SQLAgent.Entities;

namespace SQLAgent.Infrastructure.Providers;

public sealed class MySqlSchemaProvider : ISchemaProvider
{
    private readonly IDbConnectionFactory _factory;
    private readonly string _database;

    public MySqlSchemaProvider(IDbConnectionFactory factory, string database)
    {
        _factory = factory;
        _database = database;
    }

    public async Task<DatabaseSchema> LoadAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.CreateConnection();
        await conn.OpenAsync(ct);

        var tables = new List<(string Schema, string Name, string Type)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT table_schema, table_name, table_type FROM information_schema.tables WHERE table_schema='{Escape(_database)}' ORDER BY table_name";
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
            Name = _database,
            Dialect = "mysql",
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
        cmd.CommandText = $@"SELECT kcu.column_name, kcu.referenced_table_name, kcu.referenced_column_name
FROM information_schema.key_column_usage kcu
WHERE kcu.table_schema = '{Escape(schema)}' AND kcu.table_name = '{Escape(table)}' AND kcu.referenced_table_name IS NOT NULL";
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

