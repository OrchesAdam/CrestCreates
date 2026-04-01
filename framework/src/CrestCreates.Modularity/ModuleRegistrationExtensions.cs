using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Modularity
{
    public static class ModuleRegistrationExtensions
    {
        public static IHostBuilder RegisterAllModules(this IHostBuilder hostBuilder)
        {
            return hostBuilder.ConfigureServices((context, services) =>
            {
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
                    }
                }

                foreach (var moduleType in moduleTypes)
                {
                    services.AddSingleton(moduleType, Activator.CreateInstance(moduleType));
                }
            });
        }

        public static IHost InitializeAllModules(this IHost host)
        {
            var moduleBaseType = typeof(ModuleBase);
            var sp = host.Services;

            foreach (var moduleType in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in moduleType.GetExportedTypes())
                    {
                        if (moduleBaseType.IsAssignableFrom(type) && !type.IsAbstract)
                        {
                            var module = sp.GetService(type) as IModule;
                            module?.OnApplicationInitialization(host);
                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            return host;
        }
    }
}