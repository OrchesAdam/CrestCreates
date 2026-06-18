using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowLifecycleEventPublisher : IWorkflowLifecycleEventPublisher
{
    public Task PublishAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
