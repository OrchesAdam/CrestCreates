using System;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Application.AuditLog;

public static class AuditRetentionServiceCollectionExtensions
{
    public static IServiceCollection AddAuditRetentionApplication(this IServiceCollection services)
    {
        services.TryAddScoped<IAuditRetentionAppService, AuditRetentionAppService>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPermissionDefinitionProvider, AuditRetentionPermissionDefinitionProvider>());
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
