using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Clients;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Extraction;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Memory.Llm.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.Memory.Llm;

/// <summary>
/// Opt-in DI registration for LLM-backed memory compression and extraction.
/// Each adapter can be registered independently via
/// <see cref="AddAgentMemoryLlmCompressor"/> and <see cref="AddAgentMemoryLlmExtractor"/>,
/// or both together via <see cref="AddAgentMemoryLlm"/>.
/// </summary>
/// <remarks>
/// <para>Prerequisites:</para>
/// <list type="bullet">
///   <item><c>services.AddAgentMemoryRuntime()</c> must be called first</item>
///   <item><c>services.AddAgentPrompting()</c> must be called first</item>
///   <item>An <see cref="IAgentMemoryLlmModelClient"/> must be registered separately</item>
/// </list>
/// </remarks>
public static class AgentMemoryLlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers both LLM-backed compressor and extractor.
    /// Equivalent to calling <see cref="AddAgentMemoryLlmCompressor"/> +
    /// <see cref="AddAgentMemoryLlmExtractor"/>.
    /// </summary>
    public static IServiceCollection AddAgentMemoryLlm(
        this IServiceCollection services,
        Action<AgentMemoryLlmAdapterOptions>? configure = null)
    {
        services.AddAgentMemoryLlmCompressor(configure);
        services.AddAgentMemoryLlmExtractor(configure);
        return services;
    }

    /// <summary>
    /// Registers the LLM-backed context compressor, replacing the previously
    /// registered <see cref="IAgentContextCompressor"/> with an LLM-backed
    /// implementation that falls back to the previous one on failure.
    /// </summary>
    public static IServiceCollection AddAgentMemoryLlmCompressor(
        this IServiceCollection services,
        Action<AgentMemoryLlmAdapterOptions>? configure = null)
    {
        GuardDoubleRegistration(services, "AgentMemoryLlmCompressor");
        EnsureOptions(services, configure);

        // Compression-specific services
        services.TryAddSingleton<IAgentMemoryCompressionPromptBuilder, DefaultAgentMemoryCompressionPromptBuilder>();
        services.TryAddSingleton<IAgentMemoryCompressionOutputParser, JsonAgentMemoryCompressionOutputParser>();

        // Input and output hash projectors for compression
        services.TryAddSingleton<AgentMemoryCompressionPromptInputProjector>();
        services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<AgentMemoryCompressionPromptInput>>(
            sp => sp.GetRequiredService<AgentMemoryCompressionPromptInputProjector>());
        services.TryAddSingleton<AgentMemoryCompressionOutputProjector>();
        services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<IReadOnlyList<AgentCompressedContextBlock>>>(
            sp => sp.GetRequiredService<AgentMemoryCompressionOutputProjector>());

        // Capture the current IAgentContextCompressor registration as fallback
        var capturedCompressorDescriptor = services.LastOrDefault(sd => sd.ServiceType == typeof(IAgentContextCompressor));

        // Remove existing interface registration — replace with LLM-backed version
        services.RemoveAll<IAgentContextCompressor>();

        services.TryAddSingleton<LlmAgentContextCompressor>(sp =>
        {
            var fallback = ResolveFallback<IAgentContextCompressor>(sp, capturedCompressorDescriptor);
            return new LlmAgentContextCompressor(
                sp.GetRequiredService<IAgentMemoryContentSanitizer>(),
                fallback,
                sp.GetRequiredService<IAgentMemoryCompressionPromptBuilder>(),
                sp.GetRequiredService<IAgentMemoryLlmModelClient>(),
                sp.GetRequiredService<IAgentMemoryCompressionOutputParser>(),
                sp.GetRequiredService<IAgentPromptEvidenceFactory>(),
                sp.GetRequiredService<AgentMemoryLlmAdapterOptions>());
        });

        services.TryAddSingleton<IAgentContextCompressor>(sp => sp.GetRequiredService<LlmAgentContextCompressor>());

        return services;
    }

    /// <summary>
    /// Registers the LLM-backed memory extractor, replacing the previously
    /// registered <see cref="IAgentMemoryExtractor"/> with an LLM-backed
    /// implementation that falls back to the previous one on failure.
    /// </summary>
    public static IServiceCollection AddAgentMemoryLlmExtractor(
        this IServiceCollection services,
        Action<AgentMemoryLlmAdapterOptions>? configure = null)
    {
        GuardDoubleRegistration(services, "AgentMemoryLlmExtractor");
        EnsureOptions(services, configure);

        // Extraction-specific services
        services.TryAddSingleton<IAgentMemoryExtractionPromptBuilder, DefaultAgentMemoryExtractionPromptBuilder>();
        services.TryAddSingleton<IAgentMemoryExtractionOutputParser, JsonAgentMemoryExtractionOutputParser>();

        // Input and output hash projectors for extraction
        services.TryAddSingleton<AgentMemoryExtractionPromptInputProjector>();
        services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<AgentMemoryExtractionPromptInput>>(
            sp => sp.GetRequiredService<AgentMemoryExtractionPromptInputProjector>());
        services.TryAddSingleton<AgentMemoryExtractionOutputProjector>();
        services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<IReadOnlyList<AgentMemoryCandidate>>>(
            sp => sp.GetRequiredService<AgentMemoryExtractionOutputProjector>());

        // Capture the current IAgentMemoryExtractor registration as fallback
        var capturedExtractorDescriptor = services.LastOrDefault(sd => sd.ServiceType == typeof(IAgentMemoryExtractor));

        // Remove existing interface registration — replace with LLM-backed version
        services.RemoveAll<IAgentMemoryExtractor>();

        services.TryAddSingleton<LlmAgentMemoryExtractor>(sp =>
        {
            var fallback = ResolveFallback<IAgentMemoryExtractor>(sp, capturedExtractorDescriptor);
            return new LlmAgentMemoryExtractor(
                sp.GetRequiredService<IAgentMemoryContentSanitizer>(),
                fallback,
                sp.GetRequiredService<IAgentMemoryExtractionPromptBuilder>(),
                sp.GetRequiredService<IAgentMemoryLlmModelClient>(),
                sp.GetRequiredService<IAgentMemoryExtractionOutputParser>(),
                sp.GetRequiredService<IAgentPromptEvidenceFactory>(),
                sp.GetRequiredService<AgentMemoryLlmAdapterOptions>());
        });

        services.TryAddSingleton<IAgentMemoryExtractor>(sp => sp.GetRequiredService<LlmAgentMemoryExtractor>());

        return services;
    }

    private static void GuardDoubleRegistration(IServiceCollection services, string registrationKey)
    {
        var guardKey = $"__CrestCreates_Guard_{registrationKey}";
        if (services.Any(sd => sd.ServiceType == typeof(string) && sd.ServiceKey as string == guardKey))
            throw new InvalidOperationException(
                $"{registrationKey} has already been registered. Do not call AddAgentMemoryLlmCompressor/AddAgentMemoryLlmExtractor twice.");

        services.AddKeyedSingleton<string>(guardKey, (_, _) => guardKey);
    }

    private static void EnsureOptions(IServiceCollection services, Action<AgentMemoryLlmAdapterOptions>? configure)
    {
        // Only create and register options if not already registered
        if (services.Any(sd => sd.ServiceType == typeof(AgentMemoryLlmAdapterOptions)))
            return;

        var options = new AgentMemoryLlmAdapterOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
    }

    private static T ResolveFallback<T>(IServiceProvider sp, ServiceDescriptor? captured)
        where T : class
    {
        if (captured is null)
            throw new InvalidOperationException(
                $"No {typeof(T).Name} was registered before AddAgentMemoryLlm. " +
                "Call services.AddAgentMemoryRuntime() first.");

        if (captured.ImplementationInstance is T instance)
            return instance;

        if (captured.ImplementationFactory is not null)
            return (T)captured.ImplementationFactory(sp);

        if (captured.ImplementationType is not null)
            return (T)ActivatorUtilities.CreateInstance(sp, captured.ImplementationType);

        throw new InvalidOperationException(
            $"Cannot resolve fallback {typeof(T).Name} from captured descriptor.");
    }
}
