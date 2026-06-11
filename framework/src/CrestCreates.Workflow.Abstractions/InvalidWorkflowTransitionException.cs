namespace CrestCreates.Workflow.Abstractions;

public sealed class InvalidWorkflowTransitionException : Exception
{
    public WorkflowInstanceStatus From { get; }
    public WorkflowInstanceStatus To { get; }

    public InvalidWorkflowTransitionException(WorkflowInstanceStatus from, WorkflowInstanceStatus to)
        : base($"Invalid workflow state transition: {from} → {to}.")
    {
        From = from;
        To = to;
    }
}
