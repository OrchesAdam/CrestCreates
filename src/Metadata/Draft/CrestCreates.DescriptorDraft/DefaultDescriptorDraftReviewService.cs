using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.Abstractions.Registry;
using Microsoft.Extensions.Logging;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft;

public sealed class DefaultDescriptorDraftReviewService : IDescriptorDraftReviewService
{
    private readonly IDescriptorDraftValidator _validator;
    private readonly IDescriptorDraftMaterializer _materializer;
    private readonly IDescriptorRelationshipProvider _relationshipProvider;
    private readonly IDescriptorTopologyBuilder _topologyBuilder;
    private readonly IDescriptorImpactAnalyzer _impactAnalyzer;
    private readonly IDescriptorChangeSetBuilder _changeSetBuilder;
    private readonly IDescriptorCompatibilityAnalyzer _compatibilityAnalyzer;
    private readonly IDescriptorLifecycleGovernanceService _lifecycleGovernance;
    private readonly IDescriptorStableHashBuilder _stableHashBuilder;
    private readonly IDescriptorPackageBuilder _packageBuilder;
    private readonly ILogger<DefaultDescriptorDraftReviewService> _logger;

    public DefaultDescriptorDraftReviewService(
        IDescriptorDraftValidator validator,
        IDescriptorDraftMaterializer materializer,
        IDescriptorRelationshipProvider relationshipProvider,
        IDescriptorTopologyBuilder topologyBuilder,
        IDescriptorImpactAnalyzer impactAnalyzer,
        IDescriptorChangeSetBuilder changeSetBuilder,
        IDescriptorCompatibilityAnalyzer compatibilityAnalyzer,
        IDescriptorLifecycleGovernanceService lifecycleGovernance,
        IDescriptorStableHashBuilder stableHashBuilder,
        IDescriptorPackageBuilder packageBuilder,
        ILogger<DefaultDescriptorDraftReviewService> logger)
    {
        _validator = validator;
        _materializer = materializer;
        _relationshipProvider = relationshipProvider;
        _topologyBuilder = topologyBuilder;
        _impactAnalyzer = impactAnalyzer;
        _changeSetBuilder = changeSetBuilder;
        _compatibilityAnalyzer = compatibilityAnalyzer;
        _lifecycleGovernance = lifecycleGovernance;
        _stableHashBuilder = stableHashBuilder;
        _packageBuilder = packageBuilder;
        _logger = logger;
    }

    public Task<DescriptorDraftReviewResult> ReviewAsync(
        Draft draft,
        IReadOnlyList<IDescriptor> currentInventory,
        CancellationToken ct = default)
    {
        var diagnostics = new List<DescriptorDraftDiagnostic>();

        // ── Phase 1: Validation ──
        var validationResult = _validator.Validate(draft);
        if (!validationResult.IsValid)
        {
            LogEarlyStop(draft.DraftId, "validation");
            return Task.FromResult(EarlyStopResult(draft, validationResult, null, validationResult.Diagnostics));
        }

        // ── Phase 2: Materialization ──
        var materializationResult = _materializer.Materialize(draft, currentInventory);
        if (!materializationResult.IsMaterialized)
        {
            diagnostics.AddRange(validationResult.Diagnostics);
            diagnostics.AddRange(materializationResult.Diagnostics);
            LogEarlyStop(draft.DraftId, "materialization");
            return Task.FromResult(EarlyStopResult(draft, validationResult, materializationResult, diagnostics));
        }

        diagnostics.AddRange(validationResult.Diagnostics);
        diagnostics.AddRange(materializationResult.Diagnostics);

        var proposedInventory = materializationResult.ProposedInventory;

        // ── Phase 6: Control Plane Pipeline ──

        // 6a: Topology
        DescriptorTopologySnapshot? topologySnapshot = null;
        try
        {
            topologySnapshot = _topologyBuilder.Build(proposedInventory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DescriptorDraftReview: Topology build failed for draft {DraftId}", draft.DraftId);
            diagnostics.Add(Diag("REVIEW_TOPOLOGY_FAILED", DescriptorDraftDiagnosticSeverity.Error,
                $"Topology build failed: {ex.Message}", draft.DraftId));
        }

        // 6b: Change set
        DescriptorChangeSet? changeSet = null;
        try
        {
            changeSet = _changeSetBuilder.Build(currentInventory, proposedInventory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DescriptorDraftReview: ChangeSet build failed for draft {DraftId}", draft.DraftId);
            diagnostics.Add(Diag("REVIEW_CHANGESET_FAILED", DescriptorDraftDiagnosticSeverity.Error,
                $"ChangeSet build failed: {ex.Message}", draft.DraftId));
        }

        // 6c: Impact analysis
        DescriptorImpactAnalysisReport? impactResult = null;
        if (topologySnapshot is not null && changeSet is not null)
        {
            try
            {
                impactResult = _impactAnalyzer.Analyze(topologySnapshot, changeSet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DescriptorDraftReview: Impact analysis failed for draft {DraftId}", draft.DraftId);
                diagnostics.Add(Diag("REVIEW_IMPACT_FAILED", DescriptorDraftDiagnosticSeverity.Error,
                    $"Impact analysis failed: {ex.Message}", draft.DraftId));
            }
        }

        // 6d: Compatibility analysis
        DescriptorCompatibilityReport? compatibilityResult = null;
        if (changeSet is not null && impactResult is not null)
        {
            try
            {
                compatibilityResult = _compatibilityAnalyzer.Analyze(
                    currentInventory, proposedInventory, changeSet, impactResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DescriptorDraftReview: Compatibility analysis failed for draft {DraftId}", draft.DraftId);
                diagnostics.Add(Diag("REVIEW_COMPAT_FAILED", DescriptorDraftDiagnosticSeverity.Error,
                    $"Compatibility analysis failed: {ex.Message}", draft.DraftId));
            }
        }

        // 6e: Lifecycle governance
        DescriptorLifecycleGovernanceReport? governanceDecision = null;
        try
        {
            var request = BuildGovernanceRequest(
                draft, validationResult, topologySnapshot, impactResult, compatibilityResult);
            governanceDecision = _lifecycleGovernance.Evaluate(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DescriptorDraftReview: Governance evaluation failed for draft {DraftId}", draft.DraftId);
            diagnostics.Add(Diag("REVIEW_GOVERNANCE_FAILED", DescriptorDraftDiagnosticSeverity.Error,
                $"Governance evaluation failed: {ex.Message}", draft.DraftId));
        }

        // 6g: Stable hashes (optional, best-effort)
        DescriptorStableHashes? stableHashes = null;
        try
        {
            var payloadDescriptor = draft.Payload.GetDescriptor();
            stableHashes = _stableHashBuilder.Build(payloadDescriptor);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DescriptorDraftReview: Stable hash build failed for draft {DraftId}", draft.DraftId);
        }

        // 6f: Package preview — build from proposed inventory + available reports
        DescriptorPackagePreview? packagePreview = null;
        try
        {
            var pkgRequest = new DescriptorPackageBuildRequest
            {
                PackageId = draft.DraftId,
                PackageVersion = draft.ProposedVersion ?? "1",
                Name = draft.Intent,
                CreatedBy = draft.AuthorId,
                Source = draft.Source,
                CreatedAt = draft.CreatedAt,
                Descriptors = proposedInventory,
                TopologySnapshot = topologySnapshot,
                ImpactReport = impactResult,
                CompatibilityReport = compatibilityResult,
                GovernanceReport = governanceDecision
            };
            var pkg = _packageBuilder.Build(pkgRequest);
            packagePreview = new DescriptorPackagePreview
            {
                PackageManifestHash = pkg.Hashes?.PackageManifestHash,
                PackageEvidenceHash = pkg.Hashes?.PackageEvidenceHash,
                PackageEvidenceEnvelopeHash = pkg.Hashes?.PackageEvidenceEnvelopeHash,
                DescriptorIds = pkg.Manifest.DescriptorEntries
                    .Select(e => e.Ref.Id)
                    .ToList().AsReadOnly()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DescriptorDraftReview: Package preview build failed for draft {DraftId}", draft.DraftId);
            // Non-blocking — package preview is best-effort
        }

        bool isActivationEligible = governanceDecision?.IsAllowed ?? false;

        // Phase 6 blockers must suppress eligibility
        if (isActivationEligible && diagnostics.Any(d =>
                d.Severity is DescriptorDraftDiagnosticSeverity.Error or DescriptorDraftDiagnosticSeverity.Blocker))
        {
            isActivationEligible = false;
        }

        return Task.FromResult(new DescriptorDraftReviewResult
        {
            DraftId = draft.DraftId,
            TenantId = draft.TenantId,
            ValidationResult = validationResult,
            MaterializationResult = materializationResult,
            ProposedInventory = proposedInventory,
            TopologySnapshot = topologySnapshot,
            ImpactAnalysisResult = impactResult,
            CompatibilityResult = compatibilityResult,
            GovernanceDecision = governanceDecision,
            StableHashes = stableHashes,
            PackagePreview = packagePreview,
            Diagnostics = diagnostics,
            IsActivationEligible = isActivationEligible
        });
    }

    private static DescriptorDraftReviewResult EarlyStopResult(
        Draft draft,
        DescriptorDraftValidationResult validationResult,
        DescriptorDraftMaterializationResult? materializationResult,
        IReadOnlyList<DescriptorDraftDiagnostic> diagnostics)
    {
        return new DescriptorDraftReviewResult
        {
            DraftId = draft.DraftId,
            TenantId = draft.TenantId,
            ValidationResult = validationResult,
            MaterializationResult = materializationResult,
            Diagnostics = diagnostics,
            IsActivationEligible = false
        };
    }

    private static DescriptorLifecycleGovernanceRequest BuildGovernanceRequest(
        Draft draft,
        DescriptorDraftValidationResult validationResult,
        DescriptorTopologySnapshot? topologySnapshot,
        DescriptorImpactAnalysisReport? impactResult,
        DescriptorCompatibilityReport? compatibilityResult)
    {
        var payloadDescriptor = draft.Payload.GetDescriptor();
        var subjectRef = new DescriptorRef(payloadDescriptor.Namespace, draft.DescriptorId);

        var transition = new DescriptorLifecycleTransition
        {
            Subject = subjectRef,
            Operation = MapToLifecycleOperation(draft.Operation),
            FromState = GetFromState(draft.Operation),
            ToState = GetToState(draft.Operation),
            Reason = draft.Rationale
        };

        var validationReport = ConvertToValidationReport(validationResult);

        var topologyDiagnostics = topologySnapshot?.Diagnostics
            ?? new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() };

        var defaultImpact = new DescriptorImpactAnalysisReport
        {
            ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
            AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = DescriptorImpactSeverity.None,
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };

        var defaultCompatibility = new DescriptorCompatibilityReport
        {
            ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
            ImpactReport = defaultImpact,
            Findings = Array.Empty<DescriptorCompatibilityFinding>(),
            MaxLevel = DescriptorCompatibilityLevel.Compatible,
            Diagnostics = Array.Empty<DescriptorCompatibilityDiagnostic>()
        };

        return new DescriptorLifecycleGovernanceRequest
        {
            Transitions = new[] { transition },
            ValidationReport = validationReport,
            BindingReport = new RuntimeBindingReport(),
            TopologyDiagnostics = topologyDiagnostics,
            ImpactReport = impactResult ?? defaultImpact,
            CompatibilityReport = compatibilityResult ?? defaultCompatibility
        };
    }

    private static ValidationReport ConvertToValidationReport(DescriptorDraftValidationResult result)
    {
        var issues = result.Diagnostics
            .Select(d => new ValidationIssue(MapSeverity(d.Severity), d.Message))
            .ToList()
            .AsReadOnly();

        return new ValidationReport(issues);
    }

    private static ValidationSeverity MapSeverity(DescriptorDraftDiagnosticSeverity severity) => severity switch
    {
        DescriptorDraftDiagnosticSeverity.Error or DescriptorDraftDiagnosticSeverity.Blocker => ValidationSeverity.Error,
        DescriptorDraftDiagnosticSeverity.Warning => ValidationSeverity.Warning,
        _ => ValidationSeverity.Info
    };

    private static DescriptorLifecycleOperation MapToLifecycleOperation(DescriptorDraftOperation operation) => operation switch
    {
        DescriptorDraftOperation.Create => DescriptorLifecycleOperation.Activate,
        DescriptorDraftOperation.Update => DescriptorLifecycleOperation.Activate,
        DescriptorDraftOperation.Deprecate => DescriptorLifecycleOperation.Deprecate,
        DescriptorDraftOperation.Remove => DescriptorLifecycleOperation.Retire,
        _ => DescriptorLifecycleOperation.ValidateDraft
    };

    private static DescriptorState? GetFromState(DescriptorDraftOperation operation) => operation switch
    {
        DescriptorDraftOperation.Create => null,
        DescriptorDraftOperation.Update => DescriptorState.Active,
        DescriptorDraftOperation.Deprecate => DescriptorState.Active,
        DescriptorDraftOperation.Remove => DescriptorState.Active,
        _ => DescriptorState.Draft
    };

    private static DescriptorState? GetToState(DescriptorDraftOperation operation) => operation switch
    {
        DescriptorDraftOperation.Create => DescriptorState.Active,
        DescriptorDraftOperation.Update => DescriptorState.Active,
        DescriptorDraftOperation.Deprecate => DescriptorState.Deprecated,
        DescriptorDraftOperation.Remove => DescriptorState.Removed,
        _ => null
    };

    private void LogEarlyStop(string draftId, string phase)
    {
        _logger.LogDebug("DescriptorDraftReview: Early stop after {Phase} for draft {DraftId}", phase, draftId);
    }

    private static DescriptorDraftDiagnostic Diag(string code, DescriptorDraftDiagnosticSeverity severity,
        string message, string? draftId = null)
        => new() { Code = code, Severity = severity, Message = message, DraftId = draftId };
}
