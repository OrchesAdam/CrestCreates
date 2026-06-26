using System.Text;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;

namespace CrestCreates.Agent.ControlPlane;

public sealed class DefaultDescriptorReviewMessageTemplateCatalog
    : IDescriptorReviewMessageTemplateCatalog
{
    public string TemplateVersion => "7d.v1";

    private static readonly Dictionary<string, string> Templates = new(StringComparer.Ordinal)
    {
        [DescriptorActivationMessageTemplateIds.ActivationEligibleValue] = "Draft is eligible for activation handoff.",
        [DescriptorActivationMessageTemplateIds.ActivationBlockedValue] = "Draft is not eligible: {BlockingReasons}.",
        [DescriptorReviewReportMessageTemplateIds.GovernanceApprovedValue] = "Governance decision: approved. {Rationale}",
        [DescriptorReviewReportMessageTemplateIds.GovernanceRejectedValue] = "Governance decision: rejected. {Rationale}",
        [DescriptorReviewReportMessageTemplateIds.GovernanceReviewRequiredValue] = "Governance decision: review required. {Rationale}",
        [DescriptorReviewReportMessageTemplateIds.DiagnosticsMissingRefValue] = "Descriptor '{DescriptorId}' references missing '{ReferenceId}'.",
        [DescriptorReviewReportMessageTemplateIds.CompatibilitySchemaValue] = "Schema change is incompatible: {Details}.",
        [DescriptorReviewReportMessageTemplateIds.SummaryValidValue] = "Draft validation passed with {DiagnosticCount} diagnostics.",
        [DescriptorReviewReportMessageTemplateIds.SummaryInvalidValue] = "Draft validation failed with {ErrorCount} errors and {BlockerCount} blockers.",
        [DescriptorReviewReportMessageTemplateIds.HumanReviewRequiredValue] = "Human review required: {Reason}.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationNoActionValue] = "No action required at this time.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationActivationHandoffValue] = "Draft is ready for activation handoff.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationHumanReviewValue] = "Human review is required before proceeding.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationApplyFixValue] = "Fix proposal available: {FixProposalId}.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationReviseDraftValue] = "Draft needs revision before proceeding.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationCancelDraftValue] = "Draft should be cancelled.",
        [DescriptorReviewReportMessageTemplateIds.PackageAvailableValue] = "Package preview available with {DescriptorCount} descriptors.",
        [DescriptorReviewReportMessageTemplateIds.HashesComputedValue] = "Stable hashes computed for {HashCount} items.",
        [DescriptorReviewReportMessageTemplateIds.DraftIdentityInfoValue] = "Draft '{DraftId}' of kind '{DescriptorKind}', operation {Operation}, status {Status}.",
        [DescriptorReviewReportMessageTemplateIds.ProposedChangesMaterializedValue] = "Materialization produced {ProposedCount} proposed descriptors.",
        [DescriptorReviewReportMessageTemplateIds.ProposedChangesFailedValue] = "Materialization failed: {Reason}.",
        [DescriptorReviewReportMessageTemplateIds.ImpactAffectedValue] = "Impact analysis found {AffectedCount} affected descriptors.",
        [DescriptorReviewReportMessageTemplateIds.ImpactNoneValue] = "No descriptors affected by this draft.",
        [DescriptorReviewReportMessageTemplateIds.DependencySummaryValue] = "Topology: {NodeCount} nodes, {EdgeCount} edges.",
        [DescriptorReviewReportMessageTemplateIds.CompatibilityCompatibleValue] = "All {DescriptorCount} descriptors are compatible.",
        [DescriptorReviewReportMessageTemplateIds.CompatibilityIncompatibleValue] = "{IncompatibleCount} of {TotalCount} descriptors are incompatible.",
        [DescriptorReviewReportMessageTemplateIds.DiagnosticsCountValue] = "{TotalCount} diagnostics: {InfoCount} info, {WarningCount} warnings, {ErrorCount} errors, {BlockerCount} blockers.",
        [DescriptorReviewReportMessageTemplateIds.StableHashesPresentValue] = "Stable hashes available for {HashCount} items.",
        [DescriptorReviewReportMessageTemplateIds.StableHashesNoneValue] = "No stable hashes computed.",
        [DescriptorReviewReportMessageTemplateIds.PackagePreviewPresentValue] = "Package preview with {DescriptorCount} descriptors, {HashCount} hashes.",
        [DescriptorReviewReportMessageTemplateIds.PackagePreviewNoneValue] = "No package preview available.",
    };

    /// <summary>
    /// Deterministic placeholder replacer — replaces {Key} tokens without Regex,
    /// ensuring AOT compatibility (no RegexOptions.Compiled / runtime IL emit).
    /// </summary>
    public string Format(string messageTemplateId, IReadOnlyDictionary<string, string> parameters)
    {
        if (!Templates.TryGetValue(messageTemplateId, out var template))
            return $"[Unknown template: {messageTemplateId}]";

        // Hand-written replacer for {Word} placeholders — avoids Regex for AOT safety
        var result = new StringBuilder(template.Length);
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end > i)
                {
                    var key = template.Substring(i + 1, end - i - 1);
                    result.Append(parameters.TryGetValue(key, out var value) ? value : template.AsSpan(i, end - i + 1));
                    i = end + 1;
                    continue;
                }
            }
            result.Append(template[i]);
            i++;
        }
        return result.ToString();
    }
}
