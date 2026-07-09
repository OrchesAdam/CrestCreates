namespace CrestCreates.DynamicApi;

/// <summary>
/// Legacy endpoint descriptor for AppService-oriented Dynamic API.
/// New Capability Endpoint projection uses its own endpoint descriptor type.
/// </summary>
public sealed record DynamicApiEndpointDescriptor(
    string ServiceName,
    string ActionName,
    string HttpMethod,
    string RoutePattern,
    Type ServiceType,
    Type? RequestType,
    Type? ResponseType,
    IReadOnlyCollection<string> Permissions,
    bool RequiresTransaction);
