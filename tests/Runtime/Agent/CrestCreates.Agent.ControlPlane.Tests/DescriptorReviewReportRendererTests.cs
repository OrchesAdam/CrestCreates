using Xunit;
using FluentAssertions;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class DescriptorReviewReportRendererTests
{
    private readonly IDescriptorReviewReportRenderer _renderer = new DefaultDescriptorReviewReportRenderer();

    [Fact]
    public void RenderMarkdown_AllSections_Rendered()
    {
        var report = CreateFullReport();

        var output = _renderer.RenderMarkdown(report);

        // Header
        output.Should().Contain("# Review Report: rpt-001");
        output.Should().Contain("- **Draft**: draft-abc");
        output.Should().Contain("- **Tenant**: tenant-1");
        output.Should().Contain("- **Review Result**: rr-xyz");
        output.Should().Contain("- **Generated**:");
        output.Should().Contain("- **Contract Version**: 7e.v1");

        // All 13 section titles
        output.Should().Contain("## Summary");
        output.Should().Contain("## Draft Identity");
        output.Should().Contain("## Proposed Changes");
        output.Should().Contain("## Impact Analysis");
        output.Should().Contain("## Dependency Summary");
        output.Should().Contain("## Compatibility");
        output.Should().Contain("## Governance");
        output.Should().Contain("## Required Human Review");
        output.Should().Contain("## Activation Eligibility");
        output.Should().Contain("## Diagnostics");
        output.Should().Contain("## Recommendations");
        output.Should().Contain("## Package Preview");
        output.Should().Contain("## Stable Hashes");

        // Recommendations section
        output.Should().Contain("## Recommendations");
        output.Should().Contain("- **ApplyFixProposal**: Apply fix proposal 1");

        // Item content
        output.Should().Contain("[BLOCKER] **[ERR-001]** Validation failed");
        output.Should().Contain("[INFO] **[INFO-001]** Draft identity created");
    }

    [Fact]
    public void RenderMarkdown_EmptySections_Hidden()
    {
        var report = CreateReportWithMixedEmptySections();

        var output = _renderer.RenderMarkdown(report);

        // Non-empty sections should appear
        output.Should().Contain("## Summary");
        output.Should().Contain("## Draft Identity");
        output.Should().Contain("## Governance");

        // Empty sections should NOT appear
        output.Should().NotContain("## Proposed Changes");
        output.Should().NotContain("## Impact Analysis");
        output.Should().NotContain("## Dependency Summary");
        output.Should().NotContain("## Compatibility");
        output.Should().NotContain("## Required Human Review");
        output.Should().NotContain("## Activation Eligibility");
        output.Should().NotContain("## Diagnostics");
        output.Should().NotContain("## Package Preview");
        output.Should().NotContain("## Stable Hashes");
    }

    [Fact]
    public void RenderMarkdown_DeterministicWithSameDto()
    {
        var report = CreateFullReport();

        var output1 = _renderer.RenderMarkdown(report);
        var output2 = _renderer.RenderMarkdown(report);

        output1.Should().Be(output2);
    }

    [Fact]
    public void RenderMarkdown_UsesDtoMessage_NotTemplateCatalog()
    {
        var messageInDto = "Custom pre-formatted message from DTO";
        var report = CreateFullReport(
            itemMessage: messageInDto);

        var output = _renderer.RenderMarkdown(report);

        // The renderer must output the item.Message as-is, not re-format via template
        output.Should().Contain(messageInDto);
    }

    [Fact]
    public void RenderPlainText_ProducesNonEmptyOutput()
    {
        var report = CreateFullReport();

        var output = _renderer.RenderPlainText(report);

        output.Should().NotBeNullOrWhiteSpace();
        output.Should().Contain("Review Report: rpt-001");
        output.Should().Contain("Draft: draft-abc");
        output.Should().Contain("SUMMARY");
        output.Should().Contain("[Blocker] [ERR-001] Validation failed");
        output.Should().Contain("RECOMMENDATIONS");
        output.Should().Contain("[ApplyFixProposal] Apply fix proposal 1");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static DescriptorReviewReportDto CreateFullReport(
        string itemMessage = "Validation failed")
    {
        var now = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);

        var emptyParams = new Dictionary<string, string>(StringComparer.Ordinal);

        return new DescriptorReviewReportDto
        {
            ReportId = "rpt-001",
            DraftId = "draft-abc",
            TenantId = "tenant-1",
            ReviewResultId = "rr-xyz",
            DraftVersion = "v2",
            SourceReviewHash = "hash-123",
            TemplateVersion = "7d.v1",
            GeneratedAt = now,
            ContractVersion = AgentControlPlaneContractVersion.Current,

            Recommendations = new[]
            {
                new DescriptorReviewRecommendationDto
                {
                    RecommendationId = "rec-1",
                    ReasonCode = "FIX_AVAILABLE",
                    Message = "Apply fix proposal 1",
                    Kind = DescriptorReviewRecommendationKind.ApplyFixProposal,
                    IsActionable = true
                }
            },

            SummarySection = CreateSection(
                DescriptorReviewReportSectionKind.Summary, "sec-sum", "Summary", 1,
                false, SeverityLevel.Error,
                new[] { CreateItem("it-1", "ERR-001", "report.summary.invalid", itemMessage, SeverityLevel.Blocker, emptyParams) }),

            DraftIdentitySection = CreateSection(
                DescriptorReviewReportSectionKind.DraftIdentity, "sec-di", "Draft Identity", 2,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-2", "INFO-001", "report.draft_identity.info", "Draft identity created", SeverityLevel.Info, emptyParams) }),

            ProposedChangesSection = CreateSection(
                DescriptorReviewReportSectionKind.ProposedChanges, "sec-pc", "Proposed Changes", 3,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-3", "INFO-002", "report.proposed_changes.materialized", "3 proposed changes", SeverityLevel.Info, emptyParams) }),

            ImpactAnalysisSection = CreateSection(
                DescriptorReviewReportSectionKind.ImpactAnalysis, "sec-ia", "Impact Analysis", 4,
                false, SeverityLevel.Warning,
                new[] { CreateItem("it-4", "WARN-001", "report.impact.affected", "5 affected", SeverityLevel.Warning, emptyParams) }),

            DependencySummarySection = CreateSection(
                DescriptorReviewReportSectionKind.DependencySummary, "sec-ds", "Dependency Summary", 5,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-5", "INFO-003", "report.dependency.summary", "10 nodes, 15 edges", SeverityLevel.Info, emptyParams) }),

            CompatibilitySection = CreateSection(
                DescriptorReviewReportSectionKind.Compatibility, "sec-comp", "Compatibility", 6,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-6", "INFO-004", "report.compatibility.compatible", "All compatible", SeverityLevel.Info, emptyParams) }),

            GovernanceSection = CreateSection(
                DescriptorReviewReportSectionKind.Governance, "sec-gov", "Governance", 7,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-7", "GOV-001", "report.governance.approved", "Approved", SeverityLevel.Info, emptyParams) }),

            RequiredHumanReviewSection = CreateSection(
                DescriptorReviewReportSectionKind.RequiredHumanReview, "sec-hr", "Required Human Review", 8,
                false, SeverityLevel.Warning,
                new[] { CreateItem("it-8", "HR-001", "report.human_review.required", "Human review needed", SeverityLevel.Warning, emptyParams) }),

            ActivationEligibilitySection = CreateSection(
                DescriptorReviewReportSectionKind.ActivationEligibility, "sec-ae", "Activation Eligibility", 9,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-9", "AE-001", "report.activation.eligible", "Eligible", SeverityLevel.Info, emptyParams) }),

            DiagnosticsSection = CreateSection(
                DescriptorReviewReportSectionKind.Diagnostics, "sec-diag", "Diagnostics", 10,
                false, SeverityLevel.Error,
                new[] { CreateItem("it-10", "DIAG-001", "report.diagnostics.count", "3 errors", SeverityLevel.Error, emptyParams) }),

            RecommendationsSection = CreateSection(
                DescriptorReviewReportSectionKind.Recommendations, "sec-rec", "Recommendations", 11,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-11", "REC-001", "report.recommendation.apply_fix", "Apply fix", SeverityLevel.Info, emptyParams) }),

            PackagePreviewSection = CreateSection(
                DescriptorReviewReportSectionKind.PackagePreview, "sec-pp", "Package Preview", 12,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-12", "PP-001", "report.package_preview.present", "5 descriptors", SeverityLevel.Info, emptyParams) }),

            StableHashesSection = CreateSection(
                DescriptorReviewReportSectionKind.StableHashes, "sec-sh", "Stable Hashes", 13,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-13", "SH-001", "report.stable_hashes.present", "5 hashes", SeverityLevel.Info, emptyParams) }),
        };
    }

    private static DescriptorReviewReportDto CreateReportWithMixedEmptySections()
    {
        var now = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
        var emptyParams = new Dictionary<string, string>(StringComparer.Ordinal);

        return new DescriptorReviewReportDto
        {
            ReportId = "rpt-002",
            DraftId = "draft-def",
            TenantId = "tenant-1",
            ReviewResultId = "rr-abc",
            DraftVersion = "v1",
            SourceReviewHash = "hash-456",
            TemplateVersion = "7d.v1",
            GeneratedAt = now,
            ContractVersion = AgentControlPlaneContractVersion.Current,

            Recommendations = Array.Empty<DescriptorReviewRecommendationDto>(),

            SummarySection = CreateSection(
                DescriptorReviewReportSectionKind.Summary, "sec-sum", "Summary", 1,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-1", "INFO-1", "report.summary.valid", "Passed", SeverityLevel.Info, emptyParams) }),

            DraftIdentitySection = CreateSection(
                DescriptorReviewReportSectionKind.DraftIdentity, "sec-di", "Draft Identity", 2,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-2", "INFO-2", "report.draft_identity.info", "Identity info", SeverityLevel.Info, emptyParams) }),

            ProposedChangesSection = EmptySection(DescriptorReviewReportSectionKind.ProposedChanges, "sec-pc", "Proposed Changes", 3),
            ImpactAnalysisSection = EmptySection(DescriptorReviewReportSectionKind.ImpactAnalysis, "sec-ia", "Impact Analysis", 4),
            DependencySummarySection = EmptySection(DescriptorReviewReportSectionKind.DependencySummary, "sec-ds", "Dependency Summary", 5),
            CompatibilitySection = EmptySection(DescriptorReviewReportSectionKind.Compatibility, "sec-comp", "Compatibility", 6),

            GovernanceSection = CreateSection(
                DescriptorReviewReportSectionKind.Governance, "sec-gov", "Governance", 7,
                false, SeverityLevel.Info,
                new[] { CreateItem("it-3", "GOV-1", "report.governance.approved", "Approved", SeverityLevel.Info, emptyParams) }),

            RequiredHumanReviewSection = EmptySection(DescriptorReviewReportSectionKind.RequiredHumanReview, "sec-hr", "Required Human Review", 8),
            ActivationEligibilitySection = EmptySection(DescriptorReviewReportSectionKind.ActivationEligibility, "sec-ae", "Activation Eligibility", 9),
            DiagnosticsSection = EmptySection(DescriptorReviewReportSectionKind.Diagnostics, "sec-diag", "Diagnostics", 10),
            RecommendationsSection = EmptySection(DescriptorReviewReportSectionKind.Recommendations, "sec-rec", "Recommendations", 11),
            PackagePreviewSection = EmptySection(DescriptorReviewReportSectionKind.PackagePreview, "sec-pp", "Package Preview", 12),
            StableHashesSection = EmptySection(DescriptorReviewReportSectionKind.StableHashes, "sec-sh", "Stable Hashes", 13),
        };
    }

    private static DescriptorReviewReportSectionDto CreateSection(
        DescriptorReviewReportSectionKind kind,
        string sectionId,
        string title,
        int order,
        bool isEmpty,
        SeverityLevel overallSeverity,
        IReadOnlyList<DescriptorReviewReportItemDto> items)
    {
        return new DescriptorReviewReportSectionDto
        {
            Kind = kind,
            SectionId = sectionId,
            Title = title,
            Order = order,
            IsEmpty = isEmpty,
            OverallSeverity = overallSeverity,
            Items = items
        };
    }

    private static DescriptorReviewReportSectionDto EmptySection(
        DescriptorReviewReportSectionKind kind,
        string sectionId,
        string title,
        int order)
    {
        return new DescriptorReviewReportSectionDto
        {
            Kind = kind,
            SectionId = sectionId,
            Title = title,
            Order = order,
            IsEmpty = true,
            OverallSeverity = SeverityLevel.Info,
            Items = Array.Empty<DescriptorReviewReportItemDto>()
        };
    }

    private static DescriptorReviewReportItemDto CreateItem(
        string itemId,
        string reasonCode,
        string messageTemplateId,
        string message,
        SeverityLevel severity,
        IReadOnlyDictionary<string, string> parameters)
    {
        return new DescriptorReviewReportItemDto
        {
            ItemId = itemId,
            ReasonCode = reasonCode,
            MessageTemplateId = messageTemplateId,
            Message = message,
            Severity = severity,
            Parameters = parameters
        };
    }
}
