using System.ComponentModel;
using System.IO;

namespace SQLAgent.Facade;

public class GenerateAgentTool
{
    public string AgentContent = string.Empty;

    [Description(
        """
        Writes the generated Agent configuration content to Agent.md file.

        Usage:
        - This tool should be called when you have completed the Agent configuration generation.
        - The Agent configuration will be directly written to Agent.md file in Markdown format.
        - Ensure the Agent configuration content is complete and correctly formatted before calling this tool.

        Parameters:
        - agentContent: The complete Agent configuration content in Markdown format to be written to the file.
        """)]
    public string WriteAgent(
        [Description("The complete Agent configuration content in Markdown format")]
        string agentContent)
    {
        if (string.IsNullOrWhiteSpace(agentContent))
        {
            return "Error: Agent content cannot be empty.";
        }

        try
        {
            AgentContent = agentContent;

            return "Success: Agent configuration has been written to Agent.md file.";
        }
        catch (Exception ex)
        {
            return $"Error: Failed to write Agent configuration. {ex.Message}";
        }
    }
}