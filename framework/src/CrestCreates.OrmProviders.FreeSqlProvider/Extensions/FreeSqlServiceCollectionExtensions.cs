using System;
using System.Reflection;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork;

namespace CrestCreates.OrmProviders.FreeSqlProvider.Extensions
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
        /// <param name="repositoryAssemblies">仓储所在程序集</param>
        /// <remarks>
        /// 官方推荐的注册方式：
        /// 1. IFreeSql 从 UnitOfWorkManager.Orm 获取，自动跟随事务切换
        /// 2. 使用 AddFreeRepository 自动注册仓储
        /// 
        /// 示例:
        /// <code>
        /// services.AddFreeSqlWithUow(fsql =>
        /// {
        ///     fsql.UseConnectionString(DataType.SqlServer, connectionString);
        /// }, typeof(Startup).Assembly);
        /// </code>
        /// </remarks>
        public static IServiceCollection AddFreeSqlWithUow(
            this IServiceCollection services,
            Action<IFreeSql> freeSqlBuilder,
            Assembly repositoryAssembly)
        {
            // 1. 创建静态的 FreeSql 实例
            var freeSqlBuilder1 = new FreeSqlBuilder();
            var freeSql = freeSqlBuilder1.Build();
            freeSqlBuilder?.Invoke(freeSql);

            // 2. 注册 FreeSql 仓储（官方推荐）
            if (repositoryAssembly != null)
            {
                services.AddFreeRepository(null, repositoryAssembly);
            }

            // 3. 注册 UnitOfWorkManager
            services.AddScoped<FreeSqlUnitOfWorkManager>(sp => 
                new FreeSqlUnitOfWorkManager(freeSql));

            // 4. 注册 IFreeSql（关键：从 UowManager.Orm 获取）
            // 这样注入的 IFreeSql 会自动跟随 UowManager 切换事务
            services.AddScoped<IFreeSql>(sp => 
                sp.GetRequiredService<FreeSqlUnitOfWorkManager>().Orm);

            return services;
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

            // 注册 FreeSql 仓储
            if (repositoryAssembly != null)
            {
                services.AddFreeRepository(null, repositoryAssembly);
            }

            // 注册 UnitOfWorkManager
            services.AddScoped<FreeSqlUnitOfWorkManager>(sp => 
                new FreeSqlUnitOfWorkManager(freeSql));

            // 注册 IFreeSql（从 UowManager.Orm 获取）
            services.AddScoped<IFreeSql>(sp => 
                sp.GetRequiredService<FreeSqlUnitOfWorkManager>().Orm);

            return services;
        }

        /// <summary>
        /// 添加 FreeSql 多库支持（FreeSql.Cloud）
        /// </summary>
        /// <typeparam name="TDbKey">数据库键类型（如 DbEnum 或 string）</typeparam>
        /// <param name="services">服务集合</param>
        /// <param name="cloudBuilder">FreeSqlCloud 构建器</param>
        /// <param name="repositoryAssemblies">仓储所在程序集</param>
        /// <remarks>
        /// 用于多库场景，参考：https://freesql.net/guide/unitofwork-manager.html#扩展-多库场景
        /// 
        /// 示例:
        /// <code>
        /// public enum DbEnum { db1, db2, db3 }
        /// 
        /// services.AddFreeSqlCloud&lt;DbEnum&gt;(cloud =>
        /// {
        ///     cloud.Register(DbEnum.db1, () => new FreeSqlBuilder().UseConnectionString(DataType.SqlServer, connStr1).Build());
        ///     cloud.Register(DbEnum.db2, () => new FreeSqlBuilder().UseConnectionString(DataType.MySql, connStr2).Build());
        /// });
        /// </code>
        /// </remarks>
        /*
        public static IServiceCollection AddFreeSqlCloud<TDbKey>(
            this IServiceCollection services,
            Action<FreeSqlCloud<TDbKey>> cloudBuilder,
            params Assembly[] repositoryAssemblies)
        {
            // 创建 FreeSqlCloud 实例
            var cloud = new FreeSqlCloud<TDbKey>();
            cloudBuilder?.Invoke(cloud);

            // 注册为单例
            services.AddSingleton(cloud);

            // 注册仓储
            if (repositoryAssemblies != null && repositoryAssemblies.Length > 0)
            {
                services.AddFreeRepository(null, repositoryAssemblies);
            }

            // 注册 UnitOfWorkManagerCloud（需要自定义实现）
            // services.AddScoped<UnitOfWorkManagerCloud>();

            return services;
        }
        */
    }
}
