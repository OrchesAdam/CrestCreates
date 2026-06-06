namespace CrestCreates.DynamicApi;

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
