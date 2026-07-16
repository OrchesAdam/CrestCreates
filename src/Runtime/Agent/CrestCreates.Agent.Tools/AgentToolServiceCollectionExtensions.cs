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

        var json = new AgentToolJsonOptions();
        configureJson?.Invoke(json);
        services.TryAddSingleton(json);
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
        return services;
    }
}
