namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowStateMachine
{
    void ValidateTransition(WorkflowInstanceStatus from, WorkflowInstanceStatus to);
}
