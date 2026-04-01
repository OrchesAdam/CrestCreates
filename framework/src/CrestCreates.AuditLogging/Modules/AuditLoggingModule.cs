using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.AuditLogging.Services;
using CrestCreates.AuditLogging.Entities;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.AuditLogging.Modules
{
    public class AuditLoggingModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);

            // 注册审计日志服务
            services.AddScoped<IAuditLogService, AuditLogService>();

            // 注册审计日志仓库
            // 注意：这里需要根据实际的 ORM 实现来注册具体的仓库
            // 例如：services.AddScoped<IRepository<AuditLog, Guid>, EfCoreRepository<AuditLog, Guid>>();
        }
    }
}