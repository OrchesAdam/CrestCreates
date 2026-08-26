# Phase 9d — Versioned Cache Consistency Implementation Plan

> Implement Issue #26 through ordered, Case-first TDD slices. The approved R4
> Spec is normative. This Plan freezes project placement, the Organization
> generation contract, V013 schema and transaction mechanics, the local
> hierarchy safety-state machine, bounded ownership, single-flight lifetime,
> Permission cache retirement, real multi-instance evidence, NativeAOT closure,
> and the #68 H3 observation sidecar. It does not reopen the frozen design.

**Goal:** Make Organization hierarchy caching correct without shared cache or
reliable invalidation by validating every cacheable read against an atomic
authority generation, and make Permission authorization safe by reading its EF
authority directly on every check.

**Spec:**
`docs/superpowers/specs/2026-08-25-phase-9d-versioned-cache-consistency-design.md`

**Issue:** #26

**Branch:** `codex/issue-26-versioned-cache-consistency`

**Frozen Spec commit:** `fc594306`

**Current-master baseline inspected by the Spec:** `81a42edc`

**Migration baseline:** V012 is the current checksummed catalog tail.

**Spec status:** R4 APPROVED / FROZEN

**Plan status:** READY FOR IMPLEMENTATION

```text
Organization authority:      IOrganizationStore owns explicit scope generation
InMemory parity:             atomic per-scope data + generation publication
PostgreSQL durability:       V013 + entity/generation in one transaction
Hierarchy accelerator:       generation-validated immutable local snapshot
Freshness defense:           monotonic high-water + sticky quarantine
Ordinary cache failure:      direct load only through final safety-state gate
Permission authorization:    direct EF authority, no positive grant cache
Multi-instance proof:        independent providers/caches, shared authority
NativeAOT:                   extend PostgreSQL AOT Host/Fixture
Harness reuse:               H3 observation sidecar, no generic Harness wrapper
```

---

## 1. Execution Rules

### 1.1 Session preflight

Before every Slice:

```bash
git status --short --branch
git rev-parse HEAD
git rev-parse master
dotnet --info
```

Read the frozen Spec, this Plan, the previous Slice handoff, and the current
diff. Re-run the targeted `rg` inventory for the Slice before editing because
Issue #26 touches shared migration, Organization, Authorization, and AOT
hotspots.

Stop and reconcile before implementation if any of these facts changed:

- V013 already exists or V012 is no longer the migration tail;
- `IOrganizationStore` has acquired a competing freshness/version contract;
- `DefaultOrganizationHierarchyService` has another production registration;
- Permission has acquired one durable authority version advanced by every legal
  writer;
- the EF Permission authority or PostgreSQL transaction coordinator changed;
- a touched file contains unrelated uncommitted work that cannot be preserved.

Never edit or renumber V001-V012. Never delete retired files; move them under
`99_RecycleBin/issue-26/` so their removal remains reviewable.

### 1.2 Case-first TDD discipline

- Activate only the acceptance IDs owned by the current Slice.
- A Red must fail because the required behavior is absent, not because fixture
  setup, DI, migration bootstrapping, or test data is invalid.
- Shared provider cases remain runner-free static methods. Provider wrappers own
  setup, cleanup, failure injection, and provider-only evidence.
- Concurrency cases use barriers/task completion sources, never timing sleeps.
- Make the smallest mainline change that turns the focused Red Green.
- Run the changed project build, focused tests, shared contract runners,
  applicable boundary tests, and `git diff --check`.
- End each Slice with one reviewable commit and a handoff containing: commit,
  changed files, Red evidence, Green commands/counts, acceptance IDs closed,
  internal hooks added, and unresolved findings.
- Do not update `memory.md` to implemented until Slice 10 has product evidence.
- Do not claim NativeAOT verification until the published native executable
  completes Slice 9.

### 1.3 Non-negotiable boundaries

- Do not add a generic cache framework, distributed lock, Redis correctness
  dependency, Outbox producer, Permission generation, or data-permission cache.
- Do not cache the null-tenant Organization collection query.
- Do not cache Organization identity, Membership, Role Assignment, Descriptor
  snapshot, Data Permission Rule, or derived `DataPermissionScope` in this phase.
- Do not read Organization data before its generation and stamp it afterward.
- Do not downgrade cancellation, schema drift, corrupt persistence, malformed
  typed state, or unknown enum values to `Unavailable`.
- Do not return the old hierarchy snapshot after generation mismatch, authority
  failure, regression, quarantine, or ordinary cache infrastructure failure.
- Do not allow snapshot eviction/capacity pressure to erase high-water or
  quarantine safety state.
- Do not make a caller cancellation token the lifetime owner of a shared load.
- Do not keep failed/canceled single-flight tasks.
- Do not expose cache DTOs, Npgsql handles, SQLSTATE, test hooks, or safety-state
  internals as public framework APIs.
- Do not keep cached and uncached hierarchy services as two production DI paths.
- Do not let Permission tests authorize removing unrelated caching consumers.
  `AddCrestCaching`, `TenantCacheKeyContributor`, `AuditTenantContextResolver`,
  and any other proven consumer stay unless separately reviewed.
- Do not infer EF Permission NativeAOT support from the PostgreSQL Runtime AOT
  fixture.

### 1.4 Commit order

```text
Slice 1  typed generation contract + runner-free semantic cases
    -> Slice 2  InMemory atomic scope state
    -> Slice 3  PostgreSQL V013 + transactional generation
    -> Slice 4  hierarchy snapshot/safety owner + deterministic unit cases
    -> Slice 5  Organization DI cutover + real PostgreSQL multi-instance cases
    -> Slice 6  Permission direct-authority cutover + unit/composition cases
    -> Slice 7  real EF Permission multi-instance security cases
    -> Slice 8  boundary and regression closure
    -> Slice 9  PostgreSQL NativeAOT publish-link-run evidence
    -> Slice 10 H3 sidecar + product closure review
```

A later Slice cannot begin with an activated Red, an unreviewed shared-hotspot
change, or a missing handoff from the preceding Slice.

---

## 2. Locked Implementation Decisions

### 2.1 Public Organization generation contract

Add these public types to
`src/Framework/Modules/CrestCreates.Organization.Abstractions/`:

```text
OrganizationScopeKind.cs
OrganizationScopeIdentity.cs
OrganizationScopeGenerationStatus.cs
OrganizationScopeGenerationRead.cs
OrganizationHierarchyFreshnessFailureKind.cs
OrganizationHierarchyFreshnessException.cs
```

Extend `IOrganizationStore` with exactly one authority method:

```csharp
Task<OrganizationScopeGenerationRead> ReadScopeGenerationAsync(
    OrganizationScopeIdentity scope,
    CancellationToken cancellationToken = default);
```

Contract decisions:

- `OrganizationScopeIdentity.Global` and `.Tenant(string)` are the only valid
  construction paths; default/Unknown/malformed values fail before provider I/O.
- Explicit global generation is not the nullable collection-query convention.
- `Available(long)` accepts only non-negative values.
- factory `Unavailable` has canonical `Generation == 0`.
- default/Unknown/undefined/malformed results are invalid, never availability.
- generation is a read-set freshness stamp, not an ETag or expected version.
- all four successful Save surfaces advance one shared scope generation exactly
  once, including identical blind replacement.
- absence is generation 0; overflow fails the complete Save.

`OrganizationHierarchyFreshnessException` carries the Spec's provider-neutral
failure kind and generation evidence. Failure to admit/read retained local
safety state is an explicit `OrganizationException` infrastructure failure; it
must not masquerade as provider `Unavailable` or authorize data fallback. Do
not widen the frozen public enum solely to model an internal capacity failure.

### 2.2 InMemory authority publication

Replace the four independently mutable dictionaries in
`InMemoryOrganizationStore` with one atomically published state per normalized
`OrganizationScopeIdentity`:

```text
OrganizationScopeState
    Generation
    OrganizationUnits
    Positions
    Memberships
    RoleAssignments
```

Each scope owns a mutation guard. A Save performs:

```text
validate + capture detached snapshot
    -> normalize explicit scope
    -> acquire only that scope's guard
    -> checked(current.Generation + 1)
    -> build complete next immutable scope state
    -> publish one state reference
    -> release guard
```

Explicit-scope entity reads and generation reads observe one published state.
Unfiltered collection reads aggregate independently published states and retain
their existing no-cross-scope-transaction semantics. Preserve canonical order,
detached snapshots, blind writes, validation, and cancellation behavior.

### 2.3 PostgreSQL V013 and provider failure mapping

Append migration `V013_organization_scope_generation` to the existing frozen
catalog. The exact table is:

```text
organization_scope_generations

tenant_scope_kind    text COLLATE "C" NOT NULL
tenant_id            text COLLATE "C" NOT NULL
generation           bigint NOT NULL
updated_at            timestamptz NOT NULL
PRIMARY KEY (tenant_scope_kind, tenant_id)
CHECK normalized global/tenant identity
CHECK generation >= 1
```

No foreign keys or extra indexes are added. Absence is generation 0; persisted
rows begin at 1.

Update the V013 catalog entry, checksum, required-table inventory, and exact
`RuntimeSchemaManifest` column/collation/PK/check/no-index/no-FK expectations.
Never modify V001-V012 text or checksums.

Every Organization Save remains one `ExecuteTopLevelAsync` operation:

```text
entity snapshot upsert
    -> provider test write point
    -> INSERT generation 1, or UPDATE generation = generation + 1
    -> COMMIT both
```

Use the same normalized `(tenant_scope_kind, tenant_id)` for entity and
generation. PostgreSQL checked bigint overflow aborts the transaction; do not
catch and wrap it into success or retry.

`ReadScopeGenerationAsync` is one indexed point read. Its mapping is exhaustive:

| Provider observation | Public result |
|---|---|
| row absent | `Available(0)` |
| valid non-negative row | `Available(G)` |
| existing typed provider availability failure | `Unavailable` |
| caller cancellation | propagate cancellation |
| undefined table/column, datatype/schema drift | existing persisted-contract failure |
| negative/corrupt value | existing persisted-invariant failure |
| programming/unknown exception | propagate; never convert to availability |

Use a generation-specific internal read helper if the existing broad
`ExecuteReadAsync` Npgsql mapping would classify schema drift as availability.
Do not change all reference-data read semantics merely to support this method.

For V012-to-V013 testing, add an internal one-shot migration barrier before
V013. It may stop a test runner after V012, but must not become a public target
migration API. Seed V012 Organization rows, release the barrier, migrate to
V013, then prove those rows read generation 0 and the first Save reaches 1.

### 2.4 Hierarchy snapshot representation

Add internal implementation types under
`src/Framework/Modules/CrestCreates.Organization/Hierarchy/` (or the existing
project namespace-equivalent folder):

```text
OrganizationHierarchySnapshot.cs
OrganizationHierarchyCacheKey.cs
OrganizationHierarchyCacheOptions.cs
IOrganizationHierarchySnapshotCache.cs
MemoryOrganizationHierarchySnapshotCache.cs
OrganizationHierarchyCacheOwner.cs
```

The exact internal snapshot contains:

```text
Generation
canonical detached units
typed scoped-unit-key -> unit
typed scoped-parent-key -> ordered child keys
```

Use immutable/frozen maps and immutable ordered child collections. Building the
snapshot must not eagerly reject missing parents or cycles; traversal preserves
the existing lazy failure/stop semantics. Every returned entity is detached via
the existing `Snapshot()` path.

The service preserves:

- ancestors in parent-to-root order;
- deterministic breadth-first descendants;
- queried unit excluded from descendants;
- `IsDescendantOfAsync(x, x) == false`;
- missing parent stops the requested traversal;
- cycle raises the existing `OrganizationHierarchyException` when traversed;
- membership lookups remain direct authority reads.

### 2.5 Bounded owner: payload and safety are separate

The production owner is one singleton per `AddOrganizationKernel` service
provider. It has two separate bounded structures:

1. **Snapshot payload cache** — `IMemoryCache`, size limit 1,024 entries,
   size 1 per tenant snapshot, 15-minute sliding expiration. Eviction and
   expiration are resource policy only and never establish correctness.
2. **Freshness safety registry** — maximum 16,384 explicit tenant scopes per
   owner, admitted under a small owner-level lock and then retained until the
   service provider is disposed. Entries never evict, decrease high-water, or
   clear quarantine due to pressure. When the bound is full, a previously unseen
   scope fails closed before generation/cache/data I/O; existing scopes continue.
3. **Active-flight registry** — maximum 2,048 owner-wide active flights in
   addition to the per-scope/generation keying. Capacity is released on every
   success, failure, timeout, and owner disposal. Saturation fails that attempted
   load explicitly; it does not create an uncoordinated load or authorize stale
   or direct fallback.

The safety registry's process-lifetime retention is deliberate. It is the only
simple bounded mechanism that cannot turn capacity pressure into forgotten
freshness knowledge. Process restart is the only non-recovery reset allowed by
the Spec.

Each admitted scope state owns one synchronization gate and these semantics:

```text
Mode                 NORMAL | QUARANTINED
ObservedHighWater    absent or highest valid Available(G) observed
QuarantineFloor      absent, or high-water captured on first regression
Revision             monotonic local transition counter
Flights              generation -> shared load ownership
```

Ordinary snapshot-cache failures are distinct from safety-registry failures.
Only the former may take a direct request-local data path. If the safety entry
cannot be admitted/read/retained, fail explicitly with no cache or data fallback.

Add `Microsoft.Extensions.Caching.Memory` only to the Organization
implementation project. Do not reference `CrestCreates.Caching`, Redis, Runtime,
or a persistence implementation from Organization.

### 2.6 State-machine operations and linearization

Do not scatter high-water/quarantine checks through the service. The owner
provides narrow internal operations, named to match intent rather than these
exact signatures:

```text
AdmitScope
ApplyGenerationOutcome
TryReadSnapshot
JoinOrCreateFlight
CompleteGenerationStampedResult
CompleteUnavailableFallback
CompleteCacheFailureFallback
```

`ApplyGenerationOutcome` returns an immutable admission token carrying the
scope-state identity, admission revision/mode, admitted high-water/floor, the
generation when present, and the permitted next path. Callers cannot synthesize
the token.

Rules under the per-scope gate:

- first valid `Available(G)` sets high-water before cache equality/load;
- higher valid G advances high-water before load and never rolls back;
- NORMAL observation below high-water sets floor to high-water and quarantines;
- QUARANTINED never falls back on `Unavailable`;
- recovery requires G above floor and equal to current high-water;
- a failed recovery retains mode, floor, and high-water;
- successful eligible publication releases quarantine and retains high-water;
- a generation-stamped result may logically complete only when its generation
  equals current high-water and, if quarantined, is above the floor;
- an `Unavailable` fallback completes only while still NORMAL and with exactly
  the admission-time high-water, including the absent state;
- only the operation that wins the final gate establishes that caller's
  linearization point.

Snapshot publication is generation-monotonic. A lower/equal delayed candidate
cannot replace a newer snapshot. Equal generation reuse is permitted only when
the scope is NORMAL; an eligible recovery candidate must pass the recovery gate.

### 2.7 Mandatory implementation-review checkpoint: OHC09

This checkpoint is normative for implementation and review even though it does
not modify the frozen Spec.

The following shape is forbidden for ordinary snapshot-cache infrastructure
failure:

```text
cache throws
    -> Store.GetOrganizationUnitsAsync(...)
    -> return
```

Both snapshot lookup failure and snapshot publication failure must use:

```text
capture admitted safety-state token
    -> direct/request-local authority load
    -> build result stamped with the admitted generation
    -> re-enter the same per-scope safety owner
    -> apply §11 Step 12-15 final caller-completion gate
    -> return only if current state still admits that result
```

For an `Available(G)` path, final completion requires `G == current
ObservedHighWater` and the quarantine rule. For a typed `Unavailable` path,
completion requires NORMAL plus the exact admission-time high-water. Cache
failure does not weaken either rule.

OHC09 is not Green merely because direct authority data was returned. Its test
must deterministically pause the fallback load, advance high-water or enter
quarantine on a second request, release the load, and prove the first caller is
rejected by the final gate. Reviewers must search every catch/fallback branch
and verify they converge on the same completion operation.

### 2.8 Single-flight lifetime and cancellation

Flights are local and keyed by `(explicit TenantId, observed Generation)`.
Each flight owns:

```text
owner-created CancellationTokenSource
30-second owner timeout
Task<OrganizationHierarchySnapshotCandidate>
exact-key removal continuation/finalizer
```

The shared task receives only the owner token. Every waiter awaits with
`flight.Task.WaitAsync(callerCancellationToken)`, so one caller cancellation
detaches that waiter without canceling or poisoning the shared load. Completion,
fault, owner-timeout cancellation, and owner disposal remove the exact flight
and dispose its CTS. Failed/canceled tasks are never retained. A later request
at the same generation may create a new flight.

The flight only loads and builds candidate(G). It does not decide publication
or caller completion. Every waiter passes the candidate through the current
safety-state completion gate. Different tenants/generations never share work.

### 2.9 Hierarchy production composition

`AddOrganizationKernel` registers exactly one singleton cache owner and one
scoped `IOrganizationHierarchyService`. Production hierarchy resolution shares
the singleton owner across scopes in that service provider.

Preserve the public `DefaultOrganizationHierarchyService(IOrganizationStore)`
constructor for source compatibility and direct semantic tests. It creates or
uses a private non-shared owner only for that explicitly constructed service;
production DI must use an internal constructor/factory with the registered
singleton owner. Add a composition test proving only one production hierarchy
registration exists.

For `tenantId == null`, bypass generation, safety registry, snapshot cache, and
single-flight. Load the current unfiltered collection once, build a request-local
snapshot, traverse, and discard it.

### 2.10 Permission direct-authority cutover

The production read chain becomes:

```text
PermissionChecker
    -> PermissionGrantManager
    -> PermissionGrantStore
    -> IPermissionGrantRepository on every check
```

`PermissionGrantStore` maps current repository results and applies existing
tenant/global filtering without consulting `PermissionGrantCacheService`.
Repository failure propagates; there is no old-positive fallback.

Remove Permission-cache dependencies from `PermissionGrantManager`. Grant and
Revoke mutate the repository and do not invalidate a Permission cache. Preserve
existing idempotency, tenant/global semantics, and SuperAdmin behavior.

Remove only Permission cache DI/options registrations. Move these retired files
to `99_RecycleBin/issue-26/authorization/`:

```text
PermissionGrantCacheService.cs
PermissionGrantCacheOptions.cs
```

Do not remove `AddCrestCaching`, its project reference, cache-key contributors,
tenant invalidators, or audit resolution while unrelated consumers remain.

### 2.11 Test-only hooks

All deterministic hooks are `internal`, narrow, resettable, and friend-visible
only to their owning tests. Required seams:

- InMemory/store driver: block after generation observation or authority read;
- hierarchy fault driver: control generation outcome, data load, snapshot-cache
  read/write, safety-state admission, and final completion barriers separately;
- PostgreSQL: fail after entity upsert before generation increment, exercise
  known rollback, and reuse existing commit-unknown coordination;
- migration: one-shot block immediately before V013;
- AOT: no test hook or reflection dependency in the published binary.

Do not add public clock, cache, state-machine, migration-target, or provider fault
APIs for tests.

---

## 3. Acceptance Ownership Map

| Slice | Acceptance IDs | Primary runner |
|---|---|---|
| 1 | OVG01-OVG05, OVG07-OVG08, OVG12 contract shape | runner-free Organization Store kit |
| 2 | OVG01-OVG08, OVG12 | InMemory wrapper |
| 3 | OVG01-OVG12 | PostgreSQL wrapper + migration/failure suites |
| 4 | OHC01-OHC24 except real multi-instance | Organization deterministic unit/fault driver |
| 5 | OMI01-OMI02 + PostgreSQL-backed OHC01/OHC02/OHC12 | real PostgreSQL, independent providers |
| 6 | PSC01-PSC02, PSC04-PSC06, PSC08 unit/composition | Application Authorization tests |
| 7 | PSC01-PSC05, PSC07 | real EF integration topology |
| 8 | architecture invariants + all semantic regressions | boundary and existing suites |
| 9 | AOT01 | PostgreSQL AOT Host/Fixture |
| 10 | H301 + all exit criteria | product review + H3 sidecar |

The manifest is an ownership map, not permission to defer a failing earlier
case. If an earlier implementation activates a later invariant, make it Green
before committing or move that work into the current Slice explicitly.

---

## 4. File Map

### 4.1 Organization abstractions and implementation

Modify:

```text
src/Framework/Modules/CrestCreates.Organization.Abstractions/IOrganizationStore.cs
src/Framework/Modules/CrestCreates.Organization.Abstractions/CrestCreates.Organization.Abstractions.csproj
src/Framework/Modules/CrestCreates.Organization/InMemoryOrganizationStore.cs
src/Framework/Modules/CrestCreates.Organization/DefaultOrganizationHierarchyService.cs
src/Framework/Modules/CrestCreates.Organization/OrganizationServiceCollectionExtensions.cs
src/Framework/Modules/CrestCreates.Organization/CrestCreates.Organization.csproj
```

Add the six public contract files from §2.1 and the internal hierarchy files
from §2.4. Keep `OrganizationScopedKey` and current semantic helpers as the
canonical key/validation source; do not invent a parallel string protocol.

### 4.2 PostgreSQL authority and schema

Expected hotspots:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlOrganizationStore.cs
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlRuntimeMigrationRunner.cs
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlControlPlaneReferenceDataStoreSupport.cs
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlRuntimeTestHooks.cs
```

Use `rg` to locate the exact migration catalog, required-table list, schema
manifest (currently nested inside `PostgreSqlRuntimeMigrationRunner`), and every
test asserting catalog count/tail before editing. V013 must be represented
everywhere the existing V012 contract is represented.

### 4.3 Shared and provider tests

Extend/add:

```text
tests/Shared/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing/Cases/OrganizationStoreContractCases.cs
tests/Shared/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing/Drivers/StoreContractDrivers.cs
tests/Framework/Modules/CrestCreates.Organization.Tests/Persistence/InMemoryOrganizationStoreContractDriver.cs
tests/Framework/Modules/CrestCreates.Organization.Tests/Persistence/OrganizationStoreContractTests.cs
tests/Framework/Modules/CrestCreates.Organization.Tests/Hierarchy/
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/PostgreSqlOrganizationGenerationTests.cs
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/PostgreSqlOrganizationHierarchyCacheTests.cs
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/PostgreSqlPendingEvidenceMigrationTests.cs
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/PostgreSqlAgentMemoryMigrationTests.cs
```

The PostgreSQL wrapper may extend the existing large reference-data store test
class only if doing so preserves discoverability. Prefer focused new classes for
generation, hierarchy multi-instance, and migration upgrade evidence.

### 4.4 Permission cutover

Modify:

```text
src/Framework/Infrastructure/CrestCreates.Authorization/PermissionGrantStore.cs
src/Framework/Infrastructure/CrestCreates.Authorization/PermissionGrantManager.cs
src/Framework/Infrastructure/CrestCreates.Authorization/AuthorizationServiceCollectionExtensions.cs
tests/Framework/Ddd/CrestCreates.Application.Tests/Permissions/PermissionGrantManagerTests.cs
tests/Framework/Ddd/CrestCreates.Application.Tests/Permissions/PermissionCheckerTests.cs
```

Add focused authority/composition tests under the same Permission test folder.
Move, do not delete, the retired Permission cache files listed in §2.10.

Add real provider tests under:

```text
tests/Framework/Web/CrestCreates.IntegrationTests/Permissions/PermissionCacheConsistencyIntegrationTests.cs
```

### 4.5 Boundary, AOT, and review artifacts

Modify/add:

```text
tests/Boundary/CrestCreates.DependencyBoundaries.Tests/VersionedCacheConsistencyArchitectureTests.cs
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/Program.cs
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests/PostgreSqlRuntimeAotFixtureTests.cs
docs/review/phase-9d-h3-reuse-observations.md
memory.md
```

`memory.md` is a closure artifact only. Reconcile existing user changes; never
replace the file wholesale.

---

## 5. Slice 1 — Typed Generation Contract and Shared Cases

**Purpose:** Make provider completeness compiler-visible and define one
runner-free semantic oracle before either provider implementation changes.

### 5.1 Red

Add contract construction tests and shared case methods for:

- initial explicit Global and Tenant generation is 0;
- every Save surface advances the same scope generation once;
- identical blind Save advances again;
- tenant A cannot advance tenant B or Global;
- failed validation/cancellation before work advances neither data nor version;
- default/Unknown scope and blank tenant are rejected before driver I/O;
- collection `tenantId == null` remains unfiltered and is not a generation scope;
- `Available(-1)` is rejected and `Unavailable` is canonical;
- default/Unknown read outcome remains distinguishable from `Unavailable`.

Extend the driver only with setup/observation capabilities needed by those
static cases. Do not put xUnit attributes or provider branching in shared code.

Expected Red: both InMemory and PostgreSQL fail to compile because
`IOrganizationStore.ReadScopeGenerationAsync` is unimplemented.

### 5.2 Green contract shell

Add the public types and Store method. Give providers temporary explicit
`NotImplementedException` only inside the uncommitted Red step; the Slice is not
Green and must not commit until the InMemory wrapper has a compilable semantic
path in Slice 2. If repository policy requires every commit Green, combine
Slices 1 and 2 into one commit while preserving separate test-first evidence.

### 5.3 Verification

```bash
dotnet build src/Framework/Modules/CrestCreates.Organization.Abstractions
dotnet build tests/Shared/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests --filter "FullyQualifiedName~OrganizationStoreContractTests"
git diff --check
```

**Checkpoint:** public contract and shared cases reviewed; no cache code, V013,
or Permission change yet.

---

## 6. Slice 2 — InMemory Atomic Scope State

**Purpose:** Turn the shared generation semantics Green with atomic data/version
publication and retain all existing Organization semantics.

### 6.1 Red additions

Add deterministic InMemory-only cases proving:

- a reader blocked around a Save observes either the complete old pair or the
  complete new pair, never one-sided state;
- four concurrent Save surfaces serialize only within one normalized scope;
- unrelated tenants can progress independently;
- checked overflow publishes neither entity nor wrapped generation;
- reads remain detached and canonically ordered.

Use an internal initial-state/test hook for overflow only if no natural route is
available. Never make generation settable in the public Store API.

### 6.2 Implementation

Introduce immutable `OrganizationScopeState` and per-scope owner/guard. Replace
all four mutation dictionaries as one coordinated scope state. Reuse current
normalization and `Snapshot()` logic. Audit every existing read method:

- explicit tenant/global reads select one published state;
- unfiltered queries enumerate current state references and merge deterministically;
- point reads retain current global/tenant identity rules;
- all cancellation checks occur before returning results;
- no returned collection shares mutable Store state.

### 6.3 Verification

```bash
dotnet build src/Framework/Modules/CrestCreates.Organization
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests --filter "FullyQualifiedName~OrganizationStoreContractTests"
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
git diff --check
```

**Checkpoint:** OVG01-OVG08 and OVG12 Green for InMemory; existing hierarchy and
identity semantic suite unchanged.

---

## 7. Slice 3 — PostgreSQL V013 and Transactional Generation

**Purpose:** Provide FullDurable parity and prove generation cannot separate
from the entity replacement transaction.

### 7.1 Catalog and migration Red

Add/extend tests for:

- clean bootstrap ends at V013 with exact checksum and schema;
- frozen V001-V012 checksums remain byte-for-byte unchanged;
- required table/manifest includes exact columns, C collation, PK, checks, no FK,
  and no extra index;
- V012 rows upgrade with no generation row and therefore read 0;
- first post-upgrade Save creates generation 1;
- duplicate/wrong checksum/drift detection still fails closed;
- every catalog-tail/count assertion is updated intentionally, not broadly
  weakened to `>=`.

Implement the internal pre-V013 one-shot barrier for the upgrade topology. Its
reset must run in `finally` so a failed test cannot poison later fixtures.

### 7.2 Provider semantic Red

Run the shared OVG cases through the direct-Npgsql driver. Add provider-only
tests for:

- entity-upsert failure leaves generation unchanged;
- injected failure after entity upsert and before generation increment rolls
  both back;
- generation increment failure/overflow rolls back entity replacement;
- commit acknowledgement unknown yields fresh complete old or complete new pair,
  never one-sided, with no concurrent writer;
- missing row is Available(0);
- malformed scope is rejected before connection/command execution;
- cancellation propagates;
- connectivity maps to typed Unavailable;
- schema drift/undefined table/column and corrupt negative generation propagate
  as existing contract/invariant failures, not Unavailable.

Reuse/add write-point names after each entity upsert:

```text
organization-unit-snapshot-upserted
position-snapshot-upserted
membership-snapshot-upserted
role-assignment-snapshot-upserted
```

### 7.3 Implementation

Append V013 and schema manifest entries. Add one normalized generation helper
used by all four Saves in their existing transaction. Do not open a nested
connection/transaction. Add the indexed read and narrow failure mapper.

The mutation SQL must update `updated_at` on every successful generation
advance. Its value is diagnostic only and never participates in correctness.

### 7.4 Verification

```bash
dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~PostgreSqlOrganizationGenerationTests"
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~Migration"
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~OrganizationStore"
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests --filter "FullyQualifiedName~OrganizationStoreContractTests"
git diff --check
```

**Checkpoint:** OVG01-OVG12 Green for both providers, including rollback,
upgrade, overflow/corruption, and commit-unknown evidence.

---

## 8. Slice 4 — Hierarchy Snapshot and Safety-State Kernel

**Purpose:** Implement the complete local correctness state machine before
connecting it to real multi-instance topology.

### 8.1 Test driver first

Create a provider-neutral hierarchy fault driver that can independently:

- count/block generation and Organization Unit reads;
- produce Available, Unavailable, default/Unknown, cancellation, and invariant
  failure outcomes;
- fail snapshot lookup and publication separately;
- fail safety-state admission/read separately;
- block candidate load and final completion with deterministic barriers;
- create independent owners over one shared fake authority;
- inspect only friend-visible evidence: publication generation, flight count,
  mode/high-water/floor, and load count.

Production public contracts must not widen for the driver.

### 8.2 Core semantic Red: OHC01-OHC07 and OHC12

Prove:

- same-generation calls load once and reuse the immutable snapshot;
- a generation change rejects the old snapshot and reloads;
- null tenant bypasses generation/cache/flight every time;
- same unit IDs in two tenants do not collide;
- delayed G41 cannot replace/return over already observed/published G42;
- same tenant/generation concurrent misses share one authority load;
- different generations own separate flights and newer publication wins;
- ordering, missing parent, cycle timing, self-descendant, and detached results
  remain exact.

### 8.3 Failure and cancellation Red: OHC08-OHC15

Prove:

- typed Unavailable in NORMAL performs one direct load, no cache use/publication,
  and final completion requires the same NORMAL/high-water admission;
- generation mismatch plus load failure never serves the prior snapshot;
- one canceled waiter does not cancel another or retain a poisoned flight;
- unknown/default/invalid generation result performs no data fallback;
- generation cancellation performs no data fallback;
- invariant/schema/contract failure propagates exactly;
- ordinary snapshot lookup/publication failure uses request-local authority data
  only through the final safety-state gate;
- a normal Unavailable fallback authority failure propagates without stale data.

### 8.4 Regression/recovery Red: OHC16-OHC24

Prove the exact state transitions:

- lower G captures current high-water as floor and quarantines without data I/O;
- quarantined Unavailable fails explicitly without data I/O;
- at/below-floor generation cannot recover;
- eligible above-floor candidate publishes, releases quarantine, and retains
  high-water;
- snapshot eviction/capacity pressure cannot forget quarantine;
- unseen scope after safety capacity is full fails closed before provider I/O;
- higher-generation load failure retains high-water and rejects an older next
  observation;
- failed recovery at highest G may retry the same G above floor;
- candidate started before regression cannot publish or return afterward.

### 8.5 OHC09 adversarial Red — mandatory

Add at least two deterministic tests:

1. snapshot lookup throws; direct load blocks; another request advances
   high-water; release direct load; first caller fails final completion and does
   not publish/return the older result;
2. snapshot publication throws after candidate load; another request enters
   quarantine before logical completion; the request-local candidate is rejected.

Also cover the safe control: if state remains admissible, direct/request-local
data returns detached and uncached. These tests must fail if the implementation
contains `catch -> direct load -> return` without owner re-entry.

### 8.6 Implementation order

Implement in this order:

1. immutable snapshot builder/traversal core;
2. bounded safety registry and scope admission;
3. exhaustive generation outcome application;
4. high-water/quarantine transition methods;
5. bounded ordinary snapshot adapter;
6. owner-owned single-flight;
7. unified final completion operations;
8. service orchestration and null-tenant bypass;
9. production DI factory/registration.

Never place final completion logic only inside the shared flight: each waiter
must revalidate independently. Never make cache publication success a
prerequisite for a safe request-local return, but always make final safety-state
validation a prerequisite.

### 8.7 Verification

```bash
dotnet build src/Framework/Modules/CrestCreates.Organization
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests --filter "FullyQualifiedName~Hierarchy"
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests --filter "FullyQualifiedName~Cache"
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests --filter "FullyQualifiedName~Organization"
git diff --check
```

**Checkpoint:** OHC01-OHC24 Green deterministically. Handoff must call out OHC09
tests and link every fallback catch branch to its final completion operation.

---

## 9. Slice 5 — Production DI and Real PostgreSQL Multi-instance Proof

**Purpose:** Prove correctness across independent local cache owners with one
durable authority and no event/cache sharing.

### 9.1 Composition Red

Prove:

- `AddOrganizationKernel` has exactly one production
  `IOrganizationHierarchyService` registration;
- owner lifetime is singleton, hierarchy service remains scoped;
- two service scopes in one provider share an owner;
- two service providers have independent owners;
- PostgreSQL replaces `IOrganizationStore` without disconnecting generation;
- Organization has no dependency on `CrestCreates.Caching`, Redis, Runtime, or a
  concrete persistence project.

### 9.2 Real topology

Build the test with:

```text
one PostgreSQL 16 schema
    + service provider A / Organization cache owner A
    + service provider B / Organization cache owner B
    + no invalidation event
    + no shared IMemoryCache instance
```

Sequence:

1. Save tenant T hierarchy V1; generation becomes G1.
2. Resolve/query both providers; each loads and caches V1 independently.
3. Save V2 through provider A's Store only; do not publish an event.
4. Query A and B through fresh service scopes.
5. Both read G2, reject V1, and return detached V2 hierarchy.
6. Verify each owner performed its own reload and no cross-provider flight/cache
   object was shared.

Run the same key in a second tenant to prove isolation. Include null-tenant
unfiltered bypass against real PostgreSQL without retaining a cross-tenant entry.

### 9.3 Verification

```bash
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~PostgreSqlOrganizationHierarchyCacheTests"
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~PostgreSqlOrganizationGenerationTests"
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
git diff --check
```

**Checkpoint:** OMI01-OMI02 Green; correctness requires neither event delivery,
shared cache, nor distributed lock.

---

## 10. Slice 6 — Permission Direct-authority Cutover

**Purpose:** Remove the unsafe positive cache from the production authorization
decision while preserving unrelated Authorization infrastructure.

### 10.1 Inventory before edit

Run:

```bash
rg -n "PermissionGrantCache(Service|Options)|GetPermissionCacheKey|AddCrestCaching|TenantCacheKeyContributor|AuditTenantContextResolver|TenantCacheInvalidator" src tests
```

Record every production consumer. This inventory is the deletion boundary.
Permission-only evidence cannot authorize assembly-wide cleanup.

### 10.2 Unit and composition Red

Add/update tests proving:

- `PermissionGrantStore` queries repository on every call;
- an old positive backend cache entry is ignored;
- cache service/provider outage is irrelevant to grant/revoke/check;
- repository failure propagates and cannot fall back to a stale grant;
- tenant and global filtering remains exact;
- Grant/Revoke mutate repository without cache invalidation;
- SuperAdmin behavior is unchanged;
- DI constructs the full authorization graph without Permission cache services;
- unrelated `AuditTenantContextResolver` and cache-key/invalidation consumers
  remain constructible and retain behavior.

Rewrite `PermissionGrantManagerTests` to assert authority mutation rather than
cache removal. Do not retain mocks of a service production no longer resolves.

### 10.3 Implementation

- replace cache GetOrAdd with direct repository query in `PermissionGrantStore`;
- remove cache constructor dependencies and invalidation calls from Manager;
- remove only Permission cache options/service registrations;
- move the two retired source files to `99_RecycleBin/issue-26/authorization/`;
- remove obsolete Permission-only test helpers/usings;
- keep `AddCrestCaching` and related project references while other production
  consumers exist.

### 10.4 Verification

```bash
dotnet build src/Framework/Infrastructure/CrestCreates.Authorization
dotnet test tests/Framework/Ddd/CrestCreates.Application.Tests --filter "FullyQualifiedName~Permission"
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests --filter "FullyQualifiedName~Authorization"
rg -n "PermissionGrantCache(Service|Options)" src tests
git diff --check
```

The final `rg` may find only intentional `99_RecycleBin` history or explicit
negative architecture assertions; no production compile/DI/read path may refer
to the retired types.

**Checkpoint:** PSC01-PSC02, PSC04-PSC06, and PSC08 Green at unit/composition
level; unrelated caching preserved by evidence.

---

## 11. Slice 7 — Real EF Permission Multi-instance Security Evidence

**Purpose:** Prove a legal direct repository writer is sufficient and stale
positive state cannot authorize after a committed revoke.

### 11.1 Test topology

Use the existing Testcontainers PostgreSQL integration infrastructure:

```text
one committed EF Permission authority
    + application/service scope A
    + independent fresh application/service scope B
    + direct IPermissionGrantRepository writer
    + legacy cache backend seeded with stale positive value
```

Do not substitute InMemory repository, a shared DbContext, or Manager-only
mutation. Use fresh scopes/DbContexts and the real commit/unit-of-work path.

### 11.2 Required cases

- Seed a grant, verify allowed, revoke/commit via direct repository writer that
  bypasses `PermissionGrantManager` and emits no invalidation, then verify a
  fresh checker/store scope denies.
- Seed an old positive `Authorization.PermissionGrant` cache value before
  revoke and prove production ignores it afterward.
- Make the cache backend throw and prove direct authority result is unchanged.
- Make repository access fail and prove authorization fails closed with no
  cache fallback.
- Verify direct committed grant is observed by a fresh scope without Manager.
- Repeat tenant-specific/global filtering cases and preserve SuperAdmin bypass.

### 11.3 Verification

```bash
dotnet test tests/Framework/Web/CrestCreates.IntegrationTests --filter "FullyQualifiedName~PermissionCacheConsistencyIntegrationTests"
dotnet test tests/Framework/Web/CrestCreates.IntegrationTests --filter "FullyQualifiedName~Permission"
dotnet test tests/Framework/Ddd/CrestCreates.Application.Tests --filter "FullyQualifiedName~Permission"
git diff --check
```

**Checkpoint:** PSC01-PSC05 and PSC07 Green on real committed EF authority. The
handoff explicitly states that this is provider/security evidence, not an EF
NativeAOT claim.

---

## 12. Slice 8 — Boundary and Regression Closure

**Purpose:** Lock the unique mainlines and prevent future accidental restoration
of stale paths.

### 12.1 Architecture tests

Add `VersionedCacheConsistencyArchitectureTests` with focused assertions:

- every concrete `IOrganizationStore` implements typed generation read;
- Organization implementation has no Runtime, Redis, Crest caching, or concrete
  persistence reference;
- production Organization DI exposes one hierarchy service and singleton owner;
- null tenant cannot form an Organization hierarchy cache key;
- `PermissionGrantStore` constructor/read path has no cache dependency;
- Permission cache service/options are absent from production compilation/DI;
- `AddCrestCaching`, `TenantCacheKeyContributor`, `AuditTenantContextResolver`,
  and proven unrelated consumers remain;
- existing Runtime/Persistence dependency direction remains valid.

Prefer assembly references, constructors, and service-descriptor evidence over
fragile source-text matching. Use source assertions only for an invariant that
cannot be expressed structurally, and scope it narrowly.

### 12.2 Regression suites

Run the Organization semantic suite, reference-data provider suites,
Authorization tests, integration tests, migration tests, and all dependency
boundaries. Audit warnings for new reflection/trimming problems and fix them
before AOT.

```bash
dotnet build
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests
dotnet test tests/Framework/Ddd/CrestCreates.Application.Tests --filter "FullyQualifiedName~Permission"
dotnet test tests/Framework/Web/CrestCreates.IntegrationTests --filter "FullyQualifiedName~Permission"
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
git diff --check
```

**Checkpoint:** unique Organization and Permission mainlines are structurally
locked; all non-AOT acceptance IDs are Green.

---

## 13. Slice 9 — PostgreSQL NativeAOT Publish-Link-Run

**Purpose:** Extend existing native evidence for the modified first-party
PostgreSQL Runtime/Organization mainline.

### 13.1 Host changes

In the existing AOT Host:

- call `AddOrganizationKernel()` before PostgreSQL registration replaces the
  Store;
- resolve `IOrganizationHierarchyService` from DI rather than manually creating
  `DefaultOrganizationHierarchyService`;
- allow two independently built ServiceProviders to point to the same schema;
- retain every existing scenario and marker.

Native scenario:

```text
provider A/cache A + provider B/cache B
    -> V013 clean migration
    -> Save V1 / G1
    -> warm V1 through both providers
    -> Save V2 / G2 through provider A Store only
    -> no event and no shared local cache
    -> both hierarchy services reject V1 and return detached V2
    -> null tenant bypass executes without retained snapshot
    -> emit CRESTCREATES_VERSIONED_ORGANIZATION_CACHE_OK
```

Do not add Permission to this capability claim. Its EF provider has separately
declared AOT capability.

### 13.2 Fixture

The fixture publishes `linux-x64` with the repository's explicit AOT mode,
executes the original native binary against PostgreSQL 16, and requires the new
marker plus every prior marker. Running `dotnet Host.dll` is not acceptance.

### 13.3 Verification

```bash
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests --filter "FullyQualifiedName~VersionedOrganizationCache"
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests
git diff --check
```

Capture publish command, RID, binary path, process exit code, new marker, prior
markers, and duration in the Slice handoff.

**Checkpoint:** AOT01 Green from the original published native executable.

---

## 14. Slice 10 — H3 Observation and Product Closure

**Purpose:** Close Issue #26 with evidence, then separately judge the reused #68
checks without inventing a Harness product.

### 14.1 Product review first

Review every Spec exit criterion and acceptance ID. The product review must
explicitly answer:

- Is every successful Organization Save atomically paired with one generation
  advance in both providers?
- Can a schema/invariant/cancellation defect be misclassified as Unavailable?
- Can old snapshot data return after higher observation, regression, quarantine,
  mismatch, or authority failure?
- Can capacity pressure erase safety knowledge?
- Does every flight waiter and every direct fallback pass the final completion
  gate?
- For OHC09 specifically, is there any `cache failure -> direct load -> return`
  branch that omits admission capture and final revalidation?
- Can Permission production code consult or re-enable the old positive cache?
- Does a legal repository writer need Manager/invalidation for correctness?
- Were unrelated Authorization caching consumers preserved?
- Did the native binary execute V013 and two independent cache owners?

Search evidence for OHC09:

```bash
rg -n "catch|TryGet|Set|fallback|Complete.*Result|Complete.*Fallback|ObservedHighWater|Quarantine" src/Framework/Modules/CrestCreates.Organization
```

Trace every ordinary cache catch to the same owner-controlled final gate. A
review note without adversarial passing tests is insufficient.

### 14.2 H3 sidecar

Create `docs/review/phase-9d-h3-reuse-observations.md` only after product review.
For each reused check—NativeAOT fixture, runner-free provider kit, dependency
boundaries—record:

| Field | Required observation |
|---|---|
| Check | exact reused check |
| Value | what decision/defect signal it contributed |
| Misses | important defects it could not detect |
| Noise | false or low-value signal encountered |
| Runtime Cost | measured execution/setup cost |
| Maintenance Cost | code/fixture burden introduced |
| Context Required | product knowledge needed to interpret it |
| Defects Actually Caught | concrete defect, or explicit none |
| Verdict | retain, adjust, or retire for the next phase |

Do not wrap these checks in a new generic Harness runner and do not treat the H3
sidecar as product acceptance.

### 14.3 Memory and final commands

Update `memory.md` narrowly after reconciling any concurrent/user changes. State
the actual implemented provider capabilities and native evidence; do not claim
Permission AOT from the Runtime fixture.

```bash
dotnet build
dotnet test
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests
git diff --check
git status --short
```

If the full suite contains a known unrelated baseline failure, preserve exact
command/output provenance and prove all Issue #26 focused suites independently.
Do not label the phase complete merely because focused tests pass.

**Checkpoint:** H301 complete; product review precedes H3 judgment; `memory.md`
matches code and evidence; Issue #26 is ready for implementation review.

---

## 15. Detailed Review Checklist

### 15.1 Authority and migration

- [ ] Typed Global/Tenant scope cannot be confused with nullable collection query.
- [ ] Default/Unknown/malformed scope fails before provider I/O.
- [ ] All four Save surfaces share one scope generation.
- [ ] Successful identical Save advances exactly once.
- [ ] Data + generation publish/commit atomically.
- [ ] Overflow rolls back; no wraparound.
- [ ] COMMIT unknown is not replayed and never yields a one-sided pair.
- [ ] V013 is appended; V001-V012 text/checksums are unchanged.
- [ ] V012 upgrade observes generation 0 and first new Save reaches 1.
- [ ] Schema drift/corruption/cancellation never becomes Unavailable.

### 15.2 Hierarchy safety

- [ ] Generation is read before the authority collection.
- [ ] Higher valid generation advances high-water before cache/load.
- [ ] Failed load never rolls high-water back.
- [ ] Lower generation enters sticky quarantine without data fallback.
- [ ] Quarantined Unavailable performs no authority data I/O.
- [ ] Recovery at/below floor is rejected.
- [ ] Same highest generation above floor can retry after failed recovery.
- [ ] Snapshot eviction cannot remove safety state.
- [ ] Safety capacity exhaustion fails unseen scopes closed.
- [ ] Delayed older candidate neither publishes nor completes over newer state.
- [ ] Every waiter revalidates; the shared flight does not complete safety alone.
- [ ] Caller cancellation detaches only that waiter.
- [ ] Failed/canceled/timeout flight is removed and retryable.
- [ ] Null tenant bypasses generation, cache, safety registry, and flight.
- [ ] Results remain detached and traversal semantics remain exact.

### 15.3 OHC09 final-gate audit

- [ ] Snapshot lookup failure captures an owner-issued admission token.
- [ ] Snapshot publication failure retains/captures the admitted token.
- [ ] Direct/request-local load is stamped with the admitted generation.
- [ ] The branch re-enters the same scope safety owner after the load.
- [ ] Available(G) result requires G equal current high-water and quarantine safety.
- [ ] Unavailable fallback requires NORMAL and unchanged admission high-water.
- [ ] High-water advance during fallback rejects the result.
- [ ] Quarantine transition during fallback rejects the result.
- [ ] No catch branch returns Store data directly.
- [ ] Deterministic adversarial tests cover lookup and publication failure races.

### 15.4 Permission security

- [ ] PermissionGrantStore queries repository on every call.
- [ ] Manager has no Permission cache invalidation dependency.
- [ ] No option/DI registration can re-enable the old positive cache.
- [ ] Old positive entries and cache outage are irrelevant.
- [ ] Repository failure has no stale-positive fallback.
- [ ] Direct repository writer is visible after commit without Manager/event.
- [ ] Tenant/global filtering and SuperAdmin remain exact.
- [ ] Unrelated caching/key/audit consumers remain composed.

### 15.5 Evidence and scope

- [ ] InMemory and PostgreSQL run the same runner-free OVG cases.
- [ ] Real PostgreSQL hierarchy topology uses independent providers/owners.
- [ ] Real EF Permission topology uses fresh scopes and direct repository writer.
- [ ] Boundary tests lock unique mainlines and dependency direction.
- [ ] Native executable, not managed DLL, emits the new and prior markers.
- [ ] No Descriptor, Organization Identity, Data Permission Rule, derived scope,
      Redis correctness path, distributed lock, or generic cache framework added.
- [ ] H3 sidecar follows product review and records actual cost/value/defects.

---

## 16. Handoff Template for Implementing Agents

Every Slice handoff should use this shape:

```text
Slice:
Commit:
Acceptance IDs closed:

Red evidence:
- command
- expected missing behavior
- failure excerpt

Green evidence:
- command / passed count / duration
- provider or topology

Files changed:
- production
- tests
- migrations/manifests
- recycle-bin moves

State-machine evidence:
- observed high-water/floor transitions exercised
- final completion gates exercised
- OHC09 branch audit, when applicable

Compatibility/evidence notes:
- public API impact
- migration checksum/tail status
- NativeAOT status (unclaimed until Slice 9)
- unrelated user changes preserved

Unresolved findings:
- none, or exact blocker with owner
```

The implementing agent should not silently reinterpret a frozen decision. If
code reality makes a locked choice impossible, stop at the smallest failing
test, document the conflict with file/line evidence, and request a Spec/Plan
reconciliation before creating a second mainline.

---

## 17. Exit Criteria

Implementation is complete only when all of the following are evidenced:

1. Both Organization providers implement explicit typed scope generation.
2. Every successful Save surface advances the shared scope generation once.
3. InMemory and PostgreSQL publish data plus generation atomically.
4. V013 clean bootstrap, V012 upgrade, exact manifest, drift, and checksum tests
   pass without changing V001-V012.
5. Provider availability is the only normal Unavailable outcome.
6. Hierarchy cache is tenant-explicit, bounded, local, immutable, and
   generation-validated on every cacheable read.
7. Null-tenant unfiltered reads are never retained.
8. High-water never decreases within the owner lifetime.
9. Quarantine cannot be erased by eviction, Unavailable, or capacity pressure.
10. Failed higher/recovery loads retain the exact safety knowledge required by
    the frozen state machine.
11. Per-instance single-flight has owner timeout, waiter-detach cancellation,
    exact cleanup, and retry after failure.
12. Every cached, candidate, typed-Unavailable, and ordinary-cache-failure
    result passes the final caller-completion safety gate.
13. OHC09 adversarial races prove cache infrastructure fallback cannot return
    after high-water advance or quarantine transition.
14. Two independent Organization cache owners over one PostgreSQL authority
    observe committed V2 without an event or shared cache.
15. Permission production checks query committed EF authority every time.
16. Old positive cache state, invalidation loss, and cache outage cannot grant
    Permission.
17. A legal direct Permission repository writer is observed without Manager or
    invalidation.
18. Permission authority failure cannot fall back to stale positive state.
19. Tenant/global Permission filtering and SuperAdmin behavior are unchanged.
20. Unrelated Authorization caching/key/audit consumers remain operational.
21. Dependency boundaries preserve the unique mainlines.
22. The existing PostgreSQL AOT native executable runs V013 and the independent
    cache scenario while retaining all prior markers.
23. No out-of-scope cache/generation/framework feature was introduced.
24. Product review passes before the H3 reuse judgment.
25. H3 records value, misses, noise, runtime cost, maintenance cost, context,
    defects caught, and retain/adjust/retire verdict.
26. `memory.md` and review artifacts describe only proven capability.

When these criteria are Green, the PR is ready for implementation review. The
first review focus is the OHC09 fallback audit, followed by provider failure
mapping, atomic V013 writes, Permission direct-authority composition, and native
publish-link-run provenance.
