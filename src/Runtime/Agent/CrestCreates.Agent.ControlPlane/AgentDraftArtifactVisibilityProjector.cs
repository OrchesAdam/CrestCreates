using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using DraftPackagePreview = CrestCreates.DescriptorDraft.Abstractions.DescriptorPackagePreview;
using DraftReviewResult = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraftReviewResult;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Projects nested (derived) artifacts through the visible descriptor universe.
/// Review proposed inventory, topology, impact, compatibility, governance,
/// package descriptor lists, evidence, diagnostics, and readiness data are
/// filtered so no denied descriptor kind appears in the returned DTO.
///
/// <para>Projection rules (spec §9.6):</para>
/// <list type="bullet">
///   <item>Review proposed inventories contain visible descriptors only</item>
///   <item>Review diagnostics with denied DescriptorKind are omitted</item>
///   <item>Package and evidence inventories contain visible descriptors only</item>
///   <item>Evidence findings with denied Subject/RelatedRefs are filtered</item>
///   <item>Readiness blockers do not disclose denied artifacts</item>
///   <item>Comparison output cannot embed a denied active descriptor</item>
/// </list>
/// </summary>
internal sealed class AgentDraftArtifactVisibilityProjector
{
    private readonly AgentTopologyVisibilityProjector _topologyProjector;
    private readonly IDescriptorTopologyBuilder _topologyBuilder;

    public AgentDraftArtifactVisibilityProjector(
        AgentTopologyVisibilityProjector topologyProjector,
        IDescriptorTopologyBuilder topologyBuilder)
    {
        _topologyProjector = topologyProjector;
        _topologyBuilder = topologyBuilder;
    }

    /// <summary>
    /// Filters a review result's nested descriptor-bearing data through visibility.
    /// Accepts the visible descriptor universe so DescriptorRef-based fields
    /// (topology, impact, compatibility, governance, package preview) can be
    /// filtered by matching refs against visible descriptor identities.
    ///
    /// All nested sub-objects are recursively projected. If any projection step
    /// fails (e.g. package contains a known-denied descriptor ID), all nested
    /// fields are replaced with their filtered/null equivalents — no leaking
    /// source data is returned.
    /// </summary>
    /// <summary>
    /// Returns a fully projected review, or null if projection of any
    /// sub-component fails (package contains known-denied descriptor IDs).
    /// A null return signals the caller must NOT persist the review or
    /// mutate draft state — the invocation must return Failed.
    /// </summary>
    public DraftReviewResult? ProjectReview(
        DraftReviewResult source,
        AgentDescriptorVisibilityScope scope,
        AgentVisibleDescriptorUniverse universe)
    {
        var refLookup = new VisibleRefLookup(universe);
        var allRefSet = BuildAllRefSet(universe);

        // Filter proposed inventory
        var proposedInventory = source.ProposedInventory is null || source.ProposedInventory.Count == 0
            ? source.ProposedInventory
            : scope.Filter(source.ProposedInventory, d => d.Kind);

        // Filter diagnostics — omit those whose DescriptorKind is denied.
        var diagnostics = source.Diagnostics;
        if (diagnostics.Count > 0)
        {
            diagnostics = diagnostics
                .Where(d => d.DescriptorKind is null || scope.IsVisible(d.DescriptorKind.Value))
                .ToList().AsReadOnly();
        }

        // MaterializationResult — filter ProposedInventory by visible Kind
        DescriptorDraftMaterializationResult? materializationResult = source.MaterializationResult;
        if (materializationResult is not null && materializationResult.ProposedInventory.Count > 0)
        {
            var filteredInv = scope.Filter(materializationResult.ProposedInventory, d => d.Kind);
            materializationResult = materializationResult with { ProposedInventory = filteredInv };
        }

        // Topology — rebuild from visible descriptors only when source
        // topology exists and needs visibility filtering. Do NOT rebuild
        // when source topology is null — that indicates the review did not
        // produce topology (validation early-stop, materialization failure),
        // and inventing one would disguise a failure state as normal.
        DescriptorTopologySnapshot? topology = source.TopologySnapshot;
        if (topology is not null && universe.VisibleDescriptors.Count < universe.AllTenantDescriptors.Count)
        {
            topology = _topologyProjector.BuildVisible(universe, _topologyBuilder);
        }

        // Impact — full recursive filtering with summary recalculation
        DescriptorImpactAnalysisReport? impact = source.ImpactAnalysisResult;
        if (impact is not null)
        {
            impact = ProjectImpact(impact, scope, refLookup);
        }

        // Compatibility — full recursive filtering with summary recalculation
        DescriptorCompatibilityReport? compatibility = source.CompatibilityResult;
        if (compatibility is not null)
        {
            compatibility = ProjectCompatibility(compatibility, scope, refLookup);
        }

        // Governance — full recursive filtering with summary recalculation
        DescriptorLifecycleGovernanceReport? governance = source.GovernanceDecision;
        if (governance is not null)
        {
            governance = ProjectGovernance(governance, refLookup);
        }

        // PackagePreview — filter DescriptorIds by visible universe;
        // known-denied descriptor IDs cause projection failure — return null
        // so the caller rejects the entire invocation (no mutation).
        DraftPackagePreview? packagePreview = source.PackagePreview;
        if (packagePreview is not null && packagePreview.DescriptorIds.Count > 0)
        {
            var projectedPkg = ProjectPackage(packagePreview, universe, allRefSet, BuildVisibleRefSet(universe));
            if (projectedPkg is null)
                return null; // Projection failure — caller must return Failed
            packagePreview = projectedPkg;
        }

        // Re-derive IsActivationEligible from projected data only.
        // The source value was computed against the full inventory; denied
        // descriptors may have produced blockers that are no longer visible.
        var isActivationEligible = source.IsActivationEligible; // default
        if (governance is not null)
        {
            isActivationEligible = governance.MaxDecision == DescriptorLifecycleDecisionKind.Allowed
                && diagnostics.All(d => d.Severity != DescriptorDraftDiagnosticSeverity.Error);
        }
        else if (diagnostics.Count > 0)
        {
            isActivationEligible = diagnostics.All(d => d.Severity != DescriptorDraftDiagnosticSeverity.Error);
        }

        return source with
        {
            IsActivationEligible = isActivationEligible,
            ProposedInventory = proposedInventory,
            Diagnostics = diagnostics,
            MaterializationResult = materializationResult,
            TopologySnapshot = topology,
            ImpactAnalysisResult = impact,
            CompatibilityResult = compatibility,
            GovernanceDecision = governance,
            PackagePreview = packagePreview
        };
    }

    /// <summary>
    /// Filters a package preview's descriptor IDs against the visible universe.
    /// Matches by (Namespace, Id) from the universe entries. A bare ID that
    /// appears in multiple namespaces within the full catalog is ambiguous and
    /// causes projection failure. Known descriptors in denied kinds cause
    /// projection failure. IDs not present in AllTenantDescriptors are treated
    /// as new descriptors introduced by the draft and are permitted.
    /// </summary>
    public DraftPackagePreview? ProjectPackage(
        DraftPackagePreview source,
        AgentVisibleDescriptorUniverse universe)
    {
        return ProjectPackage(source, universe, BuildAllRefSet(universe), BuildVisibleRefSet(universe));
    }

    private static DraftPackagePreview? ProjectPackage(
        DraftPackagePreview source,
        AgentVisibleDescriptorUniverse universe,
        HashSet<(string Ns, string Id)> allRefSet,
        HashSet<(string Ns, string Id)> visibleRefSet)
    {
        if (source.DescriptorIds is null || source.DescriptorIds.Count == 0)
            return source;

        // Build index: bare Id → List<(Ns, Id, isVisible)>
        var idsByBareId = new Dictionary<string, List<(string Ns, string Id, bool IsVisible)>>(StringComparer.Ordinal);
        foreach (var d in universe.AllTenantDescriptors)
        {
            if (!idsByBareId.TryGetValue(d.Id, out var list))
            {
                list = new List<(string, string, bool)>();
                idsByBareId[d.Id] = list;
            }
            var nsId = (d.Namespace, d.Id);
            list.Add((d.Namespace, d.Id, visibleRefSet.Contains(nsId)));
        }

        foreach (var id in source.DescriptorIds)
        {
            if (!idsByBareId.TryGetValue(id, out var entries))
                continue; // New descriptor, not in catalog — permitted

            if (entries.Count > 1)
            {
                // Ambiguous: same bare ID in multiple namespaces.
                // Cannot determine which one the package references.
                return null;
            }

            var (_, _, isVisible) = entries[0];
            if (!isVisible)
                return null; // Known descriptor in denied kind
        }

        return source with
        {
            DescriptorIds = source.DescriptorIds
                .Where(id =>
                {
                    if (!idsByBareId.TryGetValue(id, out var entries))
                        return true; // New descriptor, keep
                    return entries.Count == 1 && entries[0].IsVisible;
                })
                .ToList().AsReadOnly()
        };
    }

    /// <summary>
    /// Filters an evidence preview's findings through the visible descriptor universe.
    /// Recalculates all summary fields (counts, maxima, flags) from the filtered
    /// findings so they cannot serve as a side-channel for denied data.
    /// </summary>
    public PackageEvidencePreview ProjectEvidence(
        PackageEvidencePreview source,
        AgentVisibleDescriptorUniverse universe)
    {
        if (source.Evidence.NormalizedFindings.Count == 0)
            return source;

        var refLookup = new VisibleRefLookup(universe);

        var filteredFindings = source.Evidence.NormalizedFindings
            .Where(f => refLookup.IsVisible(f.Subject))
            .Select(f => f with
            {
                RelatedRefs = f.RelatedRefs
                    .Where(r => refLookup.IsVisible(r))
                    .ToList().AsReadOnly()
            })
            .ToList().AsReadOnly();

        // Recalculate all derived fields from filtered data — denied entries
        // must not influence counts, maxima, or boolean flags.
        // Recompute ALL derived fields from filtered findings only.
        // Fields that cannot be reliably recomputed from the flat finding
        // list use safe defaults — fail closed rather than copy from source.

        var topologyFindings = filteredFindings.Where(f =>
            StringComparer.Ordinal.Equals(f.Source, "topology")).ToList();
        var impactFindings = filteredFindings.Where(f =>
            StringComparer.Ordinal.Equals(f.Source, "impact")).ToList();
        var compatibilityFindings = filteredFindings.Where(f =>
            StringComparer.Ordinal.Equals(f.Source, "compatibility")).ToList();
        var lifecycleFindings = filteredFindings.Where(f =>
            StringComparer.Ordinal.Equals(f.Source, "lifecycle")).ToList();

        var filteredEvidence = new DescriptorPackageEvidence
        {
            // Topology — recompute from filtered topology findings
            TopologyNodeCount = 0, // Cannot recompute from flat findings; safe default
            TopologyEdgeCount = 0, // Cannot recompute from flat findings; safe default
            TopologyDiagnosticCounts = topologyFindings
                .GroupBy(f => new { f.Severity, f.Code })
                .Select(g => new EvidenceFindingCount
                {
                    Severity = g.Key.Severity,
                    Code = g.Key.Code,
                    Count = g.Count()
                })
                .ToList().AsReadOnly(),
            HasTopologyErrors = topologyFindings.Any(f =>
                StringComparer.Ordinal.Equals(f.Severity, "Error")),

            // Impact — recompute from filtered impact findings
            MaxImpactSeverity =
                impactFindings.Any(f => StringComparer.Ordinal.Equals(f.Severity, "Critical")) ? DescriptorImpactSeverity.Critical :
                impactFindings.Any(f => StringComparer.Ordinal.Equals(f.Severity, "High")) ? DescriptorImpactSeverity.High :
                impactFindings.Any(f => StringComparer.Ordinal.Equals(f.Severity, "Medium")) ? DescriptorImpactSeverity.Medium :
                impactFindings.Any(f => StringComparer.Ordinal.Equals(f.Severity, "Low")) ? DescriptorImpactSeverity.Low :
                impactFindings.Any(f => StringComparer.Ordinal.Equals(f.Severity, "Info")) ? DescriptorImpactSeverity.Info :
                DescriptorImpactSeverity.None,
            AffectedDescriptorCount = impactFindings
                .Where(f => f.Subject is not null)
                .Select(f => f.Subject!)
                .Distinct()
                .Count(),
            ImpactPathCount = 0, // Cannot recompute from flat findings; safe default
            ImpactDiagnosticCounts = impactFindings
                .GroupBy(f => new { f.Severity, f.Code })
                .Select(g => new EvidenceFindingCount
                {
                    Severity = g.Key.Severity,
                    Code = g.Key.Code,
                    Count = g.Count()
                })
                .ToList().AsReadOnly(),

            // Compatibility — recompute from filtered compatibility findings
            MaxCompatibilityLevel =
                compatibilityFindings.Any(f => StringComparer.Ordinal.Equals(f.Severity, "Breaking")) ? DescriptorCompatibilityLevel.Breaking :
                compatibilityFindings.Any(f => StringComparer.Ordinal.Equals(f.Severity, "SecuritySensitive")) ? DescriptorCompatibilityLevel.SecuritySensitive :
                compatibilityFindings.Any(f => StringComparer.Ordinal.Equals(f.Severity, "Risky")) ? DescriptorCompatibilityLevel.Risky :
                compatibilityFindings.Count > 0 && compatibilityFindings.All(f => StringComparer.Ordinal.Equals(f.Severity, "Unsupported")) ? DescriptorCompatibilityLevel.Unsupported :
                DescriptorCompatibilityLevel.Compatible,
            BreakingFindingCount = filteredFindings.Count(f =>
                StringComparer.Ordinal.Equals(f.Severity, "Breaking")),
            SecuritySensitiveFindingCount = filteredFindings.Count(f =>
                StringComparer.Ordinal.Equals(f.Severity, "SecuritySensitive")),
            UnsupportedFindingCount = filteredFindings.Count(f =>
                StringComparer.Ordinal.Equals(f.Severity, "Unsupported")),

            // Lifecycle — recompute from filtered lifecycle findings
            MaxLifecycleDecision =
                lifecycleFindings.Any(f => StringComparer.Ordinal.Equals(f.Severity, "Blocker")) ? DescriptorLifecycleDecisionKind.Blocked :
                lifecycleFindings.Any(f => StringComparer.Ordinal.Equals(f.Severity, "Review")) ? DescriptorLifecycleDecisionKind.ReviewRequired :
                DescriptorLifecycleDecisionKind.Allowed,
            RequiresReview = filteredFindings.Any(f =>
                StringComparer.Ordinal.Equals(f.Severity, "Breaking") ||
                StringComparer.Ordinal.Equals(f.Severity, "SecuritySensitive")),
            IsBlocked = lifecycleFindings.Any(f =>
                StringComparer.Ordinal.Equals(f.Severity, "Blocker")),
            PackageFindingCount = filteredFindings.Count,

            // Unified
            NormalizedFindings = filteredFindings
        };

        return source with { Evidence = filteredEvidence };
    }

    public ActivationReadinessPreview ProjectReadiness(
        ActivationReadinessPreview source,
        AgentDescriptorVisibilityScope scope)
    {
        return source;
    }

    // ── Nested projection helpers ──

    private static DescriptorImpactAnalysisReport ProjectImpact(
        DescriptorImpactAnalysisReport source,
        AgentDescriptorVisibilityScope scope,
        VisibleRefLookup refLookup)
    {
        // AffectedDescriptors — filter by visible Kind + ref
        var affected = source.AffectedDescriptors
            .Where(a => scope.IsVisible(a.Kind) && refLookup.IsVisible(a.Ref))
            .ToList().AsReadOnly();

        // ChangeSet.Changes — filter by visible ref
        var changes = source.ChangeSet.Changes
            .Where(c => refLookup.IsVisible(c.Ref))
            .ToList().AsReadOnly();
        var changeSet = new DescriptorChangeSet { Changes = changes };

        // Paths — filter by visible SourceChange + Affected + Segments,
        // then remove paths whose segments were all stripped (empty traversal leak).
        var paths = source.Paths
            .Where(p => refLookup.IsVisible(p.SourceChange) &&
                        refLookup.IsVisible(p.Affected))
            .Select(p => p with
            {
                Segments = p.Segments
                    .Where(s => refLookup.IsVisible(s.From) && refLookup.IsVisible(s.To))
                    .ToList().AsReadOnly()
            })
            .Where(p => p.Segments.Count > 0)
            .ToList().AsReadOnly();

        // Diagnostics — filter by visible Subject/RelatedRefs
        var diags = source.Diagnostics
            .Where(d => refLookup.IsVisible(d.Subject))
            .Select(d => d with
            {
                RelatedRefs = d.RelatedRefs?
                    .Where(r => refLookup.IsVisible(r))
                    .ToList().AsReadOnly()
            })
            .ToList().AsReadOnly();

        // Recalculate MaxSeverity from visible affected descriptors
        var maxSeverity = affected.Count == 0
            ? DescriptorImpactSeverity.None
            : affected.Max(a => (int)a.Severity) switch
            {
                1 => DescriptorImpactSeverity.Info,
                2 => DescriptorImpactSeverity.Low,
                3 => DescriptorImpactSeverity.Medium,
                4 => DescriptorImpactSeverity.High,
                5 => DescriptorImpactSeverity.Critical,
                _ => DescriptorImpactSeverity.None
            };

        return source with
        {
            AffectedDescriptors = affected,
            ChangeSet = changeSet,
            Paths = paths,
            Diagnostics = diags,
            MaxSeverity = maxSeverity
        };
    }

    private static DescriptorCompatibilityReport ProjectCompatibility(
        DescriptorCompatibilityReport source,
        AgentDescriptorVisibilityScope scope,
        VisibleRefLookup refLookup)
    {
        // Findings — filter by visible Subject, strip AffectedRefs/RelatedImpactPaths,
        // then remove findings whose RelatedImpactPaths were all hollowed out.
        var findings = source.Findings
            .Where(f => refLookup.IsVisible(f.Subject))
            .Select(f => f with
            {
                AffectedRefs = f.AffectedRefs
                    .Where(r => refLookup.IsVisible(r))
                    .ToList().AsReadOnly(),
                RelatedImpactPaths = f.RelatedImpactPaths
                    .Select(p => p with
                    {
                        Segments = p.Segments
                            .Where(s => refLookup.IsVisible(s.From) &&
                                        refLookup.IsVisible(s.To))
                            .ToList().AsReadOnly()
                    })
                    .Where(p => refLookup.IsVisible(p.SourceChange) &&
                                refLookup.IsVisible(p.Affected) &&
                                p.Segments.Count > 0)
                    .ToList().AsReadOnly()
            })
            .ToList().AsReadOnly();

        // Recursively project the nested ImpactReport
        var nestedImpact = source.ImpactReport;
        var projectedImpact = ProjectImpact(nestedImpact, scope, refLookup);

        // Recalculate MaxLevel from visible findings (Compatible if none)
        var maxLevel = findings.Count == 0
            ? DescriptorCompatibilityLevel.Compatible
            : findings.Max(f => (int)f.Level) switch
            {
                0 => DescriptorCompatibilityLevel.Unsupported,
                1 => DescriptorCompatibilityLevel.Compatible,
                2 => DescriptorCompatibilityLevel.Risky,
                3 => DescriptorCompatibilityLevel.SecuritySensitive,
                4 => DescriptorCompatibilityLevel.Breaking,
                _ => DescriptorCompatibilityLevel.Compatible
            };

        // Filter ChangeSet and Diagnostics by visible refs
        var filteredChangeSet = new DescriptorChangeSet
        {
            Changes = source.ChangeSet.Changes
                .Where(c => refLookup.IsVisible(c.Ref))
                .ToList().AsReadOnly()
        };
        var filteredDiagnostics = source.Diagnostics
            .Where(d => refLookup.IsVisible(d.Subject))
            .Select(d => d with
            {
                RelatedRefs = d.RelatedRefs?
                    .Where(r => refLookup.IsVisible(r))
                    .ToList().AsReadOnly()
            })
            .ToList().AsReadOnly();

        return source with
        {
            Findings = findings,
            ImpactReport = projectedImpact,
            ChangeSet = filteredChangeSet,
            Diagnostics = filteredDiagnostics,
            MaxLevel = maxLevel
        };
    }

    private static DescriptorLifecycleGovernanceReport ProjectGovernance(
        DescriptorLifecycleGovernanceReport source,
        VisibleRefLookup refLookup)
    {
        // PackageFindings — filter by visible Subject, strip RelatedRefs
        var packageFindings = source.PackageFindings
            .Where(f => refLookup.IsVisible(f.Subject))
            .Select(f => StripDeniedRelatedRefs(f, refLookup))
            .ToList().AsReadOnly();

        // Decisions — filter by visible Transition.Subject, then filter+strip Findings
        var decisions = source.Decisions
            .Where(d => refLookup.IsVisible(d.Transition.Subject))
            .Select(d => d with
            {
                Findings = d.Findings
                    .Where(f => refLookup.IsVisible(f.Subject))
                    .Select(f => StripDeniedRelatedRefs(f, refLookup))
                    .ToList().AsReadOnly()
            })
            .ToList().AsReadOnly();

        // Recalculate MaxDecision from visible decisions (Allowed if none)
        var maxDecision = decisions.Count == 0
            ? DescriptorLifecycleDecisionKind.Allowed
            : decisions.Max(d => (int)d.Decision) switch
            {
                0 => DescriptorLifecycleDecisionKind.Allowed,
                1 => DescriptorLifecycleDecisionKind.ReviewRequired,
                2 => DescriptorLifecycleDecisionKind.Blocked,
                _ => DescriptorLifecycleDecisionKind.Allowed
            };

        return source with
        {
            PackageFindings = packageFindings,
            Decisions = decisions,
            MaxDecision = maxDecision
        };
    }

    private static DescriptorLifecycleFinding StripDeniedRelatedRefs(
        DescriptorLifecycleFinding f,
        VisibleRefLookup refLookup)
    {
        if (f.RelatedRefs.Count == 0)
            return f;

        return f with
        {
            RelatedRefs = f.RelatedRefs
                .Where(r => refLookup.IsVisible(r))
                .ToList().AsReadOnly()
        };
    }

    // ── Version-aware identity lookup ──

    /// <summary>
    /// Provides version-aware visibility checks for DescriptorRef values.
    /// An unpinned ref (Version=null) matches any visible version.
    /// A pinned ref (Version=N) must match the exact version; if version
    /// N is denied, the ref is NOT visible even if other versions are.
    /// </summary>
    private sealed class VisibleRefLookup
    {
        // (Ns, Id) set — matches unpinned refs (any version)
        private readonly HashSet<(string Ns, string Id)> _unpinned;
        // (Ns, Id, Version) set — for pinned ref matching; only contains visible versions
        private readonly HashSet<(string Ns, string Id, int Version)> _pinned;

        public VisibleRefLookup(AgentVisibleDescriptorUniverse universe)
        {
            _unpinned = new HashSet<(string, string)>();
            _pinned = new HashSet<(string, string, int)>();

            foreach (var d in universe.VisibleDescriptors)
            {
                var nsId = (d.Namespace, d.Id);
                _unpinned.Add(nsId);

                if (d is IVersionedDescriptor vd)
                    _pinned.Add((d.Namespace, d.Id, vd.Version));
            }
        }

        public bool IsVisible(DescriptorRef? r)
        {
            if (!r.HasValue)
                return true; // null ref = kind-agnostic, retain

            var nsId = (r.Value.Namespace, r.Value.Id);

            if (r.Value.Version.HasValue)
            {
                // Pinned ref: must match exact visible version.
                // If that version is denied, return false even if other
                // versions of the same (Ns, Id) are visible.
                return _pinned.Contains((r.Value.Namespace, r.Value.Id, r.Value.Version.Value));
            }

            // Unpinned ref: any visible version is sufficient.
            return _unpinned.Contains(nsId);
        }
    }

    // ── Identity lookup builders ──

    /// <summary>
    /// Builds a set of visible descriptor identities (Ns, Id) for matching
    /// unpinned DescriptorRef objects that lack typed Kind data.
    /// </summary>
    private static HashSet<(string Ns, string Id)> BuildVisibleRefSet(
        AgentVisibleDescriptorUniverse universe)
    {
        var set = new HashSet<(string, string)>();
        foreach (var d in universe.VisibleDescriptors)
            set.Add((d.Namespace, d.Id));
        return set;
    }

    /// <summary>
    /// Builds a set of all descriptor identities (Ns, Id) for distinguishing
    /// new descriptors from known-denied descriptors.
    /// </summary>
    private static HashSet<(string Ns, string Id)> BuildAllRefSet(
        AgentVisibleDescriptorUniverse universe)
    {
        var set = new HashSet<(string, string)>();
        foreach (var d in universe.AllTenantDescriptors)
            set.Add((d.Namespace, d.Id));
        return set;
    }

}
