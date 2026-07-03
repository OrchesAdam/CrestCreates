using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Prompting.Tests;

public sealed class PromptingEvidenceFactoryTests
{
    [Fact]
    public void CreateInputEvidence_PropagatesAllRequestFields()
    {
        // Arrange
        var hashService = new Mock<IAgentPromptHashService>();
        var expectedHash = TestHash("input-hash");
        hashService.Setup(h => h.ComputeInputHash(It.IsAny<AgentPromptEvidenceCreationRequest<string>>()))
            .Returns(expectedHash);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var factory = new DefaultAgentPromptEvidenceFactory(hashService.Object, timeProvider);

        var request = new AgentPromptEvidenceCreationRequest<string>
        {
            TemplateId = new AgentPromptTemplateId("test-template"),
            TemplateVersion = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7h.v1"),
            ModelProfileRef = new AgentPromptModelProfileRef("model-a"),
            ProviderProfileRef = new AgentPromptProviderProfileRef("provider-a"),
            Payload = "test-input",
            TenantId = "tenant-1",
            ActorId = "actor-1",
            CorrelationId = "corr-1"
        };

        // Act
        var evidence = factory.CreateInputEvidence(request);

        // Assert
        evidence.TemplateId.Should().Be(request.TemplateId);
        evidence.TemplateVersion.Should().Be(request.TemplateVersion);
        evidence.Purpose.Should().Be(request.Purpose);
        evidence.ContractVersion.Should().Be(request.ContractVersion);
        evidence.ModelProfileRef.Should().Be(request.ModelProfileRef);
        evidence.ProviderProfileRef.Should().Be(request.ProviderProfileRef);
        evidence.Input.Should().Be("test-input");
        evidence.InputHash.Should().Be(expectedHash);
        evidence.CreatedAt.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        evidence.TenantId.Should().Be("tenant-1");
        evidence.ActorId.Should().Be("actor-1");
        evidence.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public void CreateInputEvidence_DelegatesToHashService()
    {
        var hashService = new Mock<IAgentPromptHashService>();
        hashService.Setup(h => h.ComputeInputHash(It.IsAny<AgentPromptEvidenceCreationRequest<string>>()))
            .Returns(TestHash("hash-1"));
        var factory = new DefaultAgentPromptEvidenceFactory(hashService.Object, TimeProvider.System);

        factory.CreateInputEvidence(TestRequest("payload"));

        hashService.Verify(h => h.ComputeInputHash(It.IsAny<AgentPromptEvidenceCreationRequest<string>>()), Times.Once);
    }

    [Fact]
    public void CreateOutputEvidence_WithNonNullHash_NoDiagnostics()
    {
        var hashService = new Mock<IAgentPromptHashService>();
        var outputHash = TestHash("output-hash");
        hashService.Setup(h => h.ComputeOutputHash(
            It.IsAny<AgentPromptEvidenceCreationRequest<string>>(),
            It.IsAny<CanonicalHash>(),
            It.IsAny<AgentPromptProviderObservation?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()))
            .Returns(outputHash);
        var factory = new DefaultAgentPromptEvidenceFactory(hashService.Object, TimeProvider.System);

        var evidence = factory.CreateOutputEvidence(TestRequest("output"), TestHash("input-hash"));

        evidence.OutputHash.Should().Be(outputHash);
        evidence.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void CreateOutputEvidence_WithNullHash_ProducesWarningDiagnostic()
    {
        var hashService = new Mock<IAgentPromptHashService>();
        hashService.Setup(h => h.ComputeOutputHash(
            It.IsAny<AgentPromptEvidenceCreationRequest<string>>(),
            It.IsAny<CanonicalHash>(),
            It.IsAny<AgentPromptProviderObservation?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()))
            .Returns((CanonicalHash?)null);
        var factory = new DefaultAgentPromptEvidenceFactory(hashService.Object, TimeProvider.System);

        var evidence = factory.CreateOutputEvidence(TestRequest("output"), TestHash("input-hash"));

        evidence.OutputHash.Should().BeNull();
        evidence.Diagnostics.Should().ContainSingle(d =>
            d.Code == AgentPromptDiagnosticCodes.OutputHashUnavailable &&
            d.Severity == "Warning");
    }

    [Fact]
    public void CreateOutputEvidence_PropagatesInputHash()
    {
        var hashService = new Mock<IAgentPromptHashService>();
        hashService.Setup(h => h.ComputeOutputHash(
            It.IsAny<AgentPromptEvidenceCreationRequest<string>>(),
            It.IsAny<CanonicalHash>(),
            It.IsAny<AgentPromptProviderObservation?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()))
            .Returns(TestHash("output-hash"));
        var factory = new DefaultAgentPromptEvidenceFactory(hashService.Object, TimeProvider.System);

        var inputHash = TestHash("my-input-hash");
        var evidence = factory.CreateOutputEvidence(TestRequest("output"), inputHash);

        evidence.InputHash.Should().Be(inputHash);
    }

    private static AgentPromptEvidenceCreationRequest<string> TestRequest(string payload) => new()
    {
        TemplateId = new AgentPromptTemplateId("test-template"),
        TemplateVersion = new AgentPromptVersion("v1"),
        Purpose = AgentPromptPurpose.DescriptorAuthoring,
        ContractVersion = new AgentPromptContractVersion("7h.v1"),
        ModelProfileRef = new AgentPromptModelProfileRef("model-default"),
        ProviderProfileRef = new AgentPromptProviderProfileRef("provider-default"),
        Payload = payload
    };

    private static CanonicalHash TestHash(string value) => new()
    {
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = CanonicalHashArtifactNames.AgentPromptInputEvidence,
        Purpose = CanonicalHashPurposeNames.SourceIdentity,
        Scope = CanonicalHashScopeNames.InternalFull,
        ContractVersion = CanonicalHashContractVersions.DescriptorHash,
        CanonicalShapeVersion = "test-shape-v1",
        Value = value
    };

    /// <summary>
    /// Simple fake TimeProvider for deterministic testing.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
