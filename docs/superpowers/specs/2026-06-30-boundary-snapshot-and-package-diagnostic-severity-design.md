# Boundary Snapshot and Package Diagnostic Severity Design

Status: APPROVED FOR PLANNING
Date: 2026-06-30
Issues: #35 Adopt ISnapshotable<T> in Boundary Models, #46 Tech Debt - Type descriptor package diagnostic severity as a domain enum

## 1. Purpose

This spec closes two related technical debts:

1. `DescriptorPackageDiagnostic.Severity` must use a package-domain severity enum instead of the generic `SeverityLevel`.
2. Boundary models that require defensive copy semantics must converge on `ISnapshotable<T>.Snapshot()` as the single snapshot mainline.

The migration is intentionally a contract-shape cleanup. It must not change review, materialization, activation, runtime registry, Agent Memory recall, compression, sanitization, promotion, or Control Plane behavior.

## 2. Non-Goals

Do not include these changes in this work:

- DescriptorDraftSet as a first-class batch review or materialization core.
- Full change-set, impact, or compatibility recomputation redesign.
- Runtime hot reload.
- Runtime registry mutation.
- Activation approval flow changes.
- Control Plane tool behavior changes.
- Agent Memory recall scoring changes.
- Agent Memory compression, sanitization, promotion, source expansion, or canonical hash behavior changes.
- Source generator or analyzer work for snapshot generation.
- Real LLM/provider integration.

## 3. Design Approach

Use a full boundary snapshot migration.

`ISnapshotable<T>` remains in `CrestCreates.Snapshot.Abstractions`. Any model that crosses a store, runtime, package, authoring, or memory boundary and requires defensive copy semantics should implement `ISnapshotable<T>`.

`Snapshot()` becomes the only formal boundary-copy verb.

This work is a hard migration:

- Move production callers from `Clone()` and `CreateClone()` to `Snapshot()`.
- Delete `Clone()` and `CreateClone()` after call sites are migrated.
- Do not keep `[Obsolete]` compatibility bridges.
- If a removed method is referenced, fix the caller instead of restoring the old method.

## 4. Dependency Boundary

These projects may add direct references to `CrestCreates.Snapshot.Abstractions`:

- `CrestCreates.Metadata.Abstractions`
- `CrestCreates.DescriptorDraft.Abstractions`
- `CrestCreates.Workflow.Abstractions`
- `CrestCreates.HumanTask.Abstractions`
- `CrestCreates.Organization.Abstractions`
- `CrestCreates.Agent.Memory.Abstractions`
- sample authoring contracts if they remain sample-local

This dependency direction is acceptable because `Snapshot.Abstractions` is a lower-level contract package and does not depend on Runtime, Framework, Persistence, Platform, or Web.

Dependency boundary tests must continue to protect against reverse or upper-layer references.

## 5. Package Diagnostic Severity

Add `DescriptorPackageDiagnosticSeverity` beside `DescriptorPackageDiagnostic` in `CrestCreates.Metadata.Abstractions/DescriptorPackage`.

`DescriptorPackageDiagnostic.Severity` becomes:

```csharp
public required DescriptorPackageDiagnosticSeverity Severity { get; init; }
```

The enum should represent the package diagnostic domain. Expected values:

- `Info`
- `Warning`
- `Error`

Do not migrate unrelated diagnostic families in this work. These can continue using `SeverityLevel`:

- `EvidenceFinding`
- topology diagnostics
- impact diagnostics
- compatibility diagnostics
- lifecycle findings
- Agent Memory diagnostics
- review report items

When package diagnostics are projected into generic evidence, governance, or reporting surfaces that expect `SeverityLevel`, mapping must be explicit and local.

Remove package diagnostic severity helpers from `DescriptorPackageDiagnosticCodes` if they only wrap generic severity values.

## 6. Snapshot Semantics

`Snapshot()` means a defensive boundary copy, not a generic deep clone.

Required semantics:

- Returned instance preserves observable scalar values.
- Returned instance must not share mutable collection instances with the source.
- Nested boundary models should be snapshotted recursively.
- Immutable value objects may be reused.
- Unknown `object?` payload fields are not deep-cloned unless the existing model already provided that guarantee.

Examples of unknown object payloads:

- workflow variables
- workflow step variables
- HumanTask input/output

The migration must not imply stronger isolation than the current model provides for these unknown payloads.

For records, `Snapshot()` may use `this with { ... }` when every collection property is replaced by copied arrays/lists and nested snapshotable models call `.Snapshot()`.

For mutable classes, `Snapshot()` should construct a new instance and copy scalar properties plus defensive copies of collections.

## 7. Core Snapshot Migration

Migrate these metadata/draft/package models:

- `DescriptorDraft`
- `DescriptorDraftPayload`
- all concrete descriptor draft payloads
- `DescriptorDraftSet`
- `DescriptorAuthoringResult`
- `DescriptorPackageEvidence`
- `DescriptorPackage`
- package evidence/finding structures that own collections

`DescriptorDraftPayload` should expose `Snapshot()` at the abstract base level. Concrete payloads should snapshot their descriptor object and nested collections using the same defensive-copy rules currently implemented by `CreateClone()`.

`DescriptorDraft` should snapshot `Payload` and `Metadata`.

`DescriptorDraftSet` and `DescriptorAuthoringResult` should snapshot nested drafts and diagnostics.

Package evidence and package models should snapshot nested collections. This is not a package-build behavior change.

## 8. Runtime and Organization Snapshot Migration

Migrate these runtime boundary models:

- `WorkflowInstance`
- `HumanTaskInstance`

Stores should use `Snapshot()` on write and read:

- `InMemoryWorkflowInstanceStore`
- `InMemoryHumanTaskInstanceStore`

Migrate these organization boundary models:

- `OrganizationUnit`
- `Position`
- `UserOrganizationMembership`
- `UserOrganizationRoleAssignment`

Organization stores and services that currently call `Clone()` should use `Snapshot()`.

After all call sites are migrated, delete `Clone()`.

## 9. Agent Memory Snapshot Migration

Agent Memory should be included because #43 has completed and the memory runtime now has many stable public boundary contracts plus hand-written defensive copy logic.

Do not migrate a model solely because it exists in Agent Memory. Migrate it only if it crosses a store, service, runtime, or authoring boundary and currently requires or already implements defensive-copy semantics.

Migrate Agent Memory in layers so aggregate models can recursively use leaf snapshots before query/result/composition models are considered.

### 9.1 Memory Value and Reference Leaf Models

Migrate leaf models first:

- `AgentContextSourceRef`
- `AgentContextEvidenceRef`
- `AgentMemoryDiagnostic`
- `SanitizedAgentContent`
- source range, metadata, and evidence reference models if present

These models are copied by many aggregate contracts. They should be migrated first so later snapshots do not duplicate leaf-copy logic.

### 9.2 Memory Aggregate and Store Boundary Models

Migrate aggregate and store boundary models after leaf models:

- `AgentMemoryInvocationContext`
- `AgentConversationTurn`
- `AgentConversationRecord`
- `AgentTaskEvent`
- `AgentTaskRecord`
- `AgentCompressedContextBlock`
- `AgentCompressedContext`
- `AgentMemoryCandidate`
- `AgentMemoryItem`

These are the primary objects saved to or returned from in-memory stores. Store implementations should stop duplicating manual copy expressions and call model-owned `Snapshot()`.

### 9.3 Query, Result, and Composition Models

Migrate query/result/composition models last:

- `AgentMemoryPack`
- `AgentMemoryPackEntry` if present
- `AgentMemoryOperationRequest`
- `AgentAuthoringRequest`
- `AgentAuthoringContext`
- `AgentSourceExpansionResult` and entry models if present

`AgentMemoryQuery` and other request models should not automatically implement `ISnapshotable<T>`. Migrate a request/query model only when at least one of these is true:

- it is stored or cached;
- it is held across an asynchronous pipeline boundary;
- it contains mutable collections and is reused after submission;
- existing code already performs defensive-copy logic for it.

This preserves the rule that `ISnapshotable<T>` is a boundary-copy contract, not a generic DTO marker.

This migration must preserve:

- sanitize-before-store behavior
- redaction metadata
- source refs
- deterministic recall ordering
- canonical hash inputs and outputs
- non-authoritative memory semantics
- promotion lifecycle guards
- source expansion behavior

## 10. Error Handling

Snapshot methods should be deterministic and side-effect free.

They should not perform:

- validation
- authorization
- persistence
- sanitization
- canonical hash recomputation
- lifecycle transition
- registry mutation

If a model contains an unsupported polymorphic value that existing code already reuses by reference, `Snapshot()` may preserve that reference. The method should not throw solely because a value is not deeply cloneable.

For package diagnostic severity, unknown string-to-enum compatibility is not required. The domain enum is the contract. Serialization tests should validate the intended enum representation.

## 11. Testing

Add or update focused snapshot tests for:

- `DescriptorDraft` and each draft payload type.
- `DescriptorDraftSet` and `DescriptorAuthoringResult`.
- `DescriptorPackageEvidence` and package diagnostics where collections exist.
- `WorkflowInstance`.
- `HumanTaskInstance`.
- Organization models.
- Agent Memory boundary models with nested collections.

Each test should verify:

- snapshot is not the same instance for reference types.
- scalar values are preserved.
- collection instances are not shared.
- nested boundary models are snapshotted recursively.
- mutating source collections after snapshot does not affect snapshot.
- mutating snapshot collections does not affect source where mutation is possible.

Regression tests must preserve:

- package diagnostic behavior after `DescriptorPackageDiagnosticSeverity`.
- descriptor draft store isolation after `CreateClone()` removal.
- workflow and HumanTask store isolation after `Clone()` removal.
- organization store isolation after `Clone()` removal.
- Agent Memory sanitized content, recall ordering, source refs, diagnostics, pack hashes, and authoring context composition.
- Phase 7f sample authoring golden scenario behavior.
- dependency boundaries.

Recommended verification commands:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

## 12. Implementation Order

Implement in this order:

1. #46 package diagnostic severity.
2. #35 metadata/draft/package core migration.
3. #35 runtime and organization boundary migration.
4. #35 Agent Memory boundary migration.
5. Regression test pass and dependency boundary verification.

Update `memory.md` after implementation because this changes accepted platform state: boundary models use `ISnapshotable<T>` as the single snapshot mainline.

## 13. Acceptance Criteria

This work is complete when:

- `DescriptorPackageDiagnostic.Severity` uses `DescriptorPackageDiagnosticSeverity`.
- Package-to-generic severity mapping is explicit at projection boundaries.
- Boundary models in scope implement `ISnapshotable<T>`.
- Production code uses `Snapshot()` instead of `Clone()` or `CreateClone()`.
- `Clone()` and `CreateClone()` are removed from migrated models.
- Store read/write boundaries continue to isolate mutable collections.
- Agent Memory behavior is unchanged except for using `Snapshot()` as the copy primitive.
- No behavior changes are introduced for review, materialization, activation, runtime registry, or Control Plane flows.
- Targeted test suites pass.
- Dependency boundary tests pass.
