using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Application.AuditLog;

/// <summary>
/// 审计日志服务注册扩展
/// </summary>
public static class AuditLoggingServiceCollectionExtensions
{
    /// <summary>
    /// 添加审计日志应用服务
    /// </summary>
    public static IServiceCollection AddAuditLogging(this IServiceCollection services)
    {
        services.TryAddScoped<IAuditLogAppService, AuditLogAppService>();
        services.TryAddScoped<IAuditLogCleanupAppService, AuditLogCleanupAppService>();
        return services;
    }
}
