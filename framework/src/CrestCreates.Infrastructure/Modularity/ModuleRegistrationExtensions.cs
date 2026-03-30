using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Infrastructure.Modularity
{
    public static class ModuleRegistrationExtensions
    {
        public static IHostBuilder RegisterModules(this IHostBuilder hostBuilder)
        {
            // 这个方法在源代码生成器中会被覆盖，这里只是为了避免编译错误
            return hostBuilder.ConfigureServices((context, services) =>
            {
                // 在生产环境中，这个方法体会被源代码生成器替换
                // 此处为运行时代码，仅供开发调试使用
                
                // 查找所有实现了 ModuleBase 的类型
                var moduleBaseType = typeof(ModuleBase);
                var moduleTypes = new List<Type>();
                
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetExportedTypes())
                        {
                            if (moduleBaseType.IsAssignableFrom(type) && !type.IsAbstract)
                            {
                                moduleTypes.Add(type);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // 忽略加载失败的程序集
                    }
                }
                
                // 注册发现的模块
                foreach (var moduleType in moduleTypes)
                {
                    services.AddSingleton(moduleType, Activator.CreateInstance(moduleType));
                }
            });
        }
        
        public static IHost InitializeModules(this IHost host)
        {
            // 这个方法在源代码生成器中会被覆盖，这里只是为了避免编译错误
            var serviceProvider = host.Services;
            
            foreach (var module in serviceProvider.GetServices<ModuleBase>())
            {
                try
                {
                    module.OnPreInitialize();
                    module.OnInitialize();
                    module.OnPostInitialize();
                    module.OnApplicationInitialization(host);
                }
                catch (Exception ex)
                {
                    // 记录错误但不中断进程
                    Console.WriteLine($"Module initialization failed: {ex.Message}");
                }
            }
            
            return host;
        }
    }
}
