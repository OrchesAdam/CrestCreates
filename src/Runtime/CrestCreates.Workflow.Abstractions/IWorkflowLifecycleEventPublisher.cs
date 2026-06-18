namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowLifecycleEventPublisher
{
    Task PublishAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct);
}
