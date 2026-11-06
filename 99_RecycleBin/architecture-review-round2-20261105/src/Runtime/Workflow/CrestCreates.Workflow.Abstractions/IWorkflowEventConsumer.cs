namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowEventConsumer
{
    Task OnCapabilityEventAsync(string eventName, object? payload, CancellationToken ct);
}