namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IDescriptorReviewReportBuilder
{
    /// <summary>Builds a report from visibility-applied source values.</summary>
    DescriptorReviewReportDto Build(DescriptorReviewReportBuildRequest request);

    /// <summary>Builds a report from captured, validated report inputs.</summary>
    DescriptorReviewReportDto Build(DescriptorReviewReportInputSnapshot snapshot);
}
