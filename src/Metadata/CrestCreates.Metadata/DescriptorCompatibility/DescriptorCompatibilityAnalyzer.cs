using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.DescriptorCompatibility;

public sealed class DescriptorCompatibilityAnalyzer : IDescriptorCompatibilityAnalyzer
{
    private readonly IReadOnlyList<IDescriptorCompatibilityRule> _rules;

    public DescriptorCompatibilityAnalyzer()
    {
        _rules = new IDescriptorCompatibilityRule[]
        {
            new SchemaCompatibilityRule(),
            new FormCompatibilityRule(),
            new CapabilityCompatibilityRule(),
            new EventCompatibilityRule(),
            new HumanTaskCompatibilityRule(),
            new WorkflowCompatibilityRule(),
            new GenericCompatibilityRule() // Always last — catch-all
        };
    }

    public DescriptorCompatibilityReport Analyze(
        IReadOnlyList<IDescriptor> before,
        IReadOnlyList<IDescriptor> after,
        DescriptorChangeSet changeSet,
        DescriptorImpactAnalysisReport impactReport,
        DescriptorCompatibilityAnalysisOptions? options = null)
    {
        options ??= new DescriptorCompatibilityAnalysisOptions();
        var diagnostics = new List<DescriptorCompatibilityDiagnostic>();

        // Step 1: Validate changeSet consistency
        if (!ChangeSetsEqual(impactReport.ChangeSet, changeSet))
        {
            diagnostics.Add(new DescriptorCompatibilityDiagnostic(
                DiagnosticSeverity.Error, "COMPAT_CHANGESET_MISMATCH",
                "Provided changeSet differs from impactReport.ChangeSet.", null, null));
        }

        // Step 2: Build before/after indexes
        var beforeIndex = BuildDescriptorIndex(before, diagnostics);
        var afterIndex = BuildDescriptorIndex(after, diagnostics);

        // Step 3: Map impact diagnostics to compatibility diagnostics + unsupported findings
        var unsupportedFindings = new List<DescriptorCompatibilityFinding>();
        MapImpactDiagnostics(impactReport, diagnostics, unsupportedFindings, options.TreatImpactWarningsAsUnsupported);

        // Step 4: For each change, run rules
        var findings = new List<DescriptorCompatibilityFinding>();
        foreach (var change in changeSet.Changes)
        {
            var beforeDesc = ResolveDescriptor(change.Ref, beforeIndex);
            var afterDesc = ResolveDescriptor(change.Ref, afterIndex);

            // Run descriptor-specific rules first (all except the last Generic rule)
            bool anySpecificFindings = false;
            for (int i = 0; i < _rules.Count - 1; i++)
            {
                var rule = _rules[i];
                if (!rule.CanAnalyze(change, beforeDesc, afterDesc)) continue;
                var ruleFindings = rule.Analyze(change, beforeDesc, afterDesc, impactReport, options);
                if (ruleFindings.Count > 0)
                {
                    findings.AddRange(ruleFindings);
                    anySpecificFindings = true;
                }
            }

            // Run generic rule as catch-all. When a descriptor-specific rule already
            // produced findings for a ContractHashChanged or DefinitionHashChanged change,
            // suppress the generic COMPAT_GENERIC_UNCLASSIFIED_CONTRACT_CHANGE and
            // COMPAT_GENERIC_DEFINITION_CHANGED to avoid conflicting findings.
            // Only suppress when the specific rule actually found something — a rule that
            // CanAnalyze=true but produces zero findings (e.g., Schema rule for a
            // ValidationRules-only change) should NOT suppress the generic fallback.
            var genericFindings = _rules.Last().Analyze(change, beforeDesc, afterDesc, impactReport, options);
            if (anySpecificFindings)
            {
                genericFindings = genericFindings
                    .Where(f => f.RuleId is not (
                        "COMPAT_GENERIC_UNCLASSIFIED_CONTRACT_CHANGE"
                        or "COMPAT_GENERIC_DEFINITION_CHANGED"))
                    .ToArray();
            }
            findings.AddRange(genericFindings);

            // Attach impact paths to findings for this change
            var relatedPaths = impactReport.Paths
                .Where(p => p.SourceChange == change.Ref)
                .ToArray();
            if (relatedPaths.Length > 0)
            {
                for (int fi = 0; fi < findings.Count; fi++)
                {
                    if (findings[fi].Subject == change.Ref && findings[fi].RelatedImpactPaths.Count == 0)
                        findings[fi] = findings[fi] with { RelatedImpactPaths = relatedPaths };
                }
            }
        }

        // Step 5: Add unsupported findings from impact diagnostics
        findings.AddRange(unsupportedFindings);

        // Step 6: Deduplicate by (Subject, RuleId, Path, Level)
        findings = DeduplicateFindings(findings);

        // Step 7: Filter compatible findings if option says so
        if (!options.IncludeCompatibleFindings)
            findings = findings.Where(f => f.Level != DescriptorCompatibilityLevel.Compatible).ToList();

        // Step 8: Sort deterministically
        findings = findings
            .OrderBy(f => f.Subject.Namespace)
            .ThenBy(f => f.Subject.Id)
            .ThenBy(f => f.Subject.Version ?? 0)
            .ThenByDescending(f => (int)f.Level)
            .ThenBy(f => f.RuleId)
            .ThenBy(f => f.Path ?? string.Empty)
            .ToList();

        // Step 9: Compute MaxLevel from classified findings only.
        // Empty findings (e.g., all-compatible filtered by IncludeCompatibleFindings=false)
        // default to Compatible, not Unsupported. Only "all findings are Unsupported" → Unsupported.
        var classifiedLevels = findings
            .Where(f => f.Level != DescriptorCompatibilityLevel.Unsupported)
            .Select(f => f.Level)
            .ToArray();

        var maxLevel = classifiedLevels.Length > 0
            ? ComputeMaxLevel(classifiedLevels)
            : findings.Count > 0
                ? DescriptorCompatibilityLevel.Unsupported
                : DescriptorCompatibilityLevel.Compatible;

        return new DescriptorCompatibilityReport
        {
            ChangeSet = changeSet,
            ImpactReport = impactReport,
            Findings = findings,
            MaxLevel = maxLevel,
            Diagnostics = diagnostics
        };
    }

    // === Index builders ===

    private static Dictionary<DescriptorRef, IDescriptor> BuildDescriptorIndex(
        IReadOnlyList<IDescriptor> descriptors,
        List<DescriptorCompatibilityDiagnostic> diagnostics)
    {
        var index = new Dictionary<DescriptorRef, IDescriptor>();
        foreach (var d in descriptors)
        {
            var version = d is IVersionedDescriptor vd ? vd.Version : (int?)null;
            var key = new DescriptorRef(d.Namespace, d.Id, version);
            if (index.ContainsKey(key))
            {
                diagnostics.Add(new DescriptorCompatibilityDiagnostic(
                    DiagnosticSeverity.Warning, "COMPAT_DUPLICATE_DESCRIPTOR_REF",
                    $"Duplicate descriptor ref {key.FullId} in inventory. Using first occurrence.", key, null));
                continue;
            }
            index[key] = d;
        }
        return index;
    }

    // === Descriptor resolution ===

    private static IDescriptor? ResolveDescriptor(
        DescriptorRef targetRef,
        Dictionary<DescriptorRef, IDescriptor> index)
    {
        if (index.TryGetValue(targetRef, out var d))
            return d;

        // Try unpinned resolution: match by (Namespace, Id) with any version
        if (targetRef.Version == null)
        {
            var match = index.Keys.FirstOrDefault(k =>
                k.Namespace == targetRef.Namespace && k.Id == targetRef.Id);
            if (match != default)
                return index[match];
        }

        return null;
    }

    // === Impact diagnostic mapping ===

    private static void MapImpactDiagnostics(
        DescriptorImpactAnalysisReport impactReport,
        List<DescriptorCompatibilityDiagnostic> diagnostics,
        List<DescriptorCompatibilityFinding> unsupportedFindings,
        bool treatWarningsAsUnsupported)
    {
        foreach (var diag in impactReport.Diagnostics)
        {
            var mapped = diag.Code switch
            {
                "IMPACT_TOPOLOGY_MISSING_TARGET" => ("COMPAT_BLOCKED_BY_TOPOLOGY_ERROR", DiagnosticSeverity.Error),
                var c when c.StartsWith("IMPACT_TOPOLOGY_") => ("COMPAT_ANALYSIS_INCOMPLETE", DiagnosticSeverity.Error),
                "IMPACT_AMBIGUOUS_UNPINNED_TARGET" => ("COMPAT_VERSION_AMBIGUITY", DiagnosticSeverity.Warning),
                "IMPACT_PATH_TRUNCATED" => ("COMPAT_ANALYSIS_INCOMPLETE", DiagnosticSeverity.Warning),
                "IMPACT_CHANGE_NOT_IN_TOPOLOGY" => ("COMPAT_CHANGE_NOT_IN_TOPOLOGY", DiagnosticSeverity.Warning),
                _ => ((string Code, DiagnosticSeverity Severity)?)null
            };

            if (mapped == null) continue;

            diagnostics.Add(new DescriptorCompatibilityDiagnostic(
                mapped.Value.Severity, mapped.Value.Code, diag.Message, diag.Subject, diag.RelatedRefs));

            // Add Unsupported finding for error-level diagnostics (or warnings if option enabled)
            if (diag.Severity == DiagnosticSeverity.Error || treatWarningsAsUnsupported)
            {
                unsupportedFindings.Add(new DescriptorCompatibilityFinding
                {
                    Subject = diag.Subject ?? new DescriptorRef(string.Empty, string.Empty),
                    ChangeKind = DescriptorChangeKind.ContractHashChanged,
                    Level = DescriptorCompatibilityLevel.Unsupported,
                    Kind = DescriptorCompatibilityFindingKind.Analysis,
                    RuleId = "COMPAT_ANALYSIS_UNTRUSTED_IMPACT_REPORT",
                    Message = diag.Message,
                    AffectedRefs = diag.RelatedRefs ?? Array.Empty<DescriptorRef>()
                });
            }
        }
    }

    // === Finding helpers ===

    private static List<DescriptorCompatibilityFinding> DeduplicateFindings(
        List<DescriptorCompatibilityFinding> findings)
    {
        var seen = new HashSet<(DescriptorRef Subject, string RuleId, string? Path, DescriptorCompatibilityLevel Level)>();
        var result = new List<DescriptorCompatibilityFinding>();
        foreach (var f in findings)
        {
            var key = (f.Subject, f.RuleId, f.Path, f.Level);
            if (seen.Add(key))
                result.Add(f);
        }
        return result;
    }

    private static bool ChangeSetsEqual(DescriptorChangeSet a, DescriptorChangeSet b)
    {
        if (a == b) return true;
        if (a.Changes.Count != b.Changes.Count) return false;
        for (int i = 0; i < a.Changes.Count; i++)
        {
            var ca = a.Changes[i];
            var cb = b.Changes[i];
            if (ca.Ref != cb.Ref || ca.Kind != cb.Kind) return false;
        }
        return true;
    }

    private static DescriptorCompatibilityLevel ComputeMaxLevel(
        IEnumerable<DescriptorCompatibilityLevel> levels)
    {
        var max = DescriptorCompatibilityLevel.Compatible;
        foreach (var l in levels)
        {
            if ((int)l > (int)max) max = l;
        }
        return max;
    }
}
