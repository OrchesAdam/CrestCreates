namespace CrestCreates.Capability.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CapabilityNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
