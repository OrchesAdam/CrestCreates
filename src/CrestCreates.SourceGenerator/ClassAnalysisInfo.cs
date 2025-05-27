using System.Collections.Generic;
using CrestCreates.SourceGenerator.Model;

namespace CrestCreates.SourceGenerator;

internal record ClassAnalysisInfo(
    string FullName,
    string ClassName,
    bool HasServiceAttributes,
    List<string> AttributeNames,
    ServiceDescriptorInfo? ServiceInfo
)
{
    public string FullName { get; } = FullName;
    public string ClassName { get; } = ClassName;
    public bool HasServiceAttributes { get; } = HasServiceAttributes;
    public List<string> AttributeNames { get; } = AttributeNames;
    public ServiceDescriptorInfo? ServiceInfo { get; } = ServiceInfo;
}