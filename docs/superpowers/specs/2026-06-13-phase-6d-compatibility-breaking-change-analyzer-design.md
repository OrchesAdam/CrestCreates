# Phase 6d — Compatibility / Breaking Change Analyzer: Design Spec

> **Date:** 2026-06-13 | **Status:** Draft | **Phase 6d**

---

## 1. Overview

### 1.1 Goal

Phase 6d answers:

> Given before/after descriptor inventories, the `DescriptorChangeSet`, and the Phase 6c impact report, is each descriptor change compatible, risky, breaking, security-sensitive, or unsupported?

The output is a deterministic compatibility report that explains why a change is safe or unsafe for existing consumers.

### 1.2 Position in Phase 6

```
before descriptor inventory
        +
after descriptor inventory
        +
DescriptorChangeSet              (Phase 6c input)
        +
DescriptorImpactAnalysisReport   (Phase 6c output)
        ↓
IDescriptorCompatibilityAnalyzer
        ↓
DescriptorCompatibilityReport
```

Phase 6d sits **on top of Phase 6c**. It does not rebuild topology, rediscover relationships, or perform impact traversal.

### 1.3 Design Principles

1. **Consume impact, do not recompute it** — `DescriptorImpactAnalysisReport` is the only impact input.
2. **Rule-based compatibility** — do not collapse 6c `DescriptorImpactSeverity` into compatibility level.
3. **Contract-hash aligned** — descriptor-specific checks must track the contract fields already used by `DescriptorHashComputer`.
4. **Explicit before/after indexes** — use the provided inventories to resolve descriptors for each `DescriptorChange`.
5. **Stateless and deterministic** — analyzer and rules are pure functions over inputs.
6. **AoT-friendly** — records, enums, static dispatch via `is`/pattern matching; no runtime reflection, dynamic, expression trees, script engines, or service location.
7. **Classification only** — lifecycle blocking, activation, approval, migration, and persistence belong to later phases.

---

## 2. Scope Boundary

### 2.1 In Scope

- Compatibility report model.
- `IDescriptorCompatibilityAnalyzer`.
- Generic compatibility rules for `Added`, `Removed`, `Deprecated`, `Activated`, `StateChanged`, `Updated`, and `ContractHashChanged`.
- Descriptor-kind-specific rules for current descriptor types:
  - `SchemaDescriptor`
  - `FormDescriptor`
  - `CapabilityDescriptor`
  - `EventDescriptor`
  - `GeneratedEventDescriptor`
  - `HumanTaskDescriptor`
  - `WorkflowDescriptor`
- Security-sensitive classification for permission expansion and data-permission scope widening where the current descriptors expose enough data.
- Diagnostics mapped from impact analysis incompleteness.
- DI registration in `MetadataServiceCollectionExtensions`.
- Focused tests in `CrestCreates.Metadata.Tests`.

### 2.2 Out of Scope

- Topology building or relationship extraction.
- New impact traversal or a replacement for `IDescriptorImpactAnalyzer`.
- Lifecycle governance, activation gates, approval workflows, or publish decisions.
- Descriptor package persistence, registry refresh, or snapshot storage.
- Runtime instance lookup for workflows, human tasks, events, or capability executions.
- Migration generation, database migration analysis, SQL/LINQ/Mongo generation.
- LLM remediation generation.
- API/UI/MCP exposure.
- Changes to `Capability Authorization`, `DataPermission Runtime`, `HumanTask Runtime`, `Workflow Runtime`, or claims/token logic.
- New descriptor model fields just to support 6d.

---

## 3. Core Types

All new abstractions live under:

```
framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/
```

Implementation lives in:

```
framework/src/CrestCreates.Metadata/
```

### 3.1 Compatibility Level

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

Semantics:

| Level | Meaning |
|---|---|
| `Compatible` | Known-safe structural or contract change. |
| `Risky` | Behavior may change or review is recommended, but existing consumers are not known to break. |
| `SecuritySensitive` | Access/security posture changed and requires explicit review. This is not automatically `Breaking`. |
| `Breaking` | Existing consumers may fail, need migration, or become invalid. |
| `Unsupported` | Current analyzer cannot safely classify the change, usually due to incomplete impact data or missing rule coverage. |

`Unsupported` is **not** a more severe `Breaking`. It means the analyzer does not have enough rule coverage or semantic knowledge to classify the change. Phase 6d only reports this fact. Phase 6e can later decide policy, for example:

| 6d result | Possible 6e policy |
|---|---|
| `Unsupported` | mandatory human review |
| `Breaking` | migration and/or approval required |
| `Risky` | review recommended |
| `Compatible` | fast path allowed |

`MaxLevel` must not treat `Unsupported` as higher severity than `Breaking`. It represents the highest classified compatibility level. Reports also expose `HasUnsupportedFindings` for governance layers.

### 3.2 Finding Kind

```csharp
public enum DescriptorCompatibilityFindingKind
{
    Structural,
    Contract,
    Behavior,
    Security,
    Analysis
}
```

### 3.3 Diagnostic

```csharp
public sealed record DescriptorCompatibilityDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DescriptorRef? Subject,
    IReadOnlyList<DescriptorRef>? RelatedRefs);
```

Use the existing `DiagnosticSeverity` from `DescriptorTopology`.

### 3.4 Finding

```csharp
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

Notes:

- `Subject` is the changed descriptor ref.
- `AffectedRefs` comes from Phase 6c affected descriptors and paths.
- `Path` is a stable descriptor-local path such as `Fields.Amount.IsRequired`, `Permissions`, `Steps.approve.Target`, or `Outcomes.Approve`.
- `BeforeValue` and `AfterValue` are small diagnostic strings only; do not serialize full descriptors.

### 3.5 Report

```csharp
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

### 3.6 Options

```csharp
public sealed record DescriptorCompatibilityAnalysisOptions
{
    public bool TreatRemovedWithoutConsumersAsRisky { get; init; } = true;
    public bool TreatUnknownDescriptorKindAsUnsupported { get; init; } = true;
    public bool TreatImpactWarningsAsUnsupported { get; init; } = false;
    public bool IncludeCompatibleFindings { get; init; } = true;
}
```

Default behavior should be review-friendly but not policy-enforcing.

### 3.7 Analyzer Interface

```csharp
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

The analyzer is a stateless singleton.

---

## 4. Implementation Architecture

### 4.1 Analyzer Flow

`DescriptorCompatibilityAnalyzer` performs:

1. Validate `impactReport.ChangeSet` and provided `changeSet` refer to the same ordered set of refs/kinds. If not, add `COMPAT_CHANGESET_MISMATCH` and continue with the explicit `changeSet`.
2. Build `beforeByRef` and `afterByRef` indexes from `(Namespace, Id, Version)`.
3. Build an affected index from `impactReport.AffectedDescriptors` and `impactReport.Paths`.
4. Map relevant impact diagnostics into compatibility diagnostics.
5. For each `DescriptorChange`:
   - Resolve `before` and `after`.
   - Run generic rules.
   - Run descriptor-specific rules when both descriptor type and change kind are supported.
   - If no rule can classify a contract-changing update, add `Unsupported` or `Risky` according to options.
6. Deduplicate findings by `(Subject, RuleId, Path, Level)`.
7. Sort findings deterministically:
   - `Subject.Namespace`
   - `Subject.Id`
   - `Subject.Version`
   - `Level` descending
   - `RuleId`
   - `Path`
8. Compute `MaxLevel` from classified findings only (`Compatible`, `Risky`, `SecuritySensitive`, `Breaking`). If all findings are `Unsupported`, set `MaxLevel=Unsupported`.

### 4.2 Rule Dispatch

Phase 6d keeps an explicit rule model, following the same extension style as Phase 6a extractors and Phase 5h contributors:

```csharp
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

Rules can live in `CrestCreates.Metadata` for Phase 6d. Do not create module-specific projects or require every descriptor module to register rules yet. This avoids introducing a new multi-module DI surface before the rule vocabulary is stable.

The interface is public so later phases can move or add module-owned rules without changing the analyzer contract:

```
SchemaCompatibilityRules
FormCompatibilityRules
CapabilityCompatibilityRules
EventCompatibilityRules
WorkflowCompatibilityRules
HumanTaskCompatibilityRules
```

### 4.3 Descriptor Internal Parsing Boundary

Phase boundaries are intentionally different:

| Phase | May inspect descriptor internals? | Reason |
|---|---:|---|
| 6b Topology | no | topology consumes relationship extractors only |
| 6c Impact | no | impact consumes topology paths only |
| 6d Compatibility | yes | compatibility needs before/after contract semantics |

Phase 6d may inspect descriptor-specific fields only for compatibility classification. It must not mutate descriptors, rebuild topology, rediscover relationships, or validate runtime bindings.

Examples of allowed 6d internal comparisons:

- schema field removal, type change, required/nullable/constraint narrowing
- capability input/output schema and permissions changes
- event payload contract changes
- workflow step target and transition changes
- human task interaction, outcome, and permission changes
- future descriptor-owned data-permission scope changes

### 4.4 Rule Composition

Register:

```csharp
services.TryAddSingleton<IDescriptorCompatibilityAnalyzer, DescriptorCompatibilityAnalyzer>();
services.AddDescriptorImpactAnalysis();
```

Add extension:

```csharp
public static IServiceCollection AddDescriptorCompatibilityAnalysis(this IServiceCollection services)
```

It should call `AddDescriptorImpactAnalysis()` for prerequisites, but it must not build topology or inventories.

---

## 5. Generic Rules

### 5.1 Added

| Condition | Level | Rule |
|---|---:|---|
| `DescriptorChangeKind.Added` | `Compatible` | `COMPAT_GENERIC_ADDED` |

Added descriptors do not break existing consumers.

### 5.2 Removed

| Condition | Level | Rule |
|---|---:|---|
| removed with affected consumers | `Breaking` | `COMPAT_GENERIC_REMOVED_WITH_CONSUMERS` |
| removed without affected consumers, default options | `Risky` | `COMPAT_GENERIC_REMOVED_NO_CONSUMERS` |
| removed without affected consumers and option disabled | `Compatible` | `COMPAT_GENERIC_REMOVED_NO_CONSUMERS` |

Affected consumers are derived only from Phase 6c.

### 5.3 Deprecated

| Condition | Level | Rule |
|---|---:|---|
| deprecated with affected consumers | `Risky` | `COMPAT_GENERIC_DEPRECATED_WITH_CONSUMERS` |
| deprecated without affected consumers | `Compatible` | `COMPAT_GENERIC_DEPRECATED_NO_CONSUMERS` |

### 5.4 Activated / StateChanged / Updated

| Condition | Level | Rule |
|---|---:|---|
| activated | `Compatible` | `COMPAT_GENERIC_ACTIVATED` |
| state changed to `Removed` | `Breaking` if consumers, else `Risky` | `COMPAT_GENERIC_STATE_REMOVED` |
| other state changed | `Risky` | `COMPAT_GENERIC_STATE_CHANGED` |
| `Updated` (name-only by `DescriptorChangeSetBuilder` invariant: same ContractHash + different Name) | `Compatible` | `COMPAT_GENERIC_UPDATED` |
|
| `Updated` fallback (non-name change detected) | `Risky` | `COMPAT_GENERIC_UPDATED_UNEXPECTED` |

### 5.5 ContractHashChanged

Generic fallback:

| Condition | Level | Rule |
|---|---:|---|
| descriptor-specific rules produced findings | no fallback | n/a |
| no descriptor-specific rule and affected consumers exist | `Risky` | `COMPAT_GENERIC_UNCLASSIFIED_CONTRACT_CHANGE` |
| no descriptor-specific rule and no affected consumers | `Risky` | `COMPAT_GENERIC_UNCLASSIFIED_CONTRACT_CHANGE` |

This prevents contract changes from being silently marked compatible while preserving the baseline value of 6d before every descriptor-specific rule is complete.

### 5.6 Unsupported Baseline

| Condition | Level | Rule |
|---|---:|---|
| unsupported descriptor kind and option enabled | `Unsupported` | `COMPAT_GENERIC_UNSUPPORTED_DESCRIPTOR_KIND` |
| no rule can analyze a descriptor shape safely | `Unsupported` | `COMPAT_GENERIC_NO_MATCHING_RULE` |
| impact report has topology errors for the subject | `Unsupported` | `COMPAT_ANALYSIS_UNTRUSTED_IMPACT_REPORT` |

`Unsupported` means “not enough analyzer knowledge”, not “known breaking change”.

---

## 6. Descriptor-Specific Rules

Rules below are the Phase 6d first pass. They are intentionally shallow and aligned with fields already present in the codebase.

### 6.1 SchemaDescriptor

Compare fields by `SchemaFieldDescriptor.Name`.

| Change | Level | Rule |
|---|---:|---|
| field removed | `Breaking` if affected consumers, else `Risky` | `COMPAT_SCHEMA_FIELD_REMOVED` |
| field added and `IsRequired=false` | `Compatible` | `COMPAT_SCHEMA_OPTIONAL_FIELD_ADDED` |
| field added and `IsRequired=true` | `Breaking` | `COMPAT_SCHEMA_REQUIRED_FIELD_ADDED` |
| `FieldType` changed | `Breaking` | `COMPAT_SCHEMA_FIELD_TYPE_CHANGED` |
| `IsCollection` / `CollectionElementType` changed | `Breaking` | `COMPAT_SCHEMA_COLLECTION_CHANGED` |
| `IsRequired: false -> true` | `Breaking` | `COMPAT_SCHEMA_FIELD_REQUIRED_ADDED` |
| `IsRequired: true -> false` | `Compatible` | `COMPAT_SCHEMA_FIELD_REQUIRED_RELAXED` |
| `IsNullable: true -> false` | `Breaking` | `COMPAT_SCHEMA_NULLABILITY_NARROWED` |
| `IsNullable: false -> true` | `Compatible` | `COMPAT_SCHEMA_NULLABILITY_RELAXED` |
| `MaxLength` decreased | `Breaking` | `COMPAT_SCHEMA_MAX_LENGTH_NARROWED` |
| `MaxLength` increased/removed | `Compatible` | `COMPAT_SCHEMA_MAX_LENGTH_RELAXED` |
| `MinLength` increased | `Breaking` | `COMPAT_SCHEMA_MIN_LENGTH_NARROWED` |
| `MinLength` decreased/removed | `Compatible` | `COMPAT_SCHEMA_MIN_LENGTH_RELAXED` |
| `MaxValue` decreased | `Breaking` | `COMPAT_SCHEMA_MAX_VALUE_NARROWED` |
| `MinValue` increased | `Breaking` | `COMPAT_SCHEMA_MIN_VALUE_NARROWED` |
| `Pattern` added or changed | `Breaking` | `COMPAT_SCHEMA_PATTERN_CHANGED` |
| reference added/removed/version changed | `Risky` | `COMPAT_SCHEMA_REFERENCE_CHANGED` |
| `ChangeKind=Breaking` on after descriptor | at least `Breaking` | `COMPAT_SCHEMA_DECLARED_BREAKING` |

No rename detection in Phase 6d. A rename appears as remove + add.

### 6.2 FormDescriptor

Compare fields by `FormFieldDescriptor.SchemaFieldName`.

| Change | Level | Rule |
|---|---:|---|
| bound schema ref changed | `Breaking` | `COMPAT_FORM_SCHEMA_CHANGED` |
| form field removed | `Breaking` if affected consumers, else `Risky` | `COMPAT_FORM_FIELD_REMOVED` |
| form field added | `Compatible` | `COMPAT_FORM_FIELD_ADDED` |
| `IsRequiredOverride: false/null -> true` | `Breaking` | `COMPAT_FORM_REQUIRED_OVERRIDE_ADDED` |
| `IsRequiredOverride: true -> false/null` | `Compatible` | `COMPAT_FORM_REQUIRED_OVERRIDE_RELAXED` |
| `IsReadOnly` changed | `Risky` | `COMPAT_FORM_READONLY_CHANGED` |
| `ControlType` changed | `Risky` | `COMPAT_FORM_CONTROL_CHANGED` |
| `OptionsSource` changed | `Risky` | `COMPAT_FORM_OPTIONS_CHANGED` |
| order/group/labels/help/placeholder/metadata changes only | `Compatible` | `COMPAT_FORM_PRESENTATION_ONLY` |

`FormSchemaBindingValidator` remains a binding/validation concern; 6d does not re-run schema binding validation.

### 6.3 CapabilityDescriptor

| Change | Level | Rule |
|---|---:|---|
| input schema ref added/removed/changed | `Breaking` | `COMPAT_CAPABILITY_INPUT_SCHEMA_CHANGED` |
| output schema ref removed/changed | `Breaking` | `COMPAT_CAPABILITY_OUTPUT_SCHEMA_CHANGED` |
| output schema ref added | `Risky` | `COMPAT_CAPABILITY_OUTPUT_SCHEMA_ADDED` |
| permission removed | `SecuritySensitive` | `COMPAT_CAPABILITY_PERMISSION_REMOVED` |
| permission added | `SecuritySensitive` | `COMPAT_CAPABILITY_PERMISSION_ADDED` |
| risk level increased | `SecuritySensitive` | `COMPAT_CAPABILITY_RISK_INCREASED` |
| risk level decreased | `SecuritySensitive` | `COMPAT_CAPABILITY_RISK_DECREASED` |
| capability kind changed | `Breaking` | `COMPAT_CAPABILITY_KIND_CHANGED` |
| semantic tags changed | `Risky` | `COMPAT_CAPABILITY_TAGS_CHANGED` |

Permission additions can make execution stricter; removals can make execution broader. Both require security review.

### 6.4 EventDescriptor and GeneratedEventDescriptor

Support both event descriptor shapes currently present:

- `EventDescriptor.PayloadSchema`
- `GeneratedEventDescriptor.PayloadSchemaRef`

| Change | Level | Rule |
|---|---:|---|
| payload schema ref changed | `Breaking` | `COMPAT_EVENT_PAYLOAD_SCHEMA_CHANGED` |
| event scope changed (`GeneratedEventDescriptor`) | `Risky` | `COMPAT_EVENT_SCOPE_CHANGED` |
| reliability changed (`GeneratedEventDescriptor`) | `Risky` | `COMPAT_EVENT_RELIABILITY_CHANGED` |
| public/auditable/replayable flags changed (`GeneratedEventDescriptor`) | `Risky` | `COMPAT_EVENT_OPERATIONAL_FLAG_CHANGED` |
| importance changed | `Risky` | `COMPAT_EVENT_IMPORTANCE_CHANGED` |
| `ChangeKind=Breaking` on after descriptor | at least `Breaking` | `COMPAT_EVENT_DECLARED_BREAKING` |

Because `DescriptorHashComputer` currently has explicit contract extraction for `EventDescriptor` and not the full `GeneratedEventDescriptor` shape, generated-event rules are limited to fields directly exposed on `GeneratedEventDescriptor`.

### 6.5 HumanTaskDescriptor

| Change | Level | Rule |
|---|---:|---|
| interaction/form ref changed | `Breaking` | `COMPAT_HUMANTASK_INTERACTION_CHANGED` |
| input/output schema ref changed | `Breaking` | `COMPAT_HUMANTASK_SCHEMA_CHANGED` |
| assignee strategy changed | `Risky` | `COMPAT_HUMANTASK_ASSIGNEE_STRATEGY_CHANGED` |
| permission changed | `SecuritySensitive` | `COMPAT_HUMANTASK_PERMISSION_CHANGED` |
| completion outcome removed | `Breaking` | `COMPAT_HUMANTASK_OUTCOME_REMOVED` |
| completion outcome added | `Risky` | `COMPAT_HUMANTASK_OUTCOME_ADDED` |
| outcome capability changed | `Breaking` | `COMPAT_HUMANTASK_OUTCOME_CAPABILITY_CHANGED` |
| timeout changed | `Risky` | `COMPAT_HUMANTASK_TIMEOUT_CHANGED` |

Compare outcomes by `CompletionOutcome.Condition`. `CustomExpression` is compared only by condition and capability in Phase 6d; no expression parsing.

### 6.6 WorkflowDescriptor

Compare workflow steps by `WorkflowStep.Id`.

| Change | Level | Rule |
|---|---:|---|
| variable schema ref changed | `Breaking` | `COMPAT_WORKFLOW_VARIABLE_SCHEMA_CHANGED` |
| step removed | `Breaking` | `COMPAT_WORKFLOW_STEP_REMOVED` |
| step added | `Risky` | `COMPAT_WORKFLOW_STEP_ADDED` |
| step target kind/ref changed | `Breaking` | `COMPAT_WORKFLOW_STEP_TARGET_CHANGED` |
| transition set changed | `Breaking` | `COMPAT_WORKFLOW_TRANSITIONS_CHANGED` |
| `OnError` changed | `Risky` | `COMPAT_WORKFLOW_ERROR_BEHAVIOR_CHANGED` |
| default variable scope changed | `Risky` | `COMPAT_WORKFLOW_VARIABLE_SCOPE_CHANGED` |
| condition/input/output mapping changed | `Risky` | `COMPAT_WORKFLOW_MAPPING_CHANGED` |

Do not validate whether new target refs exist; that is binding/topology/impact responsibility.

### 6.7 Data Permission Security Changes

There is no descriptor that owns data-permission scope rules today. The current data-permission model is runtime configuration (`DataPermissionScopeRule`) under Organization, not an `IDescriptor`.

Phase 6d therefore must **not** invent data-permission descriptor comparisons.

If future descriptors include `DataPermissionScopeKind` or data access policy fields, apply this widening order:

```
None < Self < OwnOrganization < OwnOrganizationAndDescendants < All
Custom = SecuritySensitive
```

Widening is `SecuritySensitive`; narrowing is `Risky` or `SecuritySensitive` depending on options. This is a reserved rule family only:

```
COMPAT_SECURITY_DATA_SCOPE_WIDENED
COMPAT_SECURITY_DATA_SCOPE_NARROWED
```

---

## 7. Impact Report Integration

### 7.1 Affected Refs

For each change subject:

- `AffectedRefs` = affected descriptors where any path has `SourceChange == change.Ref`.
- `RelatedImpactPaths` = all such paths.

Generic rules must use these affected refs only. They must not inspect topology or call topology snapshot APIs.

### 7.2 Impact Severity Is Not Compatibility

Do not collapse Phase 6c severity into Phase 6d compatibility.

```
High impact != Breaking
Low impact != Compatible
```

Examples:

- Adding an optional schema field can affect many forms. Phase 6c may report broad impact, but Phase 6d can still classify it as `Compatible`.
- Removing or changing a required schema field may affect only one capability. Phase 6c impact may be narrow, but Phase 6d must classify it as `Breaking`.

6d may use impact severity as supporting context in messages, but final compatibility classification must come from compatibility rules.

### 7.3 Diagnostics Mapping

Map only relevant 6c diagnostics. Do not blindly copy the entire impact diagnostic list.

| Impact diagnostic | Compatibility diagnostic |
|---|---|
| `IMPACT_TOPOLOGY_MISSING_TARGET` | `COMPAT_BLOCKED_BY_TOPOLOGY_ERROR` or `COMPAT_ANALYSIS_INCOMPLETE` |
| other `IMPACT_TOPOLOGY_*` errors | `COMPAT_ANALYSIS_INCOMPLETE` |
| `IMPACT_AMBIGUOUS_UNPINNED_TARGET` | `COMPAT_VERSION_AMBIGUITY` |
| `IMPACT_PATH_TRUNCATED` | `COMPAT_ANALYSIS_INCOMPLETE` |
| `IMPACT_CHANGE_NOT_IN_TOPOLOGY` | `COMPAT_CHANGE_NOT_IN_TOPOLOGY` |

If mapped impact diagnostics make the impact report untrustworthy for a subject, add an `Unsupported` finding for that subject:

```
COMPAT_ANALYSIS_UNTRUSTED_IMPACT_REPORT
```

If only warnings exist, use options:

- default: report diagnostics, do not force `Unsupported`
- `TreatImpactWarningsAsUnsupported=true`: add `Unsupported` finding

---

## 8. Determinism Rules

- Index descriptors by `DescriptorRef(Namespace, Id, Version)`.
- If duplicate refs exist in before/after inventory, add `COMPAT_DUPLICATE_DESCRIPTOR_REF` diagnostic and analyze the first item in deterministic order.
- Sort fields, permissions, tags, references, outcomes, and steps by stable keys before comparing.
- Do not rely on input list ordering except after explicit sorting.
- Do not include timestamps, random ids, object hash codes, or runtime type display names that can vary across executions.

---

## 9. Files

### 9.1 New Abstractions

```
framework/src/CrestCreates.Metadata.Abstractions/DescriptorCompatibility/
  DescriptorCompatibilityLevel.cs
  DescriptorCompatibilityFindingKind.cs
  DescriptorCompatibilityFinding.cs
  DescriptorCompatibilityDiagnostic.cs
  DescriptorCompatibilityReport.cs
  DescriptorCompatibilityAnalysisOptions.cs
  IDescriptorCompatibilityAnalyzer.cs
```

### 9.2 New Implementation

```
framework/src/CrestCreates.Metadata/
  DescriptorCompatibilityAnalyzer.cs
  DescriptorCompatibilityRuleContext.cs
  IDescriptorCompatibilityRule.cs
  DescriptorCompatibilityRules/
    GenericCompatibilityRule.cs
    SchemaCompatibilityRule.cs
    FormCompatibilityRule.cs
    CapabilityCompatibilityRule.cs
    EventCompatibilityRule.cs
    HumanTaskCompatibilityRule.cs
    WorkflowCompatibilityRule.cs
```

If the implementation becomes simpler as static helper methods inside `DescriptorCompatibilityAnalyzer`, that is acceptable for Phase 6d. The public surface remains only `IDescriptorCompatibilityAnalyzer`.

### 9.3 Modified Files

```
framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs
```

Add:

```csharp
public static IServiceCollection AddDescriptorCompatibilityAnalysis(this IServiceCollection services)
```

### 9.4 Tests

```
framework/test/CrestCreates.Metadata.Tests/DescriptorCompatibility/
  DescriptorCompatibilityAnalyzerGenericTests.cs
  SchemaCompatibilityRuleTests.cs
  FormCompatibilityRuleTests.cs
  CapabilityCompatibilityRuleTests.cs
  EventCompatibilityRuleTests.cs
  HumanTaskCompatibilityRuleTests.cs
  WorkflowCompatibilityRuleTests.cs
  DescriptorCompatibilityDiagnosticsTests.cs
```

---

## 10. Test Plan

### 10.1 Generic

- `AddedDescriptor_ReturnsCompatible`
- `RemovedDescriptor_WithAffectedConsumers_ReturnsBreaking`
- `RemovedDescriptor_WithoutAffectedConsumers_ReturnsRiskyByDefault`
- `DeprecatedDescriptor_WithAffectedConsumers_ReturnsRisky`
- `ContractHashChanged_WithAffectedConsumers_ReturnsRiskyByDefault`
- `UnsupportedDescriptorKind_ReturnsUnsupported`
- `MaxLevel_UsesHighestCompatibilityLevel`
- `MaxLevel_DoesNotTreatUnsupportedAsMoreSevereThanBreaking`
- `HighImpactSeverity_DoesNotAutomaticallyMeanBreaking`
- `LowImpactSeverity_CanStillBeBreaking_WhenRuleSaysBreaking`

### 10.2 Schema

- `Schema_OptionalFieldAdded_Compatible`
- `Schema_RequiredFieldAdded_Breaking`
- `Schema_FieldRemoved_BreakingWithConsumers`
- `Schema_FieldTypeChanged_Breaking`
- `Schema_RequiredRelaxed_Compatible`
- `Schema_ConstraintNarrowed_Breaking`
- `Schema_ConstraintRelaxed_Compatible`
- `Schema_DeclaredBreaking_UpgradesToBreaking`

### 10.3 Form

- `Form_SchemaRefChanged_Breaking`
- `Form_FieldRemoved_BreakingWithConsumers`
- `Form_FieldAdded_Compatible`
- `Form_RequiredOverrideAdded_Breaking`
- `Form_ControlTypeChanged_Risky`

### 10.4 Capability

- `Capability_InputSchemaChanged_Breaking`
- `Capability_OutputSchemaChanged_Breaking`
- `Capability_PermissionAdded_SecuritySensitive`
- `Capability_PermissionRemoved_SecuritySensitive`
- `Capability_RiskLevelChanged_SecuritySensitive`

### 10.5 Event

- `Event_PayloadSchemaChanged_Breaking`
- `Event_DeclaredBreaking_UpgradesToBreaking`
- `GeneratedEvent_PayloadSchemaRefChanged_Breaking`
- `GeneratedEvent_ReliabilityChanged_Risky`

### 10.6 HumanTask

- `HumanTask_InteractionChanged_Breaking`
- `HumanTask_AssigneeStrategyChanged_Risky`
- `HumanTask_OutcomeRemoved_Breaking`
- `HumanTask_OutcomeCapabilityChanged_Breaking`
- `HumanTask_PermissionChanged_SecuritySensitive`

### 10.7 Workflow

- `Workflow_VariableSchemaChanged_Breaking`
- `Workflow_StepRemoved_Breaking`
- `Workflow_StepAdded_Risky`
- `Workflow_StepTargetChanged_Breaking`
- `Workflow_TransitionsChanged_Breaking`

### 10.8 Diagnostics

- `ImpactTopologyError_AddsCompatibilityDiagnostic`
- `ImpactPathTruncated_AddsAnalysisIncompleteDiagnostic`
- `ImpactError_AddsUnsupportedFinding`
- `DuplicateDescriptorRefs_AddsDiagnostic`
- `ChangeSetMismatch_AddsDiagnostic`

---

## 11. Acceptance Criteria

Phase 6d is complete when:

1. `IDescriptorCompatibilityAnalyzer` can analyze `(before, after, changeSet, impactReport)` without topology access.
2. The report includes findings, max level, diagnostics, affected refs, and related impact paths.
3. Generic rules handle all current `DescriptorChangeKind` values.
4. Descriptor-specific rules cover Schema, Form, Capability, Event, GeneratedEvent, HumanTask, and Workflow descriptors with the first-pass rules in this spec.
5. Permission and risk-level changes are classified as `SecuritySensitive`.
6. Data-permission changes are not invented where no descriptor model exists.
7. `Unsupported` is locked as “not enough rule/semantic knowledge”, not “more severe than Breaking”.
8. `ContractHashChanged + affected consumers` is `Risky` by default unless descriptor-specific rules classify it differently.
9. Impact errors can make compatibility analysis `Unsupported`.
10. 6c severity is never directly converted into 6d compatibility.
11. DI registration is available through `AddDescriptorCompatibilityAnalysis()`.
12. Tests prove compatibility is rule-based and not a direct projection of 6c severity.
13. No lifecycle activation, migration generation, persistence, topology rebuild, or impact traversal is implemented.

---

## 12. One-Line Boundary

Phase 6d classifies descriptor compatibility from before/after inventories plus Phase 6c impact; it does not decide whether the change may be activated.
