using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.Caching;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Caching
{
    public class CacheService : ICacheService
    {
        private readonly ICache _cache;
        private readonly ILogger<CacheService> _logger;

        public CacheService(ICache cache, ILogger<CacheService> logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                var value = await _cache.GetAsync<T>(key, cancellationToken);
                if (value != null)
                {
                    _logger.LogDebug("Cache hit for key: {Key}", key);
                }
                else
                {
                    _logger.LogDebug("Cache miss for key: {Key}", key);
                }
                return value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting value from cache for key: {Key}", key);
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            try
            {
                await _cache.SetAsync(key, value, expiration, cancellationToken);
                _logger.LogDebug("Value cached for key: {Key}, Expiration: {Expiration}", key, expiration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting value to cache for key: {Key}", key);
            }
        }

        public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            var cachedValue = await GetAsync<T>(key, cancellationToken);
            if (cachedValue != null)
            {
                return cachedValue;
            }

            _logger.LogDebug("Cache miss for key: {Key}, executing factory", key);
            var value = await factory();
            
            if (value != null)
            {
                await SetAsync(key, value, expiration, cancellationToken);
            }

            return value;
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                await _cache.RemoveAsync(key, cancellationToken);
                _logger.LogDebug("Cache removed for key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing value from cache for key: {Key}", key);
            }
        }

        public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        {
            try
            {
                var keys = await GetKeysAsync(pattern, cancellationToken);
                foreach (var key in keys)
                {
                    await _cache.RemoveAsync(key, cancellationToken);
                }
                _logger.LogDebug("Cache removed for pattern: {Pattern}, Count: {Count}", pattern, keys);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing values from cache for pattern: {Pattern}", pattern);
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _cache.ExistsAsync(key, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existence in cache for key: {Key}", key);
                return false;
            }
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _cache.ClearAsync(cancellationToken);
                _logger.LogDebug("Cache cleared");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing cache");
            }
        }

        public Task<IEnumerable<string>> GetKeysAsync(string pattern, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<string>>(new List<string>());
        }
    }
}
