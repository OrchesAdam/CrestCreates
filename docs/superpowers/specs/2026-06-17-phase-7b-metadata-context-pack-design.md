# Phase 7b — Metadata Context Pack Design

## Goal

Build deterministic, bounded metadata context packs that help humans and LLM agents understand the relevant descriptor universe without loading the entire registry/topology into prompt context.

This phase creates the safe context boundary for future descriptor authoring.

## Design Principle

LLM agents should receive scoped descriptor context, not unrestricted metadata dumps.

```text
Intent / Focus Descriptor
  → topology-aware scope selection
  → descriptor context pack
  → human/agent-readable summary
```

The context pack is read-only and does not create or mutate descriptors.

## Boundaries

- Does not call LLMs.
- Does not generate descriptor drafts.
- Does not review drafts.
- Does not mutate registries, topology snapshots, or descriptor inventories.
- Does not replace topology or impact analysis.
- Does not expose runtime handler execution.
- Does not call `IDescriptorImpactAnalyzer` — `ImpactRadius` is topology-radius traversal only.
- Does not inject or call `IDescriptorLifecycleGovernanceService` — governance entries are lightweight state-only.
- Does not compute binding reports — `BindingStatus` is excluded from Phase 7b.

## Project

Two new projects under `framework/src/`:

- `CrestCreates.Metadata.ContextPack.Abstractions` — public contracts: `IMetadataContextPackBuilder`, `MetadataContextPackRequest`, `MetadataContextPack`, `MetadataContextPackScope`, `RuntimeScenarioRecipe`, `ScenarioTraversalStep`, `ScenarioTraversalDirection`, all entry/summary/diagnostic types
- `CrestCreates.Metadata.ContextPack` — `DefaultMetadataContextPackBuilder` implementation + DI registration

Split rationale: ContextPack contracts will be consumed by UI, CLI, agent tool surfaces, and future authoring phases (7c/7f). Without a separate Abstractions project, those consumers would reference the implementation assembly, creating tight coupling. This follows the established project pattern in the framework (e.g., `CrestCreates.Metadata.Abstractions` / `CrestCreates.Metadata`).

### Dependency Graph

```text
CrestCreates.Metadata.ContextPack.Abstractions
  → CrestCreates.Metadata.Abstractions   (topology, descriptor, relationship types)

CrestCreates.Metadata.ContextPack
  → CrestCreates.Metadata.ContextPack.Abstractions
  → CrestCreates.Metadata.Abstractions
  ✗ does NOT depend on CrestCreates.Metadata       (no impact analyzer, no topology builder)
  ✗ does NOT depend on CrestCreates.DescriptorDraft
  ✗ does NOT depend on CrestCreates.Capability / Event / Workflow / Form / HumanTask / Schema
  ✗ does NOT depend on CrestCreates.Snapshot       (not needed; builder copies arrays directly)
  ✗ does NOT depend on CrestCreates.Snapshot.Abstractions
```

The builder reads only from topology snapshot and descriptor inventory. It never instantiates descriptor-specific types, never calls Phase 6c/6d/6e pipelines, and never accesses active registries.

---

## 1. Contracts and Data Model

Namespace: `CrestCreates.Metadata.ContextPack.Abstractions` for contracts, `CrestCreates.Metadata.ContextPack` for implementation

### 1.1 `MetadataContextPackScope`

```csharp
public enum MetadataContextPackScope
{
    FocusOnly,           // Only the focus descriptors
    DirectDependencies,  // Focus + direct dependencies + edges
    DirectDependents,    // Focus + direct dependents + edges
    ImpactRadius,        // Focus + topology neighborhood within MaxTraversalDepth
    RuntimeScenario      // Focus + recipe-driven traversal
}
```

`ImpactRadius` does not call `IDescriptorImpactAnalyzer` in Phase 7b. It is a topology-radius traversal only.

### 1.2 `ScenarioTraversalDirection`

```csharp
public enum ScenarioTraversalDirection
{
    Dependencies,   // Follow outgoing edges from current node to dependency nodes
    Dependents,     // Follow incoming edges from dependent nodes to current node
    Both            // Follow both outgoing and incoming edges
}
```

### 1.3 `RuntimeScenarioRecipe`

```csharp
public sealed record RuntimeScenarioRecipe
{
    public required string Name { get; init; }
    public required IReadOnlyList<ScenarioTraversalStep> Steps { get; init; }
}
```

The recipe describes traversal only — it does not constrain focus descriptor types. The framework does not hardcode any business scenario names. Company Certification is a sample recipe, not a built-in.

`FocusKinds` is intentionally omitted from the recipe. If focus-kind validation is needed in the future, an optional `AllowedFocusKinds` property and a `CTXPACK_FOCUS_KIND_NOT_ALLOWED` diagnostic can be added. YAGNI for Phase 7b.

### 1.4 `ScenarioTraversalStep`

```csharp
public sealed record ScenarioTraversalStep
{
    public required RelationshipKind FollowKind { get; init; }
    public ScenarioTraversalDirection Direction { get; init; } = ScenarioTraversalDirection.Dependencies;
    public string? Role { get; init; }              // null = any role
    public DescriptorKind? TargetKind { get; init; } // null = any kind
    public int MaxDepth { get; init; } = 1;
}
```

`Direction` specifies which edge direction to follow:
- `Dependencies`: follow outgoing edges from current node to dependency nodes
- `Dependents`: follow incoming edges from dependent nodes to current node
- `Both`: follow both outgoing and incoming edges

### 1.5 `MetadataContextPackRequest`

```csharp
public sealed record MetadataContextPackRequest
{
    public required MetadataContextPackScope Scope { get; init; }
    public required IReadOnlyList<DescriptorRef> FocusDescriptors { get; init; }
    public RuntimeScenarioRecipe? ScenarioRecipe { get; init; }  // Required when Scope = RuntimeScenario
    public string? Intent { get; init; }                          // Reserved, not processed in Phase 7b
    public string? TenantId { get; init; }
    public IReadOnlyList<DescriptorKind>? IncludeKinds { get; init; }   // null = all
    public IReadOnlyList<DescriptorKind>? ExcludeKinds { get; init; }   // null = none
    public int MaxTraversalDepth { get; init; } = 2;
    public int MaxDescriptorCount { get; init; } = 64;
    public bool IncludeStableHashes { get; init; }
    public bool IncludeGovernanceState { get; init; }
}
```

`IncludeDependents` is intentionally omitted. Direction is expressed through:
- `DirectDependencies` / `DirectDependents` scope for single-hop
- `ScenarioTraversalDirection` in recipe steps for `RuntimeScenario`
- `ImpactRadius` follows both directions by default

`IncludeKinds` / `ExcludeKinds` precedence when both are set:
1. `IncludeKinds` first limits candidate set
2. `ExcludeKinds` then removes matches
3. Exclude wins

Focus descriptors are always included regardless of `IncludeKinds` / `ExcludeKinds` filters. If a focus descriptor's kind would be excluded by the filters, it is still included but a `CTXPACK_FOCUS_KIND_FILTERED` warning diagnostic is emitted. This ensures the caller always sees the focus they requested while being informed of the filter mismatch.

### 1.6 `MetadataContextPackDescriptorEntry`

```csharp
public sealed record MetadataContextPackDescriptorEntry
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorState State { get; init; }
    public DescriptorStableHashes? Hashes { get; init; }     // Non-null only when IncludeStableHashes = true
    public MetadataContextPackGovernanceEntry? Governance { get; init; }  // Non-null only when IncludeGovernanceState = true
    public bool IsFocus { get; init; }
}
```

`BindingStatus` is excluded from Phase 7b — the builder does not compute or accept binding reports.

### 1.7 `MetadataContextPackGovernanceEntry`

```csharp
public sealed record MetadataContextPackGovernanceEntry
{
    public required DescriptorState State { get; init; }
    public bool RequiresReview { get; init; }
}
```

This is a lightweight DTO — it is NOT `DescriptorLifecycleGovernanceReport`. Phase 7b populates `State` from the descriptor's `State` property and `RequiresReview` as `true` only for `Draft` state. It does not run the lifecycle governance service.

### 1.8 `MetadataContextPackRelationshipEntry`

```csharp
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

### 1.9 `MetadataContextPackSummary`

```csharp
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

### 1.10 `MetadataContextPackDiagnostic`

```csharp
public enum MetadataContextPackDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record MetadataContextPackDiagnostic
{
    public required MetadataContextPackDiagnosticSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
    public string? Path { get; init; }
}
```

Diagnostic codes:

| Code | Severity | Condition |
|------|----------|-----------|
| `CTXPACK_FOCUS_NOT_FOUND` | Warning | A requested focus descriptor does not exist in the topology |
| `CTXPACK_TRUNCATED_BY_COUNT` | Info | Result was truncated by `MaxDescriptorCount` |
| `CTXPACK_TRUNCATED_BY_DEPTH` | Info | Traversal stopped because `MaxTraversalDepth` prevented reaching additional queued/neighbor nodes |
| `CTXPACK_RECIPE_MISSING` | Error | Scope is `RuntimeScenario` but no recipe was provided |
| `CTXPACK_KIND_EXCLUDED` | Info | Some traversed descriptors were filtered out by `ExcludeKinds` |
| `CTXPACK_FOCUS_KIND_FILTERED` | Warning | A focus descriptor's kind matches `ExcludeKinds` or doesn't match `IncludeKinds`; focus is still included |
| `CTXPACK_HASH_BUILDER_MISSING` | Warning | `IncludeStableHashes = true` but no `IDescriptorStableHashBuilder` is available |

`CTXPACK_TRUNCATED_BY_DEPTH` is emitted only when traversal actually stopped before reaching additional nodes that exist in the topology. It is NOT emitted merely because `MaxTraversalDepth` is set.

### 1.11 `MetadataContextPack`

```csharp
public sealed record MetadataContextPack
{
    public required MetadataContextPackRequest Request { get; init; }
    public required IReadOnlyList<MetadataContextPackDescriptorEntry> Descriptors { get; init; }
    public required IReadOnlyList<MetadataContextPackRelationshipEntry> Relationships { get; init; }
    public required MetadataContextPackSummary Summary { get; init; }
    public required IReadOnlyList<MetadataContextPackDiagnostic> Diagnostics { get; init; }
}
```

The builder snapshots request collections into arrays before storing the request in the pack. This ensures that external mutation of the original `FocusDescriptors`, `IncludeKinds`, or `ExcludeKinds` lists does not affect the pack.

---

## 2. Builder Architecture

### 2.1 `IMetadataContextPackBuilder`

```csharp
public interface IMetadataContextPackBuilder
{
    MetadataContextPack Build(
        MetadataContextPackRequest request,
        DescriptorTopologySnapshot topology,
        IReadOnlyList<IDescriptor> descriptors);
}
```

The builder is stateless — `topology` and `descriptors` are passed as explicit method parameters per call. The builder does not own snapshots, resolve services, or build topology.

Same request + same topology + same descriptor inventory → same context pack.

### 2.2 `DefaultMetadataContextPackBuilder`

```csharp
public sealed class DefaultMetadataContextPackBuilder : IMetadataContextPackBuilder
{
    private readonly IDescriptorStableHashBuilder? _hashBuilder;

    public DefaultMetadataContextPackBuilder(
        IDescriptorStableHashBuilder? hashBuilder = null)
    {
        _hashBuilder = hashBuilder;
    }

    public MetadataContextPack Build(
        MetadataContextPackRequest request,
        DescriptorTopologySnapshot topology,
        IReadOnlyList<IDescriptor> descriptors)
    {
        // ...
    }
}
```

The builder is registered as a singleton. Each `Build()` call receives its own request/topology/descriptors.

`IDescriptorStableHashBuilder` is an optional constructor dependency. When `IncludeStableHashes = true` but `_hashBuilder is null`:
- `Entry.Hashes` remains null (never generate empty/fake hashes)
- Emit `CTXPACK_HASH_BUILDER_MISSING` warning diagnostic

When `IncludeStableHashes = false`, the builder must NOT call the hash builder at all.

### 2.3 Build Algorithm

```text
Build(request, topology, descriptors):
  1. Validate request → emit CTXPACK_RECIPE_MISSING if Scope = RuntimeScenario and recipe is null
  2. Snapshot request collections defensively (copy FocusDescriptors, IncludeKinds, ExcludeKinds into arrays)
  3. Build descriptor index: Dictionary<DescriptorRef, IDescriptor> from descriptors list
  4. Resolve focus descriptor nodes from topology
     - Missing focus → CTXPACK_FOCUS_NOT_FOUND warning, continue with remaining focus
     - All focus missing → return empty pack with diagnostics (no exception)
  5. Scope-driven traversal:
     - FocusOnly: collect focus nodes only, no edges
     - DirectDependencies: focus + topology.GetDirectDependencies() for each focus node
     - DirectDependents: focus + topology.GetDirectDependents() for each focus node
     - ImpactRadius: BFS from focus nodes, following both incoming and outgoing edges, up to MaxTraversalDepth
     - RuntimeScenario: execute recipe steps sequentially (see 2.4)
  6. Apply IncludeKinds / ExcludeKinds filters to non-focus descriptors only
     - IncludeKinds limits candidate set, then ExcludeKinds removes matches; exclude wins
     - Focus descriptors are always included regardless of filters
     - If a focus descriptor's kind would be excluded, emit CTXPACK_FOCUS_KIND_FILTERED warning
  7. Apply MaxDescriptorCount truncation to non-focus descriptors → CTXPACK_TRUNCATED_BY_COUNT if limit reached
     - Focus descriptors are always included (even if over limit); truncation applies to non-focus only
     - If focus count alone exceeds MaxDescriptorCount, include all focus and emit truncation diagnostic
  8. Collect relationship edges for included descriptor pairs
  9. Optionally compute stable hashes (only when IncludeStableHashes = true AND hash builder available)
  10. Optionally populate governance entries (only when IncludeGovernanceState = true)
      - State from descriptor.State, RequiresReview = (State == Draft)
  11. Build summary and diagnostics
  12. Sort output deterministically
  13. Return MetadataContextPack
```

### 2.4 RuntimeScenario Traversal

Execute `ScenarioTraversalStep` entries sequentially. Each step:

1. Start from the current boundary (focus descriptors for the first step)
2. For each node in the boundary, look up edges matching `FollowKind` and optional `Role`
3. Based on `Direction`:
   - `Dependencies`: follow outgoing edges from current node to dependency nodes
   - `Dependents`: follow incoming edges from dependent nodes to current node
   - `Both`: follow both outgoing and incoming edges
4. Filter target nodes by `TargetKind` if specified
5. Collect matching nodes and edges
6. Repeat until `MaxDepth` reached
7. Boundary expands for the next step

The builder maintains a visited set per step to prevent infinite loops from self-cycles or re-entrant edges. The builder does NOT rely on topology diagnostics for cycle protection — it enforces its own visited set during traversal.

### 2.5 ImpactRadius Traversal

BFS from focus nodes, following both incoming and outgoing edges, up to `MaxTraversalDepth`:
- Depth 0: focus nodes
- Depth 1: immediate neighbors (both directions)
- Depth 2: neighbors-of-neighbors
- Continue until `MaxTraversalDepth` reached

The builder maintains a visited set to prevent revisiting nodes. If the BFS frontier has unvisited nodes when `MaxTraversalDepth` stops traversal, emit `CTXPACK_TRUNCATED_BY_DEPTH`.

### 2.6 Deterministic Output Ordering

All output lists use `StringComparer.Ordinal`. Sorting keys include version to ensure stable ordering when multiple versions of the same descriptor coexist:

1. Descriptors: focus first (sorted by `Namespace`, then `Id`, then `Version ?? -1`), then non-focus (sorted by `Kind`, then `Namespace`, then `Id`, then `Version ?? -1`)
2. Relationships: sorted by `From` (`Namespace`, `Id`, `Version ?? -1`), then `To` (`Namespace`, `Id`, `Version ?? -1`), then `Kind`
3. Diagnostics: sorted by `Severity` (Error > Warning > Info), then `Code`, then `Subject` (`Namespace`, `Id`, `Version ?? -1`)

Note: `DescriptorRef.FullId` is `{Namespace}.{Id}` and does not include version. Sorting by `FullId` alone produces ties for multi-version descriptors. The ordering uses the decomposed key `(Namespace, Id, Version)` instead of `FullId` to guarantee determinism.

Same semantic graph + different input order → same output order.

### 2.7 DI Registration

```csharp
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

The builder is stateless, so singleton is appropriate. `IDescriptorStableHashBuilder` is resolved from DI via constructor injection (nullable, DI provides it if registered).

### 2.8 Internal Method Decomposition

The builder is a single class with private methods, not a public pipeline:

```text
ValidateRequest()
SnapshotRequest()
BuildDescriptorIndex()
ResolveFocusNodes()
ResolveFocusOnly()
ResolveDirectDependencies()
ResolveDirectDependents()
ResolveImpactRadius()
ResolveRuntimeScenario()
ApplyKindFilters()
ApplyCountBounds()
CollectRelationshipEdges()
BuildDescriptorEntries()
BuildSummary()
SortOutput()
```

Do NOT split into public pipeline interfaces in Phase 7b.

---

## 3. Test Matrix

Test project: `CrestCreates.Metadata.ContextPack.Tests`

### A. Scope Traversal

| # | Test | Key Assertion |
|---|------|---------------|
| 1 | `FocusOnly_Returns_Only_Requested_Descriptors` | 3 focus descriptors, all `IsFocus = true`, no relationships |
| 2 | `DirectDependencies_Includes_Dependencies_And_Edges` | Capability → Schema (Uses, Strong), includes both, edge present |
| 3 | `DirectDependents_Includes_Dependents_And_Edges` | Event ← Capability (Produces), includes both, edge present |
| 4 | `ImpactRadius_Respects_MaxTraversalDepth` | 4-level chain Schema ← Cap ← Workflow ← HumanTask, depth=2 stops at Workflow, `CTXPACK_TRUNCATED_BY_DEPTH` |
| 5 | `RuntimeScenario_Executes_Recipe_Steps` | Workflow focus, recipe follows step targets → includes Capabilities, HumanTasks, Events (see 3.1) |

### B. Bounds and Filters

| # | Test | Key Assertion |
|---|------|---------------|
| 6 | `MaxDescriptorCount_Truncates_And_Emits_Diagnostic` | 10-descriptor topology, limit=5, `CTXPACK_TRUNCATED_BY_COUNT`, `TruncatedAtCount = 5` |
| 7 | `IncludeKinds_Limits_Candidates` | Only Schema descriptors included when `IncludeKinds = [Schema]` |
| 8 | `ExcludeKinds_Removes_Matches` | Schema excluded from result, `CTXPACK_KIND_EXCLUDED` diagnostic |
| 9 | `Include_And_Exclude_Precedence` | `IncludeKinds = [Schema, Capability]` + `ExcludeKinds = [Schema]` → only Capability; exclude wins |
| 10 | `Focus_Always_Included_Despite_Kind_Filters` | Focus = Capability, `ExcludeKinds = [Capability]` → Capability still included with `CTXPACK_FOCUS_KIND_FILTERED` warning |

### C. Diagnostics

| # | Test | Key Assertion |
|---|------|---------------|
| 11 | `Unknown_Focus_Produces_Diagnostic_Not_Exception` | Non-existent focus → empty pack + `CTXPACK_FOCUS_NOT_FOUND` warning, no exception |
| 12 | `Mixed_Known_And_Unknown_Focus_Continues_With_Known` | 2 focus refs (1 valid, 1 missing) → includes valid focus + its traversal, warning for missing |
| 13 | `RuntimeScenario_Without_Recipe_Emits_Error` | Scope = RuntimeScenario, recipe = null → `CTXPACK_RECIPE_MISSING` error |
| 14 | `Truncated_By_Depth_Only_When_Unvisited_Nodes_Exist` | `MaxTraversalDepth = 10` on shallow graph → no `CTXPACK_TRUNCATED_BY_DEPTH` |
| 15 | `Hash_Builder_Missing_Emits_Warning` | `IncludeStableHashes = true`, no hash builder → `CTXPACK_HASH_BUILDER_MISSING`, `Hashes = null` |

### D. Determinism and Safety

| # | Test | Key Assertion |
|---|------|---------------|
| 16 | `Deterministic_Output_Ordering` | Same input twice → identical descriptor/relationship/diagnostic order |
| 17 | `Shuffled_Input_Still_Deterministic` | Same semantic graph, different input order → same output order |
| 18 | `Self_Cycle_Terminates` | Recipe with self-loop → builder visited set prevents infinite traversal, descriptor appears once |
| 19 | `Builder_Is_Read_Only` | Topology snapshot and descriptor list unchanged after Build() |
| 20 | `Request_Collections_Are_Snapshotted` | Mutating original FocusDescriptors list after Build() does not affect pack.Request.FocusDescriptors |

### E. Optional Enrichment

| # | Test | Key Assertion |
|---|------|---------------|
| 21 | `Stable_Hashes_Omitted_By_Default` | `IncludeStableHashes = false` → all `Hashes = null` |
| 22 | `Stable_Hashes_Included_When_Requested` | `IncludeStableHashes = true` + hash builder → `Hashes` non-null |
| 23 | `Stable_Hashes_Not_Computed_When_Not_Requested` | `IncludeStableHashes = false` → hash builder call count = 0 |
| 24 | `Governance_State_From_Descriptor_State_Only` | `IncludeGovernanceState = true` → entry has `State` from descriptor, `RequiresReview = true` for Draft only |
| 25 | `Intent_Is_Ignored_In_Phase7b` | Same request with different Intent → identical descriptors/relationships |

### 3.1 RuntimeScenario Golden Test Detail

Test 5 uses Workflow as focus (not Capability), because business runtime chains are typically workflow-centric.

The recipe must match the actual `DescriptorRelationship` directions produced by the extractors:

```text
WorkflowRelationshipExtractor produces (all outgoing from workflow):
  Workflow → Capability  (Triggers, Role=CapabilityStep, Strong, IsRuntimeBinding=true)
  Workflow → HumanTask   (Triggers, Role=HumanTaskStep, Strong, IsRuntimeBinding=true)
  Workflow → Schema      (Uses, Role=VariableSchema, Strong)

HumanTaskRelationshipExtractor produces (all outgoing from human task):
  HumanTask → Form       (Uses, Role=Interaction, Strong)
  HumanTask → Capability (Triggers, Role=Outcome, Strong, IsRuntimeBinding=true)
  HumanTask → Schema     (Consumes/Produces, Strong)

CapabilityRelationshipExtractor produces (all outgoing from capability):
  Capability → Schema    (Consumes/Produces, Strong)
  Capability → Event     (Produces, Weak)
  Capability → Event     (Consumes, Weak)

EventRelationshipExtractor produces (all outgoing from event):
  Event → Schema         (Uses, Role=PayloadSchema, Strong)
```

Since all edges are outgoing (From = owner descriptor), following `Dependencies` direction (outgoing edges) from a focus node reaches its targets.

```text
Topology for golden test:
  CompanyCertificationWorkflow
    --Triggers/CapabilityStep--> SubmitCapability
    --Triggers/HumanTaskStep--> ReviewHumanTask
    --Triggers/CapacityStep--> ApproveCapability
  ReviewHumanTask
    --Triggers/Outcome--> ApproveCapability
  ApproveCapability
    --Produces--> ApprovedEvent

Recipe (2 steps):
  Step 1: Direction = Dependencies, FollowKind = Triggers (no TargetKind filter)
    From Workflow → reaches SubmitCapability, ReviewHumanTask, ApproveCapability
  Step 2: Direction = Dependencies, FollowKind = Produces, TargetKind = Event
    From boundary (SubmitCapability, ReviewHumanTask, ApproveCapability) → reaches ApprovedEvent

Focus = CompanyCertificationWorkflow
```

The test verifies:
- All step targets are included (SubmitCapability, ReviewHumanTask, ApproveCapability, ApprovedEvent)
- Boundary from step N becomes input boundary for step N+1 (step 2 starts from all nodes discovered in step 1)
- TargetKind / relationship kind / direction filters are respected
- The recipe correctly uses Dependencies direction because all relationship edges are outgoing from owner

### 3.2 Empty FocusDescriptors

Empty `FocusDescriptors` list returns an empty context pack with no diagnostics (not an error). Future agents may construct empty context packs intentionally.

---

## 4. Summary of Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Project structure | Abstractions + Implementation split | Contracts consumed by UI/CLI/agent tools without coupling to implementation |
| Topology input | Explicit method parameter | Builder is stateless, testable, no hidden service resolution |
| RuntimeScenario | Explicit recipe, not named scenarios | Framework doesn't hardcode business names; recipes are composable |
| ImpactRadius | Topology BFS, not impact analyzer | 7b is context projection, not analysis; avoids Phase 6c dependency |
| Summary format | Structured record | Deterministic, machine-readable, easy to serialize |
| Intent field | Reserved, not processed | Forward-compatible slot for future LLM augmentation |
| BindingStatus | Excluded from Phase 7b | Avoids binding analysis dependency; add later if needed |
| GovernanceState | Lightweight DTO from descriptor State only | No governance service injection; `RequiresReview` from Draft state |
| IncludeDependents | Omitted; direction via Scope/recipe | Avoids semantic ambiguity with scope types |
| Focus vs kind filters | Focus always included; `CTXPACK_FOCUS_KIND_FILTERED` if filter would exclude focus | Caller always sees requested focus; diagnostic signals filter mismatch |
| Hash builder missing | `Hashes = null` + warning diagnostic | Never generate empty/fake hashes |
| DI registration | `TryAddSingleton` with DI-resolved optional dependencies | Standard .NET DI pattern, no manual builder construction |
| Output ordering | Ordinal comparison, (Namespace, Id, Version) key, focus-first | Deterministic for prompt snapshots; version included to break ties |
| Builder internal structure | Single class with private methods | No public pipeline interfaces in Phase 7b |
| Request collection safety | Defensive snapshot into arrays | Pack.Request unaffected by external mutation |
| Snapshot.Abstractions dependency | Not needed | Builder copies arrays directly; no `ISnapshotable<T>` usage |
