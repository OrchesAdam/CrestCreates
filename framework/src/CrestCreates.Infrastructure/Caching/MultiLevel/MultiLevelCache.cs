using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CrestCreates.Infrastructure.Caching.MultiLevel
{
    public class MultiLevelCache : ICache
    {
        private readonly IMemoryCache _l1Cache;
        private readonly IDatabase _redisDatabase;
        private readonly MultiLevelCacheOptions _options;
        private readonly ILogger<MultiLevelCache> _logger;
        private readonly CacheSynchronizer? _synchronizer;

        public MultiLevelCache(
            IMemoryCache l1Cache,
            IConnectionMultiplexer redisConnection,
            IOptions<MultiLevelCacheOptions> options,
            ILogger<MultiLevelCache> logger,
            CacheSynchronizer? synchronizer = null)
        {
            _l1Cache = l1Cache ?? throw new ArgumentNullException(nameof(l1Cache));
            _redisDatabase = redisConnection?.GetDatabase() ?? throw new ArgumentNullException(nameof(redisConnection));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _synchronizer = synchronizer;
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var l1Value = _l1Cache.Get<T>(key);
            if (l1Value != null)
            {
                _logger.LogDebug("L1 cache hit for key: {Key}", key);
                return l1Value;
            }

            _logger.LogDebug("L1 cache miss for key: {Key}, checking L2", key);

            try
            {
                var redisValue = await _redisDatabase.StringGetAsync(key);
                if (redisValue.HasValue)
                {
                    var value = System.Text.Json.JsonSerializer.Deserialize<T>(redisValue.ToString());
                    if (value != null)
                    {
                        _l1Cache.Set(key, value, _options.L1Expiration);
                        _logger.LogDebug("L2 cache hit for key: {Key}, backfilled to L1", key);
                        return value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error accessing L2 cache for key: {Key}", key);
            }

            _logger.LogDebug("Cache miss for key: {Key}", key);
            return default;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (value == null)
                return;

            var l1Expiration = expiration ?? _options.L1Expiration;
            var l2Expiration = expiration ?? _options.L2Expiration;

            _l1Cache.Set(key, value, l1Expiration);
            _logger.LogDebug("Set L1 cache for key: {Key}", key);

            try
            {
                var jsonValue = System.Text.Json.JsonSerializer.Serialize(value);
                await _redisDatabase.StringSetAsync(key, jsonValue, l2Expiration);
                _logger.LogDebug("Set L2 cache for key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error setting L2 cache for key: {Key}", key);
            }
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _l1Cache.Remove(key);
            _logger.LogDebug("Removed L1 cache for key: {Key}", key);

            try
            {
                await _redisDatabase.KeyDeleteAsync(key);
                _logger.LogDebug("Removed L2 cache for key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error removing L2 cache for key: {Key}", key);
            }

            if (_options.EnableL1Sync && _synchronizer != null)
            {
                await _synchronizer.PublishEvictionAsync(key, cancellationToken);
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_l1Cache.TryGetValue(key, out _))
            {
                return true;
            }

            try
            {
                return await _redisDatabase.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking existence in L2 cache for key: {Key}", key);
                return false;
            }
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // MemoryCache 没有直接的 Clear 方法，这里暂时跳过 L1 清理

            try
            {
                var endpoints = _redisDatabase.Multiplexer.GetEndPoints();
                foreach (var endpoint in endpoints)
                {
                    var server = _redisDatabase.Multiplexer.GetServer(endpoint);
                    await server.FlushDatabaseAsync(_redisDatabase.Database);
                }

                if (_options.EnableL1Sync && _synchronizer != null)
                {
                    await _synchronizer.PublishClearAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error clearing L2 cache");
            }
        }

        public void RemoveFromL1(string key)
        {
            _l1Cache.Remove(key);
            _logger.LogDebug("Removed from L1 cache by sync for key: {Key}", key);
        }

        public void ClearL1()
        {
            _logger.LogDebug("L1 cache cleared by sync (not implemented for MemoryCache)");
        }
    }
}
