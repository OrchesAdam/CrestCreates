using CrestCreates.Metadata.Abstractions.DescriptorCapability;

namespace CrestCreates.DynamicApi.Abstractions;

public sealed record AppServiceCompatibilityProjectionEntry
{
    public string SourceService { get; init; } = string.Empty;
    public string SourceMethod { get; init; } = string.Empty;
    public string CapabilityId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = string.Empty;
    public string RoutePattern { get; init; } = string.Empty;
    public IReadOnlyList<string> PermissionNames { get; init; } = Array.Empty<string>();
    public string InvokerTypeName { get; init; } = string.Empty;
    public CapabilityProjectionKind ProjectionKind { get; init; }
}
