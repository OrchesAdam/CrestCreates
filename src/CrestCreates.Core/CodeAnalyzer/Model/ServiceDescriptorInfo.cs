using System.Collections.Generic;

namespace CrestCreates.CodeAnalyzer.Model;

public record ServiceDescriptorInfo(
    string FullTypeName,
    string ClassName,
    string Namespace,
    List<ServiceAttributeInfo> ServiceAttributes,
    ConstructorInfo? PrimaryConstructor,
    Dictionary<string, string>? AdditionalData,
    List<string> ImplementedInterfaces
)
{
    public string FullTypeName { get; } = FullTypeName;
    public string ClassName { get; } = ClassName;
    public string Namespace { get; } = Namespace;
    public List<ServiceAttributeInfo> ServiceAttributes { get; } = ServiceAttributes;
    public ConstructorInfo? PrimaryConstructor { get; } = PrimaryConstructor;
    public List<string> ImplementedInterfaces { get; } = ImplementedInterfaces;
    public Dictionary<string, string> AdditionalData { get; } = AdditionalData ?? new Dictionary<string, string>();
}