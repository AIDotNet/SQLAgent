using System.ComponentModel.DataAnnotations;
using SQLBox.Entities;

namespace SQLBox.Hosting.Dto;

/// <summary>
/// AI 提供商输入 DTO
/// </summary>
public record AIProviderInput
{
    [Required(ErrorMessage = "提供商名称不能为空")]
    [StringLength(100, ErrorMessage = "提供商名称不能超过100个字符")]
    public string Name { get; init; } = string.Empty;
    
    [Required(ErrorMessage = "提供商类型不能为空")]
    public string Type { get; init; } = string.Empty;
    
    public string? Endpoint { get; init; }
    
    /// <summary>
    /// API 密钥（创建时必填，更新时可选，留空表示不修改）
    /// </summary>
    public string? ApiKey { get; init; }
    
    [Required(ErrorMessage = "至少需要配置一个模型")]
    public string AvailableModels { get; init; } = string.Empty;
    
    public string? DefaultModel { get; init; }
    
    public bool IsEnabled { get; init; } = true;
    
    public string? ExtraConfig { get; init; }
}

/// <summary>
/// AI 提供商输出 DTO
/// </summary>
public record AIProviderOutput
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? Endpoint { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string[] AvailableModels { get; init; } = Array.Empty<string>();
    public string? DefaultModel { get; init; }
    public bool IsEnabled { get; init; }
    public string? ExtraConfig { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    
    public static AIProviderOutput FromEntity(AIProvider provider)
    {
        return new AIProviderOutput
        {
            Id = provider.Id,
            Name = provider.Name,
            Type = provider.Type.ToString(),
            Endpoint = provider.Endpoint,
            ApiKey = MaskApiKey(provider.ApiKey),
            AvailableModels = provider.AvailableModels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            DefaultModel = provider.DefaultModel,
            IsEnabled = provider.IsEnabled,
            ExtraConfig = provider.ExtraConfig,
            CreatedAt = provider.CreatedAt,
            UpdatedAt = provider.UpdatedAt
        };
    }
    
    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
            return "****";
        
        return apiKey.Substring(0, 4) + "****" + apiKey.Substring(apiKey.Length - 4);
    }
}

/// <summary>
/// AI 模型信息
/// </summary>
public record AIModelInfo
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
}
