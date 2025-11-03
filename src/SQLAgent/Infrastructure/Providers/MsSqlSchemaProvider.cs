using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SQLAgent.Entities;

namespace SQLAgent.Infrastructure.Providers;

public sealed class MsSqlSchemaProvider : ISchemaProvider
{
    private readonly IDbConnectionFactory _factory;
    private readonly string _database;

    public MsSqlSchemaProvider(IDbConnectionFactory factory, string database)
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
            cmd.CommandText = "SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_NAME";
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
            Dialect = "mssql",
            Tables = tableDocs
        };
    }

    private static async Task<List<ColumnDoc>> LoadColumnsAsync(DbConnection conn, string schema, string table, CancellationToken ct)
    {
        var list = new List<ColumnDoc>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='{Escape(schema)}' AND TABLE_NAME='{Escape(table)}' ORDER BY ORDINAL_POSITION";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
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

    private static async Task<List<string>> LoadPrimaryKeyAsync(DbConnection conn, string schema, string table, CancellationToken ct)
    {
        var pks = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT k.COLUMN_NAME
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS t
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k ON t.CONSTRAINT_NAME = k.CONSTRAINT_NAME AND t.TABLE_SCHEMA = k.TABLE_SCHEMA
WHERE t.TABLE_SCHEMA = '{Escape(schema)}' AND t.TABLE_NAME = '{Escape(table)}' AND t.CONSTRAINT_TYPE = 'PRIMARY KEY'
ORDER BY k.ORDINAL_POSITION";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) pks.Add(reader.GetString(0));
        return pks;
    }

    private static async Task<List<(string Column, string RefTable, string RefColumn)>> LoadForeignKeysAsync(DbConnection conn, string schema, string table, CancellationToken ct)
    {
        var list = new List<(string, string, string)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT kcu.COLUMN_NAME, ccu.TABLE_NAME AS REFERENCED_TABLE_NAME, ccu.COLUMN_NAME AS REFERENCED_COLUMN_NAME
FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu ON rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
JOIN INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE ccu ON rc.UNIQUE_CONSTRAINT_NAME = ccu.CONSTRAINT_NAME
WHERE kcu.TABLE_SCHEMA = '{Escape(schema)}' AND kcu.TABLE_NAME = '{Escape(table)}'";
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

