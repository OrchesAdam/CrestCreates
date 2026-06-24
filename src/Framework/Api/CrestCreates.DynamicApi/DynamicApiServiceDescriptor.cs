namespace CrestCreates.DynamicApi;

public sealed class DynamicApiServiceDescriptor
{
    public string ServiceName { get; init; } = string.Empty;

    public string RoutePrefix { get; init; } = string.Empty;

    public Type ServiceType { get; init; } = null!;

    public Type ImplementationType { get; init; } = null!;

    public IReadOnlyList<DynamicApiActionDescriptor> Actions { get; init; } = Array.Empty<DynamicApiActionDescriptor>();
}
