using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching.Abstractions;
using StackExchange.Redis;

namespace CrestCreates.Caching;

public class RedisCrestCache : ICrestCache
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly CacheOptions _options;

    public RedisCrestCache(IConnectionMultiplexer multiplexer, CacheOptions options)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
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
        var prefix = _options.Prefix ?? string.Empty;
        var regex = CacheGlob.CreateRegex(pattern);
        var server = _multiplexer.GetServer(_multiplexer.GetEndPoints()[0]);
        await foreach (var key in EnumerateOwnedKeysAsync(server, prefix).WithCancellation(cancellationToken))
        {
            if (regex.IsMatch(key.ToString().AsSpan(prefix.Length)))
            {
                await _database.KeyDeleteAsync(key);
            }
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prefix = _options.Prefix ?? string.Empty;
        var server = _multiplexer.GetServer(_multiplexer.GetEndPoints()[0]);
        await foreach (var key in EnumerateOwnedKeysAsync(server, prefix).WithCancellation(cancellationToken))
        {
            await _database.KeyDeleteAsync(key);
        }
    }

    /// <summary>
    /// Redis receives an escaped server-side prefix filter only; the instance prefix itself is
    /// never interpreted as a glob. Keys are re-verified with ordinal prefix ownership because
    /// Redis glob semantics differ from the shared in-process matcher.
    /// </summary>
    private static async IAsyncEnumerable<RedisKey> EnumerateOwnedKeysAsync(
        IServer server,
        string prefix)
    {
        var scanPattern = CacheGlob.Escape(prefix) + "*";
        await foreach (var key in server.KeysAsync(pattern: scanPattern))
        {
            if (key.ToString().StartsWith(prefix, StringComparison.Ordinal))
            {
                yield return key;
            }
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
        var prefix = _options.Prefix ?? string.Empty;
        var server = _multiplexer.GetServer(_multiplexer.GetEndPoints()[0]);
        var count = 0;
        await foreach (var _ in EnumerateOwnedKeysAsync(server, prefix).WithCancellation(cancellationToken))
        {
            count++;
        }
        return count;
    }
}