using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Authoring;
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Extraction;
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
public static class AgentMemoryServiceCollectionExtensions
{
    public static IServiceCollection AddAgentMemoryRuntime(this IServiceCollection services)
    {
        // Stores
        services.TryAddSingleton<IAgentConversationStore, InMemoryAgentConversationStore>();
        services.TryAddSingleton<IAgentTaskHistoryStore, InMemoryAgentTaskHistoryStore>();
        services.TryAddSingleton<IAgentCompressedContextStore, InMemoryAgentCompressedContextStore>();
        services.TryAddSingleton<IAgentMemoryStore, InMemoryAgentMemoryStore>();

        // Sanitization & Compression
        services.TryAddSingleton<IAgentMemoryContentSanitizer, DefaultAgentMemoryContentSanitizer>();
        services.TryAddSingleton<IAgentContextCompressor, DefaultAgentContextCompressor>();

        // Extraction & Promotion
        services.TryAddSingleton<IAgentMemoryExtractor, DefaultAgentMemoryExtractor>();
        services.TryAddSingleton<IAgentMemoryPromotionService, DefaultAgentMemoryPromotionService>();

        // Recall & Expansion
        services.TryAddSingleton<IAgentMemoryRetriever, DefaultAgentMemoryRetriever>();
        services.TryAddSingleton<IAgentContextSourceExpander, DefaultAgentContextSourceExpander>();

        // Authoring
        services.TryAddSingleton<IAgentAuthoringContextBuilder, DefaultAgentAuthoringContextBuilder>();

        // TimeProvider
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
