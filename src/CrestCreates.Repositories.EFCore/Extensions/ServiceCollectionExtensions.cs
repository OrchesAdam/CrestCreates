using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Data.Adapters;

namespace CrestCreates.Repositories.EFCore.Extensions
{
    /// <summary>
    /// Entity Framework Core 服务注册扩展方法
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加 Entity Framework Core 适配器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="connectionString">数据库连接字符串</param>
        /// <param name="databaseType">数据库类型</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddEFCoreAdapter(this IServiceCollection services, 
            string connectionString, 
            DatabaseType databaseType = DatabaseType.SqlServer)
        {
            // 注册 EF Core 适配器
            services.AddSingleton<IOrmAdapter>(provider =>
            {
                var adapter = new EFCoreOrmAdapter();
                adapter.Initialize(new DatabaseOptions
                {
                    ConnectionString = connectionString,
                    DatabaseType = databaseType
                });
                return adapter;
            });

            return services;
        }

        /// <summary>
        /// 添加 Entity Framework Core 适配器，使用自定义配置
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">配置选项</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddEFCoreAdapter(this IServiceCollection services,
            Action<DatabaseOptions> configureOptions)
        {
            var options = new DatabaseOptions();
            configureOptions(options);

            return services.AddEFCoreAdapter(options.ConnectionString, options.DatabaseType);
        }

        /// <summary>
        /// 添加 Entity Framework Core 数据库上下文
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="connectionString">数据库连接字符串</param>
        /// <param name="databaseType">数据库类型</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddEFCoreDbContext(this IServiceCollection services,
            string connectionString,
            DatabaseType databaseType = DatabaseType.SqlServer)
        {
            services.AddDbContext<EFCoreDbContext>(options =>
            {
                ConfigureDbContextOptions(options, connectionString, databaseType);
            });

            return services;
        }

        /// <summary>
        /// 添加 Entity Framework Core 数据库上下文，使用自定义配置
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureDbContext">配置数据库上下文</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddEFCoreDbContext(this IServiceCollection services,
            Action<DbContextOptionsBuilder> configureDbContext)
        {
            services.AddDbContext<EFCoreDbContext>(configureDbContext);
            return services;
        }

        /// <summary>
        /// 同时添加 EF Core 适配器和数据库上下文
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="connectionString">数据库连接字符串</param>
        /// <param name="databaseType">数据库类型</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddEFCore(this IServiceCollection services,
            string connectionString,
            DatabaseType databaseType = DatabaseType.SqlServer)
        {
            services.AddEFCoreAdapter(connectionString, databaseType);
            services.AddEFCoreDbContext(connectionString, databaseType);
            
            return services;
        }

        /// <summary>
        /// 同时添加 EF Core 适配器和数据库上下文，使用自定义配置
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">配置选项</param>
        /// <param name="configureDbContext">配置数据库上下文</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddEFCore(this IServiceCollection services,
            Action<DatabaseOptions> configureOptions,
            Action<DbContextOptionsBuilder>? configureDbContext = null)
        {
            var options = new DatabaseOptions();
            configureOptions(options);

            services.AddEFCoreAdapter(configureOptions);

            if (configureDbContext != null)
            {
                services.AddEFCoreDbContext(configureDbContext);
            }
            else
            {
                services.AddEFCoreDbContext(options.ConnectionString, options.DatabaseType);
            }

            return services;
        }

        private static void ConfigureDbContextOptions(DbContextOptionsBuilder options, 
            string connectionString, 
            DatabaseType databaseType)
        {
            switch (databaseType)
            {
                case DatabaseType.SqlServer:
                    options.UseSqlServer(connectionString);
                    break;
                case DatabaseType.SQLite:
                    options.UseSqlite(connectionString);
                    break;
                case DatabaseType.InMemory:
                    options.UseInMemoryDatabase(connectionString);
                    break;
                default:
                    throw new NotSupportedException($"Database type '{databaseType}' is not supported");
            }
        }
    }
}
