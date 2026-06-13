# Phase 6d — Compatibility / Breaking Change Analyzer: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `IDescriptorCompatibilityAnalyzer` that consumes `(before inventory, after inventory, DescriptorChangeSet, DescriptorImpactAnalysisReport)` and produces a deterministic `DescriptorCompatibilityReport` with rule-based compatibility findings, max level, and diagnostics.

**Architecture:** Analyzer is a stateless singleton. Rules are dispatched via `IDescriptorCompatibilityRule` interface — generic rules cover all `DescriptorChangeKind` values, descriptor-specific rules cover 6 descriptor kinds (Schema, Form, Capability, Event/GeneratedEvent, HumanTask, Workflow). Rules inspect descriptor internals for before/after contract comparison. No topology access, no new impact traversal, no lifecycle governance. Severity (`DescriptorImpactSeverity`) is never directly projected into compatibility (`DescriptorCompatibilityLevel`).

**Key Design Decisions:**
- `DescriptorCompatibilityLevel.Unsupported = 0` — excluded from `MaxLevel` (natural `Max()` ignores it) unless all findings are Unsupported
- Generic `Updated` rule: `DescriptorChangeSetBuilder` guarantees `Updated` means name-only change (same ContractHash). If invariant is violated, fallback to `COMPAT_GENERIC_UPDATED_UNEXPECTED` → `Risky`
- `IMPACT_DESCRIPTOR_NOT_IN_TOPOLOGY` maps to `COMPAT_CHANGE_NOT_IN_TOPOLOGY` diagnostic; adds `Unsupported` finding only when `TreatImpactWarningsAsUnsupported=true`
- Data-permission scope rules reserved (`COMPAT_SECURITY_DATA_SCOPE_WIDENED/NARROWED`) but NOT implemented — no descriptor owns data-permission scope today
- HumanTaskDescriptor.Permissions is `string?` (single nullable), NOT `IReadOnlyList<string>` like CapabilityDescriptor — comparison is simple string equality

**Tech Stack:** .NET 10, C# records/enums/interfaces only (AoT-friendly), xUnit + FluentAssertions, no runtime reflection, no dynamic, no expression trees.

---

## Task 1: Abstractions — Enums

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityLevel.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityFindingKind.cs`

- [ ] **Step 1: Write `DescriptorCompatibilityLevel.cs`**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

// Unsupported = 0 is deliberate: MaxLevel uses natural Max() over classified
// findings (Compatible..Breaking). Unsupported is excluded from MaxLevel unless
// all findings are Unsupported. This matches the semantics lock: Unsupported
// means "insufficient rule knowledge", not "more severe than Breaking".
public enum DescriptorCompatibilityLevel
{
    Unsupported = 0,
    Compatible = 1,
    Risky = 2,
    SecuritySensitive = 3,
    Breaking = 4
}
```

- [ ] **Step 2: Write `DescriptorCompatibilityFindingKind.cs`**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public enum DescriptorCompatibilityFindingKind
{
    Structural,
    Contract,
    Behavior,
    Security,
    Analysis
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityLevel.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityFindingKind.cs
git commit -m "feat(6d): add DescriptorCompatibilityLevel and FindingKind enums"
```

---

## Task 2: Abstractions — Core Types (Diagnostic, Finding, Report, Options)

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityDiagnostic.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityFinding.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityReport.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityAnalysisOptions.cs`

- [ ] **Step 1: Write `DescriptorCompatibilityDiagnostic.cs`**

Use the existing `DiagnosticSeverity` from `DescriptorTopology` (Error/Warning/Info). This is the exact same shape as `DescriptorTopologyDiagnostic` and `DescriptorImpactDiagnostic`:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public sealed record DescriptorCompatibilityDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DescriptorRef? Subject,
    IReadOnlyList<DescriptorRef>? RelatedRefs);
```

- [ ] **Step 2: Write `DescriptorCompatibilityFinding.cs`**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public sealed record DescriptorCompatibilityFinding
{
    public required DescriptorRef Subject { get; init; }
    public required DescriptorChangeKind ChangeKind { get; init; }
    public required DescriptorCompatibilityLevel Level { get; init; }
    public required DescriptorCompatibilityFindingKind Kind { get; init; }
    public required string RuleId { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<DescriptorRef> AffectedRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<DescriptorImpactPath> RelatedImpactPaths { get; init; } = Array.Empty<DescriptorImpactPath>();
    public string? Path { get; init; }
    public string? BeforeValue { get; init; }
    public string? AfterValue { get; init; }
    public string? SuggestedAction { get; init; }
}
```

- [ ] **Step 3: Write `DescriptorCompatibilityReport.cs`**

```csharp
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public sealed record DescriptorCompatibilityReport
{
    public required DescriptorChangeSet ChangeSet { get; init; }
    public required DescriptorImpactAnalysisReport ImpactReport { get; init; }
    public required IReadOnlyList<DescriptorCompatibilityFinding> Findings { get; init; }
    public required DescriptorCompatibilityLevel MaxLevel { get; init; }
    public required IReadOnlyList<DescriptorCompatibilityDiagnostic> Diagnostics { get; init; }

    public bool RequiresReview =>
        MaxLevel is DescriptorCompatibilityLevel.Risky
            or DescriptorCompatibilityLevel.SecuritySensitive
            or DescriptorCompatibilityLevel.Breaking
            or DescriptorCompatibilityLevel.Unsupported;

    public bool HasBreakingChanges =>
        Findings.Any(f => f.Level == DescriptorCompatibilityLevel.Breaking);

    public bool HasSecuritySensitiveChanges =>
        Findings.Any(f => f.Level == DescriptorCompatibilityLevel.SecuritySensitive);

    public bool HasUnsupportedFindings =>
        Findings.Any(f => f.Level == DescriptorCompatibilityLevel.Unsupported);
}
```

- [ ] **Step 4: Write `DescriptorCompatibilityAnalysisOptions.cs`**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public sealed record DescriptorCompatibilityAnalysisOptions
{
    public bool TreatRemovedWithoutConsumersAsRisky { get; init; } = true;
    public bool TreatUnknownDescriptorKindAsUnsupported { get; init; } = true;
    public bool TreatImpactWarningsAsUnsupported { get; init; } = false;
    public bool IncludeCompatibleFindings { get; init; } = true;
}
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityDiagnostic.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityFinding.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityReport.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/DescriptorCompatibilityAnalysisOptions.cs
git commit -m "feat(6d): add CompatibilityDiagnostic, Finding, Report, and Options records"
```

---

## Task 3: Abstractions — Interfaces

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/IDescriptorCompatibilityAnalyzer.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/IDescriptorCompatibilityRule.cs`

- [ ] **Step 1: Write `IDescriptorCompatibilityAnalyzer.cs`**

```csharp
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public interface IDescriptorCompatibilityAnalyzer
{
    DescriptorCompatibilityReport Analyze(
        IReadOnlyList<IDescriptor> before,
        IReadOnlyList<IDescriptor> after,
        DescriptorChangeSet changeSet,
        DescriptorImpactAnalysisReport impactReport,
        DescriptorCompatibilityAnalysisOptions? options = null);
}
```

- [ ] **Step 2: Write `IDescriptorCompatibilityRule.cs`**

```csharp
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public interface IDescriptorCompatibilityRule
{
    string RuleId { get; }

    bool CanAnalyze(
        DescriptorChange change,
        IDescriptor? before,
        IDescriptor? after);

    IReadOnlyList<DescriptorCompatibilityFinding> Analyze(
        DescriptorChange change,
        IDescriptor? before,
        IDescriptor? after,
        DescriptorImpactAnalysisReport impactReport,
        DescriptorCompatibilityAnalysisOptions options);
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/IDescriptorCompatibilityAnalyzer.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/IDescriptorCompatibilityRule.cs
git commit -m "feat(6d): add IDescriptorCompatibilityAnalyzer and IDescriptorCompatibilityRule interfaces"
```

---

## Task 4: Implementation — Generic Compatibility Rule

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorCompatibility/GenericCompatibilityRule.cs`

The generic rule handles all `DescriptorChangeKind` values without inspecting descriptor internals. It uses only change metadata (Kind, BeforeState, AfterState, ContractHash) + Phase 6c affected consumers.

- [ ] **Step 1: Write `GenericCompatibilityRule.cs`**

```csharp
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
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorCompatibility/GenericCompatibilityRule.cs
git commit -m "feat(6d): add GenericCompatibilityRule covering all DescriptorChangeKind values"
```

---

## Task 5: Implementation — Schema Compatibility Rule

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorCompatibility/SchemaCompatibilityRule.cs`

Schema-specific rules compare `SchemaFieldDescriptor` fields by Name, plus schema-level `References` and `ChangeKind`.

- [ ] **Step 1: Write `SchemaCompatibilityRule.cs`**

```csharp
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
            var bf = beforeFields[name];
            var af = afterFields[name];
            CompareField(change, findings, affectedRefs, name, bf, af);
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
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorCompatibility/SchemaCompatibilityRule.cs
git commit -m "feat(6d): add SchemaCompatibilityRule with 14 field-level compatibility checks"
```

---

## Task 6: Implementation — Form Compatibility Rule

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorCompatibility/FormCompatibilityRule.cs`

Form-specific rules compare `FormFieldDescriptor` by `SchemaFieldName`, plus form-level `Schema` ref, overrides, and presentation-only fields.

- [ ] **Step 1: Write `FormCompatibilityRule.cs`**

```csharp
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

            // IsRequiredOverride: false/null → true
            if (bf.IsRequiredOverride != true && af.IsRequiredOverride == true)
                findings.Add(MakeFinding(change, "COMPAT_FORM_REQUIRED_OVERRIDE_ADDED",
                    DescriptorCompatibilityLevel.Breaking,
                    $"Form field '{name}' IsRequiredOverride set to true.",
                    affectedRefs, $"Fields.{name}.IsRequiredOverride"));

            // IsRequiredOverride: true → false/null
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
            if (bf.Order != af.Order || bf.Group != af.Group || bf.Label != af.Label ||
                bf.Placeholder != af.Placeholder || bf.HelpText != af.HelpText || bf.Metadata != af.Metadata)
            {
                // Only add presentation finding if no structural/contract findings exist for this field
                var hasStructuralFindings = findings.Any(f =>
                    f.Path == $"Fields.{name}" || f.Path?.StartsWith($"Fields.{name}.") == true);
                if (!hasStructuralFindings)
                {
                    findings.Add(MakeFinding(change, "COMPAT_FORM_PRESENTATION_ONLY",
                        DescriptorCompatibilityLevel.Compatible,
                        $"Form field '{name}' presentation-only changes (order/group/labels).",
                        affectedRefs, $"Fields.{name}.Presentation"));
                }
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
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorCompatibility/FormCompatibilityRule.cs
git commit -m "feat(6d): add FormCompatibilityRule with schema, field, and override checks"
```

---

## Task 7: Implementation — Capability + Event Rules

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorCompatibility/CapabilityCompatibilityRule.cs`
- Create: `framework/src/CrestCreates.Metadata/DescriptorCompatibility/EventCompatibilityRule.cs`

- [ ] **Step 1: Write `CapabilityCompatibilityRule.cs`**

```csharp
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
            var level = cb.OutputSchema == null && ca.OutputSchema != null
                ? DescriptorCompatibilityLevel.Risky
                : DescriptorCompatibilityLevel.Breaking;
            var ruleId = cb.OutputSchema == null && ca.OutputSchema != null
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
```

- [ ] **Step 2: Write `EventCompatibilityRule.cs`**

Supports both `EventDescriptor` and `GeneratedEventDescriptor`:

```csharp
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
            && (after is IEventDescriptor || before is IEventDescriptor);
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
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorCompatibility/CapabilityCompatibilityRule.cs framework/src/CrestCreates.Metadata/DescriptorCompatibility/EventCompatibilityRule.cs
git commit -m "feat(6d): add Capability and Event compatibility rules with security-sensitive classification"
```

---

## Task 8: Implementation — HumanTask + Workflow Rules

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorCompatibility/HumanTaskCompatibilityRule.cs`
- Create: `framework/src/CrestCreates.Metadata/DescriptorCompatibility/WorkflowCompatibilityRule.cs`

- [ ] **Step 1: Write `HumanTaskCompatibilityRule.cs`**

```csharp
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
                    DescriptorCompatibilityLevel.Breaking, "HumanTask schema ref changed.",
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

            // Outcomes
            var beforeOutcomes = hb.Outcomes.ToDictionary(o => (o.Condition, o.Capability?.Id, o.Capability?.Version));
            var afterOutcomes = ha.Outcomes.ToDictionary(o => (o.Condition, o.Capability?.Id, o.Capability?.Version));

            foreach (var key in beforeOutcomes.Keys.Except(afterOutcomes.Keys))
                findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_OUTCOME_REMOVED",
                    DescriptorCompatibilityLevel.Breaking,
                    $"Completion outcome '{key.Condition}' removed.", affectedRefs, $"Outcomes.{key.Condition}"));

            foreach (var key in afterOutcomes.Keys.Except(beforeOutcomes.Keys))
                findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_OUTCOME_ADDED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Completion outcome '{key.Condition}' added.", affectedRefs, $"Outcomes.{key.Condition}"));

            foreach (var key in beforeOutcomes.Keys.Intersect(afterOutcomes.Keys))
            {
                if (beforeOutcomes[key].Capability?.Id != afterOutcomes[key].Capability?.Id ||
                    beforeOutcomes[key].Capability?.Version != afterOutcomes[key].Capability?.Version)
                    findings.Add(MakeFinding(change, "COMPAT_HUMANTASK_OUTCOME_CAPABILITY_CHANGED",
                        DescriptorCompatibilityLevel.Breaking,
                        $"Outcome '{key.Condition}' capability changed.", affectedRefs, $"Outcomes.{key.Condition}.Capability"));
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
```

- [ ] **Step 2: Write `WorkflowCompatibilityRule.cs`**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.DescriptorCompatibility;

public sealed class WorkflowCompatibilityRule : IDescriptorCompatibilityRule
{
    public string RuleId => "Workflow";

    public bool CanAnalyze(DescriptorChange change, IDescriptor? before, IDescriptor? after)
    {
        return change.Kind is DescriptorChangeKind.ContractHashChanged or DescriptorChangeKind.Updated
            && (after is WorkflowDescriptor || before is WorkflowDescriptor);
    }

    public IReadOnlyList<DescriptorCompatibilityFinding> Analyze(
        DescriptorChange change, IDescriptor? before, IDescriptor? after,
        DescriptorImpactAnalysisReport impactReport, DescriptorCompatibilityAnalysisOptions options)
    {
        var findings = new List<DescriptorCompatibilityFinding>();
        var wb = before as WorkflowDescriptor;
        var wa = after as WorkflowDescriptor;
        if (wa == null) return findings;

        var affectedRefs = GetAffectedRefs(change, impactReport);

        if (wb != null)
        {
            // Variable schema
            if (!SchemaRefsEqual(wb.VariableSchema, wa.VariableSchema))
                findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_VARIABLE_SCHEMA_CHANGED",
                    DescriptorCompatibilityLevel.Breaking, "Workflow variable schema ref changed.",
                    affectedRefs, "VariableSchema"));

            // Steps: compare by Id
            var beforeSteps = wb.Steps.ToDictionary(s => s.Id);
            var afterSteps = wa.Steps.ToDictionary(s => s.Id);

            foreach (var id in beforeSteps.Keys.Except(afterSteps.Keys))
                findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_STEP_REMOVED",
                    DescriptorCompatibilityLevel.Breaking, $"Workflow step '{id}' removed.",
                    affectedRefs, $"Steps.{id}"));

            foreach (var id in afterSteps.Keys.Except(beforeSteps.Keys))
                findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_STEP_ADDED",
                    DescriptorCompatibilityLevel.Risky, $"Workflow step '{id}' added.",
                    affectedRefs, $"Steps.{id}"));

            foreach (var id in beforeSteps.Keys.Intersect(afterSteps.Keys))
            {
                var bs = beforeSteps[id];
                var as_ = afterSteps[id];

                if (bs.Target.GetType() != as_.Target.GetType() ||
                    GetTargetRef(bs.Target) != GetTargetRef(as_.Target))
                    findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_STEP_TARGET_CHANGED",
                        DescriptorCompatibilityLevel.Breaking, $"Step '{id}' target changed.",
                        affectedRefs, $"Steps.{id}.Target"));

                if (!bs.Transitions.SequenceEqual(as_.Transitions))
                    findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_TRANSITIONS_CHANGED",
                        DescriptorCompatibilityLevel.Breaking, $"Step '{id}' transitions changed.",
                        affectedRefs, $"Steps.{id}.Transitions"));

                if (bs.OnError != as_.OnError)
                    findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_ERROR_BEHAVIOR_CHANGED",
                        DescriptorCompatibilityLevel.Risky, $"Step '{id}' OnError changed.",
                        affectedRefs, $"Steps.{id}.OnError"));

                if (bs.Condition != as_.Condition || bs.InputMapping != as_.InputMapping ||
                    bs.OutputMapping != as_.OutputMapping)
                    findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_MAPPING_CHANGED",
                        DescriptorCompatibilityLevel.Risky, $"Step '{id}' condition/mapping changed.",
                        affectedRefs, $"Steps.{id}.Mapping"));
            }

            // Default variable scope
            if (wb.DefaultVariableScope != wa.DefaultVariableScope)
                findings.Add(MakeFinding(change, "COMPAT_WORKFLOW_VARIABLE_SCOPE_CHANGED",
                    DescriptorCompatibilityLevel.Risky,
                    $"Workflow variable scope changed from {wb.DefaultVariableScope} to {wa.DefaultVariableScope}.",
                    affectedRefs, "DefaultVariableScope"));
        }

        return findings;
    }

    private static string? GetTargetRef(InteractionTarget target) => target switch
    {
        CapabilityTarget ct => ct.Capability.Id,
        HumanTaskTarget ht => ht.HumanTask.Id,
        SubWorkflowTarget sw => sw.SubWorkflow.Id,
        _ => target.GetType().Name
    };

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
            Kind = DescriptorCompatibilityFindingKind.Contract, RuleId = ruleId,
            Message = message, AffectedRefs = affectedRefs, Path = path
        };
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorCompatibility/HumanTaskCompatibilityRule.cs framework/src/CrestCreates.Metadata/DescriptorCompatibility/WorkflowCompatibilityRule.cs
git commit -m "feat(6d): add HumanTask and Workflow compatibility rules"
```

---

## Task 9: Implementation — Analyzer Orchestrator

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorCompatibility/DescriptorCompatibilityAnalyzer.cs`

The main orchestrator: validates inputs, builds indexes, dispatches rules, deduplicates findings, computes MaxLevel, maps diagnostics.

- [ ] **Step 1: Write `DescriptorCompatibilityAnalyzer.cs`**

```csharp
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
        if (impactReport.ChangeSet != changeSet)
        {
            diagnostics.Add(new DescriptorCompatibilityDiagnostic(
                DiagnosticSeverity.Error, "COMPAT_CHANGESET_MISMATCH",
                "Provided changeSet differs from impactReport.ChangeSet.", null, null));
            // Continue with the explicit changeSet
        }

        // Step 2: Build before/after indexes
        var beforeIndex = BuildDescriptorIndex(before, diagnostics);
        var afterIndex = BuildDescriptorIndex(after, diagnostics);

        // Step 3: Build affected index from impact report
        var affectedIndex = BuildAffectedIndex(impactReport);

        // Step 4: Map impact diagnostics to compatibility diagnostics + unsupported findings
        var unsupportedFindings = new List<DescriptorCompatibilityFinding>();
        MapImpactDiagnostics(impactReport, diagnostics, unsupportedFindings, options.TreatImpactWarningsAsUnsupported);

        // Step 5: For each change, run rules
        var findings = new List<DescriptorCompatibilityFinding>();
        foreach (var change in changeSet.Changes)
        {
            var beforeDesc = ResolveDescriptor(change.Ref, beforeIndex);
            var afterDesc = ResolveDescriptor(change.Ref, afterIndex);

            // Run descriptor-specific rules first
            bool classified = false;
            foreach (var rule in _rules.Take(_rules.Count - 1)) // All except Generic
            {
                if (!rule.CanAnalyze(change, beforeDesc, afterDesc)) continue;
                var ruleFindings = rule.Analyze(change, beforeDesc, afterDesc, impactReport, options);
                findings.AddRange(ruleFindings);
                if (ruleFindings.Count > 0)
                    classified = true;
            }

            // Run generic rule as catch-all
            var genericFindings = _rules.Last().Analyze(change, beforeDesc, afterDesc, impactReport, options);
            findings.AddRange(genericFindings);

            // If no rule could classify a contract hash change, mark Unsupported
            if (change.Kind == DescriptorChangeKind.ContractHashChanged && !classified)
            {
                var afRefs = GetAffectedRefs(change, impactReport);
                var level = afRefs.Count > 0
                    ? DescriptorCompatibilityLevel.Unsupported
                    : DescriptorCompatibilityLevel.Risky;
                findings.Add(new DescriptorCompatibilityFinding
                {
                    Subject = change.Ref, ChangeKind = change.Kind, Level = level,
                    Kind = DescriptorCompatibilityFindingKind.Analysis,
                    RuleId = "COMPAT_GENERIC_UNCLASSIFIED_CONTRACT_CHANGE",
                    Message = afRefs.Count > 0
                        ? $"Contract hash changed with {afRefs.Count} affected consumer(s). No descriptor-specific rule classified this change."
                        : "Contract hash changed with no affected consumers. No descriptor-specific rule classified this change.",
                    AffectedRefs = afRefs
                });
            }
        }

        // Step 6: Add unsupported findings from impact diagnostics
        findings.AddRange(unsupportedFindings);

        // Step 7: Deduplicate by (Subject, RuleId, Path, Level)
        findings = DeduplicateFindings(findings);

        // Step 8: Filter compatible findings if option says so
        if (!options.IncludeCompatibleFindings)
            findings = findings.Where(f => f.Level != DescriptorCompatibilityLevel.Compatible).ToList();

        // Step 9: Sort deterministically
        findings = findings
            .OrderBy(f => f.Subject.Namespace)
            .ThenBy(f => f.Subject.Id)
            .ThenBy(f => f.Subject.Version ?? 0)
            .ThenByDescending(f => (int)f.Level)
            .ThenBy(f => f.RuleId)
            .ThenBy(f => f.Path ?? string.Empty)
            .ToList();

        // Step 10: Compute MaxLevel from classified findings only
        var classifiedFindings = findings
            .Where(f => f.Level != DescriptorCompatibilityLevel.Unsupported)
            .Select(f => f.Level)
            .ToArray();

        var maxLevel = classifiedFindings.Length > 0
            ? (DescriptorCompatibilityLevel)classifiedFindings.Max((Func<DescriptorCompatibilityLevel, int>)(l => (int)l))
            : DescriptorCompatibilityLevel.Unsupported;

        return new DescriptorCompatibilityReport
        {
            ChangeSet = changeSet,
            ImpactReport = impactReport,
            Findings = findings,
            MaxLevel = maxLevel,
            Diagnostics = diagnostics
        };
    }

    // === Index helpers ===

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

    private static Dictionary<DescriptorRef, List<DescriptorImpactPath>> BuildAffectedIndex(
        DescriptorImpactAnalysisReport impactReport)
    {
        var index = new Dictionary<DescriptorRef, List<DescriptorImpactPath>>();
        foreach (var path in impactReport.Paths)
        {
            if (!index.TryGetValue(path.SourceChange, out var list))
            {
                list = new List<DescriptorImpactPath>();
                index[path.SourceChange] = list;
            }
            list.Add(path);
        }
        return index;
    }

    // === Descriptor resolution ===

    private static IDescriptor? ResolveDescriptor(
        DescriptorRef targetRef,
        Dictionary<DescriptorRef, IDescriptor> index)
    {
        // Handle Added: no before descriptor
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
            var (code, severity) = diag.Code switch
            {
                "IMPACT_TOPOLOGY_MISSING_TARGET" => ("COMPAT_BLOCKED_BY_TOPOLOGY_ERROR", DiagnosticSeverity.Error),
                var c when c.StartsWith("IMPACT_TOPOLOGY_") => ("COMPAT_ANALYSIS_INCOMPLETE", DiagnosticSeverity.Error),
                "IMPACT_AMBIGUOUS_UNPINNED_TARGET" => ("COMPAT_VERSION_AMBIGUITY", DiagnosticSeverity.Warning),
                "IMPACT_PATH_TRUNCATED" => ("COMPAT_ANALYSIS_INCOMPLETE", DiagnosticSeverity.Warning),
                "IMPACT_DESCRIPTOR_NOT_IN_TOPOLOGY" => ("COMPAT_CHANGE_NOT_IN_TOPOLOGY", DiagnosticSeverity.Warning),
                _ => (null, (DiagnosticSeverity?)null)
            };

            if (code == null) continue;

            diagnostics.Add(new DescriptorCompatibilityDiagnostic(
                severity!.Value, code, diag.Message, diag.Subject, diag.RelatedRefs));

            // Add Unsupported finding for error-level diagnostics
            if (diag.Severity == DiagnosticSeverity.Error || treatWarningsAsUnsupported)
            {
                var findingKind = code == "COMPAT_BLOCKED_BY_TOPOLOGY_ERROR"
                    ? DescriptorCompatibilityFindingKind.Analysis
                    : DescriptorCompatibilityFindingKind.Analysis;

                unsupportedFindings.Add(new DescriptorCompatibilityFinding
                {
                    Subject = diag.Subject ?? new DescriptorRef(string.Empty, string.Empty) { },
                    ChangeKind = DescriptorChangeKind.ContractHashChanged,
                    Level = DescriptorCompatibilityLevel.Unsupported,
                    Kind = findingKind,
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
}
```

Note: The `Max()` with `int` cast uses a custom extension or explicit lambda. For AoT / cleaner code, use a helper:

```csharp
private static DescriptorCompatibilityLevel ComputeMaxLevel(IEnumerable<DescriptorCompatibilityLevel> levels)
{
    var max = DescriptorCompatibilityLevel.Compatible;
    foreach (var l in levels)
    {
        if ((int)l > (int)max) max = l;
    }
    return max;
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorCompatibility/DescriptorCompatibilityAnalyzer.cs
git commit -m "feat(6d): add DescriptorCompatibilityAnalyzer orchestrator with rule dispatch and diagnostic mapping"
```

---

## Task 10: DI Registration

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs`

- [ ] **Step 1: Add `AddDescriptorCompatibilityAnalysis()` extension method**

Add after the existing `AddDescriptorImpactAnalysis` method:

```csharp
public static IServiceCollection AddDescriptorCompatibilityAnalysis(this IServiceCollection services)
{
    services.TryAddSingleton<IDescriptorCompatibilityAnalyzer, DescriptorCompatibilityAnalyzer>();
    return services;
}
```

Requires: `using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;` and `using CrestCreates.Metadata.DescriptorCompatibility;`

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs
git commit -m "feat(6d): add AddDescriptorCompatibilityAnalysis DI extension"
```

---

## Task 11: Tests — Generic Compatibility Rules

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/DescriptorCompatibilityAnalyzerGenericTests.cs`

- [ ] **Step 1: Write generic rule tests (~11 tests)**

Test structure follows Phase 6c pattern: create test descriptors, build changeSet and impactReport, call analyzer, assert findings.

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.DescriptorCompatibility;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorCompatibility;

public class DescriptorCompatibilityAnalyzerGenericTests
{
    private static readonly IDescriptorCompatibilityAnalyzer Analyzer = new DescriptorCompatibilityAnalyzer();

    private static DescriptorRef TestRef => new("test", "T1", 1);

    private static DescriptorChange MakeChange(DescriptorChangeKind kind,
        DescriptorState? beforeState = null, DescriptorState? afterState = null,
        string? beforeHash = null, string? afterHash = null)
        => new()
        {
            Ref = TestRef, Kind = kind,
            BeforeState = beforeState, AfterState = afterState,
            BeforeContractHash = beforeHash, AfterContractHash = afterHash
        };

    private static DescriptorImpactAnalysisReport MakeImpactReport(
        DescriptorChangeSet changeSet,
        params DescriptorRef[] affectedRefs)
    {
        var paths = affectedRefs.Select(r => new DescriptorImpactPath
        {
            SourceChange = TestRef, Affected = r, Segments = Array.Empty<DescriptorImpactPathSegment>()
        }).ToArray();

        return new DescriptorImpactAnalysisReport
        {
            ChangeSet = changeSet,
            AffectedDescriptors = affectedRefs.Select(r => new AffectedDescriptor
            {
                Ref = r, Kind = DescriptorKind.Schema, Name = r.FullId,
                Severity = DescriptorImpactSeverity.Low,
                RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Schema },
                Paths = paths.Where(p => p.Affected == r).ToArray()
            }).ToArray(),
            Paths = paths,
            MaxSeverity = DescriptorImpactSeverity.Low,
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };
    }

    [Fact]
    public void AddedDescriptor_ReturnsCompatible()
    {
        var change = MakeChange(DescriptorChangeKind.Added);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().ContainSingle(f => f.RuleId == "COMPAT_GENERIC_ADDED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void RemovedDescriptor_WithAffectedConsumers_ReturnsBreaking()
    {
        var change = MakeChange(DescriptorChangeKind.Removed, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var report = MakeImpactReport(cs, consumer);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_GENERIC_REMOVED_WITH_CONSUMERS" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void RemovedDescriptor_WithoutAffectedConsumers_ReturnsRiskyByDefault()
    {
        var change = MakeChange(DescriptorChangeKind.Removed, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_GENERIC_REMOVED_NO_CONSUMERS" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void RemovedDescriptor_WithoutConsumers_OptionDisabled_ReturnsCompatible()
    {
        var change = MakeChange(DescriptorChangeKind.Removed, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);
        var options = new DescriptorCompatibilityAnalysisOptions { TreatRemovedWithoutConsumersAsRisky = false };

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report, options);

        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_GENERIC_REMOVED_NO_CONSUMERS" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void DeprecatedDescriptor_WithAffectedConsumers_ReturnsRisky()
    {
        var change = MakeChange(DescriptorChangeKind.Deprecated, DescriptorState.Active, DescriptorState.Deprecated);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var report = MakeImpactReport(cs, consumer);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_GENERIC_DEPRECATED_WITH_CONSUMERS" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void ActivatedDescriptor_ReturnsCompatible()
    {
        var change = MakeChange(DescriptorChangeKind.Activated, DescriptorState.Draft, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_GENERIC_ACTIVATED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void StateChangedToRemoved_WithConsumers_ReturnsBreaking()
    {
        var change = MakeChange(DescriptorChangeKind.StateChanged, DescriptorState.Active, DescriptorState.Removed);
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var report = MakeImpactReport(cs, consumer);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_GENERIC_STATE_REMOVED" && f.Level == DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void Updated_Normal_ReturnsCompatible()
    {
        var change = MakeChange(DescriptorChangeKind.Updated, beforeHash: "hash1", afterHash: "hash1");
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_GENERIC_UPDATED" && f.Level == DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void Updated_UnexpectedHashChange_ReturnsRisky()
    {
        var change = MakeChange(DescriptorChangeKind.Updated, beforeHash: "hash1", afterHash: "hash2");
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var report = MakeImpactReport(cs);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.Findings.Should().Contain(f => f.RuleId == "COMPAT_GENERIC_UPDATED_UNEXPECTED" && f.Level == DescriptorCompatibilityLevel.Risky);
    }

    [Fact]
    public void MaxLevel_ReportsHighestLevel()
    {
        var change1 = MakeChange(DescriptorChangeKind.Added);
        var change2 = MakeChange(DescriptorChangeKind.Removed, DescriptorState.Active);
        var cs = new DescriptorChangeSet { Changes = new[] { change1, change2 } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var report = MakeImpactReport(cs, consumer);

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, report);

        result.MaxLevel.Should().Be(DescriptorCompatibilityLevel.Breaking);
    }

    [Fact]
    public void MaxLevel_DoesNotTreatUnsupportedAsMoreSevere()
    {
        var change = MakeChange(DescriptorChangeKind.ContractHashChanged, beforeHash: "h1", afterHash: "h2");
        var cs = new DescriptorChangeSet { Changes = new[] { change } };
        var consumer = new DescriptorRef("consumer", "C1", 1);
        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = cs,
            AffectedDescriptors = new[]
            {
                new AffectedDescriptor
                {
                    Ref = consumer, Kind = DescriptorKind.Schema, Name = consumer.FullId,
                    Severity = DescriptorImpactSeverity.Low,
                    RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Schema },
                    Paths = new[]
                    {
                        new DescriptorImpactPath { SourceChange = TestRef, Affected = consumer,
                            Segments = Array.Empty<DescriptorImpactPathSegment>() }
                    }
                }
            },
            Paths = new[]
            {
                new DescriptorImpactPath { SourceChange = TestRef, Affected = consumer,
                    Segments = Array.Empty<DescriptorImpactPathSegment>() }
            },
            MaxSeverity = DescriptorImpactSeverity.Low,
            Diagnostics = new[] { new DescriptorImpactDiagnostic(
                DiagnosticSeverity.Error, "IMPACT_TOPOLOGY_MISSING_TARGET",
                "Missing target", TestRef, new[] { consumer }) }
        };

        var result = Analyzer.Analyze(Array.Empty<IDescriptor>(), Array.Empty<IDescriptor>(), cs, impactReport);

        // Impact error adds Unsupported, but MaxLevel should not treat it as more severe
        // Since the contract change finding is Risky, MaxLevel should be Risky (not Unsupported)
        result.MaxLevel.Should().Be(DescriptorCompatibilityLevel.Risky);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~DescriptorCompatibilityAnalyzerGenericTests"`
Expected: 11 tests PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/DescriptorCompatibilityAnalyzerGenericTests.cs
git commit -m "test(6d): add 11 generic compatibility rule tests"
```

---

## Task 12: Tests — Schema Compatibility

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/SchemaCompatibilityRuleTests.cs`

- [ ] **Step 1: Write schema compatibility tests (~8 tests)**

Create `SchemaCompatibilityRuleTests.cs` with tests for:
- `Schema_OptionalFieldAdded_Compatible`
- `Schema_RequiredFieldAdded_Breaking`
- `Schema_FieldRemoved_BreakingWithConsumers`
- `Schema_FieldTypeChanged_Breaking`
- `Schema_RequiredRelaxed_Compatible`
- `Schema_ConstraintNarrowed_Breaking` (MaxLength decreased)
- `Schema_ConstraintRelaxed_Compatible` (MaxLength increased)
- `Schema_DeclaredBreaking_UpgradesToBreaking`

Each test creates actual `SchemaDescriptor` instances with before/after fields, constructs a changeSet with ContractHashChanged, builds an impact report with affected consumers where needed, and asserts compatibility findings.

- [ ] **Step 2: Run tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~SchemaCompatibilityRuleTests"`
Expected: 8 tests PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/SchemaCompatibilityRuleTests.cs
git commit -m "test(6d): add 8 schema compatibility rule tests"
```

---

## Task 13: Tests — Form + Capability

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/FormCompatibilityRuleTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/CapabilityCompatibilityRuleTests.cs`

- [ ] **Step 1: Write form compatibility tests (~5 tests)**

Tests:
- `Form_SchemaRefChanged_Breaking`
- `Form_FieldRemoved_BreakingWithConsumers`
- `Form_FieldAdded_Compatible`
- `Form_RequiredOverrideAdded_Breaking`
- `Form_ControlTypeChanged_Risky`

- [ ] **Step 2: Write capability compatibility tests (~5 tests)**

Tests:
- `Capability_InputSchemaChanged_Breaking`
- `Capability_OutputSchemaChanged_Breaking`
- `Capability_PermissionAdded_SecuritySensitive`
- `Capability_PermissionRemoved_SecuritySensitive`
- `Capability_RiskLevelChanged_SecuritySensitive`

- [ ] **Step 3: Run tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~FormCompatibilityRuleTests|CapabilityCompatibilityRuleTests"`
Expected: 10 tests PASS

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/FormCompatibilityRuleTests.cs framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/CapabilityCompatibilityRuleTests.cs
git commit -m "test(6d): add 10 form and capability compatibility rule tests"
```

---

## Task 14: Tests — Event, HumanTask, Workflow + Diagnostics

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/EventCompatibilityRuleTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/HumanTaskCompatibilityRuleTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/WorkflowCompatibilityRuleTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/DescriptorCompatibilityDiagnosticsTests.cs`

- [ ] **Step 1: Write event tests (~4 tests)**

Tests:
- `Event_PayloadSchemaChanged_Breaking` (both EventDescriptor and GeneratedEventDescriptor)
- `Event_DeclaredBreaking_UpgradesToBreaking`
- `GeneratedEvent_ReliabilityChanged_Risky`
- `GeneratedEvent_ScopeChanged_Risky`

- [ ] **Step 2: Write HumanTask tests (~5 tests)**

Tests:
- `HumanTask_InteractionChanged_Breaking`
- `HumanTask_AssigneeStrategyChanged_Risky`
- `HumanTask_OutcomeRemoved_Breaking`
- `HumanTask_OutcomeCapabilityChanged_Breaking`
- `HumanTask_PermissionChanged_SecuritySensitive`

- [ ] **Step 3: Write workflow tests (~5 tests)**

Tests:
- `Workflow_VariableSchemaChanged_Breaking`
- `Workflow_StepRemoved_Breaking`
- `Workflow_StepAdded_Risky`
- `Workflow_StepTargetChanged_Breaking`
- `Workflow_TransitionsChanged_Breaking`

- [ ] **Step 4: Write diagnostics tests (~5 tests)**

Tests:
- `ImpactTopologyError_AddsCompatibilityDiagnostic`
- `ImpactPathTruncated_AddsAnalysisIncompleteDiagnostic`
- `ImpactError_AddsUnsupportedFinding`
- `DuplicateDescriptorRefs_AddsDiagnostic`
- `ChangeSetMismatch_AddsDiagnostic`

- [ ] **Step 5: Run all Phase 6d tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~DescriptorCompatibility"`
Expected: ~48 tests PASS (11 generic + 8 schema + 5 form + 5 capability + 4 event + 5 humantask + 5 workflow + 5 diagnostics)

- [ ] **Step 6: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/EventCompatibilityRuleTests.cs framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/HumanTaskCompatibilityRuleTests.cs framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/WorkflowCompatibilityRuleTests.cs framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/DescriptorCompatibilityDiagnosticsTests.cs
git commit -m "test(6d): add 19 event, humantask, workflow, and diagnostics compatibility tests"
```

---

## Task 15: Regression + Docs

- [ ] **Step 1: Run full Metadata test suite**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj`
Expected: All 194 pre-existing tests + ~48 new tests = ~242 tests PASS, 0 failures

- [ ] **Step 2: Run full solution test suite**

Run: `dotnet test`
Expected: All existing tests pass, no regressions across Form (38), Capability (124), Event (41), HumanTask (51), Workflow (68)

- [ ] **Step 3: Update `docs/Feature/Descriptor/arch-design.md`**

Add Section 10 for Phase 6d with the same pattern as Sections 7-9. Document:
- Analyzer interface and rule model
- Generic rule table
- Descriptor-specific rule summary
- Impact integration boundary
- DI registration

- [ ] **Step 4: Update `docs/Feature/Descriptor/usage-guide.md`**

Add Section 9 for Phase 6d usage:
- How to call `IDescriptorCompatibilityAnalyzer`
- How to interpret `DescriptorCompatibilityReport`
- How `MaxLevel` and `HasBreakingChanges`/`HasSecuritySensitiveChanges` work
- How `Unsupported` differs from `Breaking`

- [ ] **Step 5: Update `memory.md`**

Add Phase 6d entry matching the existing 6a/6b/6c entry style:

```markdown
## Phase 6d — Compatibility / Breaking Change Analyzer

**Status:** ✅ Complete (2026-06-13)

**What:** `IDescriptorCompatibilityAnalyzer` consumes before/after descriptor inventories, `DescriptorChangeSet`, and `DescriptorImpactAnalysisReport` to produce a deterministic `DescriptorCompatibilityReport`. Rule-based: generic rules cover all `DescriptorChangeKind` values, descriptor-specific rules cover 6 descriptor kinds (Schema, Form, Capability, Event, HumanTask, Workflow). Classifies changes as Compatible, Risky, SecuritySensitive, Breaking, or Unsupported.

**Key Architecture:**
- Stateless singleton, pure function over inputs
- `IDescriptorCompatibilityRule` interface with public surface for future module-owned rules
- Generic rule dispatches all 7 ChangeKind values; specific rules fire on ContractHashChanged/Updated
- `DescriptorCompatibilityLevel.Unsupported = 0` — excluded from `MaxLevel` (natural Max() ignores it)
- Impact severity (6c) is never directly projected into compatibility (6d)
- No topology access, no new impact traversal, no lifecycle governance

**Files:** ~17 new (7 abstractions + 8 implementation + 2 test suites), ~48 new tests, 1 modified (DI)
```

- [ ] **Step 6: Commit docs**

```bash
git add docs/Feature/Descriptor/arch-design.md docs/Feature/Descriptor/usage-guide.md memory.md
git commit -m "docs: update docs and memory.md with Phase 6d completion"
```

---

## Plan Summary

| Task | Files | Tests | Est. Time |
|---|---|---|---|
| 1. Enums | 2 new | 0 | 5 min |
| 2. Core Types | 4 new | 0 | 10 min |
| 3. Interfaces | 2 new | 0 | 5 min |
| 4. Generic Rule | 1 new | 0 | 15 min |
| 5. Schema Rule | 1 new | 0 | 20 min |
| 6. Form Rule | 1 new | 0 | 15 min |
| 7. Capability + Event Rules | 2 new | 0 | 20 min |
| 8. HumanTask + Workflow Rules | 2 new | 0 | 20 min |
| 9. Analyzer Orchestrator | 1 new | 0 | 25 min |
| 10. DI Registration | 1 modify | 0 | 5 min |
| 11. Generic Tests | 1 new | 11 | 15 min |
| 12. Schema Tests | 1 new | 8 | 15 min |
| 13. Form + Capability Tests | 2 new | 10 | 15 min |
| 14. Event + HumanTask + Workflow + Diagnostics | 4 new | 19 | 20 min |
| 15. Regression + Docs | 3 modify | all | 15 min |

**Total: ~20 new files, 4 modified files, ~48 new tests, ~3.5 hours**
