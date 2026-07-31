using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class HumanTaskStepExecutor : IWorkflowStepExecutor
{
    private readonly IHumanTaskRuntime _runtime;
    private readonly IRuntimeStateContractRegistry? _stateRegistry;

    public HumanTaskStepExecutor(IHumanTaskRuntime runtime, IRuntimeStateContractRegistry? stateRegistry = null)
    {
        _runtime = runtime;
        _stateRegistry = stateRegistry;
    }

    public async Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        var target = (HumanTaskTarget)context.Step.Target;

        var instance = await _runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = target.HumanTask.Id,
            Version = target.HumanTask.Version,
            TenantId = context.Instance.TenantId,
            WorkflowKey = context.Instance.Key,
            WorkflowStepId = context.Step.Id,
            Input = _stateRegistry?.Capture(new RuntimeStateBag(
                context.Instance.Variables.Select(pair => new KeyValuePair<string, RuntimeStateValue>(pair.Key, pair.Value))))
                ?? throw new InvalidOperationException("Runtime state registry is required for HumanTask input capture.")
        }, ct).ConfigureAwait(false);

        return new StepExecutionResult(
            StepExecutionStatus.Suspended,
            WaitingHumanTaskId: instance.Id);
    }
}
