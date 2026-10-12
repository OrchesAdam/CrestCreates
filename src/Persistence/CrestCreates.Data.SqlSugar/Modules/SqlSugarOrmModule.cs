using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.Abstractions.Modules;
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

            // SqlSugar 共享单例客户端，无法提供独立的嵌套事务上下文：
            // requiresNew 请求将得到确定性 NotSupportedException 诊断，而不是静默共享事务。
            services.AddUnitOfWorkProvider(
                OrmProvider.SqlSugar,
                static sp => sp.GetRequiredService<SqlSugarUnitOfWork>(),
                supportsRequiresNew: false,
                capabilities: new UnitOfWorkProviderCapabilities
                {
                    PromptTerminationOnAbandon = true,
                    DiscardUncommittedOnAbandon = true
                });
        }
    }
}