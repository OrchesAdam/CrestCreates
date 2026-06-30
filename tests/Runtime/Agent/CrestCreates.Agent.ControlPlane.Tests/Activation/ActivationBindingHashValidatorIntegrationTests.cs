using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

// semantic-string-guard: allow

namespace CrestCreates.Agent.ControlPlane.Tests.Activation;

/// <summary>
/// Integration tests verifying ActivationBindingHashValidator is wired into
/// activation submission, evidence recheck, and runtime gate execution flows.
/// </summary>
public class ActivationBindingHashValidatorIntegrationTests
{
    private const string TestTenantId = "tenant-001";
    private const string TestCorrelationId = "corr-001";

    // ════════════════════════════════════════════════════════════════════════
    // Stub payload for Draft in recheck tests
    // ════════════════════════════════════════════════════════════════════════

    private sealed record StubDescriptorDraftPayload : DraftAbstractions.DescriptorDraftPayload
    {
        public override DescriptorKind DescriptorKind => DescriptorKind.Event;
        public override IDescriptor GetDescriptor() => new TestDescriptor
        {
            Namespace = "test",
            Id = "desc-001",
            Name = "TestDescriptor",
            Kind = DescriptorKind.Event,
            State = DescriptorState.Active
        };
        public override DraftAbstractions.DescriptorDraftPayload Snapshot() => this;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helper factories
    // ════════════════════════════════════════════════════════════════════════

    private static CanonicalHash CreateTestCanonicalHash(
        string value = "test-hash",
        string algorithmVersion = "sha256-canonical-json-v1",
        string contractVersion = "canonical-hash-v1",
        string? artifactKind = null,
        string? purpose = null)
        => new()
        {
            Algorithm = "SHA-256",
            AlgorithmVersion = algorithmVersion,
            ArtifactKind = artifactKind ?? CanonicalHashArtifactNames.Descriptor,
            Scope = CanonicalHashScopeNames.InternalFull,
            Purpose = purpose ?? CanonicalHashPurposeNames.Contract,
            ContractVersion = contractVersion,
            CanonicalShapeVersion = "test-v1",
            Value = value
        };

    private static BindingHashes CreateValidBindingHashes()
        => new()
        {
            SourceReviewHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.ReviewResult, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.SourceBinding, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "src-review-hash" },
            ReviewManifestHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.ReviewResult, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Integrity, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "manifest-hash" },
            PackageManifestHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageManifest, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Integrity, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "package-manifest-hash" },
            PackageEvidenceHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageEvidence, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.AuditEvidence, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "package-evidence-hash" },
            PackageEvidenceEnvelopeHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageEvidenceEnvelope, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.AuditEvidence, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "package-envelope-hash" },
            ContractHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Contract, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "contract-hash" },
            DefinitionHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Definition, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "definition-hash" }
        };

    private static ActivationBindingSnapshot CreateTestBindingSnapshot(
        string draftId = "draft-001",
        BindingHashes? hashes = null)
        => new()
        {
            TenantId = TestTenantId,
            DraftId = draftId,
            DraftVersion = 1,
            ReviewResultId = "review-001",
            PackagePreviewId = "pkg-001",
            EvidencePreviewId = "ev-001",
            Hashes = hashes ?? CreateValidBindingHashes(),
            CorrelationId = TestCorrelationId,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static ActivationRequest CreateTestActivationRequest(
        string requestId = "req-001",
        string draftId = "draft-001",
        ActivationBindingSnapshot? bindingSnapshot = null)
        => new()
        {
            RequestId = requestId,
            TenantId = TestTenantId,
            DraftId = draftId,
            Status = ActivationRequestStatus.Submitted,
            SubmittedAt = DateTimeOffset.UtcNow,
            SubmittedBy = "agent-001",
            CreatedByActorId = "agent-001",
            CreatedByActorKind = DescriptorActivationActorKind.Agent,
            GovernanceDecision = DescriptorLifecycleDecisionKind.Allowed,
            Eligibility = DescriptorActivationEligibility.AutoActivatable,
            BindingSnapshot = bindingSnapshot ?? CreateTestBindingSnapshot(draftId)
        };

    private static AgentToolInvocationContext CreateTestInvocationContext(
        string toolName = "ExecuteActivationGate")
        => new()
        {
            TenantId = TestTenantId,
            ActorId = "agent-001",
            ActorKind = AgentToolActorKind.Agent,
            CorrelationId = TestCorrelationId,
            ToolName = toolName,
            InvocationSource = AgentToolInvocationSource.Direct
        };

    // ════════════════════════════════════════════════════════════════════════
    // 1. Validator unit test: valid hashes → no errors
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateBindingHashes_AllValid_NoErrors()
    {
        // Arrange
        var validator = new ActivationBindingHashValidator();
        var hashes = CreateValidBindingHashes();

        // Act
        var issues = validator.Validate(hashes);

        // Assert
        issues.Should().BeEmpty();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Submission: empty hash value → error
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateBindingHashes_EmptyValue_ReturnsError()
    {
        // Arrange
        var validator = new ActivationBindingHashValidator();
        var hashes = CreateValidBindingHashes() with { SourceReviewHash = CreateTestCanonicalHash("", artifactKind: CanonicalHashArtifactNames.ReviewResult, purpose: CanonicalHashPurposeNames.SourceBinding) };

        // Act
        var issues = validator.Validate(hashes);

        // Assert
        issues.Should().NotBeEmpty();
        issues.Should().ContainSingle(i => i.Slot == "SourceReviewHash" && i.Severity == BindingHashValidationSeverity.Error);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Submission: AlgorithmVersion mismatch → error
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateBindingHashes_AlgorithmVersionMismatch_ReturnsError()
    {
        // Arrange
        var validator = new ActivationBindingHashValidator();
        var hashes = CreateValidBindingHashes() with
        {
            SourceReviewHash = CreateTestCanonicalHash("src-review-hash", algorithmVersion: "sha256-canonical-json-v1", artifactKind: CanonicalHashArtifactNames.ReviewResult, purpose: CanonicalHashPurposeNames.SourceBinding),
            ContractHash = CreateTestCanonicalHash("contract-hash", algorithmVersion: "sha256-canonical-json-v2", artifactKind: CanonicalHashArtifactNames.Descriptor, purpose: CanonicalHashPurposeNames.Contract)
        };

        // Act
        var issues = validator.Validate(hashes);

        // Assert
        issues.Should().NotBeEmpty();
        issues.Should().Contain(i => i.Slot == "AlgorithmVersion" && i.Severity == BindingHashValidationSeverity.Error);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. ContractVersion mismatch → warning (blocks at policy layer)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateBindingHashes_ContractVersionMismatch_ReturnsWarning()
    {
        // Arrange
        var validator = new ActivationBindingHashValidator();
        var hashes = CreateValidBindingHashes() with
        {
            SourceReviewHash = CreateTestCanonicalHash("src-review-hash", contractVersion: "canonical-hash-v1", artifactKind: CanonicalHashArtifactNames.ReviewResult, purpose: CanonicalHashPurposeNames.SourceBinding),
            ContractHash = CreateTestCanonicalHash("contract-hash", contractVersion: "canonical-hash-v2", artifactKind: CanonicalHashArtifactNames.Descriptor, purpose: CanonicalHashPurposeNames.Contract)
        };

        // Act
        var issues = validator.Validate(hashes);

        // Assert
        issues.Should().NotBeEmpty();
        issues.Should().Contain(i => i.Slot == "ContractVersion" && i.Severity == BindingHashValidationSeverity.Warning);
        // Warnings should not contain any errors
        issues.Where(i => i.Severity == BindingHashValidationSeverity.Error).Should().BeEmpty();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. Recheck: valid hashes → no drift
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EvidenceRechecker_ValidHashes_NoDriftDetected()
    {
        // Arrange
        var hashBuilderMock = new Mock<IDescriptorStableHashBuilder>();
        var draftStoreMock = new Mock<DraftAbstractions.IDescriptorDraftStore>();
        var artifactResolverMock = new Mock<IActivationBindingArtifactResolver>();

        // Setup: draft exists with matching version
        var draft = new DraftAbstractions.DescriptorDraft
        {
            TenantId = TestTenantId,
            DraftId = "draft-001",
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            AuthorKind = DraftAbstractions.DescriptorDraftAuthorKind.Agent,
            AuthorId = "agent-001",
            CreatedAt = DateTimeOffset.UtcNow,
            ProposedVersion = "1",
            Payload = new StubDescriptorDraftPayload(),
            Status = DraftAbstractions.DescriptorDraftStatus.Created
        };
        draftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        // Setup: hash builder returns matching hashes
        var boundHashes = CreateValidBindingHashes();
        hashBuilderMock.Setup(h => h.Build(It.IsAny<IDescriptor>()))
            .Returns(new DescriptorStableHashes
            {
                ContractHash = boundHashes.ContractHash,
                DefinitionHash = boundHashes.DefinitionHash
            });

        // Setup: artifact resolver returns matching hashes
        artifactResolverMock.Setup(r => r.ResolveAsync(
                TestTenantId, It.IsAny<ActivationBindingSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedBindingArtifacts
            {
                CurrentSourceReviewHash = boundHashes.SourceReviewHash,
                CurrentReviewManifestHash = boundHashes.ReviewManifestHash,
                CurrentPackageHashes = boundHashes.PackageHashes,
                CurrentEvidenceHashes = boundHashes.PackageHashes
            });

        var rechecker = new DefaultActivationEvidenceRechecker(
            hashBuilderMock.Object,
            draftStoreMock.Object,
            artifactResolverMock.Object,
            new ActivationBindingHashValidator(),
            NullLogger<DefaultActivationEvidenceRechecker>.Instance);

        var snapshot = CreateTestBindingSnapshot();

        // Act
        var result = await rechecker.RecheckAsync(TestTenantId, snapshot);

        // Assert
        result.IsStale.Should().BeFalse();
        result.Drifts.Should().BeEmpty();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. Recheck: invalid hashes → drift detected
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EvidenceRechecker_InvalidHashes_DriftDetected()
    {
        // Arrange
        var hashBuilderMock = new Mock<IDescriptorStableHashBuilder>();
        var draftStoreMock = new Mock<DraftAbstractions.IDescriptorDraftStore>();
        var artifactResolverMock = new Mock<IActivationBindingArtifactResolver>();

        var rechecker = new DefaultActivationEvidenceRechecker(
            hashBuilderMock.Object,
            draftStoreMock.Object,
            artifactResolverMock.Object,
            new ActivationBindingHashValidator(),
            NullLogger<DefaultActivationEvidenceRechecker>.Instance);

        var hashes = CreateValidBindingHashes() with { SourceReviewHash = CreateTestCanonicalHash("", artifactKind: CanonicalHashArtifactNames.ReviewResult, purpose: CanonicalHashPurposeNames.SourceBinding) };
        var snapshot = CreateTestBindingSnapshot(hashes: hashes);

        // Act
        var result = await rechecker.RecheckAsync(TestTenantId, snapshot);

        // Assert — validation catches the empty hash and returns drift before draft/artifact checks
        result.IsStale.Should().BeTrue();
        result.Drifts.Should().NotBeEmpty();
        result.Drifts.Should().Contain(d => d.FieldName == "BindingHash.SourceReviewHash");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. Runtime gate: valid hashes → activates
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RuntimeGate_ValidHashes_ActivatesSuccessfully()
    {
        // Arrange
        var gate = new InMemoryRuntimeActivationGate(
            NullLogger<InMemoryRuntimeActivationGate>.Instance,
            new ActivationBindingHashValidator());

        var request = CreateTestActivationRequest();
        var context = CreateTestInvocationContext();

        // Act
        var result = await gate.ActivateAsync(context, request);

        // Assert
        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().NotBeNull();
        result.Value!.DraftId.Should().Be("draft-001");
        result.Value.TenantId.Should().Be(TestTenantId);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. Runtime gate: invalid hashes → blocked
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RuntimeGate_InvalidHashes_BlocksActivation()
    {
        // Arrange
        var gate = new InMemoryRuntimeActivationGate(
            NullLogger<InMemoryRuntimeActivationGate>.Instance,
            new ActivationBindingHashValidator());

        var hashes = CreateValidBindingHashes() with { SourceReviewHash = CreateTestCanonicalHash("", artifactKind: CanonicalHashArtifactNames.ReviewResult, purpose: CanonicalHashPurposeNames.SourceBinding) };
        var snapshot = CreateTestBindingSnapshot(draftId: "draft-002", hashes: hashes);
        var request = CreateTestActivationRequest("req-002", "draft-002", snapshot);
        var context = CreateTestInvocationContext();

        // Act
        var result = await gate.ActivateAsync(context, request);

        // Assert
        result.Status.Should().Be(AgentToolResultStatus.Failed);
        result.Diagnostics.Should().NotBeEmpty();
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorActivationDiagnosticCodes.BindingHashValidationFailed &&
            d.Severity == SeverityLevel.Error);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 9. Runtime gate: null binding snapshot → does not validate (no-op)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RuntimeGate_NullBindingSnapshot_DoesNotValidate()
    {
        // Arrange
        var gate = new InMemoryRuntimeActivationGate(
            NullLogger<InMemoryRuntimeActivationGate>.Instance,
            new ActivationBindingHashValidator());

        var request = CreateTestActivationRequest("req-003", "draft-003", bindingSnapshot: null!);
        var context = CreateTestInvocationContext();

        // Act
        var result = await gate.ActivateAsync(context, request);

        // Assert — gate proceeds without validation (null snapshot is not validated)
        result.Status.Should().Be(AgentToolResultStatus.Success);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 10. Runtime gate: null hashes in snapshot → does not validate
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RuntimeGate_NullHashes_DoesNotValidate()
    {
        // Arrange
        var gate = new InMemoryRuntimeActivationGate(
            NullLogger<InMemoryRuntimeActivationGate>.Instance,
            new ActivationBindingHashValidator());

        var snapshot = CreateTestBindingSnapshot(draftId: "draft-004") with { Hashes = null! };
        var request = CreateTestActivationRequest("req-004", "draft-004", snapshot);
        var context = CreateTestInvocationContext();

        // Act
        var result = await gate.ActivateAsync(context, request);

        // Assert — gate proceeds; null hashes are already rejected at submission time
        result.Status.Should().Be(AgentToolResultStatus.Success);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 11. All slots empty → error on required hash slots
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateBindingHashes_EmptyPackageManifestHash_ReturnsError()
    {
        // Arrange
        var validator = new ActivationBindingHashValidator();
        var hashes = CreateValidBindingHashes() with { PackageManifestHash = CreateTestCanonicalHash("", artifactKind: CanonicalHashArtifactNames.PackageManifest, purpose: CanonicalHashPurposeNames.Integrity) };

        // Act
        var issues = validator.Validate(hashes);

        // Assert
        issues.Should().Contain(i => i.Slot == "PackageManifestHash" && i.Severity == BindingHashValidationSeverity.Error);
    }
}
