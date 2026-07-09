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

    /// <summary>
    /// CLR property name on the body DTO to assign this scalar input value to.
    /// When set, the generated binding code uses this name for property assignment.
    /// When null, the binding emitter falls back to PascalCase(Name).
    /// This property is source-generator-only; it does not appear in the descriptor.
    /// </summary>
    public string? TargetProperty { get; init; }
}
