using Microsoft.AspNetCore.Mvc;
using SQLAgent.Hosting.Dto;
using SQLAgent.Entities;
using SQLAgent.Infrastructure;

namespace SQLAgent.Hosting.Services;

public class ProvidersService(IAIProviderManager providerManager)
{
    /// <summary>
    /// 获取所有 AI 提供商
    /// </summary>
    [HttpGet]
    public async Task<IResult> GetAllAsync()
    {
        var providers = await providerManager.GetAllAsync();
        var outputs = providers.Select(AIProviderOutput.FromEntity).ToList();
        return Results.Ok(outputs);
    }

    /// <summary>
    /// 获取单个 AI 提供商
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IResult> GetAsync(string id)
    {
        var provider = await providerManager.GetAsync(id);
        if (provider == null)
        {
            return Results.NotFound(new { message = $"Provider '{id}' not found" });
        }

        return Results.Ok(AIProviderOutput.FromEntity(provider));
    }

    /// <summary>
    /// 创建 AI 提供商
    /// </summary>
    [HttpPost]
    public async Task<IResult> CreateAsync(AIProviderInput input)
    {
        try
        {
            // 创建时必须提供 API Key
            if (string.IsNullOrWhiteSpace(input.ApiKey))
            {
                return Results.BadRequest(new { message = "API 密钥不能为空" });
            }
            
            if (!Enum.TryParse<AIProviderType>(input.Type, true, out var providerType))
            {
                return Results.BadRequest(new { message = $"Invalid provider type: {input.Type}" });
            }

            var provider = new AIProvider
            {
                Id = Guid.NewGuid().ToString(),
                Name = input.Name,
                Type = providerType,
                Endpoint = input.Endpoint,
                ApiKey = input.ApiKey,
                AvailableModels = input.AvailableModels,
                DefaultModel = input.DefaultModel ?? input.AvailableModels.Split(',').FirstOrDefault()?.Trim(),
                IsEnabled = input.IsEnabled,
                ExtraConfig = input.ExtraConfig,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var created = await providerManager.AddAsync(provider);

            return Results.Created($"/api/providers/{created.Id}", AIProviderOutput.FromEntity(created));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新 AI 提供商
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IResult> UpdateAsync(string id, AIProviderInput input)
    {
        try
        {
            var existing = await providerManager.GetAsync(id);
            if (existing == null)
            {
                return Results.NotFound(new { message = $"Provider '{id}' not found" });
            }

            if (!Enum.TryParse<AIProviderType>(input.Type, true, out var providerType))
            {
                return Results.BadRequest(new { message = $"Invalid provider type: {input.Type}" });
            }

            var updated = existing with
            {
                Name = input.Name,
                Type = providerType,
                Endpoint = input.Endpoint,
                ApiKey = string.IsNullOrWhiteSpace(input.ApiKey) ? existing.ApiKey : input.ApiKey, // 如果新密钥为空，保留原密钥
                AvailableModels = input.AvailableModels,
                DefaultModel = input.DefaultModel ?? input.AvailableModels.Split(',').FirstOrDefault()?.Trim(),
                IsEnabled = input.IsEnabled,
                ExtraConfig = input.ExtraConfig,
                UpdatedAt = DateTime.UtcNow
            };

            var result = await providerManager.UpdateAsync(updated);
            return Results.Ok(AIProviderOutput.FromEntity(result));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 删除 AI 提供商
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IResult> DeleteAsync(string id)
    {
        var deleted = await providerManager.DeleteAsync(id);
        if (!deleted)
        {
            return Results.NotFound(new { message = $"Provider '{id}' not found" });
        }

        return Results.NoContent();
    }

    /// <summary>
    /// 获取提供商的可用模型列表
    /// </summary>
    [HttpGet("{id}/models")]
    public async Task<IResult> GetModelsAsync(string id)
    {
        var provider = await providerManager.GetAsync(id);
        if (provider == null)
        {
            return Results.NotFound(new { message = $"Provider '{id}' not found" });
        }

        var models = provider.AvailableModels
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => new AIModelInfo
            {
                Name = m,
                DisplayName = FormatModelName(m),
                IsDefault = m == provider.DefaultModel
            })
            .ToList();

        return Results.Ok(models);
    }

    private static string FormatModelName(string modelName)
    {
        // 将模型名称格式化为更友好的显示名称
        return modelName switch
        {
            "gpt-4" => "GPT-4",
            "gpt-4-turbo" => "GPT-4 Turbo",
            "gpt-3.5-turbo" => "GPT-3.5 Turbo",
            _ => modelName
        };
    }
}
