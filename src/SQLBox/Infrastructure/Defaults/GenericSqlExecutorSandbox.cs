using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SQLBox.Infrastructure;

namespace SQLBox.Infrastructure.Defaults;

public sealed class GenericSqlExecutorSandbox(IDbConnectionFactory factory) : IExecutorSandbox
{
    public async Task<string?> ExplainAsync(string sql, string dialect, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(sql.TrimStart(), @"^(?is)(explain\s+)?select\b"))
            throw new InvalidOperationException("ExecutorSandbox only supports SELECT/EXPLAIN SELECT.");

        await using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);

        var d = (dialect ?? string.Empty).ToLowerInvariant();
        if (d is "postgres" or "postgresql" or "pg")
        {
            return await ExplainSimpleAsync(conn, PrefixIfNeeded(sql, "EXPLAIN "), ct);
        }
        if (d is "mysql")
        {
            return await ExplainSimpleAsync(conn, PrefixIfNeeded(sql, "EXPLAIN "), ct);
        }
        if (d is "mssql" or "sqlserver")
        {
            return await ExplainSqlServerAsync(conn, sql, ct);
        }

        // Fallback: try a generic EXPLAIN
        return await ExplainSimpleAsync(conn, PrefixIfNeeded(sql, "EXPLAIN "), ct);
    }

    private static async Task<string> ExplainSimpleAsync(System.Data.Common.DbConnection conn, string explainSql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = explainSql;
        var lines = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        do
        {
            var fieldCount = reader.FieldCount;
            while (await reader.ReadAsync(ct))
            {
                var parts = new object[fieldCount];
                reader.GetValues(parts);
                lines.Add(string.Join(" | ", parts.Select(p => p?.ToString())));
            }
        } while (await reader.NextResultAsync(ct));
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<string> ExplainSqlServerAsync(System.Data.Common.DbConnection conn, string sql, CancellationToken ct)
    {
        // SHOWPLAN_ALL returns a tabular plan description
        await using (var on = conn.CreateCommand())
        {
            on.CommandText = "SET SHOWPLAN_ALL ON";
            await on.ExecuteNonQueryAsync(ct);
        }

        var lines = new List<string>();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            do
            {
                var headers = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
                lines.Add(string.Join(" | ", headers));
                while (await reader.ReadAsync(ct))
                {
                    var parts = new object[reader.FieldCount];
                    reader.GetValues(parts);
                    lines.Add(string.Join(" | ", parts.Select(p => p?.ToString())));
                }
            } while (await reader.NextResultAsync(ct));
        }
        finally
        {
            await using var off = conn.CreateCommand();
            off.CommandText = "SET SHOWPLAN_ALL OFF";
            await off.ExecuteNonQueryAsync(ct);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string PrefixIfNeeded(string sql, string prefix)
    {
        var s = sql.TrimStart();
        if (Regex.IsMatch(s, @"^(?is)explain\b")) return sql; // already explained
        return prefix + sql;
    }
}

