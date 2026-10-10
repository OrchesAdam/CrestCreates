using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 工作单元依赖注入扩展：唯一装配入口。
    /// </summary>
    public static class UnitOfWorkServiceCollectionExtensions
    {
        /// <summary>
        /// 注册工作单元服务（工厂 + 管理器）与默认 Provider 声明。
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="defaultProvider">
        /// 应用显式声明的默认 ORM 提供者。为 null 时按装配规则解析：
        /// 恰好注册一个 Provider 绑定时该 Provider 即默认；多绑定且无声明时，未显式传入 provider 的调用会确定性失败。
        /// 重复调用相同值幂等；不同值立即抛出确定性异常（不允许 last-wins）。
        /// </param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddUnitOfWork(
            this IServiceCollection services,
            OrmProvider? defaultProvider = null)
        {
            var state = GetOrCreateRegistrationState(services);
            state.SetAssemblyMode("provider-bindings", $"{nameof(AddUnitOfWork)}(...)");
            if (defaultProvider is not null)
            {
                state.SetExplicitDefault(defaultProvider.Value, $"{nameof(AddUnitOfWork)}(defaultProvider)");
            }

            services.TryAddSingleton(sp =>
                new UnitOfWorkProviderBindingRegistry(sp.GetServices<UnitOfWorkProviderBinding>()));
            services.TryAddScoped<UnitOfWorkChainNode>();
            services.TryAddScoped<IUnitOfWorkFactory>(sp => new UnitOfWorkFactory(
                sp,
                sp.GetRequiredService<UnitOfWorkProviderBindingRegistry>()));
            services.TryAddScoped<IUnitOfWorkManager>(sp => new UnitOfWorkManager(
                sp.GetRequiredService<IUnitOfWorkFactory>(),
                sp.GetRequiredService<UnitOfWorkProviderBindingRegistry>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                state.ExplicitDefault,
                sp.GetRequiredService<UnitOfWorkChainNode>(),
                sp));

            return services;
        }

        /// <summary>
        /// 注册工作单元服务（使用自定义工厂）。
        /// </summary>
        /// <typeparam name="TFactory">工作单元工厂类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <param name="defaultProvider">默认 ORM 提供者；为 null 时使用 EfCore（自定义工厂自行解释 Provider 语义）</param>
        /// <returns>服务集合</returns>
        /// <remarks>
        /// 自定义工厂扩展点：不经过 Provider 绑定索引，由 <typeparamref name="TFactory"/> 自行解析 Provider。
        /// </remarks>
        public static IServiceCollection AddUnitOfWork<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>(
            this IServiceCollection services,
            OrmProvider? defaultProvider = null)
            where TFactory : class, IUnitOfWorkFactory
        {
            var state = GetOrCreateRegistrationState(services);
            state.SetAssemblyMode(
                $"custom-factory:{typeof(TFactory).FullName}",
                $"{nameof(AddUnitOfWork)}<{typeof(TFactory).Name}>(...)");
            if (defaultProvider is not null)
            {
                state.SetExplicitDefault(defaultProvider.Value, $"{nameof(AddUnitOfWork)}<{typeof(TFactory).Name}>(defaultProvider)");
            }

            services.TryAddScoped<IUnitOfWorkFactory, TFactory>();
            services.TryAddScoped<IUnitOfWorkManager>(sp => new UnitOfWorkManager(
                sp.GetRequiredService<IUnitOfWorkFactory>(),
                state.ExplicitDefault ?? OrmProvider.EfCore));

            return services;
        }

        /// <summary>
        /// 注册 Provider 的强类型工作单元绑定。
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="provider">ORM 提供者类型</param>
        /// <param name="factory">在当前作用域解析工作单元的强类型委托（不得使用反射或类型名字符串）</param>
        /// <param name="supportsRequiresNew">
        /// 该 Provider 是否支持 requiresNew 隔离。为 true 时必须同时提供
        /// <paramref name="ambientContextFactory"/>：requiresNew 期间平台把内层上下文推入
        /// <see cref="UnitOfWorkAmbientContext"/>，已注入的业务依赖据此跟随当前 UoW。
        /// </param>
        /// <param name="ambientContextFactory">
        /// 在隔离子作用域内解析“当前资源上下文”的强类型委托（例如 EF 的 <c>IDataBaseContext</c>）。
        /// </param>
        /// <param name="tenantKeyFactory">
        /// 读取当前租户键的委托（隔离帧记录该键；读方在路由前校验租户一致性）。为 null 表示不参与租户身份校验。
        /// </param>
        /// <param name="capabilities">
        /// Provider 能力声明（隔离级别集合、终结/丢弃能力等）；内核在执行业务前校验。null 使用默认声明。
        /// </param>
        /// <returns>服务集合</returns>
        /// <remarks>
        /// 由 Provider 包/模块声明。同一 Provider 出现多个绑定会在装配完成时给出确定性异常，不允许 last-wins。
        /// </remarks>
        public static IServiceCollection AddUnitOfWorkProvider(
            this IServiceCollection services,
            OrmProvider provider,
            Func<IServiceProvider, IUnitOfWork> factory,
            bool supportsRequiresNew = true,
            Func<IServiceProvider, IDataBaseContext?>? ambientContextFactory = null,
            Func<IServiceProvider, string?>? tenantKeyFactory = null,
            UnitOfWorkProviderCapabilities? capabilities = null)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            services.AddSingleton(new UnitOfWorkProviderBinding(
                provider, factory, supportsRequiresNew, ambientContextFactory, tenantKeyFactory, capabilities));
            return services;
        }

        private static UnitOfWorkRegistrationState GetOrCreateRegistrationState(IServiceCollection services)
        {
            var descriptor = services.FirstOrDefault(
                service => service.ServiceType == typeof(UnitOfWorkRegistrationState));

            if (descriptor?.ImplementationInstance is UnitOfWorkRegistrationState existing)
            {
                return existing;
            }

            if (descriptor is not null)
            {
                throw new InvalidOperationException(
                    "UnitOfWorkRegistrationState is registered with an unsupported factory; " +
                    "remove the custom registration before calling AddUnitOfWork.");
            }

            var state = new UnitOfWorkRegistrationState();
            services.AddSingleton(state);
            return state;
        }
    }
}
