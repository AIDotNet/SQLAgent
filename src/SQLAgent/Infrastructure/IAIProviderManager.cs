using System.IO;
using System.Linq;
using System.Text.Json;
using SQLAgent.Entities;

namespace SQLAgent.Infrastructure;

/// <summary>
/// AI 提供商管理器接口
/// </summary>
public interface IAIProviderManager
{
    Task<IEnumerable<AIProvider>> GetAllAsync(CancellationToken ct = default);
    Task<AIProvider?> GetAsync(string id, CancellationToken ct = default);
    Task<AIProvider> AddAsync(AIProvider provider, CancellationToken ct = default);
    Task<AIProvider> UpdateAsync(AIProvider provider, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    Task<AIProvider?> GetDefaultAsync(CancellationToken ct = default);
}

/// <summary>
/// 内存实现的 AI 提供商管理器（支持可选的 JSON 文件持久化）
/// </summary>
public class InMemoryAIProviderManager : IAIProviderManager
{
    private readonly Dictionary<string, AIProvider> _providers = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string? _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

    /// <summary>
    /// 纯内存构造（不持久化）
    /// </summary>
    public InMemoryAIProviderManager() { }

    /// <summary>
    /// 指定 JSON 路径的构造（启用持久化）
    /// </summary>
    public InMemoryAIProviderManager(string filePath)
    {
        _filePath = filePath;
        LoadFromFile();
    }

    private void LoadFromFile()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_filePath)) return;
            if (!File.Exists(_filePath)) return;

            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<AIProvider>>(json) ?? new List<AIProvider>();
            _providers.Clear();
            foreach (var p in list)
            {
                if (!string.IsNullOrWhiteSpace(p.Id))
                {
                    _providers[p.Id] = p;
                }
            }
        }
        catch
        {
            // 忽略读取失败，保持空数据
        }
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_filePath)) return;

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var list = _providers.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var json = JsonSerializer.Serialize(list, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    public async Task<IEnumerable<AIProvider>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _providers.Values.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AIProvider?> GetAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _providers.TryGetValue(id, out var provider) ? provider : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AIProvider> AddAsync(AIProvider provider, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_providers.ContainsKey(provider.Id))
            {
                throw new InvalidOperationException($"Provider with ID '{provider.Id}' already exists");
            }

            _providers[provider.Id] = provider;
            await PersistAsync(ct);
            return provider;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AIProvider> UpdateAsync(AIProvider provider, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!_providers.ContainsKey(provider.Id))
            {
                throw new InvalidOperationException($"Provider with ID '{provider.Id}' not found");
            }

            var updated = provider with { UpdatedAt = DateTime.UtcNow };
            _providers[provider.Id] = updated;
            await PersistAsync(ct);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var removed = _providers.Remove(id);
            if (removed)
            {
                await PersistAsync(ct);
            }
            return removed;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AIProvider?> GetDefaultAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _providers.Values.FirstOrDefault(p => p.IsEnabled);
        }
        finally
        {
            _lock.Release();
        }
    }
}
