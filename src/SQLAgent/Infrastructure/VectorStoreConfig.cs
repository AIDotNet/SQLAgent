using SQLAgent.Entities;

namespace SQLAgent.Infrastructure;

/// <summary>
/// 向量存储配置
/// Vector store configuration
/// </summary>
public sealed class VectorStoreConfig
{
    /// <summary>
    /// 向量存储提供者类型
    /// Vector store provider type
    /// </summary>
    public VectorStoreProvider Provider { get; set; } = VectorStoreProvider.SqliteVec;

    /// <summary>
    /// 连接字符串或数据库路径
    /// Connection string or database path
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=vectors.db";

    /// <summary>
    /// 集合名称（用于某些向量数据库）
    /// Collection name (for certain vector databases)
    /// </summary>
    public string CollectionName { get; set; } = "table_vectors";

    /// <summary>
    /// 向量维度（由嵌入模型决定，运行时自动检测）
    /// Vector dimensions (determined by embedding model, auto-detected at runtime)
    /// </summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// 距离度量方式
    /// Distance metric
    /// </summary>
    public DistanceMetric DistanceMetric { get; set; } = DistanceMetric.Cosine;

    /// <summary>
    /// 是否在启动时自动创建集合/表
    /// Whether to auto-create collection/table on startup
    /// </summary>
    public bool AutoCreateCollection { get; set; } = true;

    /// <summary>
    /// 向量缓存过期时间（null表示永不过期）
    /// Vector cache expiration time (null means never expire)
    /// </summary>
    public TimeSpan? CacheExpiration { get; set; }
}

/// <summary>
/// 向量存储提供者
/// Vector store provider
/// </summary>
public enum VectorStoreProvider
{
    /// <summary>SQLite with vec extension</summary>
    SqliteVec,
    
    /// <summary>Qdrant vector database</summary>
    Qdrant,
    
    /// <summary>PostgreSQL with pgvector</summary>
    Postgres,
    
    /// <summary>Redis with vector search</summary>
    Redis,
    
    /// <summary>Azure Cosmos DB</summary>
    CosmosDb,
    
    /// <summary>In-memory (for testing)</summary>
    InMemory
}

/// <summary>
/// 距离度量方式
/// Distance metric
/// </summary>
public enum DistanceMetric
{
    /// <summary>余弦相似度 (推荐用于文本嵌入)</summary>
    Cosine,
    
    /// <summary>欧氏距离</summary>
    Euclidean,
    
    /// <summary>点积</summary>
    DotProduct
}
