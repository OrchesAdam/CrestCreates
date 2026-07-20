using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Generated;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Agent.Memory.Tools;

public static class AgentMemoryToolServiceCollectionExtensions
{
    public static IServiceCollection AddAgentMemoryTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Explicitly select this module's generated Capability provider. The
        // generated Apply method creates a resolver owned by this Host and
        // registers the seven handlers as scoped services; no process-global
        // resolver state is consulted by the Memory Tool execution path.
        GeneratedHandlerRegistry.Apply(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolJsonContextContributor, AgentMemoryToolJsonContextContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolModuleSelection, AgentMemoryToolModuleMarker>());
        services.TryAddSingleton<IAgentMemorySecurityArtifactBatchStore, AgentMemorySecurityArtifactBatchStore>();
        services.TryAddSingleton<IAgentMemoryResourceHandleStore, AgentMemoryResourceHandleStore>();
        services.TryAddSingleton<IAgentMemorySourceGrantStore, AgentMemorySourceGrantStore>();
        services.TryAddSingleton<IAgentMemoryHistoryResourceHandleIssuer, AgentMemoryHistoryResourceHandleIssuer>();
        // GeneratedHandlerRegistry.Apply owns the scoped handler registrations
        // and the Host-local resolver; keep one registration authority.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AgentMemoryToolCapabilityGateHostedService>());
        return services;
    }
}

internal sealed class AgentMemoryToolModuleMarker : IAgentToolModuleSelection
{
    public string ModuleId => "agent-memory-tools";
}

internal sealed class AgentMemoryToolCapabilityGateHostedService : IHostedService
{
    private readonly IServiceProvider _services;

    public AgentMemoryToolCapabilityGateHostedService(IServiceProvider services) => _services = services;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var service = _services.GetRequiredService<IAgentMemoryPromotionService>();
        // The guarantee must be exposed by the actual selected promotion
        // service. A separately registered capability object cannot prove the
        // behavior of a decorator or replacement service.
        if (service is not IAgentMemoryCurationServiceCapabilities capabilities
            || capabilities.OutcomeGuarantee != AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic)
        {
            throw new InvalidOperationException(
                "Memory curation tools require the selected promotion service instance to prove ConfirmedAtomic outcome semantics.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
