using System;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DependencyInjection
{
    /// <summary>
    /// 单例服务注册
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class SingletonAttribute : ServiceAttribute
    {
        public override ServiceLifetime Lifetime => ServiceLifetime.Singleton;

        public SingletonAttribute()
        {
        }

        public SingletonAttribute(Type serviceType)
        {
            ServiceType = serviceType;
        }
    }
}

