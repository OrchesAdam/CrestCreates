using System.Collections.Generic;

namespace CrestCreates.SourceGenerator.Model;

/// <summary>
/// 方法信息模型
/// </summary>
public class MethodInfo
{
    public string Name { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public bool IsAsync { get; set; }
    public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();

    public MethodInfo() { }

    public MethodInfo(string name, string returnType, bool isAsync, List<ParameterInfo> parameters)
    {
        Name = name;
        ReturnType = returnType;
        IsAsync = isAsync;
        Parameters = parameters;
    }
}