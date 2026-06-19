using CrestCreates.Agent.ControlPlane.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.ControlPlane;

public static class AgentControlPlaneServiceCollectionExtensions
{
    /// <summary>
    /// Registers Agent Control Plane services with the production-safe default policy.
    /// Mutating tools (draft create/update/cancel, fix apply, activation submit/cancel)
    /// are denied unless explicitly allowed by providing a custom policy.
    /// For development/testing, use <see cref="AgentToolAuthorizationPolicy.AllowAll"/>.
    /// </summary>
    public static IServiceCollection AddAgentControlPlane(this IServiceCollection services)
    {
        services.TryAddSingleton<IAgentToolManifestProvider, StaticAgentToolManifestProvider>();
        services.TryAddSingleton<IAgentToolAuthorizationService>(_ =>
            new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.ProductionDefaults));
        services.TryAddSingleton<IAgentToolInvocationAuditor, InMemoryAgentToolInvocationAuditor>();
        services.TryAddSingleton<IAgentControlPlaneToolService, DefaultAgentControlPlaneToolService>();
        return services;
    }

    public static IServiceCollection AddAgentControlPlane(
        this IServiceCollection services,
        AgentToolAuthorizationPolicy policy)
    {
        services.TryAddSingleton<IAgentToolManifestProvider, StaticAgentToolManifestProvider>();
        services.TryAddSingleton<IAgentToolAuthorizationService>(_ =>
            new DefaultAgentToolAuthorizationService(policy));
        services.TryAddSingleton<IAgentToolInvocationAuditor, InMemoryAgentToolInvocationAuditor>();
        services.TryAddSingleton<IAgentControlPlaneToolService, DefaultAgentControlPlaneToolService>();
        return services;
    }
}
