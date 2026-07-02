using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace CrestCreates.Agent.Prompting.Tests;

public sealed class PromptingContractTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SemanticValueObjects_RejectBlankValues(string value)
    {
        Action createTemplateId = () => _ = new AgentPromptTemplateId(value);
        Action createVersion = () => _ = new AgentPromptVersion(value);
        Action createContractVersion = () => _ = new AgentPromptContractVersion(value);
        Action createModelRef = () => _ = new AgentPromptModelProfileRef(value);
        Action createProviderRef = () => _ = new AgentPromptProviderProfileRef(value);

        createTemplateId.Should().Throw<ArgumentException>();
        createVersion.Should().Throw<ArgumentException>();
        createContractVersion.Should().Throw<ArgumentException>();
        createModelRef.Should().Throw<ArgumentException>();
        createProviderRef.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SemanticValueObjects_ToStringReturnsValue()
    {
        new AgentPromptTemplateId("descriptor-authoring").ToString().Should().Be("descriptor-authoring");
        new AgentPromptVersion("v1").ToString().Should().Be("v1");
        new AgentPromptContractVersion("7h.v1").ToString().Should().Be("7h.v1");
        new AgentPromptModelProfileRef("model-default").ToString().Should().Be("model-default");
        new AgentPromptProviderProfileRef("provider-default").ToString().Should().Be("provider-default");
    }

    [Fact]
    public void TemplateDescriptor_MetadataDefaultsToEmptyDictionary()
    {
        var descriptor = new AgentPromptTemplateDescriptor
        {
            TemplateId = new AgentPromptTemplateId("descriptor-authoring"),
            Version = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7h.v1")
        };

        descriptor.Metadata.Should().NotBeNull();
        descriptor.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void PromptEvidence_DiagnosticsDefaultToEmptyCollections()
    {
        var hash = TestHash("input-hash", CanonicalHashPurposeNames.SourceIdentity);

        var input = new AgentPromptInputEvidence<string>
        {
            TemplateId = new AgentPromptTemplateId("descriptor-authoring"),
            TemplateVersion = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7h.v1"),
            ModelProfileRef = new AgentPromptModelProfileRef("model-default"),
            ProviderProfileRef = new AgentPromptProviderProfileRef("provider-default"),
            Input = "safe input",
            InputHash = hash
        };

        var output = new AgentPromptOutputEvidence<string>
        {
            TemplateId = input.TemplateId,
            TemplateVersion = input.TemplateVersion,
            Purpose = input.Purpose,
            ContractVersion = input.ContractVersion,
            ModelProfileRef = input.ModelProfileRef,
            ProviderProfileRef = input.ProviderProfileRef,
            InputHash = hash,
            Output = "safe output"
        };

        input.Diagnostics.Should().NotBeNull().And.BeEmpty();
        output.Diagnostics.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void EvidenceSummary_JsonContextSerializesWithoutGenericPayload()
    {
        var summary = new AgentPromptInputEvidenceSummary
        {
            TemplateId = new AgentPromptTemplateId("descriptor-authoring"),
            TemplateVersion = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7h.v1"),
            ModelProfileRef = new AgentPromptModelProfileRef("model-default"),
            ProviderProfileRef = new AgentPromptProviderProfileRef("provider-default"),
            InputHash = TestHash("input-hash", CanonicalHashPurposeNames.SourceIdentity),
            CreatedAt = DateTimeOffset.UnixEpoch
        };

        var json = JsonSerializer.Serialize(
            summary,
            AgentPromptingJsonSerializerContext.Default.AgentPromptInputEvidenceSummary);

        json.Should().Contain("templateId");
        json.Should().NotContain("safe input");
        json.Should().NotContain("payload");
    }

    private static CanonicalHash TestHash(string value, string purpose) => new()
    {
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "AgentPromptInputEvidence",
        Purpose = purpose,
        Scope = CanonicalHashScopeNames.InternalFull,
        ContractVersion = CanonicalHashContractVersions.DescriptorHash,
        CanonicalShapeVersion = "test-shape-v1",
        Value = value
    };
}
