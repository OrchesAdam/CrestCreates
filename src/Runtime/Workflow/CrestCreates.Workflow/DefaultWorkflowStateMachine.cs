using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class DefaultWorkflowStateMachine : IWorkflowStateMachine
{
    public void ValidateTransition(WorkflowInstanceStatus from, WorkflowInstanceStatus to)
    {
        var valid = (from, to) switch
        {
            (WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Suspended) => true,
            (WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Completed) => true,
            (WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Failed) => true,
            (WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Running) => true,
            // Explicitly-authorized compensation may abort a suspended wait.
            // The WorkflowAbortService is the sole canonical owner of this
            // transition and records the workflow.failed lifecycle event.
            (WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Failed) => true,
            _ => false
        };

        if (!valid)
            throw new InvalidWorkflowTransitionException(from, to);
    }
}
