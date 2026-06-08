namespace CrestCreates.Workflow.Abstractions;

public enum WorkflowInstanceStatus
{
    Running,
    Suspended,
    Completed,
    Failed,
    Compensated
}
