# Phase 7c Follow-up: Agent Draft Payload Contract Source Generator Design

- Status: Design approved
- Date: 2026-06-21
- Issue: #42
- Depends on: #41

## Objective

Generate the Agent-editable metadata contract for descriptor draft payloads.

This is a contract generator, not a full domain mirror. It must keep the Agent-facing payload surface small, deterministic, and AOT-friendly, while preserving the domain substructures that are not meant to be edited directly.

`Preserve` is never emitted into Agent DTOs, never adapter-editable, and never represented as an opaque subtree, blob, or token. It exists only in the generator spec, generated manifest, create strategy, and merge strategy.

## Scope Boundary

The ownership chain is:

```text
Descriptor abstractions <- DescriptorDraft.Abstractions <- Agent.DraftContracts <- Agent.ControlPlane.Abstractions <- Agent.ControlPlane
```

`Agent.DraftContracts` is the new contract package. `Agent.ControlPlane.Abstractions` consumes it. `Agent.ControlPlane` uses the generated contract helpers through the abstractions boundary.

`CrestCreates.Agent.ControlPlane.Projections.AgentReviewResultDtoProjection` remains hand-written and out of scope for this phase.

The public surface is split by namespace:

- `CrestCreates.Agent.DraftContracts.Dto` contains request/result DTO types only.
- `CrestCreates.Agent.DraftContracts.Projection` and `CrestCreates.Agent.DraftContracts.Merge` are consumed only by the `Agent.ControlPlane` implementation.
- `CrestCreates.Agent.DraftContracts.Specs` is internal generator-only input.

`Agent.ControlPlane.Abstractions` must not expose projection helpers, `DescriptorDraftPayload` create/merge authority, or projection result internals in public signatures. Boundary tests must verify that the abstraction contract graph contains DTO types only.

## Architecture

### New Project

`src/Runtime/Agent/CrestCreates.Agent.DraftContracts`

Responsibilities:

- own the generated request/result DTOs in `Dto`;
- own the generated one-of payload DTOs and branch-specific patch DTOs in `Dto`;
- own the generated changed-field enums in `Dto`;
- own the generated projection helpers in `Projection` and merge helpers in `Merge`;
- own the generated manifest and validation helpers;
- host the central contract specs under `Specs/`;
- expose only the minimal runtime primitives required by the generated code.

### Generator

`src/Tooling/CrestCreates.CodeGenerator/AgentDraftContractGenerator`

Type:

- `netstandard2.0`
- `IIncrementalGenerator`

Responsibilities:

- read the central contract specs and internal marker attributes;
- classify every public persistent descriptor property exactly once;
- generate DTOs, reference wrappers, changed-field enums, projection helpers, manifest, and validation helpers;
- fail the build on any contract ambiguity or unsupported shape.

### Internal Contract Source

The contract source of truth lives in `Agent.DraftContracts/Specs`.

The project uses internal generator-only attributes for contract wiring. These are implementation details, not a public extension surface. The spec files are the authoritative source for field classification and update behavior.

## Contract Model

### Primary Classifications

Every public persistent descriptor property must have exactly one primary classification:

- `EditableScalar`
- `EditableReference`
- `Preserve`
- `Unsupported`

Modifiers apply on top of the primary classification:

- `RequiredOnCreate`
- `Nullable`
- `Collection`
- `ContractName`

Rules:

- exactly one primary classification is required for every public persistent property;
- `RequiredOnCreate` is a modifier, not a classification;
- `Nullable` and `Collection` describe shape and patch semantics;
- `ContractName` changes the generated contract-facing name, not the underlying semantic identity.

### Classification Semantics

- `EditableScalar`: directly editable primitive, enum, string, or value-type metadata.
- `EditableReference`: directly editable descriptor reference or typed reference.
- `Preserve`: not emitted into DTOs, not adapter-editable, and not represented as an opaque preserved subtree/token/blob. It is tracked only in the generator spec, generated manifest, create strategy, and merge strategy.
- `Unsupported`: not part of the editable contract; it exists so the generator can fail closed and block accidental exposure.

### Preserve Create Strategy

`Preserve` requires one create strategy:

- `CreateDefault`
- `KnownDomainDefault`
- `CreateUnsupported`

`CreateDefault` must be deterministic. `KnownDomainDefault` must be verified against the current domain shape before it is accepted. `CreateUnsupported` is strict: `Create` returns `AgentDraftContractResult` failure with `ADPC008`, constructs no payload, and emits no token, default, or fallback.

`Merge` copies `Preserve` fields from the existing domain payload unchanged when the spec allows merge.

`Unsupported` also requires a non-empty `Reason`.
`Preserve` requires a non-empty `Reason`.

## Generated Types

### DTO Namespace

`CrestCreates.Agent.DraftContracts.Dto` is the only namespace consumed by `Agent.ControlPlane.Abstractions`.

It contains request/result DTO types only:

- `CreateDescriptorDraftRequest`
- `UpdateDescriptorDraftRequest`
- `AgentDraftContractResult<T>`
- `AgentDraftContractError`
- `AgentDraftPayloadDto`
- `AgentDraftPayloadPatchDto`
- per-kind payload DTOs
- per-kind patch DTOs
- per-kind changed-field enums
- reference DTOs that are required on the public request/result boundary

### Payload DTOs and Patch DTOs

`AgentDraftPayloadDto` is the create payload root one-of wrapper.

Shape:

- one discriminator;
- one populated branch;
- no `ChangedFields`;
- no mixed-kind payloads;
- no reflection-based polymorphism;
- no fallback branch.

`AgentDraftPayloadPatchDto` is the update payload root one-of wrapper.

Shape:

- one discriminator;
- one populated branch;
- root invariant permits exactly one branch matching the discriminator;
- no mixed-kind payloads;
- no reflection-based polymorphism;
- no fallback branch.

Each selected branch is `Agent{Kind}DraftPayloadPatchDto` and contains:

- required `Payload` of `Agent{Kind}DraftPayloadDto`;
- required `IReadOnlyList<Agent{Kind}DraftChangedField> ChangedFields`.

The branch-specific patch shape prevents `Workflow` payloads from being paired with `Capability` changed fields.

### Per-Kind DTOs

Generate one DTO per descriptor kind:

- `AgentCapabilityDraftPayloadDto`
- `AgentWorkflowDraftPayloadDto`
- `AgentHumanTaskDraftPayloadDto`
- `AgentFormDraftPayloadDto`
- `AgentEventDraftPayloadDto`
- `AgentSchemaDraftPayloadDto`

Generate the matching patch DTOs:

- `AgentCapabilityDraftPayloadPatchDto`
- `AgentWorkflowDraftPayloadPatchDto`
- `AgentHumanTaskDraftPayloadPatchDto`
- `AgentFormDraftPayloadPatchDto`
- `AgentEventDraftPayloadPatchDto`
- `AgentSchemaDraftPayloadPatchDto`

These DTOs are the Agent-facing edit contract. They are not the full domain model, and they never carry preserved opaque subtrees.

### Reference Shapes

Reuse existing stable reference shapes where possible. Do not generate a duplicate `DescriptorRef`.

Reference generation rules:

- `CrestCreates.Metadata.Abstractions.DescriptorRef` is used directly when it can losslessly represent the domain shape.
- `AgentVersionedDescriptorRefDto` is generated only when `DescriptorRef` cannot losslessly represent the domain shape.
- `TypedDescriptorRef` wrappers are generated only when additional domain semantics require them.
- typed/reference collection wrappers are generated only when additional domain semantics require them.

When `AgentVersionedDescriptorRefDto` is generated, it preserves:

- `Id`
- `Version`
- `SelectionMode`
- `ExpectedContractHash`
- `Namespace` only if the domain shape semantically carries it

Reference behavior is fixed as follows:

| Shape | Generated behavior | Runtime validation |
|---|---|---|
| `DescriptorRef` | Use `CrestCreates.Metadata.Abstractions.DescriptorRef` directly. | Validate required identity through the existing boundary contract with `ADPC012`. |
| `VersionedDescriptorRef` | Generate `AgentVersionedDescriptorRefDto` only when lossless reuse of `DescriptorRef` is impossible. | Reject invalid runtime values with `ADPC012`. |
| `TypedDescriptorRef` | Generate only when additional domain semantics require a dedicated wrapper. | Reject invalid runtime values with `ADPC012`. |
| `ReferenceCollection` | Generate only when additional domain semantics require a collection wrapper. Preserve order and duplicates unless the domain property declares set semantics. | Reject invalid runtime values with `ADPC012`. |

Unsupported reference types are compile-time errors and must fail with `ADP010`.

### Changed Fields

Generate one strongly typed `ChangedFields` enum per descriptor kind.

Rules:

- create uses `AgentDraftPayloadDto` and has no `ChangedFields`;
- merge uses `AgentDraftPayloadPatchDto`;
- `ChangedFields` must be non-empty on update and empty lists are rejected with `ADPC004`;
- listed field + null value clears the field;
- absent field preserves the existing value;
- unlisted field preserves the existing value;
- no partial collection element patching;
- update semantics are whole-field replacement only.

## Projection API

Generate the following API for each kind and for the wrapper:

- `FromDomain(DescriptorDraftPayload)`
- `Create(AgentDraftPayloadDto)`
- `Merge(AgentDraftPayloadPatchDto, DescriptorDraftPayload)`

Return type:

- `AgentDraftContractResult<T>`

Error type:

- `AgentDraftContractError`

Rules:

- normal validation failures return result objects, not exceptions;
- exceptions are reserved for programmer errors and generator bugs;
- null input is a validation result, not a runtime throw;
- all projection helpers must be deterministic and side-effect free.

Request DTO pseudocode:

```text
CreateDescriptorDraftRequest
  Payload: AgentDraftPayloadDto

UpdateDescriptorDraftRequest
  Payload: AgentDraftPayloadPatchDto?
```

### FromDomain Rules

`FromDomain` projects a persisted domain payload into the Agent contract.

Rules:

- emit exactly one branch that matches the descriptor kind;
- map every `EditableScalar` and `EditableReference` field;
- do not expose `Preserve` as an opaque subtree, blob, or token;
- keep `Preserve` only in the generator spec, generated manifest, create strategy, and merge strategy;
- do not invent fallback values for unsupported kinds;
- do not expose runtime-only or store-only state;
- emit a validation error if the source shape cannot be represented safely.

### Create Rules

`Create` converts the Agent contract into a new domain payload.

Rules:

- require `RequiredOnCreate` fields;
- apply the declared create strategy for each `Preserve` field;
- enforce `Nullable` and `Collection` rules;
- reject mixed-kind payloads;
- reject branches that do not match the discriminator;
- reject any `Unsupported` field use;
- ignore `ChangedFields` entirely because create does not carry them.

### Merge Rules

`Merge` updates an existing domain payload using a partial Agent contract.

Rules:

- require `AgentDraftPayloadPatchDto`;
- require non-empty `ChangedFields`;
- preserve any field not listed in `ChangedFields`;
- listed field + null clears the field if allowed by the spec;
- listed field + value replaces the field;
- copy `Preserve` fields from the existing domain payload unchanged;
- preserve domain substructures not represented as editable fields;
- the current Workflow, HumanTask, Form, Event, Schema, and Capability nested fields are representative merge regression fixtures derived from current classifications, not create-survival requirements and not a permanent manual inventory;
- never replace the whole payload when a field-level merge is sufficient;
- reject any shape that violates kind consistency.

## Runtime Validation Codes

`AgentDraftContractResult<T>` returns validation failures using `AgentDraftContractErrorCodes`.

Runtime codes:

| Code | Meaning |
|---|---|
| `ADPC001` | Input payload is null or structurally missing. |
| `ADPC002` | Discriminator does not match the populated branch. |
| `ADPC003` | Descriptor kind is not supported by the generated contract. |
| `ADPC004` | `ChangedFields` is missing or empty on update. |
| `ADPC005` | `ChangedFields` contains an unknown field for the selected branch. |
| `ADPC006` | A `RequiredOnCreate` field is missing or empty on create. |
| `ADPC007` | A non-nullable field received null. |
| `ADPC008` | The requested kind or field is classified as `CreateUnsupported`, so create cannot construct a payload. |
| `ADPC009` | A collection field has an invalid shape or element type. |
| `ADPC010` | Preserve strategy cannot initialize or carry the field safely. |
| `ADPC011` | The source domain shape cannot be represented by the contract. |
| `ADPC012` | A runtime reference contract value is invalid, including missing required identity/version/hash or an invalid collection element. |

## Compiler Diagnostics

The generator must fail the build with compile-time diagnostics when the contract spec is ambiguous or incomplete.

`ADP` is the compile-time generator family. It is owned by `AgentDraftContractDiagnosticIds` in generator/tooling code and covers `ADP001` through `ADP010`. `ADPC` is the runtime contract validation family. It is owned by `AgentDraftContractErrorCodes` in generated/runtime contract code and covers `ADPC001` through `ADPC012`. Never place both families in one enum or type.

| Code | Meaning |
|---|---|
| `ADP001` | No contract spec exists for a public persistent descriptor type. |
| `ADP002` | A public persistent property has no primary classification. |
| `ADP003` | A public persistent property has more than one primary classification. |
| `ADP004` | `Preserve` or `Unsupported` is missing a non-empty `Reason`. |
| `ADP005` | `Preserve` is missing a valid create strategy. |
| `ADP006` | `RequiredOnCreate` is applied to an invalid classification or shape. |
| `ADP007` | `Nullable` or `Collection` conflicts with the CLR shape or the classification. |
| `ADP008` | `ContractName` is invalid or duplicated within the contract. |
| `ADP009` | The generated contract would be unstable, ambiguous, or non-deterministic. |
| `ADP010` | A reference shape is unsupported or incompatible at compile time. |

These diagnostics are a compile-error gate. There is no manual fallback path.

## Incremental Pipeline

The generator must be incremental and deterministic.

### Input Order

1. Read the contract specs from `Agent.DraftContracts/Specs`.
2. Bind internal generator attributes.
3. Resolve the public persistent properties for each descriptor kind.
4. Classify and validate every property.
5. Build the contract model.
6. Emit DTOs, patch DTOs, reference helpers, changed-field enums, projection helpers, manifest, and validation helpers.

### Ordering Rules

Use deterministic ordering everywhere:

- descriptor kind order first;
- contract name second;
- field name third;
- modifier order fourth;
- generated type name last.

Hint names and emitted file names must be stable across builds.

## JSON Ownership

Do not generate a `JsonSerializerContext` from this generator.

Reason:

- generator ordering makes JSON context generation in the same round brittle;
- the DTO project compiles first;
- the existing `AgentControlPlaneToolJsonSerializerContext` in `Agent.ControlPlane.Abstractions` registers the generated manifest set downstream: root create payload DTO, root patch DTO, all per-kind payload DTOs, all per-kind patch DTOs, all per-kind changed-field enums, generated reference DTO/wrapper types when present, and request/result roots that contain them;
- the generated manifest drives set-equality and `GetTypeInfo` coverage tests.

The JSON contract remains source-generated and AOT-friendly, but the context ownership stays with the downstream control-plane abstractions project.

## Tests

### Generator Tests

Add generator tests for:

- diagnostics `ADP001` through `ADP010`;
- deterministic ordering and hint names;
- generated create wrapper, patch wrapper, and per-kind DTO structure;
- generated `ChangedFields` enums and branch-specific patch DTOs;
- generated manifest set coverage;
- no reflection fallback in emitted code.

### Contract Integration Tests

Create tests and merge tests are separate.

Create tests cover:

- deterministic `CreateDefault`;
- verified `KnownDomainDefault`;
- `CreateUnsupported` returning `ADPC008` with no payload construction.

Merge tests alone verify that `Preserve` fields survive unchanged from the existing payload.

Add six actual descriptor merge regression tests. These are derived from the current classifications, not create-survival requirements and not a permanent hand-maintained inventory:

1. Workflow steps, transitions, and targets survive merge.
2. HumanTask interaction, schema refs, timeout, permissions, and outcomes survive merge.
3. Form fields and layout survive merge.
4. Event payload schema, importance, and change kind survive merge.
5. Schema fields, validation rules, and refs survive merge.
6. Capability unexposed fields remain unexposed and do not reappear through merge.

The concrete fields above are the current regressions to keep covered. New persistent descriptor fields are caught by compile-time closure, receive a primary classification through the spec, and automatically enter the generated merge regression tests and manifest expectations.

### Runtime Test Coverage

At least one service or contract test must cover each runtime validation code:

| Code | Required test coverage |
|---|---|
| `ADPC001` | Null or structurally missing payload. |
| `ADPC002` | Discriminator mismatch. |
| `ADPC003` | Unsupported descriptor kind. |
| `ADPC004` | Missing or empty `ChangedFields`. |
| `ADPC005` | Unknown field in the selected patch branch. |
| `ADPC006` | Missing or empty `RequiredOnCreate` field. |
| `ADPC007` | Invalid enum or scalar nullability violation. |
| `ADPC008` | `CreateUnsupported` returns failure with no payload construction. |
| `ADPC009` | Invalid collection shape or element type. |
| `ADPC010` | Preserve strategy cannot initialize or carry the field safely. |
| `ADPC011` | Existing payload kind mismatch or unrepresentable domain shape. |
| `ADPC012` | Invalid runtime reference contract value, including identity change, invalid clear, invalid non-nullability, missing required identity/version/hash, or invalid collection element. |

### Boundary Tests

Add tests that prove:

- reference metadata survives round-trip for `DescriptorRef`, `AgentVersionedDescriptorRefDto`, and any generated typed/reference collection wrappers;
- `CreateDescriptorDraft` / `UpdateDescriptorDraft` service paths reject invalid request payloads without saving;
- `Agent.ControlPlane.Abstractions` public signatures contain DTO types only;
- no reflection-based fallback path is used by the active contract flow.
- `GeneratedAgentDraftPayloadContractManifest.ContractTypes` has set equality with the non-null `JsonTypeInfo` entries from the downstream context.
- the stable `DescriptorRef` is registered by existing context ownership and is not generated.

## Migration Sequence

The implementation order is constrained by the current codebase and should be followed as a closed migration:

1. Create `CrestCreates.Agent.DraftContracts`.
2. Add the generator under `src/Tooling/CrestCreates.CodeGenerator/AgentDraftContractGenerator`.
3. Add the central contract specs under `Agent.DraftContracts/Specs`.
4. Generate the public JSON shape for the current Agent draft payload contract, including the manifest-tracked root create payload DTO, root patch DTO, per-kind payload DTOs, per-kind patch DTOs, per-kind changed-field enums, generated reference DTO/wrapper types when present, and the request/result roots that contain them.
5. Switch the Agent Control Plane service and abstractions to the generated DTOs and contract helpers.
6. Move the hand-written DTO and projection files to `99_RecycleBin/phase-7c-agent-draft-payload-manual`.
7. Remove the old compilation references from the active projects.
8. Update the tests introduced for #41 so they target the generated manifest set and branch-specific changed-field enums.
9. Update `memory.md` to record the new closed mainline.

`AgentReviewResultDtoProjection` is explicitly excluded from this migration.

## Non-Goals

- Do not generate a full descriptor domain mirror.
- Do not add a runtime reflection fallback.
- Do not keep a handwritten parallel DTO path in the active build.
- Do not generate JSON contexts from this generator.
- Do not change review-result projection behavior in this phase.
- Do not add MCP, HTTP, CLI, or TUI adapter code here.
- Do not add new governance or activation semantics.
- Do not introduce public preserve tokens, opaque preserve payloads, or a duplicate `DescriptorRef`.

## Acceptance Criteria

- `CrestCreates.Agent.DraftContracts` exists and compiles as the contract home for generated draft DTOs.
- `AgentDraftContractGenerator` generates the DTOs, patch DTOs, changed-field enums, projection helpers, and manifest deterministically.
- Every public persistent descriptor property has exactly one primary classification.
- `Preserve` and `Unsupported` each carry a non-empty `Reason`.
- `Preserve` always carries a create strategy.
- `Create` uses `AgentDraftPayloadDto` and never sees `ChangedFields`; `Merge` uses `AgentDraftPayloadPatchDto`.
- `CreateDefault`, `KnownDomainDefault`, and `CreateUnsupported` are the only create strategies.
- `CreateUnsupported` returns `ADPC008` and constructs no payload.
- `Merge` copies `Preserve` fields from the existing payload unchanged.
- `FromDomain`, `Create`, and `Merge` return `AgentDraftContractResult<T>` and do not throw for normal validation failures.
- Compiler diagnostics `ADP001` through `ADP010` fail the build on invalid contract specs and remain isolated from runtime validation codes.
- Runtime validation codes `ADPC001` through `ADPC012` are covered by tests and remain isolated from compiler diagnostics.
- The create tests cover deterministic `CreateDefault`, verified `KnownDomainDefault`, and `CreateUnsupported` failure without payload construction.
- The runtime test table covers every `ADPC001` through `ADPC012` scenario with at least one service or contract test.
- The six descriptor merge regression tests pass and prove the preserved substructures remain intact.
- The downstream JSON context registers the generated manifest set, and `GeneratedAgentDraftPayloadContractManifest.ContractTypes` has set equality with the non-null `JsonTypeInfo` entries from the downstream context.
- The stable `DescriptorRef` comes from existing context ownership and is not generated.
- The active build contains no hand-written fallback for the Agent draft payload contract.
