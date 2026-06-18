namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowLifecycleEvent
{
    public string EventType { get; init; } = string.Empty;
    public string WorkflowInstanceId { get; init; } = string.Empty;
    public string WorkflowId { get; init; } = string.Empty;
    public WorkflowInstanceStatus Status { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public object? Payload { get; init; }
}
