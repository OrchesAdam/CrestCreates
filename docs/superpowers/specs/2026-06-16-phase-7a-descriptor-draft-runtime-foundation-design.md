# Phase 7a — Descriptor Draft Runtime Foundation: Design Spec

> **Date:** 2026-06-16 | **Status:** Approved | **Phase 7a**
> **Parent Issue:** [#13 — Phase 7a: Descriptor Draft Runtime Foundation](https://github.com/OrchesAdam/CrestCreates/issues/13)

---

## 1. Overview

### 1.1 Goal

Introduce a deterministic Descriptor Draft Runtime that represents human-, agent-, import-, or generator-produced descriptor changes as reviewable drafts before they can affect active registries or runtime activation.

Phase 7a is the **draft runtime only**. It must work without any LLM integration.

### 1.2 Position in the Framework

```
Human / Agent / Import / Generator
        ↓
   DescriptorDraft  (create, store, validate)
        ↓
   Materializer     (draft + current inventory snapshot → proposed inventory snapshot)
        ↓
   ReviewService    (orchestrates Phase 6 Control Plane over proposed inventory)
        ↓
    ReviewResult     (validation, materialization, impact, compatibility,
                     governance, stable hashes, package preview, diagnostics, IsActivationEligible)
        ↓
   [STOP]           (no activation in Phase 7a)
```

Phase 7a sits at the authoring boundary between descriptor intake and the existing Phase 6 Control Plane. It produces reviewable proposed state; it never produces active runtime state.

### 1.3 Boundary Rule

```
7a creates, stores, validates, materializes, and reviews drafts.
7a does NOT activate runtime registries.
7a does NOT call business handlers.
7a does NOT invoke LLMs.
7a does NOT expose MCP/tool surfaces.
7a does NOT perform human approval workflows.
```

### 1.4 Design Principles

1. **Proposed state, not active state** — every operation produces proposed/snapshotted state; nothing mutates registries.
2. **Compose, do not recompute** — the ReviewService delegates to Phase 6 services; it does not re-implement them.
3. **Stateless services (except store)** — Validator, Materializer, and ReviewService are stateless; only the Store holds draft state (in-memory, non-durable).
4. **AoT-friendly** — records, enums, switch-based dispatch by `DescriptorKind` and payload type; zero runtime reflection, zero dynamic JSON.
5. **Early-stop semantics** — validation failure stops the pipeline before materialization; materialization failure stops before Phase 6 analysis.
6. **Snapshot-on-read** — the store returns cloned/snapshotted drafts; no external mutation of stored state.
7. **Typed payloads carry full descriptors** — payloads contain complete proposed descriptor content, not field-level patches.

---

## 2. Scope Boundary

### 2.1 In Scope

**Projects:**
- `CrestCreates.DescriptorDraft.Abstractions`
- `CrestCreates.DescriptorDraft`
- `CrestCreates.DescriptorDraft.Tests`

**Enums:**
- `DescriptorDraftOperation` (Create, Update, Deprecate, Remove)
- `DescriptorDraftAuthorKind` (Human, Agent, System, Import, Generator)
- `DescriptorDraftStatus` (Created, Invalid, Materialized, Reviewed, Cancelled)

**Models:**
- `DescriptorDraft` — central draft entity with required + optional fields
- `DescriptorDraftDiagnostic` — structured diagnostic (Code, Severity, Message, etc.)
- `DescriptorDraftValidationResult` — validator output
- `DescriptorDraftMaterializationResult` — materializer output
- `DescriptorDraftReviewResult` — review service output (composes all Phase 6 results)
- `DraftQuery` — store query filter (DescriptorKind, Operation, AuthorKind, Status, date range)

**Typed Payloads:**
- `SchemaDescriptorDraftPayload` — proposed SchemaDescriptor content
- `FormDescriptorDraftPayload` — proposed FormDescriptor content
- `CapabilityDescriptorDraftPayload` — proposed CapabilityDescriptor content
- `HumanTaskDescriptorDraftPayload` — proposed HumanTaskDescriptor content
- `WorkflowDescriptorDraftPayload` — proposed WorkflowDescriptor content
- `EventDescriptorDraftPayload` — proposed EventDescriptor content

**Services:**
- `IDescriptorDraftStore` + `InMemoryDescriptorDraftStore`
- `IDescriptorDraftValidator` + `DefaultDescriptorDraftValidator`
- `IDescriptorDraftMaterializer` + `DefaultDescriptorDraftMaterializer`
- `IDescriptorDraftReviewService` + `DefaultDescriptorDraftReviewService`
- `DescriptorPackagePreview` — lightweight preview DTO for review result

**DI Registration:**
- `AddDescriptorDrafts()` extension method in `CrestCreates.DescriptorDraft`

**Tests:**
- Unit tests for store, validator, materializer, review service (18 tests minimum per the issue test matrix)

### 2.2 Out of Scope

- No LLM planner / prompt / agent tool surface
- No MCP / tool projection
- No direct runtime activation (`ICapabilityDispatcher`, workflow execution, etc.)
- No direct `IDescriptorRegistry` mutation
- No durable production store (in-memory only)
- No UI / API endpoints
- No auto-apply
- No JSON Patch or field-level diff semantics
- No `DescriptorDraftSet` / `DraftGroupId` (deferred to later phase)
- No HumanTask approval workflow
- No review report prose builder
- No fix proposal engine

---

## 3. Project Structure

### 3.1 New Projects

```
framework/src/CrestCreates.DescriptorDraft.Abstractions/
  CrestCreates.DescriptorDraft.Abstractions.csproj
  DescriptorDraft.cs
  DescriptorDraftOperation.cs
  DescriptorDraftAuthorKind.cs
  DescriptorDraftStatus.cs
  DescriptorDraftDiagnostic.cs
  DescriptorDraftValidationResult.cs
  DescriptorDraftMaterializationResult.cs
  DescriptorDraftReviewResult.cs
  DraftQuery.cs
  DescriptorDraftPayloads/
    SchemaDescriptorDraftPayload.cs
    FormDescriptorDraftPayload.cs
    CapabilityDescriptorDraftPayload.cs
    HumanTaskDescriptorDraftPayload.cs
    WorkflowDescriptorDraftPayload.cs
    EventDescriptorDraftPayload.cs
  IDescriptorDraftStore.cs
  IDescriptorDraftValidator.cs
  IDescriptorDraftMaterializer.cs
  IDescriptorDraftReviewService.cs

framework/src/CrestCreates.DescriptorDraft/
  CrestCreates.DescriptorDraft.csproj
  DescriptorDraftServiceCollectionExtensions.cs
  InMemoryDescriptorDraftStore.cs
  DefaultDescriptorDraftValidator.cs
  DefaultDescriptorDraftMaterializer.cs
  DefaultDescriptorDraftReviewService.cs

framework/test/CrestCreates.DescriptorDraft.Tests/
  CrestCreates.DescriptorDraft.Tests.csproj
  InMemoryDescriptorDraftStoreTests.cs
  DefaultDescriptorDraftValidatorTests.cs
  DefaultDescriptorDraftMaterializerTests.cs
  DefaultDescriptorDraftReviewServiceTests.cs
```

### 3.2 Project Dependencies

```
CrestCreates.DescriptorDraft.Abstractions
  ├── CrestCreates.Metadata.Abstractions      (DescriptorKind, IDescriptor, Phase 6 interfaces)
  ├── CrestCreates.Domain.Shared
  ├── CrestCreates.Schema.Abstractions         (SchemaDescriptor, Clone())
  ├── CrestCreates.Capability.Abstractions     (CapabilityDescriptor, Clone())
  ├── CrestCreates.Form.Abstractions           (FormDescriptor, Clone())
  ├── CrestCreates.Event.Abstractions          (EventDescriptor, Clone())
  ├── CrestCreates.HumanTask.Abstractions      (HumanTaskDescriptor, Clone())
  └── CrestCreates.Workflow.Abstractions       (WorkflowDescriptor, Clone())

CrestCreates.DescriptorDraft
  ├── CrestCreates.DescriptorDraft.Abstractions
  ├── CrestCreates.Metadata.Abstractions       (Phase 6 interfaces only; NOT Metadata implementation)
  ├── CrestCreates.MultiTenancy.Abstract
  └── Microsoft.Extensions.Logging

CrestCreates.DescriptorDraft.Tests
  ├── CrestCreates.DescriptorDraft.Abstractions
  ├── CrestCreates.DescriptorDraft
  ├── (existing descriptor projects for test construction)
  └── CrestCreates.TestBase
```

**Dependency discipline**: `CrestCreates.DescriptorDraft` depends on `CrestCreates.Metadata.Abstractions` (interfaces) but NOT on `CrestCreates.Metadata` (implementation). The `ReviewService` takes Phase 6 interfaces via constructor injection; the DI container resolves the implementations. This prevents the Draft module from coupling to Metadata implementation internals.

### 3.3 DI Extension

```csharp
// CrestCreates.DescriptorDraft/DescriptorDraftServiceCollectionExtensions.cs
public static class DescriptorDraftServiceCollectionExtensions
{
    public static IServiceCollection AddDescriptorDrafts(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorDraftStore, InMemoryDescriptorDraftStore>();
        services.TryAddSingleton<IDescriptorDraftValidator, DefaultDescriptorDraftValidator>();
        services.TryAddSingleton<IDescriptorDraftMaterializer, DefaultDescriptorDraftMaterializer>();
        services.TryAddSingleton<IDescriptorDraftReviewService, DefaultDescriptorDraftReviewService>();
        return services;
    }
}
```

All `TryAddSingleton` — the store is a `ConcurrentDictionary`-backed singleton (non-durable in-memory); validator, materializer, and review service are true stateless singletons.

---

## 4. Core Models

### 4.1 DescriptorDraft

```csharp
public sealed record DescriptorDraft
{
    // --- Required ---
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required DescriptorKind DescriptorKind { get; init; }
    public required string DescriptorId { get; init; }
    public required DescriptorDraftOperation Operation { get; init; }
    public required DescriptorDraftAuthorKind AuthorKind { get; init; }
    public required string AuthorId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DescriptorDraftPayload Payload { get; init; }

    // --- Optional ---
    public string? BaseVersion { get; init; }
    public string? ProposedVersion { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public string? CorrelationId { get; init; }
    public string? Source { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    // --- Computed ---
    public DescriptorDraftStatus Status { get; init; } = DescriptorDraftStatus.Created;

    public DescriptorDraft Clone() => this with
    {
        Payload = Payload.Clone(),
        Metadata = Metadata is null
            ? null
            : new Dictionary<string, string>(Metadata, StringComparer.Ordinal)
    };
}
```

**Clone semantics**: `Payload.Clone()` must deep-clone the embedded descriptor (each typed payload's `Clone()` copies the descriptor via `Descriptor.Clone()`). `Metadata` dictionary is deep-copied to prevent shared mutable references from bypassing snapshot-on-read protection.

**Status semantics**: Phase 7a only tracks draft authoring lifecycle. Governance result lives in `DescriptorDraftReviewResult`. Activation/review workflow status belongs to later phases.

**Version validation rules** (enforced by `IDescriptorDraftValidator`, not the model):

| Operation | BaseVersion | ProposedVersion |
|---|---|---|
| Create | Must be null/empty | Required (unless payload descriptor version is authoritative) |
| Update | Required | Required (or inferred from payload descriptor version) |
| Deprecate | Required | Required if lifecycle/version changes |
| Remove | Required | Usually null unless Remove is modeled as tombstone |

### 4.2 Enums

```csharp
public enum DescriptorDraftOperation { Create, Update, Deprecate, Remove }

public enum DescriptorDraftAuthorKind { Human, Agent, System, Import, Generator }

public enum DescriptorDraftStatus
{
    Created,       // Draft exists in store
    Invalid,       // Validation failed
    Materialized,  // Materialization produced a proposed inventory
    Reviewed,      // Control Plane review completed
    Cancelled      // Draft was cancelled
}
```

**Status boundary note**: `ReviewRequired`, `Approved`, `Rejected`, and `Applied` belong to later phases (7e activation/review workflow). Phase 7a must not produce these transitions. The governance decision lives in the `ReviewResult`, not in the draft status.

### 4.3 Typed Payloads

Each payload carries a **full proposed descriptor** of its specific type, not patch operations. The payload is **truly typed**: `SchemaDescriptorDraftPayload` can only hold a `SchemaDescriptor`, `CapabilityDescriptorDraftPayload` can only hold a `CapabilityDescriptor`, etc. Dispatch is switch-based by `DescriptorKind` enum; the compiler enforces type correctness at construction time.

```csharp
public abstract record DescriptorDraftPayload
{
    public abstract DescriptorKind DescriptorKind { get; }
    public abstract IDescriptor GetDescriptor();
    public abstract DescriptorDraftPayload Clone();
}

public sealed record SchemaDescriptorDraftPayload(
    SchemaDescriptor Descriptor
) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Schema;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Clone() => this with { Descriptor = Descriptor.Clone() };
}

public sealed record FormDescriptorDraftPayload(
    FormDescriptor Descriptor
) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Form;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Clone() => this with { Descriptor = Descriptor.Clone() };
}

public sealed record CapabilityDescriptorDraftPayload(
    CapabilityDescriptor Descriptor
) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Capability;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Clone() => this with { Descriptor = Descriptor.Clone() };
}

// HumanTaskDescriptorDraftPayload, WorkflowDescriptorDraftPayload,
// EventDescriptorDraftPayload follow the same pattern with their respective descriptor types.
```

**Cross-project dependency**: `DescriptorDraft.Abstractions` references all six descriptor abstraction projects (`CrestCreates.Schema.Abstractions`, `.Capability.Abstractions`, `.Form.Abstractions`, `.HumanTask.Abstractions`, `.Workflow.Abstractions`, `.Event.Abstractions`) plus `CrestCreates.Metadata.Abstractions` (for `DescriptorKind` enum and `IDescriptor`). This is the **authoring boundary** — not a low-level kernel — and the dependency is acceptable. The compiler prevents mismatched payloads at construction time; the validator becomes a secondary defense.

### 4.4 Diagnostic

```csharp
public sealed record DescriptorDraftDiagnostic
{
    public required string Code { get; init; }
    public required DescriptorDraftDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public DescriptorKind? DescriptorKind { get; init; }
    public string? DescriptorId { get; init; }
    public string? DraftId { get; init; }
    public string? Path { get; init; }
    public string? RelatedDiagnosticCode { get; init; }
}

public enum DescriptorDraftDiagnosticSeverity { Info, Warning, Error, Blocker }
```

### 4.5 Validation and Materialization Results

```csharp
public sealed record DescriptorDraftValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }
}

public sealed record DescriptorDraftMaterializationResult
{
    public required bool IsMaterialized { get; init; }
    public required IReadOnlyList<IDescriptor> ProposedInventory { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }
}
```

### 4.6 Review Result

```csharp
public sealed record DescriptorDraftReviewResult
{
    // --- Identity ---
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }

    // --- Core results ---
    public required DescriptorDraftValidationResult ValidationResult { get; init; }
    public DescriptorDraftMaterializationResult? MaterializationResult { get; init; }

    // --- Phase 6 results (populated only when validation + materialization succeed) ---
    public ProposedInventorySummary? ProposedInventorySummary { get; init; }
    public DescriptorTopologySnapshot? TopologySnapshot { get; init; }
    public DescriptorImpactAnalysisReport? ImpactAnalysisResult { get; init; }
    public DescriptorCompatibilityReport? CompatibilityResult { get; init; }
    public DescriptorLifecycleGovernanceReport? GovernanceDecision { get; init; }
    public DescriptorStableHashes? StableHashes { get; init; }
    public DescriptorPackagePreview? PackagePreview { get; init; }

    // --- Diagnostics ---
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }

    // --- Eligibility ---
    public required bool IsActivationEligible { get; init; }
}
```

`IsActivationEligible` only means eligible for a future activation phase. It must not trigger activation in Phase 7a.

### 4.7 DescriptorPackagePreview

A lightweight preview DTO — not the full `DescriptorPackage`. This prevents the draft layer from creating "directly activatable" packages:

```csharp
public sealed record DescriptorPackagePreview
{
    public required string ManifestHash { get; init; }
    public required string SnapshotHash { get; init; }
    public required string EvidenceHash { get; init; }
    public required string EnvelopeHash { get; init; }
    public required IReadOnlyList<DescriptorRef> Descriptors { get; init; }
}
```

The real `DescriptorPackage` is bound by Phase 7e activation request, not by Phase 7a draft review.

---

## 5. Service Contracts

### 5.1 IDescriptorDraftStore

Stateful (in-memory, non-durable). Composite key `(tenantId, draftId)`.

```csharp
public interface IDescriptorDraftStore
{
    Task SaveAsync(DescriptorDraft draft, CancellationToken ct = default);
    Task<DescriptorDraft?> GetAsync(string tenantId, string draftId, CancellationToken ct = default);
    Task<IReadOnlyList<DescriptorDraft>> ListAsync(string tenantId, DraftQuery? query = null, CancellationToken ct = default);
}
```

**Implementation**: `ConcurrentDictionary<(string tenantId, string draftId), DescriptorDraft>`. `GetAsync` and `ListAsync` return `.Clone()` copies of stored drafts (snapshot-on-read).

**DraftQuery**: Supports filtering by `DescriptorKind`, `Operation`, `AuthorKind`, `Status`, and optional date range.

**DraftQuery**: 

```csharp
public sealed record DraftQuery
{
    public DescriptorKind? DescriptorKind { get; init; }
    public DescriptorDraftOperation? Operation { get; init; }
    public DescriptorDraftAuthorKind? AuthorKind { get; init; }
    public DescriptorDraftStatus? Status { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
}
```

### 5.2 IDescriptorDraftValidator

Stateless. Validates the draft envelope only — not topology, compatibility, or governance.

```csharp
public interface IDescriptorDraftValidator
{
    DescriptorDraftValidationResult Validate(DescriptorDraft draft);
}
```

**Validation rules:**
1. `DraftId` is not null/empty
2. `DescriptorKind` matches `Payload.DescriptorKind` (enforced by type at construction, validated at runtime)
3. `Payload.GetDescriptor().Id` matches `draft.DescriptorId` (envelope identity = payload identity)
4. Operation-specific version rules are satisfied (see §4.1 version validation table)
5. `DescriptorId` is not null/empty
6. `AuthorId` is not null/empty

Validator must not access the store, registries, or any Phase 6 services.

### 5.3 IDescriptorDraftMaterializer

Stateless. Converts draft + current inventory into proposed inventory.

```csharp
public interface IDescriptorDraftMaterializer
{
    DescriptorDraftMaterializationResult Materialize(
        DescriptorDraft draft,
        IReadOnlyList<IDescriptor> currentInventory);
}
```

**Rules:**

- **Create**: adds the proposed descriptor to the inventory. Fails if a descriptor with the same identity already exists.
- **Update**: replaces the existing descriptor. Fails if the base descriptor does not exist.
- **Deprecate**: modeled in the enum and payload, but `DefaultDescriptorDraftMaterializer` returns `UnsupportedOperation` diagnostics. Full implementation deferred to a later phase.
- **Remove**: modeled in the enum and payload, but `DefaultDescriptorDraftMaterializer` returns `UnsupportedOperation` diagnostics. Full implementation deferred to a later phase.
- Materialization must not mutate `currentInventory`. It operates on a copied `List<IDescriptor>`.

**Descriptor identity for materialization:**

```
Duplicate/identity check:
  (DescriptorKind, Id, Version) forms the unique key within a tenant-scoped inventory.

Create duplicate check:
  same (kind, id, version) already present → conflict.

Update target lookup:
  same (kind, id, BaseVersion) identifies the base descriptor to replace.
  proposed descriptor (kind, id, ProposedVersion) identifies the replacement.
```

**Tenant scoping**: The caller must pass an inventory snapshot scoped to `draft.TenantId`. The materializer assumes the inventory is already tenant-filtered. All descriptors in the consumer-facing inventory carry `TenantId` where applicable (e.g., capability descriptors). The materializer validates that `draft.TenantId` matches any tenant-identity-bearing descriptors in the inventory.

### 5.4 IDescriptorDraftReviewService

Stateless orchestration service. Composes validation, materialization, and all Phase 6 Control Plane services.

```csharp
public interface IDescriptorDraftReviewService
{
    Task<DescriptorDraftReviewResult> ReviewAsync(
        DescriptorDraft draft,
        IReadOnlyList<IDescriptor> currentInventory,
        CancellationToken ct = default);
}
```

**Orchestration flow:**

```text
1. Validate draft (IDescriptorDraftValidator)
   → On failure: return ReviewResult with validation diagnostics, stop.
2. Materialize proposed inventory (IDescriptorDraftMaterializer)
   → On failure: return ReviewResult with materialization diagnostics, stop.
3. Relationship extraction (IDescriptorRelationshipProvider)
4. Topology snapshot (IDescriptorTopologyBuilder)
5. Impact analysis (IDescriptorImpactAnalyzer)
6. Compatibility analysis (IDescriptorCompatibilityAnalyzer)
7. Lifecycle governance (IDescriptorLifecycleGovernanceService)
8. Stable hash computation (IDescriptorStableHashBuilder)
9. Package/evidence preview (IDescriptorPackageBuilder)
10. Return DescriptorDraftReviewResult with IsActivationEligible eligibility
```

Steps 3–9 run over the **proposed inventory** (from the materializer), never over active registries. The Phase 6 services consume `IReadOnlyList<IDescriptor>` — they are agnostic to whether the input is "active" or "proposed". This is the clean integration point.

**Early-stop semantics:**
- Validation failure → no materialization, no Phase 6 pipeline
- Materialization failure → no Phase 6 pipeline
- Phase 6 pipeline failures → captured in diagnostics; review continues when possible

**IsActivationEligible rule**: `IsActivationEligible = true` when governance decision is `Allowed` AND all phase steps completed without blockers. It is a hint for future phases; Phase 7a never acts on it.

### 5.5 Dependency Injection

The ReviewService depends on:
- `IDescriptorDraftValidator` (Phase 7a, own)
- `IDescriptorDraftMaterializer` (Phase 7a, own)
- `IDescriptorRelationshipProvider` (Phase 6a)
- `IDescriptorTopologyBuilder` (Phase 6b)
- `IDescriptorImpactAnalyzer` + `IDescriptorChangeSetBuilder` (Phase 6c)
- `IDescriptorCompatibilityAnalyzer` (Phase 6d)
- `IDescriptorLifecycleGovernanceService` (Phase 6e)
- `IDescriptorStableHashBuilder` (Phase 6g)
- `IDescriptorPackageBuilder` (Phase 6f)

All are resolved via constructor injection. The ReviewService does not use `IServiceProvider` for service location.

---

## 6. AOT / Determinism Rules

1. No runtime reflection.
2. No `JsonSerializer.Serialize(object)` without source-generated metadata.
3. No dynamic code generation (`System.Reflection.Emit`, `Expression.Compile`, etc.).
4. Explicit switch by `DescriptorKind` enum and payload type for all dispatch.
5. Stable ordering by descriptor kind, id, version using ordinal comparison (`StringComparer.Ordinal`).
6. No mutation of registry snapshots — the materializer works on copies.
7. Clone/snapshot all mutable models crossing store/materializer boundaries (`DescriptorDraft.Clone()`, snapshot-on-read in store).

---

## 7. Test Plan

### 7.1 Store Tests

| # | Test |
|---|---|
| 1 | `Save_And_Get_Returns_Cloned_Draft` — snapshot-on-read prevents mutation of stored state |
| 2 | `List_Filters_By_Tenant` — cross-tenant isolation |
| 3 | `List_Filters_By_DescriptorKind` — query filtering |
| 4 | `Get_Missing_Returns_Null` |

### 7.2 Validator Tests

| # | Test |
|---|---|
| 5 | `Rejects_Empty_DraftId` |
| 6 | `Rejects_Kind_Payload_Mismatch` — DescriptorKind ≠ Payload.DescriptorKind |
| 7 | `Rejects_Payload_DescriptorId_Mismatch` — draft.DescriptorId ≠ payload descriptor id |
| 8 | `Rejects_Create_With_BaseVersion` — Create must have null/empty BaseVersion |
| 9 | `Rejects_Update_Without_BaseVersion` — Update must have BaseVersion |
| 10 | `Valid_Draft_Passes_All_Checks` |

### 7.3 Materializer Tests

| # | Test |
|---|---|
| 11 | `Create_Adds_Descriptor_To_Proposed_Inventory` |
| 12 | `Create_Fails_On_Existing_Descriptor` — duplicate identity |
| 13 | `Update_Replaces_Descriptor_In_Proposed_Inventory` |
| 14 | `Update_Fails_On_Missing_Descriptor` — base not found |
| 15 | `Materialization_Does_Not_Mutate_Source_Inventory` |

### 7.4 Review Service Tests

| # | Test |
|---|---|
| 16 | `Stops_Early_On_Validation_Error` — no Phase 6 pipeline |
| 17 | `Stops_Early_On_Materialization_Error` — no Phase 6 pipeline |
| 18 | `Invokes_Control_Plane_For_Valid_Draft` — Phase 6 pipeline runs |
| 19 | `Allowed_Review_Result_Is_Activation_Eligible` — IsActivationEligible = true |
| 20 | `ReviewRequired_Result_Has_IsActivationEligible_False` |
| 21 | `Blocked_Result_Has_IsActivationEligible_False` |
| 22 | `Stable_Hash_And_Package_Preview_Present_When_Control_Plane_Produces` |
| 23 | `Output_Ordering_Is_Deterministic` |

### 7.5 Invariant Tests

| # | Test |
|---|---|
| 24 | `No_Path_Mutates_Active_Runtime_Registries` — verify no IDescriptorRegistry write access |
| 25 | `Store_Snapshot_On_Read_Protects_Stored_State` — modify returned draft, re-get returns original |

---

## 8. One-Line Boundary

Phase 7a creates, stores, validates, materializes, and reviews descriptor drafts through the existing Phase 6 Control Plane to produce a review result with governance decision and activation eligibility hint — but it never activates runtime registries, calls business handlers, invokes LLMs, or exposes API endpoints.

---

## 9. Implementation Order

1. Create projects (`CrestCreates.DescriptorDraft.Abstractions`, `.DescriptorDraft`, `.DescriptorDraft.Tests`) with `.csproj` and DI extension skeleton.
2. Add enums and diagnostic model.
3. Add `DescriptorDraft` model, typed payloads (6), and results (`ValidationResult`, `MaterializationResult`, `ReviewResult`).
4. Add `IDescriptorDraftStore` + `InMemoryDescriptorDraftStore` + store tests.
5. Add `IDescriptorDraftValidator` + `DefaultDescriptorDraftValidator` + validator tests.
6. Add `IDescriptorDraftMaterializer` + `DefaultDescriptorDraftMaterializer` + materializer tests.
7. Add `IDescriptorDraftReviewService` + `DefaultDescriptorDraftReviewService` + review service tests.
8. Register in `.slnx` and verify `dotnet build` and `dotnet test` pass.
9. Full test pass with zero regressions across existing Metadata suites.

---

*This spec incorporates decisions from GitHub Issue #13, the OrchesAdam implementation supplement, review feedback (6 required changes: DescriptorKind enum, truly typed payloads, deep Clone, slimmed Status enum, PackagePreview DTO, interface-only Phase 6 dependencies), and architectural review confirmations regarding version validation semantics, payload design (full descriptors, not patches), early-stop in review service, and the IsActivationEligible invariant.*
