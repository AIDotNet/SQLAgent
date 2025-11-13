using System.Data;
using System.Data.Common;
using Dapper;
using SQLAgent.Model;

namespace SQLAgent.Infrastructure;

public interface IDatabaseService
{
    IDbConnection GetConnection();

    public async Task<int> ExecuteSqliteNonQueryAsync(string sql, List<SqlBoxParameter> parameters)
    {
        using var connection = GetConnection();

        var paramDict = new DynamicParameters();
        foreach (var param in parameters)
        {
            paramDict.Add(param.Name, param.Value);
        }

        connection.Open();

        var result = await connection.ExecuteAsync(sql, paramDict);

        return result;
    }

    public async Task<IEnumerable<dynamic>?> ExecuteSqliteQueryAsync(string sql, List<SqlBoxParameter> parameters)
    {
        using var connection = GetConnection();

        connection.Open();

        var paramDict = new DynamicParameters();
        foreach (var param in parameters)
        {
            paramDict.Add(param.Name, param.Value);
        }

        var result = await connection.QueryAsync(sql, paramDict);

        return result;
    }

    /// <summary>
    /// 搜索表（返回表名和表的详细信息，包括CREATE SQL）
    /// </summary>
    /// <param name="keywords"></param>
    /// <param name="maxResults"></param>
    /// <returns>返回JSON格式的表信息数组，包含表名和CREATE SQL</returns>
    Task<string> SearchTables(string[] keywords, int maxResults = 20);

    /// <summary>
    /// 获取表结构
    /// </summary>
    /// <param name="tableNames"></param>
    /// <returns></returns>
    Task<string> GetTableSchema(string[] tableNames);
}