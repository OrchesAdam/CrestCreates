using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class HumanTaskStepExecutor : IWorkflowStepExecutor
{
    public Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        // Placeholder: produces Suspended result.
        // Phase 5/6 HumanTask Runtime will replace with actual task creation.
        return Task.FromResult(
            new StepExecutionResult(StepExecutionStatus.Suspended));
    }
}
