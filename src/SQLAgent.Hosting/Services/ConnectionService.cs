using Microsoft.AspNetCore.Mvc;
using SQLAgent.Entities;
using SQLAgent.Infrastructure;
using System.Diagnostics;
using SQLAgent.Hosting.Dto;

namespace SQLAgent.Hosting.Services;

public class ConnectionService
{
    private readonly IDatabaseConnectionManager _connectionManager;

    public ConnectionService(IDatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// 获取所有连接
    /// </summary>
    [HttpGet("")]
    public async Task<IResult> GetAllAsync(bool includeDisabled = false)
    {
        var connections = await _connectionManager.GetAllConnectionsAsync(includeDisabled);
        var response = connections.Select(MapToResponse).ToList();
        return Results.Ok(response);
    }

    /// <summary>
    /// 获取单个连接
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IResult> GetByIdAsync(string id)
    {
        var connection = await _connectionManager.GetConnectionAsync(id);
        if (connection == null)
        {
            return Results.NotFound(new { message = $"Connection '{id}' not found" });
        }

        return Results.Ok(MapToResponse(connection));
    }

    /// <summary>
    /// 创建连接
    /// </summary>
    [HttpPost("")]
    public async Task<IResult> CreateAsync(CreateConnectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { message = "Name is required" });
        }

        if (string.IsNullOrWhiteSpace(request.DatabaseType))
        {
            return Results.BadRequest(new { message = "DatabaseType is required" });
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return Results.BadRequest(new { message = "ConnectionString is required" });
        }

        var connection = new DatabaseConnection
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            DatabaseType = request.DatabaseType.ToLowerInvariant(),
            ConnectionString = request.ConnectionString,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            IsEnabled = true
        };

        var created = await _connectionManager.AddConnectionAsync(connection);
        return Results.Created($"/api/connections/{created.Id}", MapToResponse(created));
    }

    /// <summary>
    /// 更新连接
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IResult> UpdateAsync(string id, UpdateConnectionRequest request)
    {
        var existing = await _connectionManager.GetConnectionAsync(id);
        if (existing == null)
        {
            return Results.NotFound(new { message = $"Connection '{id}' not found" });
        }

        var updated = new DatabaseConnection
        {
            Id = existing.Id,
            Name = request.Name ?? existing.Name,
            DatabaseType = request.DatabaseType?.ToLowerInvariant() ?? existing.DatabaseType,
            ConnectionString = request.ConnectionString ?? existing.ConnectionString,
            Description = request.Description ?? existing.Description,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            IsEnabled = request.IsEnabled ?? existing.IsEnabled,
            Metadata = existing.Metadata
        };

        var result = await _connectionManager.UpdateConnectionAsync(updated);
        return Results.Ok(MapToResponse(result));
    }

    /// <summary>
    /// 删除连接
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IResult> DeleteAsync(string id)
    {
        var existing = await _connectionManager.GetConnectionAsync(id);
        if (existing == null)
        {
            return Results.NotFound(new { message = $"Connection '{id}' not found" });
        }

        await _connectionManager.DeleteConnectionAsync(id);
        return Results.NoContent();
    }

    /// <summary>
    /// 测试连接
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<IResult> TestAsync(string id)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var success = await _connectionManager.TestConnectionAsync(id);
            sw.Stop();

            var response = new TestConnectionResponse
            {
                Success = success,
                Message = success ? "Connection successful" : "Connection failed",
                ElapsedMs = sw.ElapsedMilliseconds
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            sw.Stop();

            var response = new TestConnectionResponse
            {
                Success = false,
                Message = ex.Message,
                ElapsedMs = sw.ElapsedMilliseconds
            };

            return Results.Ok(response);
        }
    }

    private static ConnectionResponse MapToResponse(DatabaseConnection connection)
    {
        return new ConnectionResponse
        {
            Id = connection.Id,
            Name = connection.Name,
            DatabaseType = connection.DatabaseType,
            ConnectionString = MaskConnectionString(connection.ConnectionString),
            Description = connection.Description,
            IsEnabled = connection.IsEnabled,
            CreatedAt = connection.CreatedAt,
            UpdatedAt = connection.UpdatedAt
        };
    }

    private static string MaskConnectionString(string connectionString)
    {
        // 简单的脱敏处理，隐藏密码
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        var parts = connectionString.Split(';');
        var masked = new List<string>();

        foreach (var part in parts)
        {
            if (part.Trim().StartsWith("Password=", StringComparison.OrdinalIgnoreCase) ||
                part.Trim().StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
            {
                masked.Add("Password=******");
            }
            else
            {
                masked.Add(part);
            }
        }

        return string.Join(";", masked);
    }
}