using CrestCreates.Generated.HttpProjection;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Web.AuditRetention;

public static class AuditRetentionHttpProjectionExtensions
{
    public static IServiceCollection AddCrestAuditRetentionHttp(this IServiceCollection services)
        => GeneratedAppServiceHttpActivation_CrestCreates_002EWeb.AddGeneratedAppServiceHttp(services);
}
