# Phase 7d — Control Plane Review Report & Fix Proposal Contract

## Objective

Convert descriptor draft review results into stable human/agent-readable review reports and upgrade the fix proposal contract for future adapter consumption.

This phase is the **explanation and remediation contract layer** of the Agent Control Plane. It makes review results understandable and fix proposals expressible without granting governance or activation authority.

## Design Principle

```text
DescriptorDraftReviewResult
    ↓
DescriptorReviewReportDto         // Structured DTO — authoritative artifact
    ↓
Markdown / PlainText Renderer     // Deterministic projection, not decision

Reports explain deterministic findings. Fix proposals express draft-level suggestions.
Governance decisions still come from the Control Plane.
```

## Scope

### In Scope

- Review Report DTO (structured, 13 fixed sections)
- Review Report Builder (IDescriptorReviewReportBuilder + default impl)
- Message Template Catalog (deterministic wording)
- Review Report Renderer (Markdown + PlainText, deterministic projection from DTO)
- Fix Proposal contract upgrade (breaking changes, no dual-track)
- 2 new Agent Control Plane tools (BuildDescriptorReviewReport, RenderDescriptorReviewReport)
- Source-generated JSON contract updates
- Test suites (builder, renderer, template, fix proposal, coverage, boundary)

### Not In Scope

- Full 8+ fix kind runtime implementation
- Patch engine
- Draft fork/revision patch workflow
- Activation gate implementation
- Approval or governance authority
- Runtime registry mutation
- LLM integration

---

## Section 1 — Review Report DTO Structure

### Core Types

All types in `CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/`.

#### DescriptorReviewReportDto

```csharp
public sealed record DescriptorReviewReportDto
{
    public required string ReportId { get; init; }
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required string ReviewResultId { get; init; }           // source binding: which review result
    public required string DraftVersion { get; init; }              // source binding: which draft revision
    public required string SourceReviewHash { get; init; }          // stable identity of the review result
    public required string TemplateVersion { get; init; }           // message template catalog version
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string ContractVersion { get; init; } = AgentControlPlaneContractVersion.Current;

    // Machine-parseable next actions — Agent reads these, not section items
    public required IReadOnlyList<DescriptorReviewRecommendationDto> Recommendations { get; init; }

    // 13 fixed sections — always present, may be empty (IsEmpty = true)
    public required DescriptorReviewReportSectionDto SummarySection { get; init; }
    public required DescriptorReviewReportSectionDto DraftIdentitySection { get; init; }
    public required DescriptorReviewReportSectionDto ProposedChangesSection { get; init; }
    public required DescriptorReviewReportSectionDto ImpactAnalysisSection { get; init; }
    public required DescriptorReviewReportSectionDto DependencySummarySection { get; init; }
    public required DescriptorReviewReportSectionDto CompatibilitySection { get; init; }
    public required DescriptorReviewReportSectionDto GovernanceSection { get; init; }
    public required DescriptorReviewReportSectionDto RequiredHumanReviewSection { get; init; }
    public required DescriptorReviewReportSectionDto ActivationEligibilitySection { get; init; }
    public required DescriptorReviewReportSectionDto DiagnosticsSection { get; init; }
    public required DescriptorReviewReportSectionDto RecommendationsSection { get; init; }
    public required DescriptorReviewReportSectionDto PackagePreviewSection { get; init; }
    public required DescriptorReviewReportSectionDto StableHashesSection { get; init; }
}
```

#### DescriptorReviewReportSectionKind

```csharp
public enum DescriptorReviewReportSectionKind
{
    Summary = 1,              // SectionId: "summary"
    DraftIdentity = 2,        // SectionId: "draft_identity"
    ProposedChanges = 3,      // SectionId: "proposed_changes"
    ImpactAnalysis = 4,       // SectionId: "impact_analysis"
    DependencySummary = 5,    // SectionId: "dependency_summary"
    Compatibility = 6,        // SectionId: "compatibility"
    Governance = 7,           // SectionId: "governance"
    RequiredHumanReview = 8,  // SectionId: "required_human_review"
    ActivationEligibility = 9, // SectionId: "activation_eligibility"
    Diagnostics = 10,         // SectionId: "diagnostics"
    Recommendations = 11,     // SectionId: "recommendations"
    PackagePreview = 12,      // SectionId: "package_preview"
    StableHashes = 13         // SectionId: "stable_hashes"
}
```

#### DescriptorReviewReportSectionDto

```csharp
public sealed record DescriptorReviewReportSectionDto
{
    public required DescriptorReviewReportSectionKind Kind { get; init; }
    public required string SectionId { get; init; }       // stable lower_case external id (e.g. "summary", "draft_identity")
    public required string Title { get; init; }
    public required int Order { get; init; }              // deterministic canonical order
    public required bool IsEmpty { get; init; }           // Renderer may hide empty sections
    public required DescriptorReviewSeverity OverallSeverity { get; init; }
    public required IReadOnlyList<DescriptorReviewReportItemDto> Items { get; init; }
}
```

#### DescriptorReviewReportItemDto

```csharp
public sealed record DescriptorReviewReportItemDto
{
    public required string ItemId { get; init; }
    public required string ReasonCode { get; init; }
    public required string MessageTemplateId { get; init; }
    public required string Message { get; init; }         // canonical deterministic wording
    public required DescriptorReviewSeverity Severity { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);
    public IReadOnlyList<string> RelatedDiagnosticIds { get; init; } = [];
    public IReadOnlyList<string> RelatedDescriptorIds { get; init; } = [];
}
```

#### DescriptorReviewSeverity

```csharp
public enum DescriptorReviewSeverity { Info, Warning, Error, Blocker }
```

#### DescriptorReviewRecommendationDto

```csharp
public sealed record DescriptorReviewRecommendationDto
{
    public required string RecommendationId { get; init; }
    public required string ReasonCode { get; init; }
    public required string Message { get; init; }
    public required DescriptorReviewRecommendationKind Kind { get; init; }
    public required bool IsActionable { get; init; }
    public IReadOnlyList<string> RelatedItemIds { get; init; } = [];
}
```

#### DescriptorReviewRecommendationKind

```csharp
public enum DescriptorReviewRecommendationKind
{
    RequestActivationHandoff,   // NOT ProceedToActivation — 7d does not own activation authority
    RequestHumanReview,
    ApplyFixProposal,
    ReviseDraft,
    CancelDraft,
    NoAction
}
```

### Key Design Decisions

1. **Structured DTO is the authoritative artifact.** Markdown/PlainText are deterministic projections, not decision inputs.
2. **13 fixed sections** always present. `IsEmpty` flag allows renderers to hide empty sections.
3. **ReasonCode + MessageTemplateId + Parameters** enable deterministic text generation without LLM.
4. **RelatedDiagnosticIds / RelatedDescriptorIds** support fix proposal correlation and traceability.
5. **DescriptorReviewRecommendationKind.RequestActivationHandoff** — 7d does not own activation authority; this is a handoff request, not an activation decision.
6. **Section Order** is deterministic but not bound to enum numeric values. Canonical order is defined by the declaration order of `DescriptorReviewReportSectionKind` (Summary=1 through StableHashes=13). Builders must emit sections in this order; tests verify order stability against a canonical list.
7. **SectionId is lower_case stable id** (e.g. `summary`, `draft_identity`, `proposed_changes`), not enum name. This decouples external contract from internal enum naming.
8. **Source binding**: Report is bound to a specific review result and draft revision via `ReviewResultId`, `DraftVersion`, `SourceReviewHash`. This prevents stale reports from being confused with current state.
9. **Recommendations at top level**: `DescriptorReviewRecommendationDto` list is a first-class field on the report DTO for machine-parseable next actions. The `RecommendationsSection` provides human-readable items.
10. **ReportId generation**: Stable hash of `TenantId + DraftId + DraftVersion + ReviewResultId + ContractVersion + TemplateVersion`, using the project's `IDescriptorStableHashBuilder` pattern. `ReportId` is a generated artifact id; `SourceReviewHash` is the stable identity.
11. **RequiresManualAction consistency**: `FixProposal.RequiresManualAction == (Applicability == FixProposalApplicability.ManualActionRequired)`. Builder enforces this invariant.

---

## Section 2 — Report Builder & Renderer

### Request Object

```csharp
public sealed record DescriptorReviewReportBuildRequest
{
    public required DescriptorDraftReviewResult ReviewResult { get; init; }
    public required DescriptorDraft Draft { get; init; }
    public required bool VisibilityApplied { get; init; }
}
```

### Builder Interface

```csharp
public interface IDescriptorReviewReportBuilder
{
    DescriptorReviewReportDto Build(DescriptorReviewReportBuildRequest request);
}
```

### Builder Implementation

```csharp
// CrestCreates.Agent.ControlPlane
internal sealed class DefaultDescriptorReviewReportBuilder
    : IDescriptorReviewReportBuilder
{
    private readonly TimeProvider _clock;
    private readonly IDescriptorReviewMessageTemplateCatalog _templateCatalog;

    public DefaultDescriptorReviewReportBuilder(
        TimeProvider clock,
        IDescriptorReviewMessageTemplateCatalog templateCatalog)
    {
        _clock = clock;
        _templateCatalog = templateCatalog;
    }

    public DescriptorReviewReportDto Build(DescriptorReviewReportBuildRequest request)
    {
        // Fail-fast: Builder is projection layer, not visibility/redaction layer
        if (!request.VisibilityApplied)
        {
            throw new InvalidOperationException(
                "DescriptorReviewReportBuilder requires a visibility-projected review result. " +
                "Call with VisibilityApplied=true after applying denied descriptor kind filtering.");
        }

        // Build each of 13 sections from request.ReviewResult + request.Draft
        // Recommendations derived from other sections' typed state
        // Message populated via _templateCatalog.Format(templateId, parameters)
        // ReportId = stable hash of (TenantId + DraftId + DraftVersion + ReviewResultId + ContractVersion + TemplateVersion)
    }
}
```

### 13 Section Mapping

| Section Kind | Data Source | Logic |
|---|---|---|
| Summary | reviewResult overall | Aggregate severity counts, activation eligibility, governance decision |
| DraftIdentity | draft | DraftId, DescriptorKind, Operation, AuthorKind, Intent, Status |
| ProposedChanges | reviewResult.MaterializationResult | Proposed inventory refs, materialization status |
| ImpactAnalysis | reviewResult.ImpactAnalysisResult | Affected descriptors count/severity, dependency chains |
| DependencySummary | reviewResult.TopologySnapshot | Node/edge counts by kind, upstream/downstream summary |
| Compatibility | reviewResult.CompatibilityResult | Compatible/incompatible count, incompatibility details |
| Governance | reviewResult.GovernanceDecision | Decision, rationale, approval status |
| RequiredHumanReview | reviewResult.Diagnostics + Governance | Blocker/Error diagnostics requiring human attention |
| ActivationEligibility | reviewResult.IsActivationEligible | Eligible status, blocking reasons — **explanation only, not gate** |
| Diagnostics | reviewResult.Diagnostics | All diagnostics grouped by severity |
| Recommendations | Derived from all sections | Next action based on severity + governance + eligibility |
| PackagePreview | reviewResult.PackagePreview | Hashes, descriptor count |
| StableHashes | reviewResult.StableHashes | All hash values |

### Message Template Catalog

```csharp
public interface IDescriptorReviewMessageTemplateCatalog
{
    string Format(string messageTemplateId, IReadOnlyDictionary<string, string> parameters);
}
```

Builder decides ReasonCode / MessageTemplateId / Parameters. Catalog formats them into canonical Message. This keeps Builder from becoming projection + wording + renderer hybrid.

### Template Examples

| ReasonCode | MessageTemplateId | Message |
|---|---|---|
| `ACTIVATION_ELIGIBLE` | `report.activation.eligible` | `"Draft is eligible for activation handoff."` |
| `ACTIVATION_BLOCKED` | `report.activation.blocked` | `"Draft is not eligible: {BlockingReasons}."` |
| `GOVERNANCE_APPROVED` | `report.governance.approved` | `"Governance decision: approved. {Rationale}"` |
| `GOVERNANCE_REJECTED` | `report.governance.rejected` | `"Governance decision: rejected. {Rationale}"` |
| `GOVERNANCE_REVIEW_REQUIRED` | `report.governance.review_required` | `"Governance decision: review required. {Rationale}"` |
| `MISSING_REFERENCE` | `report.diagnostics.missing_ref` | `"Descriptor '{DescriptorId}' references missing '{ReferenceId}'."` |
| `SCHEMA_INCOMPATIBLE` | `report.compatibility.schema` | `"Schema change is incompatible: {Details}."` |
| `DRAFT_VALID` | `report.summary.valid` | `"Draft validation passed with {DiagnosticCount} diagnostics."` |
| `DRAFT_INVALID` | `report.summary.invalid` | `"Draft validation failed with {ErrorCount} errors and {BlockerCount} blockers."` |
| `HUMAN_REVIEW_REQUIRED` | `report.human_review.required` | `"Human review required: {Reason}."` |
| `NO_ACTION` | `report.recommendation.no_action` | `"No action required at this time."` |
| `PACKAGE_PREVIEW_AVAILABLE` | `report.package.available` | `"Package preview available with {DescriptorCount} descriptors."` |
| `STABLE_HASHES_COMPUTED` | `report.hashes.computed` | `"Stable hashes computed for {HashCount} items."` |

### Renderer Interface

```csharp
public interface IDescriptorReviewReportRenderer
{
    string RenderMarkdown(DescriptorReviewReportDto report);
    string RenderPlainText(DescriptorReviewReportDto report);
}
```

### Renderer Constraints (Hard Rules)

- **Reads `DescriptorReviewReportDto` only** — does not access registry, catalog, or external services
- **Uses DTO's `Message` field** — does not regenerate text via TemplateCatalog
- **Does not** perform visibility filtering, governance decisions, activation decisions
- **Does not** mutate runtime registry, execute handlers, or call LLM
- **Deterministic output**: same DTO → same output, always

### Boundary Declaration

> Builder produces the authoritative structured report and canonical deterministic item messages. Renderer produces deterministic Markdown/PlainText projections from that DTO. MessageTemplateCatalog formats ReasonCode+Parameters into canonical Message. Neither builder nor renderer performs visibility filtering, governance decisions, activation decisions, runtime registry mutation, handler execution, or LLM calls.

---

## Section 3 — Fix Proposal Contract Upgrade

### Breaking Changes Overview

| Type | Change |
|---|---|
| `FixProposal` | New fields: `Kind`, `Title`, `Explanation`, `ReasonCode`, `Applicability`, `IsExecutable`, `RequiresManualAction`, `BlocksActivationUntilResolved`, `RelatedDiagnosticIds`, `RelatedDescriptorIds`, `ContractVersion`; `ProposalId` → `Id` |
| `FixProposalAction` | `Path` → `TargetPath`; new `TargetDescriptorId`; `CurrentValue`/`ProposedValue` from `string` → `JsonElement?`; new `IsExecutable`, `SafetyLevel` |
| `FixProposalActionKind` | From 3 values to 10 values |
| New `FixProposalKind` | 8 fix kinds |
| New `FixProposalApplicability` | 4 applicability levels |
| New `FixProposalActionSafetyLevel` | 4 safety levels |

### Upgraded FixProposal

```csharp
public sealed record FixProposal
{
    public required string Id { get; init; }                           // was ProposalId
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required FixProposalKind Kind { get; init; }                // NEW
    public required string Title { get; init; }                         // NEW
    public required string Explanation { get; init; }                   // NEW
    public required string ReasonCode { get; init; }                    // NEW

    public required FixProposalApplicability Applicability { get; init; }  // NEW
    public required bool IsExecutable { get; init; }                        // NEW
    public required bool RequiresManualAction { get; init; }                // NEW
    public required bool RequiresHumanReview { get; init; }                 // kept
    public required bool BlocksActivationUntilResolved { get; init; }       // NEW — explanation only, not gate
    public required FixProposalRiskLevel RiskLevel { get; init; }

    public IReadOnlyList<string> RelatedDiagnosticIds { get; init; } = [];    // NEW
    public IReadOnlyList<string> RelatedDescriptorIds { get; init; } = [];    // NEW
    public required IReadOnlyList<FixProposalAction> Actions { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Rationale { get; init; }
    public required string ContractVersion { get; init; } = AgentControlPlaneContractVersion.Current;  // NEW
}
```

### IsExecutable Aggregation Rule

```text
FixProposal.IsExecutable =
    Applicability == FixProposalApplicability.CurrentMutableDraft
    && Actions.All(a => a.IsExecutable)
```

Builder enforces this rule. A proposal with mixed executable/non-executable actions is not executable.

### Upgraded FixProposalAction

```csharp
public sealed record FixProposalAction
{
    public required FixProposalActionKind Kind { get; init; }
    public required string TargetPath { get; init; }              // was Path
    public string? TargetDescriptorId { get; init; }              // NEW
    public JsonElement? CurrentValue { get; init; }               // was string → JsonElement?
    public JsonElement? ProposedValue { get; init; }              // was string → JsonElement?
    public required bool IsExecutable { get; init; }              // NEW
    public required FixProposalActionSafetyLevel SafetyLevel { get; init; }  // NEW
    public string? Description { get; init; }
}
```

**JsonElement usage**: Always create via `JsonSerializer.SerializeToElement(...)`. Clone when necessary to avoid `JsonDocument` lifetime issues. Both `FixProposalAction` and `JsonElement` must be registered in source-generated JSON context.

### New Enums

```csharp
public enum FixProposalKind
{
    CreateMissingDescriptor = 1,
    ReplaceMissingReference = 2,
    RemoveInvalidRelationship = 3,
    AddRequiredBindingMetadata = 4,
    SplitBreakingChangeIntoCompatibleChange = 5,
    MarkRequiresReview = 6,
    FlagUnsafeExpansion = 7,                    // NOT RejectUnsafeExpansion — 7d flags, does not reject
    SuggestVersionBump = 8
}

public enum FixProposalActionKind
{
    SetValue = 1,                    // was Set
    RemoveValue = 2,                 // was Remove
    AddValue = 3,                    // was Add
    MergeObject = 4,                 // NEW
    ReplaceReference = 5,            // NEW
    RemoveRelationship = 6,          // NEW
    AddRequiredBindingMetadata = 7,  // NEW
    SuggestVersionBump = 8,          // NEW
    MarkRequiresReview = 9,          // NEW
    ManualActionRequired = 10        // NEW
}

public enum FixProposalApplicability
{
    CurrentMutableDraft = 1,         // can be applied to current mutable draft only
    RequiresNewDraftRevision = 2,    // requires a new draft revision to apply
    ManualActionRequired = 3,        // requires manual human action outside the system
    NotApplicable = 4                // not applicable or cannot be applied
}

public enum FixProposalActionSafetyLevel
{
    Safe = 1,
    LowRisk = 2,
    RequiresReview = 3,
    Unsafe = 4
}
```

### Runtime Compatibility Rules

`ApplyFixProposalToDraftAsync` currently supports only `SetValue`/`RemoveValue`/`AddValue` on 4 fields (Intent, Rationale, ProposedVersion, CorrelationId). After upgrade:

| Condition | Result |
|---|---|
| `action.IsExecutable == false` | Return `NonExecutableFixAction` diagnostic |
| `proposal.Actions.Count > 1` | Return `UnsupportedMultiActionFixProposal` diagnostic |
| `action.Kind` not in supported subset | Return `UnsupportedFixActionKind` diagnostic |
| `action.SafetyLevel == Unsafe` | Return `UnsafeFixActionRejected` diagnostic |
| Target is active descriptor / runtime registry | Return `FixActionTargetBoundaryViolation` diagnostic |
| Target path not in allowed set | Return `FixActionTargetNotAllowed` diagnostic |

**Multi-action strategy**: Phase 7d only supports single-action executable proposals. `ApplyFixProposalToDraftAsync` rejects proposals with `Actions.Count > 1` via `UnsupportedMultiActionFixProposal` diagnostic. Multi-action support requires atomic rollback (snapshot/clone), which is deferred to a later phase. This is more honest than claiming atomicity without implementation.

### BlocksActivationUntilResolved

This is an **explanation field**, not a gate decision. It signals that the fix proposal identifies an issue that would block activation, but the actual activation gate belongs to Phase 7e or later. 7d does not own activation blocking authority.

### Migration Impact

- `SuggestDescriptorDraftFixesAsync.GenerateFixActions()` updated: `Set`→`SetValue`, `Remove`→`RemoveValue`, `Add`→`AddValue`; `Path`→`TargetPath`; `string`→`JsonElement?` via `JsonSerializer.SerializeToElement()`; new fields populated.
- All tests constructing `FixProposal` / `FixProposalAction` must update to new shape.
- `ApplyFixProposalToDraftAsync` updated with 5 new diagnostic checks.

---

## Section 4 — Service Integration & Tool Surface

### New Tool Methods

```csharp
// IAgentControlPlaneToolService additions
Task<AgentToolResult<DescriptorReviewReportDto>> BuildDescriptorReviewReportAsync(
    AgentToolInvocationContext context,
    string draftId,
    CancellationToken ct = default);

Task<AgentToolResult<string>> RenderDescriptorReviewReportAsync(
    AgentToolInvocationContext context,
    DescriptorReviewReportDto report,
    DescriptorReviewReportFormat format,
    CancellationToken ct = default);
```

### DescriptorReviewReportFormat

```csharp
public enum DescriptorReviewReportFormat { Markdown, PlainText }
```

### Manifest Additions

| Tool Name | Permission | ReadOnly | Category |
|---|---|---|---|
| `BuildDescriptorReviewReport` | `agent.review.report` | Yes | Review |
| `RenderDescriptorReviewReport` | `agent.review.render` | Yes | Review |

**Tool count**: 32 → 34.

**Render-by-DTO**: `RenderDescriptorReviewReportAsync` accepts `DescriptorReviewReportDto` directly, not a `reportId`. The DTO is the authoritative artifact; `_reports` dictionary is optional ephemeral cache only. A convenience `RenderStoredDescriptorReviewReportAsync(context, reportId, format)` exists internally but is **not** exposed as a tool.

### Service Implementation Flows

#### BuildDescriptorReviewReportAsync

```
context + draftId
  → ExecuteAsync (manifest → authorization → scope)
  → ResolveDraftAsync
  → DenyIfInvisible
  → Lookup reviewResult from _reviewResults
  → _reportBuilder.Build(request with reviewResult + draft + VisibilityApplied=true)
  → Store report in _reports dictionary (optional cache)
  → Return AgentToolResult<DescriptorReviewReportDto>
```

#### RenderDescriptorReviewReportAsync

```
context + report DTO + format
  → ExecuteAsync (authorization check only — no registry/cache access)
  → Validate report.ContractVersion == AgentControlPlaneContractVersion.Current
    (mismatch → UnsupportedReportContractVersion diagnostic)
  → format switch { Markdown → _renderer.RenderMarkdown, PlainText → _renderer.RenderPlainText }
  → Return AgentToolResult<string>
```

### DI Registration

```csharp
services.AddSingleton<IDescriptorReviewReportBuilder, DefaultDescriptorReviewReportBuilder>();
services.AddSingleton<IDescriptorReviewReportRenderer, DefaultDescriptorReviewReportRenderer>();
services.AddSingleton<IDescriptorReviewMessageTemplateCatalog, DefaultDescriptorReviewMessageTemplateCatalog>();
```

### Existing Method Updates

**SuggestDescriptorDraftFixesAsync**: `GenerateFixActions()` updated to use new `FixProposalKind`, `FixProposalActionKind` (SetValue/RemoveValue/AddValue), `TargetPath`, `JsonElement?` values, and populate all new FixProposal fields.

**ApplyFixProposalToDraftAsync**: Updated with 5 new diagnostic checks (NonExecutableFixAction, UnsupportedFixActionKind, UnsafeFixActionRejected, FixActionTargetBoundaryViolation, FixActionTargetNotAllowed). Multi-action fail-fast strategy.

### JSON Context Updates

`AgentControlPlaneToolJsonSerializerContext` new registrations:

- `DescriptorReviewReportDto`
- `DescriptorReviewReportSectionDto`
- `DescriptorReviewReportItemDto`
- `DescriptorReviewRecommendationDto`
- `DescriptorReviewReportBuildRequest`
- `DescriptorReviewReportSectionKind`
- `DescriptorReviewSeverity`
- `DescriptorReviewRecommendationKind`
- `DescriptorReviewReportFormat`
- `FixProposalKind`
- `FixProposalApplicability`
- `FixProposalActionSafetyLevel`
- `JsonElement` (required for FixProposalAction)
- Updated `FixProposal` / `FixProposalAction` registrations (field changes)

---

## Section 5 — Testing Strategy

### 5.1 Report Builder Tests

| Test | Verification |
|------|---|
| `Build_AllowedDraft_ProducesReportWith_RequestActivationHandoff` | Activation-eligible → Recommendations contains RequestActivationHandoff |
| `Build_ReviewRequiredDraft_ProducesReportWith_RequestHumanReview` | Review-required → Recommendations contains RequestHumanReview |
| `Build_BlockedDraft_ProducesReportWith_NoActivation` | Blocked → Recommendations does not contain RequestActivationHandoff |
| `Build_AllSections_AlwaysPresent` | 13 sections always exist, empty sections have IsEmpty=true |
| `Build_SectionOrder_IsDeterministic` | Same input → same section order |
| `Build_SectionOrder_MatchesCanonicalSectionOrder` | Order matches DescriptorReviewReportSectionKind declaration order |
| `Build_DiagnosticsGroupedBySeverity` | Diagnostics section items sorted by severity |
| `Build_EmptyDiagnostics_ProducesUsefulSummary` | Empty diagnostics → Summary still has content |
| `Build_StableHashesIncluded_WhenPresent` | StableHashes section contains hash values |
| `Build_PackagePreviewIncluded_WhenPresent` | PackagePreview section contains preview data |
| `Build_VisibilityApplied_True_PreservedInRequest` | VisibilityApplied=true correctly passed |
| `Build_IsActivationEligible_IsExplanationNotGate` | ActivationEligibility is explanation, not gate decision |
| `Build_BlocksActivationUntilResolved_IsExplanationNotGate` | Field is explanation, not gate |
| `Build_DeniedDescriptorKind_NotPresent_InReportItems` | Denied kinds filtered from item RelatedDescriptorIds |
| `Build_DeniedDescriptorKind_NotPresent_InRelatedDescriptorIds` | Denied kinds not in RelatedDescriptorIds |
| `Build_DeniedDescriptorKind_NotPresent_InPackagePreviewSection` | Denied kinds not in PackagePreview items |
| `Build_DeniedDescriptorKind_NotPresent_InStableHashesSection` | Denied kinds not in StableHashes items |
| `Build_DeniedDescriptorKind_NotPresent_InImpactAnalysisSection` | Denied kinds not in ImpactAnalysis items |
| `Build_VisibilityAppliedFalse_ThrowsInvalidOperationException` | Builder rejects non-visibility-projected input |
| `Build_ReportId_IsStableHash` | ReportId is deterministic stable hash of source binding fields |
| `Build_RequiresManualAction_ConsistentWithApplicability` | RequiresManualAction == (Applicability == ManualActionRequired) |

### 5.2 Message Template Catalog Tests

| Test | Verification |
|------|---|
| `Format_KnownTemplateId_ReturnsFormattedMessage` | Known template + parameters → correct output |
| `Format_UnknownTemplateId_ReturnsFallbackMessage` | Unknown template → fallback, no exception |
| `Format_SameInput_DeterministicOutput` | Same template + parameters → same output |

### 5.3 Renderer Tests

| Test | Verification |
|------|---|
| `RenderMarkdown_AllSections_Rendered` | All 13 sections appear in Markdown output |
| `RenderMarkdown_EmptySections_OptionallyHidden` | IsEmpty=true sections can be hidden |
| `RenderMarkdown_Deterministic` | Same DTO → same Markdown output |
| `RenderPlainText_Deterministic` | Same DTO → same PlainText output |
| `Renderer_UsesDtoMessage_NotTemplateCatalog` | Renderer reads DTO Message, does not call TemplateCatalog |
| `Renderer_DoesNotRequireExternalServices` | Renderer works without registry/catalog/external dependencies |
| `Renderer_Deterministic_WithSameDto` | Repeated render of same DTO → identical output |
| `RenderMarkdown_DeniedDescriptorKind_NotRendered` | Denied kinds do not appear in rendered output |
| `RenderPlainText_DeniedDescriptorKind_NotRendered` | Denied kinds do not appear in rendered output |

### 5.4 Render-by-DTO Tests

| Test | Verification |
|------|---|
| `RenderDescriptorReviewReport_ByDto_DoesNotRequireStoredReport` | Render works without _reports cache |
| `RenderDescriptorReviewReport_ByDto_Deterministic` | Same DTO → same output |
| `RenderDescriptorReviewReport_ByDto_RejectsUnsupportedFormat` | Invalid format → error |
| `RenderDescriptorReviewReport_ByDto_RejectsUnsupportedContractVersion` | Wrong ContractVersion → UnsupportedReportContractVersion diagnostic |
| `Manifest_DoesNotInclude_RenderStoredDescriptorReviewReport` | RenderStored is not a tool |

### 5.5 Fix Proposal Contract Tests

| Test | Verification |
|------|---|
| `FixProposal_IsExecutable_AggregationRule` | IsExecutable = Applicability==CurrentMutableDraft && Actions.All(IsExecutable) |
| `FixProposal_ContractVersion_Present` | All FixProposal have ContractVersion |
| `FixProposalAction_TargetPath_NotNull` | TargetPath must be non-null |
| `FixProposalAction_JsonElement_RoundTrip_String` | JsonElement string round-trip |
| `FixProposalAction_JsonElement_RoundTrip_Number` | JsonElement number round-trip |
| `FixProposalAction_JsonElement_RoundTrip_Object` | JsonElement object round-trip |
| `FixProposalAction_JsonElement_RoundTrip_Null` | JsonElement null round-trip |
| `FixProposalActionKind_Values_1Through10` | Enum values 1-10, contiguous |

### 5.6 Apply Fix Proposal Tests (Upgraded)

| Test | Verification |
|------|---|
| `Apply_NonExecutableAction_Returns_NonExecutableFixAction` | action.IsExecutable=false → diagnostic |
| `Apply_UnsupportedKind_Returns_UnsupportedFixActionKind` | Unsupported Kind → diagnostic |
| `Apply_UnsafeAction_Returns_UnsafeFixActionRejected` | SafetyLevel=Unsafe → diagnostic |
| `Apply_BoundaryViolation_Returns_FixActionTargetBoundaryViolation` | Active descriptor target → diagnostic |
| `Apply_NotAllowedTarget_Returns_FixActionTargetNotAllowed` | Disallowed path → diagnostic |
| `Apply_SupportedAction_Succeeds` | SetValue on allowed fields → success |
| `Apply_MixedSupportedAndUnsupportedActions_FailsWithoutMutation` | Any unsupported action → entire proposal rejected, no draft mutation |
| `Apply_MultiActionProposal_Returns_UnsupportedMultiActionFixProposal` | Actions.Count > 1 → diagnostic, no mutation |

### 5.7 Coverage Tests

| Test | Verification |
|------|---|
| `AllPublicToolContractDtos_Have_JsonTypeInfo` | All public DTOs have JSON registration |
| `ManifestToolNames_Match_ContractRegistrations` | manifest = contract set equality |
| `Manifest_Includes_BuildDescriptorReviewReport` | New tool in manifest |
| `Manifest_Includes_RenderDescriptorReviewReport` | New tool in manifest |
| `Manifest_DoesNotInclude_ApproveOrActivateTools` | No governance/activation tools in manifest |
| `Manifest_DoesNotInclude_RenderStoredDescriptorReviewReport` | RenderStored not in manifest |

### 5.8 Boundary Tests

| Test | Verification |
|------|---|
| `ReportBuilder_DoesNot_PerformVisibilityFiltering` | Builder does not filter — uses pre-filtered input |
| `Renderer_DoesNot_MutateGovernanceOrActivation` | Renderer does not change governance/activation state |
| `FixProposal_DoesNot_MutateActiveRegistry` | Apply does not modify active registry |
| `FixProposal_BlocksActivationUntilResolved_IsExplanationOnly` | Field is explanation, not gate |

---

## Acceptance Criteria

### Review Report

- [ ] `DescriptorReviewReportDto` with 13 fixed sections, all always present
- [ ] Each section has `Kind`, `SectionId`, `Title`, `Order`, `IsEmpty`, `OverallSeverity`, `Items`
- [ ] Each item has `ReasonCode`, `MessageTemplateId`, `Message`, `Severity`, `Parameters`
- [ ] Report has source binding fields: `ReviewResultId`, `DraftVersion`, `SourceReviewHash`, `TemplateVersion`
- [ ] Report has top-level `Recommendations` list for machine-parseable next actions
- [ ] ReportId is stable hash of (TenantId + DraftId + DraftVersion + ReviewResultId + ContractVersion + TemplateVersion)
- [ ] Builder accepts pre-filtered `DescriptorDraftReviewResult` via `DescriptorReviewReportBuildRequest`
- [ ] Builder fail-fast on `VisibilityApplied=false`
- [ ] Renderer produces deterministic Markdown and PlainText from DTO
- [ ] Renderer does not access external services, registry, or LLM
- [ ] Render tool validates `ContractVersion` on input DTO
- [ ] Message Template Catalog provides deterministic `Format(templateId, parameters)`

### Fix Proposal Contract

- [ ] `FixProposal` upgraded with `Kind`, `Title`, `Explanation`, `ReasonCode`, `Applicability`, `IsExecutable`, `RequiresManualAction`, `BlocksActivationUntilResolved`, `RelatedDiagnosticIds`, `RelatedDescriptorIds`, `ContractVersion`
- [ ] `FixProposalAction` upgraded with `TargetPath`, `TargetDescriptorId`, `JsonElement?` values, `IsExecutable`, `SafetyLevel`
- [ ] `FixProposalActionKind` expanded to 10 values
- [ ] `FixProposalKind` has 8 values
- [ ] `FixProposalApplicability` has 4 values
- [ ] `FixProposalActionSafetyLevel` has 4 values
- [ ] IsExecutable aggregation rule enforced
- [ ] `BlocksActivationUntilResolved` is explanation only, not gate
- [ ] `RequiresManualAction == (Applicability == ManualActionRequired)` consistency enforced
- [ ] Phase 7d only supports single-action executable proposals (multi-action rejected)

### Runtime Compatibility

- [ ] `ApplyFixProposalToDraftAsync` rejects unsupported kinds, non-executable actions, unsafe actions, boundary violations, multi-action proposals
- [ ] 6 distinct diagnostic codes for different rejection reasons (including UnsupportedMultiActionFixProposal)
- [ ] No partial application — single-action only in Phase 7d
- [ ] Existing supported subset (SetValue/RemoveValue/AddValue on 4 fields) still works

### Coverage

- [ ] 34 tools in manifest
- [ ] All public DTOs registered in `AgentControlPlaneToolJsonSerializerContext`
- [ ] manifest = contract set equality

### Boundaries

- [ ] Builder does not perform visibility filtering
- [ ] Renderer does not call LLM or external services
- [ ] Fix proposal does not mutate active registry
- [ ] 7d does not own activation gate, governance authority, or runtime registry mutation
