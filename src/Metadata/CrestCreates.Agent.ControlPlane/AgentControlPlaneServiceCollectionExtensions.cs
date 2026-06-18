using CrestCreates.Agent.ControlPlane.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.ControlPlane;

public static class AgentControlPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddAgentControlPlane(this IServiceCollection services)
    {
        services.TryAddSingleton<IAgentToolManifestProvider, StaticAgentToolManifestProvider>();
        services.TryAddSingleton<IAgentToolAuthorizationService, DefaultAgentToolAuthorizationService>();
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
