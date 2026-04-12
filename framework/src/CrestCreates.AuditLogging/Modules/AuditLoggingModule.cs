using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Services;
using CrestCreates.AuditLogging.Options;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.AuditLogging.Modules
{
    [CrestModule]
    public class AuditLoggingModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);

            services.AddOptions<AuditLoggingOptions>();
            services.AddScoped<IAuditLogRedactor, AuditLogRedactor>();
            services.AddScoped<AuditLoggingMiddleware>();
            services.AddScoped<IAuditLogService, AuditLogService>();
            services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        }
    }
}
