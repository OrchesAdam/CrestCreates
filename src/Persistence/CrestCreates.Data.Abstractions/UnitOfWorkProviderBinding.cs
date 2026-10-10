using System;
using CrestCreates.DbContextProvider.Abstract;
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
        /// <param name="ambientContextFactory">
        /// 在隔离子作用域内解析“当前资源上下文”的强类型委托（例如 EF Core 的 <c>IDataBaseContext</c>）。
        /// requiresNew 期间平台将其推入 <see cref="UnitOfWorkAmbientContext"/>，使已注入的业务依赖
        /// （仓储/DbContext 适配器）在操作时跟随当前 UoW；为 null 表示该 Provider 不支持上下文跟随，
        /// 此时 <paramref name="supportsRequiresNew"/> 必须为 false。
        /// </param>
        /// <param name="tenantKeyFactory">
        /// 读取当前租户键的委托（隔离帧记录该键；读方在路由前校验租户一致性）。
        /// 为 null 表示该 Provider 不参与租户身份校验。
        /// </param>
        public UnitOfWorkProviderBinding(
            OrmProvider provider,
            Func<IServiceProvider, IUnitOfWork> factory,
            bool supportsRequiresNew = true,
            Func<IServiceProvider, IDataBaseContext?>? ambientContextFactory = null,
            Func<IServiceProvider, string?>? tenantKeyFactory = null)
        {
            if (supportsRequiresNew && ambientContextFactory is null)
            {
                throw new ArgumentException(
                    $"{nameof(supportsRequiresNew)} requires a non-null {nameof(ambientContextFactory)}: " +
                    "requiresNew isolation is only valid when business dependencies can follow the isolated unit-of-work context.",
                    nameof(ambientContextFactory));
            }

            Provider = provider;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            SupportsRequiresNew = supportsRequiresNew;
            AmbientContextFactory = ambientContextFactory;
            TenantKeyFactory = tenantKeyFactory;
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

        /// <summary>
        /// 隔离子作用域的当前资源上下文解析委托；null 表示不支持上下文跟随（即不支持 requiresNew）。
        /// </summary>
        public Func<IServiceProvider, IDataBaseContext?>? AmbientContextFactory { get; }

        /// <summary>
        /// 当前租户键读取委托；null 表示该绑定不参与租户身份校验。
        /// </summary>
        public Func<IServiceProvider, string?>? TenantKeyFactory { get; }
    }
}
