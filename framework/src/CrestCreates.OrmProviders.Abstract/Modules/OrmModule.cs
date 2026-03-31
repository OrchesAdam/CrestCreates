using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.UnitOfWorkBase;
using CrestCreates.OrmProviders.Abstract.Abstractions;

namespace CrestCreates.OrmProviders.Abstract.Modules
{
    /// <summary>
    /// 核心 ORM 模块，负责根据配置选择 ORM 实现
    /// </summary>
    public class OrmModule
    {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="configuration">配置对象</param>
        public OrmModule(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// 配置服务
        /// </summary>
        /// <param name="services">服务集合</param>
        public void ConfigureServices(IServiceCollection services)
        {
            // 注册工作单元基础服务
            services.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();
            services.AddScoped<IUnitOfWorkManager>(sp =>
                new UnitOfWorkManager(sp.GetRequiredService<IUnitOfWorkFactory>(), OrmProvider.EfCore));

            // 注意：具体的 ORM 实现需要在应用程序启动时手动注册
            // 例如：
            // services.AddScoped<OrmModuleBase, EfCoreOrmModule>();
            // 或者使用依赖注入容器直接注册具体的 ORM 服务
        }
    }
}