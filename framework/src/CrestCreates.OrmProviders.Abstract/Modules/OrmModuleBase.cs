using Microsoft.Extensions.DependencyInjection;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.UnitOfWorkBase;
using CrestCreates.OrmProviders.Abstract.Abstractions;

namespace CrestCreates.OrmProviders.Abstract.Modules
{
    /// <summary>
    /// ORM 模块基类
    /// </summary>
    public abstract class OrmModuleBase
    {
        /// <summary>
        /// 注册 ORM 相关服务
        /// </summary>
        /// <param name="services">服务集合</param>
        public abstract void RegisterOrmServices(IServiceCollection services);

        /// <summary>
        /// 配置服务
        /// </summary>
        /// <param name="services">服务集合</param>
        public virtual void ConfigureServices(IServiceCollection services)
        {
            // 注册工作单元基础服务
            services.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();
            services.AddScoped<IUnitOfWorkManager>(sp =>
                new UnitOfWorkManager(sp.GetRequiredService<IUnitOfWorkFactory>(), GetOrmProvider()));

            // 注册具体 ORM 服务
            RegisterOrmServices(services);
        }

        /// <summary>
        /// 获取 ORM 提供者类型
        /// </summary>
        /// <returns>ORM 提供者类型</returns>
        protected abstract OrmProvider GetOrmProvider();
    }
}