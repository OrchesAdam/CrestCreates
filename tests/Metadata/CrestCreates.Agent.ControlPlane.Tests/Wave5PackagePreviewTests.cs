using Xunit;
using Moq;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
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
        result.Value!.ManifestHash.Should().NotBeNullOrEmpty();
        result.Value.EvidenceHash.Should().NotBeNullOrEmpty();
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
        var context = CreateContext("PreviewDescriptorPackage");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        DraftMaterializerMock.Setup(m => m.Materialize(draft, It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(DraftAbstractions.DescriptorDraftMaterializationResult.Success(new List<IDescriptor>().AsReadOnly()));

        SetupPackageBuilder();

        // Create preview
        var previewResult = await service.PreviewDescriptorPackageAsync(context, "draft-001");
        previewResult.Status.Should().Be(AgentToolResultStatus.Success);

        // Get the preview ID from audit
        var auditRecord = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "PreviewDescriptorPackage" &&
            r.TouchedPackagePreviewIds != null);
        var previewId = auditRecord.TouchedPackagePreviewIds!.First();

        // Retrieve
        var getResult = await service.GetPackagePreviewAsync(context, previewId);

        getResult.Status.Should().Be(AgentToolResultStatus.Success);
        getResult.Value!.ManifestHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetPackagePreview_Returns_NotFound_When_Missing()
    {
        var service = CreateService();
        var context = CreateContext("GetPackagePreview");

        var result = await service.GetPackagePreviewAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }
}
