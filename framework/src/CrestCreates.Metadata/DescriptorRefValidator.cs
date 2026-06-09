using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorRefValidator
{
    public sealed class ValidationReport
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; init; } = new();
    }

    public static ValidationReport Validate(
        IDescriptor descriptor,
        IGlobalDescriptorRegistry registry)
    {
        var errors = new List<string>();

        switch (descriptor)
        {
            case CrestCreates.Capability.Abstractions.CapabilityDescriptor c:
                ValidateRef(c.InputSchema, registry, errors, $"{c.Name}.InputSchema");
                ValidateRef(c.OutputSchema, registry, errors, $"{c.Name}.OutputSchema");
                break;

            case EventDescriptor e:
                ValidateRef(e.PayloadSchema, registry, errors, $"{e.Name}.PayloadSchema");
                break;

            case FormDescriptor f:
                ValidateRef(f.Schema, registry, errors, $"{f.Name}.Schema");
                break;

            case HumanTaskDescriptor h:
                ValidateRef(h.Interaction, registry, errors, $"{h.Name}.Interaction");
                if (h.InputSchema != null)
                    ValidateRef(h.InputSchema.Value, registry, errors, $"{h.Name}.InputSchema");
                if (h.OutputSchema != null)
                    ValidateRef(h.OutputSchema.Value, registry, errors, $"{h.Name}.OutputSchema");
                foreach (var outcome in h.Outcomes)
                {
                    if (outcome.Capability != null)
                        ValidateRef(outcome.Capability.Value, registry, errors,
                            $"{h.Name}.Outcome.{outcome.Condition}");
                }
                break;

            case WorkflowDescriptor w:
                if (w.VariableSchema != null)
                    ValidateRef(w.VariableSchema.Value, registry, errors, $"{w.Name}.VariableSchema");
                foreach (var step in w.Steps)
                    ValidateStepTarget(step, registry, errors);
                break;
        }

        return new ValidationReport { Errors = errors };
    }

    private static void ValidateRef<T>(
        VersionedDescriptorRef<T> descriptorRef,
        IGlobalDescriptorRegistry registry,
        List<string> errors,
        string context) where T : IVersionedDescriptor
    {
        var resolved = registry.GetById(descriptorRef.Id);
        if (resolved == null)
        {
            errors.Add(
                $"[{context}] Unresolved descriptor ref: {typeof(T).Name} '{descriptorRef.Id}' v{descriptorRef.Version}");
        }
        else if (resolved is IVersionedDescriptor versioned && versioned.Version < descriptorRef.Version)
        {
            errors.Add(
                $"[{context}] Version conflict: {typeof(T).Name} '{descriptorRef.Id}' requires v{descriptorRef.Version} but latest is v{versioned.Version}");
        }
    }

    private static void ValidateStepTarget(
        WorkflowStep step,
        IGlobalDescriptorRegistry registry,
        List<string> errors)
    {
        switch (step.Target)
        {
            case CapabilityTarget ct:
                ValidateRef(ct.Capability, registry, errors, $"Step '{step.Id}' CapabilityTarget");
                break;
            case HumanTaskTarget ht:
                ValidateRef(ht.HumanTask, registry, errors, $"Step '{step.Id}' HumanTaskTarget");
                break;
            case SubWorkflowTarget sw:
                ValidateRef(sw.SubWorkflow, registry, errors, $"Step '{step.Id}' SubWorkflowTarget");
                break;
        }
    }
}
