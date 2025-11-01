using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using SQLBox.Entities;

namespace SQLBox.Infrastructure;

/// <summary>
/// 基于 Semantic Kernel 的表向量存储实现（Sqlite-Vec）
/// 支持动态向量维度
/// Semantic Kernel-based table vector store implementation (Sqlite-Vec)
/// Supports dynamic vector dimensions
/// </summary>
public sealed class SqliteVecTableStore : ITableVectorStore, IDisposable
{
    private readonly IEmbedder _embedder;
    private readonly VectorStoreConfig _config;
    private readonly SqliteVectorStore _vectorStore;
    private VectorStoreCollection<string, TableVectorRecord>? _collection;
    private int? _detectedDimensions;

    public string EmbeddingModel => _embedder.Model;

    public SqliteVecTableStore(IEmbedder embedder, VectorStoreConfig config)
    {
        _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // 创建 SqliteVectorStore
        _vectorStore = new SqliteVectorStore(_config.ConnectionString);
    }

    /// <summary>
    /// 初始化集合（延迟初始化，首次使用时调用）
    /// Initialize collection (lazy initialization, called on first use)
    /// </summary>
    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_collection != null) return;

        // 如果没有配置维度，先检测一次
        if (!_detectedDimensions.HasValue)
        {
            await DetectDimensionsAsync(ct);
        }

        // 创建集合配置
        var recordDefinition = new VectorStoreCollectionDefinition()
        {
            Properties = new List<VectorStoreProperty>
            {
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("ConnectionId", typeof(string)) { },
                new VectorStoreDataProperty("Schema", typeof(string)) { },
                new VectorStoreDataProperty("TableName", typeof(string)) { },
                new VectorStoreDataProperty("EmbeddingModel", typeof(string)) { },
                new VectorStoreDataProperty("Dimensions", typeof(int)),
                new VectorStoreDataProperty("SearchableText", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreDataProperty("TableMetadata", typeof(string)),
                new VectorStoreDataProperty("CreatedAt", typeof(long)),
                new VectorStoreVectorProperty("Vector", typeof(ReadOnlyMemory<float>),
                    _detectedDimensions ?? _config.Dimensions ?? 1536)
                {
                }
            }
        };

        _collection = _vectorStore.GetCollection<string, TableVectorRecord>(
            _config.CollectionName,
            recordDefinition);

        if (_config.AutoCreateCollection)
        {
            await _collection.EnsureCollectionExistsAsync(ct);
        }
    }

    /// <summary>
    /// 自动检测向量维度
    /// Auto-detect vector dimensions
    /// </summary>
    private async Task DetectDimensionsAsync(CancellationToken ct)
    {
        if (_config.Dimensions.HasValue)
        {
            _detectedDimensions = _config.Dimensions.Value;
            return;
        }

        // 使用一个示例文本检测维度
        var sampleVector = await _embedder.EmbedAsync("sample text for dimension detection", ct);
        _detectedDimensions = sampleVector.Length;
    }

    public async Task SaveTableVectorAsync(string connectionId, TableDoc table,
        Dictionary<string, string> metaData, CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        // 如果表没有向量，先生成
        if (table.Vector == null || table.Vector.Length == 0)
        {
            var text = TableVectorRecord.BuildSearchableText(table);
            table.Vector = await _embedder.EmbedAsync(text, ct);
        }

        var record = new TableVectorRecord
        {
            Id = TableVectorRecord.BuildId(connectionId, table.Schema, table.Name, _embedder.Model),
            ConnectionId = connectionId,
            Schema = table.Schema,
            TableName = table.Name,
            EmbeddingModel = _embedder.Model,
            Dimensions = table.Vector.Length,
            SearchableText = TableVectorRecord.BuildSearchableText(table),
            TableMetadata = TableVectorRecord.SerializeTableMetadata(table),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Vector = new ReadOnlyMemory<float>(table.Vector)
        };

        await _collection!.UpsertAsync(record, ct);
    }

    public async Task SaveTableVectorsBatchAsync(string connectionId, IEnumerable<TableDoc> tables,
        Dictionary<string, string> metaData,
        CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        var records = new List<TableVectorRecord>();

        foreach (var table in tables)
        {
            // 如果表没有向量，先生成
            if (table.Vector == null || table.Vector.Length == 0)
            {
                var text = TableVectorRecord.BuildSearchableText(table);
                table.Vector = await _embedder.EmbedAsync(text, ct);
            }

            var record = new TableVectorRecord
            {
                Id = TableVectorRecord.BuildId(connectionId, table.Schema, table.Name, _embedder.Model),
                ConnectionId = connectionId,
                Schema = table.Schema,
                TableName = table.Name,
                EmbeddingModel = _embedder.Model,
                Dimensions = table.Vector.Length,
                SearchableText = TableVectorRecord.BuildSearchableText(table),
                TableMetadata = TableVectorRecord.SerializeTableMetadata(table),
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Vector = new ReadOnlyMemory<float>(table.Vector)
            };

            records.Add(record);
        }

        await _collection!.UpsertAsync(records, ct);
    }

    public async Task<IReadOnlyList<(TableDoc Table, double Score)>> SearchSimilarTablesAsync(
        string connectionId,
        string query,
        int topK,
        CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        // 生成查询向量
        var queryVector = await _embedder.EmbedAsync(query, ct);

        // 使用 SK 的向量搜索
        var searchOptions = new VectorSearchOptions<TableVectorRecord>
        {
            // 过滤到当前连接且当前嵌入模型，确保多模型并存时检索维度一致
            Filter = record => record.ConnectionId == connectionId && record.EmbeddingModel == _embedder.Model,
        };

        var list = new List<(TableDoc Table, double Score)>();

        await foreach (var result in _collection!.SearchAsync(
                           new ReadOnlyMemory<float>(queryVector),
                           topK,
                           searchOptions,
                           ct))
        {
            if (result?.Record == null) continue;

            var table = TableVectorRecord.DeserializeTableMetadata(
                result.Record.TableMetadata,
                result.Record.Vector);

            var score = result.Score ?? 0.0;
            list.Add((table, score));
        }

        return list;
    }

    public async Task DeleteConnectionVectorsAsync(string connectionId, CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        // 使用 SK 的向量搜索
        var searchOptions = new VectorSearchOptions<TableVectorRecord>
        {
            Filter = record => record.ConnectionId == connectionId,
        };
        // 使用一个零向量进行搜索（只是为了获取所有匹配的记录）
        var zeroVector = new ReadOnlyMemory<float>(new float[_detectedDimensions ?? 1536]);
        var keysToDelete = new List<string>();
        await foreach (var result in _collection!.SearchAsync(zeroVector, 4096, searchOptions, ct))
        {
            if (result?.Record?.Id != null)
            {
                keysToDelete.Add(result.Record.Id);
            }
        }

        foreach (var key in keysToDelete)
        {
            await _collection.DeleteAsync(key, ct);
        }
    }

    // 判断指定连接是否已有任意表向量（存在即返回 true）
    public async Task<bool> HasConnectionVectorsAsync(string connectionId, CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        await foreach (var _ in _collection!.GetAsync(x => x.ConnectionId == connectionId, 1, cancellationToken: ct))
        {
            return true;
        }
        return false;
    }

    // 统计指定连接下表向量数量（用于状态提示）
    public async Task<int> CountConnectionVectorsAsync(string connectionId, CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        var zeroVector = new ReadOnlyMemory<float>(new float[_detectedDimensions ?? 1536]);
        var searchOptions = new VectorSearchOptions<TableVectorRecord>
        {
            Filter = record => record.ConnectionId == connectionId,
        };

        var count = 0;
        await foreach (var _ in _collection!.SearchAsync(zeroVector, 4096, searchOptions, ct))
        {
            count++;
        }
        return count;
    }

    public async Task<bool> IsTableVectorUpToDateAsync(
        string connectionId,
        string schema,
        string tableName,
        CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        var id = TableVectorRecord.BuildId(connectionId, schema, tableName, _embedder.Model);
        await foreach (var record in _collection!.GetAsync(x =>
                           x.ConnectionId == connectionId && x.Schema == schema && x.TableName == tableName, 4096,
                       cancellationToken: ct))
        {
            // 检查缓存是否过期
            if (_config.CacheExpiration.HasValue)
            {
                var createdAt = DateTimeOffset.FromUnixTimeSeconds((long)record.CreatedAt);
                var expirationTime = createdAt + _config.CacheExpiration.Value;
                if (DateTimeOffset.UtcNow > expirationTime)
                {
                    return false;
                }
            }

            // 检查模型是否匹配
            return record.EmbeddingModel == _embedder.Model;
        }

        return false;
    }

    /// <summary>
    /// 基于 TableDoc 内容检查向量是否最新（增量更新入口）
    /// Check up-to-date using current TableDoc content (incremental update entry)
    /// </summary>
    public async Task<bool> IsTableVectorUpToDateAsync(
        string connectionId,
        TableDoc table,
        CancellationToken ct = default)
    {
        // 目前复用基于标识的检查逻辑；后续将改为对比内容哈希。
        return await IsTableVectorUpToDateAsync(connectionId, table.Schema, table.Name, ct);
    }

    public void Dispose()
    {
        _vectorStore?.Dispose();
    }
}