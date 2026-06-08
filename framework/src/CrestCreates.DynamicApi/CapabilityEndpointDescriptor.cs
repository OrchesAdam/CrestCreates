using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Projection view of a CapabilityDescriptor for HTTP exposure.
/// Does NOT define its own Input/Output schema — inherits from the referenced Capability.
/// Route pattern follows the convention: {prefix}/{kebab-case-capability-name}.
/// HttpMethod is derived from CapabilityKind: Query → GET, Command → POST.
/// </summary>
public sealed class CapabilityEndpointDescriptor
{
    public VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }
    public HttpMethod HttpMethod { get; init; } = HttpMethod.Post;
    public string RoutePattern { get; init; } = string.Empty;
    public string? GroupName { get; init; }
    public bool RequireAuthorization { get; init; } = true;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public static HttpMethod DeriveHttpMethod(CapabilityKind kind)
        => kind == CapabilityKind.Query ? HttpMethod.Get : HttpMethod.Post;
}
