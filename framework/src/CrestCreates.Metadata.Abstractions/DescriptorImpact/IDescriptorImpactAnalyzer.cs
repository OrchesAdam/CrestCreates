using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public interface IDescriptorImpactAnalyzer
{
    DescriptorImpactAnalysisReport Analyze(
        DescriptorTopologySnapshot topology,
        DescriptorChangeSet changeSet,
        DescriptorImpactAnalysisOptions? options = null);
}
