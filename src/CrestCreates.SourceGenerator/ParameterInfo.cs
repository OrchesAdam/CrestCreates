namespace CrestCreates.SourceGenerator;

public class ParameterInfo
{
    public string Name { get; set; }
    public string Type { get; set; }
    public string RefKind { get; set; }
    public bool HasDefaultValue { get; set; }
    public string DefaultValue { get; set; }

    public ParameterInfo(string name, string type, string refKind, bool hasDefaultValue, string defaultValue)
    {
        Name = name;
        Type = type;
        RefKind = refKind;
        HasDefaultValue = hasDefaultValue;
        DefaultValue = defaultValue;
    }
}