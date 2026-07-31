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
    public string? RunOperationId { get; }

    public WorkflowExecutionContext(
        WorkflowDescriptor workflow,
        WorkflowInstance instance,
        WorkflowStep step,
        string? runOperationId = null)
    {
        Workflow = workflow;
        Instance = instance;
        Step = step;
        RunOperationId = runOperationId;
    }
}
