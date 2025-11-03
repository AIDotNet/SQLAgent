using System.ComponentModel;

namespace SQLAgent.Model;

public class SQLAgentResult
{
    public string Sql { get; set; } = string.Empty;

    public bool IsQuery { get; set; }

    public string? ErrorMessage { get; set; }

    public List<SqlBoxParameter> Parameters { get; set; } = new();

    public string? EchartsOption { get; set; }
}

public class SqlBoxParameter
{
    [Description("Name of the parameter in the SQL statement")]
    public string Name { get; set; } = string.Empty;

    [Description("Value of the parameter as a string")]
    public string Value { get; set; } = string.Empty;
}