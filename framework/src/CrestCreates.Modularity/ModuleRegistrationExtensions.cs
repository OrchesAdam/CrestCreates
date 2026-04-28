using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Modularity
{
    /// <summary>
    /// 反射回退的模块注册扩展方法。
    /// 仅在 SourceGenerator 和 BuildTasks 路径不可用时使用。
    /// 优先使用编译时生成的 ModuleAutoInitializer。
    /// </summary>
    public static class ModuleRegistrationExtensions
    {
        public static IHostBuilder RegisterAllModules(this IHostBuilder hostBuilder)
        {
            return hostBuilder.ConfigureServices((context, services) =>
            {
                var moduleTypes = DiscoverModuleTypes();

                foreach (var moduleType in moduleTypes)
                {
                    services.AddSingleton(moduleType);
                }

                // 执行 OnConfigureServices 生命周期
                foreach (var moduleType in moduleTypes)
                {
                    var module = (IModule)Activator.CreateInstance(moduleType);
                    module.OnConfigureServices(services);
                }
            });
        }

        public static IHost InitializeAllModules(this IHost host)
        {
            var sp = host.Services;
            var moduleTypes = DiscoverModuleTypes();

            foreach (var moduleType in moduleTypes)
            {
                var module = sp.GetService(moduleType) as IModule;
                module?.OnPreInitialize();
            }

            foreach (var moduleType in moduleTypes)
            {
                var module = sp.GetService(moduleType) as IModule;
                module?.OnInitialize();
            }

            foreach (var moduleType in moduleTypes)
            {
                var module = sp.GetService(moduleType) as IModule;
                module?.OnPostInitialize();
            }

            foreach (var moduleType in moduleTypes)
            {
                var module = sp.GetService(moduleType) as IModule;
                module?.OnApplicationInitialization(host);
            }

            return host;
        }

        private static List<Type> DiscoverModuleTypes()
        {
            var moduleTypes = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (typeof(IModule).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                        {
                            moduleTypes.Add(type);
                        }
                    }
                }
                catch
                {
                    // Skip assemblies that can't be loaded
                }
            }

            return moduleTypes;
        }
    }
}