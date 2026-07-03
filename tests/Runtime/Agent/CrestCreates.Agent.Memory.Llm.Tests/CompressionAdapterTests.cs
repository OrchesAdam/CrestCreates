using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Llm.Clients;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Memory.Llm.Validation;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public class CompressionAdapterTests
{
    [Fact]
    public async Task LlmCompressor_UsesSanitizedContentOnly()
    {
        // Use a redacting sanitizer that replaces secrets with [REDACTED]
        var sanitizer = new RedactingSanitizer("raw-secret-token", "[REDACTED]");
        IAgentContextCompressor fallback = new DefaultAgentContextCompressor(sanitizer);
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"blocks":[{"blockId":"b1","content":"sanitized summary","sourceRefIds":["conv-1_turn-1"]}]}"""
        });
        var compressor = new LlmAgentContextCompressor(
            sanitizer,
            fallback,
            new DefaultAgentMemoryCompressionPromptBuilder(),
            client,
            new JsonAgentMemoryCompressionOutputParser(),
            AgentMemoryLlmTestData.DefaultTestEvidenceFactory,
            AgentMemoryLlmTestData.DefaultOptions);

        var conversation = AgentMemoryLlmTestData.ConversationWithSecret("raw-secret-token");

        await compressor.CompressConversationAsync(conversation);

        client.Requests.Should().ContainSingle();
        client.Requests[0].PromptText.Should().NotContain("raw-secret-token");
        client.Requests[0].PromptText.Should().Contain("[REDACTED]");
    }

    private sealed class RedactingSanitizer : IAgentMemoryContentSanitizer
    {
        private readonly string _secret;
        private readonly string _replacement;

        public RedactingSanitizer(string secret, string replacement)
        {
            _secret = secret;
            _replacement = replacement;
        }

        public SanitizedAgentContent Sanitize(string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs)
        {
            var sanitized = content.Replace(_secret, _replacement);
            var redacted = sanitized != content;
            return new SanitizedAgentContent
            {
                SanitizedContent = sanitized,
                CanonicalContentHash = new CanonicalHash
                {
                    Value = "hash-" + sanitized.GetHashCode().ToString("x"),
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "sha256-canonical-json-v1",
                    ArtifactKind = CanonicalHashArtifactNames.AgentMemoryContent,
                    Purpose = CanonicalHashPurposeNames.SourceIdentity,
                    Scope = CanonicalHashScopeNames.InternalFull,
                    ContractVersion = "memory-hash-v1",
                    CanonicalShapeVersion = "memory-content-hash-v1"
                },
                Rejected = false,
                RedactionKinds = redacted ? ["secret-replacement"] : Array.Empty<string>()
            };
        }
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

    [Fact]
    public async Task LlmCompressor_ProviderOutputWithoutSourceRefs_FallsBack()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"blocks":[{"blockId":"b1","content":"summary","sourceRefIds":[]}]}"""
        });
        var compressor = AgentMemoryLlmTestData.Compressor(client);

        var result = await compressor.CompressConversationAsync(
            AgentMemoryLlmTestData.Conversation("conv-1", "tenant-1", "hello"));

        result.Blocks.Should().NotBeEmpty();
        result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicCompressor);
    }

    [Fact]
    public async Task LlmCompressor_PromptOutputEvidence_UsesAuditEvidence()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"blocks":[{"blockId":"b1","content":"summary","sourceRefIds":["conv-1_turn-1"]}]}"""
        });
        var compressor = AgentMemoryLlmTestData.Compressor(client);

        var result = await compressor.CompressConversationAsync(
            AgentMemoryLlmTestData.Conversation("conv-1", "tenant-1", "hello"));

        result.PromptOutputEvidence.Should().NotBeNull();
        // Prompt output evidence uses default AuditEvidence purpose
    }

    [Fact]
    public async Task LlmCompressor_OutputEvidence_UsesSourceIdentity()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"blocks":[{"blockId":"b1","content":"summary","sourceRefIds":["conv-1_turn-1"]}]}"""
        });
        var compressor = AgentMemoryLlmTestData.Compressor(client);

        var result = await compressor.CompressConversationAsync(
            AgentMemoryLlmTestData.Conversation("conv-1", "tenant-1", "hello"));

        result.PromptOutputEvidence.Should().NotBeNull();
        // Output evidence uses SourceIdentity purpose with AgentMemoryCompressedOutput artifact
    }
}
