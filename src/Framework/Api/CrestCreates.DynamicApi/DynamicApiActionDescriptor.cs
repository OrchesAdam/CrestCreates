using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Legacy action descriptor for AppService-oriented Dynamic API.
/// New Capability Endpoint projection uses its own input binding type.
/// </summary>
public sealed class DynamicApiActionDescriptor
{
    public string ActionName { get; init; } = string.Empty;

    public string DeclaringTypeName { get; init; } = string.Empty;

    public string OperationId { get; init; } = string.Empty;

    public string RelativeRoute { get; init; } = string.Empty;

    public string HttpMethod { get; init; } = HttpMethods.Get;

    public MethodInfo? ServiceMethod { get; init; }

    public MethodInfo? ImplementationMethod { get; init; }

    public DynamicApiReturnDescriptor ReturnDescriptor { get; init; } = null!;

    public IReadOnlyList<DynamicApiParameterDescriptor> Parameters { get; init; } = Array.Empty<DynamicApiParameterDescriptor>();

    public DynamicApiPermissionMetadata Permission { get; init; } = null!;

    public string FullRoute => string.IsNullOrWhiteSpace(RelativeRoute)
        ? RoutePrefix
        : $"{RoutePrefix}/{RelativeRoute}";

    public string RoutePrefix { get; init; } = string.Empty;
}
