using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SQLAgent.Entities;

namespace SQLAgent.Infrastructure.Defaults;

/// <summary>
/// 内存数据库连接管理器的默认实现（支持可选的 JSON 文件持久化）
/// Default in-memory implementation with optional JSON persistence
/// </summary>
public sealed class InMemoryDatabaseConnectionManager : IDatabaseConnectionManager
{
    private readonly ConcurrentDictionary<string, DatabaseConnection> _connections = new();
    private readonly string? _filePath;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

    /// <summary>
    /// 纯内存构造（不持久化）
    /// </summary>
    public InMemoryDatabaseConnectionManager() { }

    /// <summary>
    /// 指定 JSON 路径的构造（启用持久化）
    /// </summary>
    public InMemoryDatabaseConnectionManager(string filePath)
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
            var list = JsonSerializer.Deserialize<List<DatabaseConnection>>(json) ?? new List<DatabaseConnection>();
            _connections.Clear();
            foreach (var c in list)
            {
                if (!string.IsNullOrWhiteSpace(c.Id))
                {
                    _connections[c.Id] = c;
                }
            }
        }
        catch
        {
            // 忽略读取失败，保持空数据
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_filePath)) return;

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var list = _connections.Values
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var json = JsonSerializer.Serialize(list, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DatabaseConnection> AddConnectionAsync(DatabaseConnection connection, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connection.Id))
        {
            throw new ArgumentException("Connection ID cannot be empty", nameof(connection));
        }

        if (!_connections.TryAdd(connection.Id, connection))
        {
            throw new InvalidOperationException($"Connection with ID '{connection.Id}' already exists");
        }

        await PersistAsync(cancellationToken);
        return connection;
    }

    /// <inheritdoc />
    public async Task<DatabaseConnection> UpdateConnectionAsync(DatabaseConnection connection, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connection.Id))
        {
            throw new ArgumentException("Connection ID cannot be empty", nameof(connection));
        }

        if (!_connections.ContainsKey(connection.Id))
        {
            throw new InvalidOperationException($"Connection with ID '{connection.Id}' does not exist");
        }

        var updatedConnection = new DatabaseConnection
        {
            Id = connection.Id,
            Name = connection.Name,
            DatabaseType = connection.DatabaseType,
            ConnectionString = connection.ConnectionString,
            Description = connection.Description,
            CreatedAt = connection.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            IsEnabled = connection.IsEnabled,
            Metadata = connection.Metadata
        };
        _connections[connection.Id] = updatedConnection;

        await PersistAsync(cancellationToken);
        return updatedConnection;
    }

    /// <inheritdoc />
    public async Task DeleteConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            throw new ArgumentException("Connection ID cannot be empty", nameof(connectionId));
        }

        _connections.TryRemove(connectionId, out _);
        await PersistAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<DatabaseConnection?> GetConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            throw new ArgumentException("Connection ID cannot be empty", nameof(connectionId));
        }

        _connections.TryGetValue(connectionId, out var connection);
        return Task.FromResult(connection);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DatabaseConnection>> GetAllConnectionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var connections = _connections.Values
            .Where(c => includeDisabled || c.IsEnabled)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<DatabaseConnection>>(connections);
    }

    /// <inheritdoc />
    public Task<bool> TestConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        // 简单实现：仅检查连接是否存在
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return Task.FromResult(false);
        }

        var exists = _connections.ContainsKey(connectionId);
        return Task.FromResult(exists);
    }
}
