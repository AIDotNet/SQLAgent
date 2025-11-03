using System;
using System.Collections.Concurrent;
using SQLAgent.Entities;

namespace SQLAgent.Infrastructure;

public interface ISemanticCache
{
    bool TryGet(string key, out SqlResult result);
    void Set(string key, SqlResult result, TimeSpan? ttl = null);
}

public sealed class InMemorySemanticCache : ISemanticCache
{
    private sealed record Entry(SqlResult Value, DateTimeOffset? ExpireAt);
    private readonly ConcurrentDictionary<string, Entry> _map = new();

    public bool TryGet(string key, out SqlResult result)
    {
        result = default!;
        if (!_map.TryGetValue(key, out var e)) return false;
        if (e.ExpireAt is { } due && due < DateTimeOffset.UtcNow)
        {
            _map.TryRemove(key, out _);
            return false;
        }
        result = e.Value;
        return true;
    }

    public void Set(string key, SqlResult result, TimeSpan? ttl = null)
    {
        _map[key] = new Entry(result, ttl.HasValue ? DateTimeOffset.UtcNow + ttl.Value : null);
    }
}

