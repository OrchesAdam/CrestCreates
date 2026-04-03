using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Modularity
{
    /// <summary>
    /// 模块描述符
    /// </summary>
    public readonly struct ModuleDescriptor
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="moduleType">模块类型</param>
        /// <param name="order">排序顺序</param>
        /// <param name="autoRegisterServices">是否自动注册服务</param>
        public ModuleDescriptor(Type moduleType, int order, bool autoRegisterServices)
        {
            ModuleType = moduleType;
            Order = order;
            AutoRegisterServices = autoRegisterServices;
        }

        /// <summary>
        /// 模块类型
        /// </summary>
        public Type ModuleType { get; }

        /// <summary>
        /// 排序顺序
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// 是否自动注册服务
        /// </summary>
        public bool AutoRegisterServices { get; }
    }

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