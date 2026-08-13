using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Authoring;
using CrestCreates.Agent.Memory.Bootstrap;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Extraction;
using CrestCreates.Agent.Memory.Identity;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Recall;
using CrestCreates.Agent.Memory.Sanitization;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Agent.Memory;

/// <summary>
/// DI registration extension for the Agent Memory runtime.
/// </summary>
/// <remarks>
/// <para>Prerequisite: <c>ICanonicalHashComputer</c> must be registered before calling this method.</para>
/// <para>Typical usage:</para>
/// <code>
/// services.AddDescriptorStableHash();
/// services.AddAgentMemoryRuntime();
/// </code>
/// <para>
/// If <c>ICanonicalHashComputer</c> is not registered, resolving hash-dependent services
/// (<c>DefaultAgentMemoryContentSanitizer</c>, <c>DefaultAgentMemoryRetriever</c>)
/// will throw <see cref="InvalidOperationException"/> at runtime.
/// </para>
/// </remarks>
public static class AgentMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the read-only half of the Agent Memory runtime: stores, sanitization,
    /// compression, extraction, recall, expansion, authoring, and canonical hashing.
    /// No curation (promotion/archive) is registered. Use <see cref="AddAgentMemoryCuration"/>
    /// to add formal curation on top of a runtime that owns a conditional store.
    /// </summary>
    public static IServiceCollection AddAgentMemoryReadRuntime(this IServiceCollection services)
    {
        // TimeProvider
        services.TryAddSingleton(TimeProvider.System);

        // Accountability primitives
        services.TryAddSingleton<IAgentMemoryOperationIdentityFactory, DefaultAgentMemoryOperationIdentityFactory>();
        services.TryAddSingleton<IAgentMemoryAccountabilityProducer, NullAgentMemoryAccountabilityProducer>();

        // Stores
        services.TryAddSingleton<IAgentMemoryArtifactIdGenerator, DefaultAgentMemoryArtifactIdGenerator>();
        services.TryAddSingleton<IAgentConversationStore, InMemoryAgentConversationStore>();
        services.TryAddSingleton<IAgentTaskHistoryStore, InMemoryAgentTaskHistoryStore>();
        services.TryAddSingleton<IAgentCompressedContextStore, InMemoryAgentCompressedContextStore>();
        services.TryAddSingleton<IAgentMemoryStore, InMemoryAgentMemoryStore>();

        // Sanitization & Compression
        services.TryAddSingleton<IAgentMemoryContentSanitizer, DefaultAgentMemoryContentSanitizer>();
        services.TryAddSingleton<DefaultAgentContextCompressor>(sp =>
            new DefaultAgentContextCompressor(
                sp.GetRequiredService<IAgentMemoryContentSanitizer>(),
                sp.GetRequiredService<IAgentMemoryArtifactIdGenerator>(),
                sp.GetRequiredService<AgentMemoryCanonicalHashProjector>()));
        services.TryAddSingleton<IAgentContextCompressor>(sp =>
            sp.GetRequiredService<DefaultAgentContextCompressor>());

        // Extraction
        services.TryAddSingleton<DefaultAgentMemoryExtractor>(sp =>
            new DefaultAgentMemoryExtractor(
                sp.GetRequiredService<IAgentMemoryArtifactIdGenerator>(),
                sp.GetRequiredService<AgentMemoryCanonicalHashProjector>()));
        services.TryAddSingleton<IAgentMemoryExtractor>(sp =>
            sp.GetRequiredService<DefaultAgentMemoryExtractor>());

        // Recall & Expansion
        services.TryAddSingleton<IAgentMemoryRetriever, DefaultAgentMemoryRetriever>();
        services.TryAddSingleton<IAgentContextSourceExpander, DefaultAgentContextSourceExpander>();

        // Authoring
        services.TryAddSingleton<IAgentAuthoringContextBuilder, DefaultAgentAuthoringContextBuilder>();

        // Canonical Hashing
        services.TryAddSingleton<AgentMemoryCanonicalHashProjector>();

        return services;
    }

    /// <summary>
    /// Registers formal curation (promotion, rejection, supersession, archive) on top of
    /// an Agent Memory read runtime. Requires a conditional, atomic store
    /// (<see cref="IAgentMemoryConditionalCurationStore"/> with
    /// <see cref="AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic"/>); the
    /// <see cref="AgentMemoryCurationCompositionValidator"/> fails closed otherwise.
    /// </summary>
    public static IServiceCollection AddAgentMemoryCuration(this IServiceCollection services)
    {
        // Formal curation marker: absent in read-only runtimes.
        services.TryAddSingleton<IAgentMemoryFormalCurationMarker, AgentMemoryFormalCurationMarker>();

        // Promotion service (concrete + capabilities surfaces).
        services.TryAddSingleton<DefaultAgentMemoryPromotionService>();
        services.TryAddSingleton<AgentMemoryCurationFactProjector>();
        services.TryAddSingleton<IAgentMemoryPromotionService>(sp =>
            sp.GetRequiredService<DefaultAgentMemoryPromotionService>());
        services.TryAddSingleton<IAgentMemoryCurationServiceCapabilities>(sp =>
            sp.GetRequiredService<DefaultAgentMemoryPromotionService>());

        // One shared singleton surfaced as both validator and hosted service.
        // TryAddEnumerable requires an implementation type and cannot deduplicate a
        // factory descriptor, so gate the two surface registrations on the concrete
        // singleton: AddAgentMemoryCuration() called twice must not double-register.
        var validatorAlreadyRegistered = services.Any(d =>
            d.ServiceType == typeof(AgentMemoryCurationCompositionValidator));
        services.TryAddSingleton<AgentMemoryCurationCompositionValidator>();
        if (!validatorAlreadyRegistered)
        {
            services.Add(ServiceDescriptor.Singleton<IBootstrapValidator>(sp =>
                sp.GetRequiredService<AgentMemoryCurationCompositionValidator>()));
            services.Add(ServiceDescriptor.Singleton<IHostedService>(sp =>
                sp.GetRequiredService<AgentMemoryCurationCompositionValidator>()));
        }

        return services;
    }

    public static IServiceCollection AddAgentMemoryRuntime(this IServiceCollection services)
        => services.AddAgentMemoryReadRuntime().AddAgentMemoryCuration();
}
