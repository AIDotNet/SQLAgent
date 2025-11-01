using SQLBox.Entities;

namespace SQLBox.Infrastructure;

/// <summary>
/// 表向量存储接口，用于持久化和检索 TableDoc 的向量表示
/// Table vector store interface for persisting and retrieving vector representations of TableDoc
/// </summary>
public interface ITableVectorStore
{
    /// <summary>
    /// 保存表的向量表示到向量数据库
    /// Save table vector representation to vector database
    /// </summary>
    /// <param name="connectionId">数据库连接ID</param>
    /// <param name="table">表文档</param>
    /// <param name="metaData"></param>
    /// <param name="ct">取消令牌</param>
    Task SaveTableVectorAsync(string connectionId, TableDoc table,Dictionary<string,string> metaData, CancellationToken ct = default);

    /// <summary>
    /// 批量保存表的向量表示
    /// Batch save table vector representations
    /// </summary>
    Task SaveTableVectorsBatchAsync(string connectionId, IEnumerable<TableDoc> tables,
        Dictionary<string, string> metaData, CancellationToken ct = default);

    /// <summary>
    /// 搜索与查询语义相似的表
    /// Search for tables semantically similar to the query
    /// </summary>
    /// <param name="connectionId">数据库连接ID</param>
    /// <param name="query">查询文本</param>
    /// <param name="topK">返回top-K个最相似的表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>相似的表及其相似度分数</returns>
    Task<IReadOnlyList<(TableDoc Table, double Score)>> SearchSimilarTablesAsync(
        string connectionId,
        string query,
        int topK,
        CancellationToken ct = default);

    /// <summary>
    /// 删除指定连接的所有向量数据
    /// Delete all vector data for the specified connection
    /// </summary>
    Task DeleteConnectionVectorsAsync(string connectionId, CancellationToken ct = default);
    
    /// <summary>
    /// 检查表的向量是否已存在且为最新版本（按标识）
    /// Check if table vector exists and is up-to-date (by identifiers)
    /// </summary>
    Task<bool> IsTableVectorUpToDateAsync(string connectionId, string schema, string tableName, CancellationToken ct = default);

    /// <summary>
    /// 检查表的向量是否已存在且为最新版本（基于 TableDoc 内容）
    /// Check if table vector exists and is up-to-date (based on TableDoc content)
    /// </summary>
    Task<bool> IsTableVectorUpToDateAsync(string connectionId, TableDoc table, CancellationToken ct = default);

    /// <summary>
    /// 检查指定连接是否已有任意表向量
    /// Check whether any table vectors exist for the specified connection
    /// </summary>
    Task<bool> HasConnectionVectorsAsync(string connectionId, CancellationToken ct = default);

    /// <summary>
    /// 统计指定连接的表向量数量
    /// Count table vectors for the specified connection
    /// </summary>
    Task<int> CountConnectionVectorsAsync(string connectionId, CancellationToken ct = default);

    /// <summary>
    /// 获取当前使用的嵌入模型名称
    /// Get the current embedding model name
    /// </summary>
    string EmbeddingModel { get; }
}
