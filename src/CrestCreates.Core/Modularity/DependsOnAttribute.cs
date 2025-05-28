using System;

namespace CrestCreates.Modularity;

/// <summary>
/// 标记一个模块依赖于其他模块的特性，类似于ABP的DependsOn特性
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = true)]
public class DependsOnAttribute : Attribute
{
    /// <summary>
    /// 此模块依赖的其他模块类型
    /// </summary>
    public Type[] Dependencies { get; }
    
    /// <summary>
    /// 创建依赖特性
    /// </summary>
    /// <param name="dependencies">此模块依赖的其他模块类型</param>
    public DependsOnAttribute(params Type[] dependencies)
    {
        Dependencies = dependencies ?? Array.Empty<Type>();
    }
}
