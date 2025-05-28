using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Modularity;

/// <summary>
/// 模块管理器，用于管理模块的注册、初始化和生命周期
/// </summary>
public class ModuleManager
{
    private readonly List<Type> _moduleTypes = new List<Type>();
    private List<Type>? _sortedModuleTypes;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 初始化模块管理器
    /// </summary>
    /// <param name="serviceProvider">服务提供程序</param>
    public ModuleManager(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// 注册模块类型
    /// </summary>
    /// <param name="moduleType">模块类型</param>
    public void RegisterModule<T>() where T : class, ICrestCreatesModule
    {
        RegisterModule(typeof(T));
    }

    /// <summary>
    /// 注册模块类型
    /// </summary>
    /// <param name="moduleType">模块类型</param>
    public void RegisterModule(Type moduleType)
    {
        if (moduleType == null)
            throw new ArgumentNullException(nameof(moduleType));

        if (!typeof(ICrestCreatesModule).IsAssignableFrom(moduleType))
            throw new ArgumentException($"Module type {moduleType.Name} must implement ICrestCreatesModule", nameof(moduleType));

        if (!_moduleTypes.Contains(moduleType))
        {
            _moduleTypes.Add(moduleType);
            _sortedModuleTypes = null; // 清除排序缓存
        }
    }

    /// <summary>
    /// 注册多个模块类型
    /// </summary>
    /// <param name="moduleTypes">模块类型集合</param>
    public void RegisterModules(params Type[] moduleTypes)
    {
        foreach (var moduleType in moduleTypes)
        {
            RegisterModule(moduleType);
        }
    }

    /// <summary>
    /// 获取按依赖关系排序的模块类型列表
    /// </summary>
    /// <returns>排序后的模块类型列表</returns>
    public List<Type> GetSortedModuleTypes()
    {
        if (_sortedModuleTypes == null)
        {
            _sortedModuleTypes = ModuleTopologicalSorter.Sort(_moduleTypes);
        }
        return _sortedModuleTypes;
    }

    /// <summary>
    /// 获取所有注册的模块类型
    /// </summary>
    /// <returns>模块类型列表</returns>
    public List<Type> GetModuleTypes()
    {
        return new List<Type>(_moduleTypes);
    }

    /// <summary>
    /// 配置所有模块的服务
    /// </summary>
    /// <param name="services">服务集合</param>
    public void ConfigureServices(IServiceCollection services)
    {
        var sortedModules = GetSortedModuleTypes();
        
        // 按依赖关系顺序配置服务
        foreach (var moduleType in sortedModules)
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            module?.ConfigureServices(services);
        }
    }

    /// <summary>
    /// 异步配置所有模块的服务
    /// </summary>
    /// <param name="services">服务集合</param>
    public async Task ConfigureServicesAsync(IServiceCollection services)
    {
        var sortedModules = GetSortedModuleTypes();
        
        // 按依赖关系顺序配置服务
        foreach (var moduleType in sortedModules)
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            if (module != null)
            {
                await module.ConfigureServicesAsync(services);
            }
        }
    }

    /// <summary>
    /// 执行所有模块的应用程序初始化前操作
    /// </summary>
    public void PreApplicationInitialization()
    {
        var sortedModules = GetSortedModuleTypes();
        
        foreach (var moduleType in sortedModules)
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            if (module is IOnPreApplicationInitialization preInitModule)
            {
                preInitModule.OnPreApplicationInitialization();
            }
        }
    }

    /// <summary>
    /// 异步执行所有模块的应用程序初始化前操作
    /// </summary>
    public async Task PreApplicationInitializationAsync()
    {
        var sortedModules = GetSortedModuleTypes();
        
        foreach (var moduleType in sortedModules)
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            if (module is IOnPreApplicationInitialization preInitModule)
            {
                await preInitModule.OnPreApplicationInitializationAsync();
            }
        }
    }

    /// <summary>
    /// 执行所有模块的应用程序初始化后操作
    /// </summary>
    public void PostApplicationInitialization()
    {
        var sortedModules = GetSortedModuleTypes();
        
        foreach (var moduleType in sortedModules)
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            if (module is IOnPostApplicationInitialization postInitModule)
            {
                postInitModule.OnPostApplicationInitialization();
            }
        }
    }

    /// <summary>
    /// 异步执行所有模块的应用程序初始化后操作
    /// </summary>
    public async Task PostApplicationInitializationAsync()
    {
        var sortedModules = GetSortedModuleTypes();
        
        foreach (var moduleType in sortedModules)
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            if (module is IOnPostApplicationInitialization postInitModule)
            {
                await postInitModule.OnPostApplicationInitializationAsync();
            }
        }
    }

    /// <summary>
    /// 执行所有模块的应用程序关闭前操作
    /// </summary>
    public void PreApplicationShutdown()
    {
        var sortedModules = GetSortedModuleTypes();
        
        // 按与初始化相反的顺序执行关闭前操作
        foreach (var moduleType in sortedModules.AsEnumerable().Reverse())
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            if (module is IOnPreApplicationShutdown preShutdownModule)
            {
                preShutdownModule.OnPreApplicationShutdown();
            }
        }
    }

    /// <summary>
    /// 异步执行所有模块的应用程序关闭前操作
    /// </summary>
    public async Task PreApplicationShutdownAsync()
    {
        var sortedModules = GetSortedModuleTypes();
        
        // 按与初始化相反的顺序执行关闭前操作
        foreach (var moduleType in sortedModules.AsEnumerable().Reverse())
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            if (module is IOnPreApplicationShutdown preShutdownModule)
            {
                await preShutdownModule.OnPreApplicationShutdownAsync();
            }
        }
    }

    /// <summary>
    /// 执行所有模块的应用程序关闭后操作
    /// </summary>
    public void PostApplicationShutdown()
    {
        var sortedModules = GetSortedModuleTypes();
        
        // 按与初始化相反的顺序执行关闭后操作
        foreach (var moduleType in sortedModules.AsEnumerable().Reverse())
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            if (module is IOnPostApplicationShutdown postShutdownModule)
            {
                postShutdownModule.OnPostApplicationShutdown();
            }
        }
    }

    /// <summary>
    /// 异步执行所有模块的应用程序关闭后操作
    /// </summary>
    public async Task PostApplicationShutdownAsync()
    {
        var sortedModules = GetSortedModuleTypes();
        
        // 按与初始化相反的顺序执行关闭后操作
        foreach (var moduleType in sortedModules.AsEnumerable().Reverse())
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            if (module is IOnPostApplicationShutdown postShutdownModule)
            {
                await postShutdownModule.OnPostApplicationShutdownAsync();
            }
        }
    }

    /// <summary>
    /// 检查是否存在循环依赖
    /// </summary>
    /// <returns>如果存在循环依赖则返回true</returns>
    public bool HasCircularDependencies()
    {
        return ModuleTopologicalSorter.HasCircularDependencies(_moduleTypes);
    }

    /// <summary>
    /// 获取模块的所有传递依赖项
    /// </summary>
    /// <typeparam name="T">模块类型</typeparam>
    /// <returns>传递依赖项集合</returns>
    public HashSet<Type> GetTransitiveDependencies<T>() where T : ICrestCreatesModule
    {
        return GetTransitiveDependencies(typeof(T));
    }

    /// <summary>
    /// 获取模块的所有传递依赖项
    /// </summary>
    /// <param name="moduleType">模块类型</param>
    /// <returns>传递依赖项集合</returns>
    public HashSet<Type> GetTransitiveDependencies(Type moduleType)
    {
        return ModuleTopologicalSorter.GetTransitiveDependencies(moduleType, _moduleTypes);
    }

    /// <summary>
    /// 获取所有模块实例
    /// </summary>
    /// <returns>模块实例集合</returns>
    public IEnumerable<ICrestCreatesModule> GetModules()
    {
        foreach (var moduleType in _moduleTypes)
        {
            var module = (ICrestCreatesModule)_serviceProvider.GetService(moduleType);
            if (module != null)
            {
                yield return module;
            }
        }
    }    /// <summary>
    /// 根据类型获取模块实例
    /// </summary>
    /// <typeparam name="T">模块类型</typeparam>
    /// <returns>模块实例</returns>
    public T? GetModule<T>() where T : class, ICrestCreatesModule
    {
        return _serviceProvider.GetService<T>();
    }

    /// <summary>
    /// 异步初始化所有模块
    /// </summary>
    public async Task InitializeModulesAsync()
    {
        await PreApplicationInitializationAsync();
        await PostApplicationInitializationAsync();
    }

    /// <summary>
    /// 关闭所有模块
    /// </summary>
    public void ShutdownModules()
    {
        PreApplicationShutdown();
        PostApplicationShutdown();
    }

    /// <summary>
    /// 异步关闭所有模块
    /// </summary>
    public async Task ShutdownModulesAsync()
    {
        await PreApplicationShutdownAsync();
        await PostApplicationShutdownAsync();
    }
}
