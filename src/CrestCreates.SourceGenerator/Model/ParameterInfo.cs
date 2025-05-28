using System.Collections.Generic;

namespace CrestCreates.SourceGenerator.Model;

public record ConstructorInfo(List<ParameterInfo> Parameters)
{
    public List<ParameterInfo> Parameters { get; } = Parameters;
}

public record ParameterInfo(
    string Type,
    string Name,
    bool HasDefaultValue,
    string? DefaultValue
)
{
    public string Type { get; } = Type;
    public string Name { get; } = Name;
    public bool HasDefaultValue { get; } = HasDefaultValue;
    public string? DefaultValue { get; } = DefaultValue;
}