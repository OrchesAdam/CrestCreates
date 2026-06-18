using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.DescriptorCompatibility;

public sealed class HumanTaskCompatibilityRule : IDescriptorCompatibilityRule
{
    public string RuleId => "HumanTask";

    public bool CanAnalyze(DescriptorChange change, IDescriptor? before, IDescriptor? after)
    {
        return change.Kind is DescriptorChangeKind.ContractHashChanged or DescriptorChangeKind.Updated
            && (after is HumanTaskDescriptor || before is HumanTaskDescriptor);
    }

    public IReadOnlyList<DescriptorCompatibilityFinding> Analyze(
        DescriptorChange change, IDescriptor? before, IDescriptor? after,
        DescriptorImpactAnalysisReport impactReport, DescriptorCompatibilityAnalysisOptions options)
    {
        var findings = new List<DescriptorCompatibilityFinding>();
        var hb = before as HumanTaskDescriptor;
        var ha = after as HumanTaskDescriptor;
        if (ha == null) return findings;

        var affectedRefs = GetAffectedRefs(change, impactReport);

        if (hb != null)
        {
            // Interaction ref
            if (!RefsEqual(hb.Interaction, ha.Interaction))
                findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_INTERACTION_CHANGED",
                    DescriptorCompatibilityLevel.Breaking, "HumanTask interaction/form ref changed.",
                    affectedRefs, "Interaction"));

            // Input/output schema refs
            if (!SchemaRefsEqual(hb.InputSchema, ha.InputSchema))
                findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_SCHEMA_CHANGED",
                    DescriptorCompatibilityLevel.Breaking, "HumanTask input schema ref changed.",
                    affectedRefs, "InputSchema"));

            if (!SchemaRefsEqual(hb.OutputSchema, ha.OutputSchema))
                findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_SCHEMA_CHANGED",
                    DescriptorCompatibilityLevel.Breaking, "HumanTask output schema ref changed.",
                    affectedRefs, "OutputSchema"));

            // Assignee strategy
            if (hb.AssigneeStrategy != ha.AssigneeStrategy)
                findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_ASSIGNEE_STRATEGY_CHANGED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Assignee strategy changed from {hb.AssigneeStrategy} to {ha.AssigneeStrategy}.",
                    affectedRefs, "AssigneeStrategy"));

            // Permission (single nullable string)
            if (hb.Permissions != ha.Permissions)
                findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_PERMISSION_CHANGED",
                    DescriptorCompatibilityLevel.SecuritySensitive,
                    $"HumanTask permission changed from '{hb.Permissions}' to '{ha.Permissions}'.",
                    affectedRefs, "Permissions"));

            // Outcomes — keyed by Condition only (per spec §6.5). Capability ref changes
            // on the same Condition are detected as COMPAT_HUMANTASK_OUTCOME_CAPABILITY_CHANGED.
            var beforeOutcomes = hb.Outcomes.ToDictionary(o => o.Condition);
            var afterOutcomes = ha.Outcomes.ToDictionary(o => o.Condition);

            foreach (var key in beforeOutcomes.Keys.Except(afterOutcomes.Keys))
                findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_OUTCOME_REMOVED",
                    DescriptorCompatibilityLevel.Breaking,
                     $"Completion outcome '{key}' removed.", affectedRefs, $"Outcomes.{key}"));

            foreach (var key in afterOutcomes.Keys.Except(beforeOutcomes.Keys))
                findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_OUTCOME_ADDED",
                    DescriptorCompatibilityLevel.Risky,
                     $"Completion outcome '{key}' added.", affectedRefs, $"Outcomes.{key}"));

            foreach (var key in beforeOutcomes.Keys.Intersect(afterOutcomes.Keys))
            {
                if (beforeOutcomes[key].Capability?.Id != afterOutcomes[key].Capability?.Id ||
                    beforeOutcomes[key].Capability?.Version != afterOutcomes[key].Capability?.Version)
                    findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_OUTCOME_CAPABILITY_CHANGED",
                        DescriptorCompatibilityLevel.Breaking,
                        $"Outcome '{key}' capability changed.", affectedRefs, $"Outcomes.{key}.Capability"));
            }

            // Timeout
            if (hb.Timeout != ha.Timeout)
                findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_TIMEOUT_CHANGED",
                    DescriptorCompatibilityLevel.Risky, "HumanTask timeout changed.",
                    affectedRefs, "Timeout"));
        }

        return findings;
    }

    private static bool RefsEqual<T>(VersionedDescriptorRef<T> a, VersionedDescriptorRef<T> b)
        where T : IVersionedDescriptor
        => a.Id == b.Id && a.Version == b.Version;

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
