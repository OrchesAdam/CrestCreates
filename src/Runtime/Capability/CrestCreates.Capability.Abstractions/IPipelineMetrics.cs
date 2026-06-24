namespace CrestCreates.Capability.Abstractions;

public interface IPipelineMetrics
{
    void RecordExecution(string capabilityName, bool success, TimeSpan duration);
    PipelineMetricsSnapshot GetSnapshot();
}
