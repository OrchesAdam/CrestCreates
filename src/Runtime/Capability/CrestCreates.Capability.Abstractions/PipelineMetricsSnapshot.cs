namespace CrestCreates.Capability.Abstractions;

public sealed class PipelineMetricsSnapshot
{
    public int TotalExecutions { get; init; }
    public int SuccessfulExecutions { get; init; }
    public int FailedExecutions { get; init; }
    public double AverageDurationMs { get; init; }
    public IReadOnlyDictionary<string, PerCapabilityMetrics> ByCapability { get; init; }
        = new Dictionary<string, PerCapabilityMetrics>();
}
