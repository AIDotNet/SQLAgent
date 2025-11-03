using SQLAgent.Hosting.Dto;
using SQLAgent.Entities;
using SQLAgent.Facade;
using SQLAgent.Infrastructure;
using SQLAgent.Infrastructure.Defaults;

namespace SQLAgent.Hosting.Services;

/// <summary>
/// 向量索引服务
/// </summary>
public class VectorIndexService
{
    private readonly IDatabaseConnectionManager _connMgr;
    private readonly IAIProviderManager _providerMgr;
    private readonly SystemSettings _settings;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _indexBuildLocks;

    public VectorIndexService(
        IDatabaseConnectionManager connMgr, 
        IAIProviderManager providerMgr, 
        SystemSettings settings)
    {
        _connMgr = connMgr;
        _providerMgr = providerMgr;
        _settings = settings;
        _indexBuildLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>();
    }

    /// <summary>
    /// 初始化（全量重建）指定连接的表向量索引
    /// </summary>
    public async Task<IResult> InitializeIndexAsync(string connectionId)
    {
        var connection = await _connMgr.GetConnectionAsync(connectionId);
        if (connection == null)
            return Results.NotFound(new { message = $"Connection '{connectionId}' not found" });

        var (embedder, vecStore) = await CreateVectorComponentsAsync();
        if (embedder == null || vecStore == null)
            return Results.BadRequest(new { message = "Embedding provider is not configured. Please set SystemSettings.EmbeddingProviderId or configure a default provider." });

        ConfigureSqlGen(embedder, vecStore);

        var sem = _indexBuildLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        if (!await sem.WaitAsync(0))
        {
            return Results.Accepted($"/api/connections/{connectionId}/index/init", 
                new { message = "Index initialization in progress" });
        }

        try
        {
            var updated = await SqlGen.InitializeTableVectorIndexAsync(connectionId);
            var total = await vecStore.CountConnectionVectorsAsync(connectionId);
            return Results.Ok(new { initialized = total > 0, updatedCount = updated, totalCount = total });
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// 增量更新指定连接的表向量索引
    /// </summary>
    public async Task<IResult> UpdateIndexAsync(string connectionId)
    {
        var connection = await _connMgr.GetConnectionAsync(connectionId);
        if (connection == null)
            return Results.NotFound(new { message = $"Connection '{connectionId}' not found" });

        var (embedder, vecStore) = await CreateVectorComponentsAsync();
        if (embedder == null || vecStore == null)
            return Results.BadRequest(new { message = "Embedding provider is not configured. Please set SystemSettings.EmbeddingProviderId or configure a default provider." });

        ConfigureSqlGen(embedder, vecStore);

        var sem = _indexBuildLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        if (!await sem.WaitAsync(0))
        {
            return Results.Accepted($"/api/connections/{connectionId}/index/update", 
                new { message = "Index update in progress" });
        }

        try
        {
            var updated = await SqlGen.UpdateTableVectorIndexAsync(connectionId);
            var total = await vecStore.CountConnectionVectorsAsync(connectionId);
            return Results.Ok(new { initialized = total > 0, updatedCount = updated, totalCount = total });
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<(IEmbedder?, ITableVectorStore?)> CreateVectorComponentsAsync()
    {
        var providerId = _settings.EmbeddingProviderId;
        var provider = string.IsNullOrWhiteSpace(providerId)
            ? await _providerMgr.GetDefaultAsync()
            : await _providerMgr.GetAsync(providerId!);

        if (provider == null)
            return (null, null);

        var embedder = new OpenAIEmbedder(provider.ApiKey, _settings.EmbeddingModel, provider.Endpoint);
        var vecConfig = new VectorStoreConfig
        {
            ConnectionString = _settings.VectorDbPath,
            CollectionName = _settings.VectorCollection,
            DistanceMetric = _settings.DistanceMetric,
            AutoCreateCollection = _settings.AutoCreateCollection,
            CacheExpiration = _settings.VectorCacheExpireMinutes.HasValue
                ? TimeSpan.FromMinutes(_settings.VectorCacheExpireMinutes.Value)
                : null
        };
        var vecStore = new SqliteVecTableStore(embedder, vecConfig);

        return (embedder, vecStore);
    }

    private void ConfigureSqlGen(IEmbedder embedder, ITableVectorStore vecStore)
    {
        SqlGen.Configure(b =>
        {
            b.WithConnectionManager(_connMgr);
            b.WithSchemaIndexer(new EmbeddingSchemaIndexer(embedder));
            b.WithTableVectorStore(vecStore);
            b.WithSchemaRetriever(new VectorSchemaRetriever(vecStore));
        });
    }
}
