using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.Modules;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.UnitOfWork;

namespace CrestCreates.OrmProviders.EFCore.Modules
{
    /// <summary>
    /// EF Core ORM 模块
    /// </summary>
    public class EfCoreOrmModule : OrmModuleBase
    {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="configuration">配置对象</param>
        public EfCoreOrmModule(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// 注册 ORM 相关服务
        /// </summary>
        /// <param name="services">服务集合</param>
        public override void RegisterOrmServices(IServiceCollection services)
        {
            // 注册 EF Core 数据库上下文工厂
            services.AddScoped<CrestCreatesDbContextFactory>();
            
            // 注册 EF Core 工作单元
            services.AddScoped<EfCoreUnitOfWork>();
            
            // 注册数据库上下文
            services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
                sp.GetRequiredService<CrestCreatesDbContextFactory>().CreateDbContext(new string[] {}));
            services.AddScoped<IDataBaseContext>(sp =>
                sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        }

        /// <summary>
        /// 获取 ORM 提供者类型
        /// </summary>
        /// <returns>ORM 提供者类型</returns>
        protected override OrmProvider GetOrmProvider()
        {
            return OrmProvider.EfCore;
        }
    }
}
