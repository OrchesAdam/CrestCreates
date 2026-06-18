namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Pure state transfer object. No IServiceProvider, no CancellationToken,
/// no persistence references. CancellationToken travels via ExecuteAsync(..., ct).
/// </summary>
public sealed class WorkflowExecutionContext
{
    public WorkflowDescriptor Workflow { get; }
    public WorkflowInstance Instance { get; }
    public WorkflowStep Step { get; }

    public WorkflowExecutionContext(
        WorkflowDescriptor workflow,
        WorkflowInstance instance,
        WorkflowStep step)
    {
        Workflow = workflow;
        Instance = instance;
        Step = step;
    }
}
