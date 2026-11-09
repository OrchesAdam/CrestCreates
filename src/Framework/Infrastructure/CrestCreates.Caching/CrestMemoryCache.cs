using System;
using System.Collections.Generic;
using System.Runtime.Caching;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching.Abstractions;

namespace CrestCreates.Caching;

public class CrestMemoryCache : ICrestCache
{
    private readonly ObjectCache _cache;
    private readonly CacheOptions _options;

    public CrestMemoryCache(CacheOptions options)
    {
        _cache = System.Runtime.Caching.MemoryCache.Default;
        _options = options;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = _options.Prefix + key;
        var value = _cache.Get(fullKey);
        return Task.FromResult(value is T ? (T)value : default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = _options.Prefix + key;
        var policy = new CacheItemPolicy
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.Add(expiration ?? _options.DefaultExpiration)
        };
        _cache.Set(fullKey, value, policy);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = _options.Prefix + key;
        _cache.Remove(fullKey);
        return Task.CompletedTask;
    }

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPattern = (_options.Prefix ?? string.Empty) + pattern;
        var regex = CreateGlobRegex(fullPattern);
        var keysToRemove = new List<string>();
        foreach (var item in _cache)
        {
            if (regex.IsMatch(item.Key))
            {
                keysToRemove.Add(item.Key);
            }
        }
        foreach (var key in keysToRemove)
        {
            _cache.Remove(key);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Matches the same glob shape Redis accepts for key scans: <c>*</c> matches any sequence,
    /// <c>?</c> matches one character, and every other character (including regex metacharacters)
    /// is literal. Anchoring keeps a tenant prefix from matching a longer sibling tenant id.
    /// </summary>
    private static Regex CreateGlobRegex(string globPattern)
    {
        var builder = new StringBuilder("^");
        foreach (var character in globPattern)
        {
            switch (character)
            {
                case '*':
                    builder.Append(".*");
                    break;
                case '?':
                    builder.Append('.');
                    break;
                default:
                    builder.Append(Regex.Escape(character.ToString()));
                    break;
            }
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keysToRemove = new List<string>();
        var prefix = _options.Prefix ?? string.Empty;
        foreach (var item in _cache)
        {
            if (item.Key.StartsWith(prefix))
            {
                keysToRemove.Add(item.Key);
            }
        }
        foreach (var key in keysToRemove)
        {
            _cache.Remove(key);
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = (_options.Prefix ?? string.Empty) + key;
        return Task.FromResult(_cache.Contains(fullKey));
    }

    public Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = 0;
        var prefix = _options.Prefix ?? string.Empty;
        foreach (var item in _cache)
        {
            if (item.Key.StartsWith(prefix))
            {
                count++;
            }
        }
        return Task.FromResult((long)count);
    }
}