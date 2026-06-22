namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IDescriptorReviewReportRenderer
{
    string RenderMarkdown(DescriptorReviewReportDto report);
    string RenderPlainText(DescriptorReviewReportDto report);
}
