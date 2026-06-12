# Phase 6a — Descriptor Relationship Coverage Design Spec

**Date**: 2026-06-12
**Status**: In Review
**Implementation Order**: 1. Core types (RelationshipKind extension, DescriptorRelationship enhancement, RelationshipStrength, extractor/provider interfaces) → 2. Default provider implementation → 3. Schema/Capability extractors → 4. Form/Event extractors → 5. HumanTask/Workflow extractors → 6. CapabilityDescriptor cleanup (remove GetRelationships, fix schema namespace bug) → 7. Remove FormDescriptorDependencyExtractor + IRelationshipAwareDescriptor → 8. DI registration → 9. Tests (per extractor + provider + coverage) → 10. Regression
**Parent Issue**: [#6 — Phase 6a: Descriptor Relationship Coverage](https://github.com/OrchesAdam/CrestCreates/issues/6)

---

## 1. Overview

Phase 6a closes the descriptor relationship coverage gap: every descriptor that owns outgoing descriptor references must expose those references through **one uniform extraction path**.

The target question:

```
Given descriptor X, what other descriptors does X depend on / consume / produce / reference?
```

This phase is **not** an impact-analysis phase, not a compatibility phase, and not a lifecycle-governance phase. It provides the data layer that Phase 6b (Topology Engine) builds on.

### Design Principles

1. **Single main path** — `IDescriptorRelationshipExtractor` per descriptor type; no fallback, no dual-track
2. **Descriptors stay POCOs** — all relationship logic lives in extractors; descriptors are pure data
3. **AoT-friendly** — no runtime member scanning, no assembly scanning, no `dynamic`, no reflection-based ref discovery; provider uses `Type.IsInstanceOfType` (runtime type identity check, statically analyzable by NativeAOT)
4. **Deterministic & testable** — each extractor produces the same output for the same input; covered per descriptor type
5. **Aligns with existing contributor pattern** — same architecture as `IDescriptorBindingStatusContributor`: per-module, non-generic interface, DI-aggregated

### Scope Boundary

Phase 6a provides the **relationship data layer**. It tells you what a descriptor references. It does NOT:
- Build a topology graph (→ Phase 6b)
- Do transitive analysis (→ Phase 6b)
- Populate `IDescriptorDependencyGraph` (→ Phase 6b)
- Change any runtime execution behavior
- Add new registry types or build paths

---

## 2. Current State vs. Target

### Already Exists (pre-6a)

| Component | Location | Status |
|---|---|---|
| `DescriptorRelationship` / `DescriptorRef` / `RelationshipKind` | `CrestCreates.Metadata.Abstractions` | ✅ 4 kinds (Produces, Consumes, DependsOn, References) |
| `IRelationshipAwareDescriptor` | `CrestCreates.Metadata.Abstractions` | ⚠️ Only CapabilityDescriptor implements; has schema namespace bug |
| `DependencyEdge` / `DescriptorDependencyKind` | `CrestCreates.Metadata.Abstractions` | ✅ 5 kinds (Uses, Produces, References, Triggers, Consumes) |
| `IDescriptorDependencyGraph` / `DescriptorDependencyGraph` | `CrestCreates.Metadata` | ✅ Manual AddEdge population |
| `FormDescriptorDependencyExtractor` | `CrestCreates.Form` | ⚠️ Static helper; returns DependencyEdge, not DescriptorRelationship |
| `CapabilityDescriptor.GetRelationships()` | `CrestCreates.Metadata` | ⚠️ Bug: uses schema Id as Namespace for InputSchema/OutputSchema refs |
| 6 descriptor kinds with typed registries | Various modules | ✅ Schema, Capability, Event, Form, HumanTask, Workflow |

### Gaps

| Missing Capability | Why Needed |
|---|---|
| Uniform relationship extraction path | Each descriptor kind either has no relationship extraction, uses a different mechanism, or has bugs |
| `IDescriptorRelationshipExtractor` | No unified, DI-injectable extraction interface per descriptor type |
| `IDescriptorRelationshipProvider` | No consumer-facing aggregation API |
| Extended `RelationshipKind` | Missing `Uses` and `Triggers` for Form→Schema, HumanTask→Capability, etc. |
| `DescriptorRelationship` enhancements | Missing Role, SourcePath, Strength, IsRuntimeBinding |
| Schema/Event/HumanTask/Workflow extractors | No extraction exists for these descriptor kinds |
| Relationship coverage tests | No per-kind tests verifying all outgoing refs are covered |

---

## 3. Core Types

### 3.1 Extended RelationshipKind

**File**: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRelationship.cs`

```csharp
public enum RelationshipKind
{
    Produces,    // Capability output schema, capability→event, humanTask output schema
    Consumes,    // Capability input schema, capability→event, humanTask input schema
    DependsOn,   // SupersededBy, same-kind dependency
    References,  // Schema self-references
    Uses,        // Form→Schema, Event→Schema, HumanTask→Form, Workflow→Schema
    Triggers     // HumanTask→Capability, Workflow→Capability, Workflow→HumanTask
}
```

### 3.2 RelationshipStrength

**File**: `framework/src/CrestCreates.Metadata.Abstractions/RelationshipStrength.cs`

```csharp
namespace CrestCreates.Metadata.Abstractions;

public enum RelationshipStrength
{
    /// <summary>Descriptor breaks without this relationship (missing schema, missing target).</summary>
    Strong,

    /// <summary>Optional or informational (event production, superseded-by, unsupported features).</summary>
    Weak
}
```

### 3.3 Enhanced DescriptorRelationship

**File**: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRelationship.cs`

```csharp
public sealed record DescriptorRelationship(
    DescriptorRef From,
    DescriptorRef To,
    RelationshipKind Kind,
    string? Role = null,                        // "InputSchema", "OutputSchema", "Interaction", "PayloadSchema"
    string? SourcePath = null,                  // Property path on source descriptor, e.g. "InputSchema"
    RelationshipStrength Strength = RelationshipStrength.Strong,
    bool IsRuntimeBinding = false);             // Whether this relationship represents a runtime binding
```

### 3.4 IDescriptorRelationshipExtractor (non-generic runtime interface)

**File**: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRelationshipExtractor.cs`

```csharp
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Per-descriptor-type relationship extractor. One implementation per concrete descriptor type.
/// Registered as non-generic singleton in DI. Provider dispatches by DescriptorType.
/// Singleton, stateless, receives typed registry references via constructor DI if needed.
/// </summary>
public interface IDescriptorRelationshipExtractor
{
    /// <summary>Which DescriptorKind this extractor handles.</summary>
    DescriptorKind SupportedKind { get; }

    /// <summary>The concrete descriptor type this extractor handles (e.g., typeof(CapabilityDescriptor)).</summary>
    Type DescriptorType { get; }

    /// <summary>
    /// Extract all outgoing relationships from a descriptor.
    /// Returns empty list if the descriptor is not the expected concrete type.
    /// Must not mutate state.
    /// </summary>
    IReadOnlyList<DescriptorRelationship> Extract(IDescriptor descriptor);
}
```

### 3.5 DescriptorRelationshipExtractorBase<TDescriptor> (optional typed base class)

**File**: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRelationshipExtractorBase.cs`

```csharp
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Optional base class for typed relationship extractors.
/// Implements the non-generic IDescriptorRelationshipExtractor with a type-check + cast,
/// then delegates to the typed Extract(TDescriptor) method.
/// AoT-safe: uses standard `is` pattern match, NOT dynamic.
/// </summary>
public abstract class DescriptorRelationshipExtractorBase<TDescriptor>
    : IDescriptorRelationshipExtractor
    where TDescriptor : class, IDescriptor
{
    public abstract DescriptorKind SupportedKind { get; }
    public Type DescriptorType => typeof(TDescriptor);

    public IReadOnlyList<DescriptorRelationship> Extract(IDescriptor descriptor)
    {
        if (descriptor is TDescriptor typed)
            return Extract(typed);
        return Array.Empty<DescriptorRelationship>();
    }

    /// <summary>Typed extraction — override in concrete extractors.</summary>
    protected abstract IReadOnlyList<DescriptorRelationship> Extract(TDescriptor descriptor);
}
```

### 3.6 IDescriptorRelationshipProvider

**File**: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRelationshipProvider.cs`

```csharp
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Consumer-facing aggregation API. Dispatches to the correct extractor by concrete descriptor type.
/// Does not trigger registry.Build() or mutate descriptors.
/// </summary>
public interface IDescriptorRelationshipProvider
{
    /// <summary>
    /// Get relationships for this descriptor by finding the extractor whose
    /// DescriptorType matches the descriptor's concrete type.
    /// Returns empty list if no registered extractor matches.
    /// </summary>
    IReadOnlyList<DescriptorRelationship> GetRelationships(IDescriptor descriptor);
}
```

> **Note**: `GetAllRelationships()` is NOT part of Phase 6a. It requires extractors to self-enumerate descriptors from injected registries (matching the Phase 5h contributor pattern). This will be added in Phase 6b as a separate interface or extension method when extractors gain registry DI for enumeration.

### 3.7 DefaultDescriptorRelationshipProvider

**File**: `framework/src/CrestCreates.Metadata/DefaultDescriptorRelationshipProvider.cs`

```csharp
namespace CrestCreates.Metadata;

public sealed class DefaultDescriptorRelationshipProvider : IDescriptorRelationshipProvider
{
    private readonly IReadOnlyList<IDescriptorRelationshipExtractor> _extractors;

    public DefaultDescriptorRelationshipProvider(
        IEnumerable<IDescriptorRelationshipExtractor> extractors)
    {
        _extractors = extractors.ToList();
    }

    public IReadOnlyList<DescriptorRelationship> GetRelationships(IDescriptor descriptor)
    {
        // Dispatch by concrete type, not just DescriptorKind.
        // Same Kind can have multiple extractors (e.g., EventDescriptor vs GeneratedEventDescriptor).
        // IsInstanceOfType catches both exact matches and derived types.
        foreach (var extractor in _extractors)
        {
            if (extractor.DescriptorType.IsInstanceOfType(descriptor))
                return extractor.Extract(descriptor);
        }
        return Array.Empty<DescriptorRelationship>();
    }
}
```

> **Dispatch rationale**: Provider iterates all registered extractors and checks `IsInstanceOfType`. This handles both exact matches (`CapabilityDescriptor → CapabilityRelationshipExtractor`) and cases where one `DescriptorKind` has multiple concrete types (`GeneratedEventDescriptor` vs `EventDescriptor`). If no extractor matches (e.g., calling with an unknown descriptor), it returns empty — the extractor contract is per-concrete-type, and absence is not an error. The check order follows DI registration order; multiple extractors for the same concrete type are not allowed (second registration would silently shadow).

---

## 4. Per-Descriptor Relationship Mapping

### 4.1 SchemaRelationshipExtractor

**File**: `framework/src/CrestCreates.Metadata/SchemaRelationshipExtractor.cs`

**Constructor DI**: None (works inline from `SchemaDescriptor.References[]`)

| Source Field | Target | Kind | Role | Strength | SourcePath |
|---|---|---|---|---|---|
| `References[]` | `SchemaDescriptor` | `References` | `null` | `Weak` | `"References"` |

`References[]` elements are already `VersionedDescriptorRef<SchemaDescriptor>`. Extractor maps each to a `DescriptorRef(Namespace="schema", Id=ref.Id, Version=ref.Version)`.

### 4.2 FormRelationshipExtractor

**File**: `framework/src/CrestCreates.Form/FormRelationshipExtractor.cs`

**Constructor DI**: None (works inline from `FormDescriptor.Schema`)

| Source Field | Target | Kind | Role | Strength | SourcePath |
|---|---|---|---|---|---|
| `Schema` | `SchemaDescriptor` | `Uses` | `"Schema"` | `Strong` | `"Schema"` |

Replaces the existing `FormDescriptorDependencyExtractor` static class (moved to recycle bin).

### 4.3 CapabilityRelationshipExtractor

**File**: `framework/src/CrestCreates.Capability/CapabilityRelationshipExtractor.cs`

**Constructor DI**: None (works inline from CapabilityDescriptor properties)

| Source Field | Target | Kind | Role | Strength | SourcePath |
|---|---|---|---|---|---|
| `InputSchema` | `SchemaDescriptor` | `Consumes` | `"InputSchema"` | `Strong` | `"InputSchema"` |
| `OutputSchema` | `SchemaDescriptor` | `Produces` | `"OutputSchema"` | `Strong` | `"OutputSchema"` |
| `Produces[]` | Event descriptor | `Produces` | `null` | `Weak` | `"Produces"` |
| `Consumes[]` | Event descriptor | `Consumes` | `null` | `Weak` | `"Consumes"` |
| `SupersededById` | `CapabilityDescriptor` | `DependsOn` | `"SupersededBy"` | `Weak` | `"SupersededById"` |

**Bug fix**: Schema refs use correct namespace — `"schema"` (the schema namespace), NOT the schema's `Id`. InputSchema/OutputSchema are `VersionedDescriptorRef<SchemaDescriptor>` — use the schema namespace `"schema"` and the ref's `Id`.

Optional refs are omitted cleanly (null `InputSchema`/`OutputSchema` → no relationship emitted).

### 4.4 EventRelationshipExtractor

**File**: `framework/src/CrestCreates.Event/EventRelationshipExtractor.cs`

**Constructor DI**: None (works inline from `GeneratedEventDescriptor`)

| Source Field | Target | Kind | Role | Strength | SourcePath |
|---|---|---|---|---|---|
| `PayloadSchemaRef` | `SchemaDescriptor` | `Uses` | `"PayloadSchema"` | `Strong` | `"PayloadSchemaRef"` |

Covers `GeneratedEventDescriptor` (the registry main-path event descriptor). `PayloadSchemaRef` is a `VersionedDescriptorRef<SchemaDescriptor>`.

`CapabilityId` remains non-relationship metadata — it is a string, not a typed/versioned descriptor reference, and cannot be validated consistently.

### 4.5 HumanTaskRelationshipExtractor

**File**: `framework/src/CrestCreates.HumanTask/HumanTaskRelationshipExtractor.cs`

**Constructor DI**: None (works inline from `HumanTaskDescriptor`)

| Source Field | Target | Kind | Role | Strength | SourcePath |
|---|---|---|---|---|---|
| `Interaction` | `FormDescriptor` | `Uses` | `"Interaction"` | `Strong` | `"Interaction"` |
| `InputSchema` | `SchemaDescriptor` | `Consumes` | `"InputSchema"` | `Strong` | `"InputSchema"` |
| `OutputSchema` | `SchemaDescriptor` | `Produces` | `"OutputSchema"` | `Strong` | `"OutputSchema"` |
| `Outcomes[].Capability` | `CapabilityDescriptor` | `Triggers` | `"Outcome"` | `Strong` | `"Outcomes"` |

`Permissions` remains a permission string, not a descriptor relationship (no permission descriptor type exists).

### 4.6 WorkflowRelationshipExtractor

**File**: `framework/src/CrestCreates.Workflow/WorkflowRelationshipExtractor.cs`

**Constructor DI**: None (works inline from `WorkflowDescriptor`)

| Source Field | Target | Kind | Role | Strength | SourcePath |
|---|---|---|---|---|---|
| `VariableSchema` | `SchemaDescriptor` | `Uses` | `"VariableSchema"` | `Strong` | `"VariableSchema"` |
| Step `CapabilityTarget.Capability` | `CapabilityDescriptor` | `Triggers` | `"CapabilityStep"` | `Strong` | `"Steps"` |
| Step `HumanTaskTarget.HumanTask` | `HumanTaskDescriptor` | `Triggers` | `"HumanTaskStep"` | `Strong` | `"Steps"` |
| Step `SubWorkflowTarget` | `WorkflowDescriptor` | `References` | `"SubWorkflowStep"` | `Weak` | `"Steps"` |

`SubWorkflowTarget` is emitted as a relationship with `IsRuntimeBinding=false` and `Strength=Weak`. It must NOT silently imply subworkflow runtime support. The `UNSUPPORTED_SUBWORKFLOW` binding status check (Phase 5h) remains the runtime enforcement gate.

---

## 5. Strength Rationale

| Strength | Criteria | Examples |
|---|---|---|
| **Strong** | Missing this relationship breaks core functionality | Schema refs on Form/Capability/HumanTask/Workflow, Interaction target, Capability/HumanTask step targets |
| **Weak** | Optional, informational, or unsupported | Event production/consumption, SupersededBy, SubWorkflowTarget |

---

## 6. Exposure Descriptors — Explicit Exclusion

Phase 6a does NOT create extractors or tests for:

- `AgentToolDescriptor` (references `CapabilityDescriptor`)
- `MCPToolDescriptor` (references `CapabilityDescriptor`)
- `CapabilityEndpointDescriptor` (references `CapabilityDescriptor`)

**Rationale**: These are projection descriptors (Phase 8 scope). They are not full `IDescriptor` registry participants. When Phase 8 formalizes them as descriptor registry participants, they will be brought into the relationship extractor system.

**Spec note for Phase 8**: When exposure descriptors become registry participants, add:
- `AgentToolRelationshipExtractor` — `Capability` ref → `Triggers`, `"Capability"`
- `MCPToolRelationshipExtractor` — `Capability` ref → `Triggers`, `"Capability"`
- `CapabilityEndpointRelationshipExtractor` — `Capability` ref → `Triggers`, `"Capability"`

---

## 7. RelationshipKind ↔ DescriptorDependencyKind Mapping (Phase 6b Preview)

Phase 6a uses only `RelationshipKind`. Phase 6b will project to `DescriptorDependencyKind` for graph integration:

| RelationshipKind | DescriptorDependencyKind |
|---|---|
| `Produces` | `Produces` |
| `Consumes` | `Consumes` |
| `DependsOn` | `References` |
| `References` | `References` |
| `Uses` | `Uses` |
| `Triggers` | `Triggers` |

This mapping is **not implemented in Phase 6a**. It is documented here for design continuity.

---

## 8. Code Removal

### 8.1 IRelationshipAwareDescriptor

**Action**: Delete `framework/src/CrestCreates.Metadata.Abstractions/IRelationshipAwareDescriptor.cs`.

**Impact**: Zero consumers to migrate. `CapabilityDescriptor`'s `GetRelationships()` is being removed (see §8.2).

### 8.2 CapabilityDescriptor.GetRelationships()

**Action**: Remove `IRelationshipAwareDescriptor` implementation and the entire `GetRelationships()` method body from `CapabilityDescriptor`.

**Bug fix**: The schema namespace bug (using `InputSchema.Value.Id` as namespace instead of `"schema"`) is fixed in the new `CapabilityRelationshipExtractor`, not in CapabilityDescriptor itself.

### 8.3 FormDescriptorDependencyExtractor

**Action**: Move `framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs` to `./99_RecycleBin/`.

**Replacement**: `FormRelationshipExtractor` provides the same data in the correct `DescriptorRelationship` output format.

### 8.4 Preserved (NOT removed)

- `DependencyEdge`, `DescriptorDependencyKind`, `IDescriptorDependencyGraph`, `DescriptorDependencyGraph`, `DependencyGraphProvider` — all preserved for Phase 6b graph projection.
- `DescriptorDependencyKind.Uses` and `DescriptorDependencyKind.Triggers` already exist — no modification needed.

---

## 9. Project Structure

### New Files (11)

```
framework/src/CrestCreates.Metadata.Abstractions/
  RelationshipStrength.cs                              (enum)
  IDescriptorRelationshipExtractor.cs                  (non-generic runtime interface)
  DescriptorRelationshipExtractorBase.cs               (optional typed base class)
  IDescriptorRelationshipProvider.cs                   (aggregation interface)

framework/src/CrestCreates.Metadata/
  DefaultDescriptorRelationshipProvider.cs             (implementation)
  SchemaRelationshipExtractor.cs                       (Schema → Schema)

framework/src/CrestCreates.Form/
  FormRelationshipExtractor.cs                         (Form → Schema)

framework/src/CrestCreates.Capability/
  CapabilityRelationshipExtractor.cs                   (Capability → Schema/Event/Capability)

framework/src/CrestCreates.Event/
  EventRelationshipExtractor.cs                        (GeneratedEvent → Schema)

framework/src/CrestCreates.HumanTask/
  HumanTaskRelationshipExtractor.cs                    (HumanTask → Form/Schema/Capability)

framework/src/CrestCreates.Workflow/
  WorkflowRelationshipExtractor.cs                     (Workflow → Schema/Capability/HumanTask)
```

### Modified Files (8)

```
framework/src/CrestCreates.Metadata.Abstractions/
  DescriptorRelationship.cs          → Add Role, SourcePath, Strength, IsRuntimeBinding fields
                                      → Add Uses, Triggers to RelationshipKind

framework/src/CrestCreates.Metadata/
  CapabilityDescriptor.cs            → Remove IRelationshipAwareDescriptor, remove GetRelationships()

framework/src/CrestCreates.Metadata/
  MetadataServiceCollectionExtensions.cs → Add AddRelationshipKernel() + register SchemaRelationshipExtractor

framework/src/CrestCreates.Form/
  FormServiceCollectionExtensions.cs     → Register FormRelationshipExtractor as IDescriptorRelationshipExtractor

framework/src/CrestCreates.Capability/
  CapabilityServiceCollectionExtensions.cs → Register CapabilityRelationshipExtractor as IDescriptorRelationshipExtractor

framework/src/CrestCreates.Event/
  EventServiceCollectionExtensions.cs     → Register EventRelationshipExtractor as IDescriptorRelationshipExtractor

framework/src/CrestCreates.HumanTask/
  HumanTaskServiceCollectionExtensions.cs → Register HumanTaskRelationshipExtractor as IDescriptorRelationshipExtractor

framework/src/CrestCreates.Workflow/
  WorkflowServiceCollectionExtensions.cs → Register WorkflowRelationshipExtractor as IDescriptorRelationshipExtractor
```

### Deleted Files (2)

```
framework/src/CrestCreates.Metadata.Abstractions/IRelationshipAwareDescriptor.cs
framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs → 99_RecycleBin/
```

### Test Files (10)

```
framework/test/CrestCreates.Metadata.Tests/
  RelationshipKindExtensionTests.cs                    (Uses/Triggers enum values exist)
  DescriptorRelationshipEnhancementTests.cs            (Role/SourcePath/Strength/IsRuntimeBinding)
  SchemaRelationshipExtractorTests.cs                  (extracts references, empty when no refs)
  DefaultDescriptorRelationshipProviderTests.cs        (concrete type dispatch, EventDescriptor vs GeneratedEventDescriptor)
  RelationshipStrengthTests.cs                         (Strong/Weak enum values)

framework/test/CrestCreates.Form.Tests/
  FormRelationshipExtractorTests.cs                    (extracts Schema→Form, emits even with empty Id)

framework/test/CrestCreates.Capability.Tests/
  CapabilityRelationshipExtractorTests.cs              (Input/Output/Produces/Consumes/SupersededBy, correct schema namespace, nullable omission)

framework/test/CrestCreates.Event.Tests/
  EventRelationshipExtractorTests.cs                   (extracts PayloadSchemaRef from GeneratedEventDescriptor, emits even with empty Id)

framework/test/CrestCreates.HumanTask.Tests/
  HumanTaskRelationshipExtractorTests.cs               (Interaction/Input/Output/Outcomes)

framework/test/CrestCreates.Workflow.Tests/
  WorkflowRelationshipExtractorTests.cs                (VariableSchema/CapabilityTarget/HumanTaskTarget/SubWorkflowTarget, nullable omission)
```

---

## 10. DI Registration

### Metadata Module

```csharp
// MetadataServiceCollectionExtensions.AddRelationshipKernel()
services.TryAddSingleton<IDescriptorRelationshipProvider,
    DefaultDescriptorRelationshipProvider>();

// SchemaRelationshipExtractor lives in CrestCreates.Metadata — registered here, not in Schema module
services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();
```

### Per-Module Extractor Registration

Each module registers its extractor as the **non-generic** `IDescriptorRelationshipExtractor` interface via `AddSingleton` (NOT `TryAddSingleton` — multiple extractors must coexist):

```csharp
// In each module's *ServiceCollectionExtensions:
services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();
services.AddSingleton<IDescriptorRelationshipExtractor, FormRelationshipExtractor>();
services.AddSingleton<IDescriptorRelationshipExtractor, CapabilityRelationshipExtractor>();
services.AddSingleton<IDescriptorRelationshipExtractor, EventRelationshipExtractor>();
services.AddSingleton<IDescriptorRelationshipExtractor, HumanTaskRelationshipExtractor>();
services.AddSingleton<IDescriptorRelationshipExtractor, WorkflowRelationshipExtractor>();
```

> **Note**: This uses plain `IEnumerable<IDescriptorRelationshipExtractor>` injection in the provider — no generic variance, no covariance tricks, no `dynamic`. Standard .NET DI resolves all `IDescriptorRelationshipExtractor` registrations into the `IEnumerable<>` parameter.

---

## 11. Testing Strategy

### 11.1 Per-Extractor Tests

| Test | Assertion |
|---|---|
| `SchemaRelationshipExtractor_Extracts_References` | Schema with 2 references → 2 relationships (kind=References) |
| `SchemaRelationshipExtractor_Empty_When_No_References` | Schema with empty References → 0 relationships |
| `FormRelationshipExtractor_Extracts_Schema` | Form with Schema ref → 1 relationship (kind=Uses, role="Schema", strength=Strong) |
| `FormRelationshipExtractor_Emits_Even_When_Schema_Id_Empty` | Form with default Schema ref (empty Id) → 1 relationship emitted as-is (validator/binding status catches structural errors, not extractor) |
| `CapabilityRelationshipExtractor_Extracts_Input_Output_Event_Refs` | Full capability → 5+ relationships |
| `CapabilityRelationshipExtractor_Uses_Correct_Schema_Namespace` | Schema refs use "schema" namespace, not schema Id |
| `CapabilityRelationshipExtractor_Optional_Nullable_Refs_Omitted` | Null InputSchema → no InputSchema relationship; null OutputSchema → no OutputSchema relationship |
| `EventRelationshipExtractor_Extracts_PayloadSchemaRef` | GeneratedEventDescriptor with PayloadSchemaRef → 1 relationship |
| `EventRelationshipExtractor_Emits_Even_When_PayloadSchemaRef_Id_Empty` | GeneratedEventDescriptor with default PayloadSchemaRef (empty Id) → 1 relationship emitted as-is |
| `EventRelationshipExtractor_Does_Not_Match_EventDescriptor` | EventDescriptor (not GeneratedEventDescriptor) passed → extractor's `IsInstanceOfType` check fails → returns empty |
| `HumanTaskRelationshipExtractor_Extracts_All_Four_Ref_Types` | Full HumanTask → 4+ relationships |
| `HumanTaskRelationshipExtractor_Outcome_Capability_Uses_Triggers` | Outcome capability → kind=Triggers |
| `WorkflowRelationshipExtractor_Extracts_VariableSchema_StepTargets` | Full workflow → 3+ relationships |
| `WorkflowRelationshipExtractor_Nullable_VariableSchema_Omitted` | Null VariableSchema → no VariableSchema relationship |
| `WorkflowRelationshipExtractor_SubWorkflowTarget_Weak_NotRuntimeBinding` | SubWorkflowTarget → kind=References, strength=Weak, IsRuntimeBinding=false |

> **Policy for non-nullable struct refs** (FormDescriptor.Schema, GeneratedEventDescriptor.PayloadSchemaRef, HumanTaskDescriptor.Interaction): These are `VersionedDescriptorRef<T>` — a record struct, never null. The extractor emits a relationship regardless of whether the ref's Id is empty. Structural validation (missing Id, unresolvable target) is the responsibility of validators and binding status contributors, not the extractor. The extractor is a data projection, not a validator.

### 11.2 Provider Dispatch Tests

| Test | Assertion |
|---|---|
| `Provider_Dispatches_To_Correct_Concrete_Type` | CapabilityDescriptor passed → CapabilityRelationshipExtractor selected (by DescriptorType match) |
| `Provider_EventDescriptor_Returns_Empty` | EventDescriptor passed → no extractor matches (only GeneratedEventDescriptor extractor registered) → empty list |
| `Provider_GeneratedEventDescriptor_Dispatches_Correctly` | GeneratedEventDescriptor passed → EventRelationshipExtractor selected |
| `Provider_Unknown_Concrete_Type_Returns_Empty` | Descriptor with no registered extractor for its concrete type → empty list |

### 11.3 Removal Tests (Negative)

| Test | Assertion |
|---|---|
| `CapabilityDescriptor_No_Longer_Implements_IRelationshipAwareDescriptor` | Type check fails |
| `CapabilityDescriptor_No_Longer_Has_GetRelationships` | Method does not exist |
| `FormDescriptorDependencyExtractor_Removed` | Type does not exist in Form project |
| `No_Descriptor_Implements_IRelationshipAwareDescriptor` | Zero implementations across solution |

### 11.4 Regression Gate

All existing test suites must pass with zero regressions:
- Metadata.Tests (95), Form.Tests (35), Capability.Tests (120), Event.Tests (36), HumanTask.Tests (47), Workflow.Tests (63)
- Full `dotnet build` — 0 errors

---

## 12. Explicit Non-Goals

Phase 6a MUST NOT implement:

- Transitive graph traversal
- Impact analysis (beyond existing `AnalyzeImpact()` which is preserved)
- Compatibility or breaking-change analyzer
- Lifecycle governance
- Descriptor package / manifest / snapshot changes
- Topology engine
- `IDescriptorDependencyGraph` population from extracted relationships
- Runtime reflection relationship scanning
- Fallback relationship path
- Capability Authorization changes
- DataPermission changes
- HumanTask runtime changes
- Workflow execution changes
- SubWorkflow / retry / compensation / transition runtime support
- Exposure descriptor extractors or tests
- New registries or registry paths
- Modifications to `MetadataBootstrapper.BuildAll()`
- Phase 6b `RelationshipKind → DescriptorDependencyKind` mapping implementation

---

## 13. Design Decisions

| Decision | Rationale |
|---|---|
| Extractors instead of descriptor-owned `GetRelationships()` | Keeps descriptors pure POCOs; logic in DI-injectable, testable units; matches Phase 5h contributor pattern |
| Remove `IRelationshipAwareDescriptor` entirely | No dual-path risk; presence of a registered extractor IS the contract |
| Extend `RelationshipKind` with `Uses` and `Triggers` | Covers all current relationship semantics; avoids creating a third vocabulary |
| Keep `DescriptorDependencyKind` separate | Phase 6b projection target; two vocabularies serve different purposes (descriptor semantics vs. graph semantics) |
| `Role` as optional string field | Lightweight, no enum maintenance, carries semantic meaning ("InputSchema" vs "OutputSchema") |
| `SourcePath` as property path string | Enables tooling/UI to link relationship back to source descriptor field |
| `IsRuntimeBinding` bool | Distinguishes structural refs (always exist) from runtime bindings (require handler/resolver) |
| `Strength` Strong/Weak | Clear, testable distinction; Strong for critical refs, Weak for optional |
| Schema `References[]` → `Weak` | Schema field type references are informational; missing a reference doesn't break the schema itself |
| Capability `Produces[]`/`Consumes[]` → `Weak` | Event production/consumption is informational; a capability can function without publishing events |
| HumanTask/Workflow targets → `Strong` + `Triggers` | Missing a target breaks execution; `Triggers` captures the "I cause this to execute" semantic |
| Event `PayloadSchemaRef` → `Strong` | Without schema, event payload cannot be validated or deserialized |
| Exposure descriptors excluded | Not registry participants; Phase 8 scope; relationship extraction requires descriptor identity |
| Non-generic `IDescriptorRelationshipExtractor` interface | .NET generic variance does not support `IEnumerable<IExtractor<IDescriptor>>` from `IExtractor<SchemaDescriptor>` registrations; non-generic interface with `Type DescriptorType` + `IsInstanceOfType` dispatch avoids covariance tricks and is AoT-safe |
| `IsInstanceOfType` dispatch (not `DescriptorKind` dictionary) | Same `DescriptorKind` can have multiple concrete types (e.g., EventDescriptor vs GeneratedEventDescriptor); provider must match by concrete type, not just kind |
| `GetAllRelationships()` excluded from Phase 6a | Requires extractors to self-enumerate from registries (Phase 5h contributor pattern); will be added as separate interface in Phase 6b |
| `AddSingleton` (not `TryAddSingleton`) for extractors | Multiple extractors must coexist for `IDescriptorRelationshipExtractor`; all registered as the same non-generic interface |
| Non-nullable struct refs emit relationships as-is | `VersionedDescriptorRef<T>` is a record struct — never null. Extractors are data projections, not validators. Structural errors (empty Id) are caught by validators and binding status contributors. |

---

**Design reviewed and approved. Ready for implementation plan.**
