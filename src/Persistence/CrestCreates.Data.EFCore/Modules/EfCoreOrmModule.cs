using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.Abstractions.Modules;
using CrestCreates.Data.EFCore.Configuration;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Interceptors;
using CrestCreates.Data.EFCore.MultiTenancy;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.EFCore.UnitOfWork;

namespace CrestCreates.Data.EFCore.Modules
{
    /// <summary>
    /// EF Core ORM 模块
    /// </summary>
    [CrestModule]
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
            services.TryAddScoped<AuditInterceptor>();
            services.TryAddScoped<MultiTenancyInterceptor>();
            services.TryAddSingleton<TenantAwareModelCacheKeyFactory>();
            
            // 注册 EF Core 工作单元
            services.AddScoped(sp => new EfCoreUnitOfWork(
                sp.GetRequiredService<IDataBaseContext>(),
                sp.GetRequiredService<CrestCreates.Domain.DomainEvents.IDomainEventPublisher>()));
            services.AddScoped(typeof(CrestCreates.Domain.Repositories.IRepository<,>), typeof(DomainRepositoryAdapter<,>));
            services.AddScoped(typeof(ICrestRepositoryBase<,>), typeof(EfCoreRepository<,>));

            services.AddScoped<IPermissionGrantRepository, PermissionGrantRepository>();
            services.AddScoped<IPermissionRepository, PermissionRepository>();
            services.AddScoped<IRoleRepository, RoleRepository>();
            services.AddScoped<ITenantRepository, TenantRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IUserRoleRepository, UserRoleRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IIdentitySecurityLogRepository, IdentitySecurityLogRepository>();
            services.TryAddScoped<IAuditLogRepository, EfCoreAuditLogRepository>();
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
