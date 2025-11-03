using SQLAgent.Entities;
using SQLAgent.Infrastructure;

namespace SQLAgent.Facade;

public class SQLAgentOptions
{
    /// <summary>
    /// AI 模型
    /// </summary>
    public string Model { get; set; }

    /// <summary>
    /// AI 提供商
    /// </summary>
    public AIProviderType AIProvider { get; set; }

    /// <summary>
    /// 终结点
    /// </summary>
    public string Endpoint { get; set; }

    /// <summary>
    /// API 密钥
    /// </summary>
    public string APIKey { get; set; }

    /// <summary>
    /// 向量模型
    /// </summary>
    /// <returns></returns>
    public string EmbeddingModel { get; set; }

    /// <summary>
    /// 使用向量数据库索引
    /// </summary>
    /// <returns></returns>
    public bool UseVectorDatabaseIndex { get; set; } = false;

    /// <summary>
    /// 向量数据库表名
    /// </summary>
    public string DatabaseIndexTable { get; set; }

    /// <summary>
    /// 数据库连接字符串
    /// </summary>
    public string DatabaseIndexConnectionString { get; set; }

    public DatabaseIndexType DatabaseIndexType { get; set; } = DatabaseIndexType.Sqlite;

    /// <summary>
    /// 数据库连接字符串
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>
    /// SQL 类型
    /// </summary>
    public SqlType SqlType { get; set; }

    /// <summary>
    /// AI 最大输出令牌数
    /// </summary>
    public int MaxOutputTokens { get; set; } = 3200;

    /// <summary>
    /// 是否允许写操作
    /// </summary>
    public bool AllowWrite { get; set; } = true;
}