using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Modularity
{
    /// <summary>
    /// 模块基类
    /// 提供默认的空实现，派生类可以选择性地重写需要的方法
    /// </summary>
    public abstract class ModuleBase : IModule
    {
        /// <summary>
        /// 模块名称
        /// </summary>
        public virtual string Name => GetType().Name;

        /// <summary>
        /// 模块描述
        /// </summary>
        public virtual string? Description => null;

        /// <summary>
        /// 模块版本
        /// </summary>
        public virtual string? Version => null;

        /// <inheritdoc />
        public virtual void OnPreInitialize()
        {
            // 默认空实现
        }

        /// <inheritdoc />
        public virtual void OnInitialize()
        {
            // 默认空实现
        }

        /// <inheritdoc />
        public virtual void OnPostInitialize()
        {
            // 默认空实现
        }

        /// <inheritdoc />
        public virtual void OnConfigureServices(IServiceCollection services)
        {
            // 默认空实现
        }

        /// <inheritdoc />
        public virtual void OnApplicationInitialization(IHost host)
        {
            // 默认空实现
        }
    }
}