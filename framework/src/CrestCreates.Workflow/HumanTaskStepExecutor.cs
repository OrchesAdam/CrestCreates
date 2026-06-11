using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class HumanTaskStepExecutor : IWorkflowStepExecutor
{
    public Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        var target = (HumanTaskTarget)context.Step.Target;
        return Task.FromResult(
            new StepExecutionResult(
                StepExecutionStatus.Suspended,
                WaitingHumanTaskId: target.HumanTask.Id));
    }
}
