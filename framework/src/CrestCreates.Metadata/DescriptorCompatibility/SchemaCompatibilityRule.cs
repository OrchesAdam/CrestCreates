using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.DescriptorCompatibility;

public sealed class SchemaCompatibilityRule : IDescriptorCompatibilityRule
{
    public string RuleId => "Schema";

    public bool CanAnalyze(DescriptorChange change, IDescriptor? before, IDescriptor? after)
    {
        return change.Kind is DescriptorChangeKind.ContractHashChanged or DescriptorChangeKind.Updated
            && (after is SchemaDescriptor || before is SchemaDescriptor);
    }

    public IReadOnlyList<DescriptorCompatibilityFinding> Analyze(
        DescriptorChange change,
        IDescriptor? before,
        IDescriptor? after,
        DescriptorImpactAnalysisReport impactReport,
        DescriptorCompatibilityAnalysisOptions options)
    {
        var findings = new List<DescriptorCompatibilityFinding>();
        var sb = before as SchemaDescriptor;
        var sa = after as SchemaDescriptor;
        if (sa == null) return findings;

        var affectedRefs = GetAffectedRefs(change, impactReport);
        var beforeFields = sb?.Fields.ToDictionary(f => f.Name) ?? new Dictionary<string, SchemaFieldDescriptor>();
        var afterFields = sa.Fields.ToDictionary(f => f.Name);

        // Field removal
        foreach (var name in beforeFields.Keys.Except(afterFields.Keys))
        {
            var level = affectedRefs.Count > 0
                ? DescriptorCompatibilityLevel.Breaking
                : DescriptorCompatibilityLevel.Risky;
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_FIELD_REMOVED", level,
                $"Field '{name}' removed.", affectedRefs, name, beforeFields[name].FieldType, null));
        }

        // Field addition
        foreach (var name in afterFields.Keys.Except(beforeFields.Keys))
        {
            var f = afterFields[name];
            var level = f.IsRequired
                ? DescriptorCompatibilityLevel.Breaking
                : DescriptorCompatibilityLevel.Compatible;
            var ruleId = f.IsRequired
                ? "COMPAT_SCHEMA_REQUIRED_FIELD_ADDED"
                : "COMPAT_SCHEMA_OPTIONAL_FIELD_ADDED";
            var msg = f.IsRequired
                ? $"Required field '{name}' added."
                : $"Optional field '{name}' added.";
            findings.Add(MakeFieldFinding(change, ruleId, level, msg, affectedRefs, name, null, f.FieldType));
        }

        // Field changes (compare common fields)
        foreach (var name in beforeFields.Keys.Intersect(afterFields.Keys))
        {
            CompareField(change, findings, affectedRefs, name, beforeFields[name], afterFields[name]);
        }

        // Schema-level references changed
        if (sb != null && !ReferencesEqual(sb.References, sa.References))
        {
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_REFERENCE_CHANGED",
                DescriptorCompatibilityLevel.Risky, "Schema references changed.",
                affectedRefs, "References", null, null));
        }

        // Declared breaking change kind
        if (sa.ChangeKind == SchemaChangeKind.Breaking)
        {
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_DECLARED_BREAKING",
                DescriptorCompatibilityLevel.Breaking, "Schema declares ChangeKind=Breaking.",
                affectedRefs, "ChangeKind", null, nameof(SchemaChangeKind.Breaking)));
        }

        return findings;
    }

    private static void CompareField(
        DescriptorChange change,
        List<DescriptorCompatibilityFinding> findings,
        IReadOnlyList<DescriptorRef> affectedRefs,
        string name,
        SchemaFieldDescriptor bf,
        SchemaFieldDescriptor af)
    {
        if (bf.FieldType != af.FieldType)
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_FIELD_TYPE_CHANGED",
                DescriptorCompatibilityLevel.Breaking,
                $"Field '{name}' type changed from '{bf.FieldType}' to '{af.FieldType}'.",
                affectedRefs, name, bf.FieldType, af.FieldType));

        if (bf.IsCollection != af.IsCollection || bf.CollectionElementType != af.CollectionElementType)
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_COLLECTION_CHANGED",
                DescriptorCompatibilityLevel.Breaking,
                $"Field '{name}' collection type changed.",
                affectedRefs, name, null, null));

        if (!bf.IsRequired && af.IsRequired)
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_FIELD_REQUIRED_ADDED",
                DescriptorCompatibilityLevel.Breaking,
                $"Field '{name}' IsRequired changed from false to true.",
                affectedRefs, name, "false", "true"));

        if (bf.IsRequired && !af.IsRequired)
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_FIELD_REQUIRED_RELAXED",
                DescriptorCompatibilityLevel.Compatible,
                $"Field '{name}' IsRequired relaxed from true to false.",
                affectedRefs, name, "true", "false"));

        if (!bf.IsNullable && af.IsNullable)
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_NULLABILITY_RELAXED",
                DescriptorCompatibilityLevel.Compatible,
                $"Field '{name}' IsNullable relaxed.",
                affectedRefs, name, "false", "true"));

        if (bf.IsNullable && !af.IsNullable)
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_NULLABILITY_NARROWED",
                DescriptorCompatibilityLevel.Breaking,
                $"Field '{name}' IsNullable narrowed.",
                affectedRefs, name, "true", "false"));

        if (bf.MaxLength.HasValue && af.MaxLength.HasValue && af.MaxLength < bf.MaxLength)
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_MAX_LENGTH_NARROWED",
                DescriptorCompatibilityLevel.Breaking,
                $"Field '{name}' MaxLength narrowed from {bf.MaxLength} to {af.MaxLength}.",
                affectedRefs, name, bf.MaxLength.ToString(), af.MaxLength.ToString()));

        if (bf.MaxLength.HasValue && (!af.MaxLength.HasValue || af.MaxLength > bf.MaxLength))
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_MAX_LENGTH_RELAXED",
                DescriptorCompatibilityLevel.Compatible,
                $"Field '{name}' MaxLength relaxed.",
                affectedRefs, name, null, null));

        if (bf.MinLength.HasValue && af.MinLength.HasValue && af.MinLength > bf.MinLength)
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_MIN_LENGTH_NARROWED",
                DescriptorCompatibilityLevel.Breaking,
                $"Field '{name}' MinLength increased from {bf.MinLength} to {af.MinLength}.",
                affectedRefs, name, bf.MinLength.ToString(), af.MinLength.ToString()));

        if (bf.MinLength.HasValue && (!af.MinLength.HasValue || af.MinLength < bf.MinLength))
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_MIN_LENGTH_RELAXED",
                DescriptorCompatibilityLevel.Compatible,
                $"Field '{name}' MinLength relaxed.",
                affectedRefs, name, null, null));

        if (bf.MaxValue.HasValue && af.MaxValue.HasValue && af.MaxValue < bf.MaxValue)
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_MAX_VALUE_NARROWED",
                DescriptorCompatibilityLevel.Breaking,
                $"Field '{name}' MaxValue narrowed from {bf.MaxValue} to {af.MaxValue}.",
                affectedRefs, name, bf.MaxValue.ToString(), af.MaxValue.ToString()));

        if (bf.MinValue.HasValue && af.MinValue.HasValue && af.MinValue > bf.MinValue)
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_MIN_VALUE_NARROWED",
                DescriptorCompatibilityLevel.Breaking,
                $"Field '{name}' MinValue increased from {bf.MinValue} to {af.MinValue}.",
                affectedRefs, name, bf.MinValue.ToString(), af.MinValue.ToString()));

        if (bf.Pattern != af.Pattern && (bf.Pattern != null || af.Pattern != null))
            findings.Add(MakeFieldFinding(change, "COMPAT_SCHEMA_PATTERN_CHANGED",
                DescriptorCompatibilityLevel.Breaking,
                $"Field '{name}' Pattern changed.",
                affectedRefs, name, bf.Pattern, af.Pattern));
    }

    private static bool ReferencesEqual(
        IReadOnlyList<VersionedDescriptorRef<SchemaDescriptor>> a,
        IReadOnlyList<VersionedDescriptorRef<SchemaDescriptor>> b)
    {
        if (a.Count != b.Count) return false;
        var aSorted = a.Select(r => (r.Id, r.Version)).OrderBy(x => x).ToArray();
        var bSorted = b.Select(r => (r.Id, r.Version)).OrderBy(x => x).ToArray();
        return aSorted.SequenceEqual(bSorted);
    }

    private static IReadOnlyList<DescriptorRef> GetAffectedRefs(
        DescriptorChange change,
        DescriptorImpactAnalysisReport impactReport)
    {
        return impactReport.Paths
            .Where(p => p.SourceChange == change.Ref)
            .Select(p => p.Affected)
            .Distinct()
            .ToArray();
    }

    private static DescriptorCompatibilityFinding MakeFieldFinding(
        DescriptorChange change,
        string ruleId,
        DescriptorCompatibilityLevel level,
        string message,
        IReadOnlyList<DescriptorRef> affectedRefs,
        string path,
        string? beforeValue,
        string? afterValue)
    {
        return new DescriptorCompatibilityFinding
        {
            Subject = change.Ref,
            ChangeKind = change.Kind,
            Level = level,
            Kind = DescriptorCompatibilityFindingKind.Contract,
            RuleId = ruleId,
            Message = message,
            AffectedRefs = affectedRefs,
            Path = $"Fields.{path}",
            BeforeValue = beforeValue,
            AfterValue = afterValue
        };
    }
}
