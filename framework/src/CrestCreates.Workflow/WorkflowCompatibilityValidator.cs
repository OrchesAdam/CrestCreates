using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

/// <summary>
/// Bootstrap validation only. Validates that a WorkflowDescriptor
/// contains only constructs supported by the current runtime phase.
/// Must be called during application startup, not during execution.
/// </summary>
public sealed class WorkflowCompatibilityValidator
{
    public void Validate(WorkflowDescriptor descriptor)
    {
        foreach (var step in descriptor.Steps)
        {
            ValidateTarget(step.Target);
            ValidateErrorBehavior(step.OnError);
            ValidateTransitions(step.Transitions);
        }
    }

    private static void ValidateTarget(InteractionTarget target)
    {
        if (target is SubWorkflowTarget)
            throw new WorkflowValidationException(
                "SubWorkflowTarget is not supported in Phase 4b.");
    }

    private static void ValidateErrorBehavior(StepErrorBehavior behavior)
    {
        if (behavior is StepErrorBehavior.Retry or StepErrorBehavior.Compensate)
            throw new WorkflowValidationException(
                $"StepErrorBehavior.{behavior} is not supported in Phase 4b.");
    }

    private static void ValidateTransitions(IReadOnlyList<string> transitions)
    {
        if (transitions.Count > 0)
            throw new WorkflowValidationException(
                "Workflow step transitions are not supported in Phase 4b.");
    }
}
