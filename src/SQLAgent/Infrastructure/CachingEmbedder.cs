namespace SQLAgent.Infrastructure;

public sealed class CachingEmbedder : IEmbedder
{
    private readonly IEmbedder _inner;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, float[]> _cache = new();
    public string Model => $"cache({_inner.Model})";
    public CachingEmbedder(IEmbedder inner) => _inner = inner;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return await _inner.EmbedAsync(text, ct);
        if (_cache.TryGetValue(text, out var cached)) return cached;
        var v = await _inner.EmbedAsync(text, ct);
        _cache[text] = v;
        return v;
    }
}