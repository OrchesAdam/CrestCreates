using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Projection metadata describing how a CapabilityDescriptor is exposed through Dynamic API.
/// This descriptor never owns capability schemas, permissions, handlers, or execution logic.
/// </summary>
public sealed class CapabilityEndpointDescriptor : IDescriptor, IVersionedDescriptor
{
    public string Namespace => "dynamic-api-endpoint";
    public DescriptorKind Kind => DescriptorKind.DynamicApiEndpoint;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }

    public required VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }

    public CapabilityEndpointHttpMethod HttpMethod { get; init; }
    public string RoutePattern { get; init; } = string.Empty;
    public CapabilityEndpointAuthorizationMode AuthorizationMode { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;

    public IReadOnlyList<CapabilityEndpointInputBinding> InputBindings { get; init; }
        = Array.Empty<CapabilityEndpointInputBinding>();

    public CapabilityEndpointOutputMapping OutputMapping { get; init; } = new();

    public CapabilityEndpointProjectionMetadata Projection { get; init; } = new();
}
