using System;
using System.Threading;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Embeddings;
using System.ClientModel;

namespace SQLAgent.Infrastructure;

// OpenAI Client based embedder (no reflection). Requires `OpenAI` package.
public sealed class OpenAIEmbedder : IEmbedder
{
    private readonly EmbeddingClient _client;
    public string Model { get; }

    public OpenAIEmbedder(EmbeddingClient client, string model)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Model is required", nameof(model));
        Model = model;
    }

    public OpenAIEmbedder(string apiKey, string model, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API key is required", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Model is required", nameof(model));
        Model = model;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _client = new EmbeddingClient(model, apiKey);
        }
        else
        {
            _client = new EmbeddingClient(model, new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();
        var res = await _client.GenerateEmbeddingAsync(text, new EmbeddingGenerationOptions()
        {
            
        }, ct);
        var mem = res?.Value?.ToFloats() ?? ReadOnlyMemory<float>.Empty;
        return mem.ToArray();
    }
}
