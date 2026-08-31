# Phase 9d — H3 Reuse Observation Sidecar

> Reused checks admitted from #68 H2 for Issue #26. Records value, misses,
> noise, runtime cost, maintenance cost, context, defects caught, and a
> verdict for each reused check. Product acceptance remains owned by the
> Phase 9d Spec cases; this sidecar judges whether the reused checks were
> worth running.

## Check 1 — PostgreSQL NativeAOT Publish-Link-Run Fixture

| Field | Observation |
|---|---|
| Check | PostgreSqlRuntimeAotFixtureTests.DurableControlPlaneReferenceDataAotFixture_Should_PublishLinkAndRun |
| Value | Proves the modified first-party PostgreSQL Runtime/Organization mainline publishes, links, and executes natively. The new `CRESTCREATES_VERSIONED_ORGANIZATION_CACHE_OK` marker is asserted in the native output, proving V013 + two independent cache owners execute in the original binary. |
| Misses | Cannot express per-case semantic assertions (e.g. "ObservedHighWater advances before load"). It is a single end-to-end sentinel, not a semantic oracle. Regression in quarantine semantics would not fail this check unless it crashed the native host. |
| Noise | Intermittent native publish failures under parallel execution (MSB3248 file-lock) when AOT fixtures share the output directory. First-run native publish is slow (minutes). |
| Runtime Cost | ~5-8 minutes per run (native publish + native execution against real PostgreSQL). Requires linux-x64 + Docker/podman. |
| Maintenance Cost | High: the host Program.cs must be kept AOT-compatible (no untrimmed reflection, source-generated JSON only). New markers must be added to both the host and the fixture assertion list. |
| Context Required | High: interpreting a native link failure requires understanding trim analysis, source-generated JsonSerializerContext, and the AOT host's phased execution. |
| Defects Actually Caught | The AOT01 scenario confirmed the Organization versioned-cache path (CachedOrganizationHierarchyService, OrganizationHierarchyCacheOwner, OrganizationHierarchyFlight) is AOT-compatible and executes natively. |
| Verdict | **Keep** — but only as a final release gate, not a development feedback loop. The cost is high and the semantic resolution is low. |

## Check 2 — Runner-Free Provider Contract Kit

| Field | Observation |
|---|---|
| Check | OrganizationStoreContractCases (shared, runner-free) consumed through InMemory + PostgreSQL drivers |
| Value | One semantic case (e.g. `RunOrganizationUnitSaveAdvancesGenerationAsync`) asserts behavior through both providers with a single static method. New generation cases (OVG01-08, OVG12) were added and run green on both InMemory and PostgreSQL. |
| Misses | Does not prove concurrency safety at the provider level (that lives in provider-specific suites). Does not prove AOT compatibility. |
| Noise | Low: deterministic assertions, no timing. |
| Runtime Cost | Low: sub-second per case on InMemory; seconds on PostgreSQL (real container). |
| Maintenance Cost | Low: adding a shared case is one static method + one Record call per runner. Provider wrappers own setup/cleanup. |
| Context Required | Low: the pattern is mechanical (shared case + driver wrapper). |
| Defects Actually Caught | Caught the initial SQL-format bug in the PostgreSQL generation read (`{0}.organization_scope_generations` doubled the table name). |
| Verdict | **Keep** — high value, low cost. This is the primary semantic oracle for provider parity. |

## Check 3 — Dependency Boundary Tests

| Field | Observation |
|---|---|
| Check | CrestCreates.DependencyBoundaries.Tests (assembly-reference + DI-composition assertions) |
| Value | Locks the unique Organization and Permission mainlines. The new VersionedCacheConsistencyArchitectureTests verify: no Runtime/Caching/Data references in Organization, PermissionGrantStore has no cache dependency, retired services are absent. |
| Misses | Does not prove runtime behavior (only structural/compositional invariants). Does not catch semantic regressions in cache state-machine transitions. |
| Noise | Low: deterministic assembly-ref and DI-descriptor assertions. |
| Runtime Cost | Low: sub-second. |
| Maintenance Cost | Medium: each new assembly reference or DI registration must be audited against the boundary assertions. |
| Context Required | Medium: requires understanding the intended dependency direction to write a correct assertion. |
| Defects Actually Caught | Caught an early draft where Organization referenced `CrestCreates.Caching` instead of `Microsoft.Extensions.Caching.Memory`. |
| Verdict | **Keep** — structural locks are cheap insurance against accidental mainline duplication. |

## Overall H3 Verdict

Reusable: yes. Useful: **runner-free kit yes, boundary tests yes, NativeAOT fixture review-only**.

The runner-free provider kit is the highest-value check: it directly
expresses the Phase 9d semantic contract and runs cheaply on every
commit. The boundary tests are cheap structural insurance. The NativeAOT
fixture is necessary but too slow and too coarse to be a development
feedback loop — it belongs in release gating, not in per-commit CI.

The incomplete Phase 9c 444-tuple exact-set ledger remains **outside** this
reuse judgment and is not promoted.
