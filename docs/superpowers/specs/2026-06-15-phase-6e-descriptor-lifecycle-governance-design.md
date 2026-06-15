# Phase 6e — Descriptor Lifecycle Governance: Design Spec

> **Date:** 2026-06-15 | **Status:** Draft | **Phase 6e**
> **Parent Issue:** [#10 — Phase 6e: Descriptor Lifecycle Governance](https://github.com/OrchesAdam/CrestCreates/issues/10)

---

## 1. Overview

### 1.1 Goal

Phase 6e answers one question:

> Given the current analysis reports and a requested descriptor lifecycle transition, can this transition proceed, must it be blocked, or does it require review/approval?

It produces a deterministic governance report suitable for future publish pipelines, CI gates, admin workflows, and migration tooling.

### 1.2 Position in Phase 6

```
ValidationReport
+ RuntimeBindingReport / DescriptorBindingReport
+ DescriptorTopologySnapshot.Diagnostics
+ DescriptorImpactAnalysisReport
+ DescriptorCompatibilityReport
        ↓
DescriptorLifecycleGovernanceService
        ↓
Allowed / Blocked / ReviewRequired transition decision
```

Phase 6e is the governance gate. It must not duplicate validation, binding synthesis, topology building, impact traversal, or compatibility analysis.

### 1.3 Boundary Rule

```
6e decides governance outcome.
6e does not persist approval.
6e does not mutate descriptor state.
6e does not publish runtime changes.
```

### 1.4 Design Principles

1. **Consume, do not recompute** — all analysis reports are inputs; no re-validation, re-binding, re-topology, re-impact, or re-compatibility.
2. **Governance operation ≠ descriptor state** — model requested operations separately from `DescriptorState`.
3. **Stateless and deterministic** — the service is a pure function over `(request) → report`.
4. **AoT-friendly** — records, enums, static dispatch; no runtime reflection, dynamic, expression trees, or service location.
5. **Classification only** — persistence, approval workflows, and publish execution belong to later phases.
6. **Conservative defaults** — `SubmitForReview` is less strict than `Activate`; breaking compatibility defaults to `ReviewRequired`, not `Blocked`.
7. **Source-normalized findings** — stable `Source` and `Code` values for future UI/CI grouping.

---

## 2. Scope Boundary

### 2.1 In Scope

- `DescriptorLifecycleTransition`
- `DescriptorLifecycleOperation`
- `DescriptorLifecycleDecisionKind`
- `DescriptorLifecycleFindingSeverity`
- `DescriptorLifecycleFinding`
- `DescriptorLifecycleDecision`
- `DescriptorLifecycleGovernanceReport`
- `DescriptorLifecycleGovernanceOptions`
- `DescriptorLifecycleGovernanceRequest`
- `IDescriptorLifecycleGovernanceService`
- `DefaultDescriptorLifecycleGovernanceService`
- `AddDescriptorLifecycleGovernance()` DI registration
- Unit tests covering operation-specific policy mapping and option overrides

### 2.2 Consumed Reports (Inputs)

| Report | Source Phase | Key Types |
|---|---|---|
| `ValidationReport` | Pre-6 | `ValidationIssue` (Severity + Message) |
| `RuntimeBindingReport` | 5h | `DescriptorBindingReport`, `DescriptorBindingStatus` |
| `DescriptorTopologyDiagnostics` | 6b | `DescriptorTopologyDiagnostic` (Error/Warning, Code, Message) |
| `DescriptorImpactAnalysisReport` | 6c | `AffectedDescriptor`, `DescriptorImpactSeverity`, `DescriptorImpactDiagnostic` |
| `DescriptorCompatibilityReport` | 6d | `DescriptorCompatibilityFinding`, `DescriptorCompatibilityLevel`, `DescriptorCompatibilityDiagnostic` |

### 2.3 Out of Scope

- Descriptor validation rules
- Binding contributors
- Relationship extraction
- Topology building
- Impact traversal
- Compatibility rules
- Descriptor diffing / change-set building
- Registry bootstrapping
- Persistence of approvals
- Approval workflow engine
- UI / API / AppService
- GitHub/check-run integration
- Migration script generation
- Runtime deployment / publish execution
- Event outbox
- Distributed lock
- Database persistence
- Changes to Capability Authorization / DataPermission / HumanTask runtime
- Changes to `IPermissionChecker`
- Changes to claims/token behavior
- Changes to `HumanTaskCompletedEvent`
- Expanding `IDescriptor.State` with review workflow states

---

## 3. Key Design Decision: Governance Operation vs Descriptor State

Do not overload `DescriptorState`.

Current `DescriptorState` is descriptor metadata state:

```csharp
public enum DescriptorState
{
    Draft,
    Active,
    Deprecated,
    Removed
}
```

Phase 6e models requested governance operations separately:

```csharp
public enum DescriptorLifecycleOperation
{
    ValidateDraft,
    SubmitForReview,
    Approve,
    Activate,
    Deprecate,
    Retire,
    Reject
}
```

Interpretation:

| Operation | Meaning |
|---|---|
| `ValidateDraft` | Early authoring gate |
| `SubmitForReview` | Can a human review this package/change? |
| `Approve` | Would governance approve this transition, assuming review authority exists? |
| `Activate` | Can this be made runtime-active now? |
| `Deprecate` | Can this be marked deprecated? |
| `Retire` | Can this be removed/retired? |
| `Reject` | Can this review/change request be rejected? |

This keeps governance decisions separate from descriptor metadata. Later phases can persist approval state in a dedicated store if needed.

---

## 4. Core Types

All new abstractions live under:

```
framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/
```

Implementation lives in:

```
framework/src/CrestCreates.Metadata/DescriptorLifecycle/
```

Tests in:

```
framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/
```

### 4.1 DescriptorLifecycleTransition

```csharp
public sealed record DescriptorLifecycleTransition
{
    public required DescriptorRef Subject { get; init; }
    public required DescriptorLifecycleOperation Operation { get; init; }
    public DescriptorState? FromState { get; init; }
    public DescriptorState? ToState { get; init; }
    public string? Reason { get; init; }
}
```

Notes:
- `Subject` uses the existing version-aware `DescriptorRef`.
- `FromState` / `ToState` are optional because some governance checks may be package-level.
- Do not mutate descriptors inside the governance service.

### 4.2 DescriptorLifecycleDecisionKind

```csharp
public enum DescriptorLifecycleDecisionKind
{
    Allowed,
    ReviewRequired,
    Blocked
}
```

Ordering: `Blocked > ReviewRequired > Allowed`

### 4.3 DescriptorLifecycleFindingSeverity

```csharp
public enum DescriptorLifecycleFindingSeverity
{
    Info,
    Warning,
    Review,
    Blocker
}
```

This avoids overloading `ValidationSeverity`. Lifecycle findings are governance findings, not structural validation findings.

### 4.4 DescriptorLifecycleFinding

```csharp
public sealed record DescriptorLifecycleFinding
{
    public required DescriptorLifecycleFindingSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
    public string? Source { get; init; }
    public IReadOnlyList<DescriptorRef> RelatedRefs { get; init; }
        = Array.Empty<DescriptorRef>();
    public string? SuggestedAction { get; init; }
}
```

`Source` values must be stable strings:

| Source Value | Meaning |
|---|---|
| `validation` | From `ValidationReport` |
| `binding` | From `RuntimeBindingReport` |
| `topology` | From `DescriptorTopologyDiagnostics` |
| `impact` | From `DescriptorImpactAnalysisReport` |
| `compatibility` | From `DescriptorCompatibilityReport` |
| `policy` | Governance policy decision |

### 4.5 DescriptorLifecycleDecision

```csharp
public sealed record DescriptorLifecycleDecision
{
    public required DescriptorLifecycleTransition Transition { get; init; }
    public required DescriptorLifecycleDecisionKind Decision { get; init; }
    public required IReadOnlyList<DescriptorLifecycleFinding> Findings { get; init; }
}
```

### 4.6 DescriptorLifecycleGovernanceReport

```csharp
public sealed record DescriptorLifecycleGovernanceReport
{
    public required IReadOnlyList<DescriptorLifecycleDecision> Decisions { get; init; }
    public required DescriptorLifecycleDecisionKind MaxDecision { get; init; }
    public required IReadOnlyList<DescriptorLifecycleFinding> PackageFindings { get; init; }

    public bool IsAllowed => MaxDecision == DescriptorLifecycleDecisionKind.Allowed;
    public bool RequiresReview => MaxDecision == DescriptorLifecycleDecisionKind.ReviewRequired;
    public bool IsBlocked => MaxDecision == DescriptorLifecycleDecisionKind.Blocked;
}
```

Package-level findings are for issues that cannot be attributed to one descriptor transition, such as change-set mismatch or topology-wide diagnostics.

### 4.7 DescriptorLifecycleGovernanceOptions

```csharp
public sealed record DescriptorLifecycleGovernanceOptions
{
    // Validation
    public bool TreatValidationWarningsAsReviewRequired { get; init; } = false;

    // Binding — SubmitForReview
    public bool BlockSubmitForReviewOnUnboundBinding { get; init; } = false;
    public bool BlockSubmitForReviewOnUnsupportedBinding { get; init; } = false;
    public bool TreatSubmitForReviewUnsupportedBindingAsReviewRequired { get; init; } = true;
    public bool TreatSubmitForReviewPartialBindingAsReviewRequired { get; init; } = true;

    // Binding — Activate
    public bool BlockActivateOnUnboundBinding { get; init; } = true;
    public bool BlockActivateOnUnsupportedBinding { get; init; } = true;
    public bool TreatBindingPartialAsReviewRequired { get; init; } = false;

    // Compatibility
    public bool TreatBreakingCompatibilityAsReviewRequired { get; init; } = true;
    public bool TreatSecuritySensitiveAsReviewRequired { get; init; } = true;
    public bool TreatRiskyCompatibilityAsReviewRequired { get; init; } = true;
    public bool TreatCompatibilityUnsupportedAsReviewRequired { get; init; } = true;
    public bool BlockActivateOnBreakingCompatibility { get; init; } = false;

    // Impact
    public DescriptorImpactSeverity ReviewRequiredImpactThreshold { get; init; }
        = DescriptorImpactSeverity.Critical;

    // Diagnostics
    public bool BlockOnTopologyErrors { get; init; } = true;
    public bool BlockOnImpactDiagnosticsErrors { get; init; } = true;
    public bool BlockOnCompatibilityDiagnosticsErrors { get; init; } = true;
}
```

Default policy rationale:
- Compatibility drives compatibility governance; impact severity may trigger review, but must not be labeled breaking.
- Breaking compatibility defaults to `ReviewRequired`, not `Blocked`, because Phase 6e does not persist approvals.
- Callers that use 6e as a hard CI gate can set `BlockActivateOnBreakingCompatibility = true`.
- `SubmitForReview` is intentionally less strict than `Activate` by default.

### 4.8 DescriptorLifecycleGovernanceRequest

```csharp
public sealed record DescriptorLifecycleGovernanceRequest
{
    public required IReadOnlyList<DescriptorLifecycleTransition> Transitions { get; init; }
    public required ValidationReport ValidationReport { get; init; }
    public required RuntimeBindingReport BindingReport { get; init; }
    public required DescriptorTopologyDiagnostics TopologyDiagnostics { get; init; }
    public required DescriptorImpactAnalysisReport ImpactReport { get; init; }
    public required DescriptorCompatibilityReport CompatibilityReport { get; init; }
    public DescriptorLifecycleGovernanceOptions Options { get; init; } = new();
}
```

The request object keeps the public surface source-compatible as governance inputs grow.

### 4.9 Service Interface

```csharp
public interface IDescriptorLifecycleGovernanceService
{
    DescriptorLifecycleGovernanceReport Evaluate(
        DescriptorLifecycleGovernanceRequest request);
}
```

The service is stateless. The caller supplies all reports.

---

## 5. Default Policy Mapping

### 5.1 Validation

| Validation Signal | Governance Decision |
|---|---|
| Validation Error | `Blocked` |
| Validation Warning | `Allowed` by default; `ReviewRequired` if `TreatValidationWarningsAsReviewRequired = true` |
| Validation Info | `Allowed` |

Because `ValidationIssue` has no descriptor ref, validation findings are package-level unless future validators provide a richer issue type. Do not extend `ValidationIssue` for lifecycle governance metadata.

### 5.2 Runtime Binding

Binding policy is operation-specific.

**For `ValidateDraft`:**

| Binding Status | Decision |
|---|---|
| Invalid / Unbound / Unsupported / PartiallyBound | Report finding if present, but do not block by default |
| RuntimeReady | `Allowed` |

**For `SubmitForReview`:**

| Binding Status | Decision |
|---|---|
| Invalid | `Blocked` |
| Unbound | `ReviewRequired` by default; `Blocked` only if `BlockSubmitForReviewOnUnboundBinding = true` |
| Unsupported | `ReviewRequired` by default; `Blocked` only if `BlockSubmitForReviewOnUnsupportedBinding = true` |
| PartiallyBound | `ReviewRequired` by default |
| RuntimeReady | `Allowed` |

Rationale: `SubmitForReview` is where humans may intentionally review an incomplete or not-yet-bound descriptor draft. Blocking all unbound descriptors here makes it too close to `Activate`.

**For `Approve`:**

| Binding Status | Decision |
|---|---|
| Invalid | `Blocked` |
| Unbound | `ReviewRequired` by default |
| Unsupported | `ReviewRequired` by default |
| PartiallyBound | `ReviewRequired` by default |
| RuntimeReady | `Allowed` |

`Approve` does not persist approval. It only answers whether the current reports justify approval.

**For `Activate`:**

| Binding Status | Decision |
|---|---|
| Invalid | `Blocked` |
| Unbound | `Blocked` by default |
| Unsupported | `Blocked` by default |
| PartiallyBound | `Allowed` by default; `ReviewRequired` if `TreatBindingPartialAsReviewRequired = true` |
| RuntimeReady | `Allowed` |

Rationale:
- `Invalid` means unresolved references.
- `Unbound` means required runtime binding is missing.
- Binding `Unsupported` means the runtime explicitly cannot support the declared feature; for activation it should be blocked by default.
- This is different from compatibility `Unsupported`.

**For `Deprecate` / `Retire` / `Reject`:**

- Binding issues may be reported.
- Runtime readiness should not generally block `Reject`.
- `Retire` should be governed mostly by compatibility/impact/topology signals, not by whether the retiring descriptor is runtime-ready.

### 5.3 Topology Diagnostics

| Topology Signal | Decision |
|---|---|
| Topology Error | `Blocked` by default (`BlockOnTopologyErrors = true`) |
| Topology Warning | `ReviewRequired` |

Do not rebuild topology. Only consume `DescriptorTopologyDiagnostics`.

### 5.4 Impact Analysis

| Impact Signal | Decision |
|---|---|
| Impact diagnostics Error | `Blocked` by default (`BlockOnImpactDiagnosticsErrors = true`) |
| Impact diagnostics Warning | `ReviewRequired` |
| MaxSeverity ≥ `ReviewRequiredImpactThreshold` | `ReviewRequired` |

Default threshold is `Critical`.

**Important rule:**

```
High impact does not mean breaking.
Low impact does not mean compatible.
```

Impact can raise governance review, but must not be converted into compatibility classification.

### 5.5 Compatibility

| Compatibility Level | Decision |
|---|---|
| `Breaking` | `ReviewRequired` by default; `Blocked` for `Activate` only if `BlockActivateOnBreakingCompatibility = true` |
| `SecuritySensitive` | `ReviewRequired` by default |
| `Risky` | `ReviewRequired` by default |
| `Unsupported` | `ReviewRequired` by default |
| `Compatible` | `Allowed` |

**Important semantics:**

```
Compatibility Unsupported = the analyzer lacks enough semantic knowledge.
It is not more severe than Breaking.
```

6e may require manual review for `Unsupported`, but must not report it as `Breaking`.

### 5.6 Diagnostics from Compatibility

| Compatibility Diagnostic Signal | Decision |
|---|---|
| Compatibility Diagnostic Error | `Blocked` by default (`BlockOnCompatibilityDiagnosticsErrors = true`) |
| Compatibility Diagnostic Warning | `ReviewRequired` |

This covers cases like analysis inconsistency or untrusted impact reports.

---

## 6. Operation Semantics

### 6.1 ValidateDraft

- Early authoring gate.
- Blocks on validation errors.
- May report binding/topology/impact/compatibility findings (caller must pass empty reports if not available; all five reports are required in the request).
- Should not require runtime readiness by default.

### 6.2 SubmitForReview

- Asks whether a human can review this package/change.
- Blocks on validation errors, topology errors, and binding `Invalid`.
- Binding `Unbound` should be `ReviewRequired` by default, not blocked.
- Binding `Unsupported` should be `ReviewRequired` or `Blocked` depending on option.
- Binding `PartiallyBound` should be `ReviewRequired` by default.
- Risky/breaking/security/compatibility-unsupported changes should produce `ReviewRequired`.

### 6.3 Approve

- Asks whether governance would approve this transition, assuming review authority exists.
- Does not persist approval.
- Should still surface compatibility `Breaking`, `SecuritySensitive`, `Risky`, and `Unsupported` as review findings unless policy overrides.

### 6.4 Activate

- Asks whether this can be made runtime-active now.
- Blocks on validation errors, topology errors, binding invalid/unbound/unsupported, and compatibility diagnostic errors.
- Breaking/security/risky/compatibility-unsupported changes should require review by default.
- Breaking can be made a hard block by setting `BlockActivateOnBreakingCompatibility = true`.
- The service only decides; it does not change descriptor state to `Active`.

### 6.5 Deprecate

- Should generally be allowed when validation/topology reports are healthy.
- If active consumers are affected, compatibility report should make it `ReviewRequired`.

### 6.6 Retire

- Maps conceptually to `DescriptorState.Removed` but does not mutate descriptor state.
- Removed descriptors with consumers should be `ReviewRequired` or `Blocked` according to policy.

### 6.7 Reject

- Should usually be allowed unless request/report consistency is broken.
- Should not require runtime readiness.
- No descriptor mutation or persistence in Phase 6e.

---

## 7. Consistency Checks

The governance service should check report consistency without recomputing reports:

1. `compatibilityReport.ChangeSet` must match `impactReport.ChangeSet` by ordered `(Ref, Kind)` pairs.
2. Every requested transition subject should appear in the change set when the operation is change-driven. Change-driven operations are: `SubmitForReview`, `Approve`, `Activate`, `Deprecate`, `Retire`. `ValidateDraft` and `Reject` do not require change-set subject presence.
3. Binding report matching rules:
   - `DescriptorBindingReport.DescriptorId` must be treated as `DescriptorRef.FullId` (`{Namespace}.{Id}`) per the current contributor convention (contributors write `FullId` into `DescriptorId`).
   - Matching uses `DescriptorKind` from the binding report to derive the canonical namespace, then compares `DescriptorId` against transition subject `FullId`.
   - If `DescriptorId` cannot be parsed into a valid `(Namespace, Id)` pair, emit a package-level `ReviewRequired` finding with source `binding` and code `LIFECYCLE_BINDING_ID_UNRESOLVABLE`.
   - If `DescriptorKind` from the binding report does not match the transition subject's expected kind, emit a package-level `ReviewRequired` finding with source `binding` and code `LIFECYCLE_BINDING_KIND_MISMATCH`.
   - If the same `(Kind, Namespace, Id)` pair appears in the binding report with multiple distinct statuses (due to multiple versions in the runtime), emit a package-level `ReviewRequired` finding with source `binding` and code `LIFECYCLE_BINDING_VERSION_AMBIGUITY`.
   - Phase 6e must not redesign `DescriptorBindingReport` or add `DescriptorRef` / `Namespace` / `Version` fields to it.
4. Empty transitions should produce an allowed report with package info finding, not throw.

Do not use reference equality for report/change-set consistency.

---

## 8. DI Registration

Add to `MetadataServiceCollectionExtensions`:

```csharp
public static IServiceCollection AddDescriptorLifecycleGovernance(
    this IServiceCollection services)
{
    services.TryAddSingleton<IDescriptorLifecycleGovernanceService,
        DefaultDescriptorLifecycleGovernanceService>();
    return services;
}
```

The service should be stateless and singleton-safe.

Do not make it depend on registries or providers. The caller supplies reports.

---

## 9. Project Structure

### 9.1 New Abstractions

```
framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/
  DescriptorLifecycleOperation.cs
  DescriptorLifecycleDecisionKind.cs
  DescriptorLifecycleFindingSeverity.cs
  DescriptorLifecycleTransition.cs
  DescriptorLifecycleFinding.cs
  DescriptorLifecycleDecision.cs
  DescriptorLifecycleGovernanceReport.cs
  DescriptorLifecycleGovernanceOptions.cs
  DescriptorLifecycleGovernanceRequest.cs
  IDescriptorLifecycleGovernanceService.cs
```

### 9.2 New Implementation

```
framework/src/CrestCreates.Metadata/DescriptorLifecycle/
  DefaultDescriptorLifecycleGovernanceService.cs
```

### 9.3 Modified Files

```
framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs
```

Add `AddDescriptorLifecycleGovernance()` extension method.

### 9.4 Tests

```
framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/
  DescriptorLifecycleGovernanceServiceTests.cs
```

---

## 10. Test Plan

### 10.1 Validation Policy

| # | Test |
|---|---|
| 1 | `ValidationError_BlocksTransition` |
| 2 | `ValidationWarning_AllowsByDefault` |
| 3 | `ValidationWarning_ReviewRequired_WhenOptionEnabled` |

### 10.2 Binding Policy — ValidateDraft

| # | Test |
|---|---|
| 4 | `ValidateDraft_DoesNotRequireRuntimeBinding` |

### 10.3 Binding Policy — SubmitForReview

| # | Test |
|---|---|
| 5 | `SubmitForReview_BindingInvalid_Blocks` |
| 6 | `SubmitForReview_BindingUnbound_ReviewRequiredByDefault` |
| 7 | `SubmitForReview_BindingUnbound_Blocks_WhenOptionEnabled` |
| 8 | `SubmitForReview_BindingUnsupported_ReviewRequiredByDefault` |
| 9 | `SubmitForReview_BindingUnsupported_Blocks_WhenOptionEnabled` |
| 10 | `SubmitForReview_BindingPartiallyBound_ReviewRequiredByDefault` |

### 10.4 Binding Policy — Activate

| # | Test |
|---|---|
| 11 | `Activate_BindingInvalid_Blocks` |
| 12 | `Activate_BindingUnbound_BlocksByDefault` |
| 13 | `Activate_BindingUnsupported_BlocksByDefault` |
| 14 | `Activate_BindingPartiallyBound_AllowsByDefault` |
| 15 | `Activate_BindingPartiallyBound_ReviewRequired_WhenOptionEnabled` |

### 10.5 Binding Policy — Reject

| # | Test |
|---|---|
| 16 | `Reject_DoesNotRequireRuntimeBinding` |

### 10.6 Topology Policy

| # | Test |
|---|---|
| 17 | `TopologyError_BlocksByDefault` |
| 18 | `TopologyWarning_ReviewRequired` |

### 10.7 Impact Policy

| # | Test |
|---|---|
| 19 | `ImpactDiagnosticError_BlocksByDefault` |
| 20 | `ImpactDiagnosticWarning_ReviewRequired` |
| 21 | `ImpactCritical_ReviewRequired_ButNotBreaking` |

### 10.8 Compatibility Policy

| # | Test |
|---|---|
| 22 | `CompatibilityBreaking_ReviewRequiredByDefault` |
| 23 | `CompatibilityBreaking_BlocksActivate_WhenOptionEnabled` |
| 24 | `CompatibilitySecuritySensitive_ReviewRequired` |
| 25 | `CompatibilityRisky_ReviewRequired` |
| 26 | `CompatibilityUnsupported_ReviewRequired_NotBreaking` |
| 27 | `CompatibilityCompatible_Allows` |
| 28 | `CompatibilityDiagnosticError_BlocksByDefault` |

### 10.9 Consistency & Edge Cases

| # | Test |
|---|---|
| 29 | `CompatibilityChangeSetMismatch_BlocksOrPackageFinding` |
| 30 | `Activate_WithCleanReports_Allows` |
| 31 | `Deprecate_WithAffectedConsumers_ReviewRequired` |
| 32 | `Retire_WithBreakingCompatibility_ReviewRequired` |
| 33 | `EmptyTransitions_ReturnsAllowedReport` |
| 34 | `DecisionOrdering_BlockedBeatsReviewRequired` |
| 35 | `Report_IsAllowed_RequiresReview_IsBlocked_AreConsistent` |
| 36 | `DoesNotMutateDescriptorsOrReports` |
| 37 | `ChangeDrivenTransition_SubjectNotInChangeSet_ProducesPackageFinding` |
| 38 | `ValidateDraft_SubjectNotInChangeSet_Allowed` |
| 39 | `Reject_SubjectNotInChangeSet_Allowed` |
| 40 | `BindingIdUnresolvable_ProducesPackageFinding` |
| 41 | `BindingKindMismatch_ProducesPackageFinding` |
| 42 | `BindingVersionAmbiguity_ProducesPackageFinding` |

### 10.10 DI

| # | Test |
|---|---|
| 43 | `DI_RegistersLifecycleGovernanceService` |

---

## 11. Completion Criteria

Phase 6e is complete when:

1. Governance service can evaluate lifecycle transitions from existing reports.
2. Default policy maps validation, binding, topology, impact, and compatibility signals deterministically.
3. `SubmitForReview` is less strict than `Activate` by default.
4. Compatibility `Unsupported` remains review-required knowledge gap, not breaking.
5. Binding `Unsupported` is blocked by default for activation because runtime cannot support it.
6. Breaking compatibility defaults to `ReviewRequired`, not automatic `Blocked`.
7. No analyzer from previous phases is recomputed inside 6e.
8. No registry, descriptor, or runtime state is mutated.
9. DI registration exists.
10. Metadata tests cover the operation-specific policy matrix (43 tests minimum).
11. Full build has zero errors.

---

## 12. One-Line Boundary

Phase 6e decides whether a descriptor lifecycle transition is Allowed, ReviewRequired, or Blocked based on existing analysis reports; it does not validate, bind, build topology, traverse impact, analyze compatibility, persist approvals, mutate descriptor state, or publish runtime changes.

---

*This spec incorporates all decisions from Issue #10 comment thread, including the revised `SubmitForReview` policy and the `Unsupported` semantics distinction (binding vs compatibility). Revised to clarify binding report matching rules (§7.3), expand change-driven operations to include `SubmitForReview`/`Approve` (§7.2), and remove "if supplied" ambiguity for `ValidateDraft` (§6.1). Phase 6f (Package/Manifest) is the natural next phase.*
