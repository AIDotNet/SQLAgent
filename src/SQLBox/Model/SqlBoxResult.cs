using System.ComponentModel;

namespace SQLBox.Model;

public class SqlBoxResult
{
    [Description("""
                 Generated SQL statement: If parameterized query is used, it will be an SQL statement with parameters. 
                 <example>
                 SELECT * FROM Users WHERE Age > @AgeParam
                 </example>
                 """)]
    public string Sql { get; set; } = string.Empty;

    [Description("Indicates whether the SQL is a query statement")]
    public bool IsQuery { get; set; }

    [Description("If it is not possible to generate a SQL-friendly version, inform the user accordingly.")]
    public string? ErrorMessage { get; set; }

    [Description("Parameters for the SQL statement, if any")]
    public List<SqlBoxParameter> Parameters { get; set; } = new();

    [Description("ECharts option configuration (for query results visualization)")]
    public string? EchartsOption { get; set; }
}

public class SqlBoxParameter
{
    [Description("Name of the parameter in the SQL statement")]
    public string Name { get; set; } = string.Empty;

    [Description("Value of the parameter as a string")]
    public string Value { get; set; } = string.Empty;
}