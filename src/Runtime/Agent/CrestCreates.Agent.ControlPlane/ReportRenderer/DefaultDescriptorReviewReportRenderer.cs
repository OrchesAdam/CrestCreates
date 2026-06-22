using System.Text;
using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

internal sealed class DefaultDescriptorReviewReportRenderer
    : IDescriptorReviewReportRenderer
{
    public string RenderMarkdown(DescriptorReviewReportDto report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Review Report: {report.ReportId}");
        sb.AppendLine();
        sb.AppendLine($"- **Draft**: {report.DraftId}");
        sb.AppendLine($"- **Tenant**: {report.TenantId}");
        sb.AppendLine($"- **Review Result**: {report.ReviewResultId}");
        sb.AppendLine($"- **Draft Version**: {report.DraftVersion}");
        sb.AppendLine($"- **Generated**: {report.GeneratedAt:O}");
        sb.AppendLine($"- **Contract Version**: {report.ContractVersion}");
        sb.AppendLine();

        // Render each non-empty section
        foreach (var section in GetSectionsInOrder(report))
        {
            if (section.IsEmpty) continue;
            RenderMarkdownSection(sb, section);
        }

        // Render top-level recommendations
        if (report.Recommendations.Count > 0)
        {
            sb.AppendLine("## Recommendations");
            sb.AppendLine();
            foreach (var rec in report.Recommendations)
            {
                sb.AppendLine($"- **{rec.Kind}**: {rec.Message}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string RenderPlainText(DescriptorReviewReportDto report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Review Report: {report.ReportId}");
        sb.AppendLine();
        sb.AppendLine($"  Draft: {report.DraftId}");
        sb.AppendLine($"  Tenant: {report.TenantId}");
        sb.AppendLine($"  Review Result: {report.ReviewResultId}");
        sb.AppendLine($"  Draft Version: {report.DraftVersion}");
        sb.AppendLine($"  Generated: {report.GeneratedAt:O}");
        sb.AppendLine($"  Contract Version: {report.ContractVersion}");
        sb.AppendLine();

        foreach (var section in GetSectionsInOrder(report))
        {
            if (section.IsEmpty) continue;
            RenderPlainTextSection(sb, section);
        }

        if (report.Recommendations.Count > 0)
        {
            sb.AppendLine("RECOMMENDATIONS");
            sb.AppendLine("---");
            foreach (var rec in report.Recommendations)
            {
                sb.AppendLine($"  [{rec.Kind}] {rec.Message}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static IEnumerable<DescriptorReviewReportSectionDto> GetSectionsInOrder(DescriptorReviewReportDto report)
    {
        yield return report.SummarySection;
        yield return report.DraftIdentitySection;
        yield return report.ProposedChangesSection;
        yield return report.ImpactAnalysisSection;
        yield return report.DependencySummarySection;
        yield return report.CompatibilitySection;
        yield return report.GovernanceSection;
        yield return report.RequiredHumanReviewSection;
        yield return report.ActivationEligibilitySection;
        yield return report.DiagnosticsSection;
        yield return report.RecommendationsSection;
        yield return report.PackagePreviewSection;
        yield return report.StableHashesSection;
    }

    private static void RenderMarkdownSection(StringBuilder sb, DescriptorReviewReportSectionDto section)
    {
        sb.AppendLine($"## {section.Title}");
        sb.AppendLine();
        foreach (var item in section.Items)
        {
            var severityBadge = item.Severity switch
            {
                DescriptorReviewSeverity.Blocker => "[BLOCKER]",
                DescriptorReviewSeverity.Error => "[ERROR]",
                DescriptorReviewSeverity.Warning => "[WARNING]",
                _ => "[INFO]",
            };
            sb.AppendLine($"- {severityBadge} **[{item.ReasonCode}]** {item.Message}");
        }
        sb.AppendLine();
    }

    private static void RenderPlainTextSection(StringBuilder sb, DescriptorReviewReportSectionDto section)
    {
        sb.AppendLine(section.Title.ToUpperInvariant());
        sb.AppendLine("---");
        foreach (var item in section.Items)
        {
            sb.AppendLine($"  [{item.Severity}] [{item.ReasonCode}] {item.Message}");
        }
        sb.AppendLine();
    }
}
