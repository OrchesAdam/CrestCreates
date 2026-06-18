namespace CrestCreates.Capability.Abstractions;

public interface IPipelineMetrics
{
    void RecordExecution(string capabilityName, bool success, TimeSpan duration);
    PipelineMetricsSnapshot GetSnapshot();
}

public sealed class PipelineMetricsSnapshot
{
    public int TotalExecutions { get; init; }
    public int SuccessfulExecutions { get; init; }
    public int FailedExecutions { get; init; }
    public double AverageDurationMs { get; init; }
    public IReadOnlyDictionary<string, PerCapabilityMetrics> ByCapability { get; init; }
        = new Dictionary<string, PerCapabilityMetrics>();
}

public sealed class PerCapabilityMetrics
{
    public int Executions { get; init; }
    public int Successes { get; init; }
    public int Failures { get; init; }
    public double AverageDurationMs { get; init; }
}