using AIDotNet.Toon;
using Dapper;
using Microsoft.Data.Sqlite;
using SQLAgent.Facade;
using System.Data;
using System.Text;
using System.Text.Json;

namespace SQLAgent.Infrastructure.Providers;

public class SQLiteDatabaseService(SQLAgentOptions options) : IDatabaseService
{
    public IDbConnection GetConnection()
    {
        return new SqliteConnection(options.ConnectionString);
    }

    public async Task<string> SearchTables(string[] keywords, int maxResults = 20)
    {
        using var connection = GetConnection();

        string sql;
        var dp = new DynamicParameters();

        if (keywords.Length == 0)
        {
            sql = @"
                        SELECT name, sql, type
                        FROM sqlite_master
                        WHERE type='table' AND name NOT LIKE 'sqlite_%'
                        LIMIT @maxResults;";
            dp.Add("maxResults", maxResults);
        }
        else
        {
            var limitKeys = Math.Min(keywords.Length, 10);
            var conds = new List<string>();
            for (int i = 0; i < limitKeys; i++)
            {
                var param = $"k{i}";
                dp.Add(param, keywords[i]);
                conds.Add($"(name LIKE '%' || @{param} || '%' OR sql LIKE '%' || @{param} || '%')");
            }

            sql = $@"
                        SELECT name, sql, type
                        FROM sqlite_master
                        WHERE type='table' AND name NOT LIKE 'sqlite_%'
                          AND ({string.Join(" OR ", conds)})
                        LIMIT @maxResults;";
            dp.Add("maxResults", maxResults);
        }

        var rows = (await connection.QueryAsync(sql, dp)).ToArray();
        var tableInfos = new List<object>();

        foreach (var r in rows)
        {
            if (r is IDictionary<string, object> d)
            {
                d.TryGetValue("name", out var tableName);
                d.TryGetValue("sql", out var createSql);
                d.TryGetValue("type", out var tableType);

                tableInfos.Add(new
                {
                    name = tableName?.ToString() ?? string.Empty,
                    createSql = createSql?.ToString() ?? string.Empty,
                    type = tableType?.ToString() ?? "table"
                });
            }
            else
            {
                try
                {
                    tableInfos.Add(new
                    {
                        name = r?.name?.ToString() ?? string.Empty,
                        createSql = r?.sql?.ToString() ?? string.Empty,
                        type = r?.type?.ToString() ?? "table"
                    });
                }
                catch
                {
                    // 跳过无法解析的行
                }
            }
        }

        return ToonSerializer.Serialize(tableInfos);
    }

    public async Task<string> GetTableSchema(string[] tableNames)
    {
        using var connection = GetConnection();
        var master = await connection.QueryFirstOrDefaultAsync(
            "SELECT name, sql FROM sqlite_master WHERE type='table' AND name = @table;",
            new { table = tableNames });

        if (master == null)
        {
            return "<system-remind>table not found</system-remind>";
        }

        var stringBuilder = new StringBuilder();

        foreach (var tableName in tableNames)
        {
            var safeName = tableName.Replace("\"", "\"\"");

            // columns
            var pragmaCols = await connection.QueryAsync($"PRAGMA table_info(\"{safeName}\");");
            var columns = new List<object>();
            foreach (var c in pragmaCols)
            {
                if (c is IDictionary<string, object> colDict)
                {
                    colDict.TryGetValue("name", out var colName);
                    colDict.TryGetValue("type", out var colType);
                    colDict.TryGetValue("notnull", out var colNotNull);
                    colDict.TryGetValue("pk", out var colPk);
                    colDict.TryGetValue("dflt_value", out var colDefault);

                    columns.Add(new
                    {
                        name = colName,
                        type = colType,
                        notnull = colNotNull,
                        pk = colPk,
                        defaultValue = colDefault
                    });
                }
                else
                {
                    try
                    {
                        columns.Add(new
                        {
                            name = (c as dynamic)?.name,
                            type = (c as dynamic)?.type,
                            notnull = (c as dynamic)?.notnull,
                            pk = (c as dynamic)?.pk,
                            defaultValue = (c as dynamic)?.dflt_value
                        });
                    }
                    catch
                    {
                    }
                }
            }

            stringBuilder.AppendLine("table:" + tableNames);
            stringBuilder.AppendLine("columns:" +
                                     JsonSerializer.Serialize(columns, SQLAgentJsonOptions.DefaultOptions));
            stringBuilder.AppendLine();
        }

        return
            $"""
             <system-remind>
             Note: The following is the structure information of the table:
             {stringBuilder}
             </system-remind>
             """;
    }

    public async Task<string> GetAllTableNamesAsync()
    {
        using var connection = GetConnection();

        var sql = @"SELECT name, sql, type
                        FROM sqlite_master
                        WHERE type='table' AND name NOT LIKE 'sqlite_%'";

        var rows = (await connection.QueryAsync(sql)).ToArray();
        var tableInfos = new List<object>();

        foreach (var r in rows)
        {
            if (r is IDictionary<string, object> d)
            {
                d.TryGetValue("name", out var tableName);
                d.TryGetValue("sql", out var createSql);
                d.TryGetValue("type", out var tableType);

                tableInfos.Add(new
                {
                    name = tableName?.ToString() ?? string.Empty,
                    createSql = createSql?.ToString() ?? string.Empty,
                    type = tableType?.ToString() ?? "table"
                });
            }
            else
            {
                try
                {
                    tableInfos.Add(new
                    {
                        name = r?.name?.ToString() ?? string.Empty,
                        createSql = r?.sql?.ToString() ?? string.Empty,
                        type = r?.type?.ToString() ?? "table"
                    });
                }
                catch
                {
                    // 跳过无法解析的行
                }
            }
        }

        return ToonSerializer.Serialize(tableInfos);
    }
}