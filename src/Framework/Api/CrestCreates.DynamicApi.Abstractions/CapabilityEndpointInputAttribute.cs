namespace CrestCreates.DynamicApi;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class CapabilityEndpointInputAttribute : Attribute
{
    public CapabilityEndpointInputAttribute(Type type)
    {
        Type = type;
    }

    public Type Type { get; }

    public string Name { get; init; } = string.Empty;
    public CapabilityEndpointParameterSource Source { get; init; }
        = CapabilityEndpointParameterSource.Body;
    public bool Required { get; init; } = true;
    public string? CapabilityInputPath { get; init; }
}
