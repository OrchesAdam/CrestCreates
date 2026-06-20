# Agent Visibility PR B Aggregate Graph Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute descriptor/draft aggregates, topology, relationships, and context packs entirely inside the PR-A visible descriptor universe.

**Architecture:** Broad queries filter typed sources before ordering, limits, totals, and diagnostics. Graph and context operations rebuild from visible descriptors so denied nodes and incident edges never exist in downstream snapshots. Explicit subjects and focus refs are resolved and denied before builders run; completed coverage entries replace only their corresponding migration guards.

**Tech Stack:** .NET 10, C# 14, xUnit 2.9.3, FluentAssertions, Moq, CrestCreates Metadata topology/context-pack abstractions

---

**Prerequisite:** PR A plan is implemented and merged; its evaluator, visibility scope, resolver, migration guard, and coverage registry are the only policy path.

**Files map:**
- Create `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentVisibleDescriptorUniverse.cs`: materialize and classify one complete catalog snapshot.
- Create `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentTopologyVisibilityProjector.cs`: visible-only topology/relationship construction contract.
- Modify `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`: aggregate, graph, and context implementations.
- Modify `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs`: mark PR-B entries complete.
- Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AggregateVisibilityTests.cs`.
- Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/GraphVisibilityTests.cs`.
- Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/ContextPackVisibilityTests.cs`.
- Modify `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AgentControlPlaneTestBase.cs`: visible graph fixtures.

### Task 1: Visible descriptor universe and broad search

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentVisibleDescriptorUniverse.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AggregateVisibilityTests.cs`

- [ ] **Step 1: Write failing search tests**

Create Event and Workflow descriptors under a policy allowing Workflow and denying Event. Assert broad search returns only Workflow, `TotalCount == 1`, `WasTruncated` is computed after filtering, and `MaxResults` applies after deterministic ordering by namespace, ID, and version. Assert explicit Event search is `Denied`, not empty. Make catalog enumeration throw and assert `Failed` with `Value == null`.

- [ ] **Step 2: Run search tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~AggregateVisibilityTests`

Expected: broad search is still migration-blocked.

- [ ] **Step 3: Implement one materialized visible universe**

```csharp
internal sealed record AgentVisibleDescriptorUniverse(
    IReadOnlyList<IDescriptor> AllTenantDescriptors,
    IReadOnlyList<IDescriptor> VisibleDescriptors)
{
    public static AgentVisibleDescriptorUniverse Create(
        IEnumerable<IDescriptor> source,
        AgentDescriptorVisibilityScope scope)
    {
        var all = source.ToList().AsReadOnly();
        if (all.Any(d => !Enum.IsDefined(d.Kind)))
            throw new InvalidOperationException("Catalog contains an invalid descriptor kind.");
        return new(all, scope.Filter(all, d => d.Kind));
    }
}
```

Search uses `VisibleDescriptors`, applies request filters, orders deterministically, then computes total/truncation and takes `MaxResults`. Emit `RESULTS_SECURITY_TRIMMED` whenever `scope.IsRestricted`, regardless of whether a hidden row existed; the message must contain no kind name or count.

Add the shared aggregate failure/diagnostic members in the facade so later PR-B/PR-C steps do not invent their own error form:

```csharp
private static readonly IReadOnlyList<AgentToolDiagnostic> SecurityTrimmedDiagnostics =
[
    new AgentToolDiagnostic
    {
        Code = "RESULTS_SECURITY_TRIMMED",
        Severity = AgentToolDiagnosticSeverity.Info,
        Message = "Results reflect the invocation's descriptor visibility scope."
    }
];

private async Task<AgentToolResult<T>> RecordAggregateFailure<T>(
    AgentToolInvocationContext context, string code, CancellationToken ct) where T : class
{
    var diagnostic = new AgentToolDiagnostic
    {
        Code = code,
        Severity = AgentToolDiagnosticSeverity.Error,
        Message = "The visible aggregate could not be constructed safely."
    };
    var audit = BuildAudit(context, AgentToolResultStatus.Failed, [diagnostic]);
    await _auditor.RecordAsync(audit, ct);
    return AgentToolResult<T>.Failed([diagnostic], audit);
}
```

- [ ] **Step 4: Run aggregate tests and verify GREEN**

Run the Step 2 command.

Expected: all search tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentVisibleDescriptorUniverse.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AggregateVisibilityTests.cs
git commit -m "feat(agent): filter descriptor search by visibility"
```

### Task 2: Draft aggregate filtering

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AggregateVisibilityTests.cs`

- [ ] **Step 1: Add failing draft-list tests**

Return mixed Event/Workflow drafts from `IDescriptorDraftStore.ListAsync`. Assert Event is omitted, `TotalCount` equals visible drafts, touched audit IDs contain only visible drafts, and a store exception yields `Failed` with no partial value. If `DraftQuery` currently pages internally, assert the service requests an unpaged tenant list and applies query ordering/paging after visibility; if `DraftQuery` has no paging members, preserve its existing filters and still filter before result count.

- [ ] **Step 2: Run tests and verify RED**

Run the Task 1 aggregate command.

Expected: list is migration-blocked or exposes Event.

- [ ] **Step 3: Implement visible draft list**

Call `_draftStore.ListAsync(context.TenantId, query, ct)` once, reject invalid draft kinds, filter with `scope.Filter(drafts, d => d.DescriptorKind)`, and construct `DescriptorDraftListResult` plus audit from the filtered list only. Add the non-probing restricted-scope diagnostic.

```csharp
var drafts = await _draftStore.ListAsync(context.TenantId, query, ct);
if (drafts.Any(d => !Enum.IsDefined(d.DescriptorKind)))
    return await RecordAggregateFailure<DescriptorDraftListResult>(context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
var visible = scope.Filter(drafts, d => d.DescriptorKind);
var result = new DescriptorDraftListResult { Drafts = visible, TotalCount = visible.Count };
var diagnostics = scope.IsRestricted ? SecurityTrimmedDiagnostics : [];
var audit = BuildAudit(context, AgentToolResultStatus.Success, diagnostics) with
{
    TouchedDraftIds = visible.Select(d => d.DraftId).ToList().AsReadOnly()
};
await _auditor.RecordAsync(audit, ct);
return AgentToolResult<DescriptorDraftListResult>.Success(result, diagnostics, audit);
```

- [ ] **Step 4: Run tests and verify GREEN**

Run the Task 1 aggregate command.

Expected: search and draft aggregate tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AggregateVisibilityTests.cs
git commit -m "feat(agent): security trim draft aggregates"
```

### Task 3: Topology and relationship closure

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentTopologyVisibilityProjector.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AgentControlPlaneTestBase.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/GraphVisibilityTests.cs`

- [ ] **Step 1: Write failing graph tests**

Build an allowed Workflow node, denied Event node, allowed Schema node, and edges Workflow->Event and Workflow->Schema. Assert summary contains two nodes, one edge, no Event key, recomputed edge counts, and no diagnostic referencing Event. Assert allowed relationship subject returns only the Schema edge; denied subject is `Denied`; unknown subject is `NotFound`; builder receives only visible descriptors.

- [ ] **Step 2: Run graph tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~GraphVisibilityTests`

Expected: graph tools remain migration-blocked.

- [ ] **Step 3: Implement visible graph construction**

```csharp
internal sealed class AgentTopologyVisibilityProjector
{
    public DescriptorTopologySnapshot BuildVisible(
        AgentVisibleDescriptorUniverse universe,
        IDescriptorTopologyBuilder builder) => builder.Build(universe.VisibleDescriptors);
}
```

Do not project an unrestricted topology after building it. For relationships, resolve the explicit subject against the same universe, deny when invisible, then build topology from `VisibleDescriptors`; extract incoming/outgoing edges from that snapshot. For summary, group only visible snapshot nodes/edges and map only diagnostics generated by that visible build.

- [ ] **Step 4: Run graph tests and verify GREEN**

Run the Step 2 command.

Expected: all graph closure tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentTopologyVisibilityProjector.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AgentControlPlaneTestBase.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/GraphVisibilityTests.cs
git commit -m "feat(agent): build topology from visible descriptors"
```

### Task 4: Metadata and runtime context-pack closure

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/ContextPackVisibilityTests.cs`

- [ ] **Step 1: Write failing context tests**

For both context-pack tools, assert denied explicit focus returns `Denied` before topology/context builder calls. For allowed focus with a denied neighboring node, capture `IDescriptorTopologyBuilder.Build` and `IMetadataContextPackBuilder.Build` arguments and assert neither receives the denied descriptor. Assert `IncludeKinds` containing a denied kind is denied, traversal output/touched refs contains no denied ref, and builder failure returns no partial pack.

- [ ] **Step 2: Run context tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~ContextPackVisibilityTests`

Expected: context tools remain migration-blocked.

- [ ] **Step 3: Implement common visible context helper**

Add one private method used by both public context methods:

```csharp
private Task<AgentToolResult<MetadataContextPack>> BuildContextPackAsync(
    AgentToolInvocationContext context,
    MetadataContextPackRequest request,
    AgentDescriptorVisibilityScope scope,
    CancellationToken ct)
```

Resolve every `FocusDescriptors` ref version-aware against the materialized catalog; `NotFound` for absent focus, `Denied` for denied focus. Evaluate every explicit `IncludeKinds` value and deny if invisible. Build topology and context pack from `universe.VisibleDescriptors`. Audit only `pack.Summary.FocusRefs` after verifying each returned ref is present in the visible set.

- [ ] **Step 4: Run context and graph tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~ContextPackVisibilityTests|FullyQualifiedName~GraphVisibilityTests"`

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/ContextPackVisibilityTests.cs
git commit -m "feat(agent): close context packs over visible universe"
```

### Task 5: Coverage transition and PR B merge gate

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/VisibilityCoverageTests.cs`

- [ ] **Step 1: Mark only delivered PR-B entries complete**

Mark `BuildMetadataContextPack`, `BuildRuntimeScenarioContextPack`, broad `SearchDescriptors`, `ListDescriptorRelationships`, `GetTopologySummary`, and `ListDescriptorDrafts` complete. Keep review/fix/package/readiness/activation and nested artifact entries blocked for PR C.

```csharp
new(AgentToolName.BuildMetadataContextPack, AgentToolResourceShape.ContextPack, AgentVisibilityMigrationState.Complete),
new(AgentToolName.BuildRuntimeScenarioContextPack, AgentToolResourceShape.ContextPack, AgentVisibilityMigrationState.Complete),
new(AgentToolName.SearchDescriptors, AgentToolResourceShape.Aggregate, AgentVisibilityMigrationState.Complete),
new(AgentToolName.ListDescriptorRelationships, AgentToolResourceShape.Graph, AgentVisibilityMigrationState.Complete),
new(AgentToolName.GetTopologySummary, AgentToolResourceShape.Graph, AgentVisibilityMigrationState.Complete),
new(AgentToolName.ListDescriptorDrafts, AgentToolResourceShape.Aggregate, AgentVisibilityMigrationState.Complete),
new(AgentToolName.ListDraftReviewResults, AgentToolResourceShape.Indirect, AgentVisibilityMigrationState.BlockedUntilMigrated)
```

- [ ] **Step 2: Run all focused tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests`

Expected: all tests pass; PR-C tools still return the migration denial under visibility-sensitive policies.

- [ ] **Step 3: Run build gates**

Run: `dotnet format CrestCreates.slnx --verify-no-changes`

Expected: exit 0.

Run: `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests`

Expected: all pass.

Run: `dotnet build CrestCreates.slnx --no-restore`

Expected: 0 errors.

Run: `git diff --check`

Expected: no output.

- [ ] **Step 4: Commit coverage state**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/VisibilityCoverageTests.cs
git commit -m "test(agent): gate aggregate visibility coverage"
```

**PR B merge gate:** Visible filtering precedes totals and limits; graph/context builders never receive denied descriptors; focus/subject probes are denied; every undelivered indirect/nested entry remains fail-closed.
