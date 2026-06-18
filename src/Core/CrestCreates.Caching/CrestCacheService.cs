using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching.Abstractions;

namespace CrestCreates.Caching;

public class CrestCacheService : ICrestCacheService
{
    private readonly ICrestCache _crestCache;
    private readonly ICrestCacheKeyGenerator _keyGenerator;

    public CrestCacheService(ICrestCache crestCache, ICrestCacheKeyGenerator keyGenerator)
    {
        _crestCache = crestCache;
        _keyGenerator = keyGenerator;
    }

    public async Task<T?> GetAsync<T>(string prefix, params object[] parts)
    {
        var key = _keyGenerator.GenerateKey(prefix, parts);
        return await _crestCache.GetAsync<T>(key);
    }

    public async Task<T?> GetAsync<T>(string prefix, string? tenantId, params object[] parts)
    {
        var key = _keyGenerator.GenerateTenantKey(prefix, tenantId, parts);
        return await _crestCache.GetAsync<T>(key);
    }

    public async Task SetAsync<T>(string prefix, T value, params object[] parts)
    {
        var key = _keyGenerator.GenerateKey(prefix, parts);
        await _crestCache.SetAsync(key, value);
    }

    public async Task SetAsync<T>(string prefix, T value, TimeSpan expiration, params object[] parts)
    {
        var key = _keyGenerator.GenerateKey(prefix, parts);
        await _crestCache.SetAsync(key, value, expiration);
    }

    public async Task SetAsync<T>(string prefix, string? tenantId, T value, params object[] parts)
    {
        var key = _keyGenerator.GenerateTenantKey(prefix, tenantId, parts);
        await _crestCache.SetAsync(key, value);
    }

    public async Task RemoveAsync(string prefix, params object[] parts)
    {
        var key = _keyGenerator.GenerateKey(prefix, parts);
        await _crestCache.RemoveAsync(key);
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        await _crestCache.RemoveByPatternAsync(pattern);
    }

    public async Task ClearAsync()
    {
        await _crestCache.ClearAsync();
    }

    public async Task<bool> ExistsAsync(string prefix, params object[] parts)
    {
        var key = _keyGenerator.GenerateKey(prefix, parts);
        return await _crestCache.ExistsAsync(key);
    }
}