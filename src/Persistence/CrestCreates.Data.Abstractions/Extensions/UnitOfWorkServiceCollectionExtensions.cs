using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 工作单元依赖注入扩展
    /// </summary>
    public static class UnitOfWorkServiceCollectionExtensions
    {
        /// <summary>
        /// 注册工作单元服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="defaultProvider">默认 ORM 提供者</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddUnitOfWork(
            this IServiceCollection services,
            OrmProvider defaultProvider = OrmProvider.EfCore)
        {
            services.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();
            services.AddScoped<IUnitOfWorkManager>(sp =>
                new UnitOfWorkManager(sp.GetRequiredService<IUnitOfWorkFactory>(), defaultProvider));

            return services;
        }

        /// <summary>
        /// 注册工作单元服务（使用自定义工厂）
        /// </summary>
        /// <typeparam name="TFactory">工作单元工厂类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <param name="defaultProvider">默认 ORM 提供者</param>
        /// <returns>服务集合</returns>
        [UnconditionalSuppressMessage("Trimming", "IL2091:RequiresUnreferencedCode",
            Justification = "Tier 3 (Data.Abstractions): AOT capability separately declared. Generic factory registration requires public constructors.")]
        public static IServiceCollection AddUnitOfWork<TFactory>(
            this IServiceCollection services,
            OrmProvider defaultProvider = OrmProvider.EfCore)
            where TFactory : class, IUnitOfWorkFactory
        {
            services.AddScoped<IUnitOfWorkFactory, TFactory>();
            services.AddScoped<IUnitOfWorkManager>(sp =>
                new UnitOfWorkManager(sp.GetRequiredService<IUnitOfWorkFactory>(), defaultProvider));

            return services;
        }
    }
}
