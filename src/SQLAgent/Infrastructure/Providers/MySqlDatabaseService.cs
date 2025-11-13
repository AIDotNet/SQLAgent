using AIDotNet.Toon;
using Dapper;
using MySql.Data.MySqlClient;
using SQLAgent;
using SQLAgent.Facade;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SQLAgent.Infrastructure.Providers;

public class MySqlDatabaseService(SQLAgentOptions options) : IDatabaseService
{
    public IDbConnection GetConnection()
    {
        return new MySqlConnection(options.ConnectionString);
    }

    public async Task<string> SearchTables(string[] keywords, int maxResults = 20)
    {
        using var connection = GetConnection();

        string sql;
        var dp = new DynamicParameters();

        if (keywords.Length == 0)
        {
            sql = @"
                SELECT DISTINCT 
                    t.TABLE_SCHEMA AS schemaName,
                    t.TABLE_NAME AS tableName,
                    CONCAT(t.TABLE_SCHEMA, '.', t.TABLE_NAME) AS name,
                    t.TABLE_TYPE AS tableType,
                    IFNULL(t.TABLE_COMMENT,'') AS tableComment
                FROM INFORMATION_SCHEMA.TABLES t
                WHERE t.TABLE_TYPE IN ('BASE TABLE','VIEW')
                  AND t.TABLE_SCHEMA NOT IN ('information_schema','mysql','performance_schema','sys')
                ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME
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
                dp.Add(param, "%" + keywords[i].ToLowerInvariant() + "%");
                conds.Add($@"(
                    LOWER(t.TABLE_SCHEMA) LIKE @{param}
                    OR LOWER(t.TABLE_NAME) LIKE @{param}
                    OR LOWER(IFNULL(t.TABLE_COMMENT,'')) LIKE @{param}
                    OR LOWER(c.COLUMN_NAME) LIKE @{param}
                    OR LOWER(IFNULL(c.COLUMN_COMMENT,'')) LIKE @{param}
                )");
            }

            sql = $@"
                SELECT DISTINCT 
                    t.TABLE_SCHEMA AS schemaName,
                    t.TABLE_NAME AS tableName,
                    CONCAT(t.TABLE_SCHEMA, '.', t.TABLE_NAME) AS name,
                    t.TABLE_TYPE AS tableType,
                    IFNULL(t.TABLE_COMMENT,'') AS tableComment
                FROM INFORMATION_SCHEMA.TABLES t
                LEFT JOIN INFORMATION_SCHEMA.COLUMNS c 
                    ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME
                WHERE t.TABLE_TYPE IN ('BASE TABLE','VIEW')
                  AND t.TABLE_SCHEMA NOT IN ('information_schema','mysql','performance_schema','sys')
                  AND ({string.Join(" OR ", conds)})
                ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME
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
            string schema;
            string name;

            if (table.Contains('.'))
            {
                var parts = table.Split('.', 2);
                schema = parts[0].Trim('`', '"');
                name = parts[1].Trim('`', '"');
            }
            else
            {
                schema = (connection as MySqlConnection)?.Database ?? string.Empty;
                if (string.IsNullOrWhiteSpace(schema))
                {
                    try
                    {
                        var db = await connection.QueryFirstOrDefaultAsync<string>("SELECT DATABASE();");
                        if (!string.IsNullOrWhiteSpace(db)) schema = db!;
                    }
                    catch
                    {
                    }
                }

                name = table.Trim('`', '"');
            }

            var tableInfo = await connection.QueryFirstOrDefaultAsync(@"
                SELECT t.TABLE_SCHEMA AS schema,
                       t.TABLE_NAME AS name,
                       t.TABLE_COMMENT AS description
                FROM INFORMATION_SCHEMA.TABLES t
                WHERE t.TABLE_SCHEMA = @schema
                  AND t.TABLE_NAME = @table
                  AND t.TABLE_TYPE IN ('BASE TABLE','VIEW');", new { schema, table = name });

            string tableDescription = string.Empty;

            if (tableInfo == null)
            {
                stringBuilder.AppendLine("table:" + $"{schema}.{name}");
                stringBuilder.AppendLine("tableDescription:table not found");
                stringBuilder.AppendLine("columns:" +
                                         JsonSerializer.Serialize(Array.Empty<object>(),
                                             SQLAgentJsonOptions.DefaultOptions));
                stringBuilder.AppendLine();
                continue;
            }
            else
            {
                try
                {
                    if (tableInfo is IDictionary<string, object> d)
                    {
                        if (d.TryGetValue("description", out var desc))
                            tableDescription = desc?.ToString() ?? string.Empty;
                    }
                    else
                    {
                        dynamic ti = tableInfo;
                        tableDescription = (string)(ti.description ?? string.Empty);
                    }
                }
                catch
                {
                }
            }

            var colRows = await connection.QueryAsync(@"
                SELECT
                    c.ORDINAL_POSITION AS ord,
                    c.COLUMN_NAME AS name,
                    c.COLUMN_TYPE AS type,
                    (CASE WHEN c.IS_NULLABLE = 'NO' THEN TRUE ELSE FALSE END) AS notnull,
                    (CASE WHEN c.COLUMN_KEY = 'PRI' THEN TRUE ELSE FALSE END) AS pk,
                    c.COLUMN_DEFAULT AS defaultValue,
                    c.COLUMN_COMMENT AS description
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = @schema
                  AND c.TABLE_NAME = @table
                ORDER BY c.ORDINAL_POSITION;", new { schema, table = name });

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


        var sql = @"
                SELECT DISTINCT 
                    t.TABLE_SCHEMA AS schemaName,
                    t.TABLE_NAME AS tableName,
                    CONCAT(t.TABLE_SCHEMA, '.', t.TABLE_NAME) AS name,
                    t.TABLE_TYPE AS tableType,
                    IFNULL(t.TABLE_COMMENT,'') AS tableComment
                FROM INFORMATION_SCHEMA.TABLES t
                WHERE t.TABLE_TYPE IN ('BASE TABLE','VIEW')
                  AND t.TABLE_SCHEMA NOT IN ('information_schema','mysql','performance_schema','sys')
                ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME";

        var rows = (await connection.QueryAsync(sql)).ToArray();
        var tableInfos = new List<object>();

        foreach (var r in rows)
        {
            string name = string.Empty;
            string schemaName = string.Empty;
            string tableName = string.Empty;
            string tableType = "BASE TABLE";
            string tableComment = string.Empty;
            
            if (r is IDictionary<string, object> d)
            {
                if (d.TryGetValue("name", out var n)) name = n?.ToString() ?? string.Empty;
                if (d.TryGetValue("schemaName", out var sn)) schemaName = sn?.ToString() ?? string.Empty;
                if (d.TryGetValue("tableName", out var tn)) tableName = tn?.ToString() ?? string.Empty;
                if (d.TryGetValue("tableType", out var tt)) tableType = tt?.ToString() ?? "BASE TABLE";
                if (d.TryGetValue("tableComment", out var tc)) tableComment = tc?.ToString() ?? string.Empty;
            }
            else
            {
                try
                {
                    dynamic dr = r;
                    name = dr?.name?.ToString() ?? string.Empty;
                    schemaName = dr?.schemaName?.ToString() ?? string.Empty;
                    tableName = dr?.tableName?.ToString() ?? string.Empty;
                    tableType = dr?.tableType?.ToString() ?? "BASE TABLE";
                    tableComment = dr?.tableComment?.ToString() ?? string.Empty;
                }
                catch
                {
                    // 跳过无法解析的行
                }
            }
            
            // 获取表的创建 SQL (MySQL 有原生的 SHOW CREATE TABLE 命令)
            var createSql = await GetTableCreateSql(connection, schemaName, tableName, tableType);

            tableInfos.Add(new
            {
                name,
                schema = schemaName,
                table = tableName,
                type = tableType,
                comment = tableComment,
                createSql
            });
        }

        return ToonSerializer.Serialize(tableInfos);
    }

    private async Task<string> GetTableCreateSql(IDbConnection connection, string schemaName, string tableName, string tableType)
    {
        try
        {
            // MySQL 有原生的 SHOW CREATE TABLE/VIEW 命令
            var commandType = tableType == "VIEW" ? "VIEW" : "TABLE";
            var showCreate = await connection.QueryFirstOrDefaultAsync(
                $"SHOW CREATE {commandType} `{schemaName}`.`{tableName}`");

            if (showCreate != null)
            {
                // SHOW CREATE TABLE 返回两列: Table 和 Create Table
                // SHOW CREATE VIEW 返回多列,其中包含 Create View
                if (showCreate is IDictionary<string, object> dict)
                {
                    // 尝试获取 Create Table 或 Create View
                    if (dict.TryGetValue("Create Table", out var createTable))
                        return createTable?.ToString() ?? string.Empty;
                    if (dict.TryGetValue("Create View", out var createView))
                        return createView?.ToString() ?? string.Empty;
                }
                else
                {
                    try
                    {
                        dynamic d = showCreate;
                        // 动态访问可能的字段名
                        return (d?.CreateTable ?? d?.CreateView ?? d?.Create_Table ?? d?.Create_View)?.ToString() ?? string.Empty;
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
        
        return string.Empty;
    }

}