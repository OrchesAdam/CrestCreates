using CrestCreates.Generated.HttpProjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Web.PlatformManagement;

/// <summary>
/// Opt-in activation of the platform management HTTP surface (G-09). The generated activation is
/// per-assembly: it lights up every projection anchor in CrestCreates.Web (including AuditRetention),
/// idempotently. Hosts call this explicitly alongside AddCapabilityRuntime/AddCrestCapabilityEndpoints
/// and MapCrestCapabilityEndpoints; it is intentionally not folded into AddCrestWeb defaults so the
/// ExplicitHost surface stays opt-in and existing activation baselines are unchanged.
/// </summary>
public static class PlatformManagementHttpProjectionExtensions
{
    public static IServiceCollection AddCrestPlatformManagementHttp(this IServiceCollection services)
    {
        services.TryAddScoped<AuditLogHttpProjection>();
        return GeneratedAppServiceHttpActivation_CrestCreates_002EWeb.AddGeneratedAppServiceHttp(services);
    }
}