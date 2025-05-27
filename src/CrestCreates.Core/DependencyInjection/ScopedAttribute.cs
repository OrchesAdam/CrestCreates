using System;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DependencyInjection
{
    /// <summary>
    /// 作用域服务注册
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class ScopedAttribute : ServiceAttribute
    {
        public override ServiceLifetime Lifetime => ServiceLifetime.Scoped;
    
        public ScopedAttribute() { }
    
        public ScopedAttribute(Type serviceType)
        {
            ServiceType = serviceType;
        }
    }
}

