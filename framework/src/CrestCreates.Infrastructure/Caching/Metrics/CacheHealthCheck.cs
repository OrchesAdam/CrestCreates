using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CrestCreates.Infrastructure.Caching.Metrics
{
    public class CacheHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer? _redisConnection;
        private readonly ICache _cache;
        private readonly ILogger<CacheHealthCheck> _logger;

        public CacheHealthCheck(
            ICache cache,
            ILogger<CacheHealthCheck> logger,
            IConnectionMultiplexer? redisConnection = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redisConnection = redisConnection;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                const string testKey = "health:check";
                const string testValue = "ok";

                await _cache.SetAsync(testKey, testValue, TimeSpan.FromSeconds(10), cancellationToken);
                var value = await _cache.GetAsync<string>(testKey, cancellationToken);
                await _cache.RemoveAsync(testKey, cancellationToken);

                if (value != testValue)
                {
                    return HealthCheckResult.Degraded("Cache returned unexpected value");
                }

                var responseTime = DateTime.UtcNow - startTime;
                var data = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "responseTimeMs", responseTime.TotalMilliseconds }
                };

                if (_redisConnection != null)
                {
                    var redisStatus = CheckRedisHealth();
                    data.Add("redisStatus", redisStatus);

                    if (!redisStatus)
                    {
                        return HealthCheckResult.Degraded("Redis connection is not available", data: data);
                    }
                }

                return HealthCheckResult.Healthy("Cache is healthy", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cache health check failed");
                return HealthCheckResult.Unhealthy("Cache health check failed", ex);
            }
        }

        private bool CheckRedisHealth()
        {
            try
            {
                if (_redisConnection == null)
                    return false;

                var database = _redisConnection.GetDatabase();
                database.Ping();
                return _redisConnection.IsConnected;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis health check failed");
                return false;
            }
        }
    }
}
