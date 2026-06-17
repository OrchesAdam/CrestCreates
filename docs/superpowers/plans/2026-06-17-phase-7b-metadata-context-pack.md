# Phase 7b — Metadata Context Pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deterministic, bounded metadata context pack builder that produces topology-aware, prompt-ready context from descriptor snapshots without calling LLMs, mutating registries, or depending on Phase 6c/6d/6e pipelines.

**Architecture:** A stateless singleton builder (`DefaultMetadataContextPackBuilder`) receives a request, topology snapshot, and descriptor inventory as explicit method parameters. It performs scope-driven traversal (FocusOnly, DirectDependencies, DirectDependents, ImpactRadius, RuntimeScenario), applies filters and bounds, and returns a deterministic `MetadataContextPack`. Contracts live in a separate Abstractions project for cross-consumer reuse.

**Tech Stack:** .NET 10, xUnit 2.9.3, FluentAssertions, Moq, CrestCreates.Metadata.Abstractions (topology/descriptor/relationship types)

---

## File Structure

### New Files — Abstractions Project

| File | Responsibility |
|------|---------------|
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/CrestCreates.Metadata.ContextPack.Abstractions.csproj` | Project file |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackScope.cs` | Scope enum |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/ScenarioTraversalDirection.cs` | Traversal direction enum |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/RuntimeScenarioRecipe.cs` | Recipe record |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/ScenarioTraversalStep.cs` | Step record |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackRequest.cs` | Request record |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDescriptorEntry.cs` | Descriptor entry record |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackGovernanceEntry.cs` | Governance entry record |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackRelationshipEntry.cs` | Relationship entry record |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackSummary.cs` | Summary record |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnosticSeverity.cs` | Diagnostic severity enum |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnostic.cs` | Diagnostic record |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnosticCodes.cs` | Diagnostic code constants |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPack.cs` | Pack record |
| `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/IMetadataContextPackBuilder.cs` | Builder interface |

### New Files — Implementation Project

| File | Responsibility |
|------|---------------|
| `framework/src/CrestCreates.Metadata.ContextPack/CrestCreates.Metadata.ContextPack.csproj` | Project file |
| `framework/src/CrestCreates.Metadata.ContextPack/DefaultMetadataContextPackBuilder.cs` | Builder implementation |
| `framework/src/CrestCreates.Metadata.ContextPack/MetadataContextPackServiceCollectionExtensions.cs` | DI registration |

### New Files — Test Project

| File | Responsibility |
|------|---------------|
| `framework/test/CrestCreates.Metadata.ContextPack.Tests/CrestCreates.Metadata.ContextPack.Tests.csproj` | Project file |
| `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs` | All 25 tests |

### Modified Files

| File | Change |
|------|--------|
| `CrestCreates.slnx` | Add 3 new projects to `/src/core/` and `/test/` folders |

---

## Task 1: Create Abstractions Project — Enums and Simple Records

**Files:**
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/CrestCreates.Metadata.ContextPack.Abstractions.csproj`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackScope.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/ScenarioTraversalDirection.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnosticSeverity.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnosticCodes.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackGovernanceEntry.cs`

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Metadata.ContextPack.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="CrestCreates.Metadata.ContextPack" />
    <InternalsVisibleTo Include="CrestCreates.Metadata.ContextPack.Tests" />
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create MetadataContextPackScope.cs**

```csharp
namespace CrestCreates.Metadata.ContextPack.Abstractions;

public enum MetadataContextPackScope
{
    FocusOnly,
    DirectDependencies,
    DirectDependents,
    ImpactRadius,
    RuntimeScenario
}
```

- [ ] **Step 3: Create ScenarioTraversalDirection.cs**

```csharp
namespace CrestCreates.Metadata.ContextPack.Abstractions;

public enum ScenarioTraversalDirection
{
    Dependencies,
    Dependents,
    Both
}
```

- [ ] **Step 4: Create MetadataContextPackDiagnosticSeverity.cs**

```csharp
namespace CrestCreates.Metadata.ContextPack.Abstractions;

public enum MetadataContextPackDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
```

- [ ] **Step 5: Create MetadataContextPackDiagnosticCodes.cs**

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
}
```

- [ ] **Step 6: Create MetadataContextPackGovernanceEntry.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackGovernanceEntry
{
    public required DescriptorState State { get; init; }
    public bool RequiresReview { get; init; }
}
```

- [ ] **Step 7: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Metadata.ContextPack.Abstractions`
Expected: PASS with no errors

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.Metadata.ContextPack.Abstractions/
git commit -m "feat(context-pack): add abstractions project with enums and simple records (#14)"
```

---

## Task 2: Create Abstractions Project — Records and Interface

**Files:**
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/RuntimeScenarioRecipe.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/ScenarioTraversalStep.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackRequest.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDescriptorEntry.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackRelationshipEntry.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackSummary.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnostic.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPack.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/IMetadataContextPackBuilder.cs`

- [ ] **Step 1: Create RuntimeScenarioRecipe.cs**

```csharp
namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record RuntimeScenarioRecipe
{
    public required string Name { get; init; }
    public required IReadOnlyList<ScenarioTraversalStep> Steps { get; init; }
}
```

- [ ] **Step 2: Create ScenarioTraversalStep.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record ScenarioTraversalStep
{
    public required RelationshipKind FollowKind { get; init; }
    public ScenarioTraversalDirection Direction { get; init; } = ScenarioTraversalDirection.Dependencies;
    public string? Role { get; init; }
    public DescriptorKind? TargetKind { get; init; }
    public int MaxDepth { get; init; } = 1;
}
```

- [ ] **Step 3: Create MetadataContextPackRequest.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackRequest
{
    public required MetadataContextPackScope Scope { get; init; }
    public required IReadOnlyList<DescriptorRef> FocusDescriptors { get; init; }
    public RuntimeScenarioRecipe? ScenarioRecipe { get; init; }
    public string? Intent { get; init; }
    public string? TenantId { get; init; }
    public IReadOnlyList<DescriptorKind>? IncludeKinds { get; init; }
    public IReadOnlyList<DescriptorKind>? ExcludeKinds { get; init; }
    public int MaxTraversalDepth { get; init; } = 2;
    public int MaxDescriptorCount { get; init; } = 64;
    public bool IncludeStableHashes { get; init; }
    public bool IncludeGovernanceState { get; init; }
}
```

- [ ] **Step 4: Create MetadataContextPackDescriptorEntry.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackDescriptorEntry
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorState State { get; init; }
    public DescriptorStableHashes? Hashes { get; init; }
    public MetadataContextPackGovernanceEntry? Governance { get; init; }
    public bool IsFocus { get; init; }
}
```

- [ ] **Step 5: Create MetadataContextPackRelationshipEntry.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackRelationshipEntry
{
    public required DescriptorRef From { get; init; }
    public required DescriptorRef To { get; init; }
    public required RelationshipKind Kind { get; init; }
    public string? Role { get; init; }
    public string? SourcePath { get; init; }
    public required RelationshipStrength Strength { get; init; }
    public bool IsRuntimeBinding { get; init; }
}
```

- [ ] **Step 6: Create MetadataContextPackSummary.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackSummary
{
    public required int TotalDescriptorCount { get; init; }
    public required IReadOnlyDictionary<DescriptorKind, int> DescriptorCountsByKind { get; init; }
    public required int TotalRelationshipCount { get; init; }
    public required IReadOnlyDictionary<RelationshipKind, int> RelationshipCountsByKind { get; init; }
    public required IReadOnlyList<DescriptorRef> FocusRefs { get; init; }
    public required bool WasTruncated { get; init; }
    public required int? TruncatedAtCount { get; init; }
    public required int TraversalDepthReached { get; init; }
}
```

- [ ] **Step 7: Create MetadataContextPackDiagnostic.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackDiagnostic
{
    public required MetadataContextPackDiagnosticSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
    public string? Path { get; init; }
}
```

- [ ] **Step 8: Create MetadataContextPack.cs**

```csharp
namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPack
{
    public required MetadataContextPackRequest Request { get; init; }
    public required IReadOnlyList<MetadataContextPackDescriptorEntry> Descriptors { get; init; }
    public required IReadOnlyList<MetadataContextPackRelationshipEntry> Relationships { get; init; }
    public required MetadataContextPackSummary Summary { get; init; }
    public required IReadOnlyList<MetadataContextPackDiagnostic> Diagnostics { get; init; }
}
```

- [ ] **Step 9: Create IMetadataContextPackBuilder.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public interface IMetadataContextPackBuilder
{
    MetadataContextPack Build(
        MetadataContextPackRequest request,
        DescriptorTopologySnapshot topology,
        IReadOnlyList<IDescriptor> descriptors);
}
```

- [ ] **Step 10: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Metadata.ContextPack.Abstractions`
Expected: PASS with no errors

- [ ] **Step 11: Commit**

```bash
git add framework/src/CrestCreates.Metadata.ContextPack.Abstractions/
git commit -m "feat(context-pack): add all contract records and builder interface (#14)"
```

---

## Task 3: Create Implementation Project and DI Registration

**Files:**
- Create: `framework/src/CrestCreates.Metadata.ContextPack/CrestCreates.Metadata.ContextPack.csproj`
- Create: `framework/src/CrestCreates.Metadata.ContextPack/DefaultMetadataContextPackBuilder.cs`
- Create: `framework/src/CrestCreates.Metadata.ContextPack/MetadataContextPackServiceCollectionExtensions.cs`

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Metadata.ContextPack</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="CrestCreates.Metadata.ContextPack.Tests" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Metadata.ContextPack.Abstractions\CrestCreates.Metadata.ContextPack.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create DefaultMetadataContextPackBuilder.cs — skeleton with FocusOnly scope**

This is the minimal implementation that passes the first test. We'll add scopes incrementally.

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

        // 3. Build descriptor index
        var descriptorIndex = BuildDescriptorIndex(descriptors);

        // 4. Resolve focus nodes
        var focusRefs = snapshotRequest.FocusDescriptors;
        var foundFocusRefs = new List<DescriptorRef>();
        var missingFocusRefs = new List<DescriptorRef>();

        foreach (var focusRef in focusRefs)
        {
            if (topology.Contains(focusRef))
            {
                foundFocusRefs.Add(topology.FindNode(focusRef)!.Ref);
            }
            else
            {
                missingFocusRefs.Add(focusRef);
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = MetadataContextPackDiagnosticSeverity.Warning,
                    Code = MetadataContextPackDiagnosticCodes.FocusNotFound,
                    Message = $"Focus descriptor '{focusRef.FullId}' not found in topology.",
                    Subject = focusRef
                });
            }
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
                ResolveDirectDependencies(foundFocusRefs, topology, includedRefs, includedEdges);
                traversalDepthReached = 1;
                break;

            case MetadataContextPackScope.DirectDependents:
                ResolveDirectDependents(foundFocusRefs, topology, includedRefs, includedEdges);
                traversalDepthReached = 1;
                break;

            case MetadataContextPackScope.ImpactRadius:
                traversalDepthReached = ResolveImpactRadius(foundFocusRefs, topology, snapshotRequest.MaxTraversalDepth, includedRefs, includedEdges, diagnostics);
                break;

            case MetadataContextPackScope.RuntimeScenario:
                traversalDepthReached = ResolveRuntimeScenario(foundFocusRefs, topology, snapshotRequest, includedRefs, includedEdges, diagnostics);
                break;
        }

        // 6. Apply kind filters (non-focus only)
        var focusSet = new HashSet<DescriptorRef>(foundFocusRefs);
        ApplyKindFilters(includedRefs, focusSet, snapshotRequest, topology, diagnostics);

        // 7. Apply count bounds (non-focus only)
        ApplyCountBounds(includedRefs, focusSet, snapshotRequest.MaxDescriptorCount, diagnostics);

        // 8. Collect relationship edges
        var relationshipEntries = CollectRelationshipEntries(includedRefs, includedEdges, topology);

        // 9. Build descriptor entries
        var descriptorEntries = BuildDescriptorEntries(includedRefs, focusSet, descriptorIndex, snapshotRequest, diagnostics);

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

    private static Dictionary<DescriptorRef, IDescriptor> BuildDescriptorIndex(IReadOnlyList<IDescriptor> descriptors)
    {
        var index = new Dictionary<DescriptorRef, IDescriptor>();
        foreach (var d in descriptors)
        {
            var key = new DescriptorRef(d.Namespace, d.Id, null);
            if (!index.ContainsKey(key))
                index[key] = d;
        }
        return index;
    }

    private static void ResolveDirectDependencies(
        List<DescriptorRef> focusRefs, DescriptorTopologySnapshot topology,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges)
    {
        foreach (var focusRef in focusRefs)
        {
            includedRefs.Add(focusRef);
            var deps = topology.GetDirectDependencies(focusRef);
            foreach (var dep in deps)
            {
                includedRefs.Add(dep.Ref);
            }
            // Collect edges from focus to its direct dependencies
            var focusNode = topology.FindNode(focusRef);
            if (focusNode is not null)
            {
                foreach (var edgeIdx in focusNode.OutgoingEdgeIndices)
                {
                    includedEdges.Add(topology.Edges[edgeIdx]);
                }
            }
        }
    }

    private static void ResolveDirectDependents(
        List<DescriptorRef> focusRefs, DescriptorTopologySnapshot topology,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges)
    {
        foreach (var focusRef in focusRefs)
        {
            includedRefs.Add(focusRef);
            var dependents = topology.GetDirectDependents(focusRef);
            foreach (var dep in dependents)
            {
                includedRefs.Add(dep.Ref);
            }
            // Collect edges from dependents to focus
            var focusNode = topology.FindNode(focusRef);
            if (focusNode is not null)
            {
                foreach (var edgeIdx in focusNode.IncomingEdgeIndices)
                {
                    includedEdges.Add(topology.Edges[edgeIdx]);
                }
            }
        }
    }

    private static int ResolveImpactRadius(
        List<DescriptorRef> focusRefs, DescriptorTopologySnapshot topology, int maxDepth,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        var visited = new HashSet<DescriptorRef>();
        var frontier = new List<DescriptorRef>();

        // Depth 0: focus nodes
        foreach (var r in focusRefs)
        {
            var node = topology.FindNode(r);
            if (node is not null && visited.Add(node.Ref))
            {
                includedRefs.Add(node.Ref);
                frontier.Add(node.Ref);
            }
        }

        var depthReached = 0;
        var hasUnvisitedBeyondMaxDepth = false;

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            var nextFrontier = new List<DescriptorRef>();
            foreach (var currentRef in frontier)
            {
                var currentNode = topology.FindNode(currentRef);
                if (currentNode is null) continue;

                // Follow outgoing edges
                foreach (var edgeIdx in currentNode.OutgoingEdgeIndices)
                {
                    var edge = topology.Edges[edgeIdx];
                    includedEdges.Add(edge);
                    var target = topology.FindNode(edge.To);
                    if (target is not null && visited.Add(target.Ref))
                    {
                        includedRefs.Add(target.Ref);
                        nextFrontier.Add(target.Ref);
                    }
                }

                // Follow incoming edges
                foreach (var edgeIdx in currentNode.IncomingEdgeIndices)
                {
                    var edge = topology.Edges[edgeIdx];
                    includedEdges.Add(edge);
                    var source = topology.FindNode(edge.From);
                    if (source is not null && visited.Add(source.Ref))
                    {
                        includedRefs.Add(source.Ref);
                        nextFrontier.Add(source.Ref);
                    }
                }
            }

            if (nextFrontier.Count > 0)
            {
                depthReached = depth;
            }

            frontier = nextFrontier;
        }

        // Check if there are unvisited nodes beyond max depth
        if (frontier.Count > 0)
        {
            hasUnvisitedBeyondMaxDepth = true;
        }

        if (hasUnvisitedBeyondMaxDepth)
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
        List<DescriptorRef> focusRefs, DescriptorTopologySnapshot topology,
        MetadataContextPackRequest request,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges,
        List<MetadataContextPackDiagnostic> diagnostics)
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
            var stepNewBoundary = new HashSet<DescriptorRef>();

            for (int depth = 1; depth <= step.MaxDepth; depth++)
            {
                var nextBoundary = new HashSet<DescriptorRef>();

                foreach (var currentRef in boundary)
                {
                    var currentNode = topology.FindNode(currentRef);
                    if (currentNode is null) continue;

                    IEnumerable<int> edgeIndices = step.Direction switch
                    {
                        ScenarioTraversalDirection.Dependencies => currentNode.OutgoingEdgeIndices,
                        ScenarioTraversalDirection.Dependents => currentNode.IncomingEdgeIndices,
                        ScenarioTraversalDirection.Both => currentNode.OutgoingEdgeIndices
                            .Concat(currentNode.IncomingEdgeIndices),
                        _ => currentNode.OutgoingEdgeIndices
                    };

                    foreach (var edgeIdx in edgeIndices)
                    {
                        var edge = topology.Edges[edgeIdx];

                        if (edge.Kind != step.FollowKind) continue;
                        if (step.Role is not null && edge.Role != step.Role) continue;

                        var targetRef = step.Direction == ScenarioTraversalDirection.Dependents
                            ? edge.From : edge.To;

                        var targetNode = topology.FindNode(targetRef);
                        if (targetNode is null) continue;

                        if (step.TargetKind.HasValue && targetNode.Kind != step.TargetKind.Value) continue;

                        includedEdges.Add(edge);

                        if (stepVisited.Add(targetNode.Ref))
                        {
                            includedRefs.Add(targetNode.Ref);
                            nextBoundary.Add(targetNode.Ref);
                        }
                    }
                }

                stepNewBoundary.UnionWith(nextBoundary);
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
        MetadataContextPackRequest request, DescriptorTopologySnapshot topology,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        // Check if any focus descriptor would be filtered out
        foreach (var focusRef in focusSet)
        {
            var node = topology.FindNode(focusRef);
            if (node is null) continue;

            var kind = node.Kind;
            var wouldBeExcluded = false;

            if (request.IncludeKinds is not null && !request.IncludeKinds.Contains(kind))
                wouldBeExcluded = true;
            if (request.ExcludeKinds is not null && request.ExcludeKinds.Contains(kind))
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

            var node = topology.FindNode(ref_);
            if (node is null) continue;

            var kind = node.Kind;
            if (request.IncludeKinds is not null && !request.IncludeKinds.Contains(kind))
            {
                toRemove.Add(ref_);
                continue;
            }
            if (request.ExcludeKinds is not null && request.ExcludeKinds.Contains(kind))
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
        DescriptorTopologySnapshot topology)
    {
        var entries = new List<MetadataContextPackRelationshipEntry>();
        var seen = new HashSet<(DescriptorRef, DescriptorRef, RelationshipKind)>();

        foreach (var edge in includedEdges)
        {
            // Only include edges where both endpoints are in the included set
            var fromResolved = topology.FindNode(edge.From);
            var toResolved = topology.FindNode(edge.To);
            if (fromResolved is null || toResolved is null) continue;
            if (!includedRefs.Contains(fromResolved.Ref) || !includedRefs.Contains(toResolved.Ref)) continue;

            var key = (fromResolved.Ref, toResolved.Ref, edge.Kind);
            if (!seen.Add(key)) continue;

            entries.Add(new MetadataContextPackRelationshipEntry
            {
                From = fromResolved.Ref,
                To = toResolved.Ref,
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
        Dictionary<DescriptorRef, IDescriptor> descriptorIndex,
        MetadataContextPackRequest request,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        var entries = new List<MetadataContextPackDescriptorEntry>();

        foreach (var ref_ in includedRefs)
        {
            var node = new DescriptorRef(ref_.Namespace, ref_.Id, null); // unpinned for index lookup
            descriptorIndex.TryGetValue(node, out var descriptor);

            var kind = descriptor?.Kind ?? DescriptorKind.Schema; // fallback
            var name = descriptor?.Name ?? ref_.Id;
            var state = descriptor?.State ?? DescriptorState.Active;

            DescriptorStableHashes? hashes = null;
            if (request.IncludeStableHashes)
            {
                if (_hashBuilder is not null && descriptor is not null)
                {
                    hashes = _hashBuilder.Build(descriptor);
                }
                else if (_hashBuilder is null)
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
                    State = state,
                    RequiresReview = state == DescriptorState.Draft
                };
            }

            entries.Add(new MetadataContextPackDescriptorEntry
            {
                Ref = ref_,
                Kind = kind,
                Name = name,
                State = state,
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

        int? truncatedAtCount = diagnostics
            .FirstOrDefault(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByCount)
            is { } ? descriptors.Count : null;

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

- [ ] **Step 3: Create MetadataContextPackServiceCollectionExtensions.cs**

```csharp
using CrestCreates.Metadata.ContextPack.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Metadata.ContextPack;

public static class MetadataContextPackServiceCollectionExtensions
{
    public static IServiceCollection AddMetadataContextPack(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IMetadataContextPackBuilder, DefaultMetadataContextPackBuilder>();
        return services;
    }
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Metadata.ContextPack`
Expected: PASS with no errors

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.ContextPack/
git commit -m "feat(context-pack): add implementation project with builder and DI registration (#14)"
```

---

## Task 4: Create Test Project and Test Helper

**Files:**
- Create: `framework/test/CrestCreates.Metadata.ContextPack.Tests/CrestCreates.Metadata.ContextPack.Tests.csproj`
- Create: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs`

- [ ] **Step 1: Create the test project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Metadata.ContextPack.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Metadata.ContextPack.Tests</AssemblyName>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime;build;native;contentfiles;analyzers;buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Metadata.ContextPack\CrestCreates.Metadata.ContextPack.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Metadata.ContextPack.Abstractions\CrestCreates.Metadata.ContextPack.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the test file with helper and first test (FocusOnly)**

The test file contains a `CreateSnapshot` helper (matching the existing pattern in `DescriptorTopologySnapshotTests.cs`) and all 25 tests. We'll write them all at once since the builder implementation is already complete from Task 3.

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;
using CrestCreates.Metadata.ContextPack;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.ContextPack.Tests;

public class MetadataContextPackBuilderTests
{
    // ── Helpers ──

    private static DescriptorTopologySnapshot CreateSnapshot(
        (DescriptorRef Ref, DescriptorKind Kind, string Name)[] nodeDefs,
        (int Index, DescriptorRef From, DescriptorRef To, RelationshipKind Kind,
         string? Role, RelationshipStrength Strength, bool IsRuntimeBinding)[] edgeDefs)
    {
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>();
        foreach (var def in nodeDefs)
        {
            nodes[def.Ref] = new DescriptorNode
            {
                Ref = def.Ref,
                Kind = def.Kind,
                Name = def.Name,
                State = DescriptorState.Active,
                OutgoingEdgeIndices = new HashSet<int>(),
                IncomingEdgeIndices = new HashSet<int>()
            };
        }

        var edges = new List<DescriptorEdge>();
        foreach (var def in edgeDefs)
        {
            var edge = new DescriptorEdge
            {
                Index = def.Index,
                From = def.From,
                To = def.To,
                Kind = def.Kind,
                Role = def.Role,
                SourcePath = null,
                Strength = def.Strength,
                IsRuntimeBinding = def.IsRuntimeBinding
            };
            edges.Add(edge);

            if (nodes.TryGetValue(def.From, out var fromNode))
                ((HashSet<int>)fromNode.OutgoingEdgeIndices).Add(def.Index);
            if (nodes.TryGetValue(def.To, out var toNode))
                ((HashSet<int>)toNode.IncomingEdgeIndices).Add(def.Index);
        }

        foreach (var key in nodes.Keys.ToList())
        {
            nodes[key] = nodes[key] with
            {
                OutgoingEdgeIndices = nodes[key].OutgoingEdgeIndices.ToHashSet(),
                IncomingEdgeIndices = nodes[key].IncomingEdgeIndices.ToHashSet()
            };
        }

        var diagnostics = new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() };
        return new DescriptorTopologySnapshot(
            nodes, edges, diagnostics,
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            DateTimeOffset.UtcNow);
    }

    private static List<IDescriptor> CreateDescriptors(
        params (DescriptorRef Ref, DescriptorKind Kind, string Name, DescriptorState State)[] defs)
    {
        return defs.Select(d => new TestDescriptor(d.Ref, d.Kind, d.Name, d.State)).ToList<IDescriptor>();
    }

    private sealed class TestDescriptor : IDescriptor
    {
        private readonly DescriptorRef _ref;
        public TestDescriptor(DescriptorRef ref_, DescriptorKind kind, string name, DescriptorState state)
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

    private readonly DefaultMetadataContextPackBuilder _builder = new();

    // ── A. Scope Traversal ──

    [Fact]
    public void FocusOnly_Returns_Only_Requested_Descriptors()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");
        var c = new DescriptorRef("ns", "C");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Event, "B"), (c, DescriptorKind.Schema, "C") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Event, "B", DescriptorState.Active),
            (c, DescriptorKind.Schema, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a, b, c }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Should().HaveCount(3);
        pack.Descriptors.Should().OnlyContain(d => d.IsFocus);
        pack.Relationships.Should().BeEmpty();
    }

    [Fact]
    public void DirectDependencies_Includes_Dependencies_And_Edges()
    {
        var cap = new DescriptorRef("capability", "SubmitCap");
        var schema = new DescriptorRef("schema", "InputSchema");

        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "SubmitCap"), (schema, DescriptorKind.Schema, "InputSchema") },
            new[] { (0, cap, schema, RelationshipKind.Uses, "InputSchema", RelationshipStrength.Strong, false) });

        var descriptors = CreateDescriptors(
            (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
            (schema, DescriptorKind.Schema, "InputSchema", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap, schema });
        pack.Descriptors.First(d => d.Ref.Equals(cap)).IsFocus.Should().BeTrue();
        pack.Descriptors.First(d => d.Ref.Equals(schema)).IsFocus.Should().BeFalse();
        pack.Relationships.Should().ContainSingle();
        pack.Relationships[0].From.Should().Be(cap);
        pack.Relationships[0].To.Should().Be(schema);
    }

    [Fact]
    public void DirectDependents_Includes_Dependents_And_Edges()
    {
        var evt = new DescriptorRef("event", "ApprovedEvent");
        var cap = new DescriptorRef("capability", "ApproveCap");

        var topology = CreateSnapshot(
            new[] { (evt, DescriptorKind.Event, "ApprovedEvent"), (cap, DescriptorKind.Capability, "ApproveCap") },
            new[] { (0, cap, evt, RelationshipKind.Produces, null, RelationshipStrength.Weak, false) });

        var descriptors = CreateDescriptors(
            (evt, DescriptorKind.Event, "ApprovedEvent", DescriptorState.Active),
            (cap, DescriptorKind.Capability, "ApproveCap", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependents,
            FocusDescriptors = new[] { evt }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { evt, cap });
        pack.Relationships.Should().ContainSingle();
        pack.Relationships[0].From.Should().Be(cap);
        pack.Relationships[0].To.Should().Be(evt);
    }

    [Fact]
    public void ImpactRadius_Respects_MaxTraversalDepth()
    {
        var schema = new DescriptorRef("schema", "S");
        var cap = new DescriptorRef("capability", "C");
        var workflow = new DescriptorRef("workflow", "W");
        var humanTask = new DescriptorRef("humantask", "H");

        var topology = CreateSnapshot(
            new[] {
                (schema, DescriptorKind.Schema, "S"),
                (cap, DescriptorKind.Capability, "C"),
                (workflow, DescriptorKind.Workflow, "W"),
                (humanTask, DescriptorKind.HumanTask, "H")
            },
            new[] {
                (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
                (1, workflow, cap, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true),
                (2, humanTask, workflow, RelationshipKind.Triggers, null, RelationshipStrength.Strong, true)
            });

        var descriptors = CreateDescriptors(
            (schema, DescriptorKind.Schema, "S", DescriptorState.Active),
            (cap, DescriptorKind.Capability, "C", DescriptorState.Active),
            (workflow, DescriptorKind.Workflow, "W", DescriptorState.Active),
            (humanTask, DescriptorKind.HumanTask, "H", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.ImpactRadius,
            FocusDescriptors = new[] { schema },
            MaxTraversalDepth = 2
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Schema (depth 0) → Cap (depth 1) → Workflow (depth 2). HumanTask at depth 3 excluded.
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { schema, cap, workflow });
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByDepth);
    }

    [Fact]
    public void RuntimeScenario_Executes_Recipe_Steps()
    {
        var workflow = new DescriptorRef("workflow", "CompanyCert");
        var submitCap = new DescriptorRef("capability", "SubmitCap");
        var reviewHt = new DescriptorRef("humantask", "ReviewHt");
        var approveCap = new DescriptorRef("capability", "ApproveCap");
        var approvedEvt = new DescriptorRef("event", "ApprovedEvt");

        var topology = CreateSnapshot(
            new[] {
                (workflow, DescriptorKind.Workflow, "CompanyCert"),
                (submitCap, DescriptorKind.Capability, "SubmitCap"),
                (reviewHt, DescriptorKind.HumanTask, "ReviewHt"),
                (approveCap, DescriptorKind.Capability, "ApproveCap"),
                (approvedEvt, DescriptorKind.Event, "ApprovedEvt")
            },
            new[] {
                (0, workflow, submitCap, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true),
                (1, workflow, reviewHt, RelationshipKind.Triggers, "HumanTaskStep", RelationshipStrength.Strong, true),
                (2, workflow, approveCap, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true),
                (3, reviewHt, approveCap, RelationshipKind.Triggers, "Outcome", RelationshipStrength.Strong, true),
                (4, approveCap, approvedEvt, RelationshipKind.Produces, null, RelationshipStrength.Weak, false)
            });

        var descriptors = CreateDescriptors(
            (workflow, DescriptorKind.Workflow, "CompanyCert", DescriptorState.Active),
            (submitCap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
            (reviewHt, DescriptorKind.HumanTask, "ReviewHt", DescriptorState.Active),
            (approveCap, DescriptorKind.Capability, "ApproveCap", DescriptorState.Active),
            (approvedEvt, DescriptorKind.Event, "ApprovedEvt", DescriptorState.Active));

        var recipe = new RuntimeScenarioRecipe
        {
            Name = "CompanyCertification",
            Steps = new[]
            {
                new ScenarioTraversalStep
                {
                    FollowKind = RelationshipKind.Triggers,
                    Direction = ScenarioTraversalDirection.Dependencies,
                    MaxDepth = 1
                },
                new ScenarioTraversalStep
                {
                    FollowKind = RelationshipKind.Produces,
                    Direction = ScenarioTraversalDirection.Dependencies,
                    TargetKind = DescriptorKind.Event,
                    MaxDepth = 1
                }
            }
        };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.RuntimeScenario,
            FocusDescriptors = new[] { workflow },
            ScenarioRecipe = recipe
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(
            new[] { workflow, submitCap, reviewHt, approveCap, approvedEvt });
    }

    // ── B. Bounds and Filters ──

    [Fact]
    public void MaxDescriptorCount_Truncates_And_Emits_Diagnostic()
    {
        var refs = Enumerable.Range(0, 10)
            .Select(i => new DescriptorRef("ns", $"D{i}"))
            .ToArray();

        var nodeDefs = refs.Select((r, i) => (r, DescriptorKind.Capability, $"D{i}")).ToArray();
        var topology = CreateSnapshot(nodeDefs,
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var descriptors = CreateDescriptors(
            refs.Select((r, i) => (r, DescriptorKind.Capability, $"D{i}", DescriptorState.Active)).ToArray());

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = refs.Take(1).ToArray(),
            MaxDescriptorCount = 5
        };

        // Add more descriptors via ImpactRadius to exceed limit
        var requestWithRadius = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.ImpactRadius,
            FocusDescriptors = refs.Take(1).ToArray(),
            MaxDescriptorCount = 5,
            MaxTraversalDepth = 10
        };

        // For a simpler test, use FocusOnly with 10 focus descriptors and limit 5
        var requestManyFocus = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = refs,
            MaxDescriptorCount = 5
        };

        var pack = _builder.Build(requestManyFocus, topology, descriptors);

        pack.Descriptors.Should().HaveCount(5);
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByCount);
        pack.Summary.TruncatedAtCount.Should().Be(5);
    }

    [Fact]
    public void IncludeKinds_Limits_Candidates()
    {
        var schema = new DescriptorRef("schema", "S");
        var cap = new DescriptorRef("capability", "C");

        var topology = CreateSnapshot(
            new[] { (schema, DescriptorKind.Schema, "S"), (cap, DescriptorKind.Capability, "C") },
            new[] { (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false) });

        var descriptors = CreateDescriptors(
            (schema, DescriptorKind.Schema, "S", DescriptorState.Active),
            (cap, DescriptorKind.Capability, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap },
            IncludeKinds = new[] { DescriptorKind.Schema }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Focus (Capability) always included, but non-focus Schema is included by IncludeKinds
        pack.Descriptors.Select(d => d.Kind).Should().BeEquivalentTo(new[] { DescriptorKind.Capability, DescriptorKind.Schema });
    }

    [Fact]
    public void ExcludeKinds_Removes_Matches()
    {
        var schema = new DescriptorRef("schema", "S");
        var cap = new DescriptorRef("capability", "C");

        var topology = CreateSnapshot(
            new[] { (schema, DescriptorKind.Schema, "S"), (cap, DescriptorKind.Capability, "C") },
            new[] { (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false) });

        var descriptors = CreateDescriptors(
            (schema, DescriptorKind.Schema, "S", DescriptorState.Active),
            (cap, DescriptorKind.Capability, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap },
            ExcludeKinds = new[] { DescriptorKind.Schema }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Schema excluded, only focus Capability remains
        pack.Descriptors.Select(d => d.Kind).Should().BeEquivalentTo(new[] { DescriptorKind.Capability });
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.KindExcluded);
    }

    [Fact]
    public void Include_And_Exclude_Precedence()
    {
        var schema = new DescriptorRef("schema", "S");
        var cap = new DescriptorRef("capability", "C");

        var topology = CreateSnapshot(
            new[] { (schema, DescriptorKind.Schema, "S"), (cap, DescriptorKind.Capability, "C") },
            new[] { (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false) });

        var descriptors = CreateDescriptors(
            (schema, DescriptorKind.Schema, "S", DescriptorState.Active),
            (cap, DescriptorKind.Capability, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap },
            IncludeKinds = new[] { DescriptorKind.Schema, DescriptorKind.Capability },
            ExcludeKinds = new[] { DescriptorKind.Schema }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // IncludeKinds allows Schema+Capability, ExcludeKinds removes Schema → only Capability
        pack.Descriptors.Select(d => d.Kind).Should().BeEquivalentTo(new[] { DescriptorKind.Capability });
    }

    [Fact]
    public void Focus_Always_Included_Despite_Kind_Filters()
    {
        var cap = new DescriptorRef("capability", "C");

        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "C") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var descriptors = CreateDescriptors(
            (cap, DescriptorKind.Capability, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { cap },
            ExcludeKinds = new[] { DescriptorKind.Capability }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap });
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.FocusKindFiltered);
    }

    // ── C. Diagnostics ──

    [Fact]
    public void Unknown_Focus_Produces_Diagnostic_Not_Exception()
    {
        var missing = new DescriptorRef("ns", "Missing");

        var topology = CreateSnapshot(
            Array.Empty<(DescriptorRef, DescriptorKind, string)>(),
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { missing }
        };

        var act = () => _builder.Build(request, topology, Array.Empty<IDescriptor>());

        act.Should().NotThrow();
        var pack = act();
        pack.Descriptors.Should().BeEmpty();
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.FocusNotFound);
    }

    [Fact]
    public void Mixed_Known_And_Unknown_Focus_Continues_With_Known()
    {
        var known = new DescriptorRef("ns", "Known");
        var missing = new DescriptorRef("ns", "Missing");

        var topology = CreateSnapshot(
            new[] { (known, DescriptorKind.Capability, "Known") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var descriptors = CreateDescriptors(
            (known, DescriptorKind.Capability, "Known", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { known, missing }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { known });
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.FocusNotFound);
    }

    [Fact]
    public void RuntimeScenario_Without_Recipe_Emits_Error()
    {
        var focus = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (focus, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.RuntimeScenario,
            FocusDescriptors = new[] { focus },
            ScenarioRecipe = null
        };

        var pack = _builder.Build(request, topology, Array.Empty<IDescriptor>());

        pack.Diagnostics.Should().Contain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.RecipeMissing &&
            d.Severity == MetadataContextPackDiagnosticSeverity.Error);
    }

    [Fact]
    public void Truncated_By_Depth_Only_When_Unvisited_Nodes_Exist()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B") },
            new[] { (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong, false) });

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.ImpactRadius,
            FocusDescriptors = new[] { a },
            MaxTraversalDepth = 10
        };

        var pack = _builder.Build(request, topology, Array.Empty<IDescriptor>());

        // Graph is shallow (depth 1), MaxTraversalDepth=10 reaches everything → no truncation diagnostic
        pack.Diagnostics.Should().NotContain(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByDepth);
    }

    [Fact]
    public void Hash_Builder_Missing_Emits_Warning()
    {
        var focus = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (focus, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var descriptors = CreateDescriptors(
            (focus, DescriptorKind.Capability, "A", DescriptorState.Active));

        var builder = new DefaultMetadataContextPackBuilder(hashBuilder: null);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { focus },
            IncludeStableHashes = true
        };

        var pack = builder.Build(request, topology, descriptors);

        pack.Descriptors.All(d => d.Hashes is null).Should().BeTrue();
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.HashBuilderMissing);
    }

    // ── D. Determinism and Safety ──

    [Fact]
    public void Deterministic_Output_Ordering()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");
        var c = new DescriptorRef("ns", "C");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Event, "B"), (c, DescriptorKind.Schema, "C") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Event, "B", DescriptorState.Active),
            (c, DescriptorKind.Schema, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a, b, c }
        };

        var pack1 = _builder.Build(request, topology, descriptors);
        var pack2 = _builder.Build(request, topology, descriptors);

        pack1.Descriptors.Select(d => d.Ref).Should().Equal(pack2.Descriptors.Select(d => d.Ref).ToArray());
        pack1.Relationships.Should().Equal(pack2.Relationships);
        pack1.Diagnostics.Select(d => d.Code).Should().Equal(pack2.Diagnostics.Select(d => d.Code).ToArray());
    }

    [Fact]
    public void Shuffled_Input_Still_Deterministic()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");
        var c = new DescriptorRef("ns", "C");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Event, "B"), (c, DescriptorKind.Schema, "C") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var descriptors1 = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Event, "B", DescriptorState.Active),
            (c, DescriptorKind.Schema, "C", DescriptorState.Active));

        var descriptors2 = CreateDescriptors(
            (c, DescriptorKind.Schema, "C", DescriptorState.Active),
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Event, "B", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a, b, c }
        };

        var pack1 = _builder.Build(request, topology, descriptors1);
        var pack2 = _builder.Build(request, topology, descriptors2);

        pack1.Descriptors.Select(d => d.Ref).Should().Equal(pack2.Descriptors.Select(d => d.Ref).ToArray());
    }

    [Fact]
    public void Self_Cycle_Terminates()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            new[] { (0, a, a, RelationshipKind.References, null, RelationshipStrength.Weak, false) });

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active));

        var recipe = new RuntimeScenarioRecipe
        {
            Name = "SelfLoop",
            Steps = new[]
            {
                new ScenarioTraversalStep
                {
                    FollowKind = RelationshipKind.References,
                    Direction = ScenarioTraversalDirection.Dependencies,
                    MaxDepth = 5
                }
            }
        };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.RuntimeScenario,
            FocusDescriptors = new[] { a },
            ScenarioRecipe = recipe
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Should().ContainSingle();
        pack.Descriptors[0].Ref.Should().Be(a);
    }

    [Fact]
    public void Builder_Is_Read_Only()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Schema, "B") },
            new[] { (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong, false) });

        var descriptorList = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Schema, "B", DescriptorState.Active));

        var nodeCountBefore = topology.NodeCount;
        var descCountBefore = descriptorList.Count;

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { a }
        };

        _builder.Build(request, topology, descriptorList);

        topology.NodeCount.Should().Be(nodeCountBefore);
        descriptorList.Count.Should().Be(descCountBefore);
    }

    [Fact]
    public void Request_Collections_Are_Snapshotted()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var focusList = new List<DescriptorRef> { a };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = focusList
        };

        var pack = _builder.Build(request, topology, Array.Empty<IDescriptor>());

        focusList.Add(new DescriptorRef("ns", "B"));

        pack.Request.FocusDescriptors.Should().HaveCount(1);
        pack.Request.FocusDescriptors[0].Should().Be(a);
    }

    // ── E. Optional Enrichment ──

    [Fact]
    public void Stable_Hashes_Omitted_By_Default()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a },
            IncludeStableHashes = false
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.All(d => d.Hashes is null).Should().BeTrue();
    }

    [Fact]
    public void Stable_Hashes_Included_When_Requested()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var testDesc = new TestDescriptor(a, DescriptorKind.Capability, "A", DescriptorState.Active);
        var descriptors = new List<IDescriptor> { testDesc };

        var hashBuilderMock = new Mock<IDescriptorStableHashBuilder>();
        hashBuilderMock.Setup(h => h.Build(It.IsAny<IDescriptor>()))
            .Returns(new DescriptorStableHashes("contract", "definition"));

        var builder = new DefaultMetadataContextPackBuilder(hashBuilderMock.Object);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a },
            IncludeStableHashes = true
        };

        var pack = builder.Build(request, topology, descriptors);

        pack.Descriptors.All(d => d.Hashes is not null).Should().BeTrue();
    }

    [Fact]
    public void Stable_Hashes_Not_Computed_When_Not_Requested()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var testDesc = new TestDescriptor(a, DescriptorKind.Capability, "A", DescriptorState.Active);
        var descriptors = new List<IDescriptor> { testDesc };

        var hashBuilderMock = new Mock<IDescriptorStableHashBuilder>();
        hashBuilderMock.Setup(h => h.Build(It.IsAny<IDescriptor>()))
            .Returns(new DescriptorStableHashes("contract", "definition"));

        var builder = new DefaultMetadataContextPackBuilder(hashBuilderMock.Object);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a },
            IncludeStableHashes = false
        };

        _ = builder.Build(request, topology, descriptors);

        hashBuilderMock.Verify(h => h.Build(It.IsAny<IDescriptor>()), Times.Never);
    }

    [Fact]
    public void Governance_State_From_Descriptor_State_Only()
    {
        var active = new DescriptorRef("ns", "Active");
        var draft = new DescriptorRef("ns", "Draft");

        var topology = CreateSnapshot(
            new[] { (active, DescriptorKind.Capability, "Active"), (draft, DescriptorKind.Capability, "Draft") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var descriptors = CreateDescriptors(
            (active, DescriptorKind.Capability, "Active", DescriptorState.Active),
            (draft, DescriptorKind.Capability, "Draft", DescriptorState.Draft));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { active, draft },
            IncludeGovernanceState = true
        };

        var pack = _builder.Build(request, topology, descriptors);

        var activeEntry = pack.Descriptors.First(d => d.Ref.Equals(active));
        var draftEntry = pack.Descriptors.First(d => d.Ref.Equals(draft));

        activeEntry.Governance.Should().NotBeNull();
        activeEntry.Governance!.State.Should().Be(DescriptorState.Active);
        activeEntry.Governance.RequiresReview.Should().BeFalse();

        draftEntry.Governance.Should().NotBeNull();
        draftEntry.Governance!.State.Should().Be(DescriptorState.Draft);
        draftEntry.Governance.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void Intent_Is_Ignored_In_Phase7b()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active));

        var request1 = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a },
            Intent = "I want to understand the capability"
        };

        var request2 = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a },
            Intent = "Completely different intent"
        };

        var pack1 = _builder.Build(request1, topology, descriptors);
        var pack2 = _builder.Build(request2, topology, descriptors);

        pack1.Descriptors.Select(d => d.Ref).Should().Equal(pack2.Descriptors.Select(d => d.Ref).ToArray());
        pack1.Relationships.Should().Equal(pack2.Relationships);
    }
}
```

- [ ] **Step 3: Build the test project**

Run: `dotnet build framework/test/CrestCreates.Metadata.ContextPack.Tests`
Expected: PASS with no errors

- [ ] **Step 4: Run all tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests`
Expected: All 25 tests PASS

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/
git commit -m "test(context-pack): add all 25 builder tests (#14)"
```

---

## Task 5: Register Projects in Solution and Run Full Tests

**Files:**
- Modify: `CrestCreates.slnx`

- [ ] **Step 1: Add the 3 new projects to the solution**

Add to the `/src/core/` folder:
- `framework/src/CrestCreates.Metadata.ContextPack.Abstractions/CrestCreates.Metadata.ContextPack.Abstractions.csproj`
- `framework/src/CrestCreates.Metadata.ContextPack/CrestCreates.Metadata.ContextPack.csproj`

Add to the `/test/` folder:
- `framework/test/CrestCreates.Metadata.ContextPack.Tests/CrestCreates.Metadata.ContextPack.Tests.csproj`

The exact XML format follows the existing pattern in `CrestCreates.slnx` — `<Project Path="..." />` elements within the appropriate `<Folder>`.

- [ ] **Step 2: Build the entire solution**

Run: `dotnet build CrestCreates.slnx`
Expected: PASS with no errors

- [ ] **Step 3: Run all context pack tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests`
Expected: All 25 tests PASS

- [ ] **Step 4: Verify no regressions in existing tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests`
Expected: All existing tests PASS

- [ ] **Step 5: Commit**

```bash
git add CrestCreates.slnx
git commit -m "feat(context-pack): register projects in solution (#14)"
```
