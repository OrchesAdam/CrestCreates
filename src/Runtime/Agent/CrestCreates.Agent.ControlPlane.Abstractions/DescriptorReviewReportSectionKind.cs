namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum DescriptorReviewReportSectionKind
{
    Summary = 1,
    DraftIdentity = 2,
    ProposedChanges = 3,
    ImpactAnalysis = 4,
    DependencySummary = 5,
    Compatibility = 6,
    Governance = 7,
    RequiredHumanReview = 8,
    ActivationEligibility = 9,
    Diagnostics = 10,
    Recommendations = 11,
    PackagePreview = 12,
    StableHashes = 13
}
