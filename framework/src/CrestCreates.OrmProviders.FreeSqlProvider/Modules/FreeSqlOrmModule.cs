using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using FreeSql;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.Modules;
using CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork;

namespace CrestCreates.OrmProviders.FreeSqlProvider.Modules
{
    /// <summary>
    /// FreeSql ORM 模块
    /// </summary>
    public class FreeSqlOrmModule : OrmModuleBase
    {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="configuration">配置对象</param>
        public FreeSqlOrmModule(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// 注册 ORM 相关服务
        /// </summary>
        /// <param name="services">服务集合</param>
        public override void RegisterOrmServices(IServiceCollection services)
        {
            // 从配置中获取连接字符串
            var connectionString = _configuration.GetConnectionString("Default") ?? 
                throw new InvalidOperationException("Connection string not configured");

            // 创建 FreeSql 实例
            var freeSql = new FreeSqlBuilder()
                .UseConnectionString(DataType.SqlServer, connectionString)
                .Build();

            // 注册 FreeSql 实例
            services.AddSingleton(freeSql);
            
            // 注册 FreeSql 工作单元管理器
            services.AddScoped<FreeSqlUnitOfWorkManager>();
            
            // 注册 FreeSql 工作单元
            services.AddScoped<FreeSqlUnitOfWork>();
        }

        /// <summary>
        /// 获取 ORM 提供者类型
        /// </summary>
        /// <returns>ORM 提供者类型</returns>
        protected override OrmProvider GetOrmProvider()
        {
            return OrmProvider.FreeSql;
        }
    }
}