namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowValidationException : Exception
{
    public WorkflowValidationException(string message) : base(message) { }
}
