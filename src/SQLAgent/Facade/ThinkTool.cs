﻿using System.ComponentModel;

namespace SQLAgent.Facade;

public class ThinkTool
{
    [Description(
        """
        Use this tool to engage in deep analysis and structured thinking. It does not fetch new data but logs your reasoning process, which is crucial for complex tasks like database schema analysis. When analyzing a database, consider the following aspects:
        1.  **Table Analysis**: For each table, identify its purpose, primary and foreign keys, and important columns. Analyze column names, data types, and constraints to infer their meaning and relationships.
        2.  **Relationship Mapping**: Map out the relationships between tables. Are they one-to-one, one-to-many, or many-to-many? How are these relationships enforced (e.g., through foreign keys)?
        3.  **Schema Summary**: Synthesize your analysis into a high-level summary of the database schema. Describe the main entities and how they interact.
        4.  **Query Strategy**: Based on your understanding, formulate a strategy for querying the database to answer user questions. Think about which tables to join and what conditions to apply.
        Use this structured thinking process to build a comprehensive mental model of the database before generating SQL queries.
        Args:
        thought: A thought to think about. This can be structured reasoning, step-by-step analysis,
            policy verification, or any other mental process that helps with problem-solving, with a strict requirement to record the source URL immediately after each piece of evidence that could be used as a reference citation for the final action.
        """
    )]
    public static string Think(
        [Description(
            "A thought to think about. This can be structured reasoning, step-by-step analysis, policy verification, or any other mental process that helps with problem-solving.")]
        string thought)
    {
        return thought;
    }
}