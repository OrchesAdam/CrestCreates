using System.Threading.Tasks;

namespace CrestCreates.Infrastructure.Caching.Metrics
{
    public interface ICacheMetricsCollector
    {
        void RecordHit(string cacheName);
        void RecordMiss(string cacheName);
        void RecordSet(string cacheName);
        void RecordRemove(string cacheName);
        void RecordEviction(string cacheName);
        void RecordError(string cacheName);
        CacheMetrics GetMetrics(string cacheName);
        CacheMetrics GetGlobalMetrics();
        void ResetMetrics(string cacheName);
        void ResetAllMetrics();
    }
}
