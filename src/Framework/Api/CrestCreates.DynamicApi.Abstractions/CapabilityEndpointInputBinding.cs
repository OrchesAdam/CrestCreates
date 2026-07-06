namespace CrestCreates.DynamicApi;

public sealed record CapabilityEndpointInputBinding
{
    public string Name { get; init; } = string.Empty;
    public CapabilityEndpointParameterSource Source { get; init; }
    public string? CapabilityInputPath { get; init; }
    public bool Required { get; init; } = true;
}
