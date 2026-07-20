using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Authoring;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Extraction;
using CrestCreates.Agent.Memory.Identity;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Recall;
using CrestCreates.Agent.Memory.Sanitization;
using CrestCreates.Agent.Memory.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public static IServiceCollection AddAgentMemoryRuntime(this IServiceCollection services)
    {
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

        // Extraction & Promotion
        services.TryAddSingleton<DefaultAgentMemoryExtractor>(sp =>
            new DefaultAgentMemoryExtractor(
                sp.GetRequiredService<IAgentMemoryArtifactIdGenerator>(),
                sp.GetRequiredService<AgentMemoryCanonicalHashProjector>()));
        services.TryAddSingleton<IAgentMemoryExtractor>(sp =>
            sp.GetRequiredService<DefaultAgentMemoryExtractor>());
        services.TryAddSingleton<DefaultAgentMemoryPromotionService>();
        services.TryAddSingleton<IAgentMemoryPromotionService>(sp =>
            sp.GetRequiredService<DefaultAgentMemoryPromotionService>());
        services.TryAddSingleton<IAgentMemoryCurationServiceCapabilities>(sp =>
            sp.GetRequiredService<DefaultAgentMemoryPromotionService>());

        // Recall & Expansion
        services.TryAddSingleton<IAgentMemoryRetriever, DefaultAgentMemoryRetriever>();
        services.TryAddSingleton<IAgentContextSourceExpander, DefaultAgentContextSourceExpander>();

        // Authoring
        services.TryAddSingleton<IAgentAuthoringContextBuilder, DefaultAgentAuthoringContextBuilder>();

        // Canonical Hashing
        services.TryAddSingleton<AgentMemoryCanonicalHashProjector>();

        // TimeProvider
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
