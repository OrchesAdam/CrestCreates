using System;
using System.Reflection;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.FreeSql.UnitOfWork;

namespace CrestCreates.Data.FreeSql.Extensions
{
    /// <summary>
    /// FreeSql 依赖注入扩展
    /// </summary>
    public static class FreeSqlServiceCollectionExtensions
    {
        /// <summary>
        /// 添加 FreeSql 支持（推荐方式）
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="freeSqlBuilder">FreeSql 构建器</param>
        /// <param name="repositoryAssembly">保留源兼容；不再扫描该程序集</param>
        /// <remarks>
        /// IFreeSql 从 UnitOfWorkManager.Orm 获取并自动跟随事务切换。仓储通过闭包泛型显式
        /// DI 注册；不再使用 AddFreeRepository 运行时扫描。
        /// </remarks>
        public static IServiceCollection AddFreeSqlWithUow(
            this IServiceCollection services,
            Action<FreeSqlBuilder> freeSqlBuilder,
            Assembly repositoryAssembly)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(freeSqlBuilder);

            var builder = new FreeSqlBuilder();
            freeSqlBuilder(builder);
            return services.AddFreeSqlWithUow(builder.Build(), repositoryAssembly);
        }

        /// <summary>
        /// 添加 FreeSql 支持（使用已构建的实例）
        /// </summary>
        public static IServiceCollection AddFreeSqlWithUow(
            this IServiceCollection services,
            IFreeSql freeSql,
            Assembly repositoryAssembly)
        {
            if (freeSql == null)
                throw new ArgumentNullException(nameof(freeSql));

            // 注册 UnitOfWorkManager
            services.AddScoped<FreeSqlUnitOfWorkManager>(sp => 
                new FreeSqlUnitOfWorkManager(freeSql));

            // 注册 IFreeSql（从 UowManager.Orm 获取）
            services.AddScoped<IFreeSql>(sp => 
                sp.GetRequiredService<FreeSqlUnitOfWorkManager>().Orm);

            // 注册 FreeSql 工作单元与其 Provider 绑定
            services.AddFreeSqlUnitOfWorkCore();

            return services;
        }
    }
}
