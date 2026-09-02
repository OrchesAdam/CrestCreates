using System.Text;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Localization.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

public sealed class DefaultDescriptorReviewMessageTemplateCatalog
    : IDescriptorReviewMessageTemplateCatalog
{
    private const string DefaultCulture = "en";

    private readonly ILocalizationService? _localizationService;
    private readonly ILogger<DefaultDescriptorReviewMessageTemplateCatalog> _logger;
    private readonly DescriptorReviewMessageResourceCatalog _resourceCatalog;

    public DefaultDescriptorReviewMessageTemplateCatalog()
        : this(null, NullLogger<DefaultDescriptorReviewMessageTemplateCatalog>.Instance)
    {
    }

    public DefaultDescriptorReviewMessageTemplateCatalog(
        ILocalizationService? localizationService,
        ILogger<DefaultDescriptorReviewMessageTemplateCatalog> logger)
    {
        _localizationService = localizationService;
        _logger = logger;
        _resourceCatalog = new DescriptorReviewMessageResourceCatalog(logger);
    }

    public string TemplateVersion => "7d.v1";

    private static readonly Dictionary<string, string> Templates = new(StringComparer.Ordinal)
    {
        [DescriptorActivationMessageTemplateIds.ActivationEligible] = "Draft is eligible for activation handoff.",
        [DescriptorActivationMessageTemplateIds.ActivationBlocked] = "Draft is not eligible: {BlockingReasons}.",
        [DescriptorReviewReportMessageTemplateIds.GovernanceApproved] = "Governance decision: approved. {Rationale}",
        [DescriptorReviewReportMessageTemplateIds.GovernanceRejected] = "Governance decision: rejected. {Rationale}",
        [DescriptorReviewReportMessageTemplateIds.GovernanceReviewRequired] = "Governance decision: review required. {Rationale}",
        [DescriptorReviewReportMessageTemplateIds.DiagnosticsMissingRef] = "Descriptor '{DescriptorId}' references missing '{ReferenceId}'.",
        [DescriptorReviewReportMessageTemplateIds.CompatibilitySchema] = "Schema change is incompatible: {Details}.",
        [DescriptorReviewReportMessageTemplateIds.SummaryValid] = "Draft validation passed with {DiagnosticCount} diagnostics.",
        [DescriptorReviewReportMessageTemplateIds.SummaryInvalid] = "Draft validation failed with {ErrorCount} errors and {BlockerCount} blockers.",
        [DescriptorReviewReportMessageTemplateIds.HumanReviewRequired] = "Human review required: {Reason}.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationNoAction] = "No action required at this time.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationActivationHandoff] = "Draft is ready for activation handoff.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationHumanReview] = "Human review is required before proceeding.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationApplyFix] = "Fix proposal available: {FixProposalId}.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationReviseDraft] = "Draft needs revision before proceeding.",
        [DescriptorReviewReportMessageTemplateIds.RecommendationCancelDraft] = "Draft should be cancelled.",
        [DescriptorReviewReportMessageTemplateIds.PackageAvailable] = "Package preview available with {DescriptorCount} descriptors.",
        [DescriptorReviewReportMessageTemplateIds.HashesComputed] = "Stable hashes computed for {HashCount} items.",
        [DescriptorReviewReportMessageTemplateIds.DraftIdentityInfo] = "Draft '{DraftId}' of kind '{DescriptorKind}', operation {Operation}, status {Status}.",
        [DescriptorReviewReportMessageTemplateIds.ProposedChangesMaterialized] = "Materialization produced {ProposedCount} proposed descriptors.",
        [DescriptorReviewReportMessageTemplateIds.ProposedChangesFailed] = "Materialization failed: {Reason}.",
        [DescriptorReviewReportMessageTemplateIds.ImpactAffected] = "Impact analysis found {AffectedCount} affected descriptors.",
        [DescriptorReviewReportMessageTemplateIds.ImpactNone] = "No descriptors affected by this draft.",
        [DescriptorReviewReportMessageTemplateIds.DependencySummary] = "Topology: {NodeCount} nodes, {EdgeCount} edges.",
        [DescriptorReviewReportMessageTemplateIds.CompatibilityCompatible] = "All {DescriptorCount} descriptors are compatible.",
        [DescriptorReviewReportMessageTemplateIds.CompatibilityIncompatible] = "{IncompatibleCount} of {TotalCount} descriptors are incompatible.",
        [DescriptorReviewReportMessageTemplateIds.DiagnosticsCount] = "{TotalCount} diagnostics: {InfoCount} info, {WarningCount} warnings, {ErrorCount} errors, {BlockerCount} blockers.",
        [DescriptorReviewReportMessageTemplateIds.StableHashesPresent] = "Stable hashes available for {HashCount} items.",
        [DescriptorReviewReportMessageTemplateIds.StableHashesNone] = "No stable hashes computed.",
        [DescriptorReviewReportMessageTemplateIds.PackagePreviewPresent] = "Package preview with {DescriptorCount} descriptors, {HashCount} hashes.",
        [DescriptorReviewReportMessageTemplateIds.PackagePreviewNone] = "No package preview available.",
    };

    /// <summary>
    /// Deterministic placeholder replacer — replaces {Key} tokens without Regex,
    /// ensuring AOT compatibility (no RegexOptions.Compiled / runtime IL emit).
    /// </summary>
    public string Format(string messageTemplateId, IReadOnlyDictionary<string, string> parameters)
    {
        if (!Templates.TryGetValue(messageTemplateId, out var stableEnglishTemplate))
            return $"[Unknown template: {messageTemplateId}]";

        var cultureName = ResolveCultureName();
        var template = TryResolveLocalizationServiceTemplate(messageTemplateId, cultureName);
        if (template is null
            && _resourceCatalog.TryGetTemplate(cultureName, messageTemplateId, out var resourceTemplate)
            && !IsLocalizationMiss(resourceTemplate, messageTemplateId))
        {
            template = resourceTemplate;
        }
        template ??= stableEnglishTemplate;

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

    private string ResolveCultureName()
    {
        if (_localizationService is null)
            return DefaultCulture;

        try
        {
            var cultureName = _localizationService.CurrentCulture;
            return string.IsNullOrWhiteSpace(cultureName) ? DefaultCulture : cultureName;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Descriptor governance localization culture resolution failed; using the stable fallback culture.");
            return DefaultCulture;
        }
    }

    private string? TryResolveLocalizationServiceTemplate(string messageTemplateId, string cultureName)
    {
        if (_localizationService is null)
            return null;

        try
        {
            var value = _localizationService.GetString(messageTemplateId, cultureName);
            return IsLocalizationMiss(value, messageTemplateId) ? null : value;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Descriptor governance localization lookup failed for template {MessageTemplateId}; using deterministic fallback.",
                messageTemplateId);
            return null;
        }
    }

    private static bool IsLocalizationMiss(string? value, string messageTemplateId)
        => string.IsNullOrWhiteSpace(value)
            || string.Equals(value, messageTemplateId, StringComparison.Ordinal);
}
