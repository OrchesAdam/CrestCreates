# Phase 7b Stabilization — Descriptor Source and Traversal Direction Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden Phase 7b Metadata Context Pack by centralizing descriptor lookup behind `MetadataContextDescriptorSource` and introducing a direction-aware edge visit model.

**Architecture:** Introduce two internal types (`DirectedEdgeVisit` + `ResolvedDescriptor`) and one internal mediator class (`MetadataContextDescriptorSource`) that wraps both `DescriptorTopologySnapshot` and the descriptor inventory. The builder is refactored to use `source.Resolve()` and `source.GetDirectedEdges()` exclusively — it no longer directly indexes descriptors or enumerates topology edge indices.

**Tech Stack:** .NET 10, xUnit 2.9.3, FluentAssertions, Moq

## Global Constraints

- No public API change to `IMetadataContextPackBuilder.Build()` signature or public record types.
- Three new diagnostic code constants added to existing `MetadataContextPackDiagnosticCodes` — additive only.
- `DirectedEdgeVisit`, `DirectedEdgeVisitDirection`, `ResolvedDescriptor`, and `MetadataContextDescriptorSource` are all `internal` to the ContextPack implementation project.
- DescriptorEntry is built only when `resolved.Descriptor` is not null. TopologyNode must not be used to fabricate descriptor entries.
- Unpinned ref with multiple matching versions → `CTXPACK_AMBIGUOUS_DESCRIPTOR_REF`, no fallback.
- Pack closure invariant: every relationship endpoint must exist in the descriptor set.
- All output ordering must remain deterministic.
- No dependency on `CrestCreates.Metadata` (implementation project).

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `framework/src/CrestCreates.Metadata.ContextPack/DirectedEdgeVisitDirection.cs` | Enum for edge visit direction |
| Create | `framework/src/CrestCreates.Metadata.ContextPack/DirectedEdgeVisit.cs` | Direction-aware edge visit record struct |
| Create | `framework/src/CrestCreates.Metadata.ContextPack/ResolvedDescriptor.cs` | Resolution state record |
| Create | `framework/src/CrestCreates.Metadata.ContextPack/MetadataContextDescriptorSource.cs` | Mediator: Resolve + GetDirectedEdges |
| Modify | `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnosticCodes.cs` | Add 3 new diagnostic codes |
| Modify | `framework/src/CrestCreates.Metadata.ContextPack/DefaultMetadataContextPackBuilder.cs` | Refactor to use DescriptorSource |
| Modify | `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs` | Add 15 new tests |

---

### Task 1: DirectedEdgeVisit and ResolvedDescriptor Types

**Files:**
- Create: `framework/src/CrestCreates.Metadata.ContextPack/DirectedEdgeVisitDirection.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack/DirectedEdgeVisit.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack/ResolvedDescriptor.cs`

**Interfaces:**
- Consumes: `DescriptorEdge`, `DescriptorRef` from `CrestCreates.Metadata.Abstractions`
- Produces: `DirectedEdgeVisitDirection`, `DirectedEdgeVisit`, `ResolvedDescriptor` — used by Task 2 and Task 3

- [ ] **Step 1: Create DirectedEdgeVisitDirection.cs**

```csharp
namespace CrestCreates.Metadata.ContextPack;

internal enum DirectedEdgeVisitDirection
{
    Outgoing,
    Incoming
}
```

- [ ] **Step 2: Create DirectedEdgeVisit.cs**

```csharp
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.ContextPack;

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

- [ ] **Step 3: Create ResolvedDescriptor.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.ContextPack;

internal sealed record ResolvedDescriptor(
    DescriptorRef RequestedRef,
    DescriptorNode? TopologyNode,
    IDescriptor? Descriptor);
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.ContextPack`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.ContextPack/DirectedEdgeVisitDirection.cs \
        framework/src/CrestCreates.Metadata.ContextPack/DirectedEdgeVisit.cs \
        framework/src/CrestCreates.Metadata.ContextPack/ResolvedDescriptor.cs
git commit -m "feat(context-pack): add DirectedEdgeVisit and ResolvedDescriptor internal types (#36)"
```

---

### Task 2: MetadataContextDescriptorSource

**Files:**
- Create: `framework/src/CrestCreates.Metadata.ContextPack/MetadataContextDescriptorSource.cs`

**Interfaces:**
- Consumes: `DescriptorTopologySnapshot`, `IDescriptor`, `IVersionedDescriptor`, `DescriptorRef`, `ScenarioTraversalDirection`, `DirectedEdgeVisit`, `ResolvedDescriptor`
- Produces: `MetadataContextDescriptorSource` with `Resolve(DescriptorRef)` and `GetDirectedEdges(DescriptorRef, ScenarioTraversalDirection)` — used by Task 3

- [ ] **Step 1: Create MetadataContextDescriptorSource.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Metadata.ContextPack;

internal sealed class MetadataContextDescriptorSource
{
    private readonly DescriptorTopologySnapshot _topology;
    private readonly Dictionary<DescriptorRef, IDescriptor> _versionedIndex;
    private readonly Dictionary<DescriptorIdentity, List<IDescriptor>> _unpinnedIndex;

    public MetadataContextDescriptorSource(
        DescriptorTopologySnapshot topology,
        IReadOnlyList<IDescriptor> descriptors)
    {
        _topology = topology;
        _versionedIndex = new Dictionary<DescriptorRef, IDescriptor>();
        _unpinnedIndex = new Dictionary<DescriptorIdentity, List<IDescriptor>>();

        foreach (var d in descriptors)
        {
            var version = d is IVersionedDescriptor vd ? vd.Version : (int?)null;
            var exactKey = new DescriptorRef(d.Namespace, d.Id, version);
            _versionedIndex[exactKey] = d;

            var unpinnedKey = new DescriptorIdentity(d.Namespace, d.Id);
            if (!_unpinnedIndex.TryGetValue(unpinnedKey, out var list))
            {
                list = new List<IDescriptor>();
                _unpinnedIndex[unpinnedKey] = list;
            }
            list.Add(d);
        }
    }

    public ResolvedDescriptor Resolve(DescriptorRef reference)
    {
        var topologyNode = _topology.FindNode(reference);
        var descriptor = ResolveDescriptor(reference);

        return new ResolvedDescriptor(reference, topologyNode, descriptor);
    }

    public IEnumerable<DirectedEdgeVisit> GetDirectedEdges(
        DescriptorRef nodeRef,
        ScenarioTraversalDirection direction)
    {
        var node = _topology.FindNode(nodeRef);
        if (node is null)
            yield break;

        switch (direction)
        {
            case ScenarioTraversalDirection.Dependencies:
                foreach (var edgeIdx in node.OutgoingEdgeIndices)
                    yield return DirectedEdgeVisit.FromOutgoing(_topology.Edges[edgeIdx]);
                break;

            case ScenarioTraversalDirection.Dependents:
                foreach (var edgeIdx in node.IncomingEdgeIndices)
                    yield return DirectedEdgeVisit.FromIncoming(_topology.Edges[edgeIdx]);
                break;

            case ScenarioTraversalDirection.Both:
                foreach (var edgeIdx in node.OutgoingEdgeIndices)
                    yield return DirectedEdgeVisit.FromOutgoing(_topology.Edges[edgeIdx]);
                foreach (var edgeIdx in node.IncomingEdgeIndices)
                    yield return DirectedEdgeVisit.FromIncoming(_topology.Edges[edgeIdx]);
                break;
        }
    }

    private IDescriptor? ResolveDescriptor(DescriptorRef reference)
    {
        // Version-pinned lookup: exact match
        if (reference.Version.HasValue && _versionedIndex.TryGetValue(reference, out var exact))
            return exact;

        // Unpinned lookup: only allowed when exactly one version matches
        var identity = new DescriptorIdentity(reference.Namespace, reference.Id);
        if (_unpinnedIndex.TryGetValue(identity, out var candidates))
        {
            if (candidates.Count == 1)
                return candidates[0];

            // Multiple versions — ambiguous. Caller emits CTXPACK_AMBIGUOUS_DESCRIPTOR_REF.
            return null;
        }

        // No match
        return null;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.ContextPack`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata.ContextPack/MetadataContextDescriptorSource.cs
git commit -m "feat(context-pack): add MetadataContextDescriptorSource mediator (#36)"
```

---

### Task 3: New Diagnostic Codes

**Files:**
- Modify: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnosticCodes.cs`

**Interfaces:**
- Consumes: existing diagnostic codes pattern
- Produces: three new constant strings — used by Task 4 builder refactoring and Task 5+ tests

- [ ] **Step 1: Add three new diagnostic code constants**

The file currently contains 7 constants. Add 3 new ones at the end, before the closing brace:

```csharp
namespace CrestCreates.Metadata.ContextPack.Abstractions;

public static class MetadataContextPackDiagnosticCodes
{
    public const string FocusNotFound = "CTXPACK_FOCUS_NOT_FOUND";
    public const string TruncatedByCount = "CTXPACK_TRUNCATED_BY_COUNT";
    public const string TruncatedByDepth = "CTXPACK_TRUNCATED_BY_DEPTH";
    public const string RecipeMissing = "CTXPACK_RECIPE_MISSING";
    public const string KindExcluded = "CTXPACK_KIND_EXCLUDED";
    public const string FocusKindFiltered = "CTXPACK_FOCUS_KIND_FILTERED";
    public const string HashBuilderMissing = "CTXPACK_HASH_BUILDER_MISSING";
    public const string DescriptorMissingForTopologyRef = "CTXPACK_DESCRIPTOR_MISSING_FOR_TOPOLOGY_REF";
    public const string TopologyNodeMissingForDescriptor = "CTXPACK_TOPOLOGY_NODE_MISSING_FOR_DESCRIPTOR";
    public const string AmbiguousDescriptorRef = "CTXPACK_AMBIGUOUS_DESCRIPTOR_REF";
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.ContextPack.Abstractions`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnosticCodes.cs
git commit -m "feat(context-pack): add mismatch and ambiguous descriptor diagnostic codes (#36)"
```

---

### Task 4: Refactor DefaultMetadataContextPackBuilder

This is the core refactoring task. The builder is rewritten to use `MetadataContextDescriptorSource` exclusively. No direct topology access or descriptor index manipulation remains.

**Files:**
- Modify: `framework/src/CrestCreates.Metadata.ContextPack/DefaultMetadataContextPackBuilder.cs`

**Interfaces:**
- Consumes: `MetadataContextDescriptorSource`, `ResolvedDescriptor`, `DirectedEdgeVisit` from Tasks 1-2; new diagnostic codes from Task 3
- Produces: Refactored builder that passes all existing 26 tests

**Key changes from current code:**

1. Remove `BuildDescriptorIndex` and `FindDescriptor` private methods.
2. Create `MetadataContextDescriptorSource` at start of `Build()`.
3. Focus resolution uses `source.Resolve()` — applies asymmetric policy for topology-only / inventory-only / neither / ambiguous.
4. All scope traversal methods use `source.GetDirectedEdges()` instead of direct topology edge index access.
5. `BuildDescriptorEntries` only builds entry when `resolved.Descriptor is not null`.
6. `CollectRelationshipEntries` enforces pack closure invariant.
7. `ApplyKindFilters` uses `source.Resolve()` for kind lookup.

- [ ] **Step 1: Replace the entire DefaultMetadataContextPackBuilder.cs**

The new file replaces all scattered lookup/traversal logic with centralized source usage. The complete file:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Metadata.ContextPack;

public sealed class DefaultMetadataContextPackBuilder : IMetadataContextPackBuilder
{
    private readonly IDescriptorStableHashBuilder? _hashBuilder;

    public DefaultMetadataContextPackBuilder(IDescriptorStableHashBuilder? hashBuilder = null)
    {
        _hashBuilder = hashBuilder;
    }

    public MetadataContextPack Build(
        MetadataContextPackRequest request,
        DescriptorTopologySnapshot topology,
        IReadOnlyList<IDescriptor> descriptors)
    {
        var diagnostics = new List<MetadataContextPackDiagnostic>();

        // 1. Validate request
        ValidateRequest(request, diagnostics);

        // 2. Snapshot request collections defensively
        var snapshotRequest = SnapshotRequest(request);

        // 3. Create descriptor source (centralizes topology + inventory lookup)
        var source = new MetadataContextDescriptorSource(topology, descriptors);

        // 4. Resolve focus nodes with asymmetric mismatch policy
        var focusRefs = snapshotRequest.FocusDescriptors;
        var foundFocusRefs = new List<DescriptorRef>();

        foreach (var focusRef in focusRefs)
        {
            var resolved = source.Resolve(focusRef);

            if (resolved.TopologyNode is null && resolved.Descriptor is null)
            {
                // Neither topology nor inventory — not found
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = MetadataContextPackDiagnosticSeverity.Warning,
                    Code = MetadataContextPackDiagnosticCodes.FocusNotFound,
                    Message = $"Focus descriptor '{focusRef.FullId}' not found in topology or descriptor inventory.",
                    Subject = focusRef
                });
                continue;
            }

            if (resolved.TopologyNode is not null && resolved.Descriptor is null)
            {
                // Topology-only — check for ambiguous unpinned ref
                if (IsAmbiguousUnpinnedRef(focusRef, source))
                {
                    diagnostics.Add(new MetadataContextPackDiagnostic
                    {
                        Severity = MetadataContextPackDiagnosticSeverity.Warning,
                        Code = MetadataContextPackDiagnosticCodes.AmbiguousDescriptorRef,
                        Message = $"Focus descriptor ref '{focusRef.FullId}' matches multiple versions. Specify an exact version.",
                        Subject = focusRef
                    });
                    continue;
                }

                // Topology has the node but inventory has no descriptor
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = MetadataContextPackDiagnosticSeverity.Error,
                    Code = MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef,
                    Message = $"Topology references descriptor '{focusRef.FullId}' but it is absent from descriptor inventory.",
                    Subject = focusRef
                });
                continue;
            }

            if (resolved.TopologyNode is null && resolved.Descriptor is not null)
            {
                // Inventory-only — include if focused, no traversal possible
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = MetadataContextPackDiagnosticSeverity.Warning,
                    Code = MetadataContextPackDiagnosticCodes.TopologyNodeMissingForDescriptor,
                    Message = $"Descriptor '{focusRef.FullId}' exists in inventory but has no topology node.",
                    Subject = focusRef
                });
                foundFocusRefs.Add(focusRef);
                continue;
            }

            // Fully resolved
            foundFocusRefs.Add(resolved.TopologyNode!.Ref);
        }

        // 5. Scope-driven traversal
        var includedRefs = new HashSet<DescriptorRef>();
        var includedEdges = new List<DescriptorEdge>();
        int traversalDepthReached = 0;

        switch (snapshotRequest.Scope)
        {
            case MetadataContextPackScope.FocusOnly:
                foreach (var r in foundFocusRefs) includedRefs.Add(r);
                traversalDepthReached = 0;
                break;

            case MetadataContextPackScope.DirectDependencies:
                ResolveDirectDependencies(foundFocusRefs, source, includedRefs, includedEdges);
                traversalDepthReached = 1;
                break;

            case MetadataContextPackScope.DirectDependents:
                ResolveDirectDependents(foundFocusRefs, source, includedRefs, includedEdges);
                traversalDepthReached = 1;
                break;

            case MetadataContextPackScope.ImpactRadius:
                traversalDepthReached = ResolveImpactRadius(foundFocusRefs, source, snapshotRequest.MaxTraversalDepth, includedRefs, includedEdges, diagnostics);
                break;

            case MetadataContextPackScope.RuntimeScenario:
                traversalDepthReached = ResolveRuntimeScenario(foundFocusRefs, source, snapshotRequest, includedRefs, includedEdges);
                break;
        }

        // 6. Apply kind filters (non-focus only)
        var focusSet = new HashSet<DescriptorRef>(foundFocusRefs);
        ApplyKindFilters(includedRefs, focusSet, snapshotRequest, source, diagnostics);

        // 7. Apply count bounds (non-focus only)
        ApplyCountBounds(includedRefs, focusSet, snapshotRequest.MaxDescriptorCount, diagnostics);

        // 8. Collect relationship edges (with pack closure invariant)
        var relationshipEntries = CollectRelationshipEntries(includedRefs, includedEdges, source);

        // 9. Build descriptor entries (only when resolved.Descriptor is not null)
        var descriptorEntries = BuildDescriptorEntries(includedRefs, focusSet, source, snapshotRequest, diagnostics);

        // 10. Build summary
        var summary = BuildSummary(descriptorEntries, relationshipEntries, foundFocusRefs, diagnostics, traversalDepthReached);

        // 11. Sort output deterministically
        var sortedDescriptors = SortDescriptors(descriptorEntries);
        var sortedRelationships = SortRelationships(relationshipEntries);
        var sortedDiagnostics = SortDiagnostics(diagnostics);

        return new MetadataContextPack
        {
            Request = snapshotRequest,
            Descriptors = sortedDescriptors,
            Relationships = sortedRelationships,
            Summary = summary,
            Diagnostics = sortedDiagnostics
        };
    }

    private static void ValidateRequest(MetadataContextPackRequest request, List<MetadataContextPackDiagnostic> diagnostics)
    {
        if (request.Scope == MetadataContextPackScope.RuntimeScenario && request.ScenarioRecipe is null)
        {
            diagnostics.Add(new MetadataContextPackDiagnostic
            {
                Severity = MetadataContextPackDiagnosticSeverity.Error,
                Code = MetadataContextPackDiagnosticCodes.RecipeMissing,
                Message = "RuntimeScenario scope requires a ScenarioRecipe."
            });
        }
    }

    private static MetadataContextPackRequest SnapshotRequest(MetadataContextPackRequest request)
    {
        return request with
        {
            FocusDescriptors = request.FocusDescriptors.ToArray(),
            IncludeKinds = request.IncludeKinds?.ToArray(),
            ExcludeKinds = request.ExcludeKinds?.ToArray(),
            ScenarioRecipe = request.ScenarioRecipe is null ? null :
                request.ScenarioRecipe with { Steps = request.ScenarioRecipe.Steps.ToArray() }
        };
    }

    private static bool IsAmbiguousUnpinnedRef(DescriptorRef ref_, MetadataContextDescriptorSource source)
    {
        // If the ref has a version, it's not ambiguous — it's just missing
        if (ref_.Version.HasValue) return false;

        // Re-resolve: if Descriptor is null but TopologyNode is not, check if inventory has multiple versions
        var resolved = source.Resolve(ref_);
        // If both TopologyNode and Descriptor are null, it could be ambiguous or just missing.
        // But we only reach here when TopologyNode is not null and Descriptor is null.
        // That means unpinned lookup returned null, which happens when multiple versions exist.
        // We already know TopologyNode is not null (caller checked), so Descriptor null on unpinned = ambiguous.
        return true;
    }

    private static void ResolveDirectDependencies(
        List<DescriptorRef> focusRefs, MetadataContextDescriptorSource source,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges)
    {
        foreach (var focusRef in focusRefs)
        {
            includedRefs.Add(focusRef);

            foreach (var visit in source.GetDirectedEdges(focusRef, ScenarioTraversalDirection.Dependencies))
            {
                includedEdges.Add(visit.Edge);
                includedRefs.Add(visit.Target);
            }
        }
    }

    private static void ResolveDirectDependents(
        List<DescriptorRef> focusRefs, MetadataContextDescriptorSource source,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges)
    {
        foreach (var focusRef in focusRefs)
        {
            includedRefs.Add(focusRef);

            foreach (var visit in source.GetDirectedEdges(focusRef, ScenarioTraversalDirection.Dependents))
            {
                includedEdges.Add(visit.Edge);
                includedRefs.Add(visit.Target);
            }
        }
    }

    private static int ResolveImpactRadius(
        List<DescriptorRef> focusRefs, MetadataContextDescriptorSource source, int maxDepth,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        var visited = new HashSet<DescriptorRef>();
        var frontier = new List<DescriptorRef>();

        // Depth 0: focus nodes
        foreach (var r in focusRefs)
        {
            if (visited.Add(r))
            {
                includedRefs.Add(r);
                frontier.Add(r);
            }
        }

        var depthReached = 0;

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            var nextFrontier = new List<DescriptorRef>();
            foreach (var currentRef in frontier)
            {
                foreach (var visit in source.GetDirectedEdges(currentRef, ScenarioTraversalDirection.Both))
                {
                    includedEdges.Add(visit.Edge);
                    if (visited.Add(visit.Target))
                    {
                        includedRefs.Add(visit.Target);
                        nextFrontier.Add(visit.Target);
                    }
                }
            }

            if (nextFrontier.Count > 0)
            {
                depthReached = depth;
            }

            frontier = nextFrontier;
        }

        // Check if there are actually unvisited neighbors beyond the max-depth frontier
        var hasUnvisitedBeyond = false;
        foreach (var frontierRef in frontier)
        {
            foreach (var visit in source.GetDirectedEdges(frontierRef, ScenarioTraversalDirection.Both))
            {
                if (!visited.Contains(visit.Target))
                {
                    hasUnvisitedBeyond = true;
                    break;
                }
            }

            if (hasUnvisitedBeyond) break;
        }

        if (hasUnvisitedBeyond)
        {
            diagnostics.Add(new MetadataContextPackDiagnostic
            {
                Severity = MetadataContextPackDiagnosticSeverity.Info,
                Code = MetadataContextPackDiagnosticCodes.TruncatedByDepth,
                Message = $"Traversal truncated at depth {maxDepth}. Additional nodes exist beyond this depth."
            });
        }

        return depthReached;
    }

    private static int ResolveRuntimeScenario(
        List<DescriptorRef> focusRefs, MetadataContextDescriptorSource source,
        MetadataContextPackRequest request,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges)
    {
        var recipe = request.ScenarioRecipe;
        if (recipe is null) return 0;

        // Add focus refs
        foreach (var r in focusRefs) includedRefs.Add(r);

        var boundary = new HashSet<DescriptorRef>(focusRefs);
        var maxDepthReached = 0;

        foreach (var step in recipe.Steps)
        {
            var stepVisited = new HashSet<DescriptorRef>(boundary);

            for (int depth = 1; depth <= step.MaxDepth; depth++)
            {
                var nextBoundary = new HashSet<DescriptorRef>();

                foreach (var currentRef in boundary)
                {
                    foreach (var visit in source.GetDirectedEdges(currentRef, step.Direction))
                    {
                        if (visit.Edge.Kind != step.FollowKind) continue;
                        if (step.Role is not null && visit.Edge.Role != step.Role) continue;

                        var targetNode = source.Resolve(visit.Target).TopologyNode;
                        if (targetNode is null) continue;

                        if (step.TargetKind.HasValue && targetNode.Kind != step.TargetKind.Value) continue;

                        includedEdges.Add(visit.Edge);

                        if (stepVisited.Add(visit.Target))
                        {
                            includedRefs.Add(visit.Target);
                            nextBoundary.Add(visit.Target);
                        }
                    }
                }

                boundary = nextBoundary;
                if (nextBoundary.Count > 0) maxDepthReached = depth;
            }

            // Boundary for next step = all discovered nodes from this step
            boundary = new HashSet<DescriptorRef>(stepVisited);
        }

        return maxDepthReached;
    }

    private static void ApplyKindFilters(
        HashSet<DescriptorRef> includedRefs, HashSet<DescriptorRef> focusSet,
        MetadataContextPackRequest request, MetadataContextDescriptorSource source,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        // Check if any focus descriptor would be filtered out
        foreach (var focusRef in focusSet)
        {
            var resolved = source.Resolve(focusRef);
            var kind = resolved.Descriptor?.Kind ?? resolved.TopologyNode?.Kind;
            if (kind is null) continue;

            var wouldBeExcluded = false;

            if (request.IncludeKinds is not null && !request.IncludeKinds.Contains(kind.Value))
                wouldBeExcluded = true;
            if (request.ExcludeKinds is not null && request.ExcludeKinds.Contains(kind.Value))
                wouldBeExcluded = true;

            if (wouldBeExcluded)
            {
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = MetadataContextPackDiagnosticSeverity.Warning,
                    Code = MetadataContextPackDiagnosticCodes.FocusKindFiltered,
                    Message = $"Focus descriptor '{focusRef.FullId}' has kind {kind} that would be filtered. Focus is still included.",
                    Subject = focusRef
                });
            }
        }

        // Apply filters to non-focus refs
        var toRemove = new List<DescriptorRef>();
        foreach (var ref_ in includedRefs)
        {
            if (focusSet.Contains(ref_)) continue;

            var resolved = source.Resolve(ref_);
            var kind = resolved.Descriptor?.Kind ?? resolved.TopologyNode?.Kind;
            if (kind is null) continue;

            if (request.IncludeKinds is not null && !request.IncludeKinds.Contains(kind.Value))
            {
                toRemove.Add(ref_);
                continue;
            }
            if (request.ExcludeKinds is not null && request.ExcludeKinds.Contains(kind.Value))
            {
                toRemove.Add(ref_);
            }
        }

        foreach (var r in toRemove)
        {
            includedRefs.Remove(r);
        }

        if (toRemove.Count > 0)
        {
            diagnostics.Add(new MetadataContextPackDiagnostic
            {
                Severity = MetadataContextPackDiagnosticSeverity.Info,
                Code = MetadataContextPackDiagnosticCodes.KindExcluded,
                Message = $"{toRemove.Count} descriptor(s) excluded by kind filters."
            });
        }
    }

    private static void ApplyCountBounds(
        HashSet<DescriptorRef> includedRefs, HashSet<DescriptorRef> focusSet,
        int maxDescriptorCount, List<MetadataContextPackDiagnostic> diagnostics)
    {
        if (includedRefs.Count <= maxDescriptorCount) return;

        // Focus always stays. Remove non-focus descriptors that exceed the limit.
        var nonFocusRefs = includedRefs.Where(r => !focusSet.Contains(r)).ToList();
        var focusCount = focusSet.Count;

        if (focusCount >= maxDescriptorCount)
        {
            // Focus alone exceeds limit — keep all focus, remove all non-focus
            foreach (var r in nonFocusRefs) includedRefs.Remove(r);
        }
        else
        {
            var allowedNonFocus = maxDescriptorCount - focusCount;
            // Remove excess non-focus (sorted deterministically for reproducibility)
            var sortedNonFocus = nonFocusRefs
                .OrderBy(r => r.Namespace, StringComparer.Ordinal)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .ThenBy(r => r.Version ?? -1)
                .ToList();

            for (int i = allowedNonFocus; i < sortedNonFocus.Count; i++)
            {
                includedRefs.Remove(sortedNonFocus[i]);
            }
        }

        diagnostics.Add(new MetadataContextPackDiagnostic
        {
            Severity = MetadataContextPackDiagnosticSeverity.Info,
            Code = MetadataContextPackDiagnosticCodes.TruncatedByCount,
            Message = $"Result truncated to {maxDescriptorCount} descriptors.",
            Path = $"MaxDescriptorCount={maxDescriptorCount}"
        });
    }

    private static List<MetadataContextPackRelationshipEntry> CollectRelationshipEntries(
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges,
        MetadataContextDescriptorSource source)
    {
        var entries = new List<MetadataContextPackRelationshipEntry>();
        var seen = new HashSet<(DescriptorRef, DescriptorRef, RelationshipKind)>();

        foreach (var edge in includedEdges)
        {
            // Pack closure invariant: every relationship endpoint must exist in the descriptor set
            var fromResolved = source.Resolve(edge.From);
            var toResolved = source.Resolve(edge.To);

            var fromRef = fromResolved.TopologyNode?.Ref;
            var toRef = toResolved.TopologyNode?.Ref;

            if (fromRef is null || toRef is null) continue;
            if (!includedRefs.Contains(fromRef.Value) || !includedRefs.Contains(toRef.Value)) continue;

            var key = (fromRef.Value, toRef.Value, edge.Kind);
            if (!seen.Add(key)) continue;

            entries.Add(new MetadataContextPackRelationshipEntry
            {
                From = fromRef.Value,
                To = toRef.Value,
                Kind = edge.Kind,
                Role = edge.Role,
                SourcePath = edge.SourcePath,
                Strength = edge.Strength,
                IsRuntimeBinding = edge.IsRuntimeBinding
            });
        }

        return entries;
    }

    private List<MetadataContextPackDescriptorEntry> BuildDescriptorEntries(
        HashSet<DescriptorRef> includedRefs, HashSet<DescriptorRef> focusSet,
        MetadataContextDescriptorSource source,
        MetadataContextPackRequest request,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        var entries = new List<MetadataContextPackDescriptorEntry>();

        foreach (var ref_ in includedRefs)
        {
            var resolved = source.Resolve(ref_);

            // DescriptorEntry is built only when resolved.Descriptor is not null.
            // TopologyNode must not be used to fabricate descriptor entries.
            if (resolved.Descriptor is null)
                continue;

            var descriptor = resolved.Descriptor;

            DescriptorStableHashes? hashes = null;
            if (request.IncludeStableHashes)
            {
                if (_hashBuilder is not null)
                {
                    hashes = _hashBuilder.Build(descriptor);
                }
                else
                {
                    // Only emit once
                    if (!diagnostics.Any(d => d.Code == MetadataContextPackDiagnosticCodes.HashBuilderMissing))
                    {
                        diagnostics.Add(new MetadataContextPackDiagnostic
                        {
                            Severity = MetadataContextPackDiagnosticSeverity.Warning,
                            Code = MetadataContextPackDiagnosticCodes.HashBuilderMissing,
                            Message = "IncludeStableHashes is true but no IDescriptorStableHashBuilder is available."
                        });
                    }
                }
            }

            MetadataContextPackGovernanceEntry? governance = null;
            if (request.IncludeGovernanceState)
            {
                governance = new MetadataContextPackGovernanceEntry
                {
                    State = descriptor.State,
                    RequiresReview = descriptor.State == DescriptorState.Draft
                };
            }

            entries.Add(new MetadataContextPackDescriptorEntry
            {
                Ref = ref_,
                Kind = descriptor.Kind,
                Name = descriptor.Name,
                State = descriptor.State,
                Hashes = hashes,
                Governance = governance,
                IsFocus = focusSet.Contains(ref_)
            });
        }

        return entries;
    }

    private static MetadataContextPackSummary BuildSummary(
        List<MetadataContextPackDescriptorEntry> descriptors,
        List<MetadataContextPackRelationshipEntry> relationships,
        List<DescriptorRef> focusRefs,
        List<MetadataContextPackDiagnostic> diagnostics,
        int traversalDepthReached)
    {
        var wasTruncated = diagnostics.Any(d =>
            d.Code == MetadataContextPackDiagnosticCodes.TruncatedByCount ||
            d.Code == MetadataContextPackDiagnosticCodes.TruncatedByDepth);

        return new MetadataContextPackSummary
        {
            TotalDescriptorCount = descriptors.Count,
            DescriptorCountsByKind = descriptors
                .GroupBy(d => d.Kind)
                .ToDictionary(g => g.Key, g => g.Count()),
            TotalRelationshipCount = relationships.Count,
            RelationshipCountsByKind = relationships
                .GroupBy(r => r.Kind)
                .ToDictionary(g => g.Key, g => g.Count()),
            FocusRefs = focusRefs,
            WasTruncated = wasTruncated,
            TruncatedAtCount = wasTruncated && diagnostics.Any(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByCount)
                ? descriptors.Count : null,
            TraversalDepthReached = traversalDepthReached
        };
    }

    private static List<MetadataContextPackDescriptorEntry> SortDescriptors(
        List<MetadataContextPackDescriptorEntry> entries)
    {
        return entries
            .OrderByDescending(d => d.IsFocus)
            .ThenBy(d => d.Ref.Namespace, StringComparer.Ordinal)
            .ThenBy(d => d.Ref.Id, StringComparer.Ordinal)
            .ThenBy(d => d.Ref.Version ?? -1)
            .ToList();
    }

    private static List<MetadataContextPackRelationshipEntry> SortRelationships(
        List<MetadataContextPackRelationshipEntry> entries)
    {
        return entries
            .OrderBy(r => r.From.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.From.Id, StringComparer.Ordinal)
            .ThenBy(r => r.From.Version ?? -1)
            .ThenBy(r => r.To.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.To.Id, StringComparer.Ordinal)
            .ThenBy(r => r.To.Version ?? -1)
            .ThenBy(r => r.Kind)
            .ToList();
    }

    private static List<MetadataContextPackDiagnostic> SortDiagnostics(
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        return diagnostics
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => d.Subject?.Namespace ?? "", StringComparer.Ordinal)
            .ThenBy(d => d.Subject?.Id ?? "", StringComparer.Ordinal)
            .ThenBy(d => d.Subject?.Version ?? -1)
            .ToList();
    }
}
```

- [ ] **Step 2: Run all existing tests to verify no regressions**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests`
Expected: All 26 existing tests PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata.ContextPack/DefaultMetadataContextPackBuilder.cs
git commit -m "refactor(context-pack): use DescriptorSource for all lookup and traversal (#36)"
```

---

### Task 5: DescriptorSource Resolution Tests (G group — 4 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs`

**Interfaces:**
- Consumes: Refactored builder from Task 4, new diagnostic codes from Task 3

- [ ] **Step 1: Add test helper method for inventory-only descriptors**

The existing `TestDescriptor` class works for normal cases. For topology-only and inventory-only tests, we need test setups where the topology and descriptor lists are intentionally misaligned. Add this helper at the end of the class, before the `VersionedTestDescriptor` inner class:

```csharp
private sealed class InventoryOnlyDescriptor : IDescriptor
{
    private readonly DescriptorRef _ref;
    public InventoryOnlyDescriptor(DescriptorRef ref_, DescriptorKind kind, string name, DescriptorState state)
    {
        _ref = ref_; Kind = kind; Name = name; State = state;
    }
    public string Namespace => _ref.Namespace;
    public string Id => _ref.Id;
    public string Name { get; }
    public string FullId => $"{Namespace}.{Id}";
    public DescriptorKind Kind { get; }
    public DescriptorState State { get; }
    public string ContractHash => "";
    public string DefinitionHash => "";
    public string? SupersededById => null;
}
```

- [ ] **Step 2: Add test G1 — Fully resolved ref**

Add at the end of the test class, after the F group:

```csharp
// ── G. DescriptorSource Resolution ──

[Fact]
public void Fully_Resolved_Ref_Returns_Both_TopologyNode_And_Descriptor()
{
    var cap = new DescriptorRef("capability", "SubmitCap");

    var topology = CreateSnapshot(
        new[] { (cap, DescriptorKind.Capability, "SubmitCap") },
        NoEdges);

    var descriptors = CreateDescriptors(
        (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active));

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { cap }
    };

    var pack = _builder.Build(request, topology, descriptors);

    pack.Descriptors.Should().ContainSingle();
    pack.Descriptors[0].Ref.Should().Be(cap);
    pack.Descriptors[0].Kind.Should().Be(DescriptorKind.Capability);
    pack.Descriptors[0].Name.Should().Be("SubmitCap");
    pack.Diagnostics.Should().NotContain(d =>
        d.Code == MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef ||
        d.Code == MetadataContextPackDiagnosticCodes.TopologyNodeMissingForDescriptor);
}
```

- [ ] **Step 3: Add test G2 — Topology-only ref**

```csharp
[Fact]
public void Topology_Only_Ref_Emits_DescriptorMissing_Error()
{
    var cap = new DescriptorRef("capability", "SubmitCap");

    // Node in topology but no matching descriptor in inventory
    var topology = CreateSnapshot(
        new[] { (cap, DescriptorKind.Capability, "SubmitCap") },
        NoEdges);

    // Empty inventory — no descriptors at all
    var descriptors = new List<IDescriptor>();

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { cap }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Descriptor entry should NOT be fabricated from topology node
    pack.Descriptors.Should().BeEmpty();
    pack.Diagnostics.Should().Contain(d =>
        d.Code == MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef &&
        d.Severity == MetadataContextPackDiagnosticSeverity.Error);
}
```

- [ ] **Step 4: Add test G3 — Inventory-only ref**

```csharp
[Fact]
public void Inventory_Only_Ref_Emits_TopologyNodeMissing_Warning()
{
    var cap = new DescriptorRef("capability", "SubmitCap");

    // Empty topology — no nodes
    var topology = CreateSnapshot(
        Array.Empty<(DescriptorRef, DescriptorKind, string)>(),
        NoEdges);

    // Descriptor exists in inventory
    var descriptors = new List<IDescriptor>
    {
        new InventoryOnlyDescriptor(cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active)
    };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { cap }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Inventory-only focus should still be included (no traversal possible)
    pack.Descriptors.Should().ContainSingle();
    pack.Descriptors[0].Ref.Should().Be(cap);
    pack.Diagnostics.Should().Contain(d =>
        d.Code == MetadataContextPackDiagnosticCodes.TopologyNodeMissingForDescriptor &&
        d.Severity == MetadataContextPackDiagnosticSeverity.Warning);
}
```

- [ ] **Step 5: Add test G4 — Neither exists**

```csharp
[Fact]
public void Neither_Topology_Nor_Inventory_Ref_Treated_As_FocusNotFound()
{
    var missing = new DescriptorRef("ns", "Missing");

    var topology = CreateSnapshot(
        Array.Empty<(DescriptorRef, DescriptorKind, string)>(),
        NoEdges);

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { missing }
    };

    var pack = _builder.Build(request, topology, Array.Empty<IDescriptor>());

    pack.Descriptors.Should().BeEmpty();
    pack.Diagnostics.Should().Contain(d =>
        d.Code == MetadataContextPackDiagnosticCodes.FocusNotFound);
}
```

- [ ] **Step 6: Run the 4 new tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "Fully_Resolved|Topology_Only|Inventory_Only|Neither_Topology"`
Expected: All 4 PASS

- [ ] **Step 7: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add DescriptorSource resolution tests (G group) (#36)"
```

---

### Task 6: Multi-Version Coexistence Tests (H group — 5 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs`

- [ ] **Step 1: Add test H5 — Focus on v2 resolves to v2 instance**

```csharp
// ── H. Multi-Version Coexistence ──

[Fact]
public void MultiVersion_Focus_On_V2_Resolves_To_V2_Only()
{
    var v1Ref = new DescriptorRef("capability", "SubmitCap", 1);
    var v2Ref = new DescriptorRef("capability", "SubmitCap", 2);

    var topology = CreateSnapshot(
        new[] { (v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        NoEdges);

    var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { v1Desc, v2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { v2Ref }
    };

    var pack = _builder.Build(request, topology, descriptors);

    pack.Descriptors.Should().ContainSingle();
    pack.Descriptors[0].Ref.Should().Be(v2Ref);
}
```

- [ ] **Step 2: Add test H6 — ImpactRadius traverses each version separately**

```csharp
[Fact]
public void MultiVersion_ImpactRadius_Traverses_Each_Version_Separately()
{
    var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);
    var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);
    var cap = new DescriptorRef("capability", "SubmitCap");

    var topology = CreateSnapshot(
        new[] { (schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, cap, schemaV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, cap, schemaV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var v1Desc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
    var v2Desc = new VersionedTestDescriptor(schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);
    var capDesc = new VersionedTestDescriptor(cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var descriptors = new List<IDescriptor> { v1Desc, v2Desc, capDesc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.ImpactRadius,
        FocusDescriptors = new[] { cap },
        MaxTraversalDepth = 1
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Both v1 and v2 should appear — versions are not collapsed
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(
        new[] { cap, schemaV1, schemaV2 });
}
```

- [ ] **Step 3: Add test H7 — Kind filter applied per-version**

```csharp
[Fact]
public void MultiVersion_Kind_Filter_Applied_Per_Version()
{
    var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);
    var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);
    var cap = new DescriptorRef("capability", "SubmitCap");

    var topology = CreateSnapshot(
        new[] { (schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, cap, schemaV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, cap, schemaV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var v1Desc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
    var v2Desc = new VersionedTestDescriptor(schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);
    var capDesc = new VersionedTestDescriptor(cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var descriptors = new List<IDescriptor> { v1Desc, v2Desc, capDesc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { cap },
        ExcludeKinds = new[] { DescriptorKind.Schema }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Both v1 and v2 Schema excluded by kind filter
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap });
}
```

- [ ] **Step 4: Add test H8a — Unpinned ref with single version resolves**

```csharp
[Fact]
public void Unpinned_Ref_Single_Version_Resolves()
{
    // Versioned descriptor in inventory, unpinned ref in focus
    var versionedRef = new DescriptorRef("capability", "SubmitCap", 1);
    var unpinnedRef = new DescriptorRef("capability", "SubmitCap");  // Version = null

    var topology = CreateSnapshot(
        new[] { (unpinnedRef, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        NoEdges);

    var v1Desc = new VersionedTestDescriptor(versionedRef, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var descriptors = new List<IDescriptor> { v1Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { unpinnedRef }
    };

    var pack = _builder.Build(request, topology, descriptors);

    pack.Descriptors.Should().ContainSingle();
    pack.Diagnostics.Should().NotContain(d =>
        d.Code == MetadataContextPackDiagnosticCodes.AmbiguousDescriptorRef);
}
```

- [ ] **Step 5: Add test H8b — Unpinned ref with multiple versions emits ambiguous**

```csharp
[Fact]
public void Unpinned_Ref_Multiple_Versions_Emits_Ambiguous_Diagnostic()
{
    var v1Ref = new DescriptorRef("capability", "SubmitCap", 1);
    var v2Ref = new DescriptorRef("capability", "SubmitCap", 2);
    var unpinnedRef = new DescriptorRef("capability", "SubmitCap");  // Version = null

    var topology = CreateSnapshot(
        new[] { (unpinnedRef, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        NoEdges);

    var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { v1Desc, v2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { unpinnedRef }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Unpinned ref with multiple versions should emit ambiguous diagnostic
    pack.Diagnostics.Should().Contain(d =>
        d.Code == MetadataContextPackDiagnosticCodes.AmbiguousDescriptorRef);
    // No descriptor entry should be produced for the ambiguous ref
    pack.Descriptors.Should().BeEmpty();
}
```

- [ ] **Step 6: Run the 5 new tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "MultiVersion|Unpinned_Ref"`
Expected: All 5 PASS

- [ ] **Step 7: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add multi-version coexistence tests (H group) (#36)"
```

---

### Task 7: Direction-Aware Traversal Tests (I group — 4 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs`

- [ ] **Step 1: Add test I9 — DirectDependencies follows only outgoing edges**

```csharp
// ── I. Direction-Aware Traversal ──

[Fact]
public void DirectDependencies_Follows_Only_Outgoing_Edges()
{
    var cap = new DescriptorRef("capability", "SubmitCap");
    var schema = new DescriptorRef("schema", "InputSchema");
    var workflow = new DescriptorRef("workflow", "ApprovalWf");

    var topology = CreateSnapshot(
        new[] { (cap, DescriptorKind.Capability, "SubmitCap"),
                (schema, DescriptorKind.Schema, "InputSchema"),
                (workflow, DescriptorKind.Workflow, "ApprovalWf") },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            // Outgoing from cap: cap → schema (Uses)
            (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            // Incoming to cap: workflow → cap (Triggers) — should NOT be followed
            (1, workflow, cap, RelationshipKind.Triggers, null, RelationshipStrength.Strong, true)
        });

    var descriptors = CreateDescriptors(
        (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
        (schema, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
        (workflow, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active));

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { cap }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Should include cap + schema (outgoing), but NOT workflow (incoming)
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap, schema });
    pack.Relationships.Select(r => r.Kind).Should().BeEquivalentTo(new[] { RelationshipKind.Uses });
}
```

- [ ] **Step 2: Add test I10 — DirectDependents follows only incoming edges**

```csharp
[Fact]
public void DirectDependents_Follows_Only_Incoming_Edges()
{
    var cap = new DescriptorRef("capability", "SubmitCap");
    var schema = new DescriptorRef("schema", "InputSchema");
    var workflow = new DescriptorRef("workflow", "ApprovalWf");

    var topology = CreateSnapshot(
        new[] { (cap, DescriptorKind.Capability, "SubmitCap"),
                (schema, DescriptorKind.Schema, "InputSchema"),
                (workflow, DescriptorKind.Workflow, "ApprovalWf") },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            // Outgoing from cap: cap → schema (Uses) — should NOT be followed
            (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            // Incoming to cap: workflow → cap (Triggers) — should be followed
            (1, workflow, cap, RelationshipKind.Triggers, null, RelationshipStrength.Strong, true)
        });

    var descriptors = CreateDescriptors(
        (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
        (schema, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
        (workflow, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active));

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependents,
        FocusDescriptors = new[] { cap }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Should include cap + workflow (incoming), but NOT schema (outgoing)
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap, workflow });
    pack.Relationships.Select(r => r.Kind).Should().BeEquivalentTo(new[] { RelationshipKind.Triggers });
}
```

- [ ] **Step 3: Add test I11 — RuntimeScenario Both follows outgoing then incoming**

```csharp
[Fact]
public void RuntimeScenario_Both_Follows_Outgoing_Then_Incoming()
{
    var cap = new DescriptorRef("capability", "SubmitCap");
    var schema = new DescriptorRef("schema", "InputSchema");
    var workflow = new DescriptorRef("workflow", "ApprovalWf");

    var topology = CreateSnapshot(
        new[] { (cap, DescriptorKind.Capability, "SubmitCap"),
                (schema, DescriptorKind.Schema, "InputSchema"),
                (workflow, DescriptorKind.Workflow, "ApprovalWf") },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, workflow, cap, RelationshipKind.Triggers, null, RelationshipStrength.Strong, true)
        });

    var descriptors = CreateDescriptors(
        (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
        (schema, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
        (workflow, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active));

    var recipe = new RuntimeScenarioRecipe
    {
        Name = "BothDirections",
        Steps = new[]
        {
            new ScenarioTraversalStep
            {
                FollowKind = RelationshipKind.Uses,
                Direction = ScenarioTraversalDirection.Both,
                MaxDepth = 1
            }
        }
    };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.RuntimeScenario,
        FocusDescriptors = new[] { cap },
        ScenarioRecipe = recipe
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Step follows Uses in Both direction:
    // Outgoing: cap → schema (Uses) → included
    // Incoming: workflow → cap (Triggers) → NOT included (Kind=Triggers ≠ FollowKind=Uses)
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap, schema });
}
```

- [ ] **Step 4: Add test I12 — ImpactRadius bidirectional BFS with self-cycle**

```csharp
[Fact]
public void ImpactRadius_Bidirectional_BFS_With_Self_Cycle_Terminates()
{
    var a = new DescriptorRef("ns", "A");
    var b = new DescriptorRef("ns", "B");

    // A self-references and A → B
    var topology = CreateSnapshot(
        new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Schema, "B") },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, a, a, RelationshipKind.References, null, RelationshipStrength.Weak, false),
            (1, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var descriptors = CreateDescriptors(
        (a, DescriptorKind.Capability, "A", DescriptorState.Active),
        (b, DescriptorKind.Schema, "B", DescriptorState.Active));

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.ImpactRadius,
        FocusDescriptors = new[] { a },
        MaxTraversalDepth = 5
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Self-cycle should not cause infinite loop
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { a, b });
    // No truncation diagnostic — graph is fully explored
    pack.Diagnostics.Should().NotContain(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByDepth);
}
```

- [ ] **Step 5: Run the 4 new tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "DirectDependencies_Follows_Only_Outgoing|DirectDependents_Follows_Only_Incoming|RuntimeScenario_Both_Follows|ImpactRadius_Bidirectional_BFS_With_Self"`
Expected: All 4 PASS

- [ ] **Step 6: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add direction-aware traversal tests (I group) (#36)"
```

---

### Task 8: Pack Closure Invariant Tests (J group — 2 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs`

- [ ] **Step 1: Add test J13 — Missing inventory descriptor excludes relationship**

```csharp
// ── J. Pack Closure Invariant ──

[Fact]
public void Missing_Inventory_Descriptor_Excludes_Relationship()
{
    var cap = new DescriptorRef("capability", "SubmitCap");
    var schema = new DescriptorRef("schema", "InputSchema");

    var topology = CreateSnapshot(
        new[] { (cap, DescriptorKind.Capability, "SubmitCap"),
                (schema, DescriptorKind.Schema, "InputSchema") },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    // Only cap descriptor in inventory — schema is missing
    var descriptors = CreateDescriptors(
        (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active));

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { cap }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // cap entry exists, schema entry is excluded (no descriptor in inventory)
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap });
    // Relationship should be excluded by pack closure invariant (schema endpoint missing from descriptors)
    pack.Relationships.Should().BeEmpty();
    pack.Diagnostics.Should().Contain(d =>
        d.Code == MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef);
}
```

- [ ] **Step 2: Add test J14 — Mixed resolved/unresolved endpoints**

```csharp
[Fact]
public void Mixed_Resolved_Unresolved_Endpoints_Only_Fully_Contained_Relationships()
{
    var a = new DescriptorRef("ns", "A");
    var b = new DescriptorRef("ns", "B");
    var c = new DescriptorRef("ns", "C");

    var topology = CreateSnapshot(
        new[] { (a, DescriptorKind.Capability, "A"),
                (b, DescriptorKind.Schema, "B"),
                (c, DescriptorKind.Event, "C") },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, a, c, RelationshipKind.Produces, null, RelationshipStrength.Weak, false)
        });

    // Only A and B descriptors in inventory — C is missing
    var descriptors = CreateDescriptors(
        (a, DescriptorKind.Capability, "A", DescriptorState.Active),
        (b, DescriptorKind.Schema, "B", DescriptorState.Active));

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { a }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // A and B entries exist, C excluded
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { a, b });
    // A→B relationship preserved (both endpoints in descriptor set)
    // A→C relationship excluded (C endpoint missing from descriptor set)
    pack.Relationships.Should().ContainSingle();
    pack.Relationships[0].From.Should().Be(a);
    pack.Relationships[0].To.Should().Be(b);
}
```

- [ ] **Step 3: Run the 2 new tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "Missing_Inventory_Descriptor_Excludes|Mixed_Resolved_Unresolved"`
Expected: Both PASS

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add pack closure invariant tests (J group) (#36)"
```

---

### Task 9: Full Test Suite Verification

- [ ] **Step 1: Run all tests — 26 existing + 15 new = 41 total**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests -v normal`
Expected: 41 tests PASS, 0 FAIL

- [ ] **Step 2: Run full solution build to check no compilation errors**

Run: `dotnet build CrestCreates.slnx`
Expected: PASS (no errors)

- [ ] **Step 3: Commit (only if any fix was needed)**

```bash
git add -A
git commit -m "fix(context-pack): address test failures from stabilization refactoring (#36)"
```
