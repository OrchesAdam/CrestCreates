using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.Modules;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Repositories;
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
            services.AddScoped(sp => new EfCoreUnitOfWork(
                sp.GetRequiredService<IDataBaseContext>(),
                sp.GetRequiredService<CrestCreates.Domain.DomainEvents.IDomainEventPublisher>()));
            
            // 注册数据库上下文
            services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
                sp.GetRequiredService<CrestCreatesDbContextFactory>().CreateDbContext(new string[] {}));
            services.AddScoped<IDataBaseContext>(sp =>
                sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
            services.AddScoped(typeof(IRepository<,>), typeof(DomainRepositoryAdapter<,>));
            services.AddScoped(typeof(ICrestRepositoryBase<,>), typeof(EfCoreRepository<,>));

            services.AddScoped<IPermissionGrantRepository, PermissionGrantRepository>();
            services.AddScoped<IPermissionRepository, PermissionRepository>();
            services.AddScoped<IRoleRepository, RoleRepository>();
            services.AddScoped<ITenantRepository, TenantRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IUserRoleRepository, UserRoleRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IIdentitySecurityLogRepository, IdentitySecurityLogRepository>();
            services.AddScoped<IAuditLogRepository, AuditLogRepository>();
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
