using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Bootstrap;

public sealed class CapabilitySchemaValidator : IBootstrapValidator
{
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly IDescriptorLookup _descriptorLookup;

    public CapabilitySchemaValidator(
        ICapabilityRegistry capabilityRegistry,
        IDescriptorLookup descriptorLookup)
    {
        _capabilityRegistry = capabilityRegistry;
        _descriptorLookup = descriptorLookup;
    }

    public int Order => 200;

    public ValidationReport Validate()
    {
        var issues = new List<ValidationIssue>();
        var descriptors = _capabilityRegistry.GetAll();

        foreach (var descriptor in descriptors)
        {
            if (descriptor.InputSchema.HasValue)
            {
                var schemaRef = descriptor.InputSchema.Value;
                var refObj = new DescriptorRef("schema", schemaRef.Id, schemaRef.Version);
                if (!_descriptorLookup.Exists(refObj))
                {
                    issues.Add(new ValidationIssue(SeverityLevel.Error,
                        $"Capability '{descriptor.Id}' references InputSchema '{schemaRef.Id}' (v{schemaRef.Version}) which does not exist."));
                }
            }

            if (descriptor.OutputSchema.HasValue)
            {
                var schemaRef = descriptor.OutputSchema.Value;
                var refObj = new DescriptorRef("schema", schemaRef.Id, schemaRef.Version);
                if (!_descriptorLookup.Exists(refObj))
                {
                    issues.Add(new ValidationIssue(SeverityLevel.Error,
                        $"Capability '{descriptor.Id}' references OutputSchema '{schemaRef.Id}' (v{schemaRef.Version}) which does not exist."));
                }
            }
        }

        return ValidationReport.FromIssues(issues.ToArray());
    }
}
