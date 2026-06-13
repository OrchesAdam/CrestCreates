using System.Linq;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public sealed record DescriptorCompatibilityReport
{
    public required DescriptorChangeSet ChangeSet { get; init; }
    public required DescriptorImpactAnalysisReport ImpactReport { get; init; }
    public required IReadOnlyList<DescriptorCompatibilityFinding> Findings { get; init; }
    public required DescriptorCompatibilityLevel MaxLevel { get; init; }
    public required IReadOnlyList<DescriptorCompatibilityDiagnostic> Diagnostics { get; init; }

    public bool RequiresReview =>
        MaxLevel is DescriptorCompatibilityLevel.Risky
            or DescriptorCompatibilityLevel.SecuritySensitive
            or DescriptorCompatibilityLevel.Breaking
            or DescriptorCompatibilityLevel.Unsupported;

    public bool HasBreakingChanges =>
        Findings.Any(f => f.Level == DescriptorCompatibilityLevel.Breaking);

    public bool HasSecuritySensitiveChanges =>
        Findings.Any(f => f.Level == DescriptorCompatibilityLevel.SecuritySensitive);

    public bool HasUnsupportedFindings =>
        Findings.Any(f => f.Level == DescriptorCompatibilityLevel.Unsupported);
}
