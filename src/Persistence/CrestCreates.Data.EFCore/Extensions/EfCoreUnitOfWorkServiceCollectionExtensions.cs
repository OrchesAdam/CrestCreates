using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.UnitOfWork;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.EFCore.Extensions
{
    /// <summary>
    /// EF Core 工作单元注册扩展：声明具体工作单元与 Provider 强类型绑定的唯一位置。
    /// </summary>
    public static class EfCoreUnitOfWorkServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 EF Core 工作单元（scoped）及其工作单元绑定。
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        /// <remarks>
        /// 配合 <see cref="UnitOfWorkServiceCollectionExtensions.AddUnitOfWork"/> 使用。
        /// EF Core Provider 支持 requiresNew 隔离：子作用域解析独立 DbContext/连接，
        /// 并把该作用域的 <see cref="IDataBaseContext"/> 作为环境上下文推入（记录租户键，
        /// 读方先校验后路由），使已注入的仓储/适配器在隔离期间跟随当前 UoW。
        /// </remarks>
        public static IServiceCollection AddEfCoreUnitOfWork(this IServiceCollection services)
        {
            services.AddScoped(sp => new EfCoreUnitOfWork(
                sp.GetRequiredService<IDataBaseContext>(),
                sp.GetRequiredService<IDomainEventPublisher>()));
            services.AddUnitOfWorkProvider(
                OrmProvider.EfCore,
                static sp => sp.GetRequiredService<EfCoreUnitOfWork>(),
                supportsRequiresNew: true,
                ambientContextFactory: static sp => sp.GetRequiredService<IDataBaseContext>(),
                tenantKeyFactory: static sp => sp.GetService<ICurrentTenant>()?.Id);
            return services;
        }
    }
}
