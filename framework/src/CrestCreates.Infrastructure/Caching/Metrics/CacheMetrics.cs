using System;

namespace CrestCreates.Infrastructure.Caching.Metrics
{
    public class CacheMetrics
    {
        private long _hits;
        private long _misses;
        private long _sets;
        private long _removes;
        private long _evictions;
        private long _errors;
        
        public long Hits => _hits;
        public long Misses => _misses;
        public long Sets => _sets;
        public long Removes => _removes;
        public long Evictions => _evictions;
        public long Errors => _errors;
        public DateTime LastReset { get; private set; } = DateTime.UtcNow;

        public double HitRate
        {
            get
            {
                var total = _hits + _misses;
                return total == 0 ? 0 : (double)_hits / total;
            }
        }

        public void RecordHit()
        {
            System.Threading.Interlocked.Increment(ref _hits);
        }

        public void RecordMiss()
        {
            System.Threading.Interlocked.Increment(ref _misses);
        }

        public void RecordSet()
        {
            System.Threading.Interlocked.Increment(ref _sets);
        }

        public void RecordRemove()
        {
            System.Threading.Interlocked.Increment(ref _removes);
        }

        public void RecordEviction()
        {
            System.Threading.Interlocked.Increment(ref _evictions);
        }

        public void RecordError()
        {
            System.Threading.Interlocked.Increment(ref _errors);
        }

        public void Reset()
        {
            System.Threading.Interlocked.Exchange(ref _hits, 0);
            System.Threading.Interlocked.Exchange(ref _misses, 0);
            System.Threading.Interlocked.Exchange(ref _sets, 0);
            System.Threading.Interlocked.Exchange(ref _removes, 0);
            System.Threading.Interlocked.Exchange(ref _evictions, 0);
            System.Threading.Interlocked.Exchange(ref _errors, 0);
            LastReset = DateTime.UtcNow;
        }
    }
}
