using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Bootstrap;

public sealed class CapabilityHandlerValidator : IBootstrapValidator
{
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly ICapabilityHandlerRegistry _handlerRegistry;

    public CapabilityHandlerValidator(
        ICapabilityRegistry capabilityRegistry,
        ICapabilityHandlerRegistry handlerRegistry)
    {
        _capabilityRegistry = capabilityRegistry;
        _handlerRegistry = handlerRegistry;
    }

    public int Order => 100;

    public ValidationReport Validate()
    {
        var issues = new List<ValidationIssue>();
        var descriptors = _capabilityRegistry.GetAll();
        var mappings = _handlerRegistry.GetHandlerMappings();

        foreach (var descriptor in descriptors)
        {
            if (!mappings.ContainsKey(descriptor.Id))
            {
                issues.Add(new ValidationIssue(SeverityLevel.Error,
                    $"Capability '{descriptor.Id}' (Name: '{descriptor.Name}') has no registered handler. " +
                    $"Add [GenerateCapabilityHandler] or register manually."));
            }
        }

        return ValidationReport.FromIssues(issues.ToArray());
    }
}
