using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Extraction;
using CrestCreates.Agent.Memory.Llm.Clients;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Extraction;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public sealed class ServiceCollectionTests
{
    [Fact]
    public void AddAgentMemoryRuntime_UsesSameConcreteFallbackInstances_AsDefaultInterfaces()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentMemoryReadRuntime();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAgentContextCompressor>()
            .Should().BeSameAs(provider.GetRequiredService<DefaultAgentContextCompressor>());
        provider.GetRequiredService<IAgentMemoryExtractor>()
            .Should().BeSameAs(provider.GetRequiredService<DefaultAgentMemoryExtractor>());
    }

    [Fact]
    public void AddAgentMemoryLlmCompressor_OnlyReplacesCompressor_NotExtractor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentMemoryReadRuntime();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentMemoryLlmModelClient>(_ => new FakeAgentMemoryLlmModelClient());
        services.AddAgentMemoryLlmCompressor();

        using var provider = services.BuildServiceProvider();

        // Compressor should be LLM-backed
        provider.GetRequiredService<IAgentContextCompressor>()
            .Should().BeOfType<LlmAgentContextCompressor>();

        // Extractor should remain the default deterministic one
        provider.GetRequiredService<IAgentMemoryExtractor>()
            .Should().BeOfType<DefaultAgentMemoryExtractor>();
    }

    [Fact]
    public void AddAgentMemoryLlmExtractor_OnlyReplacesExtractor_NotCompressor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentMemoryReadRuntime();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentMemoryLlmModelClient>(_ => new FakeAgentMemoryLlmModelClient());
        services.AddAgentMemoryLlmExtractor();

        using var provider = services.BuildServiceProvider();

        // Compressor should remain the default deterministic one
        provider.GetRequiredService<IAgentContextCompressor>()
            .Should().BeOfType<DefaultAgentContextCompressor>();

        // Extractor should be LLM-backed
        provider.GetRequiredService<IAgentMemoryExtractor>()
            .Should().BeOfType<LlmAgentMemoryExtractor>();
    }

    [Fact]
    public void AddAgentMemoryLlm_ReplacesBothCompressorAndExtractor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentMemoryReadRuntime();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentMemoryLlmModelClient>(_ => new FakeAgentMemoryLlmModelClient());
        services.AddAgentMemoryLlm();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAgentContextCompressor>()
            .Should().BeOfType<LlmAgentContextCompressor>();
        provider.GetRequiredService<IAgentMemoryExtractor>()
            .Should().BeOfType<LlmAgentMemoryExtractor>();
    }
}
