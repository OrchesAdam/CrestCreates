namespace CrestCreates.SourceGenerator.Model;

public record ServiceAttributeInfo(
    string ServiceType,
    string Lifetime,
    bool RegisterAsImplementedInterfaces,
    bool Replace
)
{
    public string ServiceType { get; } = ServiceType;
    public string Lifetime { get; } = Lifetime;
    public bool RegisterAsImplementedInterfaces { get; } = RegisterAsImplementedInterfaces;
    public bool Replace { get; } = Replace;
}