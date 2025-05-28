using System.Collections.Generic;

namespace CrestCreates.SourceGenerator.Model;

public record ConstructorInfo(List<ParameterInfo> Parameters)
{
    public List<ParameterInfo> Parameters { get; } = Parameters;
}

/// <summary>
/// 参数信息模型
/// </summary>
public class ParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? RefKind { get; set; } = "None";
    public bool HasDefaultValue { get; set; }
    public string? DefaultValue { get; set; } = "null";

    public ParameterInfo() { }

    public ParameterInfo(string name, string type, string? refKind, bool hasDefaultValue, string? defaultValue)
    {
        Name = name;
        Type = type;
        RefKind = refKind;
        HasDefaultValue = hasDefaultValue;
        DefaultValue = defaultValue;
    }
}