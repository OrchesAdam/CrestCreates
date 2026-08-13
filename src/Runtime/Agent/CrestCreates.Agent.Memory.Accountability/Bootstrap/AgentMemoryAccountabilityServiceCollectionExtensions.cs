using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability.CanonicalHashing;
using CrestCreates.Agent.Memory.Accountability.Options;
using CrestCreates.Agent.Memory.Accountability.Production;
using CrestCreates.Agent.Memory.Accountability.Sanitization;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestCreates.Agent.Memory.Accountability.Bootstrap;

/// <summary>
/// Registers the real Agent Memory Accountability write bridge on top of an
/// existing Agent Memory runtime. The bridge never registers audit sinks and
/// never calls AddAccountability — the host decides the storage substrate and
/// the Accountability foundation separately. The bridge only replaces the
/// read-runtime's null producer with the real producer and fails closed at
/// startup when the surrounding Accountability composition is incomplete.
/// </summary>
public static class AgentMemoryAccountabilityServiceCollectionExtensions
{
    public static IServiceCollection AddAgentMemoryAccountability(
        this IServiceCollection services,
        Action<AgentMemoryAccountabilityOptions>? configure = null)
    {
        services.AddOptions<AgentMemoryAccountabilityOptions>();
        if (configure is not null)
            services.Configure(configure);
        services.TryAddSingleton<AgentMemoryAccountabilityOptions>(sp =>
            sp.GetRequiredService<IOptions<AgentMemoryAccountabilityOptions>>().Value);

        services.TryAddSingleton<AgentMemoryAccountabilityAuditIdProjector>();

        // Exactly three frozen payload rules, registered before any sink/registry
        // resolve. Unknown Kinds remain rejected by the Accountability sanitizer.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuditPayloadSanitizationRule, RecallPayloadSanitizationRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuditPayloadSanitizationRule, CurationPayloadSanitizationRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuditPayloadSanitizationRule, SourceExpansionPayloadSanitizationRule>());

        // One shared singleton surfaced as both validator and hosted service,
        // gated on the concrete type so repeated calls do not double-register.
        var validatorAlreadyRegistered = services.Any(d =>
            d.ServiceType == typeof(AgentMemoryAccountabilityCompositionValidator));
        services.TryAddSingleton<AgentMemoryAccountabilityCompositionValidator>();
        if (!validatorAlreadyRegistered)
        {
            services.Add(ServiceDescriptor.Singleton<IBootstrapValidator>(sp =>
                sp.GetRequiredService<AgentMemoryAccountabilityCompositionValidator>()));
            services.Add(ServiceDescriptor.Singleton<IHostedService>(sp =>
                sp.GetRequiredService<AgentMemoryAccountabilityCompositionValidator>()));
        }

        // Replace the read runtime's null producer with the real producer. The
        // bridge only sees the INull marker, never the concrete null type.
        foreach (var descriptor in services
                     .Where(d => d.ServiceType == typeof(IAgentMemoryAccountabilityProducer)
                         && typeof(INullAgentMemoryAccountabilityProducer).IsAssignableFrom(d.ImplementationType))
                     .ToArray())
        {
            services.Remove(descriptor);
        }
        services.AddSingleton<IAgentMemoryAccountabilityProducer, AgentMemoryAccountabilityProducer>();

        return services;
    }
}
