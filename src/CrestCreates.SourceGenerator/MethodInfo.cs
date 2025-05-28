using System.Collections.Generic;

namespace CrestCreates.SourceGenerator;

public class MethodInfo
{
    public string Name { get; set; }
    public string ReturnType { get; set; }
    public bool IsAsync { get; set; }
    public List<ParameterInfo> Parameters { get; set; }

    public MethodInfo(string name, string returnType, bool isAsync, List<ParameterInfo> parameters)
    {
        Name = name;
        ReturnType = returnType;
        IsAsync = isAsync;
        Parameters = parameters;
    }
}