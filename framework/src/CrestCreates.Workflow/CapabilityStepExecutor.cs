using CrestCreates.Capability.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class CapabilityStepExecutor : IWorkflowStepExecutor
{
    private readonly ICapabilityPipeline _pipeline;

    public CapabilityStepExecutor(ICapabilityPipeline pipeline)
        => _pipeline = pipeline;

    public async Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        var target = (CapabilityTarget)context.Step.Target;
        var result = await _pipeline.ExecuteAsync(
            $"capability:{target.Capability.Id}",
            input: context.Instance.Variables,
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
