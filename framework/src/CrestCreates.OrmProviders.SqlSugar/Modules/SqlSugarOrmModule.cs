using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.Modules;
using CrestCreates.OrmProviders.SqlSugar.UnitOfWork;

namespace CrestCreates.OrmProviders.SqlSugar.Modules
{
    /// <summary>
    /// SqlSugar ORM 模块
    /// </summary>
    [CrestModule]
    public class SqlSugarOrmModule : OrmModuleBase
    {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="configuration">配置对象</param>
        public SqlSugarOrmModule(IConfiguration configuration)
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

            // 创建 SqlSugar 实例
            var sqlSugarClient = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = connectionString,
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = true
            });

            // 注册 SqlSugar 实例
            services.AddSingleton(sqlSugarClient);
            
            // 注册 SqlSugar 工作单元
            services.AddScoped<SqlSugarUnitOfWork>();
        }

        /// <summary>
        /// 获取 ORM 提供者类型
        /// </summary>
        /// <returns>ORM 提供者类型</returns>
        protected override OrmProvider GetOrmProvider()
        {
            return OrmProvider.SqlSugar;
        }
    }
}