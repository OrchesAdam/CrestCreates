# Agent Visibility PR C Indirect Nested Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close visibility for review, fix, package, readiness, activation, nested artifacts, diagnostics, tenant boundaries, cancellation, audit, and the complete manifest.

**Architecture:** Indirect resources resolve to immutable tenant-qualified owner snapshots in one batch or one direct lookup; owner kind controls access and ownership mismatches reject before mutation. Typed projectors reconstruct nested artifacts from visible typed data, while diagnostics without a draft use a fixed code table and never echo caller content. Cancellation and audit are explicit pipeline stages, and completion removes every migration guard only after bidirectional coverage is complete.

**Tech Stack:** .NET 10, C# 14, xUnit 2.9.3, FluentAssertions, Moq, ConcurrentDictionary-backed current stores, strongly typed CrestCreates Metadata/Draft DTOs

---

**Prerequisite:** PR A and PR B are merged and green. Their evaluator, scope, visible universe, graph/context closure, resolver, and coverage registry must be reused without a second policy implementation.

**Files map:**
- Extend `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneResourceResolver.cs`: direct and batch indirect-owner snapshots.
- Create `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneArtifactEntries.cs`: shared tenant-qualified preview/evidence storage records.
- Create `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDraftArtifactVisibilityProjector.cs`: typed review/package/evidence/readiness projection.
- Create `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDiagnosticExplanationPolicy.cs`: allowlisted generic explanations.
- Modify `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`: migrate all remaining indirect/nested operations.
- Modify `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs`: complete all entries and remove migration state.
- Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/IndirectVisibilityTests.cs`.
- Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/NestedArtifactVisibilityTests.cs`.
- Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DiagnosticVisibilityTests.cs`.
- Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/VisibilityTenantCancellationAuditTests.cs`.
- Modify wave tests as needed to assert snapshot reuse and typed projection.
- Modify `memory.md`: record Phase 7c visibility closure completion.

### Task 1: Direct and batch indirect-owner resolution

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneArtifactEntries.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneResourceResolver.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/IndirectVisibilityTests.cs`

- [ ] **Step 1: Write failing owner-resolution tests**

Cover review result, fix proposal, package preview, evidence preview, and activation request. For each, assert tenant-qualified lookup, one owner-draft read, `NotFound` when the artifact is absent, authorization-context failure when the owner draft is absent, and no fallback to another tenant. For list resolution, assert one draft-list/batch operation rather than one `GetAsync` per artifact.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~IndirectVisibilityTests`

Expected: indirect tools are migration-blocked and no typed snapshots exist.

- [ ] **Step 3: Add typed owner snapshots and batch join**

```csharp
internal sealed record ReviewResourceSnapshot(DraftReviewResult Review, Draft Owner);
internal sealed record FixProposalResourceSnapshot(FixProposal Proposal, Draft Owner);
internal sealed record PackagePreviewEntry(string DraftId, string TenantId, DraftPackagePreview Preview);
internal sealed record EvidencePreviewEntry(string DraftId, string TenantId, PackageEvidencePreview Preview);
internal sealed record PackagePreviewResourceSnapshot(PackagePreviewEntry Preview, Draft Owner);
internal sealed record ActivationResourceSnapshot(ActivationRequest Request, Draft Owner);

internal async Task<IReadOnlyDictionary<string, Draft>> ResolveOwnersAsync(
    string tenantId, IEnumerable<string> draftIds, CancellationToken ct)
{
    var requested = draftIds.ToHashSet(StringComparer.Ordinal);
    var drafts = await _draftStore.ListAsync(tenantId, null, ct);
    return drafts.Where(d => requested.Contains(d.DraftId))
        .ToDictionary(d => d.DraftId, StringComparer.Ordinal);
}
```

Move the facade's current private `PackagePreviewEntry` to `AgentControlPlaneArtifactEntries.cs` and wrap evidence previews too, because `PackageEvidencePreview.DraftId` is owner-bearing stored state. Keep artifact dictionaries tenant-keyed. Never use a caller-supplied owner in place of stored `DraftId`; when both exist, require ordinal equality. Missing current-tenant owner produces `AUTHORIZATION_CONTEXT_UNAVAILABLE` without cross-tenant lookup.

- [ ] **Step 4: Run resolver tests and verify GREEN**

Run the Step 2 command.

Expected: all owner resolution and no-N+1 assertions pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneArtifactEntries.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneResourceResolver.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/IndirectVisibilityTests.cs
git commit -m "feat(agent): resolve indirect artifact visibility owners"
```

### Task 2: Review and fix operations, lists, and ownership checks

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/IndirectVisibilityTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Wave3ReviewTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Wave4FixProposalTests.cs`

- [ ] **Step 1: Write failing review/fix tests**

Assert run/suggest happen only after owner visibility; get inherits owner kind; broad list filters denied owners before returned collection/audit; explicit `DraftId` list target is denied; apply proposal requires stored proposal owner to equal request draft and rejects mismatch before save. Verify owner drafts are reused, not re-read by action code.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~IndirectVisibilityTests|FullyQualifiedName~Wave3ReviewTests|FullyQualifiedName~Wave4FixProposalTests"`

Expected: migration denials or double reads fail assertions.

- [ ] **Step 3: Migrate review/fix methods**

Resolve draft once for `ReviewDescriptorDraft` and `SuggestDescriptorDraftFixes`. Resolve `(artifact, owner)` once for get/apply. For lists, snapshot tenant-keyed dictionary values, batch-load distinct owners, fail the entire aggregate if any record lacks a current-tenant owner, filter by `scope.IsVisible(owner.DescriptorKind)`, then order and return. Audit only visible review/proposal/draft IDs.

```csharp
var reviews = _reviewResults
    .Where(pair => pair.Key.TenantId == context.TenantId)
    .Select(pair => pair.Value)
    .ToList();
var owners = await _resourceResolver.ResolveOwnersAsync(
    context.TenantId, reviews.Select(r => r.DraftId), ct);
if (reviews.Any(r => !owners.ContainsKey(r.DraftId)))
    return await RecordAggregateFailure<ReviewResultListResult>(context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
var visible = reviews
    .Where(r => scope.IsVisible(owners[r.DraftId].DescriptorKind))
    .OrderBy(r => r.DraftId, StringComparer.Ordinal)
    .ToList().AsReadOnly();
```

- [ ] **Step 4: Run tests and verify GREEN**

Run the Step 2 command.

Expected: all review/fix tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/IndirectVisibilityTests.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Wave3ReviewTests.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Wave4FixProposalTests.cs
git commit -m "feat(agent): enforce review and fix owner visibility"
```

### Task 3: Typed nested artifact projection

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDraftArtifactVisibilityProjector.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/NestedArtifactVisibilityTests.cs`

- [ ] **Step 1: Write failing nested projection tests**

Construct review proposed inventory, descriptor package snapshot/evidence, readiness blockers, refs, and diagnostics with one allowed and one denied descriptor. Assert returned typed DTOs contain no denied ref, incident relationship, count, path, or diagnostic. Assert projector input with invalid/unsupported descriptor-bearing content fails without returning the original object.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~NestedArtifactVisibilityTests`

Expected: projector is missing and package/review output is migration-blocked.

- [ ] **Step 3: Implement explicit projector methods**

```csharp
internal sealed class AgentDraftArtifactVisibilityProjector
{
    public DraftReviewResult ProjectReview(DraftReviewResult source, AgentDescriptorVisibilityScope scope, IDescriptorTopologyBuilder topologyBuilder);
    public DraftPackagePreview ProjectPackage(DraftPackagePreview source, AgentVisibleDescriptorUniverse universe);
    public PackageEvidencePreview ProjectEvidence(PackageEvidencePreview source, AgentVisibleDescriptorUniverse universe);
    public ActivationReadinessPreview ProjectReadiness(ActivationReadinessPreview source, AgentDescriptorVisibilityScope scope);
}
```

Implement each using the concrete DTO fields in `CrestCreates.DescriptorDraft.Abstractions` and `CrestCreates.Metadata.Abstractions`: filter `DescriptorDraftReviewResult.ProposedInventory`, then rebuild `TopologySnapshot` through `IDescriptorTopologyBuilder` because its constructor is internal to Metadata. Resolve every string in `DescriptorPackagePreview.DescriptorIds` against the already materialized visible universe; an unresolvable ID is a projection failure, not a pass-through. For a full `DescriptorPackage`, filter typed `DescriptorSnapshot.Descriptors`, remove `DescriptorSnapshot.Relationships` incident to removed refs, and recreate evidence/counts only through existing package builders. Do not inspect messages/paths and do not serialize/deserialize for cleanup. `ActivationReadinessBlocker` has no typed descriptor association today, so retain only fixed blockers generated from already-visible inputs; future typed associations require an explicit projector branch. If a DTO cannot be safely reconstructed, return a projector failure consumed as `Failed` with no value.

- [ ] **Step 4: Run nested tests and verify GREEN**

Run the Step 2 command.

Expected: all typed projection tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDraftArtifactVisibilityProjector.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/NestedArtifactVisibilityTests.cs
git commit -m "feat(agent): project nested artifacts by visibility"
```

### Task 4: Package, evidence, readiness, and activation handoff

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/IndirectVisibilityTests.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/NestedArtifactVisibilityTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Wave5PackagePreviewTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Wave6ActivationHandoffTests.cs`

- [ ] **Step 1: Write failing package/activation tests**

Assert preview/evidence/readiness deny invisible owner before builders; get preview projects nested data; activation submit resolves draft, review, and package once, validates same tenant/owner before persistence, and rejects mismatches; status/cancel inherit owner visibility; cancel cannot mutate after denial. Include successful allowed-owner paths.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~IndirectVisibilityTests|FullyQualifiedName~NestedArtifactVisibilityTests|FullyQualifiedName~Wave5PackagePreviewTests|FullyQualifiedName~Wave6ActivationHandoffTests"`

Expected: remaining tools are migration-blocked.

- [ ] **Step 3: Migrate remaining artifact operations**

For create-preview tools resolve the draft once, check visibility, build from visible inputs, project, then persist. For activation submit resolve all requested references into typed snapshots, verify `TenantId` and stored `DraftId` agreement, evaluate owner kind once, then persist. For get/cancel use `ActivationResourceSnapshot`; cancel mutates that exact dictionary entry only after the owner decision. Return generic ownership diagnostics without hidden metadata.

```csharp
if (!StringComparer.Ordinal.Equals(reviewSnapshot?.Owner.DraftId, draftSnapshot.Draft.DraftId) ||
    !StringComparer.Ordinal.Equals(packageSnapshot?.Owner.DraftId, draftSnapshot.Draft.DraftId))
{
    return await RecordAndReturn(context,
        AgentToolResult<ActivationRequest>.InvalidRequest([new AgentToolDiagnostic
        {
            Code = "ACTIVATION_ARTIFACT_OWNER_MISMATCH",
            Severity = AgentToolDiagnosticSeverity.Error,
            Message = "A referenced activation artifact does not belong to the requested draft."
        }]));
}
```

- [ ] **Step 4: Run package/activation tests and verify GREEN**

Run the Step 2 command.

Expected: all pass, including production explicit-grant tests.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/IndirectVisibilityTests.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/NestedArtifactVisibilityTests.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Wave5PackagePreviewTests.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Wave6ActivationHandoffTests.cs
git commit -m "feat(agent): close package and activation visibility"
```

### Task 5: Diagnostic explanation policy

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDiagnosticExplanationPolicy.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DiagnosticVisibilityTests.cs`

- [ ] **Step 1: Write failing diagnostic tests**

Without `DraftId`, submit a known code with hostile `Message`/`Path` containing a denied ref; assert output contains only fixed explanation/remediation/severity and never those strings. Submit an unknown code and assert fixed `UNKNOWN_DIAGNOSTIC` content without echoing the code. With `DraftId`, assert tenant-safe owner resolution and denied-owner semantics.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~DiagnosticVisibilityTests`

Expected: current explanation echoes/derives caller content or is migration-blocked.

- [ ] **Step 3: Implement allowlisted code-table explanations**

```csharp
internal sealed record DiagnosticExplanationTemplate(
    string Explanation, string Remediation, AgentToolDiagnosticSeverity Severity,
    IReadOnlyList<string> SuggestedFixToolNames);

internal sealed class AgentDiagnosticExplanationPolicy
{
    private static readonly IReadOnlyDictionary<string, DiagnosticExplanationTemplate> Templates =
        new Dictionary<string, DiagnosticExplanationTemplate>(StringComparer.Ordinal)
        {
            ["KIND_PAYLOAD_MISMATCH"] = new(
                "The declared descriptor kind and payload kind differ.",
                "Submit a payload whose typed kind matches the declared kind.",
                AgentToolDiagnosticSeverity.Error,
                [AgentToolName.SuggestDescriptorDraftFixes])
        };
    public DiagnosticExplanationEntry Explain(AgentToolDiagnostic diagnostic);
}
```

`Explain` uses only `Code` as a dictionary key; output `Code` is the canonical known key, while unknown input returns literal `UNKNOWN_DIAGNOSTIC`. Never read `Message`, `Path`, or interpolate unknown code. With `DraftId`, resolve/evaluate owner before applying the same generic table.

- [ ] **Step 4: Run diagnostic tests and verify GREEN**

Run the Step 2 command.

Expected: all diagnostic non-echo and owner tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDiagnosticExplanationPolicy.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DiagnosticVisibilityTests.cs
git commit -m "feat(agent): make diagnostic explanations non-probing"
```

### Task 6: Tenant, cancellation, and audit hardening

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/InMemoryAgentToolInvocationAuditor.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/VisibilityTenantCancellationAuditTests.cs`

- [ ] **Step 1: Write failing boundary tests**

Assert same ID in another tenant cannot establish owner/kind; missing and cross-tenant targets have identical outward status/diagnostic code; same-tenant denied remains internally `Denied`. Cancel tokens during resolver, batch owner load, topology/context builder adapter, projector, and auditor; assert no partial value and `OperationCanceledException` is not converted to `TOOL_INVOCATION_FAILED`. Verify caller-visible audit touched refs/IDs contain visible resources only and no hidden counts/kinds.

- [ ] **Step 2: Run boundary tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~VisibilityTenantCancellationAuditTests`

Expected: generic catch currently converts cancellation and audit may contain pre-filter IDs.

- [ ] **Step 3: Implement boundary behavior**

Pass `ct` to authorization, resolver, stores, and auditor. Add before the general exception catch:

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    throw;
}
```

Build public audit touched-resource fields only from projected results. Protected logs may include policy rule/filtered count with tenant, actor, tool, correlation, but never descriptor payloads; do not add this telemetry to `AgentToolInvocationAuditRecord`. Preserve current-tenant `NotFound` semantics and do not probe another tenant.

- [ ] **Step 4: Run boundary tests and verify GREEN**

Run the Step 2 command.

Expected: all tenant, cancellation, and audit tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs src/Runtime/Agent/CrestCreates.Agent.ControlPlane/InMemoryAgentToolInvocationAuditor.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/VisibilityTenantCancellationAuditTests.cs
git commit -m "fix(agent): harden visibility tenant cancellation audit"
```

### Task 7: Full manifest closure and documentation

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/VisibilityCoverageTests.cs`
- Modify: `memory.md`

- [ ] **Step 1: Write the final failing coverage assertions**

Assert every coverage entry is `Complete`, manifest/coverage names are duplicate-free and bidirectionally set-equal, and each entry has exactly one resource shape. Add behavior assertions that `None` applies only to `ListAgentTools` and `GetAgentToolDescriptor`.

- [ ] **Step 2: Run coverage tests and verify RED**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~VisibilityCoverageTests`

Expected: remaining PR-C entries are still `BlockedUntilMigrated`.

- [ ] **Step 3: Complete coverage and remove migration state**

Mark every delivered entry complete, then remove `AgentVisibilityMigrationState` and the facade migration branch entirely so there is one permanent path. Keep the resource-shape table as the security coverage authority. Update `memory.md` with production closed-world semantics, aggregate visibility closure, typed nested projection, and Issue #40 completion.

```csharp
internal sealed record AgentToolVisibilityEntry(string ToolName, AgentToolResourceShape Shape);

internal static IReadOnlyList<AgentToolVisibilityEntry> All { get; } =
[
    new(AgentToolName.BuildMetadataContextPack, AgentToolResourceShape.ContextPack),
    new(AgentToolName.BuildRuntimeScenarioContextPack, AgentToolResourceShape.ContextPack),
    new(AgentToolName.GetDescriptorByRef, AgentToolResourceShape.SingleDescriptor),
    new(AgentToolName.SearchDescriptors, AgentToolResourceShape.Aggregate),
    new(AgentToolName.ListDescriptorRelationships, AgentToolResourceShape.Graph),
    new(AgentToolName.GetTopologySummary, AgentToolResourceShape.Graph),
    new(AgentToolName.CreateDescriptorDraft, AgentToolResourceShape.DirectKind),
    new(AgentToolName.UpdateDescriptorDraft, AgentToolResourceShape.SingleDraft),
    new(AgentToolName.GetDescriptorDraft, AgentToolResourceShape.SingleDraft),
    new(AgentToolName.ListDescriptorDrafts, AgentToolResourceShape.Aggregate),
    new(AgentToolName.CancelDescriptorDraft, AgentToolResourceShape.SingleDraft),
    new(AgentToolName.CompareDescriptorDraft, AgentToolResourceShape.Nested),
    new(AgentToolName.ValidateDescriptorDraft, AgentToolResourceShape.SingleDraft),
    new(AgentToolName.ReviewDescriptorDraft, AgentToolResourceShape.Nested),
    new(AgentToolName.GetDraftReviewResult, AgentToolResourceShape.Indirect),
    new(AgentToolName.ListDraftReviewResults, AgentToolResourceShape.Indirect),
    new(AgentToolName.ExplainDiagnostics, AgentToolResourceShape.Indirect),
    new(AgentToolName.SuggestDescriptorDraftFixes, AgentToolResourceShape.Nested),
    new(AgentToolName.GetFixProposal, AgentToolResourceShape.Indirect),
    new(AgentToolName.ListFixProposals, AgentToolResourceShape.Indirect),
    new(AgentToolName.ApplyFixProposalToDraft, AgentToolResourceShape.Indirect),
    new(AgentToolName.PreviewDescriptorPackage, AgentToolResourceShape.Nested),
    new(AgentToolName.BuildPackageEvidencePreview, AgentToolResourceShape.Nested),
    new(AgentToolName.BuildActivationReadinessPreview, AgentToolResourceShape.Nested),
    new(AgentToolName.GetPackagePreview, AgentToolResourceShape.Indirect),
    new(AgentToolName.SubmitActivationRequest, AgentToolResourceShape.Indirect),
    new(AgentToolName.GetActivationRequestStatus, AgentToolResourceShape.Indirect),
    new(AgentToolName.CancelActivationRequest, AgentToolResourceShape.Indirect),
    new(AgentToolName.ListAgentTools, AgentToolResourceShape.None),
    new(AgentToolName.GetAgentToolDescriptor, AgentToolResourceShape.None)
];
```

- [ ] **Step 4: Run complete acceptance suite**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests`

Expected: all Control Plane tests pass.

Run: `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests`

Expected: all boundary tests pass.

Run: `dotnet format CrestCreates.slnx --verify-no-changes`

Expected: exit 0.

Run: `dotnet build CrestCreates.slnx --no-restore`

Expected: build succeeds with 0 errors.

Run: `dotnet publish samples/LibraryManagement/LibraryManagement.Web -c Release -r win-x64 --self-contained true -p:CrestCreatesPublishMode=aot --no-restore`

Expected: publish succeeds without new trim/AoT warnings attributable to Control Plane visibility code.

Run: `git diff --check`

Expected: no output.

- [ ] **Step 5: Commit closure**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/VisibilityCoverageTests.cs memory.md
git commit -m "docs(agent): complete descriptor visibility closure"
```

**PR C merge gate:** All 30 current manifest tools have one set-equal coverage entry and no migration guard remains; indirect ownership is tenant-safe and batch-resolved; nested DTOs are typed projections; diagnostics do not echo caller content; cancellation propagates; audits expose only visible resources; Control Plane, boundary, solution build, and AoT publish gates pass.
