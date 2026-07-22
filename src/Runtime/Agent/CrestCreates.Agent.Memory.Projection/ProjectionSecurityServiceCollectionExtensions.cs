using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CrestCreates.Agent.Memory.Projection;

public static class ProjectionSecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registers projection-neutral security infrastructure.
    /// Does NOT register IAgentMemoryAccessScopeProvider — missing provider fails startup.
    /// Does NOT register TimeProvider — Host must register it explicitly.
    /// </summary>
    public static IServiceCollection AddAgentMemoryProjectionSecurity(
        this IServiceCollection services,
        Action<AgentMemoryProjectionSecurityOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Resolve options from the options pattern
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<AgentMemoryProjectionSecurityOptions>>().Value);

        // Security infrastructure
        services.TryAddSingleton<IAgentMemoryAccessHandleStore, AgentMemoryAccessHandleStore>();
        services.TryAddSingleton<IAgentMemoryAccessGrantStore, AgentMemoryAccessGrantStore>();
        services.TryAddSingleton<IAgentMemoryAccessArtifactBatchStore, AgentMemoryAccessArtifactBatchStore>();
        services.TryAddSingleton<IAgentMemoryAccessArtifactCoordinator, AgentMemoryAccessArtifactCoordinator>();
        services.TryAddSingleton<IAgentMemoryAccessHandleResolver, AgentMemoryAccessHandleResolver>();
        services.TryAddSingleton<IAgentMemoryAccessGrantResolver, AgentMemoryAccessGrantResolver>();
        services.TryAddSingleton<IAgentMemoryArtifactLifetimePolicy, DefaultAgentMemoryArtifactLifetimePolicy>();
        services.TryAddSingleton<IAgentMemoryContextHandleIssuer, DefaultAgentMemoryContextHandleIssuer>();
        services.TryAddSingleton<IAgentMemoryCurrentClosureProvider, CompositeCurrentClosureProvider>();

        // Resource closure providers for live descriptor revalidation
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentMemoryResourceClosureProvider, MemoryResourceClosureProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentMemoryResourceClosureProvider, CandidateResourceClosureProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentMemoryResourceClosureProvider, ContextResourceClosureProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentMemoryResourceClosureProvider, ConversationHistoryResourceClosureProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentMemoryResourceClosureProvider, TaskHistoryResourceClosureProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentMemoryResourceClosureProvider, TaskEventResourceClosureProvider>());

        return services;
    }
}
