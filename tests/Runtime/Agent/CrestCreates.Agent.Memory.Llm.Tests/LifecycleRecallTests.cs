using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Clients;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Extraction;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Memory.Llm.Prompting;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Recall;
using CrestCreates.Agent.Memory.Sanitization;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public class LifecycleRecallTests
{
    [Fact]
    public void AddAgentMemoryLlm_RegistersLlmCompressorAndExtractor()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryRuntime();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentMemoryLlmModelClient>(_ => new FakeAgentMemoryLlmModelClient());
        services.AddAgentMemoryLlm();

        var sp = services.BuildServiceProvider();

        var compressor = sp.GetService<IAgentContextCompressor>();
        compressor.Should().BeOfType<LlmAgentContextCompressor>();

        var extractor = sp.GetService<IAgentMemoryExtractor>();
        extractor.Should().BeOfType<LlmAgentMemoryExtractor>();
    }

    [Fact]
    public void AddAgentMemoryLlm_WithoutModelClient_ThrowsAtResolve()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryRuntime();
        services.AddAgentPrompting();
        services.AddAgentMemoryLlm();

        var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<IAgentContextCompressor>();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task FullLifecycle_CompressExtractPromoteRecall()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryRuntime();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentMemoryLlmModelClient>(_ => new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"blocks":[{"blockId":"b1","content":"Users prefer dark mode","sourceRefIds":["conv-1_turn-1"]}]}"""
        }));
        services.AddAgentMemoryLlm();

        var sp = services.BuildServiceProvider();

        // Step 1: Compress conversation
        var compressor = sp.GetRequiredService<IAgentContextCompressor>();
        var conversation = AgentMemoryLlmTestData.Conversation("conv-1", "tenant-1", "I prefer dark mode");
        var compressed = await compressor.CompressConversationAsync(conversation);
        compressed.Blocks.Should().NotBeEmpty();

        // Step 2: Extract candidates
        var extractor = sp.GetRequiredService<IAgentMemoryExtractor>();
        var candidates = await extractor.ExtractCandidatesAsync(compressed);
        candidates.Should().NotBeEmpty();

        // Step 3: Save candidate
        var store = sp.GetRequiredService<IAgentMemoryStore>();
        var candidate = candidates[0];
        await store.SaveCandidateAsync(candidate);

        // Step 4: Promote candidate
        var promotionService = sp.GetRequiredService<IAgentMemoryPromotionService>();
        var promoted = await promotionService.PromoteAsync(
            candidate.TenantId,
            candidate.CandidateId,
            new AgentMemoryOperationRequest
            {
                TenantId = candidate.TenantId,
                InvocationContext = new AgentMemoryInvocationContext
                {
                    TenantId = candidate.TenantId,
                    ActorId = "test-actor",
                    ActorKind = "test"
                },
                Reason = "Test promotion",
                Explanation = "Integration test promotion",
                Timestamp = DateTimeOffset.UtcNow
            });

        // Step 5: Recall
        var retriever = sp.GetRequiredService<IAgentMemoryRetriever>();
        var query = new AgentMemoryQuery
        {
            TenantId = candidate.TenantId,
            Kinds = [candidate.Kind]
        };
        var pack = await retriever.RecallAsync(query);
        pack.Memories.Should().ContainSingle(m =>
            m.Kind == candidate.Kind &&
            m.Content == candidate.Content);
    }
}
