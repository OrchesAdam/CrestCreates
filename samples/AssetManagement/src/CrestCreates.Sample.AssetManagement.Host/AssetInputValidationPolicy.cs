using CrestCreates.Capability.Middleware;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host;

public sealed class AssetInputValidationPolicy : ICapabilityInputValidationPolicy
{
    public bool RejectUnknownProperties(CapabilityDescriptor capability, SchemaDescriptor inputSchema)
        => capability.Id.StartsWith("asset-management.", StringComparison.Ordinal)
            || capability.Id.StartsWith("compat.appservice.asset-", StringComparison.Ordinal);
}
