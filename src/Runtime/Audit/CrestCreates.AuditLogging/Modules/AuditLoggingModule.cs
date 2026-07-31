using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.AuditLogging.Services;
using CrestCreates.AuditLogging.Options;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Interceptors;
using CrestCreates.AuditLogging.Abstractions.MethodAccountability;
using CrestCreates.AuditLogging.Bootstrap;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.AuditLogging.Modules
{
    [CrestModule]
    public class AuditLoggingModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            services.AddOptions<AuditLoggingOptions>();
            services.AddScoped<IAuditLogRedactor, AuditLogRedactor>();
            services.AddScoped<AuditLoggingMiddleware>();
            services.AddScoped<IAuditLogService, AuditLogService>();
            services.AddScoped<IAuditLogWriter, AuditLogWriter>();
            services.AddScoped<AccountabilityHttpTerminalObserverMiddleware>();
            services.AddScoped<AccountabilityHttpOperationScopeMiddleware>();
            services.AddScoped<IAuditedMethodAccountabilityRuntime, AuditedMethodAccountabilityRuntime>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapValidator, AuditLoggingAccountabilityCompositionValidator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AuditLoggingAccountabilityCompositionValidator>());
        }
    }
}
