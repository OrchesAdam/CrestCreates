using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public interface IDescriptorCompatibilityAnalyzer
{
    DescriptorCompatibilityReport Analyze(
        IReadOnlyList<IDescriptor> before,
        IReadOnlyList<IDescriptor> after,
        DescriptorChangeSet changeSet,
        DescriptorImpactAnalysisReport impactReport,
        DescriptorCompatibilityAnalysisOptions? options = null);
}
