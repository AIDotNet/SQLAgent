using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SQLAgent.Entities;

namespace SQLAgent.Prompts;

public static class SqlGenerationPromptTemplate
{
    public static string BuildPrompt(string userQuestion, string dialect, SchemaContext schemaContext)
    {
        var schemaDescription = BuildSchemaDescription(schemaContext);
        var relationships = BuildRelationshipGraph(schemaContext);
        var rules = BuildSqlRules(dialect, schemaContext);
        var examples = BuildExamples(dialect);
        var dialectFeatures = BuildDialectSpecificFeatures(dialect);
        var responseFormat = BuildResponseFormat(dialect);

        return $"""
            # ROLE AND EXPERTISE
            You are an elite SQL query generator with mastery in database design, query optimization, and {dialect} dialect specifics.
            Your expertise includes: schema analysis, join optimization, index-aware queries, and security-first parameterization.

            # DATABASE SCHEMA CONTEXT
            {schemaDescription}

            # TABLE RELATIONSHIPS AND FOREIGN KEYS
            {relationships}

            # SQL GENERATION FRAMEWORK
            {rules}

            # DIALECT-SPECIFIC FEATURES ({dialect})
            {dialectFeatures}

            # QUERY CONSTRUCTION METHODOLOGY
            Follow this systematic approach (Chain-of-Thought):

            STEP 1 - INTENT ANALYSIS:
            - Parse the user's question to understand the core intent
            - Identify key entities, attributes, filters, and aggregations mentioned
            - Detect ambiguities that need clarification
            - Determine the expected result structure

            STEP 2 - SCHEMA MAPPING:
            - Map user's natural language terms to actual table/column names
            - Identify all tables needed based on requested data
            - Consider table aliases and alternate names
            - Verify all required columns exist in the schema

            STEP 3 - JOIN PATH ANALYSIS:
            - Determine the optimal join path between identified tables
            - Use foreign key relationships as primary join criteria
            - Prefer INNER JOIN unless LEFT/RIGHT JOIN is explicitly needed
            - Avoid unnecessary cartesian products

            STEP 4 - QUERY CONSTRUCTION:
            - Build SELECT clause with explicit column names (never use *)
            - Construct FROM clause with primary table
            - Add JOIN clauses following the relationship graph
            - Build WHERE clause with parameterized filters
            - Add GROUP BY for aggregations
            - Include HAVING for post-aggregation filters
            - Add ORDER BY for logical sorting
            - Apply LIMIT for large result sets (default: 100 for unbounded queries)

            STEP 5 - OPTIMIZATION CHECK:
            - Verify query uses available indexes (when known)
            - Ensure parameterization prevents SQL injection
            - Check for unnecessary subqueries
            - Validate join conditions use indexed columns
            - Consider query performance implications

            STEP 6 - VALIDATION:
            - Confirm all table names match schema exactly
            - Verify all column names are spelled correctly
            - Check data types are compatible with operators
            - Ensure parameter placeholders match dialect syntax
            - Validate JSON output structure

            # REFERENCE EXAMPLES
            {examples}

            # SECURITY AND BEST PRACTICES
            ⚠️ CRITICAL SECURITY RULES:
            - NEVER embed user input directly in SQL (always use params)
            - Parameterize ALL literal values: strings, numbers, dates, booleans
            - Validate data types match column definitions
            - Use prepared statement placeholders per dialect convention
            - Do NOT generate INSERT, UPDATE, DELETE, DROP, or DDL statements
            - Restrict to SELECT queries only

            🎯 QUALITY STANDARDS:
            - Explicit column selection (avoid SELECT *)
            - Meaningful table aliases (short but clear)
            - Proper NULL handling (IS NULL, IS NOT NULL, COALESCE)
            - Case-sensitivity awareness based on database collation
            - Use DISTINCT only when duplicates are genuinely possible
            - Include ORDER BY for predictable results
            - Apply LIMIT to prevent excessive data retrieval

            ⚡ PERFORMANCE OPTIMIZATION:
            - Join on indexed columns (typically primary/foreign keys)
            - Filter early in WHERE clause
            - Use EXISTS instead of IN for subqueries when appropriate
            - Avoid functions on indexed columns in WHERE clause
            - Consider covering indexes for selected columns
            - Use appropriate join types (INNER vs OUTER)

            # OUTPUT FORMAT SPECIFICATION
            {responseFormat}

            # USER QUESTION
            ```
            {userQuestion}
            ```

            # GENERATION INSTRUCTIONS
            Now, following the 6-step methodology above, analyze the question and generate the optimized SQL query.
            Think through each step systematically, then output ONLY the valid JSON response (no markdown, no explanations outside JSON).
            
            Generate the SQL query response:
            """;
    }

    private static string BuildSchemaDescription(SchemaContext context)
    {
        if (context.Tables == null || context.Tables.Count == 0)
        {
            return "⚠️ No table schema information available.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"📊 Database contains {context.Tables.Count} table(s)");
        sb.AppendLine();

        foreach (var table in context.Tables.OrderBy(t => t.Name))
        {
            sb.AppendLine($"## TABLE: {table.Name}");

            if (!string.IsNullOrEmpty(table.Schema))
                sb.AppendLine($"   Schema: {table.Schema}");

            if (!string.IsNullOrEmpty(table.Description))
                sb.AppendLine($"   📝 Purpose: {table.Description}");

            if (table.Aliases?.Length > 0)
                sb.AppendLine($"   🏷️  Aliases: {string.Join(", ", table.Aliases)}");

            // Identify key columns for better context
            var pkColumns = table.PrimaryKey ?? new List<string>();
            var fkColumns = table.ForeignKeys?.Select(fk => fk.Column).ToHashSet() ?? new HashSet<string>();

            sb.AppendLine($"   Columns ({table.Columns.Count}):");
            foreach (var column in table.Columns.OrderBy(c => c.Name))
            {
                var isPk = pkColumns.Contains(column.Name);
                var isFk = fkColumns.Contains(column.Name);
                var keyIndicator = isPk ? " [PRIMARY KEY]" : (isFk ? " [FOREIGN KEY]" : "");
                var nullableIndicator = column.Nullable ? "NULL" : "NOT NULL";
                var defaultInfo = !string.IsNullOrEmpty(column.Default) ? $" DEFAULT {column.Default}" : "";
                var description = !string.IsNullOrEmpty(column.Description) ? $"\n        ℹ️  {column.Description}" : "";
                var aliases = column.Aliases?.Length > 0 ? $"\n        aka: {string.Join(", ", column.Aliases)}" : "";

                sb.AppendLine($"      • {column.Name}: {column.DataType} {nullableIndicator}{defaultInfo}{keyIndicator}{description}{aliases}");
            }

            if (table.PrimaryKey?.Count > 0)
            {
                sb.AppendLine($"   🔑 Primary Key: {string.Join(", ", table.PrimaryKey)}");
            }

            if (table.ForeignKeys?.Count > 0)
            {
                sb.AppendLine("   🔗 Foreign Keys:");
                foreach (var fk in table.ForeignKeys)
                {
                    sb.AppendLine($"      → {fk.Column} references {fk.RefTable}.{fk.RefColumn}");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static string BuildRelationshipGraph(SchemaContext context)
    {
        if (context.Tables == null || context.Tables.Count == 0)
        {
            return "No relationships defined.";
        }

        var sb = new StringBuilder();
        var hasRelationships = false;

        sb.AppendLine("Table Relationship Map:");
        sb.AppendLine();

        foreach (var table in context.Tables.OrderBy(t => t.Name))
        {
            if (table.ForeignKeys?.Count > 0)
            {
                hasRelationships = true;
                foreach (var fk in table.ForeignKeys)
                {
                    sb.AppendLine($"  {table.Name}.{fk.Column} ──→ {fk.RefTable}.{fk.RefColumn}");
                    sb.AppendLine($"    JOIN: {table.Name} t1 INNER JOIN {fk.RefTable} t2 ON t1.{fk.Column} = t2.{fk.RefColumn}");
                }
            }
        }

        if (!hasRelationships)
        {
            sb.AppendLine("  No explicit foreign key relationships defined.");
            sb.AppendLine("  You may need to infer relationships from column naming patterns (e.g., user_id, order_id).");
        }

        return sb.ToString().Trim();
    }

    private static string BuildSqlRules(string dialect, SchemaContext context)
    {
        var rules = new List<string>
        {
            $"🎯 Target SQL Dialect: {dialect}",
            "",
            "QUERY SCOPE:",
            "  • Generate SELECT queries ONLY",
            "  • NEVER create INSERT, UPDATE, DELETE, DROP, TRUNCATE, or ALTER statements",
            "  • NEVER execute administrative or schema modification commands",
            "",
            "COLUMN SELECTION:",
            "  • Use explicit column names (never SELECT *)",
            "  • Select only columns needed to answer the question",
            "  • Use qualified names (table.column) in multi-table queries",
            "  • Apply appropriate aliases for computed columns",
            "",
            "FILTERING & CONDITIONS:",
            "  • Add WHERE clauses to limit result scope",
            "  • Parameterize ALL literal values (strings, numbers, dates, booleans)",
            "  • Use proper NULL handling: IS NULL / IS NOT NULL / COALESCE",
            "  • Apply date range filters for time-based queries",
            "  • Use BETWEEN for numeric/date ranges",
            "",
            "JOINS & RELATIONSHIPS:",
            "  • Use INNER JOIN by default unless outer join is explicitly needed",
            "  • Follow foreign key relationships for join conditions",
            "  • Use table aliases for readability (e.g., u, o, p)",
            "  • Join on indexed columns (typically PK/FK)",
            "  • Avoid cartesian products (always specify ON condition)",
            "",
            "AGGREGATION:",
            "  • Use GROUP BY for aggregate functions (COUNT, SUM, AVG, MAX, MIN)",
            "  • Include all non-aggregated columns in GROUP BY",
            "  • Use HAVING for filtering aggregated results",
            "  • Consider DISTINCT only when duplicates are genuinely expected",
            "",
            "SORTING & LIMITS:",
            "  • Include ORDER BY for predictable result ordering",
            "  • Sort by meaningful columns (dates, names, counts)",
            "  • Use DESC for most recent/largest first",
            "  • Apply LIMIT (default 100) for unbounded result sets",
            "  • Use pagination patterns (LIMIT + OFFSET) for large datasets",
            "",
            "PARAMETERIZATION:",
            $"  • Use {GetPlaceholderStyle(dialect)} placeholder syntax",
            "  • Create params object with properly named parameters",
            "  • Ensure parameter types match column data types",
            "  • Never concatenate user input into SQL strings",
            "",
            "PERFORMANCE:",
            "  • Filter before joining when possible",
            "  • Use indexes (typically on PK, FK, and common filter columns)",
            "  • Avoid functions on indexed columns in WHERE (e.g., UPPER(column))",
            "  • Consider query cost for large tables",
            "  • Limit column selection to necessary fields"
        };

        // Add dialect-specific rules
        switch (dialect.ToLowerInvariant())
        {
            case "postgres":
            case "postgresql":
                rules.Add("");
                rules.Add("POSTGRESQL-SPECIFIC:");
                rules.Add("  • Use $1, $2, $n for parameter placeholders");
                rules.Add("  • Use ILIKE for case-insensitive string matching");
                rules.Add("  • Leverage ARRAY types and operators when appropriate");
                rules.Add("  • Use :: for type casting (e.g., '2024-01-01'::date)");
                rules.Add("  • Consider JSONB operators for JSON data");
                rules.Add("  • Use LIMIT and OFFSET for pagination");
                break;
            case "mysql":
                rules.Add("");
                rules.Add("MYSQL-SPECIFIC:");
                rules.Add("  • Use ? for parameter placeholders (positional)");
                rules.Add("  • Use LIKE for string matching (case-insensitive by default)");
                rules.Add("  • Use COLLATE utf8mb4_bin for case-sensitive comparison");
                rules.Add("  • Use LIMIT with optional OFFSET for pagination");
                rules.Add("  • Consider backticks for reserved word column names");
                break;
            case "sqlite":
                rules.Add("");
                rules.Add("SQLITE-SPECIFIC:");
                rules.Add("  • Use ? for parameter placeholders (positional)");
                rules.Add("  • Use LIKE for string matching (case-insensitive by default)");
                rules.Add("  • Use GLOB for case-sensitive pattern matching");
                rules.Add("  • Limited data types (flexible typing)");
                rules.Add("  • Use LIMIT with optional OFFSET for pagination");
                break;
            case "mssql":
            case "sqlserver":
                rules.Add("");
                rules.Add("SQL SERVER-SPECIFIC:");
                rules.Add("  • Use @p1, @p2, @pN for parameter placeholders");
                rules.Add("  • Use LIKE for case-insensitive matching (depends on collation)");
                rules.Add("  • Use TOP (n) instead of LIMIT");
                rules.Add("  • Use OFFSET/FETCH for pagination (SQL Server 2012+)");
                rules.Add("  • Consider [brackets] for reserved word identifiers");
                rules.Add("  • Use CAST or CONVERT for type conversions");
                break;
        }

        return string.Join("\n", rules);
    }

    private static string BuildExamples(string dialect)
    {
        var ph1 = GetPlaceholderStyle(dialect);
        var ph2 = GetPlaceholderStyleForParam(dialect, 2);
        var ph3 = GetPlaceholderStyleForParam(dialect, 3);
        var ph4 = GetPlaceholderStyleForParam(dialect, 4);
        var param1 = GetParamName(dialect, 1);
        var param2 = GetParamName(dialect, 2);
        var param3 = GetParamName(dialect, 3);
        var param4 = GetParamName(dialect, 4);
        var limitClause = GetLimitClause(dialect, 100);
        var limit50 = GetLimitClause(dialect, 50);
        var limit10 = GetLimitClause(dialect, 10);

        var sb = new StringBuilder();
        
        sb.AppendLine("## Example 1: Simple Filtered Query");
        sb.AppendLine("Question: \"Show me active users created after January 1, 2024\"");
        sb.AppendLine($"{{\"sql\": \"SELECT id, name, email, created_at FROM users WHERE created_at >= {ph1} AND status = {ph2} ORDER BY created_at DESC {limitClause}\", \"params\": {{\"{param1}\": \"2024-01-01\", \"{param2}\": \"active\"}}, \"tables\": [\"users\"]}}");
        sb.AppendLine();

        sb.AppendLine("## Example 2: JOIN with Multiple Filters");
        sb.AppendLine("Question: \"Get all completed orders from USA customers in the last month\"");
        sb.AppendLine($"{{\"sql\": \"SELECT u.name, u.email, o.order_date, o.total, o.status FROM users u INNER JOIN orders o ON u.id = o.user_id WHERE u.country = {ph1} AND o.status = {ph2} AND o.order_date >= {ph3} ORDER BY o.order_date DESC {limit50}\", \"params\": {{\"{param1}\": \"USA\", \"{param2}\": \"completed\", \"{param3}\": \"2024-09-30\"}}, \"tables\": [\"users\", \"orders\"]}}");
        sb.AppendLine();

        sb.AppendLine("## Example 3: Aggregation with GROUP BY");
        sb.AppendLine("Question: \"Show me the total sales per country\"");
        sb.AppendLine($"{{\"sql\": \"SELECT u.country, COUNT(o.id) as order_count, SUM(o.total) as total_sales FROM users u INNER JOIN orders o ON u.id = o.user_id WHERE o.status = {ph1} GROUP BY u.country ORDER BY total_sales DESC {limit10}\", \"params\": {{\"{param1}\": \"completed\"}}, \"tables\": [\"users\", \"orders\"]}}");
        sb.AppendLine();

        sb.AppendLine("## Example 4: Subquery with Filtering");
        sb.AppendLine("Question: \"Find users who have placed more than 5 orders\"");
        sb.AppendLine($"{{\"sql\": \"SELECT u.id, u.name, u.email, (SELECT COUNT(*) FROM orders o WHERE o.user_id = u.id) as order_count FROM users u WHERE (SELECT COUNT(*) FROM orders o WHERE o.user_id = u.id) > {ph1} ORDER BY order_count DESC {limit50}\", \"params\": {{\"{param1}\": 5}}, \"tables\": [\"users\", \"orders\"]}}");
        sb.AppendLine();

        sb.AppendLine("## Example 5: Multiple JOINs");
        sb.AppendLine("Question: \"Show order details with user and product information\"");
        sb.AppendLine($"{{\"sql\": \"SELECT o.id, o.order_date, u.name as customer_name, p.name as product_name, oi.quantity, oi.price FROM orders o INNER JOIN users u ON o.user_id = u.id INNER JOIN order_items oi ON o.id = oi.order_id INNER JOIN products p ON oi.product_id = p.id WHERE o.status = {ph1} ORDER BY o.order_date DESC {limitClause}\", \"params\": {{\"{param1}\": \"completed\"}}, \"tables\": [\"orders\", \"users\", \"order_items\", \"products\"]}}");
        sb.AppendLine();

        sb.AppendLine("## Example 6: Date Range with BETWEEN");
        sb.AppendLine("Question: \"Get orders placed between two dates\"");
        sb.AppendLine($"{{\"sql\": \"SELECT id, user_id, order_date, total, status FROM orders WHERE order_date BETWEEN {ph1} AND {ph2} ORDER BY order_date DESC {limitClause}\", \"params\": {{\"{param1}\": \"2024-01-01\", \"{param2}\": \"2024-12-31\"}}, \"tables\": [\"orders\"]}}");
        sb.AppendLine();

        sb.AppendLine("## Example 7: NULL Handling");
        sb.AppendLine("Question: \"Find users without a phone number\"");
        sb.AppendLine($"{{\"sql\": \"SELECT id, name, email FROM users WHERE phone IS NULL AND status = {ph1} ORDER BY created_at DESC {limitClause}\", \"params\": {{\"{param1}\": \"active\"}}, \"tables\": [\"users\"]}}");
        sb.AppendLine();

        sb.AppendLine("## Example 8: String Pattern Matching");
        var likeOperator = GetLikeOperator(dialect);
        sb.AppendLine("Question: \"Search for users whose email contains 'gmail'\"");
        sb.AppendLine($"{{\"sql\": \"SELECT id, name, email FROM users WHERE email {likeOperator} {ph1} ORDER BY name {limitClause}\", \"params\": {{\"{param1}\": \"%gmail%\"}}, \"tables\": [\"users\"]}}");
        sb.AppendLine();

        sb.AppendLine("❌ ANTI-EXAMPLE (DO NOT DO THIS):");
        sb.AppendLine("WRONG: {\"sql\": \"SELECT * FROM users\", \"params\": {}, \"tables\": [\"users\"]}");
        sb.AppendLine("Issues: Uses SELECT *, no filtering, no ORDER BY, no LIMIT, no parameterization");
        sb.AppendLine();

        return sb.ToString().Trim();
    }

    private static string BuildDialectSpecificFeatures(string dialect)
    {
        return dialect.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => 
                """
                Advanced PostgreSQL Features Available:
                • Window Functions: ROW_NUMBER(), RANK(), DENSE_RANK(), LAG(), LEAD()
                • Common Table Expressions (CTEs): WITH clause for complex queries
                • Array operations: ANY(), ALL(), array_agg()
                • JSONB operators: ->, ->>, @>, ? for JSON queries
                • String functions: CONCAT_WS(), STRING_AGG(), REGEXP_REPLACE()
                • Date functions: EXTRACT(), DATE_TRUNC(), AGE()
                • Full-text search: to_tsvector(), to_tsquery(), @@
                • Type casting: ::type notation (e.g., '123'::integer)
                • DISTINCT ON: Get first row per group efficiently
                • RETURNING clause: Get values from INSERT/UPDATE (if allowed)
                """,
            
            "mysql" =>
                """
                MySQL-Specific Features Available:
                • Window Functions: ROW_NUMBER(), RANK(), DENSE_RANK() (MySQL 8.0+)
                • Common Table Expressions: WITH clause (MySQL 8.0+)
                • String functions: CONCAT(), SUBSTRING(), REPLACE(), REGEXP_LIKE()
                • Date functions: DATE_FORMAT(), STR_TO_DATE(), TIMESTAMPDIFF()
                • Aggregate functions: GROUP_CONCAT() for string aggregation
                • JSON functions: JSON_EXTRACT(), JSON_CONTAINS() (MySQL 5.7+)
                • Full-text search: MATCH() AGAINST()
                • LIMIT with OFFSET for pagination
                • IF() and CASE for conditional logic
                """,
            
            "sqlite" =>
                """
                SQLite Features Available:
                • Window Functions: ROW_NUMBER(), RANK(), DENSE_RANK() (SQLite 3.25+)
                • Common Table Expressions: WITH clause
                • String functions: SUBSTR(), REPLACE(), TRIM(), LENGTH()
                • Date functions: DATE(), TIME(), DATETIME(), STRFTIME()
                • Aggregate functions: GROUP_CONCAT() for string aggregation
                • JSON functions: json_extract(), json_array_length() (SQLite 3.38+)
                • GLOB operator for case-sensitive pattern matching
                • LIMIT with OFFSET for pagination
                • Simple but efficient for embedded use
                """,
            
            "mssql" or "sqlserver" =>
                """
                SQL Server Features Available:
                • Window Functions: ROW_NUMBER(), RANK(), DENSE_RANK(), LAG(), LEAD()
                • Common Table Expressions (CTEs): WITH clause
                • TOP (n) with optional PERCENT and WITH TIES
                • OFFSET/FETCH for pagination (SQL Server 2012+)
                • String functions: CONCAT(), STRING_AGG(), STRING_SPLIT()
                • Date functions: DATEPART(), DATEADD(), DATEDIFF(), FORMAT()
                • JSON functions: JSON_VALUE(), JSON_QUERY(), OPENJSON() (SQL Server 2016+)
                • Full-text search: CONTAINS(), FREETEXT()
                • CROSS APPLY and OUTER APPLY for lateral joins
                • IIF() and CHOOSE() for conditional logic
                """,
            
            _ => "Standard SQL features available."
        };
    }

    private static string BuildResponseFormat(string dialect)
    {
        var placeholderExample = GetPlaceholderStyle(dialect);
        var paramExample = GetParamName(dialect, 1);
        
        return $$"""
            Return ONLY valid JSON in this exact structure (no markdown, no code blocks, no explanations):

            {
              "sql": "SELECT column1, column2 FROM table WHERE condition = {{placeholderExample}}",
              "params": {
                "{{paramExample}}": "value1"
              },
              "tables": ["table1", "table2"]
            }

            Field Requirements:
            • "sql" (string, required): The complete SQL SELECT query with parameter placeholders
            • "params" (object, required): Key-value pairs mapping parameter names to their values
            • "tables" (array of strings, required): List of all tables referenced in the query

            Type Mapping for params:
            • Strings: Use string values in params object
            • Numbers: Use numeric values (no quotes)
            • Dates: Use ISO 8601 format strings (YYYY-MM-DD or YYYY-MM-DD HH:MM:SS)
            • Booleans: Use true/false (no quotes)
            • NULL: Use null keyword

            Validation Rules:
            ✓ SQL must be a valid SELECT statement
            ✓ All placeholders in SQL must have corresponding params entries
            ✓ All table names in tables array must appear in the SQL
            ✓ Parameter types must match column data types
            ✓ JSON must be properly escaped and formatted
            """;
    }

    private static string GetLikeOperator(string dialect)
    {
        return dialect.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => "ILIKE",
            _ => "LIKE"
        };
    }

    private static string GetLimitClause(string dialect, int limit)
    {
        return dialect.ToLowerInvariant() switch
        {
            "mssql" or "sqlserver" => $"OFFSET 0 ROWS FETCH NEXT {limit} ROWS ONLY",
            _ => $"LIMIT {limit}"
        };
    }

    private static string GetPlaceholderStyleForParam(string dialect, int index)
    {
        return dialect.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => $"${index}",
            "mysql" or "sqlite" => "?",
            "mssql" or "sqlserver" => $"@p{index}",
            _ => "?"
        };
    }

    private static string GetPlaceholderStyle(string dialect)
    {
        return dialect.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => "$1",
            "mysql" or "sqlite" => "?",
            "mssql" or "sqlserver" => "@p1",
            _ => "?"
        };
    }

    private static string GetParamName(string dialect, int index)
    {
        return dialect.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => $"${index}",
            "mysql" or "sqlite" => $"param{index}",
            "mssql" or "sqlserver" => $"@p{index}",
            _ => $"param{index}"
        };
    }
}