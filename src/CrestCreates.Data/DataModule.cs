using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using CrestCreates.Data.Extensions;
using CrestCreates.Data.Adapters;
using CrestCreates.Data.Providers;
using CrestCreates.Modularity;

namespace CrestCreates.Data
{
    // DataModule 的自定义实现部分
    // 此类与生成器生成的 DataModule 合并为同一个类
    public partial class DataModule
    {
        private DataModuleOptions? _cachedOptions;

        /// <summary>
        /// 获取数据模块选项
        /// </summary>
        private DataModuleOptions? GetOptions(IServiceProvider serviceProvider)
        {
            return _cachedOptions ??= serviceProvider.GetService<IOptions<DataModuleOptions>>()?.Value;
        }

        /// <summary>
        /// 配置数据层服务
        /// </summary>
        /// <param name="services">服务集合</param>
        public override void ConfigureServices(IServiceCollection services)
        {
            // 注意：由于构造函数中没有 IServiceProvider，我们需要从 DI 容器中获取配置
            // 这里我们先注册服务，配置会在模块初始化时通过其他方式传递
            
            // 自动注册发现的仓储
            services.AddGeneratedRepositories();

            base.ConfigureServices(services);
        }

        /// <summary>
        /// 解析数据库类型
        /// </summary>
        /// <param name="databaseType">数据库类型字符串</param>
        /// <returns>数据库类型枚举</returns>
        private static DatabaseType ParseDatabaseType(string databaseType)
        {
            return databaseType.ToLowerInvariant() switch
            {
                "sqlserver" => DatabaseType.SqlServer,
                "mysql" => DatabaseType.MySQL,
                "postgresql" => DatabaseType.PostgreSQL,
                "sqlite" => DatabaseType.SQLite,
                "oracle" => DatabaseType.Oracle,
                "mongodb" => DatabaseType.MongoDB,
                "inmemory" => DatabaseType.InMemory,
                _ => DatabaseType.SqlServer
            };
        }
    }
}
