using CrestCreates.Capability.Middleware;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Sample.Procurement.Host;

public sealed class ProcurementInputValidationPolicy : ICapabilityInputValidationPolicy
{
    public bool RejectUnknownProperties(
        CapabilityDescriptor capability,
        SchemaDescriptor inputSchema)
        => capability.Id.StartsWith("procurement.", StringComparison.Ordinal)
            || capability.Id.StartsWith("compat.appservice.procurement.", StringComparison.Ordinal);
}
