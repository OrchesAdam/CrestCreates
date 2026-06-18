using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public interface IDescriptorCompatibilityRule
{
    string RuleId { get; }

    bool CanAnalyze(
        DescriptorChange change,
        IDescriptor? before,
        IDescriptor? after);

    IReadOnlyList<DescriptorCompatibilityFinding> Analyze(
        DescriptorChange change,
        IDescriptor? before,
        IDescriptor? after,
        DescriptorImpactAnalysisReport impactReport,
        DescriptorCompatibilityAnalysisOptions options);
}
