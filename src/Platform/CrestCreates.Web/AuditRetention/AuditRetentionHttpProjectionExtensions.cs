using System;
using System.Linq;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Application.AuditLog;
using CrestCreates.Generated.HttpProjection;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Web.AuditRetention;

public static class AuditRetentionHttpProjectionExtensions
{
    /// <summary>Activates the generated retention HTTP projection after the kernel and a store are registered.</summary>
    public static IServiceCollection AddCrestAuditRetentionHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IAuditRetentionService) && !descriptor.IsKeyedService)
            || !services.Any(descriptor => descriptor.ServiceType == typeof(IAuditRetentionStore) && !descriptor.IsKeyedService))
        {
            throw new InvalidOperationException(
                "Audit retention HTTP requires an IAuditRetentionService and at least one IAuditRetentionStore. "
                + "Register the retention kernel and a store before calling AddCrestAuditRetentionHttp().");
        }

        services.AddAuditRetentionApplication();
        return GeneratedAppServiceHttpActivation_CrestCreates_002EWeb
            .AddGeneratedAppServiceHttpFor_CrestCreates_002EApplication_002EContracts_002EInterfaces_002EIAuditRetentionAppService(services);
    }
}
