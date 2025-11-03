using System.Threading;
using System.Threading.Tasks;
using SQLAgent.Entities;

namespace SQLAgent.Prompts;

public interface ISqlPromptBuilder
{
    Task<string> BuildPromptAsync(
        string userQuestion,
        string dialect,
        SchemaContext schemaContext,
        bool allowWrite,
        CancellationToken ct = default);

    string BuildPrompt(
        string userQuestion,
        string dialect,
        SchemaContext schemaContext,
        bool allowWrite);
}