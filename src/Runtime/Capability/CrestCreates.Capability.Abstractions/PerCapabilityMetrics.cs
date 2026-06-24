namespace CrestCreates.Capability.Abstractions;

public sealed class PerCapabilityMetrics
{
    public int Executions { get; init; }
    public int Successes { get; init; }
    public int Failures { get; init; }
    public double AverageDurationMs { get; init; }
}
