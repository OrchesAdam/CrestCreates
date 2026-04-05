using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching.Abstractions;
using StackExchange.Redis;

namespace CrestCreates.Caching;

public class RedisCrestCache : ICrestCache
{
    private readonly IDatabase _database;
    private readonly CacheOptions _options;

    public RedisCrestCache(string connectionString, CacheOptions options)
    {
        var multiplexer = ConnectionMultiplexer.Connect(connectionString);
        _database = multiplexer.GetDatabase();
        _options = options;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = _options.Prefix + key;
        var value = await _database.StringGetAsync(fullKey);
        if (value.IsNull)
        {
            return default;
        }
        return System.Text.Json.JsonSerializer.Deserialize<T>(value.ToString());
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = _options.Prefix + key;
        var jsonValue = System.Text.Json.JsonSerializer.Serialize(value);
        await _database.StringSetAsync(fullKey, jsonValue, expiration ?? _options.DefaultExpiration);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = _options.Prefix + key;
        await _database.KeyDeleteAsync(fullKey);
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPattern = _options.Prefix + pattern;
        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints()[0]);
        await foreach (var key in server.KeysAsync(pattern: fullPattern).WithCancellation(cancellationToken))
        {
            await _database.KeyDeleteAsync(key);
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pattern = _options.Prefix + "*";
        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints()[0]);
        await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(cancellationToken))
        {
            await _database.KeyDeleteAsync(key);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = _options.Prefix + key;
        return await _database.KeyExistsAsync(fullKey);
    }

    public async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pattern = _options.Prefix + "*";
        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints()[0]);
        var count = 0;
        await foreach (var _ in server.KeysAsync(pattern: pattern).WithCancellation(cancellationToken))
        {
            count++;
        }
        return count;
    }
}