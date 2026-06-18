using System;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace CrestCreates.Aop.Configuration;

public class DistributedCacheConfigurationProvider : IDynamicConfigurationProvider
{
    private readonly IDistributedCache _cache;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DistributedCacheConfigurationProvider(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var bytes = await _cache.GetAsync(key);
        if (bytes == null || bytes.Length == 0)
            return default;

        var json = System.Text.Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    public async Task SetAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await _cache.SetAsync(key, bytes, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        });
    }

    public async Task InvalidateAsync(string key)
    {
        await _cache.RemoveAsync(key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        var bytes = await _cache.GetAsync(key);
        return bytes != null && bytes.Length > 0;
    }
}
