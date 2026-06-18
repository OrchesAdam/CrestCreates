# Phase 7c — Agent Control Plane Tool Surface Implementation Plan

**Date**: 2026-06-18
**Branch**: `feature/phase-7c-agent-control-plane-tool-surface`
**Depends on**: Phase 7a, Phase 7b

## Implementation Order

Waves are ordered so each wave can be implemented and tested independently before the next begins.

---

## Wave 0 — Core Contracts and Invariants

### Step 0.1: Create project structure

Create three new projects:

```
src/Metadata/CrestCreates.Agent.ControlPlane.Abstractions/
  CrestCreates.Agent.ControlPlane.Abstractions.csproj

src/Metadata/CrestCreates.Agent.ControlPlane/
  CrestCreates.Agent.ControlPlane.csproj

tests/Metadata/CrestCreates.Agent.ControlPlane.Tests/
  CrestCreates.Agent.ControlPlane.Tests.csproj
```

**Abstractions csproj** depends on:
- `CrestCreates.Metadata.Abstractions`
- `CrestCreates.Metadata.ContextPack.Abstractions`
- `CrestCreates.DescriptorDraft.Abstractions`

**Implementation csproj** depends on:
- `CrestCreates.Agent.ControlPlane.Abstractions`
- `CrestCreates.Metadata.Abstractions`
- `CrestCreates.Metadata.ContextPack` (for `DefaultMetadataContextPackBuilder`)
- `CrestCreates.DescriptorDraft` (for `DefaultDescriptorDraftReviewService`, `InMemoryDescriptorDraftStore`)
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging`

**Tests csproj** depends on:
- `CrestCreates.Agent.ControlPlane`
- `CrestCreates.Agent.ControlPlane.Abstractions`
- `CrestCreates.Metadata.Abstractions`
- `CrestCreates.DescriptorDraft.Abstractions`
- `CrestCreates.Schema.Abstractions`
- `FluentAssertions`, `Moq`, `xunit`

### Step 0.2: Abstractions — Enums and base types

Files to create in `CrestCreates.Agent.ControlPlane.Abstractions/`:

1. `AgentToolActorKind.cs` — `Human, Agent, System, Import, Generator`
2. `AgentToolInvocationSource.cs` — `Direct, McpAdapter, HttpAdapter, CliAdapter, Internal`
3. `AgentToolResultStatus.cs` — `Success, Denied, Failed, NotFound, InvalidRequest`
4. `AgentToolDiagnosticSeverity.cs` — `Info, Warning, Error, Blocker`
5. `AgentToolCategory.cs` — `Context, Draft, Review, FixProposal, PackagePreview, ActivationHandoff, Manifest`
6. `AgentToolPermissionName.cs` — static class with const permission strings
7. `FixProposalRiskLevel.cs` — `Safe, Low, Medium, High, Unsafe`
8. `FixProposalActionKind.cs` — `Set, Remove, Add`
9. `DraftDifferenceKind.cs` — `Added, Removed, Modified`
10. `ActivationRequestStatus.cs` — `Submitted, UnderReview, Approved, Rejected, Cancelled, Expired`
11. `ActivationReadinessBlockerSeverity.cs` — `Warning, Error, Blocker`

### Step 0.3: Abstractions — Core DTOs

1. `AgentToolDiagnostic.cs`
2. `AgentToolInvocationContext.cs`
3. `AgentToolPermissionRequirement.cs`
4. `AgentToolInvocationAuditRecord.cs`
5. `AgentToolAuthorizationResult.cs`
6. `AgentToolResult.cs` — generic result wrapper `AgentToolResult<T>`
7. `AgentToolDescriptor.cs` — tool manifest entry (different from Exposure.Abstractions one)

### Step 0.4: Abstractions — Service interfaces

1. `IAgentToolAuthorizationService.cs`
2. `IAgentToolInvocationAuditor.cs`
3. `IAgentToolManifestProvider.cs`
4. `IAgentControlPlaneToolService.cs` — the main facade interface (all tool methods)

### Step 0.5: Abstractions — Tool request/response DTOs

Group by wave:

**Context/Read (Wave 1):**
1. `DescriptorInfo.cs`
2. `DescriptorSearchRequest.cs`
3. `DescriptorSearchResult.cs`
4. `DescriptorRelationshipsResult.cs`
5. `TopologySummaryResult.cs`

**Draft (Wave 2):**
6. `CreateDescriptorDraftRequest.cs`
7. `UpdateDescriptorDraftRequest.cs`
8. `DescriptorDraftListResult.cs`
9. `DraftComparisonResult.cs`
10. `DraftDifference.cs`

**Review (Wave 3):**
11. `ReviewResultListResult.cs`
12. `ExplainDiagnosticsRequest.cs`
13. `DiagnosticExplanation.cs`
14. `DiagnosticExplanationEntry.cs`

**Fix Proposal (Wave 4):**
15. `FixProposal.cs`
16. `FixProposalAction.cs`
17. `FixProposalListResult.cs`
18. `ApplyFixProposalRequest.cs`

**Package Preview (Wave 5):**
19. `PackageEvidencePreview.cs`
20. `ActivationReadinessPreview.cs`
21. `ActivationReadinessBlocker.cs`

**Activation Handoff (Wave 6):**
22. `ActivationRequest.cs`
23. `SubmitActivationRequestRequest.cs`

### Step 0.6: Implementation — Default manifest provider

1. `StaticAgentToolManifestProvider.cs` — hardcoded list of all 30 tool descriptors with permissions

### Step 0.7: Implementation — Default authorization service

1. `DefaultAgentToolAuthorizationService.cs` — configurable allow/deny policy
2. `AgentToolAuthorizationPolicy.cs` — policy configuration record

### Step 0.8: Implementation — Default auditor

1. `InMemoryAgentToolInvocationAuditor.cs` — concurrent store, sufficient for 7c

### Step 0.9: Tests — Wave 0

1. `AgentToolManifestTests.cs` — manifest is deterministic, all tools declare permissions, unknown tool returns null
2. `AgentToolAuthorizationTests.cs` — blocked tool denied, blocked descriptor kind denied, agent intent doesn't affect auth
3. `AgentToolAuditTests.cs` — successful/failed/denied invocations emit audit records
4. `AgentToolResultTests.cs` — DTO construction, AOT-friendliness

---

## Wave 1 — Context and Descriptor Read Tools

### Step 1.1: Implementation

In `DefaultAgentControlPlaneToolService.cs`:

- `BuildMetadataContextPackAsync` — delegates to `IMetadataContextPackBuilder.Build()`
- `BuildRuntimeScenarioContextPackAsync` — same builder with `RuntimeScenario` scope
- `GetDescriptorByRefAsync` — looks up from `IDescriptorCatalog` or registry, wraps as `DescriptorInfo`
- `SearchDescriptorsAsync` — bounded search from catalog, wraps as `DescriptorSearchResult`
- `ListDescriptorRelationshipsAsync` — delegates to `IDescriptorRelationshipProvider`
- `GetTopologySummaryAsync` — delegates to `IDescriptorTopologyBuilder.Build()`, extracts summary

### Step 1.2: Tests

1. `ContextToolTests.cs` — ContextPack builder invoked through facade, diagnostics preserved, ambiguous ref returns diagnostic, agent intent doesn't affect traversal, search is bounded, relationships preserve version-aware refs

---

## Wave 2 — Draft Tools

### Step 2.1: Implementation

- `CreateDescriptorDraftAsync` — creates draft via `IDescriptorDraftStore.SaveAsync()`
- `UpdateDescriptorDraftAsync` — gets existing, applies updates, saves new revision
- `GetDescriptorDraftAsync` — delegates to `IDescriptorDraftStore.GetAsync()`
- `ListDescriptorDraftsAsync` — delegates to `IDescriptorDraftStore.ListAsync()`
- `CancelDescriptorDraftAsync` — sets status to `Cancelled`, saves
- `CompareDescriptorDraftAsync` — compares draft payload against current active descriptor

### Step 2.2: Tests

1. `DraftToolTests.cs` — create doesn't activate, update only updates draft, cancel doesn't affect active, list is bounded, snapshot isolation preserved

---

## Wave 3 — Validation and Review Tools

### Step 3.1: Implementation

- `ValidateDescriptorDraftAsync` — delegates to `IDescriptorDraftValidator.Validate()`
- `ReviewDescriptorDraftAsync` — delegates to `IDescriptorDraftReviewService.ReviewAsync()`
- `GetDraftReviewResultAsync` — retrieves stored review result
- `ListDraftReviewResultsAsync` — lists stored review results
- `ExplainDiagnosticsAsync` — maps diagnostic codes to human/LLM-readable explanations

Need: `IReviewResultStore` (simple in-memory store for review results, similar to draft store)

### Step 3.2: Tests

1. `ReviewToolTests.cs` — validate invokes validator only, review invokes review service, review pass doesn't create activation request, diagnostics are structured

---

## Wave 4 — Fix Proposal Tools

### Step 4.1: Implementation

Need: `IFixProposalService` interface + `DefaultFixProposalService`

- `SuggestDescriptorDraftFixesAsync` — analyzes draft diagnostics, generates fix proposals
- `GetFixProposalAsync` — retrieves stored proposal
- `ListFixProposalsAsync` — lists proposals for a draft
- `ApplyFixProposalToDraftAsync` — applies proposal actions to draft, creates new revision

Need: `IFixProposalStore` (in-memory store for proposals)

### Step 4.2: Tests

1. `FixProposalToolTests.cs` — suggest creates proposal only, apply updates draft only, no runtime mutation, high-risk requires explicit marker

---

## Wave 5 — Package Preview and Evidence Tools

### Step 5.1: Implementation

- `PreviewDescriptorPackageAsync` — delegates to `IDescriptorPackageBuilder.Build()`, wraps as `DescriptorPackagePreview`
- `BuildPackageEvidencePreviewAsync` — builds package + extracts evidence
- `BuildActivationReadinessPreviewAsync` — checks review result, evidence, governance, reports blockers
- `GetPackagePreviewAsync` — retrieves stored preview

Need: `IPackagePreviewStore` (in-memory store for previews)

### Step 5.2: Tests

1. `PackagePreviewToolTests.cs` — preview creates preview only, readiness reports blockers but doesn't submit, preview includes review/evidence refs

---

## Wave 6 — Activation Handoff Tools

### Step 6.1: Implementation

- `SubmitActivationRequestAsync` — creates handoff record, requires review/package/evidence refs
- `GetActivationRequestStatusAsync` — reads handoff record
- `CancelActivationRequestAsync` — sets status to `Cancelled`

Need: `IActivationRequestStore` (in-memory store for activation requests)

### Step 6.2: Tests

1. `ActivationHandoffToolTests.cs` — submit creates record only, requires references, doesn't approve/execute, get is read-only, cancel doesn't affect runtime

---

## Wave 7 — Integration and Build

### Step 7.1: DI extensions

1. `AgentControlPlaneServiceCollectionExtensions.cs` — registers all services

### Step 7.2: Add to slnx

Add all three projects to `CrestCreates.slnx` under `/src/Metadata/` and `/tests/Metadata/`.

### Step 7.3: Runtime boundary tests

1. `RuntimeBoundaryTests.cs` — verify no 7c tool calls ICapabilityDispatcher, IWorkflowEngine, etc.

### Step 7.4: Build verification

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~CrestCreates.Agent.ControlPlane.Tests"
```

---

## File Count Estimate

| Project | Files |
|---------|-------|
| Abstractions | ~35 (.cs) + 1 (.csproj) |
| Implementation | ~10 (.cs) + 1 (.csproj) |
| Tests | ~8 (.cs) + 1 (.csproj) |
| **Total** | ~57 files |

## Key Design Decisions

1. **Tool surface is a facade, not a runtime** — every tool method delegates to existing Phase 7a/7b services.
2. **Permission boundary is a gate, not a bypass** — authorization check happens before service invocation.
3. **Audit is mandatory, not optional** — every invocation produces an audit record.
4. **Manifest is static, not discovered** — no runtime reflection, no assembly scanning.
5. **Activation is handoff, not execution** — submit creates a record, does not execute activation.
6. **Fix proposals are suggestions, not commands** — apply only updates draft, never patches active descriptors.
7. **Review pass is not activation approval** — `IsActivationEligible` is informational only.
