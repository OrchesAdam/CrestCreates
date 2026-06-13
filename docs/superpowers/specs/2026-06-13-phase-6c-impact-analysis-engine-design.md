# Phase 6c — Impact Analysis Engine: Design Spec

> **Date:** 2026-06-13 | **Status:** Draft | **Phase 6c**

---

## 1. Overview

### 1.1 Goal

Phase 6c answers:

> Given descriptor changes, which descriptors are affected, through which relationship paths, and what runtime areas may need attention?

### 1.2 Design Principles

1. **Consume Phase 6b, don't re-parse** — `DescriptorTopologySnapshot` is the only input topology. No descriptor field re-parsing, no new relationship extraction path.
2. **Impact flows upstream** — traverse incoming edges (consumers/dependents of changed descriptors).
3. **Structural severity only** — descriptor-kind-specific breaking compatibility belongs to Phase 6d.
4. **Build-once, stateless** — analyzer is a pure function over `(topology, changeSet, options)`.
5. **Deterministic** — unpinned version resolution must not depend on inventory ordering.
6. **AoT-friendly** — all types are records/enums; no runtime reflection, no dynamic dispatch.

---

## 2. Architecture & Data Flow

```
DescriptorTopologySnapshot (Phase 6b)
        +
DescriptorChangeSet (changed descriptors)
        ↓
IDescriptorImpactAnalyzer.Analyze(topology, changeSet, options)
        ↓
   ┌─ Traversal (BFS, incoming edges → upstream impact)
   │    ├─ First-hop: table severity
   │    ├─ Deeper (2+ hops): one level attenuated
   │    ├─ RuntimeBinding boost: per-terminal-segment (cap High)
   │    ├─ Advisory edges: skipped when IncludeAdvisoryRelationships=false
   │    └─ Unpinned fan-out: IMPACT_AMBIGUOUS_UNPINNED_TARGET
   │
   ├─ Diagnostics along path
   │    ├─ Topology diagnostics encountered: re-mapped to IMPACT_TOPOLOGY_*
   │    └─ Impact-native: IMPACT_*
   │
   └─ Assembly
        ↓
DescriptorImpactAnalysisReport
  ├─ ChangeSet
  ├─ AffectedDescriptors (deduped, with Paths per version branch)
  ├─ Paths (all version branches preserved)
  ├─ MaxSeverity
  └─ Diagnostics
```

### 2.1 Component Separation

| Component | Role | Location |
|---|---|---|
| `IDescriptorImpactAnalyzer` | Orchestrates traversal + severity + diagnostics + assembly | `CrestCreates.Metadata` |
| `IDescriptorChangeSetBuilder` | Builds `DescriptorChangeSet` from before/after inventories | `CrestCreates.Metadata` |
| Core types | `DescriptorChangeSet`, `DescriptorImpactAnalysisReport`, enums, records | `CrestCreates.Metadata.Abstractions/DescriptorImpact/` |
| `DescriptorTopologySnapshot` | Phase 6b — consumed as-is via `Nodes`/`Edges`; analyzer builds internal lookup + fan-out-aware incoming index (NOT using `DescriptorNode.IncomingEdgeIndices`) | Existing |

### 2.2 Boundary Rules

- Impact flows **upstream**: traverse **incoming** edges (consumers/dependents of changed descriptors).
- Use topology edges as the **only** path source; no descriptor field re-parsing.
- **Do NOT use `DescriptorNode.IncomingEdgeIndices` for traversal** — Phase 6b's unpinned edge resolution uses `FirstOrDefault`, which is not fan-out-safe. The analyzer must build its own `_impactIncomingIndex` that fans out unpinned edges to ALL matching versioned nodes (see §5.1).
- Severity is **structural only** — descriptor-kind-specific compatibility rules belong to Phase 6d.
- Changed descriptor not in topology → empty report (no reverse unpinned lookup for Added descriptors).
- Legacy `DescriptorCatalog.AnalyzeImpact()` is **not** the implementation path.

---

## 3. Core Types

All types under `CrestCreates.Metadata.Abstractions.DescriptorImpact/`. All are records, enums, or interfaces — stateless, AoT-friendly, no runtime binding.

### 3.1 Enums

```csharp
public enum DescriptorChangeKind
{
    Added,
    Updated,
    Deprecated,
    Removed,
    Activated,
    StateChanged,
    ContractHashChanged
}

public enum DescriptorImpactSeverity
{
    None,       // No consumers found
    Info,       // Added/Activated, or too deep to matter
    Low,        // Weak advisory path, StateChanged via metadata path
    Medium,     // Updated/ContractHashChanged via weak path, Deprecated via advisory
    High,       // Removed via runtime path, Updated via strong path, runtime binding boost
    Critical    // Removed via strong runtime path (hard break)
}

public enum DescriptorImpactRuntimeArea
{
    Metadata,
    Schema,
    Form,
    Capability,
    Event,
    Workflow,
    HumanTask,
    RuntimeBinding      // Any path with IsRuntimeBinding=true edge
}
```

### 3.2 Change Set Types

```csharp
public sealed record DescriptorChange
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorChangeKind Kind { get; init; }
    public DescriptorState? BeforeState { get; init; }
    public DescriptorState? AfterState { get; init; }
    public string? BeforeContractHash { get; init; }
    public string? AfterContractHash { get; init; }
    public string? Reason { get; init; }
}

public sealed record DescriptorChangeSet
{
    public required IReadOnlyList<DescriptorChange> Changes { get; init; }
}
```

### 3.3 Impact Path Types

```csharp
public sealed record DescriptorImpactPathSegment
{
    public required DescriptorRef From { get; init; }
    public required DescriptorRef To { get; init; }
    public required RelationshipKind Kind { get; init; }
    public required RelationshipStrength Strength { get; init; }
    public required bool IsRuntimeBinding { get; init; }
    public string? Role { get; init; }
    public string? SourcePath { get; init; }
}

public sealed record DescriptorImpactPath
{
    public required DescriptorRef SourceChange { get; init; }
    public required DescriptorRef Affected { get; init; }
    public required IReadOnlyList<DescriptorImpactPathSegment> Segments { get; init; }
}
```

### 3.4 Report Types

```csharp
public sealed record AffectedDescriptor
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorImpactSeverity Severity { get; init; }
    public required IReadOnlyList<DescriptorImpactRuntimeArea> RuntimeAreas { get; init; }
    public required IReadOnlyList<DescriptorImpactPath> Paths { get; init; }
    public string? Reason { get; init; }
    public string? SuggestedAction { get; init; }
}

public sealed record DescriptorImpactDiagnostic(
    DiagnosticSeverity Severity,
    string Code,              // IMPACT_TOPOLOGY_* or IMPACT_*
    string Message,
    DescriptorRef? Subject,
    IReadOnlyList<DescriptorRef>? RelatedRefs);

public sealed record DescriptorImpactAnalysisReport
{
    public required DescriptorChangeSet ChangeSet { get; init; }
    public required IReadOnlyList<AffectedDescriptor> AffectedDescriptors { get; init; }
    public required IReadOnlyList<DescriptorImpactPath> Paths { get; init; }
    public required DescriptorImpactSeverity MaxSeverity { get; init; }
    public required IReadOnlyList<DescriptorImpactDiagnostic> Diagnostics { get; init; }
}
```

### 3.5 Options & Interfaces

```csharp
public sealed record DescriptorImpactAnalysisOptions
{
    public bool IncludeWeakRelationships { get; init; } = true;
    public bool IncludeAdvisoryRelationships { get; init; } = true;
    public int? MaxDepth { get; init; }
}

public interface IDescriptorImpactAnalyzer
{
    DescriptorImpactAnalysisReport Analyze(
        DescriptorTopologySnapshot topology,
        DescriptorChangeSet changeSet,
        DescriptorImpactAnalysisOptions? options = null);
}

public interface IDescriptorChangeSetBuilder
{
    DescriptorChangeSet Build(
        IReadOnlyList<IDescriptor> before,
        IReadOnlyList<IDescriptor> after);
}
```

**Type count:** 3 enums, 10 records, 2 interfaces. `DescriptorImpactDiagnostic` reuses the existing `DiagnosticSeverity` enum from `CrestCreates.Metadata.Abstractions.DescriptorTopology`.

---

## 4. Severity Model

### 4.1 Base Severity Table

| Change Kind | Strong Runtime Path | Strong Descriptor Path | Weak Path |
|---|---|---|---|
| Removed | Critical | High | Medium |
| Deprecated | High | Medium | Low |
| Updated / ContractHashChanged | High | Medium | Low |
| StateChanged | Medium | Low | Info |
| Activated / Added | Info | Info | Info |

### 4.2 Path Classification Rules

```
Edge is on Strong path if:  edge.Strength == Strong
Edge is on Weak path   if:  edge.Strength == Weak

Edge is Runtime  if:  IsRuntimeBinding == true
Edge is Descriptor if: IsRuntimeBinding == false (but NOT advisory)
```

**Mapping to table columns:**
- `edge.Strength == Strong && edge.IsRuntimeBinding == true`  → "Strong Runtime"
- `edge.Strength == Strong && edge.IsRuntimeBinding == false` → "Strong Descriptor" (non-advisory)
- `edge.Strength == Weak` → "Weak" (regardless of IsRuntimeBinding)

### 4.3 Severity Modifiers (applied in order)

**Modifier 1 — Transitive attenuation:**
- First-hop consumers → table severity as-is.
- 2+ hops → reduce by one level (`Critical→High`, `High→Medium`, `Medium→Low`, `Low→Info`, `Info` stays `Info`).

**Modifier 2 — Runtime binding boost (per-terminal-segment):**
- If the **terminal segment** of the path (the edge that connects to the affected descriptor) has `IsRuntimeBinding == true`: increase severity by one level, ceiling at `High`.
- Applied AFTER attenuation. A once-attenuated `Medium` with a runtime-binding terminal segment becomes `High`.
- Earlier segments' `IsRuntimeBinding` do NOT boost severity for downstream nodes — the boost is local to the edge that created the dependency.

**Modifier 2.5 — RuntimeBinding area (path-wide):**
- Separately, if ANY segment in the path has `IsRuntimeBinding == true`, add `RuntimeBinding` to the `AffectedDescriptor.RuntimeAreas`. This is independent of the severity boost.

**Modifier 3 — Advisory edge exclusion:**
- When `IncludeAdvisoryRelationships == false`, advisory edges are skipped during traversal.
- An `AffectedDescriptor` reachable ONLY through advisory edges is NOT included in the report.

### 4.4 Advisory Edge Definition

```csharp
static bool IsAdvisory(DescriptorEdge edge)
{
    if (edge.IsRuntimeBinding)
        return false;  // Runtime edges are never advisory

    return edge.Strength == RelationshipStrength.Weak
        && (edge.Kind == RelationshipKind.References
            || edge.Kind == RelationshipKind.DependsOn
            || edge.Role == RelationshipRoles.SupersededBy
            || edge.Role == RelationshipRoles.SubWorkflowStep);
}
```

Advisory examples: `SupersededBy`, `SubWorkflowStep` while unsupported, `Weak References`, `Weak DependsOn`.

Non-advisory examples (not filtered even though `IsRuntimeBinding == false`): `Form → Schema`, `Capability → InputSchema`, `Capability → OutputSchema`, `Event → PayloadSchema`, `Workflow → VariableSchema`. These are strong runtime contracts or structural contracts.

### 4.5 Per-AffectedDescriptor Severity

An `AffectedDescriptor` may be reachable through multiple paths (especially after fan-out). The severity assigned to the descriptor is the **highest** severity across all its paths.

### 4.6 None Severity

`None` is used only when a changed descriptor has **zero** consumers in the topology (no incoming edges). This is NOT an error — it signals "no impact detected."

---

## 5. Traversal Algorithm

### 5.1 Internal Lookup Construction

At the start of `Analyze()`, the analyzer builds deterministic lookup indices from `topology.Nodes` and `topology.Edges`. This is necessary because `DescriptorTopologySnapshot.TryResolveRef` is **private** — the analyzer cannot call it. No modification to 6b types is needed.

Three indices are constructed:

```
// Index 1: Exact ref → DescriptorNode (keyed by full DescriptorRef value equality)
_exactIndex: Dictionary<DescriptorRef, DescriptorNode>

// Index 2: Identity → all versioned nodes sharing (Namespace, Id)
_identityIndex: Dictionary<DescriptorIdentity, List<DescriptorNode>>

// Index 3: Resolved target/current node → incoming edges (fan-out aware)
_impactIncomingIndex: Dictionary<DescriptorRef, List<DescriptorEdge>>
```

`_exactIndex` is populated from `topology.Nodes` directly (topology nodes already have exact-version `DescriptorRef` keys).

`_identityIndex` is populated by grouping: for each `(node.Ref.Namespace, node.Ref.Id)`, collect all `DescriptorNode` with matching identity.

**`_impactIncomingIndex` is critical and must NOT use `DescriptorNode.IncomingEdgeIndices`.** Phase 6b's builder resolves unpinned edges with `FirstOrDefault` — an unpinned edge `To = schema.User@null` is written to only ONE versioned node's incoming index. If the topology has both `schema.User@1` and `schema.User@2`, the edge only appears on one of them, making analysis inventory-order dependent.

Instead, `_impactIncomingIndex` is built by iterating `topology.Edges` and fanning out:

- For each edge in `topology.Edges`:
  - If `edge.To.Version != null`: add the edge to `_impactIncomingIndex[edge.To]` only.
  - If `edge.To.Version == null`: look up `_identityIndex[(edge.To.Namespace, edge.To.Id)]`. Add the edge to `_impactIncomingIndex[v.ResolvedNodeRef]` for **every** matching versioned node in the list.
  - If `edge.To.Version == null` and zero matching nodes in `_identityIndex`: do NOT add to `_impactIncomingIndex`. Record as an unresolved target for impact diagnostic purposes only if the target identity matches a changed descriptor ref; otherwise ignore.
  - `edge.From` is NOT resolved here — consumer-side resolution happens during BFS (see §5.7).

This ensures deterministic fan-out: an unpinned consumer edge appears in the incoming list of ALL versioned target nodes it could bind to.

### 5.2 Version-Aware Node Resolution

Internal method `ResolveRef(ref)`:

1. Try `_exactIndex[ref]` → one node. Return.
2. If `ref.Version == null`: try `_identityIndex[(ref.Namespace, ref.Id)]` → list of matching nodes. Return all (for fan-out) or empty (for unresolved).
3. If `ref.Version != null` and not in `_exactIndex`: no match.

### 5.3 Entry Points

For each `DescriptorChange` in the `DescriptorChangeSet`:

1. Resolve the changed descriptor's node via `ResolveRef(change.Ref)`.
2. If **not found** → skip (Added/Activated of new descriptor; no consumers; no reverse unpinned lookup).
3. If **found** → this is the **origin node**. Begin BFS following **incoming edges** (upstream = who depends on me). Incoming edges are resolved via `_impactIncomingIndex[originNode.Ref]` (the analyzer-local fan-out index, NOT `DescriptorNode.IncomingEdgeIndices`).

### 5.4 BFS State

Each BFS wave carries:

```
(currentNode, depth, pathSoFar, hasRuntimeBindingAlongPath)
```

- `depth` = 1-based hop count from origin. Origin → depth 1 = direct consumers.
- `pathSoFar` = accumulated `DescriptorImpactPathSegment` list.
- `hasRuntimeBindingAlongPath` = any segment so far has `IsRuntimeBinding == true` (used only for `RuntimeBinding` area on `AffectedDescriptor`, NOT for severity boost — severity boost uses terminal segment only per Modifier 2).

### 5.5 Edge Filtering

For each edge in `_impactIncomingIndex[currentNode.Ref]` (analyzer-local fan-out index), before traversing:

```
1. Weak filter (IncludeWeakRelationships):
   If false && edge.Strength == Weak → SKIP.

2. Advisory filter (IncludeAdvisoryRelationships):
   If false && IsAdvisory(edge) → SKIP, emit IMPACT_SKIPPED_WEAK_PATH (Info).
```

### 5.6 Visited Key (Fan-Out Safe)

The visited set key is:

```
(originChangedRef, currentNodeRef, edgeIndex)
```

This prevents:
- **False merge**: two different version branches (`Schema.A@v1 → Form.X` vs `Schema.A@v2 → Form.X`) incorrectly collapsing into one visited entry.
- **Infinite cycles**: a Strong cycle doesn't loop indefinitely because `(origin, current, edge)` tuples don't repeat.

If the visited key is already seen → SKIP.

### 5.7 Node Resolution (Unpinned Fan-Out)

When traversing an edge, resolve `edge.From` (the consumer) via `ResolveRef`: 

**Case A: Exact match** → one consumer node. Normal traversal.

**Case B: Unpinned ref (Version == null), one matching node** → one consumer node. Normal traversal.

**Case C: Unpinned ref (Version == null), multiple matching nodes** — Fan-out:
1. For EACH matching versioned node, create a separate traversal branch.
2. Each branch gets its own path segment (with the versioned `From` ref).
3. Emit `IMPACT_AMBIGUOUS_UNPINNED_TARGET` (Warning) attached to the affected descriptor, with `Subject` = the ambiguous edge's From ref (the unpinned consumer that maps to multiple versions).
4. Continue BFS for all branches.

**Case D: Unpinned ref, zero matching nodes** → emit `IMPACT_UNRESOLVED_CONSUMER` (Warning) with the unresolved ref. Path stops here.

### 5.8 Deduplication Rules

- **Affected descriptors**: dedupe by `consumerNodeRef`. `Form.X` reached from two version branches → one `AffectedDescriptor` with multiple `Paths`.
- **Impact paths**: preserve all version branches. Each fan-out branch = distinct path (semantically different).
- Paths are not deduped by segment contents.

### 5.9 Depth Limit

When `MaxDepth` is set and `depth >= MaxDepth`:
- Record the current consumer as affected (at current depth severity).
- Emit `IMPACT_PATH_TRUNCATED` (Warning) with the truncated node ref.
- Do NOT enqueue further hops from this node.

### 5.10 Assembly: Affected Descriptors

After BFS completes for all changed descriptors:

1. Group all discovered `(consumerNodeRef, path)` pairs by `consumerNodeRef`.
2. For each unique consumer node:
   - Compute severity: `max(severity across all paths)`.
   - Collect all paths (multiple version branches → multiple `DescriptorImpactPath` entries).
   - Collect runtime areas: `KindFromNode` + `RuntimeBinding` if any path has `hasRuntimeBindingAlongPath == true`.
   - Derive `Reason` from the highest-contributing path (e.g., "Removed: Schema.order → Form.checkout via InputSchema").
3. Sort affected descriptors by severity (descending), then by name.

### 5.11 Assembly: Diagnostics

Collect and order:

1. **Topology diagnostics encountered along traversed paths** → re-map as `IMPACT_TOPOLOGY_*` impact diagnostics (only when `Subject` or `RelatedRefs` match nodes/edges along the traversed path).
2. **Impact-native diagnostics** (`IMPACT_*`) emitted during traversal.
3. Sort: Errors first, then Warnings, then Info.

### 5.12 MaxSeverity

The report's `MaxSeverity` = max of all `AffectedDescriptor.Severity` values, or `None` if empty.

---

## 6. Diagnostic Codes Reference

### 6.1 Topology-Derived Codes (`IMPACT_TOPOLOGY_*`)

Re-mapped from `DescriptorTopologySnapshot.Diagnostics` when the diagnostic's `Subject` or `RelatedRefs` match nodes/edges along impact paths:

| Code | Source | Remapping |
|---|---|---|
| `IMPACT_TOPOLOGY_MISSING_TARGET` | `MISSING_TARGET` | Severity preserved (Error/Warning from topology). Attached to the `AffectedDescriptor` whose path edge had the missing target. |
| `IMPACT_TOPOLOGY_STRONG_CYCLE` | `STRONG_CYCLE` | Error. Attached to any `AffectedDescriptor` whose path traverses a cycle participant. |
| `IMPACT_TOPOLOGY_UNSUPPORTED_REFERENCE` | `UNSUPPORTED_REFERENCE` | Warning. Attached when an impact path crosses an unsupported reference edge. |

### 6.2 Impact-Native Codes (`IMPACT_*`)

| Code | Severity | Trigger |
|---|---|---|
| `IMPACT_AMBIGUOUS_UNPINNED_TARGET` | Warning | Edge's `From` ref (consumer) with `Version == null` resolves to 2+ versioned nodes during fan-out. `Subject` = the ambiguous consumer ref. |
| `IMPACT_UNRESOLVED_CONSUMER` | Warning | Edge with `Version == null` resolves to zero nodes. `Subject` = the unresolved consumer ref. |
| `IMPACT_PATH_TRUNCATED` | Warning | BFS stopped at `MaxDepth`. `Subject` = the node where truncation occurred. |
| `IMPACT_SKIPPED_WEAK_PATH` | Info | Advisory edge skipped when `IncludeAdvisoryRelationships == false`. One per skipped edge. |

### 6.3 Runtime Area Derivation Table

| `DescriptorKind` | `DescriptorImpactRuntimeArea` |
|---|---|
| Schema | Schema |
| Form | Form |
| Capability | Capability |
| Event | Event |
| Workflow | Workflow |
| HumanTask | HumanTask |
| Any path with `hasRuntimeBindingAlongPath == true` | Also add `RuntimeBinding` |

`RuntimeArea` is additive: a Workflow consumer whose path contains a `IsRuntimeBinding == true` segment gets both `[Workflow, RuntimeBinding]`.

---

## 7. DescriptorChangeSetBuilder

### 7.1 Comparison Logic

```csharp
IDescriptorChangeSetBuilder.Build(before, after)
```

For each descriptor in `after`:
1. Match to `before` descriptor by exact `DescriptorRef` (`Namespace, Id, Version`).
2. If no match → `Added`.
3. If match found → compare `State`, `ContractHash`, `Name`:
   - `State` changed **to** `Removed` (from any non-Removed) → `Removed`
   - `State` changed **to** `Deprecated` (from Active/Draft) → `Deprecated`
   - `State` changed **to** `Active` (from Draft) → `Activated`
   - `State` changed, none of the above → `StateChanged` (e.g., Active → Draft)
   - `State` unchanged, `ContractHash` changed → `ContractHashChanged`
   - `State` unchanged, `ContractHash` unchanged, but `Name` changed → `Updated`

For each descriptor in `before` but NOT in `after` → `Removed`.

Detection is evaluated top-to-bottom and stops at the first match for each descriptor pair.

### 7.2 Change Kind Deduplication

When multiple changes apply to the same `DescriptorRef`, pick the highest priority:

| Priority | ChangeKind |
|---|---|
| 1 (highest) | Removed |
| 2 | Deprecated |
| 3 | StateChanged |
| 4 | ContractHashChanged |
| 5 | Updated |
| 6 | Added |
| 7 | Activated |

Only one `DescriptorChange` per `DescriptorRef` in the output set.

### 7.3 Non-Goals

- Package storage, manifest persistence, lifecycle policy enforcement → not in 6c.
- Deep content comparison (schema fields, step counts) → Phase 6d.
- Descriptor-specific compatibility rules → Phase 6d.

---

## 8. DI Registration

### 8.1 Extension Method

In `CrestCreates.Metadata` / `MetadataServiceCollectionExtensions`:

```csharp
public static IServiceCollection AddDescriptorImpactAnalysis(
    this IServiceCollection services)
{
    services.TryAddSingleton<IDescriptorImpactAnalyzer, DescriptorImpactAnalyzer>();
    services.TryAddSingleton<IDescriptorChangeSetBuilder, DescriptorChangeSetBuilder>();
    return services;
}
```

### 8.2 Lifetime Rationale

Both services are **stateless** pure functions:
- `DescriptorImpactAnalyzer` — `(topology, changeSet, options)` → report.
- `DescriptorChangeSetBuilder` — `(before, after)` → change set.

No scoped runtime store dependencies. No `ITenantContext`, `ICurrentUser`, `HttpContext`. Singleton-safe.

### 8.3 Registration Boundary

- `IDescriptorImpactAnalyzer` → `TryAddSingleton` (allow override for testing).
- `IDescriptorChangeSetBuilder` → `TryAddSingleton` (allow override).
- Analyzer receives `DescriptorTopologySnapshot` as a **method parameter**, not constructor injection.
- No `IDescriptorTopologyBuilder` or `IDescriptorRelationshipProvider` dependency — all facts are already in the snapshot edges.

---

## 9. Test Plan

All tests in `framework/test/CrestCreates.Metadata.Tests/DescriptorImpact/`.

### 9.1 Analyzer Tests

| Test | What It Proves |
|---|---|
| `DirectStrongConsumer_IsReported` | First-hop Strong consumer appears with table severity |
| `TransitiveConsumer_IsReported_WithAttenuatedSeverity` | 2-hop consumer → one level down |
| `RuntimeBinding_TerminalSegment_BoostsSeverity` | Terminal edge `IsRuntimeBinding=true` → +1, ceiling High |
| `RuntimeBinding_NonTerminalSegment_DoesNotBoostDownstream` | Non-terminal runtime edge → downstream severity unchanged; area `RuntimeBinding` still added |
| `RuntimeBinding_Area_Added_PathWide` | Path with 2+ hops, one runtime → `RuntimeBinding` appears in `RuntimeAreas` even though boost only on terminal |
| `WeakPath_Included_ByDefault` | `IncludeWeakRelationships=true` → Weak consumer visible |
| `WeakPath_Excluded_WhenFalse` | `IncludeWeakRelationships=false` → Weak-only paths missing |
| `AdvisoryPath_Included_ByDefault` | `SupersededBy` → appears with advisory severity |
| `AdvisoryPath_Skipped_WhenFalse_WithDiagnostic` | `IncludeAdvisoryRelationships=false` → `IMPACT_SKIPPED_WEAK_PATH` |
| `UnpinnedConsumer_Included_ForExactChangedVersion` | `Edge.To.Version=null` consumer found for exact version change |
| `UnpinnedRef_Ambiguous_FanOut_WithDiagnostic` | `Version=null` edge → 2+ versioned nodes → fan-out + `IMPACT_AMBIGUOUS_UNPINNED_TARGET` |
| `UnpinnedRef_Unresolved_EmitsDiagnostic` | `Version=null` edge → 0 nodes → `IMPACT_UNRESOLVED_CONSUMER` |
| `ChangedDescriptor_NotInTopology_ReturnsEmpty` | Added descriptor with no node → empty report |
| `MultipleChangeKinds_MultipleAffected_AllReported` | 2 changed descriptors → 3 affected → deduped |
| `Severity_IsMaxAcrossAllPaths` | Consumer reachable via Strong+Weak paths → Strong severity wins |
| `Path_ContainsRole_And_SourcePath` | Path segment carries `Role`/`SourcePath` from `DescriptorEdge` |
| `DepthLimit_Truncates_WithDiagnostic` | `MaxDepth=2` → depth 2 consumer reported, depth 3 truncated |
| `FanOut_PreservesVersionBranchPaths_ButDedupesAffected` | `Schema.A@v1→Form.X` and `Schema.A@v2→Form.X` → 1 `AffectedDescriptor` with 2 Paths |
| `Cycle_DoesNotLoop_Infinite` | Strong cycle → visited key prevents infinite expansion |
| `TopologyDiagnostic_OnPath_ReExported` | `MISSING_TARGET` on impact path → `IMPACT_TOPOLOGY_MISSING_TARGET` in report |
| `TopologyDiagnostic_OffPath_NotExported` | `MISSING_TARGET` unrelated to change set → not in report |

### 9.2 ChangeSetBuilder Tests

| Test | What It Proves |
|---|---|
| `Added_Descriptor_WhenNotInBefore` | Descriptor only in after → Added |
| `Removed_Descriptor_WhenNotInAfter` | Descriptor only in before → Removed |
| `StateChanged_Detected` | Same ref, different State → StateChanged |
| `ContractHashChanged_Detected` | Same ref, same State, different ContractHash → ContractHashChanged |
| `StateChanged_Priority_Over_ContractHashChanged` | Both changed → StateChanged only |
| `Deprecated_StateTransition` | State becomes Deprecated → Deprecated |
| `Removed_StateTransition` | State becomes Removed → Removed |
| `Activated_StateTransition` | State Draft → Active → Activated |
| `Update_StateAndContractUnchanged_OtherFieldsDiffer` | Same ref, same State, same ContractHash → Updated |
| `Ordering_IsPredictionIndependent` | Result does not depend on dictionary enumeration order |

### 9.3 Severity Table Tests

| Test | What It Proves |
|---|---|
| `Removed_StrongRuntime_IsCritical` | Removed, Strong, IsRuntimeBinding → Critical |
| `Removed_StrongDescriptor_IsHigh` | Removed, Strong, non-runtime → High |
| `Removed_Weak_IsMedium` | Removed, Weak → Medium |
| `Deprecated_StrongRuntime_IsHigh` | Deprecated, Strong, IsRuntimeBinding → High |
| `Deprecated_StrongDescriptor_IsMedium` | Deprecated, Strong, non-runtime → Medium |
| `Deprecated_Weak_IsLow` | Deprecated, Weak → Low |
| `Updated_StrongRuntime_IsHigh` | Updated, Strong, IsRuntimeBinding → High |
| `StateChanged_StrongRuntime_IsMedium` | StateChanged, Strong, IsRuntimeBinding → Medium |
| `Activated_AlwaysInfo` | Activated → Info |
| `None_WhenZeroConsumers` | Descriptor with no incoming edges → None |
| `TransitiveAttenuation_Removed_CriticalToHigh` | Removed Strong Runtime at depth 2 → High |
| `TransitiveAttenuation_Deprecated_HighToMedium` | Deprecated Strong Runtime at depth 2 → Medium |

### 9.4 Non-Regression

- Legacy `DescriptorCatalog.AnalyzeImpact()` tests must still pass (adapter path unchanged).
- All 146 existing Metadata.Tests must pass.
- All cross-module test suites (Form, Capability, Event, HumanTask, Workflow) remain at 0 regressions.

---

## 10. Non-Goals (Explicitly Out of Scope)

- Descriptor-kind-specific breaking-change rules (Phase 6d)
- Schema field compatibility analysis (Phase 6d)
- Workflow / Capability input-output contract compatibility (Phase 6d)
- Lifecycle governance or activation policy (Phase 6e)
- Concrete runtime instance lookup (no runtime store scanning)
- Package store / manifest / snapshot persistence (Phase 6f)
- LLM suggestions or planning (Phase 7)
- UI / API / AppService / Dynamic API exposure
- Reverse unpinned lookup for Added descriptors
- Legacy `DescriptorCatalog.AnalyzeImpact()` rewrite — stays as adapter-backed
- `[Obsolete]` on `DependencyEdge` / `DescriptorDependencyKind` / `ImpactReport` — defer to after Phase 6c migration
- `IDescriptorTopologyProvider` — deferred (Phase 6c comment explicitly says "not in 6c")

---

## 11. Completion Criteria

Phase 6c is complete when:

- [ ] `IDescriptorImpactAnalyzer` returns deterministic `DescriptorImpactAnalysisReport` from `(topology, changeSet)`.
- [ ] Reports include: affected descriptors, impact paths with roles/source paths, structural severity, runtime areas, diagnostics.
- [ ] Unpinned version behavior is explicit, fan-out-safe, and not inventory-order dependent.
- [ ] Severity model passes the table + modifiers + attenuation test matrix.
- [ ] Advisory edge filtering works correctly with and without the option.
- [ ] `IDescriptorChangeSetBuilder` correctly diffs descriptor inventories.
- [ ] All new tests pass (~30 tests across 3 categories).
- [ ] Zero regressions on existing 468+ tests across 6 suites.
- [ ] Legacy `DescriptorCatalog.AnalyzeImpact()` tests still pass (adapter unchanged).

---

## 12. Project Structure

```
framework/src/CrestCreates.Metadata.Abstractions/
  DescriptorImpact/                               (new folder, 15 files)
    DescriptorChangeKind.cs
    DescriptorImpactSeverity.cs
    DescriptorImpactRuntimeArea.cs
    DescriptorChange.cs
    DescriptorChangeSet.cs
    DescriptorImpactPathSegment.cs
    DescriptorImpactPath.cs
    AffectedDescriptor.cs
    DescriptorImpactDiagnostic.cs
    DescriptorImpactAnalysisReport.cs
    DescriptorImpactAnalysisOptions.cs
    IDescriptorImpactAnalyzer.cs
    IDescriptorChangeSetBuilder.cs

framework/src/CrestCreates.Metadata/
  DescriptorImpactAnalyzer.cs                     (new)
  DescriptorChangeSetBuilder.cs                   (new)
  MetadataServiceCollectionExtensions.cs          (edit: AddDescriptorImpactAnalysis)

framework/test/CrestCreates.Metadata.Tests/
  DescriptorImpact/                               (new folder, 3 test files)
    DescriptorImpactAnalyzerTests.cs
    DescriptorChangeSetBuilderTests.cs
    DescriptorImpactSeverityTests.cs
```

~17 new files, 1 edited file. No changes to Phase 6a/6b types, no changes to legacy adapter.

---

*This spec incorporates all decisions from issue #8 comment thread. Phase 6d (Compatibility/Breaking Change Analyzer) is the natural next phase.*
