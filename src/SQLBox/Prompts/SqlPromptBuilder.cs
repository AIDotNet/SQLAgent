using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SQLBox.Entities;

namespace SQLBox.Prompts;

public sealed class SqlPromptBuilder : ISqlPromptBuilder
{
    public Task<string> BuildPromptAsync(
        string userQuestion,
        string dialect,
        SchemaContext schemaContext,
        bool allowWrite,
        CancellationToken ct = default)
    {
        return Task.FromResult(BuildPrompt(userQuestion, dialect, schemaContext, allowWrite));
    }

    public string BuildPrompt(string userQuestion, string dialect, SchemaContext schemaContext, bool allowWrite)
    {
        var systemPrompt = BuildSystemPrompt(dialect, allowWrite);
        var schemaInfo = BuildSchemaInformation(schemaContext);
        var userPrompt = BuildUserPrompt(userQuestion, dialect, schemaContext, schemaInfo);

        return $"{systemPrompt}\n\n{schemaInfo}\n\n{userPrompt}";
    }

    private string BuildSystemPrompt(string dialect, bool allowWrite)
    {
        var dialectRules = GetDialectRules(dialect);

        var security = allowWrite
            ? """
              Security Guidelines (Write-enabled):
              - Write operations are allowed when explicitly requested:
                INSERT, UPDATE, DELETE, and DDL (CREATE/ALTER/DROP TABLE)
              - ALWAYS parameterize user inputs; never concatenate strings
              - UPDATE/DELETE must include precise WHERE clauses to avoid full-table impact
              - Consider transaction safety and idempotency when applicable
              """
            : PromptConstants.SecurityRestrictions;

        return $"{PromptConstants.SystemRoleDescription}\n\n" +
               $"{PromptConstants.SchemaAnalysisInstructions}\n\n" +
               $"{PromptConstants.SqlGenerationGuidelines}\n\n" +
               $"{dialectRules}\n\n" +
               $"{security}\n\n" +
               $"{PromptConstants.JsonOutputRequirements}";
    }

    private string BuildSchemaInformation(SchemaContext context)
    {
        if (context.Tables == null || context.Tables.Count == 0)
        {
            return "# SCHEMA INFORMATION\nNo table schema information available.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("# SCHEMA INFORMATION");
        sb.AppendLine($"Database contains {context.Tables.Count} tables:");
        sb.AppendLine();

        foreach (var table in context.Tables.OrderBy(t => t.Name))
        {
            BuildTableDescription(sb, table);
        }

        return sb.ToString();
    }

    private void BuildTableDescription(StringBuilder sb, TableDoc table)
    {
        sb.AppendLine($"## Table: {table.Name}");

        if (!string.IsNullOrEmpty(table.Schema))
            sb.AppendLine($"**Schema**: {table.Schema}");

        if (!string.IsNullOrEmpty(table.Description))
            sb.AppendLine($"**Description**: {table.Description}");

        if (table.Aliases?.Length > 0)
            sb.AppendLine($"**Aliases**: {string.Join(", ", table.Aliases)}");

        // Columns
        sb.AppendLine($"**Columns** ({table.Columns.Count}):");
        foreach (var column in table.Columns.OrderBy(c => c.Name))
        {
            var columnInfo = new List<string>
            {
                $"`{column.Name}`",
                column.DataType.ToUpper(),
                column.Nullable ? "NULL" : "NOT NULL"
            };

            if (!string.IsNullOrEmpty(column.Default))
                columnInfo.Add($"DEFAULT {column.Default}");

            if (!string.IsNullOrEmpty(column.Description))
                columnInfo.Add($"-- {column.Description}");

            if (column.Aliases?.Length > 0)
                columnInfo.Add($"(aliases: {string.Join(", ", column.Aliases)})");

            sb.AppendLine($"  - {string.Join(" ", columnInfo)}");
        }

        // Primary Key
        if (table.PrimaryKey?.Count > 0)
        {
            sb.AppendLine($"**Primary Key**: {string.Join(", ", table.PrimaryKey)}");
        }

        // Foreign Keys
        if (table.ForeignKeys?.Count > 0)
        {
            sb.AppendLine("**Foreign Keys**:");
            foreach (var fk in table.ForeignKeys)
            {
                sb.AppendLine($"  - `{fk.Column}` → `{fk.RefTable}`.`{fk.RefColumn}`");
            }
        }

        sb.AppendLine();
    }

    private string BuildUserPrompt(string userQuestion, string dialect, SchemaContext schemaContext, string schemaInfo)
    {
        var availableTables = schemaContext.Tables.Select(t => t.Name).ToArray();
        var tableList = string.Join(", ", availableTables);

        return $"# USER QUESTION\n" +
               $"{userQuestion}\n\n" +
               $"# TASK\n" +
               $"Generate a SQL query that answers the user's question using the schema information provided above.\n\n" +
               $"# CONSTRAINTS\n" +
               $"- Use only these tables: {tableList}\n" +
               $"- Follow the {dialect} SQL dialect syntax\n" +
               $"- Apply all the rules and guidelines mentioned in the system prompt\n" +
               $"- Ensure your query uses correct table and column names from the schema\n\n" +
               $"# OUTPUT FORMAT\n" +
               $"Return your response as valid JSON with exactly these fields:\n" +
               $"{{\"sql\": \"your generated SQL query\", " +
               $"\"params\": {{\"param1\": \"value1\", \"param2\": \"value2\"}}, " +
               $"\"tables\": [\"table1\", \"table2\"]}}\n\n" +
               $"Generate the SQL query now:";
    }

    private string GetDialectRules(string dialect)
    {
        return dialect.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => PromptConstants.DialectSpecificRules.PostgreSQL,
            "mysql" => PromptConstants.DialectSpecificRules.MySQL,
            "sqlite" => PromptConstants.DialectSpecificRules.SQLite,
            "mssql" or "sqlserver" => PromptConstants.DialectSpecificRules.SQLServer,
            _ => PromptConstants.DialectSpecificRules.SQLite // Default fallback
        };
    }
}