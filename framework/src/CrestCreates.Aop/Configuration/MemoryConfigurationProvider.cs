using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions.Interfaces;

namespace CrestCreates.Aop.Configuration;

public class MemoryConfigurationProvider : IDynamicConfigurationProvider
{
    private readonly ConcurrentDictionary<string, object?> _cache = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<T?> GetAsync<T>(string key)
    {
        if (_cache.TryGetValue(key, out var value))
        {
            if (value is T typedValue)
                return Task.FromResult<T?>(typedValue);

            var json = JsonSerializer.Serialize(value);
            return Task.FromResult(JsonSerializer.Deserialize<T>(json, _jsonOptions));
        }

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value)
    {
        _cache[key] = value;
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string key)
    {
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key)
    {
        return Task.FromResult(_cache.ContainsKey(key));
    }
}
