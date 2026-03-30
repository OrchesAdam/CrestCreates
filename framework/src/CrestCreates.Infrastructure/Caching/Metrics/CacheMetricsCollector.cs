using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Caching.Metrics
{
    public class CacheMetricsCollector : ICacheMetricsCollector
    {
        private readonly ConcurrentDictionary<string, CacheMetrics> _metrics;
        private readonly CacheMetrics _globalMetrics;
        private readonly ILogger<CacheMetricsCollector> _logger;

        public CacheMetricsCollector(ILogger<CacheMetricsCollector> logger)
        {
            _metrics = new ConcurrentDictionary<string, CacheMetrics>();
            _globalMetrics = new CacheMetrics();
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        }

        public void RecordHit(string cacheName)
        {
            GetOrCreateMetrics(cacheName).RecordHit();
            _globalMetrics.RecordHit();
            _logger.LogDebug("Cache hit recorded for {CacheName}", cacheName);
        }

        public void RecordMiss(string cacheName)
        {
            GetOrCreateMetrics(cacheName).RecordMiss();
            _globalMetrics.RecordMiss();
            _logger.LogDebug("Cache miss recorded for {CacheName}", cacheName);
        }

        public void RecordSet(string cacheName)
        {
            GetOrCreateMetrics(cacheName).RecordSet();
            _globalMetrics.RecordSet();
            _logger.LogDebug("Cache set recorded for {CacheName}", cacheName);
        }

        public void RecordRemove(string cacheName)
        {
            GetOrCreateMetrics(cacheName).RecordRemove();
            _globalMetrics.RecordRemove();
            _logger.LogDebug("Cache remove recorded for {CacheName}", cacheName);
        }

        public void RecordEviction(string cacheName)
        {
            GetOrCreateMetrics(cacheName).RecordEviction();
            _globalMetrics.RecordEviction();
            _logger.LogDebug("Cache eviction recorded for {CacheName}", cacheName);
        }

        public void RecordError(string cacheName)
        {
            GetOrCreateMetrics(cacheName).RecordError();
            _globalMetrics.RecordError();
            _logger.LogWarning("Cache error recorded for {CacheName}", cacheName);
        }

        public CacheMetrics GetMetrics(string cacheName)
        {
            return GetOrCreateMetrics(cacheName);
        }

        public CacheMetrics GetGlobalMetrics()
        {
            return _globalMetrics;
        }

        public void ResetMetrics(string cacheName)
        {
            if (_metrics.TryGetValue(cacheName, out var metrics))
            {
                metrics.Reset();
                _logger.LogInformation("Metrics reset for {CacheName}", cacheName);
            }
        }

        public void ResetAllMetrics()
        {
            foreach (var metrics in _metrics.Values)
            {
                metrics.Reset();
            }
            _globalMetrics.Reset();
            _logger.LogInformation("All metrics reset");
        }

        private CacheMetrics GetOrCreateMetrics(string cacheName)
        {
            return _metrics.GetOrAdd(cacheName, _ => new CacheMetrics());
        }
    }
}
