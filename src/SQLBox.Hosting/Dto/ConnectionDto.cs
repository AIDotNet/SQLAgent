namespace SQLBox.Hosting.Dto;

/// <summary>
/// 创建连接请求
/// </summary>
public class CreateConnectionRequest
{
    /// <summary>
    /// 连接名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 数据库类型
    /// </summary>
    public string DatabaseType { get; set; } = string.Empty;
    
    /// <summary>
    /// 连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
    
    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// 更新连接请求
/// </summary>
public class UpdateConnectionRequest
{
    /// <summary>
    /// 连接名称
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// 数据库类型
    /// </summary>
    public string? DatabaseType { get; set; }
    
    /// <summary>
    /// 连接字符串
    /// </summary>
    public string? ConnectionString { get; set; }
    
    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool? IsEnabled { get; set; }
}

/// <summary>
/// 连接响应
/// </summary>
public class ConnectionResponse
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// 连接名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 数据库类型
    /// </summary>
    public string DatabaseType { get; set; } = string.Empty;
    
    /// <summary>
    /// 连接字符串（脱敏）
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
    
    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; }
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 是否已完成表向量索引初始化（该连接是否存在任意索引记录）
    /// </summary>
    public bool VectorIndexInitialized { get; set; }

    /// <summary>
    /// 当前表向量索引记录数量
    /// </summary>
    public int VectorIndexCount { get; set; }
}

/// <summary>
/// 测试连接响应
/// </summary>
public class TestConnectionResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// 消息
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// 耗时(毫秒)
    /// </summary>
    public long ElapsedMs { get; set; }
}
