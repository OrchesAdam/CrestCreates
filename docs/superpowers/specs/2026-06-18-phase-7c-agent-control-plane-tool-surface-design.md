# Phase 7c — Agent Control Plane Tool Surface Design

**Date**: 2026-06-18
**Status**: Design
**Depends on**: Phase 7a (DescriptorDraft), Phase 7b (ContextPack)

## 1. Overview

Phase 7c exposes an internal **Agent Control Plane Tool Surface** — a deterministic, auditable, AOT-safe facade that allows Agents, UI, CLI, importers, generators, and future protocol adapters to operate on the Control Plane without bypassing descriptor governance, draft review, package evidence, human approval, or activation gates.

### Core Rule

```
Agent can inspect, draft, review, propose, package-preview, and request activation handoff.
Agent CANNOT approve, activate, execute runtime handlers, mutate runtime registries, or become governance authority.
```

### Design Principle

Agent tools are a **Control Plane facade**, not a new runtime.

```
Agent / UI / CLI / Importer / Generator / Future Protocol Adapter
  → Agent Control Plane Tool Surface
  → permission boundary
  → deterministic Control Plane service
  → local invocation audit
  → structured result / diagnostics
```

## 2. Project Structure

```
src/Metadata/
  CrestCreates.Agent.ControlPlane.Abstractions/   # DTOs, interfaces, permission model
  CrestCreates.Agent.ControlPlane/                 # Default implementations, DI extensions

tests/Metadata/
  CrestCreates.Agent.ControlPlane.Tests/           # Unit tests for all waves
```

**Rationale for placement under `src/Metadata/`**:
- The tool surface operates on metadata descriptors, drafts, context packs, and topology — all metadata-domain concepts.
- It depends on `CrestCreates.Metadata.Abstractions`, `CrestCreates.Metadata.ContextPack.Abstractions`, and `CrestCreates.DescriptorDraft.Abstractions`.
- It does NOT depend on runtime execution (`ICapabilityDispatcher`, `IWorkflowEngine`, `HumanTaskInstance`).
- The slnx virtual folder `/src/Metadata/` already groups all metadata-related projects.

**Not placed under `CrestCreates.Metadata.ContextPack`** because 7c spans ContextPack, DescriptorDraft, Review, Diagnostics, Fix Proposal, Package Preview, Authorization, Audit, and Activation handoff — far beyond ContextPack scope.

## 3. Dependency Graph

```
CrestCreates.Agent.ControlPlane.Abstractions
  ← CrestCreates.Metadata.Abstractions
  ← CrestCreates.Metadata.ContextPack.Abstractions
  ← CrestCreates.DescriptorDraft.Abstractions

CrestCreates.Agent.ControlPlane
  ← CrestCreates.Agent.ControlPlane.Abstractions
  ← CrestCreates.Metadata.Abstractions
  ← CrestCreates.Metadata.ContextPack
  ← CrestCreates.DescriptorDraft
  ← Microsoft.Extensions.DependencyInjection.Abstractions
  ← Microsoft.Extensions.Logging
```

No dependency on:
- `CrestCreates.Capability.Runtime` / `ICapabilityDispatcher`
- `CrestCreates.Workflow.Runtime` / `IWorkflowEngine`
- `CrestCreates.HumanTask.Runtime` / `HumanTaskInstance`
- Any runtime registry mutation interface

## 4. Core Contracts (Wave 0)

### 4.1 Invocation Context

```csharp
public sealed record AgentToolInvocationContext
{
    public required string TenantId { get; init; }
    public required string ActorId { get; init; }
    public required AgentToolActorKind ActorKind { get; init; }
    public string? AgentId { get; init; }
    public string? SessionId { get; init; }
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public required string ToolName { get; init; }
    public required AgentToolInvocationSource InvocationSource { get; init; }
    public IReadOnlyDictionary<string, string>? TraceAttributes { get; init; }
}
```

### 4.2 Result Status

```csharp
public enum AgentToolResultStatus
{
    Success,
    Denied,
    Failed,
    NotFound,
    InvalidRequest
}
```

### 4.3 Diagnostic

```csharp
public sealed record AgentToolDiagnostic
{
    public required string Code { get; init; }
    public required AgentToolDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }
    public string? RelatedDiagnosticCode { get; init; }
}

public enum AgentToolDiagnosticSeverity
{
    Info,
    Warning,
    Error,
    Blocker
}
```

### 4.4 Permission Model

```csharp
public sealed record AgentToolPermissionRequirement
{
    public required string PermissionName { get; init; }
    public string? DescriptorKindConstraint { get; init; }
    public string? Description { get; init; }
}
```

Permission names (from issue spec):
```
agent.context.read
agent.descriptor.read
agent.descriptor.search
agent.draft.create
agent.draft.update
agent.draft.read
agent.draft.list
agent.draft.cancel
agent.review.validate
agent.review.run
agent.review.read
agent.diagnostic.explain
agent.fix.suggest
agent.fix.apply_to_draft
agent.package.preview
agent.activation.request.submit
agent.activation.request.read
agent.activation.request.cancel
```

### 4.5 Audit Record

```csharp
public sealed record AgentToolInvocationAuditRecord
{
    public required string AuditId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required AgentToolInvocationContext Context { get; init; }
    public required AgentToolResultStatus ResultStatus { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public string? InputSummaryHash { get; init; }
    public IReadOnlyList<DescriptorRef>? TouchedDescriptorRefs { get; init; }
    public IReadOnlyList<string>? TouchedDraftIds { get; init; }
    public IReadOnlyList<string>? TouchedReviewResultIds { get; init; }
    public IReadOnlyList<string>? TouchedFixProposalIds { get; init; }
    public IReadOnlyList<string>? TouchedPackagePreviewIds { get; init; }
    public IReadOnlyList<string>? TouchedActivationRequestIds { get; init; }
}
```

### 4.6 Service Interfaces

```csharp
public interface IAgentToolAuthorizationService
{
    Task<AgentToolAuthorizationResult> AuthorizeAsync(
        AgentToolInvocationContext context,
        AgentToolPermissionRequirement permission,
        CancellationToken ct = default);
}

public sealed record AgentToolAuthorizationResult
{
    public required bool IsAllowed { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> DenialDiagnostics { get; init; }
}

public interface IAgentToolInvocationAuditor
{
    Task RecordAsync(AgentToolInvocationAuditRecord record, CancellationToken ct = default);
}

public interface IAgentControlPlaneToolService
{
    // Wave 1 — Context / Read
    Task<AgentToolResult<MetadataContextPack>> BuildMetadataContextPackAsync(
        AgentToolInvocationContext context, MetadataContextPackRequest request, CancellationToken ct = default);

    Task<AgentToolResult<MetadataContextPack>> BuildRuntimeScenarioContextPackAsync(
        AgentToolInvocationContext context, MetadataContextPackRequest request, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorInfo>> GetDescriptorByRefAsync(
        AgentToolInvocationContext context, DescriptorRef descriptorRef, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorSearchResult>> SearchDescriptorsAsync(
        AgentToolInvocationContext context, DescriptorSearchRequest request, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorRelationshipsResult>> ListDescriptorRelationshipsAsync(
        AgentToolInvocationContext context, DescriptorRef descriptorRef, CancellationToken ct = default);

    Task<AgentToolResult<TopologySummaryResult>> GetTopologySummaryAsync(
        AgentToolInvocationContext context, CancellationToken ct = default);

    // Wave 2 — Draft
    Task<AgentToolResult<DescriptorDraft>> CreateDescriptorDraftAsync(
        AgentToolInvocationContext context, CreateDescriptorDraftRequest request, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorDraft>> UpdateDescriptorDraftAsync(
        AgentToolInvocationContext context, UpdateDescriptorDraftRequest request, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorDraft>> GetDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorDraftListResult>> ListDescriptorDraftsAsync(
        AgentToolInvocationContext context, DraftQuery? query, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorDraft>> CancelDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default);

    Task<AgentToolResult<DraftComparisonResult>> CompareDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default);

    // Wave 3 — Review
    Task<AgentToolResult<DescriptorDraftValidationResult>> ValidateDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorDraftReviewResult>> ReviewDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorDraftReviewResult>> GetDraftReviewResultAsync(
        AgentToolInvocationContext context, string reviewResultId, CancellationToken ct = default);

    Task<AgentToolResult<ReviewResultListResult>> ListDraftReviewResultsAsync(
        AgentToolInvocationContext context, string? draftId, CancellationToken ct = default);

    Task<AgentToolResult<DiagnosticExplanation>> ExplainDiagnosticsAsync(
        AgentToolInvocationContext context, ExplainDiagnosticsRequest request, CancellationToken ct = default);

    // Wave 4 — Fix Proposal
    Task<AgentToolResult<FixProposalListResult>> SuggestDescriptorDraftFixesAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default);

    Task<AgentToolResult<FixProposal>> GetFixProposalAsync(
        AgentToolInvocationContext context, string proposalId, CancellationToken ct = default);

    Task<AgentToolResult<FixProposalListResult>> ListFixProposalsAsync(
        AgentToolInvocationContext context, string? draftId, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorDraft>> ApplyFixProposalToDraftAsync(
        AgentToolInvocationContext context, ApplyFixProposalRequest request, CancellationToken ct = default);

    // Wave 5 — Package Preview
    Task<AgentToolResult<DescriptorPackagePreview>> PreviewDescriptorPackageAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default);

    Task<AgentToolResult<PackageEvidencePreview>> BuildPackageEvidencePreviewAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default);

    Task<AgentToolResult<ActivationReadinessPreview>> BuildActivationReadinessPreviewAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default);

    Task<AgentToolResult<DescriptorPackagePreview>> GetPackagePreviewAsync(
        AgentToolInvocationContext context, string previewId, CancellationToken ct = default);

    // Wave 6 — Activation Handoff
    Task<AgentToolResult<ActivationRequest>> SubmitActivationRequestAsync(
        AgentToolInvocationContext context, SubmitActivationRequestRequest request, CancellationToken ct = default);

    Task<AgentToolResult<ActivationRequestStatus>> GetActivationRequestStatusAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default);

    Task<AgentToolResult<ActivationRequest>> CancelActivationRequestAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default);
}
```

### 4.7 Generic Result Wrapper

```csharp
public sealed record AgentToolResult<T> where T : class
{
    public required AgentToolResultStatus Status { get; init; }
    public T? Value { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public AgentToolInvocationAuditRecord? AuditRecord { get; init; }
}
```

### 4.8 Tool Manifest

```csharp
public sealed record AgentToolDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required AgentToolCategory Category { get; init; }
    public required IReadOnlyList<AgentToolPermissionRequirement> Permissions { get; init; }
    public required AgentToolActorKind AllowedActors { get; init; }
    public bool IsReadOnly { get; init; }
    public bool MutatesRuntimeRegistry { get; init; }  // Always false for 7c tools
}

public enum AgentToolCategory
{
    Context,
    Draft,
    Review,
    FixProposal,
    PackagePreview,
    ActivationHandoff,
    Manifest
}

public interface IAgentToolManifestProvider
{
    IReadOnlyList<AgentToolDescriptor> GetAllTools();
    AgentToolDescriptor? GetToolByName(string name);
}
```

## 5. Tool Request/Response DTOs

### 5.1 Context / Read (Wave 1)

```csharp
// Reuses MetadataContextPackRequest from ContextPack.Abstractions

public sealed record DescriptorInfo
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorState State { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public DescriptorStableHashes? Hashes { get; init; }
}

public sealed record DescriptorSearchRequest
{
    public string? Namespace { get; init; }
    public DescriptorKind? Kind { get; init; }
    public string? NameContains { get; init; }
    public DescriptorState? State { get; init; }
    public int MaxResults { get; init; } = 50;
}

public sealed record DescriptorSearchResult
{
    public required IReadOnlyList<DescriptorInfo> Descriptors { get; init; }
    public required int TotalCount { get; init; }
    public required bool WasTruncated { get; init; }
}

public sealed record DescriptorRelationshipsResult
{
    public required DescriptorRef Subject { get; init; }
    public required IReadOnlyList<DescriptorRelationship> Dependencies { get; init; }
    public required IReadOnlyList<DescriptorRelationship> Dependents { get; init; }
}

public sealed record TopologySummaryResult
{
    public required int TotalNodeCount { get; init; }
    public required int TotalEdgeCount { get; init; }
    public required IReadOnlyDictionary<DescriptorKind, int> NodeCountsByKind { get; init; }
    public required IReadOnlyDictionary<RelationshipKind, int> EdgeCountsByKind { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> TopologyDiagnostics { get; init; }
}
```

### 5.2 Draft (Wave 2)

```csharp
public sealed record CreateDescriptorDraftRequest
{
    public required DescriptorKind DescriptorKind { get; init; }
    public required string DescriptorId { get; init; }
    public required DescriptorDraftOperation Operation { get; init; }
    public required DescriptorDraftPayload Payload { get; init; }
    public string? BaseVersion { get; init; }
    public string? ProposedVersion { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public string? CorrelationId { get; init; }
    public string? Source { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record UpdateDescriptorDraftRequest
{
    public required string DraftId { get; init; }
    public DescriptorDraftPayload? Payload { get; init; }
    public string? ProposedVersion { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record DescriptorDraftListResult
{
    public required IReadOnlyList<DescriptorDraft> Drafts { get; init; }
    public required int TotalCount { get; init; }
}

public sealed record DraftComparisonResult
{
    public required DescriptorDraft Draft { get; init; }
    public required IDescriptor? CurrentActiveDescriptor { get; init; }
    public required IReadOnlyList<DraftDifference> Differences { get; init; }
}

public sealed record DraftDifference
{
    public required string Path { get; init; }
    public required string CurrentValue { get; init; }
    public required string ProposedValue { get; init; }
    public required DraftDifferenceKind Kind { get; init; }
}

public enum DraftDifferenceKind
{
    Added,
    Removed,
    Modified
}
```

### 5.3 Review (Wave 3)

```csharp
// Reuses DescriptorDraftValidationResult and DescriptorDraftReviewResult from DescriptorDraft.Abstractions

public sealed record ReviewResultListResult
{
    public required IReadOnlyList<DescriptorDraftReviewResult> Results { get; init; }
}

public sealed record ExplainDiagnosticsRequest
{
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public string? DraftId { get; init; }
}

public sealed record DiagnosticExplanation
{
    public required IReadOnlyList<DiagnosticExplanationEntry> Explanations { get; init; }
}

public sealed record DiagnosticExplanationEntry
{
    public required string Code { get; init; }
    public required string Explanation { get; init; }
    public required string Remediation { get; init; }
    public required AgentToolDiagnosticSeverity Severity { get; init; }
    public IReadOnlyList<string>? SuggestedFixToolNames { get; init; }
}
```

### 5.4 Fix Proposal (Wave 4)

```csharp
public sealed record FixProposal
{
    public required string ProposalId { get; init; }
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required FixProposalRiskLevel RiskLevel { get; init; }
    public required bool RequiresHumanApproval { get; init; }
    public required IReadOnlyList<FixProposalAction> Actions { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Rationale { get; init; }
}

public enum FixProposalRiskLevel
{
    Safe,
    Low,
    Medium,
    High,
    Unsafe
}

public sealed record FixProposalAction
{
    public required string Path { get; init; }
    public required FixProposalActionKind ActionKind { get; init; }
    public required string CurrentValue { get; init; }
    public required string ProposedValue { get; init; }
    public string? Description { get; init; }
}

public enum FixProposalActionKind
{
    Set,
    Remove,
    Add
}

public sealed record FixProposalListResult
{
    public required IReadOnlyList<FixProposal> Proposals { get; init; }
}

public sealed record ApplyFixProposalRequest
{
    public required string ProposalId { get; init; }
    public required string DraftId { get; init; }
}
```

### 5.5 Package Preview (Wave 5)

```csharp
// Reuses DescriptorPackagePreview from DescriptorDraft.Abstractions

public sealed record PackageEvidencePreview
{
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required DescriptorPackagePreview PackagePreview { get; init; }
    public required DescriptorPackageEvidence Evidence { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
}

public sealed record ActivationReadinessPreview
{
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required bool IsReady { get; init; }
    public required IReadOnlyList<ActivationReadinessBlocker> Blockers { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public string? ReviewResultId { get; init; }
    public string? PackagePreviewId { get; init; }
}

public sealed record ActivationReadinessBlocker
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required ActivationReadinessBlockerSeverity Severity { get; init; }
    public string? Remedy { get; init; }
}

public enum ActivationReadinessBlockerSeverity
{
    Warning,
    Error,
    Blocker
}
```

### 5.6 Activation Handoff (Wave 6)

```csharp
public sealed record ActivationRequest
{
    public required string RequestId { get; init; }
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required ActivationRequestStatus Status { get; init; }
    public required DateTimeOffset SubmittedAt { get; init; }
    public required string SubmittedBy { get; init; }
    public string? ReviewResultId { get; init; }
    public string? PackagePreviewId { get; init; }
    public string? EvidencePreviewId { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<AgentToolDiagnostic>? Diagnostics { get; init; }
}

public enum ActivationRequestStatus
{
    Submitted,
    UnderReview,
    Approved,
    Rejected,
    Cancelled,
    Expired
}

public sealed record SubmitActivationRequestRequest
{
    public required string DraftId { get; init; }
    public string? ReviewResultId { get; init; }
    public string? PackagePreviewId { get; init; }
    public string? EvidencePreviewId { get; init; }
    public string? CorrelationId { get; init; }
    public string? Rationale { get; init; }
}
```

## 6. Permission Boundary Design

Every tool invocation flows through:

1. **Manifest lookup** — Is the tool known? Unknown tool → `NotFound` + diagnostic.
2. **Permission check** — Does the actor have the required permission? Denied → `Denied` + audit.
3. **Service invocation** — Call the underlying Control Plane service.
4. **Audit recording** — Record result, diagnostics, and touched refs.

### Permission Resolution

`IAgentToolAuthorizationService` is the single authorization point. Default implementation checks a configurable allow/deny policy:

- Allow/deny by tool name
- Allow/deny by descriptor kind
- Allow/deny by draft operation kind
- Allow/deny by review mode
- Allow/deny by fix proposal application
- Allow/deny by package preview
- Allow/deny by activation request submission
- **Deny runtime execution tools by default**

Agent intent (from `InvocationContext.TraceAttributes`) does NOT affect authorization.

### Permission-to-Tool Mapping

| Tool | Permission |
|------|-----------|
| BuildMetadataContextPack | `agent.context.read` |
| BuildRuntimeScenarioContextPack | `agent.context.read` |
| GetDescriptorByRef | `agent.descriptor.read` |
| SearchDescriptors | `agent.descriptor.search` |
| ListDescriptorRelationships | `agent.descriptor.read` |
| GetTopologySummary | `agent.context.read` |
| CreateDescriptorDraft | `agent.draft.create` |
| UpdateDescriptorDraft | `agent.draft.update` |
| GetDescriptorDraft | `agent.draft.read` |
| ListDescriptorDrafts | `agent.draft.list` |
| CancelDescriptorDraft | `agent.draft.cancel` |
| CompareDescriptorDraft | `agent.draft.read` |
| ValidateDescriptorDraft | `agent.review.validate` |
| ReviewDescriptorDraft | `agent.review.run` |
| GetDraftReviewResult | `agent.review.read` |
| ListDraftReviewResults | `agent.review.read` |
| ExplainDiagnostics | `agent.diagnostic.explain` |
| SuggestDescriptorDraftFixes | `agent.fix.suggest` |
| GetFixProposal | `agent.fix.suggest` |
| ListFixProposals | `agent.fix.suggest` |
| ApplyFixProposalToDraft | `agent.fix.apply_to_draft` |
| PreviewDescriptorPackage | `agent.package.preview` |
| BuildPackageEvidencePreview | `agent.package.preview` |
| BuildActivationReadinessPreview | `agent.package.preview` |
| GetPackagePreview | `agent.package.preview` |
| SubmitActivationRequest | `agent.activation.request.submit` |
| GetActivationRequestStatus | `agent.activation.request.read` |
| CancelActivationRequest | `agent.activation.request.cancel` |
| ListAgentTools | (no permission — manifest discovery) |
| GetAgentToolDescriptor | (no permission — manifest discovery) |

## 7. Audit Boundary

Every tool invocation (success, failure, or denied) produces an `AgentToolInvocationAuditRecord`.

The audit record is field-compatible with a future `CrestCreates.Accountability` audit envelope but does not force that module into 7c.

Default implementation: `InMemoryAgentToolInvocationAuditor` (sufficient for 7c scope).

## 8. AOT-First Invocation

Tool dispatch is explicit and AOT-safe:

- **No runtime reflection discovery** — tool manifest is static/hardcoded.
- **No dynamic method generation** — `DefaultAgentControlPlaneToolService` uses explicit method dispatch.
- **No assembly scanning** — `IAgentToolManifestProvider` returns a fixed list.
- **No generic `object` payload** — every tool has typed request/response.
- **No unbounded `JsonElement` pass-through** — all DTOs are strongly typed records.
- **Source-generated JSON serialization ready** — all DTOs are `sealed record` types with primitive/string/record-typed properties.

Adapter layers (MCP, HTTP, CLI) can use explicit switch-based dispatch on tool name.

## 9. Runtime Boundary Invariants

The following are **forbidden** in Phase 7c code:

1. No call to `ICapabilityDispatcher`
2. No call to `IWorkflowEngine`
3. No completion of `HumanTaskInstance`
4. No mutation of Runtime Registry (`IDescriptorRegistry.Build()`, `IDynamicRegistry`)
5. No publishing of runtime domain events
6. No approval of activation requests (only handoff record creation)
7. No execution of activation gates

These invariants are enforced by:
- **Dependency architecture**: Abstractions project has no reference to runtime execution assemblies.
- **Code review**: Explicit check that no runtime mutation interfaces are injected.
- **Tests**: Boundary tests verify no runtime interfaces are called.

## 10. Relationship to Existing Code

### Phase 7a (DescriptorDraft)

7c delegates to:
- `IDescriptorDraftStore` — draft persistence
- `IDescriptorDraftValidator` — validation
- `IDescriptorDraftMaterializer` — materialization
- `IDescriptorDraftReviewService` — full review pipeline

7c does NOT:
- Add new draft capabilities beyond what 7a provides
- Bypass draft snapshot isolation
- Mutate runtime registries through draft operations

### Phase 7b (ContextPack)

7c delegates to:
- `IMetadataContextPackBuilder` — context pack construction
- `MetadataContextPackRequest` / `MetadataContextPack` — request/response types

7c does NOT:
- Add new ContextPack scopes or traversal modes
- Bypass ContextPack diagnostics
- Use agent intent to affect ContextPack traversal

### Exposure.Abstractions

The existing `AgentToolDescriptor` in `CrestCreates.Exposure.Abstractions` is a **runtime exposure** concept (maps tools to capabilities). Phase 7c's `AgentToolDescriptor` is a **Control Plane manifest** concept (describes tool permissions and audit requirements). These are different abstractions serving different layers.

7c's `AgentToolDescriptor` is intentionally in `CrestCreates.Agent.ControlPlane.Abstractions` to avoid coupling to the runtime exposure layer.

## 11. Test Coverage

47 minimum test cases as specified in the issue, organized by category:

- Core/Manifest/AOT (6 tests)
- Permission Boundary (5 tests)
- Audit (4 tests)
- Context/Read (6 tests)
- Draft (5 tests)
- Review (4 tests)
- Fix Proposal (4 tests)
- Package/Evidence (3 tests)
- Activation Handoff (5 tests)
- Runtime Boundary (5 tests)
