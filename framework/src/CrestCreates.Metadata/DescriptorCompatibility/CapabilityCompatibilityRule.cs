using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.DescriptorCompatibility;

public sealed class CapabilityCompatibilityRule : IDescriptorCompatibilityRule
{
    public string RuleId => "Capability";

    public bool CanAnalyze(DescriptorChange change, IDescriptor? before, IDescriptor? after)
    {
        return change.Kind is DescriptorChangeKind.ContractHashChanged or DescriptorChangeKind.Updated
            && (after is CapabilityDescriptor || before is CapabilityDescriptor);
    }

    public IReadOnlyList<DescriptorCompatibilityFinding> Analyze(
        DescriptorChange change, IDescriptor? before, IDescriptor? after,
        DescriptorImpactAnalysisReport impactReport, DescriptorCompatibilityAnalysisOptions options)
    {
        var findings = new List<DescriptorCompatibilityFinding>();
        var cb = before as CapabilityDescriptor;
        var ca = after as CapabilityDescriptor;
        if (ca == null) return findings;

        var affectedRefs = GetAffectedRefs(change, impactReport);

        // Input schema
        if (cb != null && !SchemaRefsEqual(cb.InputSchema, ca.InputSchema))
            findings.Add(MakeFinding(change, "COMPAT_CAPABILITY_INPUT_SCHEMA_CHANGED",
                DescriptorCompatibilityLevel.Breaking, "Capability input schema ref changed.",
                affectedRefs, "InputSchema"));

        // Output schema
        if (cb != null && !SchemaRefsEqual(cb.OutputSchema, ca.OutputSchema))
        {
            var level = cb.OutputSchema == null
                ? DescriptorCompatibilityLevel.Risky
                : DescriptorCompatibilityLevel.Breaking;
            var ruleId = cb.OutputSchema == null
                ? "COMPAT_CAPABILITY_OUTPUT_SCHEMA_ADDED"
                : "COMPAT_CAPABILITY_OUTPUT_SCHEMA_CHANGED";
            findings.Add(MakeFinding(change, ruleId, level,
                "Capability output schema ref changed.", affectedRefs, "OutputSchema"));
        }

        // Permissions
        if (cb != null)
        {
            var removedPerms = cb.Permissions.Except(ca.Permissions).ToArray();
            var addedPerms = ca.Permissions.Except(cb.Permissions).ToArray();

            foreach (var p in removedPerms)
                findings.Add(MakeFinding(change, "COMPAT_CAPABILITY_PERMISSION_REMOVED",
                    DescriptorCompatibilityLevel.SecuritySensitive,
                    $"Permission '{p}' removed from capability.", affectedRefs, $"Permissions.{p}"));

            foreach (var p in addedPerms)
                findings.Add(MakeFinding(change, "COMPAT_CAPABILITY_PERMISSION_ADDED",
                    DescriptorCompatibilityLevel.SecuritySensitive,
                    $"Permission '{p}' added to capability.", affectedRefs, $"Permissions.{p}"));
        }

        // Risk level
        if (cb != null && cb.RiskLevel != ca.RiskLevel)
            findings.Add(MakeFinding(change,
                ca.RiskLevel > cb.RiskLevel ? "COMPAT_CAPABILITY_RISK_INCREASED" : "COMPAT_CAPABILITY_RISK_DECREASED",
                DescriptorCompatibilityLevel.SecuritySensitive,
                $"Capability risk level changed from {cb.RiskLevel} to {ca.RiskLevel}.",
                affectedRefs, "RiskLevel"));

        // Capability kind
        if (cb != null && cb.CapabilityKind != ca.CapabilityKind)
            findings.Add(MakeFinding(change, "COMPAT_CAPABILITY_KIND_CHANGED",
                DescriptorCompatibilityLevel.Breaking,
                $"Capability kind changed from {cb.CapabilityKind} to {ca.CapabilityKind}.",
                affectedRefs, "CapabilityKind"));

        // Semantic tags
        if (cb != null && !cb.SemanticTags.SequenceEqual(ca.SemanticTags))
            findings.Add(MakeFinding(change, "COMPAT_CAPABILITY_TAGS_CHANGED",
                DescriptorCompatibilityLevel.Risky, "Capability semantic tags changed.",
                affectedRefs, "SemanticTags"));

        return findings;
    }

    private static bool SchemaRefsEqual(
        VersionedDescriptorRef<SchemaDescriptor>? a,
        VersionedDescriptorRef<SchemaDescriptor>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Value.Id == b.Value.Id && a.Value.Version == b.Value.Version;
    }

    private static IReadOnlyList<DescriptorRef> GetAffectedRefs(DescriptorChange change, DescriptorImpactAnalysisReport report)
        => report.Paths.Where(p => p.SourceChange == change.Ref).Select(p => p.Affected).Distinct().ToArray();

    private static DescriptorCompatibilityFinding MakeFinding(
        DescriptorChange change, string ruleId, DescriptorCompatibilityLevel level,
        string message, IReadOnlyList<DescriptorRef> affectedRefs, string path)
        => new()
        {
            Subject = change.Ref, ChangeKind = change.Kind, Level = level,
            Kind = level == DescriptorCompatibilityLevel.SecuritySensitive
                ? DescriptorCompatibilityFindingKind.Security
                : DescriptorCompatibilityFindingKind.Contract,
            RuleId = ruleId, Message = message, AffectedRefs = affectedRefs, Path = path
        };
}
