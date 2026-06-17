# Phase 7a Stabilization — Materializer Proposed Inventory Snapshot Isolation Design

**Date:** 2026-06-17
**Issue:** #34
**Labels:** type-tech-debt, area-metadata, area-aot

## Goal

Fix the Phase 7a Descriptor Draft materializer so the proposed inventory is a true defensive snapshot, not only a copied `List<IDescriptor>` that reuses descriptor object references from the current inventory or draft payload.

This is a stabilization patch for Phase 7a. It does not become a broader migration to `ISnapshotable<T>`.

## Background

The materializer currently builds the proposed inventory with:

```csharp
var proposed = new List<IDescriptor>(currentInventory);
```

This is a **shallow list copy** — the list is new but elements are shared references. The draft payload descriptor is also inserted/replaced by reference without cloning.

While all 6 descriptor types are `sealed class` with `init`-only properties, their collection properties (`IReadOnlyList<>`, `IReadOnlyDictionary<>`) may be backed by mutable `List<>`/`Dictionary<>` instances. A caller retaining a reference to the backing collection can mutate it after construction, affecting the proposed inventory.

The most notable mutability hole is `FormFieldDescriptor.Metadata`, whose default value is `new Dictionary<string, string>()` — a mutable dictionary shared by reference.

## DescriptorDraftSnapshotHelper

**File:** `CrestCreates.DescriptorDraft/DescriptorDraftSnapshotHelper.cs`

```csharp
namespace CrestCreates.DescriptorDraft;

/// <summary>
/// Internal Phase 7a-local proposed inventory snapshot helper.
/// Clones descriptors and their mutable collection state so the proposed
/// inventory does not share references with currentInventory or draft payload.
/// <para>
/// This is temporary until #35 (ISnapshotable adoption across boundary models).
/// Do not use outside of Phase 7a materialization.
/// </para>
/// </summary>
internal static class DescriptorDraftSnapshotHelper
{
    public static IReadOnlyList<IDescriptor> SnapshotInventory(
        IReadOnlyList<IDescriptor> inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory.Select(SnapshotDescriptor).ToArray();
    }

    public static IDescriptor SnapshotDescriptor(IDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.Kind switch
        {
            DescriptorKind.Schema when descriptor is SchemaDescriptor schema =>
                SnapshotSchema(schema),
            DescriptorKind.Form when descriptor is FormDescriptor form =>
                SnapshotForm(form),
            DescriptorKind.Capability when descriptor is CapabilityDescriptor capability =>
                SnapshotCapability(capability),
            DescriptorKind.HumanTask when descriptor is HumanTaskDescriptor humanTask =>
                SnapshotHumanTask(humanTask),
            DescriptorKind.Workflow when descriptor is WorkflowDescriptor workflow =>
                SnapshotWorkflow(workflow),
            DescriptorKind.Event when descriptor is EventDescriptor @event =>
                SnapshotEvent(@event),
            DescriptorKind kind =>
                throw new InvalidOperationException(
                    $"Descriptor kind {kind} does not match descriptor CLR type {descriptor.GetType().FullName}"),
            _ => throw new NotSupportedException($"Unsupported descriptor kind: {descriptor.Kind}")
        };
    }

    // Private methods: SnapshotSchema, SnapshotForm, SnapshotCapability,
    //                  SnapshotHumanTask, SnapshotWorkflow, SnapshotEvent
    // Plus sub-object helpers: CloneSchemaField, CloneSchemaValidationRule,
    //                         CloneFormField, CloneCompletionOutcome,
    //                         CloneWorkflowStep, CloneWorkflowTarget
}
```

Key decisions:
- **`internal`** — only visible within `CrestCreates.DescriptorDraft`. Not a public API.
- **Naming: `Snapshot*`** — consistent with #33 terminology, not `Clone*`, reinforcing snapshot boundary semantics.
- **Separate private method per descriptor type** — e.g., `SnapshotSchema`, `SnapshotForm`. Keeps the switch case short and isolates each descriptor's clone logic.
- **Separate private method for mutable sub-objects** — e.g., `CloneSchemaField`, `CloneFormField`, `CloneWorkflowStep`, `CloneWorkflowTarget`. Matches the pattern already used in payload `CreateClone()`.
- **Kind/type mismatch fails fast** — the switch uses `when` pattern matching to verify `DescriptorKind` matches the CLR type. A mismatch throws `InvalidOperationException` with both the kind and the actual type name, which is clearer than a raw `InvalidCastException` during debugging.

## Per-Descriptor Clone Strategy

| Descriptor Type | Scalar Copy | Collection Copy | Sub-object Clone |
|---|---|---|---|
| `SchemaDescriptor` | Namespace, Id, Name, Version, State, ContractHash, DefinitionHash, SupersededById, SchemaKind | `Fields` → `.Select(CloneSchemaField).ToArray()` | `SchemaFieldDescriptor` — create new instance; currently only scalar/init-only, no nested clone needed |
| | | `ValidationRules` → `.Select(CloneSchemaValidationRule).ToArray()` | `SchemaValidationRule` — create new instance; currently only scalar/init-only, no nested clone needed |
| | | `References` → `.ToArray()` | Elements are `VersionedDescriptorRef`, immutable struct |
| `FormDescriptor` | Namespace, Id, Name, Version, State, ContractHash, DefinitionHash, SupersededById, Schema, LayoutColumns | `Fields` → `.Select(CloneFormField).ToArray()` | `FormFieldDescriptor` — new instance + `Metadata` defensive copy with `StringComparer.Ordinal`; `Metadata` is non-nullable on `FormFieldDescriptor`, so clone always produces a new dictionary |
| `CapabilityDescriptor` | Namespace, Id, Name, Version, State, ContractHash, DefinitionHash, SupersededById, RiskLevel, CapabilityKind, InputSchema, OutputSchema | `Categories` → `.ToArray()` | String, immutable |
| | | `Produces` → `.ToArray()` | `EventRef` immutable struct |
| | | `Consumes` → `.ToArray()` | `EventRef` immutable struct |
| | | `SemanticTags` → `.ToArray()` | String, immutable |
| | | `Permissions` → `.ToArray()` | String, immutable |
| `HumanTaskDescriptor` | Namespace, Id, Name, Version, State, ContractHash, DefinitionHash, SupersededById, Interaction, InputSchema, OutputSchema, Timeout, Permissions (scalar `string?`) | `Outcomes` → `.Select(CloneCompletionOutcome).ToArray()` | `CompletionOutcome` — Condition enum + nullable `VersionedDescriptorRef`, both immutable |
| `WorkflowDescriptor` | Namespace, Id, Name, Version, State, ContractHash, DefinitionHash, SupersededById, VariableSchema | `Steps` → `.Select(CloneWorkflowStep).ToArray()` | `WorkflowStep` — new instance + `Transitions` → `.ToArray()` (string, immutable) + `Target` → `CloneWorkflowTarget()` |
| `EventDescriptor` | All scalars — no collection properties | None | None |

`CloneWorkflowTarget` dispatches by runtime type:
- `CapabilityTarget` → new instance, copy `Capability` property
- `HumanTaskTarget` → new instance, copy `HumanTask` property
- `SubWorkflowTarget` → new instance, copy `SubWorkflow` property
- Unknown → `NotSupportedException($"Unsupported workflow target type: {target.GetType().FullName}")`

`FormFieldDescriptor.Metadata` is defensively copied with `.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal)` — matching the existing pattern in `FormDescriptorDraftPayload.CreateClone()`. Since `Metadata` is non-nullable on `FormFieldDescriptor`, the clone always produces a new dictionary instance.

## Materializer Changes

**File:** `CrestCreates.DescriptorDraft/DefaultDescriptorDraftMaterializer.cs`

Before:
```csharp
var proposed = new List<IDescriptor>(currentInventory);
// ...
proposed.Add(draft.Payload.GetDescriptor());  // Create
// or
proposed[index] = draft.Payload.GetDescriptor();  // Update
```

After:
```csharp
var proposed = DescriptorDraftSnapshotHelper
    .SnapshotInventory(currentInventory)
    .ToList();

var proposedDescriptor = DescriptorDraftSnapshotHelper
    .SnapshotDescriptor(draft.Payload.GetDescriptor());

// Create: proposed.Add(proposedDescriptor);
// Update: proposed[index] = proposedDescriptor;
```

Changes:
- `currentInventory` descriptors are cloned via `SnapshotInventory` — each gets a new instance with defensively-copied mutable collections
- The draft payload descriptor is cloned via `SnapshotDescriptor` before insertion/replacement — no shared reference with the draft payload
- The `proposed` list is still a `List<IDescriptor>` (mutable, for the add/replace logic), but built from cloned descriptors
- No changes to the materializer's public API or return type
- No changes to the materializer's operation logic (Create/Update validation, version matching, etc.)

## Test Coverage

Add to existing `DefaultDescriptorDraftMaterializerTests.cs`:

### Descriptor Reference Isolation

| # | Test | Verifies |
|---|------|----------|
| 1 | `Materialize_Does_Not_Share_Descriptor_References_With_CurrentInventory` | Proposed inventory descriptors are not the same object references as currentInventory descriptors |
| 2 | `Create_Does_Not_Insert_Original_Payload_Descriptor_Reference` | Inserted descriptor is not the same reference as `draft.Payload.GetDescriptor()` |
| 3 | `Update_Does_Not_Insert_Original_Payload_Descriptor_Reference` | Replaced descriptor is not the same reference as `draft.Payload.GetDescriptor()` |
| 4 | `Update_Replaces_Descriptor_Using_Cloned_Replacement` | Replaced descriptor has same identity fields (Kind + Id + Version) but different reference |

### Collection State Isolation

| # | Test | Verifies |
|---|------|----------|
| 5 | `Create_Does_Not_Share_Collection_State_With_CurrentInventory` | Create path: mutating source `List<>` backing a descriptor's `IReadOnlyList<>` after materialization does not affect proposed inventory |
| 6 | `Create_Does_Not_Share_Collection_State_With_DraftPayloadDescriptor` | Create path: mutating source `List<>` backing the draft payload descriptor's collection after materialization does not affect proposed inventory |
| 7 | `Update_Does_Not_Share_Collection_State_With_CurrentInventory` | Update path: mutating source `List<>` backing a descriptor's `IReadOnlyList<>` after materialization does not affect proposed inventory |
| 8 | `Update_Does_Not_Share_Collection_State_With_DraftPayloadDescriptor` | Update path: mutating source `List<>` backing the draft payload descriptor's collection after materialization does not affect proposed inventory |
| 9 | `FormFieldDescriptor_Metadata_Is_Defensively_Copied` | Mutating source `Dictionary<>` backing `FormFieldDescriptor.Metadata` after materialization does not affect proposed inventory |

Test strategy:
- Tests use `SchemaDescriptor` or `FormDescriptor` (most complex collection behavior)
- Existing tests remain unchanged — they verify behavior has not regressed
- Isolation assertions use `Should().NotBeSameAs()` for reference checks and `Should().NotContain()` for collection state checks

## Scope Boundaries

**In scope:**
- `DescriptorDraftSnapshotHelper` — internal to `CrestCreates.DescriptorDraft`
- `DefaultDescriptorDraftMaterializer` — updated to use the snapshot helper
- New tests in existing test file

**Explicitly out of scope:**
- No migration of `DescriptorDraft` or descriptor models to `ISnapshotable<T>`
- No public `Clone()` method on any descriptor type
- No modification of existing `CreateClone()` on payload types
- No changes to descriptor `Abstractions` projects
- No changes to active registries or runtime activation behavior
- No `object DeepClone(object)` or generic runtime cloning
- No making `CrestCreates.Snapshot` reference Metadata/Descriptor projects
- No reopening Phase 7a as a broader refactor

## Relationship to Other Issues

```
#33 = AOT-safe Snapshot Contract (completed)
#34 = this issue — local Phase 7a stabilization patch
#35 = future — ISnapshotable adoption across boundary models
```

The snapshot helper is intentionally redundant with the payload `CreateClone()` logic. This redundancy is temporary — #35 will unify both paths when descriptors adopt `ISnapshotable<T>`.

## Exit Criteria

- `DefaultDescriptorDraftMaterializer` returns a proposed inventory that does not share descriptor object references with `currentInventory`
- Inserted/replaced draft descriptor is also cloned before entering proposed inventory
- Mutable collection properties inside descriptors are defensively copied by the clone/snapshot path
- Tests prove reference isolation and collection-state isolation for Create and Update
- No new reflection, JSON clone, expression compile, or runtime graph walker is introduced
- Existing Phase 7a public contracts remain stable
- `DescriptorDraftSnapshotHelper` does not construct temporary payloads or call payload `CreateClone()`
