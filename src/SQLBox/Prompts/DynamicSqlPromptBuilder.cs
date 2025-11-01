using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SQLBox.Entities;

namespace SQLBox.Prompts;

public sealed class DynamicSqlPromptBuilder : ISqlPromptBuilder
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
        var systemPrompt = BuildDynamicSystemPrompt(dialect, allowWrite);
        var schemaSummary = BuildSchemaSummary(schemaContext, dialect);
        var queryGuidance = BuildQueryGuidance(userQuestion, schemaContext);

        return $"""
            {systemPrompt}

            {schemaSummary}

            {queryGuidance}

            User Question: {userQuestion}

            Generate the SQL query:
            """;
    }

    private string BuildDynamicSystemPrompt(string dialect, bool allowWrite)
    {
        var dialectSpecific = GetDialectSpecificGuidance(dialect);

        if (!allowWrite)
        {
            return $"You are a SQL query generator. Follow these principles:\n\n" +
                   $"CORE RULES:\n" +
                   $"1. Generate only SELECT queries (no INSERT/UPDATE/DELETE/DDL)\n" +
                   $"2. Use actual table/column names from the provided schema\n" +
                   $"3. Parameterize all literal values ({GetParameterStyle(dialect)})\n" +
                   $"4. Return valid JSON: {{\"sql\": \"query\", \"params\": {{...}}, \"tables\": [...]}}\n\n" +
                   $"{dialectSpecific}\n\n" +
                   $"SECURITY:\n" +
                   $"- Read-only mode: Only SELECT statements allowed\n" +
                   $"- Use parameterized queries for all user input\n" +
                   $"- No DDL or data modification statements";
        }

        // allowWrite == true
        return $"You are a SQL query generator. Follow these principles:\n\n" +
               $"CORE RULES:\n" +
               $"1. Generate queries for SELECT as well as data/structure changes when explicitly requested\n" +
               $"   (INSERT, UPDATE, DELETE; CREATE/ALTER/DROP TABLE)\n" +
               $"2. Use actual table/column names from the provided schema\n" +
               $"3. Parameterize all literal values ({GetParameterStyle(dialect)})\n" +
               $"4. Return valid JSON: {{\"sql\": \"query\", \"params\": {{...}}, \"tables\": [...]}}\n\n" +
               $"{dialectSpecific}\n\n" +
               $"SECURITY:\n" +
               $"- Write operations allowed: be conservative and safe\n" +
               $"- Always parameterize user input; never concatenate\n" +
               $"- For UPDATE/DELETE, include precise WHERE clauses to avoid full-table changes\n" +
               $"- For DDL (CREATE/ALTER/DROP), ensure correct syntax and explicit intent";
    }

    private string BuildSchemaSummary(SchemaContext context, string dialect)
    {
        if (context.Tables == null || context.Tables.Count == 0)
            return "Database schema: No tables available.";

        var sb = new StringBuilder();
        sb.AppendLine($"DATABASE OVERVIEW ({context.Tables.Count} tables):");

        // Group tables by relevance to the query (simplified)
        var relevantTables = context.Tables.Take(5).ToList(); // Limit to most relevant tables

        foreach (var table in relevantTables)
        {
            BuildCompactTableInfo(sb, table, dialect);
        }

        // Add relationship hints
        var relationships = ExtractRelationships(context);
        if (relationships.Any())
        {
            sb.AppendLine("\nKEY RELATIONSHIPS:");
            foreach (var rel in relationships.Take(3))
            {
                sb.AppendLine($"- {rel}");
            }
        }

        return sb.ToString();
    }

    private void BuildCompactTableInfo(StringBuilder sb, TableDoc table, string dialect)
    {
        sb.AppendLine($"\nTable: {table.Name}");

        if (!string.IsNullOrEmpty(table.Description))
            sb.AppendLine($"  Description: {table.Description}");

        // Show only key columns (primary keys, foreign keys, and commonly used fields)
        var keyColumns = GetKeyColumns(table);
        if (keyColumns.Any())
        {
            sb.AppendLine("  Key columns:");
            foreach (var col in keyColumns.Take(4)) // Limit to 4 key columns
            {
                var colType = SimplifyDataType(col.DataType, dialect);
                var nullable = col.Nullable ? "NULL" : "NOT NULL";
                var desc = !string.IsNullOrEmpty(col.Description) ? $" - {col.Description}" : "";
                sb.AppendLine($"    {col.Name}: {colType} {nullable}{desc}");
            }
        }

        // Show indexes/keys
        if (table.PrimaryKey?.Count > 0)
            sb.AppendLine($"  Primary Key: {string.Join(", ", table.PrimaryKey)}");
    }

    private List<ColumnDoc> GetKeyColumns(TableDoc table)
    {
        var keyCols = new List<ColumnDoc>();

        // Primary key columns
        if (table.PrimaryKey?.Count > 0)
        {
            keyCols.AddRange(table.Columns.Where(c => table.PrimaryKey.Contains(c.Name)));
        }

        // Foreign key columns
        if (table.ForeignKeys?.Count > 0)
        {
            keyCols.AddRange(table.Columns.Where(c => table.ForeignKeys.Any(fk => fk.Column == c.Name)));
        }

        // Add common query columns (based on naming patterns)
        var commonPatterns = new[] { "name", "title", "status", "created", "updated", "date", "email" };
        keyCols.AddRange(table.Columns.Where(c =>
            commonPatterns.Any(pattern => c.Name.ToLower().Contains(pattern)) &&
            !keyCols.Contains(c)));

        return keyCols.Distinct().ToList();
    }

    private List<string> ExtractRelationships(SchemaContext context)
    {
        var relationships = new List<string>();

        foreach (var table in context.Tables)
        {
            if (table.ForeignKeys?.Count > 0)
            {
                foreach (var fk in table.ForeignKeys)
                {
                    relationships.Add($"{table.Name}.{fk.Column} → {fk.RefTable}.{fk.RefColumn}");
                }
            }
        }

        return relationships;
    }

    private string BuildQueryGuidance(string userQuestion, SchemaContext schemaContext)
    {
        var relevantTables = IdentifyRelevantTables(userQuestion, schemaContext);

        var sb = new StringBuilder();
        sb.AppendLine("QUERY GUIDANCE:");

        // Suggest relevant tables
        if (relevantTables.Any())
        {
            sb.AppendLine($"Most relevant tables: {string.Join(", ", relevantTables)}");
        }

        // Add query-specific hints based on question analysis
        if (userQuestion.ToLower().Contains("top") || userQuestion.ToLower().Contains("limit"))
        {
            sb.AppendLine("- Add ORDER BY and LIMIT for top-N queries");
        }

        if (userQuestion.ToLower().Contains("last") || userQuestion.ToLower().Contains("recent"))
        {
            sb.AppendLine("- Add date filtering for recent data");
        }

        if (userQuestion.ToLower().Contains("total") || userQuestion.ToLower().Contains("sum"))
        {
            sb.AppendLine("- Consider aggregation functions (SUM, COUNT, etc.)");
        }

        if (userQuestion.ToLower().Contains("join") || relevantTables.Count > 1)
        {
            sb.AppendLine("- Use appropriate JOINs based on foreign key relationships");
        }

        return sb.ToString();
    }

    private List<string> IdentifyRelevantTables(string question, SchemaContext schemaContext)
    {
        var questionLower = question.ToLower();
        var relevantTables = new List<string>();

        foreach (var table in schemaContext.Tables)
        {
            // Check table name
            if (questionLower.Contains(table.Name.ToLower()))
            {
                relevantTables.Add(table.Name);
                continue;
            }

            // Check table description
            if (!string.IsNullOrEmpty(table.Description) &&
                questionLower.Contains(table.Description.ToLower()))
            {
                relevantTables.Add(table.Name);
                continue;
            }

            // Check column names
            foreach (var column in table.Columns)
            {
                if (questionLower.Contains(column.Name.ToLower()))
                {
                    relevantTables.Add(table.Name);
                    break;
                }
            }
        }

        return relevantTables.Distinct().ToList();
    }

    private string GetDialectSpecificGuidance(string dialect)
    {
        return dialect.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => "POSTGRESQL SPECIFIC:\n- Use $1, $2 for parameters\n- Use ILIKE for case-insensitive matching\n- Support window functions and CTEs",
            "mysql" => "MYSQL SPECIFIC:\n- Use ? for parameters\n- Use LIMIT for result limiting\n- Backticks for reserved words",
            "sqlite" => "SQLITE SPECIFIC:\n- Use ? for parameters\n- Limited ALTER TABLE support\n- PRAGMA commands for metadata",
            "mssql" or "sqlserver" => "SQL SERVER SPECIFIC:\n- Use @p1, @p2 for parameters\n- Use TOP instead of LIMIT\n- Square brackets for identifiers",
            _ => "GENERIC SQL:\n- Use ? for parameters\n- Standard SQL syntax"
        };
    }

    private string GetParameterStyle(string dialect)
    {
        return dialect.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => "$1, $2",
            "mysql" or "sqlite" => "?",
            "mssql" or "sqlserver" => "@p1, @p2",
            _ => "?"
        };
    }

    private string SimplifyDataType(string dataType, string dialect)
    {
        // Simplify complex data types to their essence
        var lowerType = dataType.ToLowerInvariant();

        if (lowerType.Contains("int")) return "INT";
        if (lowerType.Contains("varchar") || lowerType.Contains("text") || lowerType.Contains("char")) return "TEXT";
        if (lowerType.Contains("decimal") || lowerType.Contains("numeric") || lowerType.Contains("money")) return "NUMBER";
        if (lowerType.Contains("date") || lowerType.Contains("time")) return "DATE";
        if (lowerType.Contains("bool")) return "BOOLEAN";
        if (lowerType.Contains("float") || lowerType.Contains("double") || lowerType.Contains("real")) return "FLOAT";

        return dataType.ToUpper();
    }
}