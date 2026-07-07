namespace CrestCreates.DynamicApi;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CapabilityEndpointSetAttribute : Attribute
{
    public string? RoutePrefix { get; init; }
    public string? GroupName { get; init; }
    public string[]? Tags { get; init; }
    public string? Summary { get; init; }
}
