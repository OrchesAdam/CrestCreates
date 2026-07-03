using CrestCreates.Agent.Memory.Llm.Clients;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public sealed class RecordedClientTests
{
    [Fact]
    public async Task MissingFixture_ReturnsProviderUnavailable_NotEmptySuccess()
    {
        var client = new RecordedAgentMemoryLlmModelClient(Array.Empty<RecordedAgentMemoryLlmFixture>());
        var response = await client.CompleteAsync(Request("hash-missing"));

        response.ResponseText.Should().BeNull();
        response.FailureKind.Should().Be(AgentMemoryLlmProviderFailureKind.ProviderUnavailable);
        response.FailureDetail.Should().Contain("MissingRecordedFixture");
    }

    [Fact]
    public async Task FixtureMatch_UsesPromptHashTemplateAndProfileRefs()
    {
        var fixture = new RecordedAgentMemoryLlmFixture(
            PromptInputHash: "hash-1",
            TemplateId: "agent-memory.compression.default",
            TemplateVersion: "7gplus.v1",
            ModelProfileRef: "model-a",
            ProviderProfileRef: "provider-a",
            ResponseText: """{"blocks":[]}""",
            ProviderName: "recorded",
            ModelName: "model-a");

        var client = new RecordedAgentMemoryLlmModelClient([fixture]);
        var response = await client.CompleteAsync(Request("hash-1"));

        response.ResponseText.Should().Be("""{"blocks":[]}""");
        response.FailureKind.Should().BeNull();
    }

    private static AgentMemoryLlmModelRequest Request(string promptInputHash) => new()
    {
        PromptText = "prompt",
        PromptInputEvidence = new AgentPromptInputEvidenceSummary
        {
            TemplateId = new AgentPromptTemplateId("agent-memory.compression.default"),
            TemplateVersion = new AgentPromptVersion("7gplus.v1"),
            Purpose = AgentPromptPurpose.MemoryCompression,
            ContractVersion = new AgentPromptContractVersion("agent-memory-llm.v1"),
            ModelProfileRef = new AgentPromptModelProfileRef("model-a"),
            ProviderProfileRef = new AgentPromptProviderProfileRef("provider-a"),
            InputHash = Hash(promptInputHash),
            CreatedAt = DateTimeOffset.UnixEpoch
        }
    };

    private static CanonicalHash Hash(string value) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = CanonicalHashArtifactNames.AgentPromptInputEvidence,
        Purpose = CanonicalHashPurposeNames.SourceIdentity,
        Scope = CanonicalHashScopeNames.InternalFull,
        ContractVersion = "descriptor-hash-v1",
        CanonicalShapeVersion = "agent-prompt-input-evidence-v1"
    };
}
