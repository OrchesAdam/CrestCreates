# Phase 6b — Descriptor Topology Read Model Design Spec

**Date**: 2026-06-12
**Status**: In Review
**Parent Issue**: [#7 — Phase 6b: Descriptor Topology Engine](https://github.com/OrchesAdam/CrestCreates/issues/7)

---

## 1. Overview

Phase 6b builds the **unique topology read model** on top of Phase 6a's `DescriptorRelationship` data layer. It projects flat relationship lists into a graph: nodes, edges, consumer index, and diagnostics — all computed at snapshot build time.

Phase 6a answered: "What does descriptor X reference?"  
Phase 6b answers: "What does the entire descriptor graph look like?"

### Design Principles

1. **Single read model** — `IDescriptorTopologyBuilder` is the one and only topology entry point
2. **Explicit inventory** — caller provides the descriptor list; topology does not guess where descriptors come from
3. **Stateless builder** — `Build(descriptors)` is a pure function; no singleton caching, no hidden state
4. **Build-once immutable snapshot** — `Build()` produces a frozen `DescriptorTopologySnapshot`; no incremental mutation
5. **Content-agnostic** — topology reads the provided descriptor list + `IDescriptorRelationshipProvider`; never re-parses descriptor internals
6. **AoT-friendly** — no runtime reflection, no `dynamic`, no assembly scanning
7. **Diagnostics at build time** — errors/warnings computed during `Build()` and frozen into snapshot
8. **Old graph as dead-end compatibility** — `DescriptorCatalog` keeps injecting `IDescriptorDependencyGraph` for backward compat only; new code uses `DescriptorTopologySnapshot` directly

### Scope Boundary

Phase 6b provides the **topology read model**. It does NOT:
- Re-parse descriptor internals
- Do runtime reflection scanning
- Compatibility or breaking-change analysis
- Lifecycle governance
- Package import/export
- LLM draft planning
- `GetAllRelationships()` on extractors (requires registry DI → deferred)
- Change `DescriptorCatalog` (compatibility-only, receives adapter)
- Change `MetadataBootstrapper.BuildAll()`, or any registry
- Create `IDescriptorTopologyProvider` with caching/refresh (deferred to Phase 6c)

---

## 2. Inputs

Phase 6b reads from:

| Input | Source | Provides |
|---|---|---|
| Descriptor inventory | `IReadOnlyList<IDescriptor>` (caller-provided) | All known descriptors (nodes) |
| Relationship Provider | `IDescriptorRelationshipProvider` (DI) | All outgoing relationships per descriptor (edges) |

**Why caller-provided inventory, not `IGlobalDescriptorRegistry`?** Typed registries do not sync to `IGlobalDescriptorRegistry` during `Build()`. Depending on it would produce empty topology or massive `MISSING_TARGET` false positives. The caller — test harness, adapter, or future `IDescriptorTopologyProvider` (Phase 6c) — is responsible for assembling the descriptor list from built typed registries.

Edge semantics from Phase 6a:
- `DescriptorRelationship.From` = source / consumer
- `DescriptorRelationship.To` = target / dependency
- A → B means "A depends on B"

---

## 3. Core Types

### 3.1 DescriptorIdentity

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

/// <summary>
/// Version-independent descriptor identity key.
/// Based on (Namespace, Id) only — Namespace is already unique per descriptor kind.
/// DescriptorRef has no Kind field, so identity is derived from Namespace + Id.
/// </summary>
public readonly record struct DescriptorIdentity(
    string Namespace,
    string Id);
```

### 3.2 DescriptorNode

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

/// <summary>
/// A descriptor as a topology node. Holds identity + precomputed summary properties.
/// Edge references are stored as indices into the snapshot's edge list.
/// </summary>
public sealed record DescriptorNode
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorState State { get; init; }
    public string? ContractHash { get; init; }
    public string? SupersededById { get; init; }

    /// <summary>Indices into DescriptorTopologySnapshot.Edges for outgoing edges (this → target).</summary>
    public required IReadOnlySet<int> OutgoingEdgeIndices { get; init; }

    /// <summary>Indices into DescriptorTopologySnapshot.Edges for incoming edges (consumer → this).</summary>
    public required IReadOnlySet<int> IncomingEdgeIndices { get; init; }
}
```

### 3.3 DescriptorEdge

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

/// <summary>
/// A directed edge in the topology graph.
/// From = consumer/source, To = target/dependency.
/// </summary>
public sealed record DescriptorEdge
{
    public required int Index { get; init; }
    public required DescriptorRef From { get; init; }
    public required DescriptorRef To { get; init; }
    public required RelationshipKind Kind { get; init; }
    public string? Role { get; init; }
    public string? SourcePath { get; init; }
    public required RelationshipStrength Strength { get; init; }
    public required bool IsRuntimeBinding { get; init; }
}
```

### 3.4 RelationshipRoles

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

/// <summary>
/// Canonical role constants used by relationship extractors.
/// Prevents magic strings in diagnostics whitelists and consumer code.
/// </summary>
public static class RelationshipRoles
{
    public const string InputSchema     = "InputSchema";
    public const string OutputSchema    = "OutputSchema";
    public const string Schema          = "Schema";
    public const string Interaction     = "Interaction";
    public const string PayloadSchema   = "PayloadSchema";
    public const string VariableSchema  = "VariableSchema";
    public const string CapabilityStep  = "CapabilityStep";
    public const string HumanTaskStep   = "HumanTaskStep";
    public const string SubWorkflowStep = "SubWorkflowStep";
    public const string Outcome         = "Outcome";
    public const string SupersededBy    = "SupersededBy";
}
```

### 3.5 IDescriptorTopologyBuilder

```csharp
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Primary topology entry point. Stateless: Build() is a pure function.
/// Registered as singleton in DI.
/// </summary>
public interface IDescriptorTopologyBuilder
{
    /// <summary>
    /// Build a frozen topology snapshot from the provided descriptor inventory.
    /// Uses IDescriptorRelationshipProvider (DI-injected) to extract edges.
    /// Diagnostics are computed inline and frozen into the snapshot.
    /// </summary>
    DescriptorTopologySnapshot Build(IReadOnlyList<IDescriptor> descriptors);
}
```

> **Note**: `IDescriptorTopologyProvider` (auto-inventory, caching, refresh) is **deferred to Phase 6c**. Phase 6b caller provides descriptors explicitly. The adapter and tests both call `Build(descriptors)` directly.

---

## 4. DescriptorTopologySnapshot

### 4.1 Structure

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed class DescriptorTopologySnapshot
{
    public DateTimeOffset BuiltAt { get; }
    public int NodeCount { get; }
    public int EdgeCount { get; }

    public IReadOnlyDictionary<DescriptorRef, DescriptorNode> Nodes { get; }
    public IReadOnlyList<DescriptorEdge> Edges { get; }
    public DescriptorTopologyDiagnostics Diagnostics { get; }

    // -- Direct Queries --

    /// <summary>Descriptors that 'of' depends on (outgoing edges).</summary>
    public IReadOnlyList<DescriptorNode> GetDirectDependencies(DescriptorRef of);

    /// <summary>Descriptors that depend on 'of' (incoming edges).</summary>
    public IReadOnlyList<DescriptorNode> GetDirectDependents(DescriptorRef of);

    // -- Transitive Queries --

    /// <summary>All descriptors reachable by following outgoing edges (downstream).
    /// Default: Strong edges only. Set includeWeak=true for full graph.</summary>
    public IReadOnlySet<DescriptorNode> GetTransitiveDependencies(
        DescriptorRef of, bool includeWeak = false);

    /// <summary>All descriptors reachable by following incoming edges (upstream / reversed graph).
    /// Default: Strong edges only. Set includeWeak=true for full graph.</summary>
    public IReadOnlySet<DescriptorNode> GetTransitiveDependents(
        DescriptorRef of, bool includeWeak = false);

    // -- Version-Aware Consumer Index --

    /// <summary>
    /// Find all descriptors that consume the identified descriptor.
    /// version == null → all consumers regardless of version.
    /// version != null → exact-version consumers ∪ unpinned (Version=null) consumers.
    /// </summary>
    public IReadOnlyList<DescriptorNode> GetConsumers(
        string ns, string id, int? version = null);

    // -- Lookups --

    public bool Contains(DescriptorRef r);
    public DescriptorNode? FindNode(DescriptorRef r);
}
```

> **Note**: There is no id-only lookup on the public snapshot API. `DescriptorRef` carries `(Namespace, Id, Version?)` — use `Contains(DescriptorRef)` and `FindNode(DescriptorRef)` as the only lookup entry points. Id-only lookup (for `DescriptorCatalog` backward compat) is an **adapter-internal** concern, not a public topology API. This prevents namespace/version confusion when schema/capability/form may share the same id string.

### 4.2 Traversal Semantics

Edge: `From → To` = "From depends on To"

| Query | Direction | Follows |
|---|---|---|
| `GetDirectDependencies(of)` | Outgoing from `of` | All edges |
| `GetDirectDependents(of)` | Incoming to `of` | All edges |
| `GetTransitiveDependencies(of)` | BFS outgoing (downstream) | Strong by default |
| `GetTransitiveDependents(of)` | BFS incoming (upstream, reversed) | Strong by default |

### 4.3 Consumer Index Internals

Three internal dictionaries keyed by `DescriptorIdentity` = `(Namespace, Id)`:

```
_consumersByIdentity:       Dictionary<DescriptorIdentity, List<(Consumer, Edge)>>
                            All consumers, regardless of version.

_consumersByExactVersion:   Dictionary<(DescriptorIdentity, int Version), List<(Consumer, Edge)>>
                            Only edges where To.Version != null.

_consumersByUnpinnedVersion: Dictionary<DescriptorIdentity, List<(Consumer, Edge)>>
                            Only edges where To.Version == null.
```

Population (per edge):

```csharp
// DescriptorRef has Namespace, Id, Version. No Kind field.
var identity = new DescriptorIdentity(edge.To.Namespace, edge.To.Id);
_consumersByIdentity[identity].Add((edge.From, edge));

if (edge.To.Version.HasValue)
    _consumersByExactVersion[(identity, edge.To.Version.Value)].Add((edge.From, edge));
else
    _consumersByUnpinnedVersion[identity].Add((edge.From, edge));
```

Query:

```csharp
GetConsumers(ns, id, version):
    identity = new DescriptorIdentity(ns, id)
    if version == null → _consumersByIdentity[identity]
    if version != null → _consumersByExactVersion[(identity, version)]
                       ∪ _consumersByUnpinnedVersion[identity]
```

---

## 5. Diagnostics

### 5.1 Severity Model

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public enum DiagnosticSeverity { Error, Warning, Info }

public sealed record DescriptorTopologyDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DescriptorRef? Subject,
    IReadOnlyList<DescriptorRef>? RelatedRefs);
```

### 5.2 Five Diagnostic Rules

| # | Code | Severity | Detection Rule |
|---|---|---|---|
| 1 | `MISSING_TARGET` | **Error** (Strong) / **Warning** (Weak) | Edge.To is not a key in `Nodes`. Severity mirrors the edge's `Strength`: a missing Strong dependency (e.g., Form→Schema) is a structural Error; a missing Weak reference (e.g., Capability event production, SupersededBy) is a Warning. |
| 2 | `STRONG_CYCLE` | **Error** | DFS back-edge on Strong edges only |
| 3 | `ORPHAN` | **Warning** | Node with zero incoming edges, `State` ∉ {Draft, Removed} |
| 4 | `EXACT_DUPLICATE` | **Warning** | Same full semantic identity (From, To, Kind, Role, SourcePath, Strength, IsRuntimeBinding) appears ≥2 times. Two edges with the same (From, To, Kind) but different Role are distinct relationships, not duplicates. |
| 5 | `UNSUPPORTED_REFERENCE` | **Warning** | Edge matches an explicit `(Role, Kind)` entry in a known-unsupported whitelist |

### 5.3 UNSUPPORTED_REFERENCE Whitelist

Uses `RelationshipRoles` constants, NOT `Strength.Weak` inference:

```csharp
private static readonly HashSet<(string Role, RelationshipKind Kind)> KnownUnsupported =
[
    (RelationshipRoles.SubWorkflowStep, RelationshipKind.References),
];
```

When SubWorkflow runtime support is added in the future, remove the entry — the diagnostic disappears automatically. No extractor changes needed.

### 5.4 Diagnostics Record

```csharp
public sealed record DescriptorTopologyDiagnostics
{
    public required IReadOnlyList<DescriptorTopologyDiagnostic> All { get; init; }

    public IReadOnlyList<DescriptorTopologyDiagnostic> Errors => ...;
    public IReadOnlyList<DescriptorTopologyDiagnostic> Warnings => ...;

    public bool HasErrors { get; }
    public bool IsHealthy => !HasErrors;
}
```

---

## 6. Builder (Internal)

`DescriptorTopologyBuilder` is internal to `CrestCreates.Metadata`. Not exposed via any interface other than `IDescriptorTopologyBuilder`. The class itself has a **public constructor** taking `IDescriptorRelationshipProvider` so DI can activate it when registered via `AddSingleton<IDescriptorTopologyBuilder, DescriptorTopologyBuilder>()` in the same assembly's extension method.

Injected dependencies: `IDescriptorRelationshipProvider` (from DI).

### Build Phases

```
Phase 1: Receive descriptor inventory from caller
         → IReadOnlyList<IDescriptor> descriptors

Phase 2: Create DescriptorNode per descriptor
         → Dictionary<DescriptorRef, DescriptorNode> nodes
         (Identity + Kind/Name/State/ContractHash/SupersededById)

Phase 3: Extract edges via IDescriptorRelationshipProvider
         → For each descriptor, call _relationshipProvider.GetRelationships(d)
         → List<DescriptorEdge> edges
         → Populate node.OutgoingEdgeIndices / node.IncomingEdgeIndices

Phase 4: Build consumer index
         → _consumersByIdentity / _consumersByExactVersion / _consumersByUnpinnedVersion
         → Keyed by DescriptorIdentity(Namespace, Id) — derived from DescriptorRef

Phase 5: Run diagnostics
         → MISSING_TARGET, STRONG_CYCLE, ORPHAN, EXACT_DUPLICATE, UNSUPPORTED_REFERENCE

Phase 6: Freeze and return DescriptorTopologySnapshot
```

---

## 7. DescriptorDependencyGraphAdapter

Compatibility projection. Takes an `IDescriptorTopologyBuilder` reference + a descriptor list. Adapter builds snapshot once on first query and caches it.

**Important**: `DescriptorCatalog` is compatibility-only. New code MUST use `DescriptorTopologySnapshot` / `IDescriptorTopologyBuilder` directly. Do not use `DescriptorCatalog` as the Phase 6b acceptance test entry point.

```csharp
namespace CrestCreates.Metadata;

/// <summary>
/// Compatibility adapter: wraps IDescriptorTopologyBuilder → IDescriptorDependencyGraph.
/// Builds snapshot once from the provided descriptor inventory.
/// Intended ONLY for DescriptorCatalog backward compat. New code uses DescriptorTopologySnapshot directly.
/// </summary>
public sealed class DescriptorDependencyGraphAdapter : IDescriptorDependencyGraph
{
    private readonly IDescriptorTopologyBuilder _builder;
    private readonly IReadOnlyList<IDescriptor> _descriptors;
    private DescriptorTopologySnapshot? _snapshot;

    public DescriptorDependencyGraphAdapter(
        IDescriptorTopologyBuilder builder,
        IReadOnlyList<IDescriptor> descriptors)
    {
        _builder = builder;
        _descriptors = descriptors;
    }

    private DescriptorTopologySnapshot Snapshot =>
        _snapshot ??= _builder.Build(_descriptors);

    public IReadOnlyList<DependencyEdge> GetDependencies(string descriptorId)
        → Snapshot.FindNode(descriptorId) → GetDirectDependencies → project to DependencyEdge[]

    public IReadOnlyList<DependencyEdge> GetDependents(string descriptorId)
        → Snapshot.FindNode(descriptorId) → GetDirectDependents → project to DependencyEdge[]

    public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion)
        → Snapshot → GetTransitiveDependents → version comparison → ImpactReport

    public void AddEdge(string sourceId, string targetId, DescriptorDependencyKind kind)
        => throw new NotSupportedException(
            "AddEdge is no longer supported. " +
            "Edges are computed from descriptor relationships via IDescriptorTopologyBuilder.");

    private static DescriptorDependencyKind MapKind(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Produces   => DescriptorDependencyKind.Produces,
        RelationshipKind.Consumes   => DescriptorDependencyKind.Consumes,
        RelationshipKind.DependsOn  => DescriptorDependencyKind.References,
        RelationshipKind.References => DescriptorDependencyKind.References,
        RelationshipKind.Uses       => DescriptorDependencyKind.Uses,
        RelationshipKind.Triggers   => DescriptorDependencyKind.Triggers,
        _ => DescriptorDependencyKind.References
    };
}
```

---

## 8. Legacy & Removal Policy

| Type | Phase 6b Action |
|---|---|
| `DependencyEdge` | **Preserve** — still used by adapter & `DescriptorCatalog`. Doc-mark as legacy (XML comment). No `[Obsolete]` — add in Phase 6c after migration. |
| `DescriptorDependencyKind` | **Preserve** — still used by adapter & `DescriptorCatalog`. Doc-mark as legacy. No `[Obsolete]` — add in Phase 6c. |
| `ImpactReport` | **Preserve as-is** — `DescriptorCatalog.AnalyzeImpact()` depends on it. Defer to Phase 6c. |
| `DescriptorDependencyGraph` | Move to `./99_RecycleBin/` |
| `DependencyGraphProvider` | Move to `./99_RecycleBin/` |

---

## 9. Project Structure

### New Files (11)

```
framework/src/CrestCreates.Metadata.Abstractions/
  DescriptorTopology/
    DescriptorIdentity.cs
    DescriptorNode.cs
    DescriptorEdge.cs
    DescriptorTopologySnapshot.cs
    DescriptorTopologyDiagnostics.cs
    DescriptorTopologyDiagnostic.cs
    DiagnosticSeverity.cs
    RelationshipRoles.cs
  IDescriptorTopologyBuilder.cs

framework/src/CrestCreates.Metadata/
  DescriptorTopologyBuilder.cs              (internal)
  DescriptorDependencyGraphAdapter.cs
```

### Modified Files (1)

```
framework/src/CrestCreates.Metadata/
  MetadataServiceCollectionExtensions.cs   → AddTopologyKernel() — registers IDescriptorTopologyBuilder
                                            → No IDescriptorDependencyGraph registration change
                                              (adapter is not a DI singleton)
```

### Moved to Recycle Bin (2)

```
framework/src/CrestCreates.Metadata/DescriptorDependencyGraph.cs   → 99_RecycleBin/
framework/src/CrestCreates.Metadata/DependencyGraphProvider.cs     → 99_RecycleBin/
```

### Test Files (5)

```
framework/test/CrestCreates.Metadata.Tests/
  DescriptorTopology/
    DescriptorNodeTests.cs
    DescriptorTopologySnapshotTests.cs
    DescriptorTopologyBuilderTests.cs
    DescriptorTopologyDiagnosticsTests.cs
    DescriptorDependencyGraphAdapterTests.cs
```

---

## 10. DI Registration

```csharp
// MetadataServiceCollectionExtensions.AddTopologyKernel()
public static IServiceCollection AddTopologyKernel(this IServiceCollection services)
{
    // Stateless builder — registered as singleton
    services.TryAddSingleton<IDescriptorTopologyBuilder, DescriptorTopologyBuilder>();

    // Adapter is NOT registered as a DI service.
    // It's constructed explicitly with a descriptor list when needed
    // (e.g., by DescriptorCatalog setup code or in tests).

    return services;
}
```

`DescriptorCatalog` continues injecting `IDescriptorDependencyGraph`. The adapter is constructed and passed to `DescriptorCatalog` at composition time (not via DI). This keeps backward compat without forcing the adapter into the DI container.

---

## 11. Testing Strategy

### 11.1 Builder Tests

| Test | Assertion |
|---|---|
| `Builder_Creates_Nodes_For_All_Provided_Descriptors` | Node count = input descriptor count |
| `Builder_Node_Summary_Properties_Correct` | Kind, Name, State, ContractHash match descriptor |
| `Builder_Edges_From_RelationshipProvider` | Edge count = sum of all extractor outputs |
| `Builder_Edge_Indices_Populated` | Node.OutgoingEdgeIndices and IncomingEdgeIndices are correct |
| `Builder_Empty_Input_Produces_Empty_Snapshot` | Nodes=0, Edges=0, no diagnostics |

### 11.2 Snapshot Query Tests

| Test | Assertion |
|---|---|
| `GetDirectDependencies_Returns_Outgoing` | A→B, A→C → GetDirectDependencies(A) = {B, C} |
| `GetDirectDependents_Returns_Incoming` | A→C, B→C → GetDirectDependents(C) = {A, B} |
| `GetTransitiveDependencies_Defaults_Strong_Only` | A→B(Strong), B→C(Weak) → {B} only |
| `GetTransitiveDependencies_IncludeWeak` | A→B(Strong), B→C(Weak) → {B, C} |
| `GetTransitiveDependents_Direction_Correct` | A→B→C → GetTransitiveDependents(C) = {B, A} (reversed) |
| `Transitive_Cycle_Safe` | A→B→A → terminates cleanly |

### 11.3 Consumer Index Tests

| Test | Assertion |
|---|---|
| `GetConsumers_NullVersion_Returns_All` | All consumers regardless of pinned version |
| `GetConsumers_ExactVersion_Returns_Exact_Plus_Unpinned` | v2 consumers + Version=null consumers; NOT v1/v3 consumers |
| `GetConsumers_UnpinnedConsumer_Included_In_Exact_Query` | Consumer with Version=null appears in both v1 and v2 queries |
| `GetConsumers_No_Match_Returns_Empty` | Unknown descriptor → empty |

### 11.4 Diagnostics Tests

| Test | Assertion |
|---|---|
| `Missing_Strong_Target_Error` | Strong edge (e.g., Form→Schema) to non-existent descriptor → `MISSING_TARGET` Error |
| `Missing_Weak_Target_Warning` | Weak edge (e.g., SupersededBy) to non-existent descriptor → `MISSING_TARGET` Warning |
| `Strong_Cycle_Error` | A→B→A (Strong) → `STRONG_CYCLE` error |
| `Weak_Cycle_No_Error` | A→B→A (Weak) → no cycle diagnostic |
| `Orphan_Warning` | Node with zero incoming, State=Active → `ORPHAN` warning |
| `Orphan_Draft_Excluded` | Node with zero incoming, State=Draft → no orphan |
| `Exact_Duplicate_Warning` | Same full key (From, To, Kind, Role, SourcePath, Strength, IsRuntimeBinding) twice → `EXACT_DUPLICATE` warning |
| `Different_Role_Not_Duplicate` | Same (From, To, Kind) but different Role → no duplicate diagnostic |
| `Unsupported_Reference_Warning` | SubWorkflowStep + References → `UNSUPPORTED_REFERENCE` |
| `Unsupported_Not_Triggered_By_Weak_Alone` | Weak SupersededBy → no diagnostic (not in whitelist) |

### 11.5 Adapter Tests

| Test | Assertion |
|---|---|
| `Adapter_GetDependencies_Maps_Correctly` | Returns DependencyEdge[] with correct Kind mapping |
| `Adapter_GetDependents_Maps_Correctly` | Returns DependencyEdge[] |
| `Adapter_AddEdge_Throws_NotSupportedException` | Mutation path is blocked |
| `Adapter_AnalyzeImpact_Uses_TransitiveDependents` | Version-aware impact from transitive closure |
| `Adapter_KindMapping_All_Six_Covered` | Every RelationshipKind maps to correct DescriptorDependencyKind |

### 11.6 Regression Gate

All existing test suites must pass:
- Metadata.Tests (95), Form.Tests (35), Capability.Tests (120), Event.Tests (36), HumanTask.Tests (47), Workflow.Tests (63)
- Full `dotnet build` — 0 errors

---

## 12. RelationshipKind → DescriptorDependencyKind Mapping

Implemented in `DescriptorDependencyGraphAdapter`:

| RelationshipKind | DescriptorDependencyKind |
|---|---|
| `Produces` | `Produces` |
| `Consumes` | `Consumes` |
| `DependsOn` | `References` |
| `References` | `References` |
| `Uses` | `Uses` |
| `Triggers` | `Triggers` |

---

## 13. Explicit Non-Goals

Phase 6b MUST NOT implement:

- Re-parsing descriptor internals
- Runtime reflection scanning
- Compatibility or breaking-change analyzer
- Lifecycle governance
- Package import/export
- LLM draft planning / AI reasoning
- `GetAllRelationships()` on extractors (deferred — requires registry DI into extractors)
- `IDescriptorTopologyProvider` with auto-inventory / caching / refresh (deferred to Phase 6c)
- `DescriptorCatalog` changes (compatibility-only; new code uses `DescriptorTopologySnapshot` directly)
- `MetadataBootstrapper.BuildAll()` changes
- New descriptor registries or registry build paths
- `ImpactReport` replacement (deferred to Phase 6c)
- `[Obsolete]` attribute on `DependencyEdge` / `DescriptorDependencyKind` (deferred to Phase 6c after migration)

---

## 14. Design Decisions Summary

| Decision | Rationale |
|---|---|
| Explicit inventory (`IReadOnlyList<IDescriptor>`) | Typed registries don't sync to `IGlobalDescriptorRegistry`; builder must not depend on potentially-empty global state |
| `IDescriptorTopologyBuilder` (stateless) | No caching ambiguity; caller owns descriptor lifecycle |
| `IDescriptorTopologyProvider` deferred to Phase 6c | Requires registry-aware inventory aggregation; out of scope for Phase 6b |
| `DescriptorIdentity` = `(Namespace, Id)` — no Kind | `DescriptorRef` has no Kind field; Namespace is already unique per descriptor kind |
| `GetConsumers(ns, id, version?)` — no `DescriptorKind` parameter | Derived from `DescriptorIdentity` key shape |
| Build-once immutable snapshot | No concurrency, no incremental mutation, simpler builder, AoT-friendly |
| Node = identity + summary properties | Sufficient for common queries without registry round-trips; avoids full descriptor duplication |
| Diagnostics embedded in snapshot | Self-contained; computed once at Build(); no lazy evaluation surprises |
| MISSING_TARGET severity mirrors edge Strength | Strong missing target → Error (structural break); Weak missing target → Warning (informational / optional ref not found). Preserves Phase 6a's Strength semantics. |
| Severity tiers (Error/Warning) | MISSING_TARGET(Strong) and STRONG_CYCLE are structural errors; MISSING_TARGET(Weak)/ORPHAN/DUPLICATE/UNSUPPORTED are advisory |
| UNSUPPORTED_REFERENCE uses explicit `(Role, Kind)` whitelist | Not `Strength.Weak` inference; avoids false positives on metadata refs; auto-cleans as runtime support is added |
| EXACT_DUPLICATE uses full semantic key | (From, To, Kind, Role, SourcePath, Strength, IsRuntimeBinding) — edges with different Role/Strength are distinct relationships, not duplicates |
| UNSUPPORTED_REFERENCE uses explicit `(Role, Kind)` whitelist | Not `Strength.Weak` inference; avoids false positives on metadata refs; auto-cleans as runtime support is added |
| Consumer index: 3-way segmentation | Correct null-as-any semantics: exact version query returns exact-version + unpinned, NOT all-version |
| Traversal direction: outgoing = dependencies | Matches edge semantics (From→To = depends on) |
| `includeWeak = false` by default | Keeps structural topology clean; Phase 6c/6d can opt into full impact |
| `RelationshipRoles` constants | Prevents magic strings in diagnostics whitelist; single source of truth for role names |
| Adapter NOT registered in DI | Caller constructs adapter with explicit descriptor list; avoids hidden state |
| `DescriptorCatalog` compatibility-only | New code uses `DescriptorTopologySnapshot` directly; catalog is NOT the Phase 6b acceptance test entry point |
| Doc-only legacy marking (no `[Obsolete]`) | Avoids noise in adapter/ImpactReport/test code; add `[Obsolete]` in Phase 6c after migration |
| Move `DescriptorDependencyGraph` + `DependencyGraphProvider` to recycle bin | Mutation path eliminated; static singleton removed |
| `DescriptorTopologyBuilder` is internal | Consumers only see `IDescriptorTopologyBuilder.Build()` + `DescriptorTopologySnapshot` |

---

**Design reviewed and approved. Ready for implementation plan.**
