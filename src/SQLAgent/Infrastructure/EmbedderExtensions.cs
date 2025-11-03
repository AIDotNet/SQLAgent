namespace SQLAgent.Infrastructure;

public static class EmbedderExtensions
{
    public static async Task<float[][]> EmbedBatchAsync(this IEmbedder embedder, System.Collections.Generic.IEnumerable<string> texts, int maxDegreeOfParallelism = 4, CancellationToken ct = default)
    {
        var arr = texts.ToArray();
        var results = new float[arr.Length][];
        await System.Threading.Tasks.Parallel.ForEachAsync(Enumerable.Range(0, arr.Length), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism), CancellationToken = ct }, async (i, token) =>
        {
            results[i] = await embedder.EmbedAsync(arr[i], token);
        });
        return results;
    }
}