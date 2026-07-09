namespace CrestCreates.DynamicApi;

/// <summary>
/// Level 2 convenience attribute for PATCH endpoints.
/// Projects to <see cref="CapabilityEndpointSpecAttribute"/> with <see cref="CapabilityEndpointHttpMethod.Patch"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PatchAttribute : Attribute
{
    public PatchAttribute(string capabilityId, string route = "")
    {
        CapabilityId = capabilityId;
        Route = route;
    }

    public string CapabilityId { get; }
    public string Route { get; }

    public Type? Body { get; init; }
    public Type? Input { get; init; }
    public string? InputName { get; init; }
    public int CapabilityVersion { get; init; }
    public string? EndpointId { get; init; }
    public int EndpointVersion { get; init; }
    public CapabilityEndpointAuthorizationMode Auth { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;
    public int SuccessStatusCode { get; init; }
    public string? OperationId { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
}
