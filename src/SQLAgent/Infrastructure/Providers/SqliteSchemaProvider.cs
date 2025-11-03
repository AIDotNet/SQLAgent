using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SQLAgent.Entities;

namespace SQLAgent.Infrastructure.Providers;

public sealed class SqliteSchemaProvider : ISchemaProvider
{
    private readonly IDbConnectionFactory _factory;
    private readonly Func<string, bool>? _tableFilter;

    public SqliteSchemaProvider(IDbConnectionFactory factory, Func<string, bool>? tableFilter = null)
    {
        _factory = factory;
        _tableFilter = tableFilter;
    }

    public async Task<DatabaseSchema> LoadAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.CreateConnection();
        await conn.OpenAsync(ct);

        var tables = await LoadTablesAsync(conn, ct);
        var tableDocs = new List<TableDoc>();

        foreach (var t in tables)
        {
            if (_tableFilter != null && !_tableFilter(t.Name)) continue;

            var columns = await LoadColumnsAsync(conn, t.Name, ct);
            var fks = await LoadForeignKeysAsync(conn, t.Name, ct);

            tableDocs.Add(new TableDoc
            {
                Schema = "main",
                Name = t.Name,
                Aliases = Array.Empty<string>(),
                Description = string.Empty,
                Columns = columns,
                PrimaryKey = columns.Where(c => (c.Stats != null && c.Stats.TryGetValue("pk", out var pk) && pk is int pkOrd && pkOrd > 0))
                                     .Select(c => c.Name).ToList(),
                ForeignKeys = fks,
                Stats = null
            });
        }

        return new DatabaseSchema
        {
            Name = "sqlite",
            Dialect = "sqlite",
            Tables = tableDocs
        };
    }

    private static async Task<List<(string Name, string Type)>> LoadTablesAsync(DbConnection conn, CancellationToken ct)
    {
        var result = new List<(string, string)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, type FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetString(0), reader.GetString(1)));
        }
        return result;
    }

    private static async Task<List<ColumnDoc>> LoadColumnsAsync(DbConnection conn, string table, CancellationToken ct)
    {
        var list = new List<ColumnDoc>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{Escape(table)}')";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            // pragma columns: cid, name, type, notnull, dflt_value, pk
            var name = reader.GetString(1);
            var type = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var notnull = !reader.IsDBNull(3) && reader.GetInt32(3) == 1;
            var dflt = reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString();
            var pk = !reader.IsDBNull(5) ? reader.GetInt32(5) : 0;

            list.Add(new ColumnDoc
            {
                Name = name,
                Aliases = Array.Empty<string>(),
                Description = string.Empty,
                DataType = type,
                Nullable = !notnull,
                Default = dflt,
                Stats = new Dictionary<string, object?> { { "pk", pk } }
            });
        }
        return list;
    }

    private static async Task<List<(string Column, string RefTable, string RefColumn)>> LoadForeignKeysAsync(DbConnection conn, string table, CancellationToken ct)
    {
        var fks = new List<(string, string, string)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list('{Escape(table)}')";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            // columns: id, seq, table, from, to, on_update, on_delete, match
            var refTable = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var from = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var to = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(refTable))
                fks.Add((from, refTable, to));
        }
        return fks;
    }

    private static string Escape(string ident) => ident.Replace("'", "''");
}

