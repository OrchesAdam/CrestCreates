using System;

namespace CrestCreates.Modularity;

/// <summary>
/// 标记一个接口为模块接口，源生成器将为此接口生成实现
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class ModuleInterfaceAttribute : Attribute
{
    /// <summary>
    /// 此模块依赖的其他模块类型
    /// </summary>
    public Type[] DependsOn { get; }
    
    /// <summary>
    /// 模块的配置类型
    /// </summary>
    public Type? ConfigurationType { get; set; }
    
    /// <summary>
    /// 创建模块接口特性
    /// </summary>
    /// <param name="dependsOn">此模块依赖的其他模块类型</param>
    public ModuleInterfaceAttribute(params Type[] dependsOn)
    {
        DependsOn = dependsOn ?? Array.Empty<Type>();
    }
}
