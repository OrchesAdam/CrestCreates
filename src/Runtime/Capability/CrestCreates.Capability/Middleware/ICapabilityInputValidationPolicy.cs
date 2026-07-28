using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability.Middleware;

public interface ICapabilityInputValidationPolicy
{
    bool RejectUnknownProperties(
        CapabilityDescriptor capability,
        SchemaDescriptor inputSchema);
}

internal sealed class AllowUnknownCapabilityInputPropertiesPolicy
    : ICapabilityInputValidationPolicy
{
    public static AllowUnknownCapabilityInputPropertiesPolicy Instance { get; } = new();

    public bool RejectUnknownProperties(
        CapabilityDescriptor capability,
        SchemaDescriptor inputSchema)
        => false;
}
