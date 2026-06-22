using System.Collections;
using System.Reflection;
using Xunit;
using Moq;
using FluentAssertions;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Metadata.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class Phase7dServiceIntegrationTests : AgentControlPlaneTestBase
{
    // ── Reflection helpers ──────────────────────────────────────────────────

    private static readonly FieldInfo ReviewResultsField = typeof(DefaultAgentControlPlaneToolService)
        .GetField("_reviewResults", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>
    /// Inserts a review result directly into the service's internal _reviewResults
    /// dictionary for test setup. This avoids the need to set up the full Review
    /// pipeline mocks when testing report building in isolation.
    /// </summary>
    private static void PopulateReviewResult(
        DefaultAgentControlPlaneToolService service,
        string tenantId,
        string reviewResultId,
        DraftAbstractions.DescriptorDraftReviewResult reviewResult,
        Draft ownerDraft)
    {
        // ReviewResourceSnapshot is internal; create via reflection
        var snapshotType = typeof(DefaultAgentControlPlaneToolService).Assembly
            .GetType("CrestCreates.Agent.ControlPlane.ReviewResourceSnapshot")!;
        var snapshot = Activator.CreateInstance(snapshotType, reviewResult, ownerDraft, DateTimeOffset.UtcNow)!;

        // Access the _reviewResults ConcurrentDictionary via its non-generic IDictionary interface
        var dict = (IDictionary)ReviewResultsField.GetValue(service)!;
        dict[(tenantId, reviewResultId)] = snapshot;
    }

    private static DraftAbstractions.DescriptorDraftReviewResult CreateReviewResult(
        string draftId = "draft-001",
        string tenantId = TestTenantId,
        bool isActivationEligible = true,
        IReadOnlyList<DraftAbstractions.DescriptorDraftDiagnostic>? diagnostics = null)
    {
        return new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = draftId,
            TenantId = tenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = diagnostics ?? Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = isActivationEligible,
        };
    }

    // ── Test 1: Build with no review result → Failed ───────────────────────

    [Fact]
    public async Task BuildDescriptorReviewReport_NoReviewResult_ReturnsError()
    {
        var service = CreateService();
        var context = CreateContext("BuildDescriptorReviewReport");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var result = await service.BuildDescriptorReviewReportAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "NO_REVIEW_RESULT");
    }

    // ── Test 2: Build with non-existent draftId → NotFound ──────────────────

    [Fact]
    public async Task BuildDescriptorReviewReport_DraftNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        var context = CreateContext("BuildDescriptorReviewReport");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var result = await service.BuildDescriptorReviewReportAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    // ── Test 3: Build with denied descriptor kind → Denied ──────────────────

    [Fact]
    public async Task BuildDescriptorReviewReport_DeniedKind_ReturnsDenied()
    {
        // Use a policy where the draft's kind (Event) is denied
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowedDescriptorKinds = ["Capability", "Workflow"], // Event not allowed
            DeniedDescriptorKinds = [],
        };
        var service = CreateService(options);
        var context = CreateContext("BuildDescriptorReviewReport");
        var draft = CreateTestDraft(kind: DescriptorKind.Event);

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var result = await service.BuildDescriptorReviewReportAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    // ── Test 4: Build with valid draft + review result → Success ────────────

    [Fact]
    public async Task BuildDescriptorReviewReport_ValidDraft_ReturnsReport()
    {
        var service = CreateService();
        var context = CreateContext("BuildDescriptorReviewReport");
        var draft = CreateTestDraft();
        var reviewResult = CreateReviewResult();

        // Populate review result into the service's internal dictionary
        PopulateReviewResult(service, TestTenantId, "rr-001", reviewResult, draft);

        // Set up the draft store
        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        // Set up the report builder to return a known report DTO
        var expectedReport = CreateMinimalReportDto("draft-001");
        ReportBuilderMock
            .Setup(b => b.Build(It.Is<DescriptorReviewReportBuildRequest>(r =>
                r.ReviewResult.DraftId == "draft-001" && r.VisibilityApplied)))
            .Returns(expectedReport);

        var result = await service.BuildDescriptorReviewReportAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().NotBeNull();
        result.Value!.DraftId.Should().Be("draft-001");
        result.Value.ContractVersion.Should().Be(AgentControlPlaneContractVersion.Current);
    }

    // ── Test 4b: Build uses review-time draft, not current draft ─────────────
    // Verifies P0-1 fix: report is bound to the draft snapshot from the review,
    // not the current (potentially modified) draft.

    [Fact]
    public async Task BuildDescriptorReviewReport_UsesReviewTimeDraft_NotCurrentDraft()
    {
        var service = CreateService();
        var context = CreateContext("BuildDescriptorReviewReport");

        // Create the draft as it was at review time
        var reviewTimeDraft = CreateTestDraft(
            draftId: "draft-001",
            descriptorId: "desc-at-review-time",
            operation: DraftAbstractions.DescriptorDraftOperation.Create);

        // Create a different current draft (modified after review)
        var currentDraft = CreateTestDraft(
            draftId: "draft-001",
            descriptorId: "desc-modified-after-review",
            operation: DraftAbstractions.DescriptorDraftOperation.Update);

        var reviewResult = CreateReviewResult();

        // Populate review result with the review-time draft as the owner
        PopulateReviewResult(service, TestTenantId, "rr-001", reviewResult, reviewTimeDraft);

        // DraftStore returns the current (modified) draft
        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(currentDraft));

        var expectedReport = CreateMinimalReportDto("draft-001");
        Draft? capturedDraft = null;
        ReportBuilderMock
            .Setup(b => b.Build(It.IsAny<DescriptorReviewReportBuildRequest>()))
            .Callback<DescriptorReviewReportBuildRequest>(r => capturedDraft = r.Draft)
            .Returns(expectedReport);

        var result = await service.BuildDescriptorReviewReportAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        capturedDraft.Should().NotBeNull("the builder should have been called");
        capturedDraft!.DescriptorId.Should().Be("desc-at-review-time",
            "builder must receive the draft from review time, not the current draft");
        capturedDraft.Operation.Should().Be(DraftAbstractions.DescriptorDraftOperation.Create,
            "builder must receive the draft from review time, not the current draft");
    }

    // ── Test 5: Render with invalid contract version → InvalidRequest ───────

    [Fact]
    public async Task RenderDescriptorReviewReport_InvalidContractVersion_ReturnsUnsupportedVersion()
    {
        var service = CreateService();
        var context = CreateContext("RenderDescriptorReviewReport");
        var report = CreateMinimalReportDto("draft-001") with { ContractVersion = "old.v1" };

        var result = await service.RenderDescriptorReviewReportAsync(
            context, report, DescriptorReviewReportFormat.Markdown);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "UNSUPPORTED_REPORT_CONTRACT_VERSION");
    }

    // ── Test 6: Render with valid contract, Markdown format → Success ───────

    [Fact]
    public async Task RenderDescriptorReviewReport_ValidContractVersion_Markdown_ReturnsSuccess()
    {
        var service = CreateService();
        var context = CreateContext("RenderDescriptorReviewReport");
        var report = CreateMinimalReportDto("draft-001");

        ReportRendererMock
            .Setup(r => r.RenderMarkdown(report))
            .Returns("# Markdown Report\ncontent");

        var result = await service.RenderDescriptorReviewReportAsync(
            context, report, DescriptorReviewReportFormat.Markdown);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().Be("# Markdown Report\ncontent");
    }

    // ── Test 7: Render with valid contract, PlainText format → Success ──────

    [Fact]
    public async Task RenderDescriptorReviewReport_ValidContractVersion_PlainText_ReturnsSuccess()
    {
        var service = CreateService();
        var context = CreateContext("RenderDescriptorReviewReport");
        var report = CreateMinimalReportDto("draft-001");

        ReportRendererMock
            .Setup(r => r.RenderPlainText(report))
            .Returns("Plain text report content");

        var result = await service.RenderDescriptorReviewReportAsync(
            context, report, DescriptorReviewReportFormat.PlainText);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().Be("Plain text report content");
    }

    // ── Test 8: Render with unsupported format → InvalidRequest ─────────────

    [Fact]
    public async Task RenderDescriptorReviewReport_UnsupportedFormat_ReturnsError()
    {
        var service = CreateService();
        var context = CreateContext("RenderDescriptorReviewReport");
        var report = CreateMinimalReportDto("draft-001");

        // Cast an invalid int value to the enum (bypassing switch cases)
        var invalidFormat = (DescriptorReviewReportFormat)999;

        var result = await service.RenderDescriptorReviewReportAsync(
            context, report, invalidFormat);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "UNSUPPORTED_REPORT_FORMAT");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static DescriptorReviewReportDto CreateMinimalReportDto(string draftId)
    {
        var now = DateTimeOffset.UtcNow;
        var emptyParams = new Dictionary<string, string>(StringComparer.Ordinal);

        var summarySection = new DescriptorReviewReportSectionDto
        {
            Kind = DescriptorReviewReportSectionKind.Summary,
            SectionId = "sec-sum",
            Title = "Summary",
            Order = 1,
            IsEmpty = false,
            OverallSeverity = DescriptorReviewSeverity.Info,
            Items = new[]
            {
                new DescriptorReviewReportItemDto
                {
                    ItemId = "it-1",
                    ReasonCode = "test",
                    MessageTemplateId = "report.summary.valid",
                    Message = "All valid.",
                    Severity = DescriptorReviewSeverity.Info,
                    Parameters = emptyParams,
                }
            }
        };

        var emptySection = new DescriptorReviewReportSectionDto
        {
            Kind = DescriptorReviewReportSectionKind.DraftIdentity,
            SectionId = "sec-empty",
            Title = "Empty",
            Order = 2,
            IsEmpty = true,
            OverallSeverity = DescriptorReviewSeverity.Info,
            Items = Array.Empty<DescriptorReviewReportItemDto>(),
        };

        return new DescriptorReviewReportDto
        {
            ReportId = "rpt-minimal",
            DraftId = draftId,
            TenantId = TestTenantId,
            ReviewResultId = "rr-minimal",
            DraftVersion = "1",
            SourceReviewHash = "hash-minimal",
            TemplateVersion = "7d.v1",
            GeneratedAt = now,
            ContractVersion = AgentControlPlaneContractVersion.Current,
            Recommendations = Array.Empty<DescriptorReviewRecommendationDto>(),
            SummarySection = summarySection,
            DraftIdentitySection = emptySection,
            ProposedChangesSection = emptySection,
            ImpactAnalysisSection = emptySection,
            DependencySummarySection = emptySection,
            CompatibilitySection = emptySection,
            GovernanceSection = emptySection,
            RequiredHumanReviewSection = emptySection,
            ActivationEligibilitySection = emptySection,
            DiagnosticsSection = emptySection,
            RecommendationsSection = emptySection,
            PackagePreviewSection = emptySection,
            StableHashesSection = emptySection,
        };
    }
}
