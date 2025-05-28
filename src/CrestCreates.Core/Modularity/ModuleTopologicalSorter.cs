using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CrestCreates.Modularity;

/// <summary>
/// 模块拓扑排序器，用于按依赖关系顺序排序模块
/// </summary>
public static class ModuleTopologicalSorter
{
    /// <summary>
    /// 按依赖关系对模块类型进行拓扑排序
    /// </summary>
    /// <param name="moduleTypes">要排序的模块类型集合</param>
    /// <returns>按依赖关系排序后的模块类型列表</returns>
    public static List<Type> Sort(IEnumerable<Type> moduleTypes)
    {
        var modules = moduleTypes.ToList();
        var visited = new Dictionary<Type, bool>();
        var temporaryMarked = new HashSet<Type>();
        var sortedModules = new List<Type>();

        foreach (var moduleType in modules)
        {
            if (!visited.ContainsKey(moduleType) && !temporaryMarked.Contains(moduleType))
            {
                Visit(moduleType, modules, visited, temporaryMarked, sortedModules);
            }
        }

        return sortedModules;
    }

    /// <summary>
    /// 递归访问模块及其依赖项
    /// </summary>
    private static void Visit(Type moduleType, List<Type> allModules, Dictionary<Type, bool> visited, 
        HashSet<Type> temporaryMarked, List<Type> sortedModules)
    {
        // 如果已经在结果列表中，直接返回
        if (sortedModules.Contains(moduleType))
        {
            return;
        }

        // 检查是否有循环依赖
        if (!temporaryMarked.Add(moduleType))
        {
            // 发现循环依赖，记录警告并继续
            System.Diagnostics.Debug.WriteLine($"Warning: Circular dependency detected for module {moduleType.FullName}");
            return;
        }

        // 临时标记为正在访问

        // 获取模块的依赖项
        var dependencies = GetModuleDependencies(moduleType, allModules);

        // 先访问所有依赖项
        foreach (var dependency in dependencies)
        {
            if (!sortedModules.Contains(dependency))
            {
                Visit(dependency, allModules, visited, temporaryMarked, sortedModules);
            }
        }

        // 移除临时标记
        temporaryMarked.Remove(moduleType);

        // 标记为已访问完成
        visited[moduleType] = true;

        // 添加到结果列表（如果尚未添加）
        if (!sortedModules.Contains(moduleType))
        {
            sortedModules.Add(moduleType);
        }    }

    /// <summary>
    /// 获取模块的直接依赖项
    /// </summary>
    /// <param name="moduleType">模块类型</param>
    /// <returns>直接依赖项集合</returns>
    public static IEnumerable<Type> GetModuleDependencies(Type moduleType)
    {
        try
        {
            // 查找DependsOnAttribute
            var dependsOnAttribute = moduleType.GetCustomAttribute<DependsOnAttribute>();
            if (dependsOnAttribute?.Dependencies != null)
            {
                return dependsOnAttribute.Dependencies;
            }

            // 向后兼容：检查ModuleAttribute
            var moduleAttribute = moduleType.GetCustomAttribute<ModuleAttribute>();
            if (moduleAttribute?.Dependencies != null)
            {
                return moduleAttribute.Dependencies;
            }
        }
        catch (Exception ex)
        {
            // 如果在获取依赖项过程中发生异常，记录错误日志
            System.Diagnostics.Debug.WriteLine($"Error getting dependencies for module {moduleType.Name}: {ex.Message}");
        }

        return [];
    }

    /// <summary>
    /// 获取模块类型的依赖项（内部实现）
    /// </summary>
    private static IEnumerable<Type> GetModuleDependencies(Type moduleType, List<Type> availableModules)
    {
        try
        {
            // 查找DependsOnAttribute
            var dependsOnAttribute = moduleType.GetCustomAttribute<DependsOnAttribute>();
            if (dependsOnAttribute?.Dependencies != null)
            {
                // 过滤出有效的依赖项（在可用模块中存在的）
                var validDependencies = new List<Type>();
                foreach (var dependency in dependsOnAttribute.Dependencies)
                {
                    if (availableModules.Contains(dependency))
                    {
                        validDependencies.Add(dependency);
                    }
                    else
                    {
                        // 记录未找到的依赖项，可能是未注册的模块或者拼写错误
                        System.Diagnostics.Debug.WriteLine(
                            $"Warning: Module {moduleType.Name} depends on {dependency.Name}, but it's not found in registered modules");
                    }
                }

                return validDependencies;
            }

            // 向后兼容：检查ModuleAttribute
            var moduleAttribute = moduleType.GetCustomAttribute<ModuleAttribute>();
            if (moduleAttribute?.Dependencies != null)
            {
                var validDependencies = new List<Type>();
                foreach (var dependency in moduleAttribute.Dependencies)
                {
                    if (availableModules.Contains(dependency))
                    {
                        validDependencies.Add(dependency);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Warning: Module {moduleType.Name} depends on {dependency.Name}, but it's not found in registered modules");
                    }
                }

                return validDependencies;
            }
        }
        catch (Exception ex)
        {
            // 如果在获取依赖项过程中发生异常，记录错误日志
            System.Diagnostics.Debug.WriteLine($"Error getting dependencies for module {moduleType.Name}: {ex.Message}");
        }

        return [];
    }

    /// <summary>
    /// 验证模块依赖关系是否存在循环依赖
    /// </summary>
    /// <param name="moduleTypes">要验证的模块类型集合</param>
    /// <returns>如果存在循环依赖则返回true</returns>
    public static bool HasCircularDependencies(IEnumerable<Type> moduleTypes)
    {
        var modules = moduleTypes.ToList();
        var visited = new HashSet<Type>();
        var recursionStack = new HashSet<Type>();

        foreach (var moduleType in modules)
        {
            if (HasCircularDependenciesHelper(moduleType, modules, visited, recursionStack))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 循环依赖检测的辅助方法
    /// </summary>
    private static bool HasCircularDependenciesHelper(Type moduleType, List<Type> allModules, 
        HashSet<Type> visited, HashSet<Type> recursionStack)
    {
        if (recursionStack.Contains(moduleType))
        {
            return true; // 发现循环依赖
        }

        if (!visited.Add(moduleType))
        {
            return false; // 已经访问过且无循环依赖
        }

        recursionStack.Add(moduleType);

        var dependencies = GetModuleDependencies(moduleType, allModules);
        foreach (var dependency in dependencies)
        {
            if (HasCircularDependenciesHelper(dependency, allModules, visited, recursionStack))
            {
                return true;
            }
        }

        recursionStack.Remove(moduleType);
        return false;
    }

    /// <summary>
    /// 获取模块的所有传递依赖项
    /// </summary>
    /// <param name="moduleType">模块类型</param>
    /// <param name="availableModules">可用的模块类型列表</param>
    /// <returns>传递依赖项集合</returns>
    public static HashSet<Type> GetTransitiveDependencies(Type moduleType, List<Type> availableModules)
    {
        var result = new HashSet<Type>();
        var visited = new HashSet<Type>();

        GetTransitiveDependenciesHelper(moduleType, availableModules, result, visited);
        
        return result;
    }

    /// <summary>
    /// 获取传递依赖项的辅助方法
    /// </summary>
    private static void GetTransitiveDependenciesHelper(Type moduleType, List<Type> availableModules, 
        HashSet<Type> result, HashSet<Type> visited)
    {
        if (!visited.Add(moduleType))
        {
            return;
        }

        var dependencies = GetModuleDependencies(moduleType, availableModules);
        foreach (var dependency in dependencies)
        {
            result.Add(dependency);
            GetTransitiveDependenciesHelper(dependency, availableModules, result, visited);
        }
    }
}
