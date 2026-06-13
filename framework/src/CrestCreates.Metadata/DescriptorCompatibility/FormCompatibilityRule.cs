using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Form.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.DescriptorCompatibility;

public sealed class FormCompatibilityRule : IDescriptorCompatibilityRule
{
    public string RuleId => "Form";

    public bool CanAnalyze(DescriptorChange change, IDescriptor? before, IDescriptor? after)
    {
        return change.Kind is DescriptorChangeKind.ContractHashChanged or DescriptorChangeKind.Updated
            && (after is FormDescriptor || before is FormDescriptor);
    }

    public IReadOnlyList<DescriptorCompatibilityFinding> Analyze(
        DescriptorChange change,
        IDescriptor? before,
        IDescriptor? after,
        DescriptorImpactAnalysisReport impactReport,
        DescriptorCompatibilityAnalysisOptions options)
    {
        var findings = new List<DescriptorCompatibilityFinding>();
        var fb = before as FormDescriptor;
        var fa = after as FormDescriptor;
        if (fa == null) return findings;

        var affectedRefs = GetAffectedRefs(change, impactReport);

        // Schema ref changed
        if (fb != null && !RefsEqual(fb.Schema, fa.Schema))
            findings.Add(MakeFinding(change, "COMPAT_FORM_SCHEMA_CHANGED",
                DescriptorCompatibilityLevel.Breaking, "Form bound schema ref changed.",
                affectedRefs, "Schema"));

        var beforeFields = fb?.Fields.ToDictionary(f => f.SchemaFieldName) ?? new Dictionary<string, FormFieldDescriptor>();
        var afterFields = fa.Fields.ToDictionary(f => f.SchemaFieldName);

        // Field removal
        foreach (var name in beforeFields.Keys.Except(afterFields.Keys))
        {
            var level = affectedRefs.Count > 0
                ? DescriptorCompatibilityLevel.Breaking
                : DescriptorCompatibilityLevel.Risky;
            findings.Add(MakeFinding(change, "COMPAT_FORM_FIELD_REMOVED", level,
                $"Form field '{name}' removed.", affectedRefs, $"Fields.{name}"));
        }

        // Field addition
        foreach (var name in afterFields.Keys.Except(beforeFields.Keys))
        {
            findings.Add(MakeFinding(change, "COMPAT_FORM_FIELD_ADDED",
                DescriptorCompatibilityLevel.Compatible,
                $"Form field '{name}' added.", affectedRefs, $"Fields.{name}"));
        }

        // Field changes
        foreach (var name in beforeFields.Keys.Intersect(afterFields.Keys))
        {
            var bf = beforeFields[name];
            var af = afterFields[name];

            if (bf.IsRequiredOverride != true && af.IsRequiredOverride == true)
                findings.Add(MakeFinding(change, "COMPAT_FORM_REQUIRED_OVERRIDE_ADDED",
                    DescriptorCompatibilityLevel.Breaking,
                    $"Form field '{name}' IsRequiredOverride set to true.",
                    affectedRefs, $"Fields.{name}.IsRequiredOverride"));

            if (bf.IsRequiredOverride == true && af.IsRequiredOverride != true)
                findings.Add(MakeFinding(change, "COMPAT_FORM_REQUIRED_OVERRIDE_RELAXED",
                    DescriptorCompatibilityLevel.Compatible,
                    $"Form field '{name}' IsRequiredOverride relaxed.",
                    affectedRefs, $"Fields.{name}.IsRequiredOverride"));

            if (bf.IsReadOnly != af.IsReadOnly)
                findings.Add(MakeFinding(change, "COMPAT_FORM_READONLY_CHANGED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Form field '{name}' IsReadOnly changed.",
                    affectedRefs, $"Fields.{name}.IsReadOnly"));

            if (bf.ControlType != af.ControlType)
                findings.Add(MakeFinding(change, "COMPAT_FORM_CONTROL_CHANGED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Form field '{name}' ControlType changed from '{bf.ControlType}' to '{af.ControlType}'.",
                    affectedRefs, $"Fields.{name}.ControlType"));

            if (bf.OptionsSource != af.OptionsSource)
                findings.Add(MakeFinding(change, "COMPAT_FORM_OPTIONS_CHANGED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Form field '{name}' OptionsSource changed.",
                    affectedRefs, $"Fields.{name}.OptionsSource"));

            // Presentation-only: check if only presentation fields changed
            var hasStructuralFindings = findings.Any(f =>
                f.Path == $"Fields.{name}" || (f.Path?.StartsWith($"Fields.{name}.") ?? false));
            if (!hasStructuralFindings &&
                (bf.Order != af.Order || bf.Group != af.Group || bf.Label != af.Label ||
                 bf.Placeholder != af.Placeholder || bf.HelpText != af.HelpText))
            {
                findings.Add(MakeFinding(change, "COMPAT_FORM_PRESENTATION_ONLY",
                    DescriptorCompatibilityLevel.Compatible,
                    $"Form field '{name}' presentation-only changes (order/group/labels).",
                    affectedRefs, $"Fields.{name}.Presentation"));
            }
        }

        return findings;
    }

    private static bool RefsEqual(VersionedDescriptorRef<SchemaDescriptor> a, VersionedDescriptorRef<SchemaDescriptor> b)
    {
        return a.Id == b.Id && a.Version == b.Version;
    }

    private static IReadOnlyList<DescriptorRef> GetAffectedRefs(DescriptorChange change, DescriptorImpactAnalysisReport impactReport)
    {
        return impactReport.Paths.Where(p => p.SourceChange == change.Ref).Select(p => p.Affected).Distinct().ToArray();
    }

    private static DescriptorCompatibilityFinding MakeFinding(
        DescriptorChange change, string ruleId, DescriptorCompatibilityLevel level,
        string message, IReadOnlyList<DescriptorRef> affectedRefs, string path)
    {
        return new DescriptorCompatibilityFinding
        {
            Subject = change.Ref, ChangeKind = change.Kind, Level = level,
            Kind = DescriptorCompatibilityFindingKind.Contract, RuleId = ruleId,
            Message = message, AffectedRefs = affectedRefs, Path = path
        };
    }
}
