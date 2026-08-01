using CrestCreates.Capability.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class CapabilityStepExecutor : IWorkflowStepExecutor
{
    private readonly ICapabilityPipeline _pipeline;
    private readonly IRuntimeStateContractRegistry? _stateRegistry;

    public CapabilityStepExecutor(
        ICapabilityPipeline pipeline,
        IRuntimeStateContractRegistry? stateRegistry = null)
    {
        _pipeline = pipeline;
        _stateRegistry = stateRegistry;
    }

    public async Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        var target = (CapabilityTarget)context.Step.Target;
        var input = context.Instance.Variables.ToDictionary(
            pair => pair.Key,
            pair => _stateRegistry?.Restore(pair.Value) ?? pair.Value,
            StringComparer.Ordinal);
        var result = await _pipeline.ExecuteAsync(
            target.Capability.Id,
            input: input,
            ct: ct);

        var variables = result.IsSuccess && result.Output is Dictionary<string, object?> vars
            ? vars
            : null;

        return new StepExecutionResult(
            result.IsSuccess ? StepExecutionStatus.Completed : StepExecutionStatus.Failed,
            result.Output,
            variables);
    }
}
