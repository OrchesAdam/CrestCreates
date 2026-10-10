using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using FreeSql;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.Abstractions.Modules;
using CrestCreates.Data.FreeSql.UnitOfWork;

namespace CrestCreates.Data.FreeSql.Modules
{
    /// <summary>
    /// FreeSql ORM 模块
    /// </summary>
    [CrestModule]
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

            // FreeSql 仓储经 SDK 的 UnitOfWorkManager.Binding 绑定连接，当前未接入
            // ambient 上下文跟随；requiresNew 请求将得到确定性 NotSupportedException
            // 诊断，而不是静默共享事务上下文（扩展支持交 #130 评估）。
            services.AddUnitOfWorkProvider(
                OrmProvider.FreeSql,
                static sp => sp.GetRequiredService<FreeSqlUnitOfWork>(),
                supportsRequiresNew: false);
        }
    }
}