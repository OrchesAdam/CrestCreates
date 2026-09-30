# Issue #110 — Agent Tool Exposure Activation Probe Evidence Record

**Date**: 2026-09-30
**Probe fixture**: `AgentToolExposureActivationProbeFixture`
**Location**: `tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/AgentToolExposureActivationProbeFixture.cs`
**Test framework**: xUnit + FluentAssertions
**Total tests**: 8 acceptance tests, all passing
**Full test suite**: 408 tests pass (400 pre-existing + 8 new), zero regressions

---

## 1. Current Production Path Under Test

```
AgentToolRuntimeSnapshotProvider
    -> AgentToolRuntimeSnapshot (FrozenDictionary<string, AgentToolRuntimeEntry>)
        -> AgentToolCatalog (IAgentToolCatalog)
            -> ListAsync(): filters by visibility (role overlap + selection policy)
            -> Orders by ToolName (StringComparer.Ordinal)
            -> Returns IReadOnlyList<AgentToolDiscoveryContract>
                -> Provider Adapter (not yet implemented in production)
```

The probe tests the `IAgentToolCatalog.ListAsync()` output boundary and a modeled provider adapter test double. No production provider adapter exists yet; the test double (`ConstrainedProviderAdapter`) models only `MaxToolCount = N`.

**Key source files**:
- `src/Runtime/Agent/CrestCreates.Agent.Tools/Discovery/AgentToolCatalog.cs` — catalog implementation
- `src/Runtime/Agent/CrestCreates.Agent.Tools/Snapshot/AgentToolRuntimeSnapshot.cs` — snapshot record
- `src/Runtime/Agent/CrestCreates.Agent.Tools/Snapshot/AgentToolRuntimeSnapshotProvider.cs` — snapshot provider
- `src/Runtime/Agent/CrestCreates.Agent.Tools.Abstractions/Discovery/AgentToolDiscoveryContracts.cs` — contracts

---

## 2. Tool Count Cases and Results

| Case | Authorized | Budget | Catalog Returns | Adapter Result | Overflow? |
|------|-----------|--------|-----------------|----------------|-----------|
| 20/128 | 20 | 128 | 20 | Submitted=20 | No |
| 128/128 | 128 | 128 | 128 | Submitted=128 | No |
| 129/128 | 129 | 128 | 129 | Submitted=0 | Yes — `ToolCountExceedsBudget` |
| 20/15 | 20 | 15 | 20 | Submitted=0 | Yes — `ToolCountExceedsBudget` |
| 20/64 | 20 | 64 | 20 | Submitted=20 | No |
| 20/256 | 20 | 256 | 20 | Submitted=20 | No |

---

## 3. IAgentToolCatalog Returns the Full Authorized Set

**Confirmed.** `AgentToolCatalog.ListAsync()` returns all entries whose `AllowedAgentRoles` overlap with the execution context's `AgentRoles` and whose `SelectionPolicy` is compatible with the `CallOrigin`. No count-based truncation, clipping, or budget filtering exists in the catalog.

Evidence: In the 129/128 case, the catalog returned all 129 authorized tools without any reduction.

---

## 4. Where the Above-Limit Case Fails

The failure occurs at the **Provider Adapter boundary**, not in the catalog. The catalog faithfully returns the full authorized set. The modeled adapter (`ConstrainedProviderAdapter`) detects `authorizedCount > MaxToolCount` and reports overflow with:
- `IsOverflow = true`
- `SubmittedCount = 0` (no partial submission)
- `FailureKind = "ToolCountExceedsBudget"`
- `DroppedToolIds = []` (no individual tool dropping)

The current production codebase has **no provider adapter** between the catalog and the model request. This means the overflow case is currently unhandled at the framework level — it would be the responsibility of each provider-specific adapter implementation.

---

## 5. No Existing Code Truncates or Reorders Tools

**Confirmed.** The catalog's `ListAsync()`:
- Filters by visibility (role overlap + selection policy) — this is authorization-based, not count-based
- Orders by `ToolName` using `StringComparer.Ordinal` — deterministic, not registration-order-dependent
- Returns all matching entries via `.ToArray()` — no `Take(N)` or equivalent

No `Take(N)`, registration-order clipping, or unstable selection exists in the catalog path.

---

## 6. Provider Adapter Solvability

The overflow case **can be solved entirely inside a provider adapter** without losing CrestCreates semantics, provided each adapter:
1. Receives the full authorized set from the catalog
2. Checks `authorizedCount <= providerBudget`
3. If within budget: exposes all tools deterministically
4. If over budget: reports explicit overflow/failure (no silent truncation)

However, this creates a **duplication risk**: every provider adapter must independently implement the same overflow check. If 5 provider adapters exist, 5 implementations of the same boundary check are needed.

---

## 7. Framework-Owned Exposure Contract Required?

**Not yet required.** The probe demonstrates that:
- The catalog correctly returns the full authorized set
- The overflow boundary is cleanly representable as an adapter-local concern
- No existing production code silently truncates or loses tools
- The failure mode (explicit overflow) does not require a new framework contract

The duplication risk (point 6) is a maintainability concern, not a correctness gap. A framework-owned `ToolExposureBoundary` contract could reduce duplication but is not required for correctness.

---

## 8. Proposed Semantic Owner (if Activated)

If activation is recommended, the semantic owner would be:
- **Provider-aware Exposure Boundary**: a narrow contract between `IAgentToolCatalog` output and provider-specific adapter input
- Location: `CrestCreates.Agent.Tools.Abstractions` (alongside existing discovery contracts)
- Responsibility: validate `authorizedCount <= budget`, report overflow, preserve ordering

This is **not** a ranking, selection, or discovery service. It is a boundary check only.

---

## 9. Pre-Implementation Failing Acceptance Case

If activated, the following acceptance test should fail before implementation and pass after:

```
AuthorizedTools_AboveProviderLimit_Should_Be_Handled_By_Framework
```

Given:
- 129 authorized tools
- Provider budget = 128
- A production provider adapter is registered

Expected:
- The framework detects overflow before the adapter receives the full set
- An explicit `ToolExposureOverflowException` (or equivalent) is raised
- No partial tool set is sent to the provider
- The authorized set in the catalog remains unchanged

Currently this test would fail because no production provider adapter or framework boundary exists.

---

## 10. Final Disposition

### **Keep #76 Candidate D Deferred**

**Rationale:**
1. The existing `IAgentToolCatalog` path safely returns the full authorized set for all tested tool counts (20, 128, 129).
2. Overflow can remain an adapter-local explicit failure without losing CrestCreates semantics.
3. No repeated/runtime-wide contract is required — the boundary check is simple and provider-specific.
4. The probe demonstrates adapter-level inconvenience (each adapter must check the same boundary), not missing ownership.
5. No production code truncates, reorders, or loses tools at any tested count.
6. The failure mode (overflow) exists only at the unimplemented provider adapter boundary, not in existing production code.

**Activation criteria assessment:**
1. Concrete provider/Harness Tool budget limit — **Not yet present** (no production adapter exists)
2. Authorized Tool set legitimately exceeds that limit — **Theoretically possible** but not demonstrated in production
3. Current supported path cannot safely represent the request — **Incorrect**: the catalog safely returns the full set
4. Arbitrary truncation is not acceptable — **Confirmed**, but no truncation exists
5. Adapter-local handling is insufficient — **Not demonstrated**: the check is simple and reproducible
6. A bounded framework owner can be named — Yes, but premature
7. A pre-implementation failing acceptance test exists — Yes, but against a non-existent production path

Criteria 1, 3, and 5 are not met. Candidate D remains Deferred.

---

## Appendix: Test Execution Evidence

```
Test Run: 2026-09-30T08:30:00Z
Framework: xUnit 3.1.5+1b188a7b0a (.NET 10.0.12)
Total probe tests: 8
Passed: 8
Failed: 0
Duration: 0.9584 seconds

Full suite regression:
Total tests: 408
Passed: 408
Failed: 0
Duration: 1.1354 seconds
```

### Individual Test Results

| Test | Result | Duration |
|------|--------|----------|
| AuthorizedTools_BelowProviderLimit_Should_Be_Consumable | Passed | < 1 ms |
| AuthorizedTools_AtProviderLimit_Should_Be_Consumable | Passed | 30 ms |
| AuthorizedTools_AboveProviderLimit_Should_Expose_CurrentFailure | Passed | 1 ms |
| ProviderLimit_Should_Not_Change_AuthorizedToolSet | Passed | < 1 ms |
| ProviderAdapter_Should_Not_Arbitrarily_Truncate_AuthorizedTools | Passed | 1 ms |
| RegistrationOrder_Should_Not_Change_ExposureProbeOutcome | Passed | 9 ms |
| UnauthorizedTool_Should_Never_Become_ModelVisible | Passed | 9 ms |
| ModelVisibleTool_Should_Still_Require_ExecutionTimeGovernance | Passed | 8 ms |

### Invariant Compliance

| # | Invariant | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Authority != Exposure | Verified | Catalog returns authorized set; adapter models exposure boundary separately |
| 2 | Exposure may only narrow authority | Verified | Adapter SubmittedCount <= AuthorizedCount in all cases |
| 3 | Provider limits do not change authority | Verified | ProviderLimit test: same catalog output for budgets 64, 128, 256 |
| 4 | Exposure is not execution authorization | Verified | ModelVisibleTool test: governance metadata present on all visible tools |
| 5 | No arbitrary truncation | Verified | ProviderAdapter test: overflow returns SubmittedCount=0, no dropped tools |
| 6 | Deterministic ordering | Verified | RegistrationOrder test: shuffled registration produces identical fingerprint |
| 7 | Catalog remains authorization owner | Verified | Catalog returns full authorized set; no budget filtering |
| 8 | Failing probe != automatic abstraction | Applied | Disposition: Keep Deferred |
| 9 | No General Agent Runtime | Verified | No changes to Agent Runtime |
| 10 | NativeAOT constraint | Verified | No production serialization contracts added |
