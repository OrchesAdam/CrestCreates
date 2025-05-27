using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CrestCreates.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Extensions.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加程序集中标记的服务
        /// </summary>
        public static IServiceCollection AddServicesFromAssembly(this IServiceCollection services, Assembly assembly)
        {
            var registrarTypes = GetServiceRegistrars(assembly);

            foreach (var registrarType in registrarTypes)
            {
                var provider = services.BuildServiceProvider();
                var registrar = ActivatorUtilities.CreateInstance(provider, registrarType) as IServiceRegistrar;
                registrar?.RegisterServices(services);
            }

            return services;
        }

        /// <summary>
        /// 添加当前程序集中标记的服务
        /// </summary>
        public static IServiceCollection AddServicesFromCallingAssembly(this IServiceCollection services)
        {
            var callingAssembly = Assembly.GetCallingAssembly();
            return services.AddServicesFromAssembly(callingAssembly);
        }

        /// <summary>
        /// 添加入口程序集中标记的服务
        /// </summary>
        public static IServiceCollection AddServicesFromEntryAssembly(this IServiceCollection services)
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null)
            {
                return services.AddServicesFromAssembly(entryAssembly);
            }

            return services;
        }

        /// <summary>
        /// 添加多个程序集中标记的服务
        /// </summary>
        public static IServiceCollection AddServicesFromAssemblies(this IServiceCollection services,
            params Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                services.AddServicesFromAssembly(assembly);
            }

            return services;
        }

        /// <summary>
        /// 添加类型所在程序集中标记的服务
        /// </summary>
        public static IServiceCollection AddServicesFromAssemblyContaining<T>(this IServiceCollection services)
        {
            return services.AddServicesFromAssembly(typeof(T).Assembly);
        }

        /// <summary>
        /// 添加类型所在程序集中标记的服务
        /// </summary>
        public static IServiceCollection AddServicesFromAssemblyContaining(this IServiceCollection services, Type type)
        {
            return services.AddServicesFromAssembly(type.Assembly);
        }

        private static IEnumerable<Type> GetServiceRegistrars(Assembly assembly)
        {
            var attributes = assembly.GetCustomAttributes();
            // 查找生成的服务注册器
            var generatedRegistrarAttributes = attributes
                .Where(attr => attr.GetType().Name == ServiceRegistrationConstDefine.GeneratedServiceRegistrarAttributeName)
                .ToList();

            foreach (var attr in generatedRegistrarAttributes)
            {
                var registrarTypeProperty = attr.GetType().GetProperty("RegistrarType");
                if (registrarTypeProperty?.GetValue(attr) is Type registrarType)
                {
                    yield return registrarType;
                }
            }
        }
    }
}