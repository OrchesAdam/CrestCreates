using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class InMemoryPipelineMetrics : IPipelineMetrics
{
    private readonly ConcurrentDictionary<string, List<ExecutionRecord>> _records = new();

    public void RecordExecution(string capabilityName, bool success, TimeSpan duration)
    {
        _records.GetOrAdd(capabilityName, _ => new()).Add(new ExecutionRecord
        {
            Success = success,
            Duration = duration
        });
    }

    public PipelineMetricsSnapshot GetSnapshot()
    {
        var byCapability = new Dictionary<string, PerCapabilityMetrics>();
        int total = 0, succeeded = 0, failed = 0;
        double totalMs = 0;

        foreach (var kv in _records)
        {
            var records = kv.Value;
            var capTotal = records.Count;
            var capSuccess = records.Count(r => r.Success);
            var capFailed = capTotal - capSuccess;
            var capAvgMs = records.Average(r => r.Duration.TotalMilliseconds);

            total += capTotal;
            succeeded += capSuccess;
            failed += capFailed;
            totalMs += records.Sum(r => r.Duration.TotalMilliseconds);

            byCapability[kv.Key] = new PerCapabilityMetrics
            {
                Executions = capTotal,
                Successes = capSuccess,
                Failures = capFailed,
                AverageDurationMs = capAvgMs
            };
        }

        return new PipelineMetricsSnapshot
        {
            TotalExecutions = total,
            SuccessfulExecutions = succeeded,
            FailedExecutions = failed,
            AverageDurationMs = total > 0 ? totalMs / total : 0,
            ByCapability = byCapability
        };
    }

    private sealed class ExecutionRecord
    {
        public bool Success { get; init; }
        public TimeSpan Duration { get; init; }
    }
}