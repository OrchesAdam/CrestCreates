using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class HumanTaskStepExecutor : IWorkflowStepExecutor
{
    private readonly IHumanTaskRuntime _runtime;

    public HumanTaskStepExecutor(IHumanTaskRuntime runtime)
        => _runtime = runtime;

    public async Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        var target = (HumanTaskTarget)context.Step.Target;

        var instance = await _runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = target.HumanTask.Id,
            Version = target.HumanTask.Version,
            WorkflowInstanceId = context.Instance.InstanceId,
            WorkflowStepId = context.Step.Id,
            Input = context.Instance.Variables
        }, ct).ConfigureAwait(false);

        return new StepExecutionResult(
            StepExecutionStatus.Suspended,
            WaitingHumanTaskId: instance.Id);
    }
}
