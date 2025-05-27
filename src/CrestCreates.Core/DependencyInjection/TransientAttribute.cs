using System;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DependencyInjection
{
    /// <summary>
    /// 瞬态服务注册
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class TransientAttribute : ServiceAttribute
    {
        public override ServiceLifetime Lifetime => ServiceLifetime.Transient;
    
        public TransientAttribute() { }
    
        public TransientAttribute(Type serviceType)
        {
            ServiceType = serviceType;
        }
    }
}

