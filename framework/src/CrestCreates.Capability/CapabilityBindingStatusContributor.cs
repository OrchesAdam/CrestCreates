using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability;

public sealed class CapabilityBindingStatusContributor : IDescriptorBindingStatusContributor
{
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly ICapabilityHandlerResolver _handlerResolver;
    private readonly ISchemaRegistry _schemaRegistry;

    public CapabilityBindingStatusContributor(
        ICapabilityRegistry capabilityRegistry,
        ICapabilityHandlerResolver handlerResolver,
        ISchemaRegistry schemaRegistry)
    {
        _capabilityRegistry = capabilityRegistry;
        _handlerResolver = handlerResolver;
        _schemaRegistry = schemaRegistry;
    }

    public DescriptorKind SupportedKind => DescriptorKind.Capability;
    public int Order => 10;

    public IReadOnlyList<IDescriptor> GetDescriptors()
    {
        return _capabilityRegistry.GetAll().Cast<IDescriptor>().ToList();
    }

    public DescriptorBindingReport Evaluate(IDescriptor descriptor)
    {
        var cap = (CapabilityDescriptor)descriptor;
        var issues = new List<DescriptorBindingIssue>();
        var fullId = $"{cap.Namespace}.{cap.Id}";

        if (cap.InputSchema.HasValue)
        {
            var refVal = cap.InputSchema.Value;
            var schema = _schemaRegistry.GetByVersion(refVal.Id, refVal.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_INPUT_SCHEMA",
                    $"Input schema '{refVal.Id}' v{refVal.Version} not found.",
                    fullId, DescriptorKind.Capability, "InputSchema"));
            }
        }

        if (cap.OutputSchema.HasValue)
        {
            var refVal = cap.OutputSchema.Value;
            var schema = _schemaRegistry.GetByVersion(refVal.Id, refVal.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_OUTPUT_SCHEMA",
                    $"Output schema '{refVal.Id}' v{refVal.Version} not found.",
                    fullId, DescriptorKind.Capability, "OutputSchema"));
            }
        }

        var handler = _handlerResolver.Resolve(cap.Id);
        if (handler == null)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "BIND_NO_HANDLER",
                $"No handler registered for capability '{cap.Id}'.",
                fullId, DescriptorKind.Capability));
        }

        var status = BindingStatusSynthesizer.SynthesizeStatus(issues);
        return new DescriptorBindingReport
        {
            DescriptorId = fullId,
            DescriptorKind = DescriptorKind.Capability,
            Status = status,
            Issues = issues
        };
    }
}
