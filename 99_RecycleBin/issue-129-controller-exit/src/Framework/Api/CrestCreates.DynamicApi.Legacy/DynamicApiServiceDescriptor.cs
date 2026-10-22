namespace CrestCreates.DynamicApi;

/// <summary>
/// Legacy service descriptor for AppService-oriented Dynamic API.
/// New Capability Endpoint projection uses its own endpoint descriptor type.
/// </summary>
public sealed class DynamicApiServiceDescriptor
{
    public string ServiceName { get; init; } = string.Empty;

    public string RoutePrefix { get; init; } = string.Empty;

    public Type ServiceType { get; init; } = null!;

    public Type ImplementationType { get; init; } = null!;

    public IReadOnlyList<DynamicApiActionDescriptor> Actions { get; init; } = Array.Empty<DynamicApiActionDescriptor>();
}
