namespace CrestCreates.DynamicApi;

public sealed record CapabilityEndpointOutputMapping
{
    public int SuccessStatusCode { get; init; } = 200;
    public string? ContentType { get; init; }
}
