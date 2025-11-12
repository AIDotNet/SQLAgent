using System.ComponentModel;

namespace SQLAgent.Model;

public class SQLAgentResult
{
    public string Sql { get; set; } = string.Empty;

    /// <summary>
    /// 执行类型
    /// </summary>
    public SqlBoxExecuteType ExecuteType { get; set; }

    public string? ErrorMessage { get; set; }

    public List<SqlBoxParameter> Parameters { get; set; } = new();

    public Dictionary<string, string>? Columns { get; set; } = null;
    
    public object[] Result { get; set; } = [];

    public string? EchartsOption { get; set; }
}

public enum SqlBoxExecuteType
{
    /// <summary>
    /// 只是查询则返回结果集
    /// </summary>
    [Description("只是查询则返回结果集")] Query,

    /// <summary>
    /// 查询并返回图表选项
    /// </summary>
    [Description("查询并返回图表选项")] EChart,

    /// <summary>
    /// 非查询语句
    /// </summary>
    [Description("非查询语句")] NonQuery
}

public class SqlBoxParameter
{
    [Description("Name of the parameter in the SQL statement")]
    public string Name { get; set; } = string.Empty;

    [Description("Value of the parameter as a string")]
    public string Value { get; set; } = string.Empty;
}