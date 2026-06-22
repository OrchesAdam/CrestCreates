using CrestCreates.Agent.ControlPlane.Abstractions.Json;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorReviewReportDto
{
    public required string ReportId { get; init; }
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required string ReviewResultId { get; init; }
    public required string DraftVersion { get; init; }
    public required string SourceReviewHash { get; init; }
    public required string TemplateVersion { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string ContractVersion { get; init; } = AgentControlPlaneContractVersion.Current;

    public required IReadOnlyList<DescriptorReviewRecommendationDto> Recommendations { get; init; }

    public required DescriptorReviewReportSectionDto SummarySection { get; init; }
    public required DescriptorReviewReportSectionDto DraftIdentitySection { get; init; }
    public required DescriptorReviewReportSectionDto ProposedChangesSection { get; init; }
    public required DescriptorReviewReportSectionDto ImpactAnalysisSection { get; init; }
    public required DescriptorReviewReportSectionDto DependencySummarySection { get; init; }
    public required DescriptorReviewReportSectionDto CompatibilitySection { get; init; }
    public required DescriptorReviewReportSectionDto GovernanceSection { get; init; }
    public required DescriptorReviewReportSectionDto RequiredHumanReviewSection { get; init; }
    public required DescriptorReviewReportSectionDto ActivationEligibilitySection { get; init; }
    public required DescriptorReviewReportSectionDto DiagnosticsSection { get; init; }
    public required DescriptorReviewReportSectionDto RecommendationsSection { get; init; }
    public required DescriptorReviewReportSectionDto PackagePreviewSection { get; init; }
    public required DescriptorReviewReportSectionDto StableHashesSection { get; init; }
}
