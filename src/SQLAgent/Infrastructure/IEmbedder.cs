namespace SQLAgent.Infrastructure;

public interface IEmbedder
{
    string Model { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}