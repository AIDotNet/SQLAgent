namespace SQLAgent.Entities;

/// <summary>
/// AI 提供商类型
/// </summary>
public enum AIProviderType
{
    /// <summary>
    /// OpenAI 官方端点
    /// </summary>
    OpenAI,
    
    /// <summary>
    /// Azure OpenAI 服务
    /// </summary>
    AzureOpenAI,
    
    /// <summary>
    /// OpenAI 兼容的自定义端点
    /// </summary>
    CustomOpenAI
}

/// <summary>
/// AI 提供商配置
/// </summary>
public record AIProvider
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// 提供商名称
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// 提供商类型
    /// </summary>
    public AIProviderType Type { get; init; }
    
    /// <summary>
    /// API 端点（可选，OpenAI 使用默认端点）
    /// </summary>
    public string? Endpoint { get; init; }
    
    /// <summary>
    /// API 密钥
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;
    
    /// <summary>
    /// 可用的模型列表（逗号分隔）
    /// </summary>
    public string AvailableModels { get; init; } = string.Empty;
    
    /// <summary>
    /// 默认模型
    /// </summary>
    public string? DefaultModel { get; init; }
    
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; init; } = true;
    
    /// <summary>
    /// 附加配置（JSON 格式）
    /// </summary>
    public string? ExtraConfig { get; init; }
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
