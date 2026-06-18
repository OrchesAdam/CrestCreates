using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Event.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.DescriptorCompatibility;

public sealed class EventCompatibilityRule : IDescriptorCompatibilityRule
{
    public string RuleId => "Event";

    public bool CanAnalyze(DescriptorChange change, IDescriptor? before, IDescriptor? after)
    {
        return change.Kind is DescriptorChangeKind.ContractHashChanged or DescriptorChangeKind.Updated
            && (after is EventDescriptor || after is IEventDescriptor || before is EventDescriptor || before is IEventDescriptor);
    }

    public IReadOnlyList<DescriptorCompatibilityFinding> Analyze(
        DescriptorChange change, IDescriptor? before, IDescriptor? after,
        DescriptorImpactAnalysisReport impactReport, DescriptorCompatibilityAnalysisOptions options)
    {
        var findings = new List<DescriptorCompatibilityFinding>();
        var affectedRefs = GetAffectedRefs(change, impactReport);

        // Try both regular EventDescriptor and GeneratedEventDescriptor
        var eb = before as EventDescriptor;
        var ea = after as EventDescriptor;
        var geb = before as GeneratedEventDescriptor;
        var gea = after as GeneratedEventDescriptor;

        if (ea != null && eb != null)
        {
            // Standard EventDescriptor checks
            if (!RefsEqual(eb.PayloadSchema, ea.PayloadSchema))
                findings.Add(MakeFinding(change, "COMPAT_EVENT_PAYLOAD_SCHEMA_CHANGED",
                    DescriptorCompatibilityLevel.Breaking, "Event payload schema ref changed.",
                    affectedRefs, "PayloadSchema"));

            if (eb.Importance != ea.Importance)
                findings.Add(MakeFinding(change, "COMPAT_EVENT_IMPORTANCE_CHANGED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Event importance changed from {eb.Importance} to {ea.Importance}.",
                    affectedRefs, "Importance"));

            if (ea.ChangeKind == SchemaChangeKind.Breaking)
                findings.Add(MakeFinding(change, "COMPAT_EVENT_DECLARED_BREAKING",
                    DescriptorCompatibilityLevel.Breaking,
                    "Event declares ChangeKind=Breaking.", affectedRefs, "ChangeKind"));
        }
        else if (gea != null && geb != null)
        {
            // GeneratedEventDescriptor checks
            if (!RefsEqual(geb.PayloadSchemaRef, gea.PayloadSchemaRef))
                findings.Add(MakeFinding(change, "COMPAT_EVENT_PAYLOAD_SCHEMA_CHANGED",
                    DescriptorCompatibilityLevel.Breaking, "Event payload schema ref changed.",
                    affectedRefs, "PayloadSchemaRef"));

            if (geb.Scope != gea.Scope)
                findings.Add(MakeFinding(change, "COMPAT_EVENT_SCOPE_CHANGED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Event scope changed from {geb.Scope} to {gea.Scope}.",
                    affectedRefs, "Scope"));

            if (geb.Reliability != gea.Reliability)
                findings.Add(MakeFinding(change, "COMPAT_EVENT_RELIABILITY_CHANGED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Event reliability changed from {geb.Reliability} to {gea.Reliability}.",
                    affectedRefs, "Reliability"));

            if (geb.IsAuditable != gea.IsAuditable || geb.IsReplayable != gea.IsReplayable ||
                geb.IsPublic != gea.IsPublic)
                findings.Add(MakeFinding(change, "COMPAT_EVENT_OPERATIONAL_FLAG_CHANGED",
                    DescriptorCompatibilityLevel.Risky, "Event operational flag changed.",
                    affectedRefs, "OperationalFlags"));

            if (geb.Importance != gea.Importance)
                findings.Add(MakeFinding(change, "COMPAT_EVENT_IMPORTANCE_CHANGED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Event importance changed from {geb.Importance} to {gea.Importance}.",
                    affectedRefs, "Importance"));

            if (gea.ChangeKind == SchemaChangeKind.Breaking)
                findings.Add(MakeFinding(change, "COMPAT_EVENT_DECLARED_BREAKING",
                    DescriptorCompatibilityLevel.Breaking,
                    "Event declares ChangeKind=Breaking.", affectedRefs, "ChangeKind"));
        }

        return findings;
    }

    private static bool RefsEqual<T>(VersionedDescriptorRef<T> a, VersionedDescriptorRef<T> b)
        where T : IVersionedDescriptor
        => a.Id == b.Id && a.Version == b.Version;

    private static IReadOnlyList<DescriptorRef> GetAffectedRefs(DescriptorChange change, DescriptorImpactAnalysisReport report)
        => report.Paths.Where(p => p.SourceChange == change.Ref).Select(p => p.Affected).Distinct().ToArray();

    private static DescriptorCompatibilityFinding MakeFinding(
        DescriptorChange change, string ruleId, DescriptorCompatibilityLevel level,
        string message, IReadOnlyList<DescriptorRef> affectedRefs, string path)
        => new()
        {
            Subject = change.Ref, ChangeKind = change.Kind, Level = level,
            Kind = DescriptorCompatibilityFindingKind.Contract, RuleId = ruleId,
            Message = message, AffectedRefs = affectedRefs, Path = path
        };
}
