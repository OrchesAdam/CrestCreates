using System.Text.Json;
using CrestCreates.Agent.Memory.Llm.Prompting;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public sealed class PromptEvidenceTests
{
    [Fact]
    public void OutputEvidenceHash_ExcludesRawProviderResponseText()
    {
        typeof(AgentMemoryLlmModelResponseEvidenceProjection)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain("ResponseText");

        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<AgentMemoryLlmModelResponseEvidenceProjection>, AgentMemoryLlmModelResponseEvidenceProjector>();
        using var provider = services.BuildServiceProvider();

        var hashService = provider.GetRequiredService<IAgentPromptHashService>();
        var inputHash = Hash("input-hash");

        var safeA = new AgentMemoryLlmModelResponseEvidenceProjection
        {
            ProviderName = "provider",
            ModelName = "model",
            PromptInputHash = inputHash.Value,
            FailureKind = null,
            FailureDetail = null
        };

        var hashA = hashService.ComputeOutputHash(Request(safeA), inputHash, new AgentPromptProviderObservation { ProviderName = "provider", ModelName = "model" });
        var hashB = hashService.ComputeOutputHash(Request(safeA), inputHash, new AgentPromptProviderObservation { ProviderName = "provider", ModelName = "model" });

        hashA.Should().NotBeNull();
        hashA!.Value.Should().Be(hashB!.Value);
        hashA.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
    }

    private static AgentPromptEvidenceCreationRequest<AgentMemoryLlmModelResponseEvidenceProjection> Request(AgentMemoryLlmModelResponseEvidenceProjection payload) => new()
    {
        TemplateId = new AgentPromptTemplateId("agent-memory.compression.default"),
        TemplateVersion = new AgentPromptVersion("7gplus.v1"),
        Purpose = AgentPromptPurpose.MemoryCompression,
        ContractVersion = new AgentPromptContractVersion("agent-memory-llm.v1"),
        ModelProfileRef = new AgentPromptModelProfileRef("model-a"),
        ProviderProfileRef = new AgentPromptProviderProfileRef("provider-a"),
        Payload = payload
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
