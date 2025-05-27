using System;

namespace CrestCreates.Modularity;

/// <summary>
/// 模块特性
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ModuleAttribute: Attribute
{
    public Type[] Dependencies { get; }

    public ModuleAttribute(params Type[] dependencies)
    {
        Dependencies = dependencies;
    }
}