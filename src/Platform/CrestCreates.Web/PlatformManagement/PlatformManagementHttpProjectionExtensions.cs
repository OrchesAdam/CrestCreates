using CrestCreates.Generated.HttpProjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Web.PlatformManagement;

/// <summary>
/// Activates the generated platform management HTTP surface (G-09). Hosts call this alongside
/// AddCapabilityRuntime, AddCrestCapabilityEndpoints, and MapCrestCapabilityEndpoints. Audit retention
/// has its own activation through AddCrestAuditRetentionHttp.
/// </summary>
public static class PlatformManagementHttpProjectionExtensions
{
    public static IServiceCollection AddCrestPlatformManagementHttp(this IServiceCollection services)
    {
        services.TryAddScoped<AuditLogHttpProjection>();
        return GeneratedAppServiceHttpActivation_CrestCreates_002EWeb.AddGeneratedAppServiceHttp(services);
    }
}