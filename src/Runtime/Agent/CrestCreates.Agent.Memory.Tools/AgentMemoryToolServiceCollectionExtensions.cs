using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.DescriptorProviders;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Memory.Tools.Adapters;
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
        AgentMemoryProjectionSchemaProviders.EnsureRegistered();
        AgentMemoryToolDescriptorProviders.EnsureRegistered();
        services.TryAddSingleton<IServiceCollection>(services);
        var promotionRegistration = services.LastOrDefault(item => item.ServiceType == typeof(IAgentMemoryPromotionService));
        if (promotionRegistration is not null && promotionRegistration.Lifetime != ServiceLifetime.Singleton)
            throw new InvalidOperationException("Memory curation requires a singleton Promotion Service binding.");

        // Tool-level JSON context, audit, outcome providers
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolJsonContextContributor, AgentMemoryToolJsonContextContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolPreparedOutcomeRequirementProvider, AgentMemoryPreparedOutcomeRequirementProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolOutputAuditProjectionProvider, AgentMemoryToolAuditProjectionProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolOutputAuditProjectionContractProvider, AgentMemoryToolAuditProjectionProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolOutputOutcomeCodeProvider, AgentMemoryToolAuditProjectionProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolModuleSelection, AgentMemoryToolModuleMarker>());

        // Canonical security infrastructure from Projection
        services.AddAgentMemoryProjectionSecurity();
        services.AddAgentMemoryReadCore();

        // TimeProvider — host can override, default is System
        services.TryAddSingleton(TimeProvider.System);

        // Capability module + handler DI (new composable pattern)
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICapabilityHandlerModule>(
                GeneratedCapabilityHandlerModule.Instance));
        GeneratedHandlerRegistry.RegisterServices(services);

        // Old-interface adapters wrapping canonical stores
        services.TryAddSingleton<IAgentMemorySecurityArtifactBatchStore, AgentMemorySecurityArtifactBatchStore>();
        services.TryAddSingleton<IAgentMemoryResourceHandleStore, AgentMemoryResourceHandleStoreAdapter>();
        services.TryAddSingleton<IAgentMemorySourceGrantStore, AgentMemorySourceGrantStoreAdapter>();
        services.TryAddSingleton<IAgentMemorySecurityArtifactCoordinator, AgentMemorySecurityArtifactCoordinatorAdapter>();
        services.TryAddSingleton<IAgentMemoryResourceHandleResolver, AgentMemoryResourceHandleResolverAdapter>();
        services.TryAddSingleton<IAgentMemorySourceGrantResolver, AgentMemorySourceGrantResolverAdapter>();
        services.TryAddSingleton<IAgentMemoryHistoryResourceHandleIssuer, AgentMemoryHistoryResourceHandleIssuerAdapter>();

        // Legacy scope provider adapter
        services.TryAddSingleton<IAgentMemoryAccessScopeProvider, LegacyAgentMemoryAccessScopeProviderAdapter>();

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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AgentMemoryToolCapabilityGateHostedService>());
        return services;
    }
}

internal sealed class AgentMemoryPreparedOutcomeRequirementProvider : IAgentToolPreparedOutcomeRequirementProvider
{
    public AgentToolPreparedOutcomeContract? Create(string toolName)
        => toolName switch
        {
            AgentMemoryToolCapabilityIds.BuildPack
                or AgentMemoryToolCapabilityIds.CompressHistory
                or AgentMemoryToolCapabilityIds.ExtractCandidates
                => Contract("completed", "unavailable"),
            AgentMemoryToolCapabilityIds.ExpandSource
                => Contract("completed", "unavailable", "redacted", "not-expandable"),
            AgentMemoryToolCapabilityIds.PromoteCandidate
                or AgentMemoryToolCapabilityIds.RejectCandidate
                or AgentMemoryToolCapabilityIds.SupersedeItem
                => Contract("completed", "conflict", "unavailable"),
            _ => null
        };

    private static AgentToolPreparedOutcomeContract Contract(params string[] codes)
        => new()
        {
            AllowedOutcomeCodes = codes.ToHashSet(StringComparer.Ordinal),
            MaximumBranches = codes.Length
        };
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
        if (_services.GetService<IAuditOperationContextAccessor>() is null)
        {
            throw new InvalidOperationException(
                "Memory curation tools require the Accountability audit context accessor. " +
                "Call AddAccountability() before AddAgentMemoryTools().");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
