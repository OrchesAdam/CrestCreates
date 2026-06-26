using Xunit;
using Moq;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using FluentAssertions;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Wave 5 tests: Package Preview tools.
/// Verifies: PreviewDescriptorPackage, BuildPackageEvidencePreview,
/// BuildActivationReadinessPreview, GetPackagePreview.
/// Key invariant: Package preview is evidence only, not activation.
/// </summary>
public class Wave5PackagePreviewTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task PreviewDescriptorPackage_Returns_Package_Preview()
    {
        var service = CreateService();
        var context = CreateContext("PreviewDescriptorPackage");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        DraftMaterializerMock.Setup(m => m.Materialize(draft, It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(DraftAbstractions.DescriptorDraftMaterializationResult.Success(new List<IDescriptor>().AsReadOnly()));

        SetupPackageBuilder();

        var result = await service.PreviewDescriptorPackageAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().NotBeNull();
        result.Value!.PackageManifestHash?.Value.Should().NotBeNullOrEmpty();
        result.Value.PackageEvidenceHash?.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PreviewDescriptorPackage_Returns_NotFound_When_Draft_Missing()
    {
        var service = CreateService();
        var context = CreateContext("PreviewDescriptorPackage");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var result = await service.PreviewDescriptorPackageAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task PreviewDescriptorPackage_Returns_Failed_When_Materialization_Fails()
    {
        var service = CreateService();
        var context = CreateContext("PreviewDescriptorPackage");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        DraftMaterializerMock.Setup(m => m.Materialize(draft, It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(DraftAbstractions.DescriptorDraftMaterializationResult.Failure(
                new DraftAbstractions.DescriptorDraftDiagnostic
                {
                    Code = "MATERIALIZATION_FAILED",
                    Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error,
                    Message = "Cannot materialize"
                }));

        var result = await service.PreviewDescriptorPackageAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "MATERIALIZATION_FAILED");
    }

    [Fact]
    public async Task BuildPackageEvidencePreview_Returns_Evidence()
    {
        var service = CreateService();
        var context = CreateContext("BuildPackageEvidencePreview");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true,
            ProposedInventory = new List<IDescriptor>().AsReadOnly()
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(reviewResult));

        SetupPackageBuilder();

        var result = await service.BuildPackageEvidencePreviewAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.DraftId.Should().Be("draft-001");
        result.Value.Evidence.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildActivationReadinessPreview_Returns_Ready_When_No_Blockers()
    {
        var service = CreateService();
        var context = CreateContext("BuildActivationReadinessPreview");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(reviewResult));

        var result = await service.BuildActivationReadinessPreviewAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.IsReady.Should().BeTrue();
        result.Value.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildActivationReadinessPreview_Returns_Blockers_When_Validation_Fails()
    {
        var service = CreateService();
        var context = CreateContext("BuildActivationReadinessPreview");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
                new DraftAbstractions.DescriptorDraftDiagnostic
                {
                    Code = "VALIDATION_ERROR",
                    Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error,
                    Message = "Validation failed"
                }),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(reviewResult));

        var result = await service.BuildActivationReadinessPreviewAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.IsReady.Should().BeFalse();
        result.Value.Blockers.Should().Contain(b => b.Code == "VALIDATION_FAILED");
    }

    [Fact]
    public async Task BuildActivationReadinessPreview_Returns_Blockers_When_Review_Has_Errors()
    {
        var service = CreateService();
        var context = CreateContext("BuildActivationReadinessPreview");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = new List<DraftAbstractions.DescriptorDraftDiagnostic>
            {
                new()
                {
                    Code = "REVIEW_ERROR",
                    Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error,
                    Message = "Review found errors"
                }
            },
            IsActivationEligible = true
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(reviewResult));

        var result = await service.BuildActivationReadinessPreviewAsync(context, "draft-001");

        result.Value!.IsReady.Should().BeFalse();
        result.Value.Blockers.Should().Contain(b => b.Code == "REVIEW_HAS_ERRORS");
    }

    [Fact]
    public async Task BuildActivationReadinessPreview_Returns_Blockers_When_Not_Activation_Eligible()
    {
        var service = CreateService();
        var context = CreateContext("BuildActivationReadinessPreview");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = false
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(reviewResult));

        var result = await service.BuildActivationReadinessPreviewAsync(context, "draft-001");

        result.Value!.IsReady.Should().BeFalse();
        result.Value.Blockers.Should().Contain(b => b.Code == "NOT_ACTIVATION_ELIGIBLE");
    }

    [Fact]
    public async Task GetPackagePreview_Returns_Stored_Preview()
    {
        var service = CreateService();
        var previewContext = CreateContext("PreviewDescriptorPackage");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        DraftMaterializerMock.Setup(m => m.Materialize(draft, It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(DraftAbstractions.DescriptorDraftMaterializationResult.Success(new List<IDescriptor>().AsReadOnly()));

        SetupPackageBuilder();

        // Create preview
        var previewResult = await service.PreviewDescriptorPackageAsync(previewContext, "draft-001");
        previewResult.Status.Should().Be(AgentToolResultStatus.Success);

        // Get the preview ID from audit
        var auditRecord = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "PreviewDescriptorPackage" &&
            r.TouchedPackagePreviewIds != null);
        var previewId = auditRecord.TouchedPackagePreviewIds!.First();

        // Retrieve — use correct tool name for GetPackagePreview
        var getContext = CreateContext("GetPackagePreview");
        var getResult = await service.GetPackagePreviewAsync(getContext, previewId);

        getResult.Status.Should().Be(AgentToolResultStatus.Success);
        getResult.Value!.PackageManifestHash?.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetPackagePreview_Returns_NotFound_When_Missing()
    {
        var service = CreateService();
        var context = CreateContext("GetPackagePreview");

        var result = await service.GetPackagePreviewAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task BuildPackageEvidencePreview_ReusesExistingPackagePreview_PathA()
    {
        // Arrange — create package preview first, then evidence should reuse the package
        var service = CreateService();
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        DraftMaterializerMock.Setup(m => m.Materialize(draft, It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(DraftAbstractions.DescriptorDraftMaterializationResult.Success(new List<IDescriptor>().AsReadOnly()));

        SetupPackageBuilderWithHashes();

        // Step 1: PreviewDescriptorPackageAsync — creates package preview, stores package hashes
        var previewContext = CreateContext("PreviewDescriptorPackage");
        var previewResult = await service.PreviewDescriptorPackageAsync(previewContext, "draft-001");
        previewResult.Status.Should().Be(AgentToolResultStatus.Success);

        // Step 2: BuildPackageEvidencePreviewAsync — should detect existing package and reuse (Path A)
        var evidenceContext = CreateContext("BuildPackageEvidencePreview");
        var evidenceResult = await service.BuildPackageEvidencePreviewAsync(evidenceContext, "draft-001");

        // Assert
        evidenceResult.Status.Should().Be(AgentToolResultStatus.Success);
        evidenceResult.Value.Should().NotBeNull();
        evidenceResult.Value!.DraftId.Should().Be("draft-001");
        evidenceResult.Value.Evidence.Should().NotBeNull();

        // Diagnostics should be empty (no new review was run in Path A)
        evidenceResult.Value.Diagnostics.Should().BeEmpty();

        // Package builder should have been called exactly ONCE (during Preview, not again during Evidence)
        PackageBuilderMock.Verify(
            b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()), Times.Once);

        // Evidence hashes should match package hashes (same DescriptorPackageHashSet)
        var previewAudit = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "PreviewDescriptorPackage" &&
            r.TouchedPackagePreviewIds != null);
        var packagePreviewId = previewAudit.TouchedPackagePreviewIds!.First();

        var evidenceAudit = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "BuildPackageEvidencePreview" &&
            r.TouchedPackagePreviewIds != null);
        var evidencePreviewId = evidenceAudit.TouchedPackagePreviewIds!.First();

        var packageHashSet = InMemoryArtifactResolver.GetPackageHashSet(TestTenantId, packagePreviewId);
        var evidenceHashSet = InMemoryArtifactResolver.GetEvidenceHashSet(TestTenantId, evidencePreviewId);

        packageHashSet.Should().NotBeNull();
        evidenceHashSet.Should().NotBeNull();
        evidenceHashSet.Should().BeEquivalentTo(packageHashSet);
    }

    [Fact]
    public async Task BuildPackageEvidencePreview_CreatesBothPreviews_PathB()
    {
        // Arrange — no existing package preview, should create both package + evidence in one go
        var service = CreateService();
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true,
            ProposedInventory = new List<IDescriptor>().AsReadOnly()
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(reviewResult));

        SetupPackageBuilderWithHashes();

        // Act — BuildPackageEvidencePreviewAsync without prior PreviewDescriptorPackageAsync
        var evidenceContext = CreateContext("BuildPackageEvidencePreview");
        var evidenceResult = await service.BuildPackageEvidencePreviewAsync(evidenceContext, "draft-001");

        // Assert — success
        evidenceResult.Status.Should().Be(AgentToolResultStatus.Success);
        evidenceResult.Value.Should().NotBeNull();

        // Review and package builder should each have been called exactly once
        DraftReviewServiceMock.Verify(
            r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        PackageBuilderMock.Verify(
            b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()), Times.Once);

        // Audit should contain both package preview ID and evidence preview ID
        var evidenceAudit = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "BuildPackageEvidencePreview" &&
            r.TouchedPackagePreviewIds != null &&
            r.TouchedPackagePreviewIds.Count >= 2);
        var packagePreviewId = evidenceAudit.TouchedPackagePreviewIds![0];
        var evidencePreviewId = evidenceAudit.TouchedPackagePreviewIds![1];

        // Both package and evidence hash sets should be stored
        InMemoryArtifactResolver.PackageHashSetCount.Should().Be(1);
        InMemoryArtifactResolver.EvidenceHashSetCount.Should().Be(1);

        var packageHashSet = InMemoryArtifactResolver.GetPackageHashSet(TestTenantId, packagePreviewId);
        var evidenceHashSet = InMemoryArtifactResolver.GetEvidenceHashSet(TestTenantId, evidencePreviewId);
        packageHashSet.Should().NotBeNull();
        evidenceHashSet.Should().NotBeNull();
        evidenceHashSet.Should().BeEquivalentTo(packageHashSet);

        // _latestPackageByDraft must have been updated: calling PreviewDescriptorPackageAsync
        // is NOT needed — BuildPackageEvidencePreviewAsync itself creates the package preview.
        // Prove by fetching the package preview through GetPackagePreviewAsync.
        var getContext = CreateContext("GetPackagePreview");
        var getResult = await service.GetPackagePreviewAsync(getContext, packagePreviewId);
        getResult.Status.Should().Be(AgentToolResultStatus.Success);

        // Calling BuildPackageEvidencePreviewAsync AGAIN should go Path A (reuse),
        // proving _latestPackageByDraft was set in the first call.
        var evidenceResult2 = await service.BuildPackageEvidencePreviewAsync(evidenceContext, "draft-001");
        evidenceResult2.Status.Should().Be(AgentToolResultStatus.Success);
        // No additional Build call — still exactly once
        PackageBuilderMock.Verify(
            b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()), Times.Once);
    }

    private void SetupPackageBuilderWithHashes()
    {
        var hashes = new DescriptorPackageHashSet
        {
            PackageManifestHash = new CanonicalHash
            {
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = CanonicalHashArtifactNames.Package,
                Scope = CanonicalHashScopeNames.InternalFull,
                Purpose = CanonicalHashPurposeNames.Integrity,
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "test-manifest-hash-v1",
                Value = "test-manifest-hash-value"
            },
            PackageEvidenceHash = new CanonicalHash
            {
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = CanonicalHashArtifactNames.Package,
                Scope = CanonicalHashScopeNames.InternalFull,
                Purpose = CanonicalHashPurposeNames.Integrity,
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "test-evidence-hash-v1",
                Value = "test-evidence-hash-value"
            },
            PackageEvidenceEnvelopeHash = new CanonicalHash
            {
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = CanonicalHashArtifactNames.Package,
                Scope = CanonicalHashScopeNames.InternalFull,
                Purpose = CanonicalHashPurposeNames.Integrity,
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "test-envelope-hash-v1",
                Value = "test-envelope-hash-value"
            }
        };

        PackageBuilderMock.Setup(b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()))
            .Returns(new DescriptorPackage
            {
                Manifest = new DescriptorManifest
                {
                    PackageId = "pkg-001",
                    PackageVersion = "1",
                    DescriptorEntries = Array.Empty<DescriptorManifestEntry>()
                },
                Snapshot = new DescriptorSnapshot(),
                Evidence = new DescriptorPackageEvidence(),
                Hashes = hashes
            });
    }

    [Fact]
    public void ComputeFingerprint_DifferentOptions_ReturnsDifferentFingerprints()
    {
        var devOptions = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var prodOptions = AgentToolAuthorizationOptions.ProductionDefaults;

        var devFp = AgentDescriptorVisibilityScope.ComputeFingerprint(devOptions);
        var prodFp = AgentDescriptorVisibilityScope.ComputeFingerprint(prodOptions);

        devFp.Should().NotBe(prodFp);
    }

    [Fact]
    public void ComputeFingerprint_SameOptions_ReturnsSameFingerprint()
    {
        var options1 = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var options2 = AgentToolAuthorizationOptions.DevelopmentDefaults;

        var fp1 = AgentDescriptorVisibilityScope.ComputeFingerprint(options1);
        var fp2 = AgentDescriptorVisibilityScope.ComputeFingerprint(options2);

        fp1.Should().Be(fp2);
    }

    [Fact]
    public void ComputeFingerprint_DifferentDeniedKinds_ReturnsDifferentFingerprints()
    {
        var options1 = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            DeniedDescriptorKinds = new HashSet<string> { "Schema" }
        };
        var options2 = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            DeniedDescriptorKinds = new HashSet<string> { "Capability" }
        };

        var fp1 = AgentDescriptorVisibilityScope.ComputeFingerprint(options1);
        var fp2 = AgentDescriptorVisibilityScope.ComputeFingerprint(options2);

        fp1.Should().NotBe(fp2);
    }
}
