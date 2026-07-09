namespace CrestCreates.DynamicApi;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CapabilityEndpointSpecAttribute : Attribute
{
    public CapabilityEndpointSpecAttribute(
        string capabilityId,
        CapabilityEndpointHttpMethod httpMethod,
        string routePattern)
    {
        CapabilityId = capabilityId;
        HttpMethod = httpMethod;
        RoutePattern = routePattern;
    }

    public string CapabilityId { get; }
    public CapabilityEndpointHttpMethod HttpMethod { get; }
    public string RoutePattern { get; }

    public int CapabilityVersion { get; init; }
    public string? EndpointId { get; init; }
    public int EndpointVersion { get; init; }
    public CapabilityEndpointAuthorizationMode AuthorizationMode { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;
    public int SuccessStatusCode { get; init; }
    public string? OperationId { get; init; }
    public string? GroupName { get; init; }
    public string[]? Tags { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
}
