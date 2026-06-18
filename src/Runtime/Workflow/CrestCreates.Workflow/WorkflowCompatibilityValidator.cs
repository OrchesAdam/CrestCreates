using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

/// <summary>
/// Bootstrap validation only. Validates that a WorkflowDescriptor
/// contains only constructs supported by the current runtime phase,
/// and that all target references point to existing descriptors.
/// Must be called during application startup, not during execution.
/// </summary>
public sealed class WorkflowCompatibilityValidator
{
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly IHumanTaskRegistry _humanTaskRegistry;

    public WorkflowCompatibilityValidator(
        ICapabilityRegistry capabilityRegistry,
        IHumanTaskRegistry humanTaskRegistry)
    {
        _capabilityRegistry = capabilityRegistry;
        _humanTaskRegistry = humanTaskRegistry;
    }

    public void Validate(WorkflowDescriptor descriptor)
    {
        foreach (var step in descriptor.Steps)
        {
            ValidateTarget(step.Target);
            ValidateErrorBehavior(step.OnError);
            ValidateTransitions(step.Transitions);
        }
    }

    private void ValidateTarget(InteractionTarget target)
    {
        switch (target)
        {
            case CapabilityTarget capTarget:
                if (_capabilityRegistry.GetById(capTarget.Capability.Id) == null)
                    throw new WorkflowValidationException(
                        $"Capability '{capTarget.Capability.Id}' referenced by workflow step not found.");
                break;

            case HumanTaskTarget htTarget:
                if (_humanTaskRegistry.GetById(htTarget.HumanTask.Id) == null)
                    throw new WorkflowValidationException(
                        $"HumanTask '{htTarget.HumanTask.Id}' referenced by workflow step not found.");
                break;

            case SubWorkflowTarget:
                throw new WorkflowValidationException(
                    "SubWorkflowTarget is not supported in Phase 4b.");
        }
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
