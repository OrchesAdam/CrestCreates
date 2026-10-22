using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.Abstractions.Modules;
using CrestCreates.Data.SqlSugar.Extensions;
using CrestCreates.Data.SqlSugar.UnitOfWork;

namespace CrestCreates.Data.SqlSugar.Modules
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
            var connectionString = _configuration.GetConnectionString("Default") ??
                throw new InvalidOperationException("Connection string not configured");

            services.AddSqlSugarWithUow(new ConnectionConfig
            {
                ConnectionString = connectionString,
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = true
            });
        }
    }
}