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
        services.TryAddSingleton<IServiceCollection>(services);
        var promotionRegistration = services.LastOrDefault(item => item.ServiceType == typeof(IAgentMemoryPromotionService));
        if (promotionRegistration is not null && promotionRegistration.Lifetime != ServiceLifetime.Singleton)
            throw new InvalidOperationException("Memory curation requires a singleton Promotion Service binding.");
        // Explicitly select this module's generated Capability provider. The
        // generated Apply method creates a resolver owned by this Host and
        // registers the seven handlers as scoped services; no process-global
        // resolver state is consulted by the Memory Tool execution path.
        GeneratedHandlerRegistry.Apply(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolJsonContextContributor, AgentMemoryToolJsonContextContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolPreparedOutcomeRequirementProvider, AgentMemoryPreparedOutcomeRequirementProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolOutputAuditProjectionProvider, AgentMemoryToolAuditProjectionProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolOutputOutcomeCodeProvider, AgentMemoryToolAuditProjectionProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolModuleSelection, AgentMemoryToolModuleMarker>());
        services.TryAddSingleton<IAgentMemorySecurityArtifactBatchStore, AgentMemorySecurityArtifactBatchStore>();
        services.TryAddSingleton<IAgentMemoryResourceHandleStore, AgentMemoryResourceHandleStore>();
        services.TryAddSingleton<IAgentMemorySourceGrantStore, AgentMemorySourceGrantStore>();
        services.TryAddSingleton<IAgentMemorySecurityArtifactCoordinator, AgentMemorySecurityArtifactCoordinator>();
        services.TryAddSingleton<AgentMemoryResourceHandleResolver>();
        services.TryAddSingleton<IAgentMemoryResourceHandleResolver>(sp => sp.GetRequiredService<AgentMemoryResourceHandleResolver>());
        services.TryAddSingleton<IAgentMemorySourceGrantResolver>(sp => sp.GetRequiredService<AgentMemoryResourceHandleResolver>());
        services.TryAddSingleton<IAgentMemoryHistoryResourceHandleIssuer, AgentMemoryHistoryResourceHandleIssuer>();
        services.TryAddSingleton<AgentMemoryToolRuntimeBinding>(sp =>
        {
            var finalRegistrations = sp.GetRequiredService<IServiceCollection>()
                .Where(item => item.ServiceType == typeof(IAgentMemoryPromotionService))
                .ToArray();
            if (finalRegistrations.Length != 1 || finalRegistrations.Any(item => item.Lifetime != ServiceLifetime.Singleton))
                throw new InvalidOperationException("The finalized Promotion Service registration must be singleton.");
            var service = sp.GetRequiredService<IAgentMemoryPromotionService>();
            if (service is not IAgentMemoryCurationServiceCapabilities capabilities)
                throw new InvalidOperationException("Selected Promotion Service must expose curation capabilities.");
            return new AgentMemoryToolRuntimeBinding
            {
                PromotionService = service,
                OutcomeGuarantee = capabilities.OutcomeGuarantee
            };
        });
        // GeneratedHandlerRegistry.Apply owns the scoped handler registrations
        // and the Host-local resolver; keep one registration authority.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AgentMemoryToolCapabilityGateHostedService>());
        return services;
    }
}

internal sealed class AgentMemoryPreparedOutcomeRequirementProvider : IAgentToolPreparedOutcomeRequirementProvider
{
    public AgentToolPreparedOutcomeContract? Create(string toolName)
        => toolName is AgentMemoryToolCapabilityIds.BuildPack
            or AgentMemoryToolCapabilityIds.ExpandSource
            or AgentMemoryToolCapabilityIds.CompressHistory
            or AgentMemoryToolCapabilityIds.ExtractCandidates
            or AgentMemoryToolCapabilityIds.PromoteCandidate
            or AgentMemoryToolCapabilityIds.RejectCandidate
            or AgentMemoryToolCapabilityIds.SupersedeItem
            ? new AgentToolPreparedOutcomeContract
            {
                AllowedOutcomeCodes = new HashSet<string>(["completed", "unavailable", "conflict", "redacted", "not-expandable"], StringComparer.Ordinal),
                MaximumBranches = 5
            }
            : null;
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
        var binding = _services.GetRequiredService<AgentMemoryToolRuntimeBinding>();
        if (binding.OutcomeGuarantee != AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic)
        {
            throw new InvalidOperationException(
                "Memory curation tools require the selected promotion service instance to prove ConfirmedAtomic outcome semantics.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
