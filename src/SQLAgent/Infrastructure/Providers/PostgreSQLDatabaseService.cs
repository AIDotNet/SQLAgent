using System.Data;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Npgsql;
using SQLAgent.Facade;
using SQLAgent;
using AIDotNet.Toon;

namespace SQLAgent.Infrastructure.Providers;

public class PostgreSQLDatabaseService(SQLAgentOptions options) : IDatabaseService
{
    public IDbConnection GetConnection()
    {
        return new NpgsqlConnection(options.ConnectionString);
    }

    /// <inheritdoc />
    public async Task<string> SearchTables(string[] keywords, int maxResults = 20)
    {
        using var connection = GetConnection();

        string sql;
        var dp = new Dapper.DynamicParameters();

        if (keywords.Length == 0)
        {
            sql = @"
                SELECT DISTINCT 
                    n.nspname AS schemaName,
                    c.relname AS tableName,
                    n.nspname || '.' || c.relname AS name,
                    CASE c.relkind 
                        WHEN 'r' THEN 'BASE TABLE'
                        WHEN 'v' THEN 'VIEW'
                        WHEN 'm' THEN 'MATERIALIZED VIEW'
                        WHEN 'p' THEN 'PARTITIONED TABLE'
                        WHEN 'f' THEN 'FOREIGN TABLE'
                        ELSE 'OTHER'
                    END AS tableType,
                    COALESCE(obj_description(c.oid, 'pg_class'), '') AS tableComment
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname NOT IN ('pg_catalog','information_schema','pg_toast')
                  AND c.relkind IN ('r','p','v','m','f')
                                -- ORDER BY 里使用未在 SELECT DISTINCT 结果集中出现的表达式会导致 PostgreSQL 错误 42P10。
                                -- 改为按已选出的别名 name 排序。
                                ORDER BY name
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
                dp.Add(param, $"%{keywords[i]}%");
                conds.Add($@"(
                    n.nspname ILIKE @{param}
                    OR c.relname ILIKE @{param}
                    OR obj_description(c.oid, 'pg_class') ILIKE @{param}
                    OR a.attname ILIKE @{param}
                    OR col_description(c.oid, a.attnum) ILIKE @{param}
                )");
            }

            sql = $@"
                SELECT DISTINCT 
                    n.nspname AS schemaName,
                    c.relname AS tableName,
                    n.nspname || '.' || c.relname AS name,
                    CASE c.relkind 
                        WHEN 'r' THEN 'BASE TABLE'
                        WHEN 'v' THEN 'VIEW'
                        WHEN 'm' THEN 'MATERIALIZED VIEW'
                        WHEN 'p' THEN 'PARTITIONED TABLE'
                        WHEN 'f' THEN 'FOREIGN TABLE'
                        ELSE 'OTHER'
                    END AS tableType,
                    COALESCE(obj_description(c.oid, 'pg_class'), '') AS tableComment
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                LEFT JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                WHERE n.nspname NOT IN ('pg_catalog','information_schema','pg_toast')
                  AND c.relkind IN ('r','p','v','m','f')
                  AND ({string.Join(" OR ", conds)})
                                ORDER BY name
                LIMIT @maxResults;";
            dp.Add("maxResults", maxResults);
        }

        var rows = (await connection.QueryAsync(sql, dp)).ToArray();
        var tableInfos = new List<object>();
        
        foreach (var r in rows)
        {
            if (r is IDictionary<string, object> d)
            {
                d.TryGetValue("name", out var name);
                d.TryGetValue("schemaname", out var schemaName);
                d.TryGetValue("tablename", out var tableName);
                d.TryGetValue("tabletype", out var tableType);
                d.TryGetValue("tablecomment", out var tableComment);
                
                tableInfos.Add(new
                {
                    name = name?.ToString() ?? string.Empty,
                    schema = schemaName?.ToString() ?? string.Empty,
                    table = tableName?.ToString() ?? string.Empty,
                    type = tableType?.ToString() ?? "BASE TABLE",
                    comment = tableComment?.ToString() ?? string.Empty
                });
            }
            else
            {
                try
                {
                    tableInfos.Add(new
                    {
                        name = r?.name?.ToString() ?? string.Empty,
                        schema = r?.schemaName?.ToString() ?? string.Empty,
                        table = r?.tableName?.ToString() ?? string.Empty,
                        type = r?.tableType?.ToString() ?? "BASE TABLE",
                        comment = r?.tableComment?.ToString() ?? string.Empty
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
        var stringBuilder = new StringBuilder();

        foreach (var table in tableNames)
        {
            var schema = "public";
            var name = table;
            if (table.Contains('.'))
            {
                var parts = table.Split('.', 2);
                schema = parts[0].Trim('\"');
                name = parts[1].Trim('\"');
            }

            var tableInfo = await connection.QueryFirstOrDefaultAsync(@"
                SELECT c.oid AS oid,
                       n.nspname AS schema,
                       c.relname AS name,
                       obj_description(c.oid, 'pg_class') AS description
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema
                  AND c.relname = @table
                  AND c.relkind IN ('r','p','v','m','f');", new { schema, table = name });

            long oid = 0;
            string tableDescription = string.Empty;

            if (tableInfo == null)
            {
                stringBuilder.AppendLine("table:" + $"{schema}.{name}");
                stringBuilder.AppendLine("tableDescription:table not found");
                stringBuilder.AppendLine("columns:" + JsonSerializer.Serialize(Array.Empty<object>(), SQLAgentJsonOptions.DefaultOptions));
                stringBuilder.AppendLine();
                continue;
            }
            else
            {
                try
                {
                    if (tableInfo is IDictionary<string, object> d)
                    {
                        if (d.TryGetValue("oid", out var o) && o != null) oid = Convert.ToInt64(o);
                        if (d.TryGetValue("description", out var desc)) tableDescription = desc?.ToString() ?? string.Empty;
                    }
                    else
                    {
                        dynamic ti = tableInfo;
                        oid = (long)ti.oid;
                        tableDescription = (string)(ti.description ?? string.Empty);
                    }
                }
                catch
                {
                }
            }

            var colRows = await connection.QueryAsync(@"
                SELECT
                    a.attnum,
                    a.attname AS name,
                    format_type(a.atttypid, a.atttypmod) AS type,
                    a.attnotnull AS notnull,
                    EXISTS (
                        SELECT 1
                        FROM pg_index i
                        WHERE i.indrelid = c.oid
                          AND i.indisprimary
                          AND a.attnum = ANY (i.indkey)
                    ) AS pk,
                    pg_get_expr(ad.adbin, ad.adrelid) AS defaultValue,
                    col_description(c.oid, a.attnum) AS description
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_attribute a ON a.attrelid = c.oid
                LEFT JOIN pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
                WHERE c.oid = @oid
                  AND a.attnum > 0
                  AND NOT a.attisdropped
                ORDER BY a.attnum;", new { oid });

            var columns = new List<object>();
            foreach (var c in colRows)
            {
                if (c is IDictionary<string, object> colDict)
                {
                    colDict.TryGetValue("name", out var colName);
                    colDict.TryGetValue("type", out var colType);
                    colDict.TryGetValue("notnull", out var colNotNull);
                    colDict.TryGetValue("pk", out var colPk);
                    colDict.TryGetValue("defaultValue", out var colDefault);
                    colDict.TryGetValue("description", out var colDesc);

                    columns.Add(new
                    {
                        name = colName,
                        type = colType,
                        notnull = colNotNull,
                        pk = colPk,
                        defaultValue = colDefault,
                        description = colDesc
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
                            defaultValue = (c as dynamic)?.defaultValue,
                            description = (c as dynamic)?.description
                        });
                    }
                    catch
                    {
                    }
                }
            }

            stringBuilder.AppendLine("table:" + $"{schema}.{name}");
            stringBuilder.AppendLine("tableDescription:" + tableDescription);
            stringBuilder.AppendLine("columns:" + JsonSerializer.Serialize(columns, SQLAgentJsonOptions.DefaultOptions));
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
}