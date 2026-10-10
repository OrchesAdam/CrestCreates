using System;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 显式 Provider 绑定：ORM Provider → 强类型工作单元工厂委托。
    /// </summary>
    /// <remarks>
    /// 由 Provider 包在其注册代码中声明（例如 <c>sp =&gt; sp.GetRequiredService&lt;EfCoreUnitOfWork&gt;()</c>），
    /// 普通业务代码不声明 Provider 身份与构造信息。
    /// 委托为静态强类型，不包含反射、类型名字符串或运行时扫描。
    /// </remarks>
    public sealed class UnitOfWorkProviderBinding
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="provider">ORM 提供者类型</param>
        /// <param name="factory">在当前作用域内解析工作单元的强类型委托</param>
        /// <param name="supportsRequiresNew">该 Provider 是否支持 requiresNew 隔离（独立作用域/连接/事务）</param>
        public UnitOfWorkProviderBinding(
            OrmProvider provider,
            Func<IServiceProvider, IUnitOfWork> factory,
            bool supportsRequiresNew = true)
        {
            Provider = provider;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            SupportsRequiresNew = supportsRequiresNew;
        }

        /// <summary>
        /// ORM 提供者类型
        /// </summary>
        public OrmProvider Provider { get; }

        /// <summary>
        /// 工作单元创建委托（在调用方作用域内解析强类型依赖）
        /// </summary>
        public Func<IServiceProvider, IUnitOfWork> Factory { get; }

        /// <summary>
        /// 该 Provider 是否支持 requiresNew 隔离。
        /// 不支持时，管理器在使用 requiresNew 时给出确定性诊断而不是静默共享事务上下文。
        /// </summary>
        public bool SupportsRequiresNew { get; }
    }
}
