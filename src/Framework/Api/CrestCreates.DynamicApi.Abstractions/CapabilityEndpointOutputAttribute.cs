namespace CrestCreates.DynamicApi;

[Obsolete("CapabilityEndpointOutput is reserved for future Level 1 output mapping override. Not yet consumed by the source generator.")]
[AttributeUsage(AttributeTargets.Class)]
public sealed class CapabilityEndpointOutputAttribute : Attribute
{
    public int SuccessStatusCode { get; init; } = 200;
    public string? ContentType { get; init; }
}
