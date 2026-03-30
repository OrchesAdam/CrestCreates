using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Caching.Metrics;

namespace CrestCreates.Infrastructure.Tests
{
    public class CacheMetricsTests
    {
        private readonly CacheMetrics _metrics;

        public CacheMetricsTests()
        {
            _metrics = new CacheMetrics();
        }

        [Fact]
        public void RecordHit_Should_Increment_Hits_Count()
        {
            var initialHits = _metrics.Hits;

            _metrics.RecordHit();

            _metrics.Hits.Should().Be(initialHits + 1);
        }

        [Fact]
        public void RecordMiss_Should_Increment_Misses_Count()
        {
            var initialMisses = _metrics.Misses;

            _metrics.RecordMiss();

            _metrics.Misses.Should().Be(initialMisses + 1);
        }

        [Fact]
        public void RecordSet_Should_Increment_Sets_Count()
        {
            var initialSets = _metrics.Sets;

            _metrics.RecordSet();

            _metrics.Sets.Should().Be(initialSets + 1);
        }

        [Fact]
        public void RecordRemove_Should_Increment_Removes_Count()
        {
            var initialRemoves = _metrics.Removes;

            _metrics.RecordRemove();

            _metrics.Removes.Should().Be(initialRemoves + 1);
        }

        [Fact]
        public void RecordEviction_Should_Increment_Evictions_Count()
        {
            var initialEvictions = _metrics.Evictions;

            _metrics.RecordEviction();

            _metrics.Evictions.Should().Be(initialEvictions + 1);
        }

        [Fact]
        public void RecordError_Should_Increment_Errors_Count()
        {
            var initialErrors = _metrics.Errors;

            _metrics.RecordError();

            _metrics.Errors.Should().Be(initialErrors + 1);
        }

        [Fact]
        public void HitRate_Should_Calculate_Correctly()
        {
            _metrics.RecordHit();
            _metrics.RecordHit();
            _metrics.RecordMiss();
            _metrics.RecordMiss();
            _metrics.RecordMiss();

            var hitRate = _metrics.HitRate;

            hitRate.Should().Be(0.4);
        }

        [Fact]
        public void HitRate_Should_Return_Zero_When_No_Hits_Or_Misses()
        {
            var hitRate = _metrics.HitRate;

            hitRate.Should().Be(0.0);
        }

        [Fact]
        public void Reset_Should_Reset_All_Counters()
        {
            _metrics.RecordHit();
            _metrics.RecordMiss();
            _metrics.RecordSet();
            _metrics.RecordRemove();
            _metrics.RecordEviction();
            _metrics.RecordError();
            var beforeResetTime = _metrics.LastReset;

            _metrics.Reset();

            _metrics.Hits.Should().Be(0);
            _metrics.Misses.Should().Be(0);
            _metrics.Sets.Should().Be(0);
            _metrics.Removes.Should().Be(0);
            _metrics.Evictions.Should().Be(0);
            _metrics.Errors.Should().Be(0);
            _metrics.LastReset.Should().BeAfter(beforeResetTime);
        }

        [Fact]
        public void RecordHit_Should_Be_Thread_Safe()
        {
            var iterations = 1000;
            var tasks = new System.Threading.Tasks.Task[iterations];

            for (var i = 0; i < iterations; i++)
            {
                tasks[i] = System.Threading.Tasks.Task.Run(() => _metrics.RecordHit());
            }

            System.Threading.Tasks.Task.WaitAll(tasks);

            _metrics.Hits.Should().Be(iterations);
        }

        [Fact]
        public void RecordMultipleMetrics_Should_Increment_All_Correctly()
        {
            _metrics.RecordHit();
            _metrics.RecordHit();
            _metrics.RecordMiss();
            _metrics.RecordSet();
            _metrics.RecordSet();
            _metrics.RecordSet();
            _metrics.RecordRemove();
            _metrics.RecordEviction();
            _metrics.RecordError();
            _metrics.RecordError();

            _metrics.Hits.Should().Be(2);
            _metrics.Misses.Should().Be(1);
            _metrics.Sets.Should().Be(3);
            _metrics.Removes.Should().Be(1);
            _metrics.Evictions.Should().Be(1);
            _metrics.Errors.Should().Be(2);
        }
    }
}
