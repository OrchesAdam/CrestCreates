namespace CrestCreates.DynamicApi;

/// <summary>
/// Level 2 convenience attribute for DELETE endpoints.
/// Projects to <see cref="CapabilityEndpointSpecAttribute"/> with <see cref="CapabilityEndpointHttpMethod.Delete"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DeleteAttribute : Attribute
{
    public DeleteAttribute(string capabilityId, string route = "")
    {
        CapabilityId = capabilityId;
        Route = route;
    }

    public string CapabilityId { get; }
    public string Route { get; }

    public Type? Input { get; init; }
    public string? InputName { get; init; }
    public int CapabilityVersion { get; init; }
    public CapabilityEndpointAuthorizationMode Auth { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;
    public string? OperationId { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
}
