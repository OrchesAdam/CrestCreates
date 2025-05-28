using System.Collections.Generic;

namespace CrestCreates.SourceGenerator;

public class InterfaceInfo
{
    public string FullName { get; set; }
    public string InterfaceName { get; set; }
    public string Namespace { get; set; }
    public List<string> DependencyTypes { get; set; }
    public string? ConfigurationType { get; set; }
    public List<MethodInfo> CustomMethods { get; set; }

    public InterfaceInfo(string fullName, string interfaceName, string @namespace, 
        List<string> dependencyTypes, string? configurationType, List<MethodInfo> customMethods)
    {
        FullName = fullName;
        InterfaceName = interfaceName;
        Namespace = @namespace;
        DependencyTypes = dependencyTypes;
        ConfigurationType = configurationType;
        CustomMethods = customMethods;
    }
}