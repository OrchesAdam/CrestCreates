using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.Workflow;

public sealed class HumanTaskStepExecutor : IWorkflowStepExecutor
{
    private readonly IHumanTaskRuntime _runtime;
    private readonly IRuntimeStateContractRegistry _stateRegistry;

    public HumanTaskStepExecutor(IHumanTaskRuntime runtime, IRuntimeStateContractRegistry stateRegistry)
    {
        _runtime = runtime;
        _stateRegistry = stateRegistry;
    }

    public async Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        var target = (HumanTaskTarget)context.Step.Target;

        var instance = await _runtime.PrepareAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = target.HumanTask.Id,
            Version = target.HumanTask.Version,
            InstanceId = CreateInstanceId(context),
            TenantId = context.Instance.TenantId,
            WorkflowKey = context.Instance.Key,
            WorkflowStepId = context.Step.Id,
            Input = _stateRegistry.Capture(new RuntimeStateBag(
                context.Instance.Variables.Select(pair => new KeyValuePair<string, RuntimeStateValue>(pair.Key, pair.Value))))
        }, ct).ConfigureAwait(false);

        return new StepExecutionResult(
            StepExecutionStatus.Suspended,
            WaitingHumanTaskId: instance.Id,
            PreparedHumanTask: instance);
    }

    private static string? CreateInstanceId(WorkflowExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.RunOperationId))
            return null;

        var material = string.Join("\n",
            context.Instance.TenantId ?? "<host>",
            context.Instance.InstanceId,
            context.Step.Id,
            context.RunOperationId);
        return "ht_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
}
