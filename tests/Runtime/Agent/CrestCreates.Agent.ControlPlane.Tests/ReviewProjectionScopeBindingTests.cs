using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

public sealed class ReviewProjectionScopeBindingTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task NarrowedScope_DeniesGet_OmitsList_RejectsReport_AndRereviewRecovers()
    {
        var currentOptions = BroadScope();
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        ConfigureReviewInputs(draft);
        var service = CreateServiceWithOptionsFactory(() => currentOptions);
        var originalReviewId = await CreateReviewAsync(service, draft);

        var broadRead = await service.GetDraftReviewResultAsync(
            CreateContext(AgentToolName.GetDraftReviewResult), originalReviewId);
        broadRead.Status.Should().Be(AgentToolResultStatus.Success);
        broadRead.Value!.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.DescriptorKind == DescriptorKind.Capability);

        currentOptions = NarrowScope();
        var narrowedRead = await service.GetDraftReviewResultAsync(
            CreateContext(AgentToolName.GetDraftReviewResult), originalReviewId);
        narrowedRead.Status.Should().Be(AgentToolResultStatus.Denied);
        narrowedRead.Value.Should().BeNull();
        narrowedRead.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AgentToolDiagnosticCodes.ReviewResultScopeMismatch);

        var list = await service.ListDraftReviewResultsAsync(
            CreateContext(AgentToolName.ListDraftReviewResults), draft.DraftId);
        list.Status.Should().Be(AgentToolResultStatus.Success);
        list.Value!.Results.Should().BeEmpty();
        list.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AgentToolDiagnosticCodes.ResultsSecurityTrimmed);

        ReportBuilderMock.Setup(builder => builder.Build(It.IsAny<DescriptorReviewReportBuildRequest>()))
            .Returns(CreateReport(draft.DraftId));
        var blockedReport = await service.BuildDescriptorReviewReportAsync(
            CreateContext(AgentToolName.BuildDescriptorReviewReport), draft.DraftId);
        blockedReport.Status.Should().Be(AgentToolResultStatus.Failed);
        blockedReport.Value.Should().BeNull();
        blockedReport.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AgentToolDiagnosticCodes.ReviewResultScopeMismatch);
        ReportBuilderMock.Verify(builder => builder.Build(It.IsAny<DescriptorReviewReportBuildRequest>()), Times.Never);

        var newReviewId = await CreateReviewAsync(service, draft);
        newReviewId.Should().NotBe(originalReviewId);
        var recoveredRead = await service.GetDraftReviewResultAsync(
            CreateContext(AgentToolName.GetDraftReviewResult), newReviewId);
        recoveredRead.Status.Should().Be(AgentToolResultStatus.Success);
        recoveredRead.Value!.Diagnostics.Should().BeEmpty();

        var recoveredList = await service.ListDraftReviewResultsAsync(
            CreateContext(AgentToolName.ListDraftReviewResults), draft.DraftId);
        recoveredList.Value!.Results.Should().ContainSingle();
        recoveredList.Value.Results[0].Diagnostics.Should().BeEmpty();
        recoveredList.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AgentToolDiagnosticCodes.ResultsSecurityTrimmed);

        var recoveredReport = await service.BuildDescriptorReviewReportAsync(
            CreateContext(AgentToolName.BuildDescriptorReviewReport), draft.DraftId);
        recoveredReport.Status.Should().Be(AgentToolResultStatus.Success);
        ReportBuilderMock.Verify(builder => builder.Build(It.IsAny<DescriptorReviewReportBuildRequest>()), Times.Once);
    }

    [Fact]
    public async Task ScopeExpansion_RequiresRereview_AndReportDoesNotFallBackToOlderMatchingReview()
    {
        var currentOptions = NarrowScope();
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        ConfigureReviewInputs(draft);
        var service = CreateServiceWithOptionsFactory(() => currentOptions);
        var narrowReviewId = await CreateReviewAsync(service, draft);

        currentOptions = BroadScope();
        var expandedRead = await service.GetDraftReviewResultAsync(
            CreateContext(AgentToolName.GetDraftReviewResult), narrowReviewId);
        expandedRead.Status.Should().Be(AgentToolResultStatus.Denied);
        expandedRead.Value.Should().BeNull();

        var expandedList = await service.ListDraftReviewResultsAsync(
            CreateContext(AgentToolName.ListDraftReviewResults), draft.DraftId);
        expandedList.Value!.Results.Should().BeEmpty();
        expandedList.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AgentToolDiagnosticCodes.ResultsSecurityTrimmed);

        ReportBuilderMock.Setup(builder => builder.Build(It.IsAny<DescriptorReviewReportBuildRequest>()))
            .Returns(CreateReport(draft.DraftId));
        var expandedReport = await service.BuildDescriptorReviewReportAsync(
            CreateContext(AgentToolName.BuildDescriptorReviewReport), draft.DraftId);
        expandedReport.Status.Should().Be(AgentToolResultStatus.Failed);
        ReportBuilderMock.Verify(builder => builder.Build(It.IsAny<DescriptorReviewReportBuildRequest>()), Times.Never);

        var broadReviewId = await CreateReviewAsync(service, draft);
        var broadRead = await service.GetDraftReviewResultAsync(
            CreateContext(AgentToolName.GetDraftReviewResult), broadReviewId);
        broadRead.Status.Should().Be(AgentToolResultStatus.Success);
        broadRead.Value!.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.DescriptorKind == DescriptorKind.Capability);

        // Returning to the older scope makes the older review readable, but the
        // report still must reject the latest review instead of falling back.
        currentOptions = NarrowScope();
        var oldMatchingRead = await service.GetDraftReviewResultAsync(
            CreateContext(AgentToolName.GetDraftReviewResult), narrowReviewId);
        oldMatchingRead.Status.Should().Be(AgentToolResultStatus.Success);
        var latestMismatchReport = await service.BuildDescriptorReviewReportAsync(
            CreateContext(AgentToolName.BuildDescriptorReviewReport), draft.DraftId);
        latestMismatchReport.Status.Should().Be(AgentToolResultStatus.Failed);
        latestMismatchReport.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AgentToolDiagnosticCodes.ReviewResultScopeMismatch);
        ReportBuilderMock.Verify(builder => builder.Build(It.IsAny<DescriptorReviewReportBuildRequest>()), Times.Never);
    }

    [Fact]
    public async Task SameScope_ReadsAndBuildsReportWithoutTrimming()
    {
        var options = BroadScope();
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        ConfigureReviewInputs(draft);
        var service = CreateServiceWithOptionsFactory(() => options);
        var reviewId = await CreateReviewAsync(service, draft);

        ReportBuilderMock.Setup(builder => builder.Build(It.IsAny<DescriptorReviewReportBuildRequest>()))
            .Returns(CreateReport(draft.DraftId));

        var read = await service.GetDraftReviewResultAsync(
            CreateContext(AgentToolName.GetDraftReviewResult), reviewId);
        read.Status.Should().Be(AgentToolResultStatus.Success);
        read.Value!.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.DescriptorKind == DescriptorKind.Capability);

        var list = await service.ListDraftReviewResultsAsync(
            CreateContext(AgentToolName.ListDraftReviewResults), draft.DraftId);
        list.Status.Should().Be(AgentToolResultStatus.Success);
        list.Value!.Results.Should().ContainSingle();
        list.Diagnostics.Should().BeEmpty();

        var report = await service.BuildDescriptorReviewReportAsync(
            CreateContext(AgentToolName.BuildDescriptorReviewReport), draft.DraftId);
        report.Status.Should().Be(AgentToolResultStatus.Success);
        ReportBuilderMock.Verify(builder => builder.Build(It.IsAny<DescriptorReviewReportBuildRequest>()), Times.Once);
    }

    [Fact]
    public async Task GetPackagePreview_DeniesPreviewCapturedUnderAnotherScope()
    {
        var currentOptions = BroadScope();
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        DraftStoreMock.Setup(store => store.GetAsync(TestTenantId, draft.DraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        DraftMaterializerMock.Setup(materializer => materializer.Materialize(
                draft, It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(DraftAbstractions.DescriptorDraftMaterializationResult.Success([]));
        DescriptorCatalogMock.Setup(catalog => catalog.GetAll())
            .Returns([CreateTestDescriptor(kind: DescriptorKind.Event), CreateTestDescriptor(id: "cap-1", kind: DescriptorKind.Capability)]);
        SetupPackageBuilder();
        var service = CreateServiceWithOptionsFactory(() => currentOptions);

        var preview = await service.PreviewDescriptorPackageAsync(
            CreateContext(AgentToolName.PreviewDescriptorPackage), draft.DraftId);
        preview.Status.Should().Be(AgentToolResultStatus.Success);
        var previewId = preview.AuditRecord!.TouchedPackagePreviewIds!.Single();

        currentOptions = NarrowScope();
        var read = await service.GetPackagePreviewAsync(
            CreateContext(AgentToolName.GetPackagePreview), previewId);
        read.Status.Should().Be(AgentToolResultStatus.Denied);
        read.Value.Should().BeNull();
        read.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == AgentToolDiagnosticCodes.PackagePreviewScopeMismatch);
    }

    [Fact]
    public async Task SubmitActivationRequest_RejectsReviewAndPackageFromAnotherScopeBeforeServiceCall()
    {
        var currentOptions = BroadScope();
        ConfigureReviewHashes();
        var (service, broadReviewId, broadPackageId, _) =
            await CreateServiceWithFullBindingArtifacts(optionsFactory: () => currentOptions);

        DraftReviewServiceMock.Setup(reviewService => reviewService.ReviewAsync(
                It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Draft requestedDraft, IReadOnlyList<IDescriptor> _, CancellationToken __) =>
                new DescriptorDraftReviewResult
                {
                    DraftId = requestedDraft.DraftId,
                    TenantId = requestedDraft.TenantId,
                    ValidationResult = DescriptorDraftValidationResult.Success(),
                    Diagnostics = [],
                    IsActivationEligible = true,
                    ProposedInventory = []
                });

        currentOptions = NarrowScope();
        var narrowReviewId = await CreateReviewAsync(service, CreateTestDraft());
        DescriptorCatalogMock.Setup(catalog => catalog.GetAll()).Returns([]);
        SetupPackageBuilder();
        var narrowEvidence = await service.BuildPackageEvidencePreviewAsync(
            CreateContext(AgentToolName.BuildPackageEvidencePreview), "draft-001");
        narrowEvidence.Status.Should().Be(AgentToolResultStatus.Success);
        var narrowPackageId = narrowEvidence.AuditRecord!.TouchedPackagePreviewIds![0];
        var narrowEvidenceId = narrowEvidence.AuditRecord.TouchedPackagePreviewIds[1];

        var reviewMismatch = await service.SubmitActivationRequestAsync(
            CreateContext(AgentToolName.SubmitActivationRequest),
            CreateActivationRequest("draft-001", broadReviewId, narrowPackageId, narrowEvidenceId));
        reviewMismatch.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        reviewMismatch.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == DescriptorActivationDiagnosticCodes.ReviewResultScopeMismatch);
        reviewMismatch.Diagnostics.Should().NotContain(diagnostic =>
            diagnostic.Code == DescriptorActivationDiagnosticCodes.PackagePreviewScopeMismatch);
        ActivationRequestServiceMock.Verify(requestService => requestService.CreateActivationRequestAsync(
            It.IsAny<AgentToolInvocationContext>(), It.IsAny<SubmitActivationRequestRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // The fresh review matches the active narrow scope, so this request
        // isolates the stale package reference guard.
        var packageMismatch = await service.SubmitActivationRequestAsync(
            CreateContext(AgentToolName.SubmitActivationRequest),
            CreateActivationRequest("draft-001", narrowReviewId, broadPackageId, narrowEvidenceId));
        packageMismatch.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        packageMismatch.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == DescriptorActivationDiagnosticCodes.PackagePreviewScopeMismatch);
        packageMismatch.Diagnostics.Should().NotContain(diagnostic =>
            diagnostic.Code == DescriptorActivationDiagnosticCodes.ReviewResultScopeMismatch);
        ActivationRequestServiceMock.Verify(requestService => requestService.CreateActivationRequestAsync(
            It.IsAny<AgentToolInvocationContext>(), It.IsAny<SubmitActivationRequestRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private void ConfigureReviewInputs(Draft draft)
    {
        var visibleOwner = CreateTestDescriptor(id: "test.desc-001", kind: DescriptorKind.Event);
        var hiddenCapability = CreateTestDescriptor(id: "cap-1", kind: DescriptorKind.Capability);
        DraftStoreMock.Setup(store => store.GetAsync(TestTenantId, draft.DraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        DraftStoreMock.Setup(store => store.ListAsync(TestTenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([draft]);
        DescriptorCatalogMock.Setup(catalog => catalog.GetAll()).Returns([visibleOwner, hiddenCapability]);
        DraftReviewServiceMock.Setup(reviewService => reviewService.ReviewAsync(
                draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateReview(draft, visibleOwner, hiddenCapability));

        ConfigureReviewHashes();
    }

    private void ConfigureReviewHashes()
    {
        var reviewHash = CreateHash("review-hash");
        ReviewHashServiceMock.Setup(hashService => hashService.ComputeSourceReviewHash(It.IsAny<DescriptorDraftReviewResult>()))
            .Returns(reviewHash);
        ReviewHashServiceMock.Setup(hashService => hashService.ComputeReviewManifestHash(It.IsAny<DescriptorDraftReviewResult>()))
            .Returns(reviewHash with { Value = "manifest-hash" });
    }

    private async Task<string> CreateReviewAsync(DefaultAgentControlPlaneToolService service, Draft draft)
    {
        var result = await service.ReviewDescriptorDraftAsync(
            CreateContext(AgentToolName.ReviewDescriptorDraft), draft.DraftId);
        result.Status.Should().Be(AgentToolResultStatus.Success);
        return result.AuditRecord!.TouchedReviewResultIds!.Single();
    }

    private static DescriptorDraftReviewResult CreateReview(
        Draft draft, IDescriptor visibleOwner, IDescriptor hiddenCapability) => new()
    {
        DraftId = draft.DraftId,
        TenantId = draft.TenantId,
        ValidationResult = DescriptorDraftValidationResult.Success(),
        ProposedInventory = [visibleOwner, hiddenCapability],
        Diagnostics =
        [
            new DescriptorDraftDiagnostic
            {
                Code = new("CAPABILITY_REVIEW_FACT"),
                Severity = SeverityLevel.Warning,
                Message = "capability review detail",
                DescriptorKind = DescriptorKind.Capability,
                DescriptorId = hiddenCapability.Id
            }
        ],
        IsActivationEligible = true
    };

    private static AgentToolAuthorizationOptions BroadScope()
        => AgentToolAuthorizationOptions.DevelopmentDefaults;

    private static AgentToolAuthorizationOptions NarrowScope()
        => AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = [nameof(DescriptorKind.Capability)]
        };

    private static DescriptorReviewReportDto CreateReport(string draftId)
    {
        static DescriptorReviewReportSectionDto Section(DescriptorReviewReportSectionKind kind, int order)
            => new()
            {
                Kind = kind,
                SectionId = $"section-{kind}",
                Title = kind.ToString(),
                Order = order,
                IsEmpty = true,
                OverallSeverity = SeverityLevel.Info,
                Items = []
            };

        var sections = Enum.GetValues<DescriptorReviewReportSectionKind>()
            .Select((kind, index) => Section(kind, index + 1))
            .ToDictionary(section => section.Kind);
        return new DescriptorReviewReportDto
        {
            ReportId = "report-1",
            DraftId = draftId,
            TenantId = TestTenantId,
            ReviewResultId = "review-1",
            DraftVersion = "1",
            SourceReviewHash = "hash",
            TemplateVersion = "v1",
            GeneratedAt = DateTimeOffset.UtcNow,
            ContractVersion = AgentControlPlaneContractVersion.Current,
            Recommendations = [],
            SummarySection = sections[DescriptorReviewReportSectionKind.Summary],
            DraftIdentitySection = sections[DescriptorReviewReportSectionKind.DraftIdentity],
            ProposedChangesSection = sections[DescriptorReviewReportSectionKind.ProposedChanges],
            ImpactAnalysisSection = sections[DescriptorReviewReportSectionKind.ImpactAnalysis],
            DependencySummarySection = sections[DescriptorReviewReportSectionKind.DependencySummary],
            CompatibilitySection = sections[DescriptorReviewReportSectionKind.Compatibility],
            GovernanceSection = sections[DescriptorReviewReportSectionKind.Governance],
            RequiredHumanReviewSection = sections[DescriptorReviewReportSectionKind.RequiredHumanReview],
            ActivationEligibilitySection = sections[DescriptorReviewReportSectionKind.ActivationEligibility],
            DiagnosticsSection = sections[DescriptorReviewReportSectionKind.Diagnostics],
            RecommendationsSection = sections[DescriptorReviewReportSectionKind.Recommendations],
            PackagePreviewSection = sections[DescriptorReviewReportSectionKind.PackagePreview],
            StableHashesSection = sections[DescriptorReviewReportSectionKind.StableHashes]
        };
    }

    private static SubmitActivationRequestRequest CreateActivationRequest(
        string draftId, string reviewId, string packageId, string evidenceId)
    {
        var hash = CreateHash("binding-hash");
        return new SubmitActivationRequestRequest
        {
            DraftId = draftId,
            BindingSnapshot = new ActivationBindingSnapshot
            {
                TenantId = TestTenantId,
                DraftId = draftId,
                DraftVersion = 1,
                ReviewResultId = reviewId,
                PackagePreviewId = packageId,
                EvidencePreviewId = evidenceId,
                Hashes = new BindingHashes
                {
                    SourceReviewHash = hash,
                    ReviewManifestHash = hash,
                    PackageManifestHash = hash,
                    PackageEvidenceHash = hash,
                    PackageEvidenceEnvelopeHash = hash,
                    ContractHash = hash,
                    DefinitionHash = hash
                },
                CreatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    private static CanonicalHash CreateHash(string value) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "ReviewResult",
        Scope = "InternalFull",
        Purpose = "Integrity",
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "test-v1"
    };
}
