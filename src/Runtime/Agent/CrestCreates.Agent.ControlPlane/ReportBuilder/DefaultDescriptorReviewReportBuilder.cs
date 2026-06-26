using System.Security.Cryptography;
using System.Text;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Agent.ControlPlane;

public sealed class DefaultDescriptorReviewReportBuilder : IDescriptorReviewReportBuilder
{
    private readonly IDescriptorReviewMessageTemplateCatalog _templateCatalog;
    private readonly TimeProvider _clock;
    private readonly IDescriptorDraftReviewHashService _reviewHashService;

    public DefaultDescriptorReviewReportBuilder(
        IDescriptorReviewMessageTemplateCatalog templateCatalog,
        IDescriptorDraftReviewHashService reviewHashService,
        TimeProvider? clock = null)
    {
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
        _reviewHashService = reviewHashService ?? throw new ArgumentNullException(nameof(reviewHashService));
        _clock = clock ?? TimeProvider.System;
    }

    public DescriptorReviewReportDto Build(DescriptorReviewReportBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.VisibilityApplied)
            throw new InvalidOperationException(
                "Cannot build review report: visibility has not been applied to the review result. " +
                "Apply visibility filtering before building the report.");

        var reviewResult = request.ReviewResult;
        var draft = request.Draft;

        var contractVersion = AgentControlPlaneContractVersion.Current;
        var templateVersion = _templateCatalog.TemplateVersion;
        var draftVersion = draft.ProposedVersion ?? draft.BaseVersion ?? "0";

        var sourceReviewHash = _reviewHashService.ComputeSourceReviewHash(reviewResult);
        var reviewResultId = sourceReviewHash.Value;
        var reportIdRaw = $"{reviewResult.TenantId}|{reviewResult.DraftId}|{draftVersion}|{reviewResultId}|{contractVersion}|{templateVersion}";
        var reportId = ComputeSha256(reportIdRaw);

        var summarySection = BuildSummarySection(reviewResult);
        var draftIdentitySection = BuildDraftIdentitySection(draft);
        var proposedChangesSection = BuildProposedChangesSection(reviewResult);
        var impactAnalysisSection = BuildImpactAnalysisSection(reviewResult);
        var dependencySummarySection = BuildDependencySummarySection(reviewResult);
        var compatibilitySection = BuildCompatibilitySection(reviewResult);
        var governanceSection = BuildGovernanceSection(reviewResult);
        var requiredHumanReviewSection = BuildRequiredHumanReviewSection(reviewResult);
        var activationEligibilitySection = BuildActivationEligibilitySection(reviewResult);
        var diagnosticsSection = BuildDiagnosticsSection(reviewResult);
        var packagePreviewSection = BuildPackagePreviewSection(reviewResult);
        var stableHashesSection = BuildStableHashesSection(reviewResult);

        // Build Recommendations section before computing top-level recommendations
        // so we can derive them from the rendered sections.
        var recommendationsSection = BuildRecommendationsSection(
            reviewResult, summarySection, governanceSection,
            requiredHumanReviewSection, activationEligibilitySection, diagnosticsSection);

        var topLevelRecommendations = DeriveTopLevelRecommendations(
            reviewResult, recommendationsSection, governanceSection,
            diagnosticsSection, requiredHumanReviewSection, activationEligibilitySection);

        var generatedAt = _clock.GetUtcNow();

        return new DescriptorReviewReportDto
        {
            ReportId = reportId,
            DraftId = reviewResult.DraftId,
            TenantId = reviewResult.TenantId,
            ReviewResultId = reviewResultId,
            DraftVersion = draftVersion,
            SourceReviewHash = sourceReviewHash.Value,
            TemplateVersion = templateVersion,
            GeneratedAt = generatedAt,
            ContractVersion = contractVersion,
            Recommendations = topLevelRecommendations,
            SummarySection = summarySection,
            DraftIdentitySection = draftIdentitySection,
            ProposedChangesSection = proposedChangesSection,
            ImpactAnalysisSection = impactAnalysisSection,
            DependencySummarySection = dependencySummarySection,
            CompatibilitySection = compatibilitySection,
            GovernanceSection = governanceSection,
            RequiredHumanReviewSection = requiredHumanReviewSection,
            ActivationEligibilitySection = activationEligibilitySection,
            DiagnosticsSection = diagnosticsSection,
            RecommendationsSection = recommendationsSection,
            PackagePreviewSection = packagePreviewSection,
            StableHashesSection = stableHashesSection,
        };
    }

    // ── Section 1: Summary ──────────────────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildSummarySection(DescriptorDraftReviewResult reviewResult)
    {
        var diagnostics = reviewResult.ValidationResult.Diagnostics;
        var reviewDiagnostics = reviewResult.Diagnostics;

        var allDiagnostics = diagnostics.Concat(reviewDiagnostics).ToList();

        var infoCount = allDiagnostics.Count(d => d.Severity == DescriptorDraftDiagnosticSeverity.Info);
        var warningCount = allDiagnostics.Count(d => d.Severity == DescriptorDraftDiagnosticSeverity.Warning);
        var errorCount = allDiagnostics.Count(d => d.Severity == DescriptorDraftDiagnosticSeverity.Error);
        var blockerCount = allDiagnostics.Count(d => d.Severity == DescriptorDraftDiagnosticSeverity.Blocker);

        var items = new List<DescriptorReviewReportItemDto>();

        // Validation status item
        if (reviewResult.ValidationResult.IsValid)
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DiagnosticCount"] = allDiagnostics.Count.ToString(),
                ["InfoCount"] = infoCount.ToString(),
                ["WarningCount"] = warningCount.ToString(),
                ["ErrorCount"] = errorCount.ToString(),
                ["BlockerCount"] = blockerCount.ToString(),
            };
            items.Add(CreateItem("summary_validation", "validation_passed", DescriptorReviewReportMessageTemplateIds.SummaryValid,
                MapMaxSeverity(allDiagnostics), parameters));
        }
        else
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DiagnosticCount"] = allDiagnostics.Count.ToString(),
                ["ErrorCount"] = errorCount.ToString(),
                ["BlockerCount"] = blockerCount.ToString(),
            };
            items.Add(CreateItem("summary_validation", "validation_failed", DescriptorReviewReportMessageTemplateIds.SummaryInvalid,
                MapMaxSeverity(allDiagnostics), parameters));
        }

        // Diagnostic counts summary
        var countParams = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TotalCount"] = allDiagnostics.Count.ToString(),
            ["InfoCount"] = infoCount.ToString(),
            ["WarningCount"] = warningCount.ToString(),
            ["ErrorCount"] = errorCount.ToString(),
            ["BlockerCount"] = blockerCount.ToString(),
        };
        items.Add(CreateItem("summary_diag_counts", "diagnostic_counts", DescriptorReviewReportMessageTemplateIds.DiagnosticsCount,
            MapMaxSeverity(allDiagnostics), countParams));

        // Activation eligibility item
        if (reviewResult.IsActivationEligible)
        {
            items.Add(CreateItem("summary_activation", "activation_eligible",
                DescriptorActivationMessageTemplateIds.ActivationEligible, DescriptorReviewSeverity.Info,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }
        else
        {
            var blockers = (reviewResult.Diagnostics ?? Array.Empty<DescriptorDraftDiagnostic>())
                .Where(d => d.Severity is DescriptorDraftDiagnosticSeverity.Blocker or DescriptorDraftDiagnosticSeverity.Error)
                .Select(d => d.Code)
                .ToList();
            var blockingReasons = blockers.Count > 0 ? string.Join(", ", blockers.OrderBy(c => c)) : "unknown";
            var blockParams = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BlockingReasons"] = blockingReasons,
            };
            items.Add(CreateItem("summary_activation", "activation_blocked",
                DescriptorActivationMessageTemplateIds.ActivationBlocked, DescriptorReviewSeverity.Blocker, blockParams));
        }

        // Governance summary item
        if (reviewResult.GovernanceDecision != null)
        {
            var decision = reviewResult.GovernanceDecision;
            var reason = decision.Decisions.FirstOrDefault()?.Decision.ToString() ?? decision.MaxDecision.ToString();
            var govParams = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Rationale"] = reason,
            };
            var templateId = decision.MaxDecision switch
            {
                DescriptorLifecycleDecisionKind.Allowed => DescriptorReviewReportMessageTemplateIds.GovernanceApproved,
                DescriptorLifecycleDecisionKind.Blocked => DescriptorReviewReportMessageTemplateIds.GovernanceRejected,
                DescriptorLifecycleDecisionKind.ReviewRequired => DescriptorReviewReportMessageTemplateIds.GovernanceReviewRequired,
                _ => DescriptorReviewReportMessageTemplateIds.GovernanceReviewRequired,
            };
            var severity = decision.MaxDecision switch
            {
                DescriptorLifecycleDecisionKind.Allowed => DescriptorReviewSeverity.Info,
                DescriptorLifecycleDecisionKind.Blocked => DescriptorReviewSeverity.Blocker,
                DescriptorLifecycleDecisionKind.ReviewRequired => DescriptorReviewSeverity.Warning,
                _ => DescriptorReviewSeverity.Warning,
            };
            items.Add(CreateItem("summary_governance", "governance_decision", templateId, severity, govParams));
        }

        var overallSeverity = items.Count > 0
            ? items.Max(i => (int)i.Severity) is var max && Enum.IsDefined(typeof(DescriptorReviewSeverity), max)
                ? (DescriptorReviewSeverity)max
                : DescriptorReviewSeverity.Info
            : DescriptorReviewSeverity.Info;

        return CreateSection(DescriptorReviewReportSectionKind.Summary, "summary",
            "Summary", 1, items.Count == 0, overallSeverity, items);
    }

    // ── Section 2: DraftIdentity ────────────────────────────────────────────

    private static DescriptorReviewReportSectionDto BuildDraftIdentitySection(Draft draft)
    {
        var items = new List<DescriptorReviewReportItemDto>();
        // Build message directly since we want a structured identity summary
        var message = $"Draft '{draft.DraftId}' of kind '{draft.DescriptorKind}', " +
                      $"operation {draft.Operation}, status {draft.Status}. " +
                      $"Author: {draft.AuthorKind}:{draft.AuthorId}.";

        var item = new DescriptorReviewReportItemDto
        {
            ItemId = "draft_identity_info",
            ReasonCode = "draft_identity",
            MessageTemplateId = DescriptorReviewReportMessageTemplateIds.DraftIdentityInfo,
            Message = message,
            Severity = DescriptorReviewSeverity.Info,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DraftId"] = draft.DraftId,
                ["DescriptorId"] = draft.DescriptorId,
                ["DescriptorKind"] = draft.DescriptorKind.ToString(),
                ["Operation"] = draft.Operation.ToString(),
                ["AuthorKind"] = draft.AuthorKind.ToString(),
                ["AuthorId"] = draft.AuthorId,
                ["Status"] = draft.Status.ToString(),
                ["TenantId"] = draft.TenantId,
            },
        };
        items.Add(item);

        return CreateSection(DescriptorReviewReportSectionKind.DraftIdentity, "draft_identity",
            "Draft Identity", 2, items.Count == 0, DescriptorReviewSeverity.Info, items);
    }

    // ── Section 3: ProposedChanges ──────────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildProposedChangesSection(DescriptorDraftReviewResult reviewResult)
    {
        var items = new List<DescriptorReviewReportItemDto>();
        var materialization = reviewResult.MaterializationResult;

        if (materialization != null)
        {
            if (materialization.IsMaterialized)
            {
                var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ProposedCount"] = materialization.ProposedInventory.Count.ToString(),
                };
                items.Add(CreateItem("proposed_materialized", "materialized",
                    DescriptorReviewReportMessageTemplateIds.ProposedChangesMaterialized, DescriptorReviewSeverity.Info, parameters));

                // Add items for each proposed descriptor
                for (int i = 0; i < materialization.ProposedInventory.Count; i++)
                {
                    var desc = materialization.ProposedInventory[i];
                    var descParams = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["DescriptorId"] = desc.Id,
                        ["DescriptorName"] = desc.Name,
                        ["DescriptorKind"] = desc.Kind.ToString(),
                        ["Namespace"] = desc.Namespace,
                    };
                    items.Add(CreateItem($"proposed_desc_{i}", "proposed_descriptor",
                        DescriptorReviewReportMessageTemplateIds.ProposedChangesMaterialized, DescriptorReviewSeverity.Info, descParams));
                }
            }
            else
            {
                var reason = materialization.Diagnostics.FirstOrDefault()?.Message ?? "Materialization failed";
                var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Reason"] = reason,
                };
                items.Add(CreateItem("proposed_failed", "materialization_failed",
                    DescriptorReviewReportMessageTemplateIds.ProposedChangesFailed,
                    MapMaxSeverity(materialization.Diagnostics), parameters));
            }
        }

        var overallSeverity = items.Count > 0
            ? (DescriptorReviewSeverity)items.Max(i => (int)i.Severity)
            : DescriptorReviewSeverity.Info;

        return CreateSection(DescriptorReviewReportSectionKind.ProposedChanges, "proposed_changes",
            "Proposed Changes", 3, items.Count == 0, overallSeverity, items);
    }

    // ── Section 4: ImpactAnalysis ───────────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildImpactAnalysisSection(DescriptorDraftReviewResult reviewResult)
    {
        var items = new List<DescriptorReviewReportItemDto>();
        var impact = reviewResult.ImpactAnalysisResult;

        if (impact != null)
        {
            if (impact.AffectedDescriptors.Count > 0)
            {
                var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["AffectedCount"] = impact.AffectedDescriptors.Count.ToString(),
                    ["MaxSeverity"] = impact.MaxSeverity.ToString(),
                };
                items.Add(CreateItem("impact_affected", "impact_has_affected",
                    DescriptorReviewReportMessageTemplateIds.ImpactAffected, MapImpactSeverity(impact.MaxSeverity), parameters));

                // Add items for each affected descriptor
                foreach (var affected in impact.AffectedDescriptors.OrderBy(a => a.Name))
                {
                    var affParams = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["DescriptorName"] = affected.Name,
                        ["DescriptorKind"] = affected.Kind.ToString(),
                        ["Severity"] = affected.Severity.ToString(),
                        ["Reason"] = affected.Reason ?? "",
                    };
                    items.Add(CreateItem($"impact_affected_{affected.Name}", "affected_descriptor",
                        DescriptorReviewReportMessageTemplateIds.ImpactAffected, MapImpactSeverity(affected.Severity), affParams));
                }
            }
            else
            {
                items.Add(CreateItem("impact_none", "impact_none",
                    DescriptorReviewReportMessageTemplateIds.ImpactNone, DescriptorReviewSeverity.Info,
                    new Dictionary<string, string>(StringComparer.Ordinal)));
            }
        }

        var overallSeverity = items.Count > 0
            ? (DescriptorReviewSeverity)items.Max(i => (int)i.Severity)
            : DescriptorReviewSeverity.Info;

        return CreateSection(DescriptorReviewReportSectionKind.ImpactAnalysis, "impact_analysis",
            "Impact Analysis", 4, items.Count == 0, overallSeverity, items);
    }

    // ── Section 5: DependencySummary ───────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildDependencySummarySection(DescriptorDraftReviewResult reviewResult)
    {
        var items = new List<DescriptorReviewReportItemDto>();
        var topology = reviewResult.TopologySnapshot;

        if (topology != null)
        {
            // Count nodes by kind
            var nodeCountsByKind = topology.Nodes.Values
                .GroupBy(n => n.Kind)
                .ToDictionary(g => g.Key, g => g.Count());

            var nodeKindSummary = string.Join(", ", nodeCountsByKind.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}:{kvp.Value}"));

            // Count edges by kind
            var edgeCountsByKind = topology.Edges
                .GroupBy(e => e.Kind)
                .ToDictionary(g => g.Key, g => g.Count());

            var edgeKindSummary = string.Join(", ", edgeCountsByKind.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}:{kvp.Value}"));

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NodeCount"] = topology.NodeCount.ToString(),
                ["EdgeCount"] = topology.EdgeCount.ToString(),
                ["NodeKindSummary"] = nodeKindSummary,
                ["EdgeKindSummary"] = edgeKindSummary,
            };
            items.Add(CreateItem("topology_summary", "dependency_summary",
                DescriptorReviewReportMessageTemplateIds.DependencySummary, DescriptorReviewSeverity.Info, parameters));
        }

        var overallSeverity = items.Count > 0
            ? (DescriptorReviewSeverity)items.Max(i => (int)i.Severity)
            : DescriptorReviewSeverity.Info;

        return CreateSection(DescriptorReviewReportSectionKind.DependencySummary, "dependency_summary",
            "Dependency Summary", 5, items.Count == 0, overallSeverity, items);
    }

    // ── Section 6: Compatibility ────────────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildCompatibilitySection(DescriptorDraftReviewResult reviewResult)
    {
        var items = new List<DescriptorReviewReportItemDto>();
        var compatibility = reviewResult.CompatibilityResult;

        if (compatibility != null)
        {
            var totalFindings = compatibility.Findings.Count;
            var incompatibleCount = compatibility.Findings.Count(f =>
                f.Level != DescriptorCompatibilityLevel.Compatible);

            var compatibleCount = totalFindings - incompatibleCount;

            if (totalFindings > 0)
            {
                var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["TotalCount"] = totalFindings.ToString(),
                    ["CompatibleCount"] = compatibleCount.ToString(),
                    ["IncompatibleCount"] = incompatibleCount.ToString(),
                    ["DescriptorCount"] = totalFindings.ToString(),
                };

                var templateId = incompatibleCount > 0
                    ? DescriptorReviewReportMessageTemplateIds.CompatibilityIncompatible
                    : DescriptorReviewReportMessageTemplateIds.CompatibilityCompatible;

                var severity = incompatibleCount > 0
                    ? DescriptorReviewSeverity.Warning
                    : DescriptorReviewSeverity.Info;

                items.Add(CreateItem("compatibility_summary", "compatibility_assessment",
                    templateId, severity, parameters));

                // Add items for incompatible findings
                foreach (var finding in compatibility.Findings.Where(f =>
                    f.Level != DescriptorCompatibilityLevel.Compatible)
                    .OrderBy(f => f.Level).ThenBy(f => f.Subject.Id))
                {
                    var findParams = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Level"] = finding.Level.ToString(),
                        ["DescriptorId"] = finding.Subject.Id,
                    };
                    items.Add(CreateItem($"compat_finding_{finding.Level}_{items.Count}",
                        "incompatible_finding", DescriptorReviewReportMessageTemplateIds.CompatibilityIncompatible,
                        MapCompatibilityLevel(finding.Level), findParams));
                }
            }
        }

        var overallSeverity = items.Count > 0
            ? (DescriptorReviewSeverity)items.Max(i => (int)i.Severity)
            : DescriptorReviewSeverity.Info;

        return CreateSection(DescriptorReviewReportSectionKind.Compatibility, "compatibility",
            "Compatibility", 6, items.Count == 0, overallSeverity, items);
    }

    // ── Section 7: Governance ───────────────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildGovernanceSection(DescriptorDraftReviewResult reviewResult)
    {
        var items = new List<DescriptorReviewReportItemDto>();
        var gov = reviewResult.GovernanceDecision;

        if (gov != null)
        {
            var rationale = gov.Decisions.FirstOrDefault()?.Transition.ToString() ?? "";
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Rationale"] = rationale,
                ["MaxDecision"] = gov.MaxDecision.ToString(),
            };

            var (templateId, severity) = gov.MaxDecision switch
            {
                DescriptorLifecycleDecisionKind.Allowed =>
                    (DescriptorReviewReportMessageTemplateIds.GovernanceApproved, DescriptorReviewSeverity.Info),
                DescriptorLifecycleDecisionKind.Blocked =>
                    (DescriptorReviewReportMessageTemplateIds.GovernanceRejected, DescriptorReviewSeverity.Blocker),
                DescriptorLifecycleDecisionKind.ReviewRequired =>
                    (DescriptorReviewReportMessageTemplateIds.GovernanceReviewRequired, DescriptorReviewSeverity.Warning),
                _ => (DescriptorReviewReportMessageTemplateIds.GovernanceReviewRequired, DescriptorReviewSeverity.Warning),
            };

            items.Add(CreateItem("governance_decision", "governance", templateId, severity, parameters));

            // Add items for package findings
            for (int i = 0; i < gov.PackageFindings.Count; i++)
            {
                var finding = gov.PackageFindings[i];
                var findParams = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["FindingIndex"] = i.ToString(),
                    ["DescriptorId"] = finding.Subject?.Id ?? "",
                };
                items.Add(CreateItem($"governance_finding_{i}",
                    "governance_finding", templateId, severity, findParams));
            }
        }

        var overallSeverity = items.Count > 0
            ? (DescriptorReviewSeverity)items.Max(i => (int)i.Severity)
            : DescriptorReviewSeverity.Info;

        return CreateSection(DescriptorReviewReportSectionKind.Governance, "governance",
            "Governance", 7, items.Count == 0, overallSeverity, items);
    }

    // ── Section 8: RequiredHumanReview ──────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildRequiredHumanReviewSection(DescriptorDraftReviewResult reviewResult)
    {
        var items = new List<DescriptorReviewReportItemDto>();
        var allDiagnostics = reviewResult.ValidationResult.Diagnostics
            .Concat(reviewResult.Diagnostics ?? Array.Empty<DescriptorDraftDiagnostic>())
            .OrderBy(d => d.Code)
            .ThenBy(d => d.Severity)
            .Where(d => d.Severity is DescriptorDraftDiagnosticSeverity.Blocker
                or DescriptorDraftDiagnosticSeverity.Error)
            .ToList();

        foreach (var diag in allDiagnostics)
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Reason"] = diag.Message,
                ["Code"] = diag.Code,
                ["Severity"] = diag.Severity.ToString(),
                ["DescriptorId"] = diag.DescriptorId ?? "",
                ["Path"] = diag.Path ?? "",
            };
            items.Add(CreateItem($"human_review_{diag.Code}_{items.Count}", diag.Code,
                DescriptorReviewReportMessageTemplateIds.HumanReviewRequired, MapDiagnosticSeverity(diag.Severity), parameters));
        }

        // Also check governance for human review requirement
        if (reviewResult.GovernanceDecision?.RequiresReview == true)
        {
            var govParams = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Reason"] = "Governance requires human review",
                ["Code"] = "GOV_REVIEW_REQUIRED",
                ["Severity"] = "Warning",
                ["DescriptorId"] = "",
                ["Path"] = "",
            };
            items.Add(CreateItem("human_review_governance", "GOV_REVIEW_REQUIRED",
                DescriptorReviewReportMessageTemplateIds.HumanReviewRequired, DescriptorReviewSeverity.Warning, govParams));
        }

        var overallSeverity = items.Count > 0
            ? (DescriptorReviewSeverity)items.Max(i => (int)i.Severity)
            : DescriptorReviewSeverity.Info;

        return CreateSection(DescriptorReviewReportSectionKind.RequiredHumanReview, "required_human_review",
            "Required Human Review", 8, items.Count == 0, overallSeverity, items);
    }

    // ── Section 9: ActivationEligibility ────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildActivationEligibilitySection(DescriptorDraftReviewResult reviewResult)
    {
        var items = new List<DescriptorReviewReportItemDto>();

        if (reviewResult.IsActivationEligible)
        {
            items.Add(CreateItem("activation_eligible", "activation_eligible",
                DescriptorActivationMessageTemplateIds.ActivationEligible, DescriptorReviewSeverity.Info,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }
        else
        {
            var blockerDiags = (reviewResult.Diagnostics ?? Array.Empty<DescriptorDraftDiagnostic>())
                .Where(d => d.Severity is DescriptorDraftDiagnosticSeverity.Blocker or DescriptorDraftDiagnosticSeverity.Error)
                .ToList();
            var blockingReasons = blockerDiags.Count > 0
                ? string.Join(", ", blockerDiags.Select(d => d.Code).OrderBy(c => c))
                : "Not activation eligible";

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BlockingReasons"] = blockingReasons,
            };
            items.Add(CreateItem("activation_blocked", "activation_blocked",
                DescriptorActivationMessageTemplateIds.ActivationBlocked, DescriptorReviewSeverity.Blocker, parameters));

            // Add individual blocker details (explanation only, NOT gate)
            foreach (var blocker in blockerDiags.OrderBy(d => d.Code).ThenBy(d => d.Severity))
            {
                var blockParams = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["BlockingReasons"] = $"{blocker.Code}: {blocker.Message}",
                };
                items.Add(CreateItem($"activation_blocker_{blocker.Code}_{items.Count}", blocker.Code,
                    DescriptorActivationMessageTemplateIds.ActivationBlocked,
                    MapDiagnosticSeverity(blocker.Severity), blockParams));
            }

            // Also check other factors that block activation
            if (reviewResult.GovernanceDecision?.IsBlocked == true)
            {
                var govParams = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["BlockingReasons"] = "Governance decision is blocked",
                };
                items.Add(CreateItem("activation_blocker_governance", "GOV_BLOCKED",
                    DescriptorActivationMessageTemplateIds.ActivationBlocked, DescriptorReviewSeverity.Blocker, govParams));
            }
        }

        var overallSeverity = items.Count > 0
            ? (DescriptorReviewSeverity)items.Max(i => (int)i.Severity)
            : DescriptorReviewSeverity.Info;

        return CreateSection(DescriptorReviewReportSectionKind.ActivationEligibility, "activation_eligibility",
            "Activation Eligibility", 9, items.Count == 0, overallSeverity, items);
    }

    // ── Section 10: Diagnostics ─────────────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildDiagnosticsSection(DescriptorDraftReviewResult reviewResult)
    {
        var items = new List<DescriptorReviewReportItemDto>();
        var allDiagnostics = reviewResult.ValidationResult.Diagnostics
            .Concat(reviewResult.Diagnostics ?? Array.Empty<DescriptorDraftDiagnostic>())
            .OrderBy(d => d.Code)
            .ThenBy(d => d.Severity)
            .ToList();

        foreach (var diag in allDiagnostics)
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Code"] = diag.Code,
                ["Severity"] = diag.Severity.ToString(),
                ["DescriptorId"] = diag.DescriptorId ?? "",
                ["DescriptorKind"] = diag.DescriptorKind?.ToString() ?? "",
                ["Path"] = diag.Path ?? "",
                ["DraftId"] = diag.DraftId ?? "",
                ["RelatedCode"] = diag.RelatedDiagnosticCode ?? "",
            };
            // Use diagnostic's own message as the rendered text; template catalog
            // provides structured formatting when a matching template exists.
            var message = _templateCatalog.Format(diag.Code, parameters);
            if (message.StartsWith("[Unknown template:"))
            {
                // Fallback: use the diagnostic's own message directly
                message = diag.Message;
            }

            items.Add(new DescriptorReviewReportItemDto
            {
                ItemId = $"diag_{diag.Code}_{items.Count}",
                ReasonCode = diag.Code,
                MessageTemplateId = diag.Code,
                Message = message,
                Severity = MapDiagnosticSeverity(diag.Severity),
                Parameters = parameters,
                RelatedDiagnosticIds = [diag.Code],
            });
        }

        var overallSeverity = items.Count > 0
            ? (DescriptorReviewSeverity)items.Max(i => (int)i.Severity)
            : DescriptorReviewSeverity.Info;

        return CreateSection(DescriptorReviewReportSectionKind.Diagnostics, "diagnostics",
            "Diagnostics", 10, items.Count == 0, overallSeverity, items);
    }

    // ── Section 11: Recommendations ────────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildRecommendationsSection(
        DescriptorDraftReviewResult reviewResult,
        DescriptorReviewReportSectionDto summarySection,
        DescriptorReviewReportSectionDto governanceSection,
        DescriptorReviewReportSectionDto requiredHumanReviewSection,
        DescriptorReviewReportSectionDto activationEligibilitySection,
        DescriptorReviewReportSectionDto diagnosticsSection)
    {
        var items = new List<DescriptorReviewReportItemDto>();

        // Build recommendation items based on review state
        if (reviewResult.IsActivationEligible &&
            reviewResult.GovernanceDecision?.IsAllowed == true)
        {
            items.Add(CreateItem("rec_activation_handoff", "activation_handoff",
                DescriptorReviewReportMessageTemplateIds.RecommendationActivationHandoff, DescriptorReviewSeverity.Info,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }
        else if (reviewResult.GovernanceDecision?.RequiresReview == true)
        {
            items.Add(CreateItem("rec_human_review", "human_review_required",
                DescriptorReviewReportMessageTemplateIds.RecommendationHumanReview, DescriptorReviewSeverity.Warning,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }
        else if (!requiredHumanReviewSection.IsEmpty)
        {
            items.Add(CreateItem("rec_human_review_items", "human_review_required",
                DescriptorReviewReportMessageTemplateIds.RecommendationHumanReview, DescriptorReviewSeverity.Warning,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        // Check for blocker/error diagnostics → recommend revision
        var hasBlockers = (reviewResult.ValidationResult.Diagnostics
            .Concat(reviewResult.Diagnostics ?? Array.Empty<DescriptorDraftDiagnostic>()))
            .Any(d => d.Severity is DescriptorDraftDiagnosticSeverity.Blocker
                or DescriptorDraftDiagnosticSeverity.Error);

        if (hasBlockers)
        {
            items.Add(CreateItem("rec_revise_draft", "draft_needs_revision",
                DescriptorReviewReportMessageTemplateIds.RecommendationReviseDraft, DescriptorReviewSeverity.Blocker,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        // Check for warnings with fix proposals available
        var hasWarnings = (reviewResult.ValidationResult.Diagnostics
            .Concat(reviewResult.Diagnostics ?? Array.Empty<DescriptorDraftDiagnostic>()))
            .Any(d => d.Severity == DescriptorDraftDiagnosticSeverity.Warning);

        if (hasWarnings && !hasBlockers)
        {
            items.Add(CreateItem("rec_apply_fix", "fix_proposal_available",
                DescriptorReviewReportMessageTemplateIds.RecommendationApplyFix, DescriptorReviewSeverity.Warning,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["FixProposalId"] = "", // Populated by FixProposal system when available
                }));
        }

        // If no issues at all → NoAction
        if (items.Count == 0)
        {
            items.Add(CreateItem("rec_no_action", "no_action_required",
                DescriptorReviewReportMessageTemplateIds.RecommendationNoAction, DescriptorReviewSeverity.Info,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        var overallSeverity = items.Count > 0
            ? (DescriptorReviewSeverity)items.Max(i => (int)i.Severity)
            : DescriptorReviewSeverity.Info;

        return CreateSection(DescriptorReviewReportSectionKind.Recommendations, "recommendations",
            "Recommendations", 11, items.Count == 0, overallSeverity, items);
    }

    // ── Section 12: PackagePreview ──────────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildPackagePreviewSection(DescriptorDraftReviewResult reviewResult)
    {
        var items = new List<DescriptorReviewReportItemDto>();
        var preview = reviewResult.PackagePreview;

        if (preview != null)
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DescriptorCount"] = preview.DescriptorIds.Count.ToString(),
                ["HashCount"] = "3", // ManifestHash, EvidenceHash, EnvelopeHash
                ["ManifestHash"] = preview.PackageManifestHash?.Value ?? "",
                ["EvidenceHash"] = preview.PackageEvidenceHash?.Value ?? "",
                ["EnvelopeHash"] = preview.PackageEvidenceEnvelopeHash?.Value ?? "",
            };
            items.Add(CreateItem("package_preview_present", "package_preview_available",
                DescriptorReviewReportMessageTemplateIds.PackagePreviewPresent, DescriptorReviewSeverity.Info, parameters));
        }
        else
        {
            items.Add(CreateItem("package_preview_none", "package_preview_missing",
                DescriptorReviewReportMessageTemplateIds.PackagePreviewNone, DescriptorReviewSeverity.Info,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        return CreateSection(DescriptorReviewReportSectionKind.PackagePreview, "package_preview",
            "Package Preview", 12, items.Count == 0, DescriptorReviewSeverity.Info, items);
    }

    // ── Section 13: StableHashes ────────────────────────────────────────────

    private DescriptorReviewReportSectionDto BuildStableHashesSection(DescriptorDraftReviewResult reviewResult)
    {
        var items = new List<DescriptorReviewReportItemDto>();
        var hashes = reviewResult.StableHashes;

        if (hashes != null)
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HashCount"] = "2", // ContractHash + DefinitionHash
                ["ContractHash"] = hashes.ContractHash.Value,
                ["DefinitionHash"] = hashes.DefinitionHash.Value,
                ["RuntimeHash"] = hashes.RuntimeHash?.Value ?? "",
                ["BindingHash"] = hashes.BindingHash?.Value ?? "",
            };
            items.Add(CreateItem("stable_hashes_present", "stable_hashes_available",
                DescriptorReviewReportMessageTemplateIds.StableHashesPresent, DescriptorReviewSeverity.Info, parameters));
        }
        else
        {
            items.Add(CreateItem("stable_hashes_none", "stable_hashes_missing",
                DescriptorReviewReportMessageTemplateIds.StableHashesNone, DescriptorReviewSeverity.Info,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        return CreateSection(DescriptorReviewReportSectionKind.StableHashes, "stable_hashes",
            "Stable Hashes", 13, items.Count == 0, DescriptorReviewSeverity.Info, items);
    }

    // ── Top-level Recommendations ───────────────────────────────────────────

    private static IReadOnlyList<DescriptorReviewRecommendationDto> DeriveTopLevelRecommendations(
        DescriptorDraftReviewResult reviewResult,
        DescriptorReviewReportSectionDto recommendationsSection,
        DescriptorReviewReportSectionDto governanceSection,
        DescriptorReviewReportSectionDto diagnosticsSection,
        DescriptorReviewReportSectionDto requiredHumanReviewSection,
        DescriptorReviewReportSectionDto activationEligibilitySection)
    {
        var recommendations = new List<DescriptorReviewRecommendationDto>();

        // 1. If activation eligible && governance approved → RequestActivationHandoff
        if (reviewResult.IsActivationEligible &&
            reviewResult.GovernanceDecision?.IsAllowed == true)
        {
            recommendations.Add(new DescriptorReviewRecommendationDto
            {
                RecommendationId = "rec_activation_handoff",
                ReasonCode = "activation_eligible",
                Message = "Draft is eligible for activation handoff.",
                Kind = DescriptorReviewRecommendationKind.RequestActivationHandoff,
                IsActionable = true,
                RelatedItemIds = ["rec_activation_handoff"],
            });
        }

        // 2. If governance requires review → RequestHumanReview
        if (reviewResult.GovernanceDecision?.RequiresReview == true)
        {
            recommendations.Add(new DescriptorReviewRecommendationDto
            {
                RecommendationId = "rec_human_review_governance",
                ReasonCode = "governance_review_required",
                Message = "Governance requires human review before proceeding.",
                Kind = DescriptorReviewRecommendationKind.RequestHumanReview,
                IsActionable = true,
                RelatedItemIds = ["rec_human_review"],
            });
        }

        // 3. If diagnostics contain Blocker/Error → ReviseDraft
        var hasBlockers = (reviewResult.ValidationResult.Diagnostics
            .Concat(reviewResult.Diagnostics ?? Array.Empty<DescriptorDraftDiagnostic>()))
            .Any(d => d.Severity is DescriptorDraftDiagnosticSeverity.Blocker
                or DescriptorDraftDiagnosticSeverity.Error);

        if (hasBlockers)
        {
            recommendations.Add(new DescriptorReviewRecommendationDto
            {
                RecommendationId = "rec_revise_draft",
                ReasonCode = "blocker_diagnostics",
                Message = "Draft needs revision due to blocker/error diagnostics.",
                Kind = DescriptorReviewRecommendationKind.ReviseDraft,
                IsActionable = true,
                RelatedItemIds = ["rec_revise_draft"],
            });
        }

        // 4. If diagnostics contain Warning && fix proposals available → ApplyFixProposal
        //    (Only if no blockers are present — blockers take priority)
        var hasWarnings = (reviewResult.ValidationResult.Diagnostics
            .Concat(reviewResult.Diagnostics ?? Array.Empty<DescriptorDraftDiagnostic>()))
            .Any(d => d.Severity == DescriptorDraftDiagnosticSeverity.Warning);

        if (hasWarnings && !hasBlockers)
        {
            recommendations.Add(new DescriptorReviewRecommendationDto
            {
                RecommendationId = "rec_apply_fix",
                ReasonCode = "warning_diagnostics",
                Message = "Fix proposal available for warning-level diagnostics.",
                Kind = DescriptorReviewRecommendationKind.ApplyFixProposal,
                IsActionable = true,
                RelatedItemIds = ["rec_apply_fix"],
            });
        }

        // 5. If no issues → NoAction
        if (recommendations.Count == 0)
        {
            recommendations.Add(new DescriptorReviewRecommendationDto
            {
                RecommendationId = "rec_no_action",
                ReasonCode = "no_issues",
                Message = "No action required at this time.",
                Kind = DescriptorReviewRecommendationKind.NoAction,
                IsActionable = false,
                RelatedItemIds = [],
            });
        }

        return recommendations.AsReadOnly();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private DescriptorReviewReportItemDto CreateItem(
        string itemId,
        string reasonCode,
        string messageTemplateId,
        DescriptorReviewSeverity severity,
        IReadOnlyDictionary<string, string> parameters)
    {
        var message = _templateCatalog.Format(messageTemplateId, parameters);
        return new DescriptorReviewReportItemDto
        {
            ItemId = itemId,
            ReasonCode = reasonCode,
            MessageTemplateId = messageTemplateId,
            Message = message,
            Severity = severity,
            Parameters = parameters,
        };
    }

    private static DescriptorReviewReportSectionDto CreateSection(
        DescriptorReviewReportSectionKind kind,
        string sectionId,
        string title,
        int order,
        bool isEmpty,
        DescriptorReviewSeverity overallSeverity,
        IReadOnlyList<DescriptorReviewReportItemDto> items)
    {
        return new DescriptorReviewReportSectionDto
        {
            Kind = kind,
            SectionId = sectionId,
            Title = title,
            Order = order,
            IsEmpty = isEmpty,
            OverallSeverity = overallSeverity,
            Items = items,
        };
    }

    // ── Severity Mappings ───────────────────────────────────────────────────

    private static DescriptorReviewSeverity MapDiagnosticSeverity(DescriptorDraftDiagnosticSeverity severity)
    {
        return severity switch
        {
            DescriptorDraftDiagnosticSeverity.Info => DescriptorReviewSeverity.Info,
            DescriptorDraftDiagnosticSeverity.Warning => DescriptorReviewSeverity.Warning,
            DescriptorDraftDiagnosticSeverity.Error => DescriptorReviewSeverity.Error,
            DescriptorDraftDiagnosticSeverity.Blocker => DescriptorReviewSeverity.Blocker,
            _ => DescriptorReviewSeverity.Info,
        };
    }

    private static DescriptorReviewSeverity MapMaxSeverity(IReadOnlyList<DescriptorDraftDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
            return DescriptorReviewSeverity.Info;

        return diagnostics.Max(d => MapDiagnosticSeverity(d.Severity));
    }

    private static DescriptorReviewSeverity MapImpactSeverity(DescriptorImpactSeverity severity)
    {
        return severity switch
        {
            DescriptorImpactSeverity.None => DescriptorReviewSeverity.Info,
            DescriptorImpactSeverity.Info => DescriptorReviewSeverity.Info,
            DescriptorImpactSeverity.Low => DescriptorReviewSeverity.Warning,
            DescriptorImpactSeverity.Medium => DescriptorReviewSeverity.Warning,
            DescriptorImpactSeverity.High => DescriptorReviewSeverity.Error,
            DescriptorImpactSeverity.Critical => DescriptorReviewSeverity.Blocker,
            _ => DescriptorReviewSeverity.Info,
        };
    }

    private static DescriptorReviewSeverity MapCompatibilityLevel(DescriptorCompatibilityLevel level)
    {
        return level switch
        {
            DescriptorCompatibilityLevel.Compatible => DescriptorReviewSeverity.Info,
            DescriptorCompatibilityLevel.Risky => DescriptorReviewSeverity.Warning,
            DescriptorCompatibilityLevel.SecuritySensitive => DescriptorReviewSeverity.Error,
            DescriptorCompatibilityLevel.Breaking => DescriptorReviewSeverity.Blocker,
            DescriptorCompatibilityLevel.Unsupported => DescriptorReviewSeverity.Error,
            _ => DescriptorReviewSeverity.Info,
        };
    }
}
