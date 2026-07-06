namespace CrestCreates.DynamicApi;

public sealed record CapabilityEndpointProjectionMetadata
{
    public string? OperationId { get; init; }
    public string? GroupName { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
    public CapabilityEndpointVisibility Visibility { get; init; } = CapabilityEndpointVisibility.Public;
}
