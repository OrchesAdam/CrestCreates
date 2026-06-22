namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IDescriptorReviewReportBuilder
{
    DescriptorReviewReportDto Build(DescriptorReviewReportBuildRequest request);
}
