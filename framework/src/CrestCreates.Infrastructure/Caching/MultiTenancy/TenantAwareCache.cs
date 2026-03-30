using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Caching.MultiTenancy
{
    public class TenantAwareCache : ICache
    {
        private readonly ICache _innerCache;
        private readonly ICacheKeyGenerator _keyGenerator;
        private readonly ILogger<TenantAwareCache> _logger;

        public TenantAwareCache(
            ICache innerCache,
            ICacheKeyGenerator keyGenerator,
            ILogger<TenantAwareCache> logger)
        {
            _innerCache = innerCache ?? throw new ArgumentNullException(nameof(innerCache));
            _keyGenerator = keyGenerator ?? throw new ArgumentNullException(nameof(keyGenerator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var tenantKey = _keyGenerator.GenerateKey(key);
            _logger.LogDebug("Getting cache with tenant key: {TenantKey}", tenantKey);
            return await _innerCache.GetAsync<T>(tenantKey, cancellationToken);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            var tenantKey = _keyGenerator.GenerateKey(key);
            _logger.LogDebug("Setting cache with tenant key: {TenantKey}", tenantKey);
            await _innerCache.SetAsync(tenantKey, value, expiration, cancellationToken);
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            var tenantKey = _keyGenerator.GenerateKey(key);
            _logger.LogDebug("Removing cache with tenant key: {TenantKey}", tenantKey);
            await _innerCache.RemoveAsync(tenantKey, cancellationToken);
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            var tenantKey = _keyGenerator.GenerateKey(key);
            return await _innerCache.ExistsAsync(tenantKey, cancellationToken);
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("Clearing all cache entries for current tenant");
            await _innerCache.ClearAsync(cancellationToken);
        }
    }
}
