namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowLifecycleObserver
{
    /// <summary>
    /// Implementations must return their ValueTask promptly and must not perform
    /// unbounded synchronous blocking before returning it. Notification timeout
    /// bounds asynchronous completion after invocation has returned.
    /// </summary>
    ValueTask ObserveAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken cancellationToken = default);
}
