using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SQLBox.Infrastructure;
using SQLBox.Entities;
using Microsoft.Extensions.AI;
using SQLBox.Prompts;

namespace SQLBox.Infrastructure.Providers.ExtensionsAI;

// Adapter around Microsoft.Extensions.AI abstractions (hard dependency).
public sealed class ExtensionsAiLlmClient : ILlmClient
{
    private readonly IChatClient _chat;
    private readonly ISqlPromptBuilder _promptBuilder;

    public ExtensionsAiLlmClient(IChatClient chatClient) : this(chatClient, new DynamicSqlPromptBuilder()) { }

    public ExtensionsAiLlmClient(IChatClient chatClient, ISqlPromptBuilder promptBuilder)
    {
        _chat = chatClient;
        _promptBuilder = promptBuilder;
    }

    public async Task<GeneratedSql> GenerateAsync(string prompt, string dialect, SchemaContext schemaContext, CancellationToken ct = default)
    {
        // Enhanced method with schema-aware prompt
        var enhancedPrompt = await _promptBuilder.BuildPromptAsync(prompt, dialect, schemaContext,true, ct);

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, "You are an expert SQL query generator. Follow the provided instructions precisely and output valid JSON."),
            new ChatMessage(ChatRole.User, enhancedPrompt)
        };

        var response = await _chat.GetResponseAsync(messages, new ChatOptions()
        {
            MaxOutputTokens = 4096,
            Temperature = 0.1f // Lower temperature for more consistent SQL generation
        }, ct);
        return await ParseResponse(response, ct);
    }

    private async Task<GeneratedSql> ParseResponse(ChatResponse? response, CancellationToken ct)
    {
        var content = response?.Text ?? string.Empty;

        // Try parse JSON; fallback heuristic
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var sql = root.TryGetProperty("sql", out var pSql) ? pSql.GetString() ?? string.Empty : string.Empty;
            var parms = new Dictionary<string, object?>();

            if (root.TryGetProperty("params", out var pParams) && pParams.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in pParams.EnumerateObject())
                    parms[kv.Name] = kv.Value.ToString();
            }

            var tables = Array.Empty<string>();
            if (root.TryGetProperty("tables", out var pTabs) && pTabs.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var v in pTabs.EnumerateArray())
                    list.Add(v.GetString() ?? string.Empty);
                tables = list.ToArray();
            }

            if (!string.IsNullOrWhiteSpace(sql))
                return new GeneratedSql(new[] { sql }, parms, tables);
        }
        catch
        {
            // ignore, try fallback
        }

        // Fallback: extract first SELECT...
        var m = Regex.Match(content, @"(?is)\bselect\b[\s\S]+$");
        var sqlFallback = m.Success ? m.Value.Trim() : "SELECT 1 AS value";
        return new GeneratedSql(new[] { sqlFallback }, new Dictionary<string, object?>(), Array.Empty<string>());
    }
}

// Note: We intentionally do not provide an Extensions.AI embedder adapter to avoid
// reliance on reflection/unstable extension methods across providers. For embeddings,
// you can continue using the built‑in HashingEmbedder or implement IEmbedder to wrap
// your preferred provider in application code.