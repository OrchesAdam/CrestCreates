using CrestCreates.Agent.ControlPlane.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.ControlPlane;

public static class AgentControlPlaneServiceCollectionExtensions
{
    /// <summary>
    /// Registers Agent Control Plane services with production-safe default authorization.
    /// Read-only/context tools are allowed by default; mutating and activation handoff
    /// tools require explicit permission grants via <see cref="AgentToolAuthorizationOptions"/>.
    /// For development/testing, use <see cref="AddAgentControlPlane(IServiceCollection, AgentToolAuthorizationOptions)"/>
    /// with <see cref="AgentToolAuthorizationOptions.DevelopmentDefaults"/>.
    /// </summary>
    public static IServiceCollection AddAgentControlPlane(this IServiceCollection services)
    {
        services.TryAddSingleton<IAgentToolManifestProvider, StaticAgentToolManifestProvider>();
        services.TryAddSingleton<IAgentToolAuthorizationService>(_ =>
            new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.ProductionDefaults));
        services.TryAddSingleton<IAgentToolInvocationAuditor, InMemoryAgentToolInvocationAuditor>();
        services.TryAddSingleton<IAgentControlPlaneToolService, DefaultAgentControlPlaneToolService>();
        return services;
    }

    /// <summary>
    /// Registers Agent Control Plane services with the specified authorization options.
    /// </summary>
    public static IServiceCollection AddAgentControlPlane(
        this IServiceCollection services,
        AgentToolAuthorizationOptions options)
    {
        services.TryAddSingleton<IAgentToolManifestProvider, StaticAgentToolManifestProvider>();
        services.TryAddSingleton<IAgentToolAuthorizationService>(_ =>
            new DefaultAgentToolAuthorizationService(options));
        services.TryAddSingleton<IAgentToolInvocationAuditor, InMemoryAgentToolInvocationAuditor>();
        services.TryAddSingleton<IAgentControlPlaneToolService, DefaultAgentControlPlaneToolService>();
        return services;
    }

    /// <summary>
    /// Registers Agent Control Plane services with a legacy authorization policy.
    /// The policy is converted to equivalent <see cref="AgentToolAuthorizationOptions"/> internally.
    /// Prefer using <see cref="AddAgentControlPlane(IServiceCollection, AgentToolAuthorizationOptions)"/> for new code.
    /// </summary>
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
