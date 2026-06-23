using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft.Tests;

public class DefaultDescriptorDraftReviewServiceTests
{
    private readonly Mock<IDescriptorDraftValidator> _validatorMock = new();
    private readonly Mock<IDescriptorDraftMaterializer> _materializerMock = new();
    private readonly Mock<IDescriptorRelationshipProvider> _relationshipProviderMock = new();
    private readonly Mock<IDescriptorTopologyBuilder> _topologyBuilderMock = new();
    private readonly Mock<IDescriptorImpactAnalyzer> _impactAnalyzerMock = new();
    private readonly Mock<IDescriptorChangeSetBuilder> _changeSetBuilderMock = new();
    private readonly Mock<IDescriptorCompatibilityAnalyzer> _compatibilityAnalyzerMock = new();
    private readonly Mock<IDescriptorLifecycleGovernanceService> _lifecycleGovernanceMock = new();
    private readonly Mock<IDescriptorStableHashBuilder> _stableHashBuilderMock = new();
    private readonly Mock<IDescriptorPackageBuilder> _packageBuilderMock = new();
    private readonly Mock<ILogger<DefaultDescriptorDraftReviewService>> _loggerMock = new();

    private DefaultDescriptorDraftReviewService CreateService() =>
        new(
            _validatorMock.Object,
            _materializerMock.Object,
            _relationshipProviderMock.Object,
            _topologyBuilderMock.Object,
            _impactAnalyzerMock.Object,
            _changeSetBuilderMock.Object,
            _compatibilityAnalyzerMock.Object,
            _lifecycleGovernanceMock.Object,
            _stableHashBuilderMock.Object,
            _packageBuilderMock.Object,
            _loggerMock.Object);

    private static IReadOnlyList<IDescriptor> EmptyInventory => Array.Empty<IDescriptor>();

    private static Draft CreateValidCreateDraft(string draftId = "d1", string descriptorId = "schema1")
    {
        var descriptor = new SchemaDescriptor
        {
            Id = descriptorId,
            Name = "Test",
            Version = 1,
            State = DescriptorState.Active
        };
        return new Draft
        {
            TenantId = "t1",
            DraftId = draftId,
            DescriptorKind = DescriptorKind.Schema,
            DescriptorId = descriptorId,
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human,
            AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new SchemaDescriptorDraftPayload(descriptor),
            ProposedVersion = "1"
        };
    }

    // ── Test 1: Early stop on validation error ──

    [Fact]
    public void Stops_Early_On_Validation_Error()
    {
        var draft = new Draft
        {
            TenantId = "t1",
            DraftId = "",
            DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "",
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human,
            AuthorId = "",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "", Name = "" })
        };

        var failedValidation = DescriptorDraftValidationResult.Failure(
            new DescriptorDraftDiagnostic
            {
                Code = "DRAFT_ID_EMPTY",
                Severity = DescriptorDraftDiagnosticSeverity.Error,
                Message = "DraftId must not be empty."
            });

        _validatorMock
            .Setup(v => v.Validate(draft))
            .Returns(failedValidation);

        var service = CreateService();
        var result = service.ReviewAsync(draft, EmptyInventory).Result;

        result.IsActivationEligible.Should().BeFalse();
        result.MaterializationResult.Should().BeNull();
        result.Diagnostics.Should().Contain(d => d.Code == "DRAFT_ID_EMPTY");

        // Phase 6 services should never be called
        _topologyBuilderMock.Verify(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>()), Times.Never);
        _lifecycleGovernanceMock.Verify(g => g.Evaluate(It.IsAny<DescriptorLifecycleGovernanceRequest>()), Times.Never);
    }

    // ── Test 2: Early stop on materialization error ──

    [Fact]
    public void Stops_Early_On_Materialization_Error()
    {
        var draft = CreateValidCreateDraft();
        var existingDescriptor = new SchemaDescriptor
        {
            Id = "schema1",
            Name = "Existing",
            Version = 1,
            State = DescriptorState.Active
        };
        var currentInventory = new List<IDescriptor> { existingDescriptor };

        _validatorMock
            .Setup(v => v.Validate(draft))
            .Returns(DescriptorDraftValidationResult.Success());

        _materializerMock
            .Setup(m => m.Materialize(draft, currentInventory))
            .Returns(DescriptorDraftMaterializationResult.Failure(
                new DescriptorDraftDiagnostic
                {
                    Code = "CREATE_DESCRIPTOR_EXISTS",
                    Severity = DescriptorDraftDiagnosticSeverity.Error,
                    Message = "Descriptor already exists.",
                    DraftId = draft.DraftId
                }));

        var service = CreateService();
        var result = service.ReviewAsync(draft, currentInventory).Result;

        result.IsActivationEligible.Should().BeFalse();
        result.MaterializationResult.Should().NotBeNull();
        result.MaterializationResult!.IsMaterialized.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "CREATE_DESCRIPTOR_EXISTS");

        // Phase 6 services must NOT be called
        _topologyBuilderMock.Verify(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>()), Times.Never);
        _changeSetBuilderMock.Verify(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<IReadOnlyList<IDescriptor>>()), Times.Never);
        _impactAnalyzerMock.Verify(a => a.Analyze(It.IsAny<DescriptorTopologySnapshot>(), It.IsAny<DescriptorChangeSet>(), It.IsAny<DescriptorImpactAnalysisOptions?>()), Times.Never);
        _compatibilityAnalyzerMock.Verify(a => a.Analyze(It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<DescriptorChangeSet>(), It.IsAny<DescriptorImpactAnalysisReport>(), It.IsAny<DescriptorCompatibilityAnalysisOptions?>()), Times.Never);
        _lifecycleGovernanceMock.Verify(g => g.Evaluate(It.IsAny<DescriptorLifecycleGovernanceRequest>()), Times.Never);
    }

    // ── Test 3: Full pipeline for valid draft ──

    [Fact]
    public void Invokes_Control_Plane_For_Valid_Draft()
    {
        var draft = CreateValidCreateDraft();
        var materializedInventory = new List<IDescriptor>
        {
            new SchemaDescriptor
            {
                Id = "schema1",
                Name = "Test",
                Version = 1,
                State = DescriptorState.Active
            }
        };

        _validatorMock
            .Setup(v => v.Validate(draft))
            .Returns(DescriptorDraftValidationResult.Success());

        _materializerMock
            .Setup(m => m.Materialize(draft, EmptyInventory))
            .Returns(DescriptorDraftMaterializationResult.Success(materializedInventory));

        // Mock governance to return Allowed
        var governanceReport = new DescriptorLifecycleGovernanceReport
        {
            Decisions = new[]
            {
                new DescriptorLifecycleDecision
                {
                    Transition = new DescriptorLifecycleTransition
                    {
                        Subject = new DescriptorRef("schema", "schema1"),
                        Operation = DescriptorLifecycleOperation.Activate
                    },
                    Decision = DescriptorLifecycleDecisionKind.Allowed,
                    Findings = Array.Empty<DescriptorLifecycleFinding>()
                }
            },
            MaxDecision = DescriptorLifecycleDecisionKind.Allowed,
            PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
        };

        _lifecycleGovernanceMock
            .Setup(g => g.Evaluate(It.IsAny<DescriptorLifecycleGovernanceRequest>()))
            .Returns(governanceReport);

        // Stable hash builder returns success
        _stableHashBuilderMock
            .Setup(h => h.Build(It.IsAny<IDescriptor>()))
            .Returns(new DescriptorStableHashes
            {
                ContractHash = new CanonicalHash
                {
                    Value = "chash",
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "sha256-canonical-json-v1",
                    ArtifactKind = "Descriptor",
                    Scope = "InternalFull",
                    Purpose = "Contract",
                    ContractVersion = "canonical-hash-v1",
                    CanonicalShapeVersion = "schema-contract-hash-v1"
                },
                DefinitionHash = new CanonicalHash
                {
                    Value = "dhash",
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "sha256-canonical-json-v1",
                    ArtifactKind = "Descriptor",
                    Scope = "InternalFull",
                    Purpose = "Definition",
                    ContractVersion = "canonical-hash-v1",
                    CanonicalShapeVersion = "schema-definition-hash-v1"
                }
            });

        var service = CreateService();
        var result = service.ReviewAsync(draft, EmptyInventory).Result;

        // Validation and materialization should have passed
        result.ValidationResult.IsValid.Should().BeTrue();
        result.MaterializationResult!.IsMaterialized.Should().BeTrue();
        result.ProposedInventory.Should().NotBeNull();

        // Phase 6 topology builder must have been called
        _topologyBuilderMock.Verify(
            b => b.Build(It.Is<IReadOnlyList<IDescriptor>>(inv => inv.Count == 1)),
            Times.Once);

        // Governance must have been called
        _lifecycleGovernanceMock.Verify(
            g => g.Evaluate(It.IsAny<DescriptorLifecycleGovernanceRequest>()),
            Times.Once);

        // IsActivationEligible reflects governance decision (Allowed = true)
        result.IsActivationEligible.Should().BeTrue();
        result.GovernanceDecision!.IsAllowed.Should().BeTrue();

        // Stable hashes populated
        result.StableHashes.Should().NotBeNull();
    }

    [Fact]
    public void Package_Preview_Does_Not_Fabricate_SnapshotHash()
    {
        var draft = CreateValidCreateDraft();
        var materializedInventory = new List<IDescriptor>
        {
            new SchemaDescriptor
            {
                Id = "schema1",
                Name = "Test",
                Version = 1,
                State = DescriptorState.Active
            }
        };

        _validatorMock
            .Setup(v => v.Validate(draft))
            .Returns(DescriptorDraftValidationResult.Success());

        _materializerMock
            .Setup(m => m.Materialize(draft, EmptyInventory))
            .Returns(DescriptorDraftMaterializationResult.Success(materializedInventory));

        _packageBuilderMock
            .Setup(b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()))
            .Returns(new DescriptorPackage
            {
                Manifest = new DescriptorManifest
                {
                    PackageId = draft.DraftId,
                    PackageVersion = "1",
                    ContentHash = "manifest-hash",
                    EvidenceHash = "evidence-hash",
                    EnvelopeHash = "envelope-hash",
                    DescriptorEntries = new[]
                    {
                        new DescriptorManifestEntry
                        {
                            Ref = new DescriptorRef("schema", "schema1", 1),
                            Kind = DescriptorKind.Schema,
                            Name = "Test",
                            State = DescriptorState.Active,
                            ContractHash = "abc",
                            DefinitionHash = "def"
                        }
                    }
                }
            });

        var service = CreateService();
        var result = service.ReviewAsync(draft, EmptyInventory).Result;

        result.PackagePreview.Should().NotBeNull();
        result.PackagePreview!.SnapshotHash.Should().BeNull();
    }
}
