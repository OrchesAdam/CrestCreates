# Agent Visibility PR A Policy Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish one fail-closed descriptor-kind policy and invocation pipeline, migrate direct descriptor/draft tools, and block every descriptor-bearing tool not yet migrated.

**Architecture:** Coarse tool authorization remains first and performs zero resource reads. An immutable visibility scope then evaluates typed `DescriptorKind` values from a tenant-safe resolver snapshot; direct execution consumes that same snapshot. A manifest coverage registry declares every tool's resource shape and migration state, and the facade rejects descriptor-bearing entries whose migration state is not complete.

**Tech Stack:** .NET 10, C# 14, xUnit 2.9.3, FluentAssertions, Moq, Microsoft.Extensions.DependencyInjection

---

**Prerequisite:** Approved spec `docs/superpowers/specs/2026-06-19-agent-descriptor-kind-visibility-closure-design.md` is merged. Execute in a clean worktree created with `superpowers:using-git-worktrees`.

**Files map:**
- Modify `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/AgentToolAuthorizationOptions.cs`: add production closed-world `AllowedDescriptorKinds` configuration.
- Create `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorKindPolicyEvaluator.cs`: normalize typed kinds and compute deny-wins effective visibility.
- Create `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorVisibilityScope.cs`: immutable per-invocation visibility snapshot.
- Create `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs`: authoritative resource-shape/migration registry.
- Create `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneResourceResolver.cs`: version-aware descriptor and tenant-safe draft snapshots.
- Modify `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentToolAuthorizationService.cs`: remove independent kind-policy interpretation.
- Modify `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/IAgentToolAuthorizationService.cs`: remove the nullable string kind helper after facade migration.
- Modify `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`: staged pipeline and direct-tool snapshot reuse.
- Modify `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneServiceCollectionExtensions.cs`: register internal policy/resolver services.
- Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorVisibilityPolicyTests.cs`.
- Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorVisibilityPipelineTests.cs`.
- Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/VisibilityCoverageTests.cs`.
- Modify `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AgentControlPlaneTestBase.cs`: construct the new dependencies.

### Task 1: Typed policy evaluator and closed-world options

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/AgentToolAuthorizationOptions.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorKindPolicyEvaluator.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorVisibilityPolicyTests.cs`

- [ ] **Step 1: Write failing mode and deny-wins tests**

Add tests that instantiate the evaluator with `DevelopmentDefaults`, `ProductionDefaults`, and explicit options. Use `DescriptorKind.Event` and `DescriptorKind.Workflow`; assert development permits a valid unlisted kind, production denies a kind absent from `AllowedDescriptorKinds`, explicit allow permits it, and deny overrides allow. Also cast `int.MaxValue` to `DescriptorKind` and assert `Invalid`.

```csharp
[Fact]
public void Production_Is_Closed_World_And_Deny_Wins()
{
    var options = AgentToolAuthorizationOptions.ProductionDefaults with
    {
        AllowedDescriptorKinds = [nameof(DescriptorKind.Event), nameof(DescriptorKind.Workflow)],
        DeniedDescriptorKinds = [nameof(DescriptorKind.Event)]
    };
    var evaluator = new AgentDescriptorKindPolicyEvaluator(options);

    evaluator.Evaluate(DescriptorKind.Event).Should().Be(AgentDescriptorKindDecision.Denied);
    evaluator.Evaluate(DescriptorKind.Workflow).Should().Be(AgentDescriptorKindDecision.Visible);
    evaluator.Evaluate(DescriptorKind.Schema).Should().Be(AgentDescriptorKindDecision.Denied);
}
```

- [ ] **Step 2: Run the policy test and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~DescriptorVisibilityPolicyTests`

Expected: compile failure because `AllowedDescriptorKinds`, `AgentDescriptorKindPolicyEvaluator`, and `AgentDescriptorKindDecision` do not exist.

- [ ] **Step 3: Add options and the evaluator**

Add `HashSet<string> AllowedDescriptorKinds` to the options. Implement:

```csharp
internal enum AgentDescriptorKindDecision { Visible, Denied, Invalid }

internal sealed class AgentDescriptorKindPolicyEvaluator
{
    private readonly bool _openWorld;
    private readonly IReadOnlySet<string> _allowed;
    private readonly IReadOnlySet<string> _denied;
    public bool HasRestrictions => !_openWorld || _denied.Count != 0;

    public AgentDescriptorKindPolicyEvaluator(AgentToolAuthorizationOptions options)
    {
        _openWorld = options.Mode == AgentToolAuthorizationMode.DevelopmentAllowAll;
        _allowed = options.AllowedDescriptorKinds.ToHashSet(StringComparer.Ordinal);
        _denied = options.DeniedDescriptorKinds.ToHashSet(StringComparer.Ordinal);
    }

    public AgentDescriptorKindDecision Evaluate(DescriptorKind kind)
    {
        if (!Enum.IsDefined(kind)) return AgentDescriptorKindDecision.Invalid;
        var canonical = kind.ToString();
        if (_denied.Contains(canonical)) return AgentDescriptorKindDecision.Denied;
        return _openWorld || _allowed.Contains(canonical)
            ? AgentDescriptorKindDecision.Visible
            : AgentDescriptorKindDecision.Denied;
    }
}
```

Document that production/locked-down callers must populate `AllowedDescriptorKinds`; empty means no descriptor visibility. Legacy policy conversion forwards denies and remains closed-world.

- [ ] **Step 4: Run policy tests and verify GREEN**

Run the command from Step 2.

Expected: all `DescriptorVisibilityPolicyTests` pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/AgentToolAuthorizationOptions.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorKindPolicyEvaluator.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorVisibilityPolicyTests.cs
git commit -m "feat(agent): add descriptor visibility policy evaluator"
```

### Task 2: Visibility scope and bidirectional coverage registry

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorVisibilityScope.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/VisibilityCoverageTests.cs`

- [ ] **Step 1: Write failing scope and coverage tests**

Define tests that assert `Filter` preserves order and removes denied kinds. Add a table test that compares manifest names and coverage names in both directions, asserts both sets contain no duplicates, and does not assert a fixed count.

```csharp
manifestNames.Should().OnlyHaveUniqueItems();
coverageNames.Should().OnlyHaveUniqueItems();
manifestNames.ToHashSet(StringComparer.Ordinal)
    .SetEquals(coverageNames).Should().BeTrue();
```

Assert `ListAgentTools` and `GetAgentToolDescriptor` use `None/Complete`; the 4 PR-A direct groups use `DirectKind` or `SingleResource/Complete`; the remaining descriptor-bearing tools use their final resource shape with `BlockedUntilMigrated`.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~VisibilityCoverageTests|FullyQualifiedName~DescriptorVisibilityPolicyTests"`

Expected: compile failure for missing scope and coverage types.

- [ ] **Step 3: Implement focused types**

```csharp
internal sealed class AgentDescriptorVisibilityScope
{
    private readonly AgentDescriptorKindPolicyEvaluator _evaluator;
    public string TenantId { get; }
    public bool IsRestricted => _evaluator.HasRestrictions;
    public AgentDescriptorVisibilityScope(string tenantId, AgentDescriptorKindPolicyEvaluator evaluator)
    {
        TenantId = tenantId;
        _evaluator = evaluator;
    }
    public AgentDescriptorKindDecision EvaluateExplicit(DescriptorKind kind) => _evaluator.Evaluate(kind);
    public bool IsVisible(DescriptorKind kind) => _evaluator.Evaluate(kind) == AgentDescriptorKindDecision.Visible;
    public IReadOnlyList<T> Filter<T>(IEnumerable<T> source, Func<T, DescriptorKind> selector) =>
        source.Where(item => IsVisible(selector(item))).ToList().AsReadOnly();
}

internal enum AgentToolResourceShape { None, DirectKind, SingleDescriptor, SingleDraft, Aggregate, Graph, ContextPack, Indirect, Nested }
internal enum AgentVisibilityMigrationState { Complete, BlockedUntilMigrated }
internal sealed record AgentToolVisibilityEntry(string ToolName, AgentToolResourceShape Shape, AgentVisibilityMigrationState State);
```

Populate `AgentToolVisibilityCoverage.All` with each of the 30 names from `AgentToolName`, exactly once. Mark `CreateDescriptorDraft`, `GetDescriptorByRef`, `UpdateDescriptorDraft`, `GetDescriptorDraft`, `CancelDescriptorDraft`, `CompareDescriptorDraft`, `ValidateDescriptorDraft`, plus manifest tools complete. Keep aggregate/context/indirect/nested tools blocked for PR B/C. `SearchDescriptors` is marked complete only for explicit-kind mode; broad mode is guarded until PR B by the facade.

- [ ] **Step 4: Run tests and verify GREEN**

Run the Step 2 command.

Expected: scope and bidirectional coverage tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorVisibilityScope.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/VisibilityCoverageTests.cs
git commit -m "feat(agent): define visibility scope and tool coverage"
```

### Task 3: Tenant-safe direct resource snapshots

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneResourceResolver.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorVisibilityPipelineTests.cs`

- [ ] **Step 1: Write failing resolver tests**

Test exact version matching, ambiguous unpinned refs, current-tenant draft lookup, missing draft, and cancellation propagation. Verify a versioned descriptor is selected using namespace, ID, and version, not namespace/ID alone.

- [ ] **Step 2: Run resolver tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~DescriptorVisibilityPipelineTests`

Expected: compile failure because resolver snapshot types do not exist.

- [ ] **Step 3: Implement resolver and immutable outcomes**

```csharp
internal sealed record DescriptorResourceSnapshot(IDescriptor Descriptor, DescriptorRef Ref);
internal sealed record DraftResourceSnapshot(Draft Draft);
internal enum ResourceResolutionStatus { Resolved, NotFound, Ambiguous }
internal sealed record ResourceResolution<T>(ResourceResolutionStatus Status, T? Snapshot) where T : class;

internal sealed class AgentControlPlaneResourceResolver
{
    public Task<ResourceResolution<DraftResourceSnapshot>> ResolveDraftAsync(
        string tenantId, string draftId, CancellationToken ct);
    public ResourceResolution<DescriptorResourceSnapshot> ResolveDescriptor(DescriptorRef descriptorRef);
}
```

`ResolveDraftAsync` calls `_draftStore.GetAsync(tenantId, draftId, ct)` once. `ResolveDescriptor` materializes `_descriptorCatalog.GetAll()` once; pinned refs require `IVersionedDescriptor.Version`, unpinned refs with multiple matches return `Ambiguous`.

- [ ] **Step 4: Run resolver tests and verify GREEN**

Run the Step 2 command.

Expected: resolver tests pass and cancellation is observed as `OperationCanceledException`.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneResourceResolver.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorVisibilityPipelineTests.cs
git commit -m "feat(agent): add tenant-safe visibility snapshots"
```

### Task 4: Split coarse authorization from visibility and add migration guard

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/IAgentToolAuthorizationService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentToolAuthorizationService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneServiceCollectionExtensions.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AgentControlPlaneTestBase.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorVisibilityPipelineTests.cs`

- [ ] **Step 1: Write failing pipeline-order tests**

For forged tool name, denied actor, denied tool, ungranted mutation, and ungranted activation permission, verify `DraftStoreMock.Verify(..., Times.Never)` and `DescriptorCatalogMock.Verify(..., Times.Never)`. Invoke a blocked PR-B tool under an otherwise permissive policy and assert `Denied` with `DESCRIPTOR_VISIBILITY_MIGRATION_REQUIRED` and zero data reads.

- [ ] **Step 2: Run pipeline tests and verify RED**

Run the focused pipeline command from Task 3.

Expected: blocked tools currently execute and touch stores/catalogs.

- [ ] **Step 3: Implement the staged pipeline**

Replace `Func<Task<string?>>? kindResolver` with a coarse wrapper that creates a scope after authorization:

```csharp
private async Task<AgentToolResult<T>> ExecuteAsync<T>(
    AgentToolInvocationContext context,
    string expectedToolName,
    string permissionName,
    Func<AgentDescriptorVisibilityScope, CancellationToken, Task<AgentToolResult<T>>> action,
    CancellationToken ct) where T : class
```

Order: tool-name integrity, manifest lookup, coarse `AuthorizeAsync`, coverage migration guard, scope creation, action. Catch `OperationCanceledException` separately and rethrow. Delete the facade's nullable-string kind check and stop calling `IsDescriptorKindDenied`; remove that method from `IAgentToolAuthorizationService`, `DefaultAgentToolAuthorizationService`, and authorization mocks/tests in the same change. Register evaluator/resolver using factories bound to the same immutable options instance.

- [ ] **Step 4: Run pipeline tests and verify GREEN**

Run the Task 3 focused command.

Expected: all pipeline-order and migration-guard tests pass with zero forbidden reads.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/IAgentToolAuthorizationService.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentToolAuthorizationService.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneServiceCollectionExtensions.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AgentControlPlaneTestBase.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorVisibilityPipelineTests.cs
git commit -m "refactor(agent): separate coarse authorization and visibility"
```

### Task 5: Migrate direct descriptor and draft tools with snapshot reuse

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorVisibilityPipelineTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorKindDenyTests.cs`

- [ ] **Step 1: Write failing direct-operation tests**

Cover: denied create before save; denied exact-version descriptor; ambiguous ref; unresolved direct target returns `AUTHORIZATION_CONTEXT_UNAVAILABLE` without action; direct draft update/get/cancel/compare/validate reads once; a first-read failure cannot be followed by a successful second read; same-tenant denied target is internally `Denied`.

- [ ] **Step 2: Run direct tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~DescriptorVisibilityPipelineTests|FullyQualifiedName~DescriptorKindDenyTests"`

Expected: double-read assertions fail and direct tools are blocked by the migration guard.

- [ ] **Step 3: Migrate each direct action explicitly**

Use helpers that return a typed denial, never `null`:

```csharp
private AgentToolResult<T>? DenyIfInvisible<T>(
    AgentToolInvocationContext context,
    AgentDescriptorVisibilityScope scope,
    DescriptorKind kind) where T : class
{
    if (scope.EvaluateExplicit(kind) == AgentDescriptorKindDecision.Visible)
        return null;
    var diagnostic = new AgentToolDiagnostic
    {
        Code = "DESC_KIND_DENIED",
        Severity = AgentToolDiagnosticSeverity.Error,
        Message = "The requested descriptor kind is not visible to this invocation."
    };
    var audit = BuildAudit(context, AgentToolResultStatus.Denied, [diagnostic]);
    return AgentToolResult<T>.Denied([diagnostic], audit);
}
```

`CreateDescriptorDraft` evaluates `request.DescriptorKind` before save. `GetDescriptorByRef` executes against `DescriptorResourceSnapshot.Descriptor`. Draft update/get/cancel/compare/validate resolve once and pass `snapshot.Draft` to the operation. Explicit `SearchDescriptors` evaluates `request.Kind.Value`; broad search returns `DESCRIPTOR_VISIBILITY_MIGRATION_REQUIRED` until PR B. Mark exactly these completed entries in coverage.

- [ ] **Step 4: Run direct and full focused tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests`

Expected: all Control Plane tests pass; blocked PR-B/C tools have explicit migration-guard expectations.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorVisibilityPipelineTests.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorKindDenyTests.cs
git commit -m "feat(agent): close direct descriptor visibility"
```

### Task 6: PR A merge gate

**Files:**
- Verify only; no source edits expected.

- [ ] **Step 1: Run formatting and focused verification**

Run: `dotnet format CrestCreates.slnx --verify-no-changes`

Expected: exit 0.

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests`

Expected: all tests pass.

- [ ] **Step 2: Run boundary and build gates**

Run: `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests`

Expected: all dependency-boundary tests pass.

Run: `dotnet build CrestCreates.slnx --no-restore`

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Verify the staged security invariant**

Run: `git diff --check`

Expected: no output. Review `AgentToolVisibilityCoverage.All`: every manifest tool is set-equal exactly once; any descriptor-bearing tool not delivered by PR A remains `BlockedUntilMigrated`.

**PR A merge gate:** Do not begin PR B until direct snapshot reuse, closed-world production behavior, coarse-auth zero-read tests, and bidirectional coverage pass. No aggregate/context/indirect descriptor-bearing tool may execute through a nullable-kind path.
