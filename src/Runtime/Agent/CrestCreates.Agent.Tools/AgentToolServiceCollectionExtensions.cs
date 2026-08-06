using CrestCreates.Metadata.AgentTool;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Agent.Tools;

public static class AgentToolServiceCollectionExtensions
{
    public static IServiceCollection AddCrestAgentTools(
        this IServiceCollection services,
        Action<AgentToolJsonOptions>? configureJson = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<AgentToolJsonOptions>(sp =>
        {
            var json = new AgentToolJsonOptions();
            configureJson?.Invoke(json);
            foreach (var selection in sp.GetServices<IAgentToolModuleSelection>())
                json.EnabledModuleIds.Add(selection.ModuleId);
            foreach (var contributor in sp.GetServices<IAgentToolJsonContextContributor>()
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                if (json.ContextContributors.All(item => item.Id != contributor.Id))
                    json.ContextContributors.Add(contributor);
            }
            return json;
        });
        services.TryAddSingleton<IRegistryValidator<AgentCapabilityToolDescriptor>, AgentToolDescriptorValidator>();
        services.TryAddSingleton<IRegistryValidationEngine<AgentCapabilityToolDescriptor>,
            RegistryValidationEngine<AgentCapabilityToolDescriptor>>();
        services.TryAddSingleton<AgentToolRegistry>();
        services.TryAddSingleton<IAgentToolRegistry>(provider => provider.GetRequiredService<AgentToolRegistry>());
        services.TryAddSingleton<SchemaJsonContractProjector>();
        services.TryAddSingleton<SchemaJsonTypeInfoParityValidator>();
        services.TryAddSingleton<IAgentToolJsonSchemaProjector, AgentToolJsonSchemaProjector>();
        services.TryAddSingleton<AgentToolSchemaParityValidator>();
        services.TryAddSingleton<AgentToolCapabilityResolver>();
        services.TryAddSingleton<AgentToolSchemaResolver>();
        services.TryAddSingleton<AgentToolEffectiveGovernanceDeriver>();
        services.TryAddSingleton<AgentToolRuntimeSnapshotBuilder>();
        services.TryAddSingleton<AgentToolRuntimeSnapshotProvider>();
        services.TryAddSingleton<AgentToolProjectionStartupBuilder>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AgentToolProjectionHostedService>());

        services.TryAddSingleton<IAgentToolApprovalGate, FailClosedAgentToolApprovalGate>();
        services.TryAddSingleton<ISchemaValidator, SchemaValidator>();
        services.TryAddSingleton<AgentToolInvocationFingerprintBuilder>();
        services.TryAddSingleton<AgentCapabilityIdempotencyKeyBuilder>();
        services.TryAddSingleton<AgentToolResultMapper>();
        services.TryAddScoped<IAgentToolCatalog, AgentToolCatalog>();
        services.TryAddScoped<IAgentToolInvoker, AgentToolInvoker>();

        // Pre-dispatch reconciliation: register the reconciler, accountability
        // producer, and development in-memory participants. Durable providers
        // (PostgreSQL etc.) replace the in-memory participants via RemoveAll/Replace
        // in their own extension — the reconciler and producer remain.
        services.TryAddSingleton<DevelopmentInMemoryAgentToolInvocationGate>();
        services.TryAddSingleton<IAgentToolInvocationGate>(sp => sp.GetRequiredService<DevelopmentInMemoryAgentToolInvocationGate>());
        services.TryAddSingleton<IAgentToolInvocationLeaseAbandoner>(sp => sp.GetRequiredService<DevelopmentInMemoryAgentToolInvocationGate>());
        services.TryAddSingleton<IAgentToolPreDispatchPersistenceCapabilities>(sp => sp.GetRequiredService<DevelopmentInMemoryAgentToolInvocationGate>());
        services.TryAddSingleton<IAgentToolBudgetGate, DevelopmentInMemoryAgentToolBudgetGate>();
        services.TryAddSingleton<IAgentToolGovernanceAuditor, DevelopmentInMemoryAgentToolGovernanceAuditor>();
        services.TryAddSingleton<IAgentToolPreDispatchReconciliationStore, DevelopmentInMemoryAgentToolPreDispatchReconciliationStore>();
        services.TryAddSingleton<IAgentToolPreDispatchReconciliationAccountabilityProducer, AgentToolPreDispatchReconciliationAccountabilityProducer>();
        services.TryAddSingleton<IAgentToolPreDispatchReconciler, DefaultAgentToolPreDispatchReconciler>();
        return services;
    }
}
