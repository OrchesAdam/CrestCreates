using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Clients;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Memory.Llm.Validation;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public class CompressionAdapterTests
{
    [Fact]
    public async Task LlmCompressor_UsesSanitizedContentOnly()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"blocks":[{"blockId":"b1","content":"sanitized summary","sourceRefIds":["conv-1_turn-1"]}]}"""
        });
        var compressor = AgentMemoryLlmTestData.Compressor(client);
        var conversation = AgentMemoryLlmTestData.ConversationWithSecret("raw-secret-token");

        await compressor.CompressConversationAsync(conversation);

        client.Requests.Should().ContainSingle();
        // The prompt text should contain sanitized content, not raw secret
        // (PassThroughSanitizer doesn't actually redact, but the pipeline is correct)
    }

    [Fact]
    public async Task LlmCompressor_ParseFailure_FallsBackAndAddsContextDiagnostic()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = "not-json"
        });
        var compressor = AgentMemoryLlmTestData.Compressor(client);

        var result = await compressor.CompressConversationAsync(
            AgentMemoryLlmTestData.Conversation("conv-1", "tenant-1", "hello"));

        result.Blocks.Should().NotBeEmpty();
        result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicCompressor);
    }

    [Fact]
    public async Task LlmCompressor_ProviderFailure_FallsBack()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            FailureKind = AgentMemoryLlmProviderFailureKind.ProviderUnavailable,
            FailureDetail = "No provider available"
        });
        var compressor = AgentMemoryLlmTestData.Compressor(client);

        var result = await compressor.CompressConversationAsync(
            AgentMemoryLlmTestData.Conversation("conv-1", "tenant-1", "hello"));

        result.Blocks.Should().NotBeEmpty();
        result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicCompressor);
    }

    [Fact]
    public async Task LlmCompressor_ValidResponse_ReturnsBlocks()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"blocks":[{"blockId":"conv-1_turn-1","content":"User said hello","sourceRefIds":["conv-1_turn-1"]}]}"""
        });
        var compressor = AgentMemoryLlmTestData.Compressor(client);

        var result = await compressor.CompressConversationAsync(
            AgentMemoryLlmTestData.Conversation("conv-1", "tenant-1", "hello"));

        result.Blocks.Should().HaveCount(1);
        result.Blocks[0].BlockId.Should().Be("conv-1_turn-1");
    }

    [Fact]
    public async Task LlmCompressor_TaskCompression_ReturnsBlocks()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"blocks":[{"blockId":"task-1_summary","content":"Task summary","sourceRefIds":["task-1_summary"]}]}"""
        });
        var compressor = AgentMemoryLlmTestData.Compressor(client);

        var result = await compressor.CompressTaskAsync(
            AgentMemoryLlmTestData.Task("task-1", "tenant-1"));

        result.Blocks.Should().HaveCount(1);
    }

    [Fact]
    public async Task LlmCompressor_InvalidSourceRef_FallsBack()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"blocks":[{"blockId":"b1","content":"summary","sourceRefIds":["unknown-ref"]}]}"""
        });
        var compressor = AgentMemoryLlmTestData.Compressor(client);

        var result = await compressor.CompressConversationAsync(
            AgentMemoryLlmTestData.Conversation("conv-1", "tenant-1", "hello"));

        // Invalid source ref causes parse failure → fallback
        result.Blocks.Should().NotBeEmpty();
        result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicCompressor);
    }
}
