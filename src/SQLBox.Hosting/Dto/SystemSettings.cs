using SQLBox.Infrastructure;

namespace SQLBox.Hosting.Dto;

/// <summary>
/// 系统级设置：索引使用的嵌入提供商/模型、向量库配置、默认对话提供商/模型
/// 提供默认参数以便在未配置的情况下可运行
/// </summary>
public sealed class SystemSettings
{
    // —— 索引（向量化）配置 ——
    /// <summary>用于索引构建的 AI 提供商ID（必须能提供嵌入模型）</summary>
    public string? EmbeddingProviderId { get; set; }

    /// <summary>索引用嵌入模型（如：text-embedding-3-small）</summary>
    public string EmbeddingModel { get; set; } = "text-embedding-ada-002";

    // —— 向量存储（Sqlite-Vec）配置 ——
    /// <summary>Sqlite-Vec 数据库文件路径</summary>
    public string VectorDbPath { get; set; } = "Data Source=vectors.db";

    /// <summary>集合名称（表集合名）</summary>
    public string VectorCollection { get; set; } = "table_vectors";

    /// <summary>距离度量方式（Cosine/Euclidean/DotProduct）</summary>
    public DistanceMetric DistanceMetric { get; set; } = DistanceMetric.Cosine;

    /// <summary>是否启动时自动创建集合（默认 true）</summary>
    public bool AutoCreateCollection { get; set; } = true;

    /// <summary>索引缓存过期时间（分钟，null 表示永不过期）</summary>
    public int? VectorCacheExpireMinutes { get; set; } = null;

    // —— 默认聊天（对话）配置 ——
    /// <summary>默认聊天使用的 AI 提供商ID（可由前端覆盖）</summary>
    public string? DefaultChatProviderId { get; set; }

    /// <summary>默认聊天模型（可由前端覆盖）</summary>
    public string? DefaultChatModel { get; set; }
}