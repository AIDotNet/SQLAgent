using AIDotNet.Toon;
using Dapper;
using Microsoft.Data.SqlClient;
using SQLAgent;
using SQLAgent.Facade;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SQLAgent.Infrastructure.Providers;

public class SqlServerDatabaseService(SQLAgentOptions options) : IDatabaseService
{
    public IDbConnection GetConnection()
    {
        return new SqlConnection(options.ConnectionString);
    }

    public async Task<string> SearchTables(string[] keywords, int maxResults = 20)
    {
        using var connection = GetConnection();

        string sql;
        var dp = new DynamicParameters();

        if (keywords.Length == 0)
        {
            sql = @"
                SELECT DISTINCT TOP (@maxResults)
                       s.name AS schemaName,
                       o.name AS tableName,
                       s.name + '.' + o.name AS name,
                       CASE o.type WHEN 'U' THEN 'BASE TABLE' WHEN 'V' THEN 'VIEW' ELSE 'OTHER' END AS tableType,
                       ISNULL(CAST(ep.value AS NVARCHAR(MAX)), '') AS tableComment
                FROM sys.objects AS o
                JOIN sys.schemas AS s ON s.schema_id = o.schema_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
                WHERE o.type IN ('U','V')
                  AND s.name NOT IN ('sys','INFORMATION_SCHEMA')
                ORDER BY s.name, o.name;";
            dp.Add("maxResults", maxResults);
        }
        else
        {
            var limitKeys = Math.Min(keywords.Length, 10);
            var conds = new List<string>();
            for (int i = 0; i < limitKeys; i++)
            {
                var param = $"k{i}";
                dp.Add(param, $"%{keywords[i].ToLowerInvariant()}%");
                conds.Add($@"(
                    LOWER(s.name) LIKE @{param}
                    OR LOWER(o.name) LIKE @{param}
                    OR LOWER(CAST(ep.value AS NVARCHAR(MAX))) LIKE @{param}
                    OR LOWER(c.name) LIKE @{param}
                    OR LOWER(CAST(epc.value AS NVARCHAR(MAX))) LIKE @{param}
                )");
            }

            sql = $@"
                SELECT DISTINCT TOP (@maxResults)
                       s.name AS schemaName,
                       o.name AS tableName,
                       s.name + '.' + o.name AS name,
                       CASE o.type WHEN 'U' THEN 'BASE TABLE' WHEN 'V' THEN 'VIEW' ELSE 'OTHER' END AS tableType,
                       ISNULL(CAST(ep.value AS NVARCHAR(MAX)), '') AS tableComment
                FROM sys.objects AS o
                JOIN sys.schemas AS s ON s.schema_id = o.schema_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
                LEFT JOIN sys.columns c
                    ON c.object_id = o.object_id
                LEFT JOIN sys.extended_properties epc
                    ON epc.class = 1 AND epc.major_id = c.object_id AND epc.minor_id = c.column_id AND epc.name = 'MS_Description'
                WHERE o.type IN ('U','V')
                  AND s.name NOT IN ('sys','INFORMATION_SCHEMA')
                  AND ({string.Join(" OR ", conds)})
                ORDER BY s.name, o.name;";
            dp.Add("maxResults", maxResults);
        }

        var rows = (await connection.QueryAsync(sql, dp)).ToArray();
        var tableInfos = new List<object>();
        
        foreach (var r in rows)
        {
            if (r is IDictionary<string, object> d)
            {
                d.TryGetValue("name", out var name);
                d.TryGetValue("schemaName", out var schemaName);
                d.TryGetValue("tableName", out var tableName);
                d.TryGetValue("tableType", out var tableType);
                d.TryGetValue("tableComment", out var tableComment);
                
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
            var schema = "dbo";
            var name = table;
            if (table.Contains('.'))
            {
                var parts = table.Split('.', 2);
                schema = parts[0].Trim('[',']','"');
                name = parts[1].Trim('[',']','"');
            }
            else
            {
                name = table.Trim('[',']','"');
            }

            var obj = await connection.QueryFirstOrDefaultAsync(@"
                SELECT o.object_id,
                       s.name AS [schema],
                       o.name AS [name],
                       CAST(ep.value AS NVARCHAR(MAX)) AS description
                FROM sys.objects o
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                LEFT JOIN sys.extended_properties ep
                  ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
                WHERE s.name = @schema
                  AND o.name = @table
                  AND o.type IN ('U','V');", new { schema, table = name });

            int objectId = 0;
            string tableDescription = string.Empty;

            if (obj == null)
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
                    if (obj is IDictionary<string, object> d)
                    {
                        if (d.TryGetValue("object_id", out var oid) && oid != null) objectId = Convert.ToInt32(oid);
                        if (d.TryGetValue("description", out var desc)) tableDescription = desc?.ToString() ?? string.Empty;
                    }
                    else
                    {
                        dynamic ti = obj;
                        objectId = (int)ti.object_id;
                        tableDescription = (string)(ti.description ?? string.Empty);
                    }
                }
                catch
                {
                }
            }

            var colRows = await connection.QueryAsync(@"
                SELECT
                    c.column_id AS ord,
                    c.name AS name,
                    CASE
                        WHEN t.name IN ('varchar','nvarchar','char','nchar','varbinary','binary')
                            THEN t.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE
                                CAST(CASE WHEN t.name IN ('nchar','nvarchar') THEN c.max_length/2 ELSE c.max_length END AS VARCHAR(10)) END + ')'
                        WHEN t.name IN ('decimal','numeric')
                            THEN t.name + '(' + CAST(c.precision AS VARCHAR(10)) + ',' + CAST(c.scale AS VARCHAR(10)) + ')'
                        ELSE t.name
                    END AS [type],
                    CASE WHEN c.is_nullable = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS notnull,
                    CASE
                        WHEN EXISTS (
                            SELECT 1
                            FROM sys.indexes i
                            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                            WHERE i.object_id = c.object_id
                              AND i.is_primary_key = 1
                              AND ic.column_id = c.column_id
                        ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit)
                    END AS pk,
                    OBJECT_DEFINITION(c.default_object_id) AS defaultValue,
                    CAST(epc.value AS NVARCHAR(MAX)) AS description
                FROM sys.columns c
                JOIN sys.types t ON t.user_type_id = c.user_type_id
                LEFT JOIN sys.extended_properties epc
                    ON epc.class = 1 AND epc.major_id = c.object_id AND epc.minor_id = c.column_id AND epc.name = 'MS_Description'
                WHERE c.object_id = @objectId
                ORDER BY c.column_id;", new { objectId });

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