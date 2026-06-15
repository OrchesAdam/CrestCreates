# Phase 6f — Descriptor Package / Manifest / Snapshot: Design Spec

> **Date:** 2026-06-15 | **Status:** Design Approved | **Phase 6f**
> **Parent Issue:** [#11 — Phase 6f: Descriptor Package / Manifest / Snapshot](https://github.com/OrchesAdam/CrestCreates/issues/11)

---

## 1. Overview

### 1.1 Goal

Phase 6f turns descriptor control-plane results into a deterministic, inspectable, portable package/snapshot unit:

```
Descriptor inventory
+ Descriptor hashes
+ Relationship / topology facts (already supplied)
+ Impact / compatibility / lifecycle evidence summaries (already supplied)
+ Package self-consistency diagnostics
→ deterministic DescriptorPackage
```

Phase 6f does package construction, manifest generation, immutable snapshot creation, and shallow structural diff. It does **not** decide runtime activation, mutate registries, or re-execute prior phase analyzers.

### 1.2 Position in Phase 6

```
6a Relationship facts
6b Topology snapshot
6c Impact report
6d Compatibility report
6e Governance decision
        ↓
6f Descriptor package / manifest / immutable snapshot / evidence summary / shallow diff
```

### 1.3 One-Line Boundary

**6f freezes what the descriptor control plane knows; it does not decide or apply what the runtime should do.**

### 1.4 Design Principles

1. **Consume, do not recompute** — all analysis reports are inputs; no re-validation, re-binding, re-topology, re-impact, or re-compatibility.
2. **Evolve existing types** — upgrade `DescriptorPackage`, `DescriptorManifest`, `DescriptorManifestEntry`, `DescriptorSnapshot`, `SnapshotEntry`; do not create parallel second-main-chain types.
3. **Stateless and deterministic** — builder/differ/serializer are pure functions; singleton-safe.
4. **AoT-friendly** — records, enums, static dispatch, source-generated JSON serialization; no runtime reflection, dynamic, or expression trees. `ContentHash` is computed by a new `DescriptorPackageHashComputer` using deterministic string concatenation over stable manifest/relationship/evidence fields — it does NOT use `JsonSerializer.Serialize` with runtime types, anonymous objects, or `descriptor.GetType()`.
5. **Deterministic hashing** — same inventory + same evidence → same `ContentHash`, regardless of input order or `CreatedAt`.
6. **Explicit inventory input** — builder accepts `IReadOnlyList<IDescriptor>` from caller; does not read from `IGlobalDescriptorRegistry`.
7. **Metadata/evidence package** — 6f package contains manifest identity, descriptor refs, hashes, relationships, evidence summaries, and diagnostics. It does NOT contain full descriptor payload (no `IDescriptor` objects in serialized form). This is intentional: 6f is a control-plane metadata package, not a descriptor import/export provider. Full descriptor payload import/export belongs to a later provider/import phase.

---

## 2. Scope Boundary

### 2.1 In Scope

- Upgraded `DescriptorPackage`, `DescriptorManifest`, `DescriptorManifestEntry`, `DescriptorSnapshot`, `SnapshotEntry`
- Removal of per-kind manifest entry lists (`Schemas`, `Capabilities`, …) — intentional breaking change; replaced by flat `DescriptorEntries`
- `IDescriptorPackageBuilder` + `DefaultDescriptorPackageBuilder` (stateless singleton)
- `DescriptorPackageHashComputer` (NEW, AoT-safe deterministic hash utility — string concat, no runtime JSON)
- `DescriptorPackageBuildRequest` + `DescriptorPackageBuildOptions`
- `DescriptorPackageEvidence` + `EvidenceFinding` + `EvidenceFindingCount` (normalized evidence summary)
- `DescriptorPackageRelationshipEntry` (flattened relationship facts from topology)
- `DescriptorPackageDiagnostic` + `DescriptorPackageDiagnosticCode` (self-consistency checks)
- `IDescriptorPackageDiffer` + `DescriptorPackageDiffer` (shallow structural diff)
- `DescriptorPackageDiff` + `DescriptorPackageMetadataChange` + `DescriptorPackageDiffOptions`
- `IDescriptorPackageSerializer` (source-generated JSON context, metadata/evidence only — no descriptor payload)
- `AddDescriptorPackaging()` DI registration
- Unit tests covering builder determinism, evidence summary, diagnostics, diff, serialization (42 tests total)

### 2.2 Consumed Reports (Inputs)

| Report | Source Phase | Key Types |
|---|---|---|
| `IReadOnlyList<IDescriptor>` | Descriptor providers | All concrete descriptor types |
| `DescriptorTopologySnapshot` (optional) | 6b | Nodes, Edges, Diagnostics |
| `DescriptorImpactAnalysisReport` (optional) | 6c | ChangeSet, AffectedDescriptors, Severity, Diagnostics |
| `DescriptorCompatibilityReport` (optional) | 6d | Findings, Levels, Diagnostics |
| `DescriptorLifecycleGovernanceReport` (optional) | 6e | Decisions, PackageFindings |

### 2.3 Out of Scope

- Descriptor activation / apply / registry mutation
- Approval workflow persistence
- Package repository / marketplace
- Remote sync / database persistence provider
- Rollback engine / environment promotion pipeline
- LLM draft generation
- API / UI / MCP / AgentTool exposure
- Full descriptor import/export provider
- New topology / impact / compatibility / lifecycle analysis rules
- Runtime binding resolution
- Semantic analysis in diff

---

## 3. Core Model Evolution

All types live under `CrestCreates.Metadata.Abstractions` (evolved in-place) and new types in new files.

### 3.1 DescriptorPackage (UPGRADED)

```csharp
public sealed class DescriptorPackage
{
    public DescriptorManifest Manifest { get; init; } = new();
    public DescriptorSnapshot Snapshot { get; init; } = new();
    public DescriptorPackageEvidence Evidence { get; init; } = new();
    public IReadOnlyList<DescriptorPackageDiagnostic> Diagnostics { get; init; }
        = Array.Empty<DescriptorPackageDiagnostic>();

    // Convenience passthroughs — Manifest is the single truth
    public string PackageId => Manifest.PackageId;
    public string PackageVersion => Manifest.PackageVersion;
    public string ContentHash => Manifest.ContentHash;
}
```

### 3.2 DescriptorManifest (UPGRADED)

```csharp
public sealed class DescriptorManifest
{
    public string FormatVersion { get; init; } = "1.0";
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public string? Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? Source { get; init; }
    public int DescriptorCount { get; init; }
    public IReadOnlyList<DescriptorManifestEntry> DescriptorEntries { get; init; }
        = Array.Empty<DescriptorManifestEntry>();
    public string ContentHash { get; init; } = string.Empty;
    public string? EvidenceHash { get; init; }
    public string? EnvelopeHash { get; init; }
}
```

`CreatedAt` / `CreatedBy` / `Source` are envelope metadata. `ContentHash` excludes them.

Remove the old per-kind entry lists (`Schemas`, `Capabilities`, …). The new `DescriptorEntries` is a flat, sorted list.

### 3.3 DescriptorManifestEntry (UPGRADED)

```csharp
public sealed class DescriptorManifestEntry
{
    public DescriptorRef Ref { get; init; }            // (Namespace, Id, Version)
    public DescriptorKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }
}
```

Sort order: `Namespace`, `Id`, `Version`, `Kind`, `Name`.

### 3.4 DescriptorSnapshot (UPGRADED)

```csharp
public sealed class DescriptorSnapshot
{
    public string SnapshotId { get; init; } = string.Empty;   // "snapshot_" + first 16 chars of ContentHash
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<SnapshotEntry> Descriptors { get; init; } = Array.Empty<SnapshotEntry>();
    public IReadOnlyList<DescriptorPackageRelationshipEntry> Relationships { get; init; }
        = Array.Empty<DescriptorPackageRelationshipEntry>();
}
```

`SnapshotId` is deterministic: `"snapshot_" + ContentHash[..16]`. No Guid.

### 3.5 SnapshotEntry (UPGRADED)

```csharp
public sealed class SnapshotEntry
{
    public DescriptorRef Ref { get; init; }
    public string DescriptorName { get; init; } = string.Empty;
    public DescriptorKind Kind { get; init; }
    public DescriptorState State { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }
}
```

### 3.6 DescriptorPackageRelationshipEntry (NEW)

```csharp
public sealed record DescriptorPackageRelationshipEntry
{
    public required DescriptorRef From { get; init; }
    public required DescriptorRef To { get; init; }
    public required RelationshipKind Kind { get; init; }
    public string? Role { get; init; }
    public string? SourcePath { get; init; }
    public required RelationshipStrength Strength { get; init; }
    public required bool IsRuntimeBinding { get; init; }
}
```

Copied from `DescriptorTopologySnapshot.Edges`. `SourcePath` must be preserved so package review can explain which descriptor field produced each edge.

### 3.7 DescriptorPackageEvidence (NEW)

```csharp
public sealed class DescriptorPackageEvidence
{
    // Topology
    public int TopologyNodeCount { get; init; }
    public int TopologyEdgeCount { get; init; }
    public IReadOnlyList<EvidenceFindingCount> TopologyDiagnosticCounts { get; init; }
        = Array.Empty<EvidenceFindingCount>();
    public bool HasTopologyErrors { get; init; }

    // Impact
    public DescriptorImpactSeverity MaxImpactSeverity { get; init; }
    public int AffectedDescriptorCount { get; init; }
    public int ImpactPathCount { get; init; }
    public IReadOnlyList<EvidenceFindingCount> ImpactDiagnosticCounts { get; init; }
        = Array.Empty<EvidenceFindingCount>();

    // Compatibility
    public DescriptorCompatibilityLevel MaxCompatibilityLevel { get; init; }
    public int BreakingFindingCount { get; init; }
    public int SecuritySensitiveFindingCount { get; init; }
    public int UnsupportedFindingCount { get; init; }

    // Lifecycle
    public DescriptorLifecycleDecisionKind MaxLifecycleDecision { get; init; }
    public bool RequiresReview { get; init; }
    public bool IsBlocked { get; init; }
    public int PackageFindingCount { get; init; }

    // Unified normalized findings for UI/CI/LLM consumption
    public IReadOnlyList<EvidenceFinding> NormalizedFindings { get; init; }
        = Array.Empty<EvidenceFinding>();
}
```

### 3.8 EvidenceFinding + EvidenceFindingCount (NEW)

```csharp
public sealed record EvidenceFinding
{
    public required string Source { get; init; }      // topology | impact | compatibility | lifecycle | package
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public DescriptorRef? Subject { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<DescriptorRef> RelatedRefs { get; init; } = Array.Empty<DescriptorRef>();
}

public sealed record EvidenceFindingCount
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public int Count { get; init; }
}
```

---

## 4. Builder API

```csharp
public interface IDescriptorPackageBuilder
{
    DescriptorPackage Build(DescriptorPackageBuildRequest request);
}

public sealed record DescriptorPackageBuildRequest
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public string? Name { get; init; }
    public string? CreatedBy { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public required IReadOnlyList<IDescriptor> Descriptors { get; init; }

    public DescriptorTopologySnapshot? TopologySnapshot { get; init; }
    public DescriptorImpactAnalysisReport? ImpactReport { get; init; }
    public DescriptorCompatibilityReport? CompatibilityReport { get; init; }
    public DescriptorLifecycleGovernanceReport? GovernanceReport { get; init; }

    public DescriptorPackageBuildOptions Options { get; init; } = new();
}

public sealed record DescriptorPackageBuildOptions
{
    public string FormatVersion { get; init; } = "1.0";
}
```

### Builder Behavior

**Allowed:**
- Sort descriptors → compute `ContractHash` / `DefinitionHash` via `DescriptorHashComputer`
- Build `DescriptorManifest` with sorted entries and `ContentHash`
- Build `DescriptorSnapshot` with deterministic `SnapshotId`
- Build `DescriptorPackageEvidence` from supplied reports
- Copy relationship facts from `TopologySnapshot.Edges` into `DescriptorPackageRelationshipEntry[]`
- Run self-consistency diagnostics

**Forbidden:**
- Rebuild topology
- Reanalyze impact
- Rerun compatibility rules
- Reevaluate lifecycle governance
- Mutate registries
- Activate descriptors
- Persist approvals

---

## 5. Hash Rules

### 5.1 AoT Safety Note

The existing `DescriptorHashComputer.ComputeContractHash()` serializes anonymous-type projections via `JsonSerializer.Serialize(contractFields, ...)` — this uses runtime type metadata and is **not AoT-safe**. Similarly, `ComputeDefinitionHash()` uses `descriptor.GetType()` — also not AoT-safe.

Both `ContractHash` and `DefinitionHash` are stored in `DescriptorManifestEntry` for **informational/review purposes only**. They are NOT used in `ContentHash` computation.

### 5.2 Hash Hierarchy

6f introduces a new `DescriptorPackageHashComputer` for `ContentHash` and `EvidenceHash` computation. It uses deterministic string concatenation (explicit field ordering, UTF-8 encoding, SHA-256) — no `JsonSerializer.Serialize`, no anonymous objects, no `descriptor.GetType()`, no runtime reflection.

| Hash | Computed by | Contents | Affected by CreatedAt? |
|------|-------------|----------|------------------------|
| `ContractHash` (per descriptor) | `DescriptorHashComputer` (legacy, not AoT-safe) | Informational only; not used in 6f identity | No |
| `DefinitionHash` (per descriptor) | `DescriptorHashComputer` (legacy, not AoT-safe) | Informational only; not used in 6f identity | No |
| `EvidenceHash` | `DescriptorPackageHashComputer` (NEW, AoT-safe) | Deterministic string concat of normalized evidence summary fields | No |
| `ContentHash` | `DescriptorPackageHashComputer` (NEW, AoT-safe) | Deterministic string concat of: format version + sorted descriptor refs (Ns:Id:Version:Kind:State) + sorted relationship entries + EvidenceHash | **No** |
| `EnvelopeHash` | `DescriptorPackageHashComputer` (NEW, AoT-safe) | ContentHash + PackageId + PackageVersion + CreatedAt + CreatedBy + Source | **Yes** |

### 5.3 ContentHash Algorithm

```
ContentHash = SHA256(
    FormatVersion
    + "|" + sortedDescriptorRefs (each: "Ns:Id:Version:Kind:State", joined by "||")
    + "|" + sortedRelationshipEntries (each: "FromNs:FromId:FromVersion→ToNs:ToId:ToVersion:Kind:Strength", joined by "||")
    + "|" + EvidenceHash
)
```

Sorting rules:
- Descriptor refs: `Namespace`, `Id`, `Version` ascending
- Relationship entries: `From.Namespace`, `From.Id`, `From.Version`, `To.Namespace`, `To.Id`, `To.Version`

### 5.4 Invariants

- Same descriptor inventory + same evidence → same `ContentHash`, regardless of input order or `CreatedAt`.
- Different `CreatedAt` → different `EnvelopeHash`, same `ContentHash`.
- `ContentHash` does NOT depend on `ContractHash`, `DefinitionHash`, or any runtime-type serialization.
- `EvidenceHash` is computed from the same normalized evidence summary fields that populate `DescriptorPackageEvidence`.

---

## 6. Relationship Facts

If `request.TopologySnapshot` is provided, copy `TopologySnapshot.Edges` into `DescriptorPackageRelationshipEntry[]`. Preserve all fields: `From`, `To`, `Kind`, `Role`, `SourcePath`, `Strength`, `IsRuntimeBinding`.

If `TopologySnapshot` is null, produce `PACKAGE_TOPOLOGY_NOT_PROVIDED` info-level diagnostic and emit empty relationships list. Do **not** build topology internally.

---

## 7. Package Diagnostics (Self-Consistency Only)

| Code | Severity | Trigger |
|------|----------|---------|
| `PACKAGE_DUPLICATE_DESCRIPTOR_REF` | Error | Same (Namespace, Id, Version) appears twice in inventory |
| `PACKAGE_DESCRIPTOR_HASH_MISMATCH` | Error | Stored hash ≠ recomputed hash for a descriptor |
| `PACKAGE_MANIFEST_REF_MISMATCH` | Error | Manifest entry refs ≠ snapshot descriptor refs |
| `PACKAGE_EVIDENCE_SUBJECT_OUTSIDE_INVENTORY` | Warning | Evidence finding references unknown descriptor |
| `PACKAGE_TOPOLOGY_NODE_OUTSIDE_PACKAGE` | Warning | Topology node references descriptor not in package |
| `PACKAGE_TOPOLOGY_EDGE_OUTSIDE_PACKAGE` | Warning | Topology edge endpoint not in package |
| `PACKAGE_IMPACT_CHANGE_OUTSIDE_PACKAGE` | Warning | Impact change ref not in package |
| `PACKAGE_COMPATIBILITY_SUBJECT_OUTSIDE_PACKAGE` | Warning | Compatibility finding subject not in package |
| `PACKAGE_LIFECYCLE_TRANSITION_OUTSIDE_INVENTORY` | Warning | Lifecycle transition subject not in package |
| `PACKAGE_HASH_MISMATCH` | Error | ContentHash recomputation ≠ stored value |
| `PACKAGE_FORMAT_UNSUPPORTED` | Error | Unknown FormatVersion |
| `PACKAGE_TOPOLOGY_NOT_PROVIDED` | Info | No topology snapshot supplied |

Forbidden: diagnostics that imply semantic re-analysis (new topology/impact/compatibility/lifecycle decisions).

---

## 8. Package Diff (Shallow)

```csharp
public interface IDescriptorPackageDiffer
{
    DescriptorPackageDiff Diff(
        DescriptorPackage before,
        DescriptorPackage after,
        DescriptorPackageDiffOptions? options = null);
}

public sealed record DescriptorPackageDiff
{
    public required IReadOnlyList<DescriptorRef> AddedRefs { get; init; }
    public required IReadOnlyList<DescriptorRef> RemovedRefs { get; init; }
    public required IReadOnlyList<DescriptorDiffEntry> ChangedEntries { get; init; }
    public required IReadOnlyList<DescriptorStateChange> StateChanges { get; init; }
    public required IReadOnlyList<DescriptorPackageMetadataChange> MetadataChanges { get; init; }
    public string BeforeContentHash { get; init; } = string.Empty;
    public string AfterContentHash { get; init; } = string.Empty;
}

public sealed record DescriptorDiffEntry
{
    public required DescriptorRef Ref { get; init; }
    public string BeforeContractHash { get; init; } = string.Empty;
    public string AfterContractHash { get; init; } = string.Empty;
}

public sealed record DescriptorStateChange
{
    public required DescriptorRef Ref { get; init; }
    public DescriptorState FromState { get; init; }
    public DescriptorState ToState { get; init; }
}

public sealed record DescriptorPackageMetadataChange
{
    public required string Field { get; init; }
    public string? BeforeValue { get; init; }
    public string? AfterValue { get; init; }
}

public sealed record DescriptorPackageDiffOptions
{
    // Reserved for future use
}
```

**Constraints:**
- Diff is shallow: added refs, removed refs, changed hashes, changed states, metadata changes.
- `MetadataChanges` covers package-level fields only: `PackageVersion`, `Name`, `Source`. It does NOT cover descriptor-level changes (those go into `ChangedEntries` or `StateChanges`).
- Diff does **not** perform impact traversal, compatibility classification, or lifecycle governance.
- Diff can produce change information that feeds Phase 6c/6d, but analysis remains owned by those phases.

---

## 9. Serializer

Source-generated JSON context for AoT safety. Serializer round-trips the package **metadata/envelope**: manifest, snapshot refs, relationship entries, evidence summaries, and diagnostics. It does NOT serialize or deserialize full descriptor payload (`IDescriptor` objects).

```csharp
public interface IDescriptorPackageSerializer
{
    string Serialize(DescriptorPackage package);
    DescriptorPackage Deserialize(string content);
}
```

**What is serialized:**
- `DescriptorManifest` (identity, format version, descriptor entries with refs/hashes, hashes)
- `DescriptorSnapshot` (refs only — `SnapshotEntry` list; no `IDescriptor` payload)
- `DescriptorPackageRelationshipEntry[]` (from topology edges)
- `DescriptorPackageEvidence` (summary counts, normalized findings)
- `DescriptorPackageDiagnostic[]` (self-consistency diagnostics)

**What is NOT serialized:**
- Live `IDescriptor` objects (no descriptor payload in serialized form)
- Runtime registries or providers
- The builder's internal state

**Boundary rule:** 6f package is a control-plane metadata package. After deserialization, you can verify manifest identity, content hash, evidence summaries, and diagnostics — but you cannot validate `PACKAGE_DESCRIPTOR_HASH_MISMATCH` (that requires live descriptor payload, which is only available at BUILD time). Full descriptor import/export with payload belongs to a later provider/import phase.

Add `DescriptorPackage` (upgraded), `DescriptorManifest` (upgraded), `DescriptorSnapshot` (upgraded), `DescriptorPackageEvidence`, `DescriptorPackageRelationshipEntry`, `DescriptorPackageDiagnostic`, `DescriptorPackageDiff` to the existing `CrestCreatesMetadataJsonContext`.

Update existing `DescriptorManifestSerializer` to handle the upgraded `DescriptorManifest` type.

---

## 10. DI Registration

```csharp
public static IServiceCollection AddDescriptorPackaging(
    this IServiceCollection services)
{
    services.TryAddSingleton<IDescriptorPackageBuilder,
        DefaultDescriptorPackageBuilder>();
    services.TryAddSingleton<IDescriptorPackageDiffer,
        DescriptorPackageDiffer>();
    services.TryAddSingleton<IDescriptorPackageSerializer,
        DescriptorPackageSerializer>();
    return services;
}
```

Added to `MetadataServiceCollectionExtensions`. All registrations `TryAddSingleton`. Builder/differ/serializer are stateless; no scoped dependencies, no registries.

---

## 11. Legacy Compatibility

### DescriptorSnapshotBuilder.TakeSnapshot()

Mark as `[Obsolete("Use IDescriptorPackageBuilder.Build() instead. This static method reads from IGlobalDescriptorRegistry and does not produce deterministic snapshots.")]`.

Do **not** attempt to delegate to `IDescriptorPackageBuilder` from this static method — there is no DI injection point, and service-locating or new-ing a default builder would break the stateless DI main chain. Keep the old implementation as-is for backward compatibility but remove from the new main path.

### Per-Kind Manifest Lists (Breaking Change)

`DescriptorManifest.Schemas`, `.Capabilities`, `.Events`, `.Workflows`, `.Forms`, `.HumanTasks` are **removed** in 6f. This is an intentional breaking change:

- These per-kind lists are replaced by the flat `DescriptorEntries` list.
- The old per-kind grouping was structural but added no behavioral value beyond what `DescriptorManifestEntry.Kind` already provides.
- Any consumer that needs per-kind grouping can filter `DescriptorEntries` by `Kind`.
- No `[Obsolete]` compatibility properties are provided — 6f is the cutover point. This is consistent with the "唯一主链" (single main chain) principle.

### Existing Tests

`DescriptorManifestTests` and `DescriptorSnapshotTests` must be updated to validate new deterministic behavior (ContentHash, flat entries, no per-kind lists) instead of old random-snapshot behavior.

---

## 12. Project Structure

### 12.1 Modified Abstractions

```
framework/src/CrestCreates.Metadata.Abstractions/
  DescriptorPackage.cs              ← UPGRADE
  DescriptorManifest.cs             ← UPGRADE
  DescriptorSnapshot.cs             ← UPGRADE
```

### 12.2 New Abstractions

```
framework/src/CrestCreates.Metadata.Abstractions/
  DescriptorPackageRelationshipEntry.cs
  DescriptorPackageEvidence.cs
  EvidenceFinding.cs
  EvidenceFindingCount.cs
  DescriptorPackageDiagnostic.cs
  DescriptorPackageDiagnosticCode.cs
  IDescriptorPackageBuilder.cs
  DescriptorPackageBuildRequest.cs
  DescriptorPackageBuildOptions.cs
  IDescriptorPackageDiffer.cs
  DescriptorPackageDiff.cs
  DescriptorPackageMetadataChange.cs
  DescriptorPackageDiffOptions.cs
  IDescriptorPackageSerializer.cs
```

### 12.3 New Implementation

```
framework/src/CrestCreates.Metadata/
  DefaultDescriptorPackageBuilder.cs
  DescriptorPackageHashComputer.cs
  DescriptorPackageDiffer.cs
  DescriptorPackageSerializer.cs
```

### 12.4 Modified Implementation

```
framework/src/CrestCreates.Metadata/
  MetadataServiceCollectionExtensions.cs   ← Add AddDescriptorPackaging()
  CrestCreatesMetadataJsonContext.cs       ← Add new types
  DescriptorManifestSerializer.cs          ← Evolve for upgraded manifest
  DescriptorSnapshotBuilder.cs             ← Legacy compatibility wrapper
```

### 12.5 Tests

```
framework/test/CrestCreates.Metadata.Tests/
  DescriptorPackageBuilderTests.cs
  DescriptorPackageEvidenceTests.cs
  DescriptorPackageDiagnosticsTests.cs
  DescriptorPackageDiffTests.cs
  DescriptorPackageSerializerTests.cs
  DescriptorManifestTests.cs              ← MODIFY
  DescriptorSnapshotTests.cs              ← MODIFY
```

---

## 13. Test Plan

### 13.1 Builder — Determinism

| # | Test |
|---|------|
| 1 | `Build_SameInput_ProducesSameContentHash` |
| 2 | `Build_SameContentDifferentInputOrder_SameContentHash` |
| 3 | `Build_DifferentCreatedAt_DoesNotChangeContentHash` |
| 4 | `Build_ChangedDescriptorRef_ChangesContentHash` |
| 5 | `Build_ProducesDeterministicSnapshotId_NoGuid` |
| 6 | `Build_SnapshotId_DerivedFromContentHash` |
| 7 | `Build_ContentHash_DoesNotDependOnContractHash` |
| 8 | `HashComputer_NoJsonSerialization_NoRuntimeReflection` |

### 13.2 Builder — Evidence

| # | Test |
|---|------|
| 9 | `Build_CapturesEvidenceSummary_FromImpactReport` |
| 10 | `Build_CapturesEvidenceSummary_FromCompatibilityReport` |
| 11 | `Build_CapturesEvidenceSummary_FromLifecycleReport` |
| 12 | `Build_CapturesEvidenceSummary_FromTopologySnapshot` |
| 13 | `Build_WithoutReports_ProducesEmptyEvidence` |
| 14 | `Build_DoesNotRerunTopology_WhenTopologyMissing` |
| 15 | `Build_DoesNotRerunAnalysis` |

### 13.3 Builder — Relationship Facts

| # | Test |
|---|------|
| 16 | `Build_CapturesTopologyRelationshipFacts_WithSourcePath` |
| 17 | `Build_WithoutTopology_EmitsTopologyNotProvidedDiagnostic` |

### 13.4 Builder — Diagnostics

| # | Test |
|---|------|
| 18 | `Build_DuplicateDescriptorRefs_EmitsPackageDiagnostic` |
| 19 | `Build_EvidenceSubjectOutsideInventory_EmitsPackageDiagnostic` |
| 20 | `Build_ReportsTopologyEdgeOutsidePackage` |
| 21 | `Build_ReportsImpactChangeOutsidePackage` |
| 22 | `Build_ReportsCompatibilitySubjectOutsidePackage` |
| 23 | `Build_ReportsLifecycleSubjectOutsidePackage` |
| 24 | `Build_ManifestRefMismatch_EmitsPackageDiagnostic` |

### 13.5 Diff

| # | Test |
|---|------|
| 25 | `Diff_ChangedDescriptorHash_ProducesChangedEntry` |
| 26 | `Diff_AddedRef_ProducesAddedEntry` |
| 27 | `Diff_RemovedRef_ProducesRemovedEntry` |
| 28 | `Diff_StateChange_ProducesStateChangeEntry` |
| 29 | `Diff_MetadataChange_ProducesMetadataChange` |
| 30 | `Diff_IdenticalPackages_ProducesEmptyDiff` |
| 31 | `Diff_DoesNotRunImpactOrCompatibilityAnalysis` |
| 32 | `Diff_MetadataChanges_UsesStrongTypedRecords` |

### 13.6 Serializer

| # | Test |
|---|------|
| 33 | `Serializer_RoundTripsManifest` |
| 34 | `Serializer_RoundTripsPackageData_MetadataOnly` |
| 35 | `Serializer_RoundTripsPackageWithEvidence` |
| 36 | `Serializer_RoundTripsPackageWithDiagnostics` |
| 37 | `Serializer_RoundTripsPackageDiff` |
| 38 | `Serializer_DeserializedPackage_CannotRecomputeDescriptorHashes` |

### 13.7 DI

| # | Test |
|---|------|
| 39 | `DI_AddDescriptorPackaging_RegistersBuilder` |
| 40 | `DI_AddDescriptorPackaging_RegistersDiffer` |
| 41 | `DI_AddDescriptorPackaging_RegistersSerializer` |

### 13.8 Legacy

| # | Test |
|---|------|
| 42 | `LegacyDescriptorSnapshotBuilder_IsObsolete_DoesNotParticipateInNewMainPath` |

---

## 14. Completion Criteria

Phase 6f is complete when:

1. `IDescriptorPackageBuilder.Build()` returns a deterministic `DescriptorPackage` from explicit inventory and optional precomputed 6b/6c/6d/6e reports.
2. Package includes: manifest identity/descriptor hash entries, deterministic content hash computed by `DescriptorPackageHashComputer` from stable refs/states/relationships/EvidenceHash (not from `ContractHash` or `DefinitionHash`), deterministic snapshot ID, relationship facts with SourcePath, evidence summary, package self-consistency diagnostics.
3. Hash hierarchy is correct: `ContentHash` is computed by `DescriptorPackageHashComputer` using deterministic string concatenation over stable refs/relationships/evidence fields — no `JsonSerializer.Serialize`, no anonymous objects, no runtime reflection. Same input produces same hash; `CreatedAt` does not affect `ContentHash`. `ContractHash` and `DefinitionHash` (from legacy `DescriptorHashComputer`) are stored for informational purposes only; not used in 6f identity.
4. `IDescriptorPackageDiffer` produces shallow structural diff (added/removed changed refs/states, strong-typed metadata changes).
5. `IDescriptorPackageSerializer` round-trips package metadata/envelope (manifest, refs, evidence, diagnostics) via source-generated JSON; does NOT serialize descriptor payload.
6. Builder/differ/serializer are stateless, AoT-friendly, do not mutate registries or rerun prior analyzers.
7. `AddDescriptorPackaging()` DI registration exists with TryAddSingleton.
8. `DescriptorSnapshotBuilder.TakeSnapshot()` is marked `[Obsolete]`; static method does NOT delegate to DI builder.
9. Per-kind manifest lists (`Schemas`, `Capabilities`, …) are removed; consumers filter flat `DescriptorEntries` by `Kind`.
10. Existing `DescriptorManifestTests` and `DescriptorSnapshotTests` are updated for new deterministic behavior and flat entry model.
11. Full build has zero errors.
12. All 42 tests pass.

---

## 15. Reference: Phase Dependency Chain

```
Phase 5h: Runtime Binding Status
Phase 6a: Descriptor Relationship Coverage
Phase 6b: Descriptor Topology Read Model
Phase 6c: Impact Analysis Engine
Phase 6d: Compatibility / Breaking Change Analyzer
Phase 6e: Descriptor Lifecycle Governance
Phase 6f: Descriptor Package / Manifest / Snapshot  ← THIS PHASE
```

---

*Spec synthesized from Issue #11 comment thread, existing codebase audit, and design review. All decisions approved.*
