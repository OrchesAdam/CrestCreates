using System;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DependencyInjection
{
    /// <summary>
    /// 基础服务注册属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public abstract class ServiceAttribute : Attribute
    {
        /// <summary>
        /// 服务类型，如果为null则自动推断
        /// </summary>
        public Type? ServiceType { get; set; }
    
        /// <summary>
        /// 服务生命周期
        /// </summary>
        public abstract ServiceLifetime Lifetime { get; }
    
        /// <summary>
        /// 实现类型，通常由编译器自动填充
        /// </summary>
        public Type? ImplementationType { get; set; }
    
        /// <summary>
        /// 是否尝试注册为所有实现的接口
        /// </summary>
        public bool RegisterAsImplementedInterfaces { get; set; } = false;
    
        /// <summary>
        /// 是否替换已存在的服务注册
        /// </summary>
        public bool Replace { get; set; } = false;
    }
}

