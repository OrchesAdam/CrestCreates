using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Extensions.DependencyInjection
{
    public static class ServiceProviderExtensions
    {
        /// <summary>
        /// 获取所有指定类型的服务
        /// </summary>
        public static IEnumerable<T> GetServices<T>(this IServiceProvider serviceProvider)
        {
            return serviceProvider.GetRequiredService<IEnumerable<T>>();
        }

        /// <summary>
        /// 尝试获取服务，如果不存在则返回null
        /// </summary>
        public static T? TryGetService<T>(this IServiceProvider serviceProvider) where T : class
        {
            return serviceProvider.GetService<T>();
        }

        /// <summary>
        /// 获取服务，如果不存在则使用默认值
        /// </summary>
        public static T GetServiceOrDefault<T>(this IServiceProvider serviceProvider, T defaultValue) where T : class
        {
            return serviceProvider.GetService<T>() ?? defaultValue;
        }

        /// <summary>
        /// 检查服务是否已注册
        /// </summary>
        public static bool IsServiceRegistered<T>(this IServiceProvider serviceProvider)
        {
            return serviceProvider.GetService<T>() != null;
        }

        /// <summary>
        /// 检查服务是否已注册
        /// </summary>
        public static bool IsServiceRegistered(this IServiceProvider serviceProvider, Type serviceType)
        {
            return serviceProvider.GetService(serviceType) != null;
        }
    }
}

