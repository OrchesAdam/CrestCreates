# Phase 7b Stabilization — Descriptor Source and Traversal Direction Model

**Date:** 2026-06-17
**Issue:** #36
**Status:** Design approved

## Problem

Phase 7b Metadata Context Pack has two structural risks that must be stabilized before later consumers (Agent Tool Surface, Review Report, AI-assisted authoring) increase pressure on the builder:

1. **Dual data source alignment.** The builder receives `DescriptorTopologySnapshot` (graph structure) and `IReadOnlyList<IDescriptor>` (content data) separately. Version-aware lookup logic is scattered across `BuildDescriptorIndex`, `FindDescriptor`, and `BuildDescriptorEntries` with slightly different fallback strategies. There is no single point of truth for resolving a `DescriptorRef` to both its topology node and its descriptor instance.

2. **Traversal direction modeling.** Traversal over outgoing and incoming topology edges does not preserve direction explicitly. `RuntimeScenario.Both` concatenates `OutgoingEdgeIndices` and `IncomingEdgeIndices` into a flat list that loses direction information. `ImpactRadius` BFS does bidirectional traversal without tracking which direction each edge was reached from. `DirectDependencies` and `DirectDependents` use different APIs to enumerate edges.

## Design

### DirectedEdgeVisit

An internal `readonly record struct` that carries edge direction explicitly and pre-resolves source/target.

```csharp
internal enum DirectedEdgeVisitDirection { Outgoing, Incoming }

internal readonly record struct DirectedEdgeVisit(
    DescriptorEdge Edge,
    DescriptorRef Source,
    DescriptorRef Target,
    DirectedEdgeVisitDirection Direction)
{
    internal static DirectedEdgeVisit FromOutgoing(DescriptorEdge edge)
        => new(edge, edge.From, edge.To, DirectedEdgeVisitDirection.Outgoing);

    internal static DirectedEdgeVisit FromIncoming(DescriptorEdge edge)
        => new(edge, edge.To, edge.From, DirectedEdgeVisitDirection.Incoming);
}
```

- `Source` is always the node being traversed from (the current node in the walk).
- `Target` is always the node being traversed to (the neighbor discovered by this edge).
- `Direction = Outgoing` means the edge originates at the current node (`Edge.From == Source`, `Edge.To == Target`).
- `Direction = Incoming` means the edge arrives at the current node (`Edge.To == Source`, `Edge.From == Target`).
- Factory methods `FromOutgoing`/`FromIncoming` are the preferred construction path — they eliminate the risk of swapping Source/Target.

### ResolvedDescriptor

A pure data record expressing the resolution state of a `DescriptorRef`. No policy, no behavior.

```csharp
internal sealed record ResolvedDescriptor(
    DescriptorRef RequestedRef,
    DescriptorNode? TopologyNode,
    IDescriptor? Descriptor);
```

Four possible states:

| TopologyNode | Descriptor | Meaning |
|---|---|---|
| not null | not null | Fully resolved |
| not null | null | Topology-only — no matching descriptor in inventory |
| null | not null | Inventory-only — no matching topology node |
| null | null | Not found in either source |

Policy decisions based on these states live in the builder, not in this type.

### MetadataContextDescriptorSource

An internal class that mediates between `DescriptorTopologySnapshot` and the descriptor inventory. The builder never directly accesses topology or descriptor inventory — it goes through this source.

```csharp
internal sealed class MetadataContextDescriptorSource
{
    MetadataContextDescriptorSource(
        DescriptorTopologySnapshot topology,
        IReadOnlyList<IDescriptor> descriptors);

    ResolvedDescriptor Resolve(DescriptorRef reference);

    IEnumerable<DirectedEdgeVisit> GetDirectedEdges(
        DescriptorRef nodeRef,
        ScenarioTraversalDirection direction);
}
```

**Two methods, no extra exits.** The builder uses `Resolve` for all ref→data lookups and `GetDirectedEdges` for all edge enumeration.

#### Resolve

Always returns a `ResolvedDescriptor` — never null. The caller inspects `TopologyNode` and `Descriptor` to determine the resolution state and apply policy.

**Version-aware indexing (absorbs current `BuildDescriptorIndex`):**

- Version-pinned lookup uses exact `Namespace + Id + Version`.
- Unpinned lookup is allowed only when exactly one descriptor version matches `Namespace + Id`.
- If multiple versions exist, the source returns `Descriptor = null` and the builder emits `CTXPACK_AMBIGUOUS_DESCRIPTOR_REF`.
- No latest-version or first-match fallback is allowed.

**Unpinned ref resolution rule:**

- If exactly one matching descriptor exists (single version), resolve it.
- If multiple versions match the same `Namespace + Id`, treat as ambiguous — emit `CTXPACK_AMBIGUOUS_DESCRIPTOR_REF`, do not silently choose one.
- If zero matches, the `Descriptor` field of `ResolvedDescriptor` is null.

**Topology-side resolution:**

- Delegates to `topology.TryResolveRef()` for the `TopologyNode` field.
- Topology resolution is independent of inventory resolution — both are attempted, and the results are combined into a single `ResolvedDescriptor`.

#### GetDirectedEdges

Enumerates directed edge visits from a topology node based on the requested direction.

- Resolves `nodeRef` via topology to get the `DescriptorNode`.
- If the node is not in topology, returns empty enumerable.
- Based on `direction`:
  - `Dependencies` → `OutgoingEdgeIndices` → `DirectedEdgeVisit.FromOutgoing(edge)` for each
  - `Dependents` → `IncomingEdgeIndices` → `DirectedEdgeVisit.FromIncoming(edge)` for each
  - `Both` → yields outgoing visits first, then incoming visits
- Returns `IEnumerable<DirectedEdgeVisit>` — no forced allocation. Callers can materialize if needed.
- Edge indices are enumerated in their natural (sorted) order, preserving determinism.

**This is not scope resolution.** The source does not know about `FocusOnly`, `ImpactRadius`, or `RuntimeScenario` semantics. It only provides ref resolution and directed edge visits. Scope traversal remains in `DefaultMetadataContextPackBuilder`.

### Builder Refactoring

#### Removed private methods

- `BuildDescriptorIndex` — moved to `DescriptorSource` constructor
- `FindDescriptor` — replaced by `source.Resolve(ref).Descriptor`

#### Simplified steps

| Step | Change |
|------|--------|
| Resolve focus nodes | `source.Resolve(focusRef)` replaces `topology.Contains` + `topology.FindNode` + `FindDescriptor`. Not found = `TopologyNode is null && Descriptor is null`. Inventory-only = include if focused but emit warning. Topology-only = skip descriptor entry, emit error. |
| DirectDependencies | `source.GetDirectedEdges(focusRef, Dependencies)` replaces `topology.GetDirectDependencies` + `node.OutgoingEdgeIndices`. Each visit's `Target` goes through `source.Resolve` for inclusion. |
| DirectDependents | `source.GetDirectedEdges(focusRef, Dependents)` replaces `topology.GetDirectDependents` + `node.IncomingEdgeIndices`. Same pattern. |
| ImpactRadius BFS | `source.GetDirectedEdges(frontierNode, Both)` replaces manual out+in enumeration. Each visit's `Target` goes through `source.Resolve`. Truncation diagnostic checks resolved targets for unvisited neighbors. |
| RuntimeScenario | `source.GetDirectedEdges(boundaryNode, step.Direction)` replaces manual edge index merging per step. `Direction` field is carried correctly through `FollowKind`/`Role` filtering. |
| BuildDescriptorEntries | `source.Resolve(ref)` replaces `topology.FindNode` + `FindDescriptor` + version fallback. `DescriptorEntry` is built only when `resolved.Descriptor` is not null. `TopologyNode` may provide topology metadata for diagnostics/traversal, but must not be used to fabricate descriptor entries. Hash/governance enrichment is computed only from `resolved.Descriptor`. |

#### Asymmetric mismatch policy

**Focus resolution:**

| State | Diagnostic | Action |
|---|---|---|
| Neither topology nor inventory | `CTXPACK_FOCUS_NOT_FOUND` (Warning) | Skip |
| Topology only | `CTXPACK_DESCRIPTOR_MISSING_FOR_TOPOLOGY_REF` (Error) | Skip descriptor entry, no traversal expansion into final set |
| Inventory only | `CTXPACK_TOPOLOGY_NODE_MISSING_FOR_DESCRIPTOR` (Warning) | Include only if directly focused, no traversal possible |
| Both | — | Normal |

**Traversal-discovered target:**

| State | Diagnostic | Action |
|---|---|---|
| Topology only | `CTXPACK_DESCRIPTOR_MISSING_FOR_TOPOLOGY_REF` (Error) | Do not add to final descriptor set |
| Inventory only | — | Not normally possible from topology traversal |
| Neither | — | Not normally possible; if encountered, diagnostic as unresolved edge target |

**Unpinned ref with multiple versions:**

| State | Diagnostic | Action |
|---|---|---|
| Multiple versions match | `CTXPACK_AMBIGUOUS_DESCRIPTOR_REF` (Warning) | Skip the ref, do not silently choose |

#### Pack closure invariant

Every `MetadataContextPackRelationshipEntry` endpoint must exist in `MetadataContextPack.Descriptors`.

Enforced in `CollectRelationshipEntries`:

```csharp
includedEdges = includedEdges.Where(e =>
    includedRefs.Contains(e.From) && includedRefs.Contains(e.To));
```

This prevents dangling relationships in degraded packs where some descriptors were excluded due to resolution failures.

### New Diagnostic Codes

| Code | Severity | Meaning |
|------|----------|---------|
| `CTXPACK_DESCRIPTOR_MISSING_FOR_TOPOLOGY_REF` | Error | Topology node/edge references a descriptor ref that is absent from descriptor inventory |
| `CTXPACK_TOPOLOGY_NODE_MISSING_FOR_DESCRIPTOR` | Warning | Descriptor inventory contains the requested descriptor, but topology has no node for it |
| `CTXPACK_AMBIGUOUS_DESCRIPTOR_REF` | Warning | The requested descriptor ref did not specify a version, and multiple descriptor versions match the same Namespace/Id |

Added to `MetadataContextPackDiagnosticCodes` in the Abstractions project.

### No Public API Change

- `IMetadataContextPackBuilder.Build()` signature unchanged.
- `MetadataContextPackRequest`, `MetadataContextPack`, and all public record types unchanged.
- `MetadataContextPackScope`, `ScenarioTraversalDirection`, `MetadataContextPackDiagnosticSeverity` enums unchanged.
- Three new diagnostic code constants added to the existing `MetadataContextPackDiagnosticCodes` static class — additive only.
- `DirectedEdgeVisit`, `DirectedEdgeVisitDirection`, `ResolvedDescriptor`, and `MetadataContextDescriptorSource` are all `internal` to the ContextPack implementation project.

## Test Coverage

26 existing tests + 15 new tests = 41 total.

### G. DescriptorSource Resolution (4 tests)

1. Fully-resolved ref returns `TopologyNode` and `Descriptor` both non-null
2. Topology-only ref returns `Descriptor` null, builder emits `CTXPACK_DESCRIPTOR_MISSING_FOR_TOPOLOGY_REF`
3. Inventory-only ref returns `TopologyNode` null, builder emits `CTXPACK_TOPOLOGY_NODE_MISSING_FOR_DESCRIPTOR`
4. Neither-exists ref returns both null, builder treats as focus-not-found

### H. Multi-Version Coexistence (5 tests)

5. Two versions of same descriptor, focus on v2 → resolves to v2 instance, v1 not in set
6. Two versions, ImpactRadius traversal → each version traversed separately, not collapsed
7. Two versions, kind filter → filter applied per-version independently
8a. Unpinned ref with exactly one matching version → resolves to that descriptor
8b. Unpinned ref with multiple matching versions → emits `CTXPACK_AMBIGUOUS_DESCRIPTOR_REF`, does not choose

### I. Direction-Aware Traversal (4 tests)

9. DirectDependencies follows only outgoing edges, incoming edges ignored
10. DirectDependents follows only incoming edges, outgoing edges ignored
11. RuntimeScenario Both follows outgoing then incoming, Direction field distinguishes them
12. ImpactRadius bidirectional BFS uses direction-aware visits, self-cycle terminates

### J. Pack Closure Invariant (2 tests)

13. Topology edge references descriptor not in inventory → relationship excluded, `CTXPACK_DESCRIPTOR_MISSING_FOR_TOPOLOGY_REF` emitted
14. Graph with mixed resolved/unresolved endpoints → only fully-contained relationships preserved

All new tests exercise the `DefaultMetadataContextPackBuilder` public API. `MetadataContextDescriptorSource` is an internal implementation detail tested indirectly through the builder.

## Out of Scope

- No LLM integration
- No prompt formatting
- No Agent Tool Surface
- No DescriptorDraft generation
- No registry mutation
- No topology rebuilding
- No dependency on the Metadata implementation project
- No RuntimeScenario branching or conditional recipe redesign
- No splitting of the builder into multiple classes (future concern, not this stabilization)
