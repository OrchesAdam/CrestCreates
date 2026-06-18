namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowCorrelationException : Exception
{
    public WorkflowCorrelationException(string message) : base(message) { }
}
