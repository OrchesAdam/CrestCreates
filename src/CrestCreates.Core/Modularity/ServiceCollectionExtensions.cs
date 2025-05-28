using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Modularity;

/// <summary>
/// 服务集合扩展，用于模块系统的集成
/// </summary>
public static class ServiceCollectionExtensions
{    /// <summary>
    /// 添加CrestCreates模块系统
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="moduleTypes">要注册的模块类型</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCrestCreatesModules(this IServiceCollection services, params Type[] moduleTypes)
    {
        // 注册模块管理器
        services.AddSingleton<ModuleManager>();
        
        // 注册所有模块类型
        foreach (var moduleType in moduleTypes)
        {
            if (typeof(ICrestCreatesModule).IsAssignableFrom(moduleType))
            {
                services.AddSingleton(moduleType);
            }
        }

        return services;
    }

    /// <summary>
    /// 添加CrestCreates模块系统，使用配置回调
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureModules">模块配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCrestCreatesModules(this IServiceCollection services, Action<ModuleManager> configureModules)
    {
        // 注册模块管理器
        services.AddSingleton<ModuleManager>();
        
        // 创建临时的服务提供者来获取模块管理器进行配置
        var tempServiceProvider = services.BuildServiceProvider();
        var moduleManager = tempServiceProvider.GetRequiredService<ModuleManager>();
        
        // 执行模块配置
        configureModules(moduleManager);
          // 获取所有注册的模块类型并注册到服务容器
        var moduleTypes = moduleManager.GetModuleTypes();
        foreach (var moduleType in moduleTypes)
        {
            if (typeof(ICrestCreatesModule).IsAssignableFrom(moduleType))
            {
                // 注册实现类型
                services.AddSingleton(moduleType);
                
                // 注册所有实现的接口（除了ICrestCreatesModule基接口）
                var interfaces = moduleType.GetInterfaces()
                    .Where(i => i != typeof(ICrestCreatesModule) && 
                               typeof(ICrestCreatesModule).IsAssignableFrom(i))
                    .ToArray();
                
                foreach (var interfaceType in interfaces)
                {
                    services.AddSingleton(interfaceType, provider => provider.GetRequiredService(moduleType));
                }
            }
        }

        // 配置模块服务
        moduleManager.ConfigureServices(services);

        return services;
    }

    /// <summary>
    /// 添加CrestCreates模块系统，并从程序集中自动发现模块
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="assemblies">要扫描的程序集</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCrestCreatesModules(this IServiceCollection services, params Assembly[] assemblies)
    {
        var moduleTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(ICrestCreatesModule).IsAssignableFrom(type) && 
                          !type.IsInterface && 
                          !type.IsAbstract)
            .ToArray();

        return AddCrestCreatesModules(services, moduleTypes);
    }

    /// <summary>
    /// 添加CrestCreates模块系统，从当前执行程序集中自动发现模块
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCrestCreatesModules(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();
        return AddCrestCreatesModules(services, assembly);
    }

    /// <summary>
    /// 配置模块系统
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="moduleTypes">要配置的模块类型</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureCrestCreatesModules(this IServiceCollection services, params Type[] moduleTypes)
    {
        var serviceProvider = services.BuildServiceProvider();
        var moduleManager = serviceProvider.GetRequiredService<ModuleManager>();

        // 注册模块类型到管理器
        moduleManager.RegisterModules(moduleTypes);

        // 检查循环依赖
        if (moduleManager.HasCircularDependencies())
        {
            throw new InvalidOperationException("Circular dependencies detected in module configuration. Please check your module dependencies.");
        }

        // 配置模块服务
        moduleManager.ConfigureServices(services);

        return services;
    }

    /// <summary>
    /// 使用模块系统初始化应用程序
    /// </summary>
    /// <param name="serviceProvider">服务提供程序</param>
    public static void InitializeCrestCreatesModules(this IServiceProvider serviceProvider)
    {
        var moduleManager = serviceProvider.GetRequiredService<ModuleManager>();
        
        // 执行应用程序初始化前操作
        moduleManager.PreApplicationInitialization();
        
        // 执行应用程序初始化后操作
        moduleManager.PostApplicationInitialization();
    }

    /// <summary>
    /// 异步使用模块系统初始化应用程序
    /// </summary>
    /// <param name="serviceProvider">服务提供程序</param>
    public static async System.Threading.Tasks.Task InitializeCrestCreatesModulesAsync(this IServiceProvider serviceProvider)
    {
        var moduleManager = serviceProvider.GetRequiredService<ModuleManager>();
        
        // 执行应用程序初始化前操作
        await moduleManager.PreApplicationInitializationAsync();
        
        // 执行应用程序初始化后操作
        await moduleManager.PostApplicationInitializationAsync();
    }

    /// <summary>
    /// 使用模块系统关闭应用程序
    /// </summary>
    /// <param name="serviceProvider">服务提供程序</param>
    public static void ShutdownCrestCreatesModules(this IServiceProvider serviceProvider)
    {
        var moduleManager = serviceProvider.GetRequiredService<ModuleManager>();
        
        // 执行应用程序关闭前操作
        moduleManager.PreApplicationShutdown();
        
        // 执行应用程序关闭后操作
        moduleManager.PostApplicationShutdown();
    }

    /// <summary>
    /// 异步使用模块系统关闭应用程序
    /// </summary>
    /// <param name="serviceProvider">服务提供程序</param>
    public static async System.Threading.Tasks.Task ShutdownCrestCreatesModulesAsync(this IServiceProvider serviceProvider)
    {
        var moduleManager = serviceProvider.GetRequiredService<ModuleManager>();
        
        // 执行应用程序关闭前操作
        await moduleManager.PreApplicationShutdownAsync();
        
        // 执行应用程序关闭后操作
        await moduleManager.PostApplicationShutdownAsync();
    }
}
