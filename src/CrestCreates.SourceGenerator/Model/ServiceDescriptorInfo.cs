using System.Collections.Generic;

namespace CrestCreates.SourceGenerator.Model;

public record ServiceDescriptorInfo(
    string FullTypeName,
    string ClassName,
    string Namespace,
    List<ServiceAttributeInfo> ServiceAttributes,
    ConstructorInfo? PrimaryConstructor,
    List<string> ImplementedInterfaces
)
{
    public string FullTypeName { get; } = FullTypeName;
    public string ClassName { get; } = ClassName;
    public string Namespace { get; } = Namespace;
    public List<ServiceAttributeInfo> ServiceAttributes { get; } = ServiceAttributes;
    public ConstructorInfo? PrimaryConstructor { get; } = PrimaryConstructor;
    public List<string> ImplementedInterfaces { get; } = ImplementedInterfaces;
}