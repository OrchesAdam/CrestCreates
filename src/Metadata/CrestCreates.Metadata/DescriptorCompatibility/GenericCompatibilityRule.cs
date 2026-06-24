using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.DescriptorCompatibility;

public sealed class GenericCompatibilityRule : IDescriptorCompatibilityRule
{
    public string RuleId => "Generic";

    public bool CanAnalyze(DescriptorChange change, IDescriptor? before, IDescriptor? after) => true;

    public IReadOnlyList<DescriptorCompatibilityFinding> Analyze(
        DescriptorChange change,
        IDescriptor? before,
        IDescriptor? after,
        DescriptorImpactAnalysisReport impactReport,
        DescriptorCompatibilityAnalysisOptions options)
    {
        var affectedRefs = GetAffectedDescriptors(change, impactReport);

        return change.Kind switch
        {
            DescriptorChangeKind.Added => [MakeFinding(change, "COMPAT_GENERIC_ADDED",
                DescriptorCompatibilityLevel.Compatible, DescriptorCompatibilityFindingKind.Structural,
                "Added descriptors do not break existing consumers.", affectedRefs)],

            DescriptorChangeKind.Removed when affectedRefs.Count > 0 => [MakeFinding(change, "COMPAT_GENERIC_REMOVED_WITH_CONSUMERS",
                DescriptorCompatibilityLevel.Breaking, DescriptorCompatibilityFindingKind.Structural,
                $"Removed descriptor has {affectedRefs.Count} affected consumer(s).", affectedRefs)],

            DescriptorChangeKind.Removed => [MakeFinding(change, "COMPAT_GENERIC_REMOVED_NO_CONSUMERS",
                options.TreatRemovedWithoutConsumersAsRisky
                    ? DescriptorCompatibilityLevel.Risky
                    : DescriptorCompatibilityLevel.Compatible,
                DescriptorCompatibilityFindingKind.Structural,
                "Removed descriptor has no affected consumers.", affectedRefs)],

            DescriptorChangeKind.Deprecated when affectedRefs.Count > 0 => [MakeFinding(change, "COMPAT_GENERIC_DEPRECATED_WITH_CONSUMERS",
                DescriptorCompatibilityLevel.Risky, DescriptorCompatibilityFindingKind.Structural,
                $"Deprecated descriptor has {affectedRefs.Count} affected consumer(s).", affectedRefs)],

            DescriptorChangeKind.Deprecated => [MakeFinding(change, "COMPAT_GENERIC_DEPRECATED_NO_CONSUMERS",
                DescriptorCompatibilityLevel.Compatible, DescriptorCompatibilityFindingKind.Structural,
                "Deprecated descriptor has no affected consumers.", affectedRefs)],

            DescriptorChangeKind.Activated => [MakeFinding(change, "COMPAT_GENERIC_ACTIVATED",
                DescriptorCompatibilityLevel.Compatible, DescriptorCompatibilityFindingKind.Behavior,
                "Activated descriptors are compatible.", affectedRefs)],

            DescriptorChangeKind.StateChanged when change.AfterState == DescriptorState.Removed =>
                affectedRefs.Count > 0
                    ? [MakeFinding(change, "COMPAT_GENERIC_STATE_REMOVED",
                        DescriptorCompatibilityLevel.Breaking, DescriptorCompatibilityFindingKind.Structural,
                        "State changed to Removed with affected consumers.", affectedRefs)]
                    : [MakeFinding(change, "COMPAT_GENERIC_STATE_REMOVED",
                        DescriptorCompatibilityLevel.Risky, DescriptorCompatibilityFindingKind.Structural,
                        "State changed to Removed with no affected consumers.", affectedRefs)],

            DescriptorChangeKind.StateChanged => [MakeFinding(change, "COMPAT_GENERIC_STATE_CHANGED",
                DescriptorCompatibilityLevel.Risky, DescriptorCompatibilityFindingKind.Behavior,
                $"State changed from {change.BeforeState} to {change.AfterState}.", affectedRefs)],

            DescriptorChangeKind.Updated when change.BeforeContractHash == change.AfterContractHash =>
                [MakeFinding(change, "COMPAT_GENERIC_UPDATED",
                    DescriptorCompatibilityLevel.Compatible, DescriptorCompatibilityFindingKind.Structural,
                    "Name-only update from DescriptorChangeSetBuilder.", affectedRefs)],

            DescriptorChangeKind.Updated => [MakeFinding(change, "COMPAT_GENERIC_UPDATED_UNEXPECTED",
                DescriptorCompatibilityLevel.Risky, DescriptorCompatibilityFindingKind.Analysis,
                "Updated with unexpected contract hash change — fallback to Risky.", affectedRefs)],

            DescriptorChangeKind.ContractHashChanged => affectedRefs.Count > 0
                ? [MakeFinding(change, "COMPAT_GENERIC_UNCLASSIFIED_CONTRACT_CHANGE",
                    DescriptorCompatibilityLevel.Risky, DescriptorCompatibilityFindingKind.Contract,
                    $"Contract hash changed with {affectedRefs.Count} affected consumer(s). Descriptor-specific rule did not classify.", affectedRefs)]
                : [MakeFinding(change, "COMPAT_GENERIC_UNCLASSIFIED_CONTRACT_CHANGE",
                    DescriptorCompatibilityLevel.Risky, DescriptorCompatibilityFindingKind.Contract,
                    "Contract hash changed with no affected consumers. Descriptor-specific rule did not classify.", affectedRefs)],

            DescriptorChangeKind.DefinitionHashChanged => [MakeFinding(change, "COMPAT_GENERIC_DEFINITION_CHANGED",
                DescriptorCompatibilityLevel.Risky, DescriptorCompatibilityFindingKind.Contract,
                "Definition hash changed. Descriptor-specific rule did not classify. Treating as Risky.", affectedRefs)],

            _ => [MakeFinding(change, "COMPAT_GENERIC_NO_MATCHING_RULE",
                options.TreatUnknownDescriptorKindAsUnsupported
                    ? DescriptorCompatibilityLevel.Unsupported
                    : DescriptorCompatibilityLevel.Risky,
                DescriptorCompatibilityFindingKind.Analysis,
                $"No rule can analyze {change.Kind} for {change.Ref}.", affectedRefs)]
        };
    }

    private static IReadOnlyList<DescriptorRef> GetAffectedDescriptors(
        DescriptorChange change,
        DescriptorImpactAnalysisReport impactReport)
    {
        return impactReport.Paths
            .Where(p => p.SourceChange == change.Ref)
            .Select(p => p.Affected)
            .Distinct()
            .ToArray();
    }

    private static DescriptorCompatibilityFinding MakeFinding(
        DescriptorChange change,
        string ruleId,
        DescriptorCompatibilityLevel level,
        DescriptorCompatibilityFindingKind kind,
        string message,
        IReadOnlyList<DescriptorRef> affectedRefs)
    {
        return new DescriptorCompatibilityFinding
        {
            Subject = change.Ref,
            ChangeKind = change.Kind,
            Level = level,
            Kind = kind,
            RuleId = ruleId,
            Message = message,
            AffectedRefs = affectedRefs
        };
    }
}
