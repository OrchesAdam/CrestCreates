using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CrestCreates.Data.Adapters;
using CrestCreates.Data.Adapters.InMemory;
using CrestCreates.Data.Providers;
using CrestCreates.Data.Repository;
using CrestCreates.Data.UnitOfWork;

namespace CrestCreates.Data.Extensions
{
    /// <summary>
    /// 数据服务注册扩展
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加数据层服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">配置选项</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddDataLayer(this IServiceCollection services, 
            Action<DataLayerOptions>? configureOptions = null)
        {
            var options = new DataLayerOptions();
            configureOptions?.Invoke(options);

            // 注册核心服务
            services.AddSingleton<IOrmAdapterFactory, OrmAdapterFactory>();
            services.AddSingleton<IDatabaseProvider>(provider => 
                new DatabaseProvider(options.DatabaseOptions, provider.GetRequiredService<IOrmAdapterFactory>()));

            // 注册默认适配器
            RegisterDefaultAdapters(services, options);

            // 注册仓储和工作单元
            services.AddScoped(typeof(IRepository<>), provider =>
            {
                var databaseProvider = provider.GetRequiredService<IDatabaseProvider>();
                var dbContext = databaseProvider.CreateDbContext();
                return databaseProvider.Adapter.CreateRepository<object>(dbContext);
            });

            services.AddScoped<IUnitOfWork>(provider =>
            {
                var databaseProvider = provider.GetRequiredService<IDatabaseProvider>();
                return databaseProvider.CreateUnitOfWork();
            });

            return services;
        }

        /// <summary>
        /// 添加数据层服务（使用指定的数据库选项）
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="databaseOptions">数据库选项</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddDataLayer(this IServiceCollection services, DatabaseOptions databaseOptions)
        {
            return AddDataLayer(services, options => options.DatabaseOptions = databaseOptions);
        }

        /// <summary>
        /// 添加ORM适配器
        /// </summary>
        /// <typeparam name="TAdapter">适配器类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddOrmAdapter<TAdapter>(this IServiceCollection services)
            where TAdapter : class, IOrmAdapter
        {
            services.AddSingleton<IOrmAdapter, TAdapter>();
            return services;
        }

        /// <summary>
        /// 添加自定义ORM适配器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="adapter">适配器实例</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddOrmAdapter(this IServiceCollection services, IOrmAdapter adapter)
        {
            services.AddSingleton(adapter);
            return services;
        }

        /// <summary>
        /// 注册默认适配器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="options">配置选项</param>
        private static void RegisterDefaultAdapters(IServiceCollection services, DataLayerOptions options)
        {
            // 始终注册内存适配器（用于测试）
            services.AddSingleton<IOrmAdapter, InMemoryOrmAdapter>();

            // 根据配置注册其他适配器
            if (options.EnableEntityFrameworkCore)
            {
                // 如果有EF Core适配器实现，在这里注册
                // services.AddSingleton<IOrmAdapter, EntityFrameworkCoreAdapter>();
            }

            if (options.EnableDapper)
            {
                // 如果有Dapper适配器实现，在这里注册
                // services.AddSingleton<IOrmAdapter, DapperAdapter>();
            }

            // 其他ORM适配器...
        }

        /// <summary>
        /// 添加生成的仓储服务
        /// 此方法由源生成器生成，目前提供空实现以确保编译通过
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddGeneratedRepositories(this IServiceCollection services)
        {
            // TODO: 当有具体的仓储接口实现时，这里会由源生成器自动填充
            // 目前为空实现，确保DataModule.cs能够编译通过
            return services;
        }
    }

    /// <summary>
    /// 数据层配置选项
    /// </summary>
    public class DataLayerOptions
    {
        /// <summary>
        /// 数据库配置选项
        /// </summary>
        public DatabaseOptions DatabaseOptions { get; set; } = new();

        /// <summary>
        /// 是否启用Entity Framework Core适配器
        /// </summary>
        public bool EnableEntityFrameworkCore { get; set; } = false;

        /// <summary>
        /// 是否启用Dapper适配器
        /// </summary>
        public bool EnableDapper { get; set; } = false;

        /// <summary>
        /// 是否启用FreeSql适配器
        /// </summary>
        public bool EnableFreeSql { get; set; } = false;

        /// <summary>
        /// 是否启用SqlSugar适配器
        /// </summary>
        public bool EnableSqlSugar { get; set; } = false;

        /// <summary>
        /// 是否启用MongoDB适配器
        /// </summary>
        public bool EnableMongoDB { get; set; } = false;

        /// <summary>
        /// 默认ORM类型
        /// </summary>
        public OrmType DefaultOrmType { get; set; } = OrmType.Custom;
    }
}
