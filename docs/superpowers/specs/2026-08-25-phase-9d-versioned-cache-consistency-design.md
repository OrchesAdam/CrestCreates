# Phase 9d — Versioned Cache Consistency Design Spec

- **Date:** 2026-08-25
- **Last revised:** 2026-08-26
- **Issue:** [#26 — Phase 9d Versioned Cache Consistency](https://github.com/OrchesAdam/CrestCreates/issues/26)
- **Depends on:** [#24 — Durable Persistence Foundation](https://github.com/OrchesAdam/CrestCreates/issues/24), [#25 — Transactional Outbox & Reliable Event Delivery](https://github.com/OrchesAdam/CrestCreates/issues/25), [#69 — Durable Control Plane and Reference Data Stores](https://github.com/OrchesAdam/CrestCreates/issues/69), [#68 — Engineering Harness Seed](https://github.com/OrchesAdam/CrestCreates/issues/68)
- **Current-master baseline:** `81a42edc`
- **Status:** R4 APPROVED / FROZEN — R3 high-water state-machine blocker addressed; implementation must follow this behavioral contract
- **Provider baseline:** Organization InMemory `FullSemantic`; Organization PostgreSQL 16 direct-Npgsql `FullDurable` with the frozen catalog currently ending at V012; Permission Grant authority is the existing EF Core repository path and has a separately declared provider/AOT capability

---

## 1. Decision Summary

Phase 9d closes the cache-consistency boundary only for current data whose read
cost or security consequence justifies action. It does not introduce a generic
cache framework and does not implement the old roadmap's complete candidate
list.

The target mainlines proposed by this R4 draft are:

```text
Organization hierarchy
    tenant-scoped authority generation read
        -> same-generation immutable local snapshot hit
        -> otherwise one per-instance/key/generation authority load
        -> publish only without regressing a newer snapshot
        -> traversal over the immutable snapshot

Permission authorization
    PermissionChecker
        -> PermissionGrantManager
        -> PermissionGrantStore
        -> IPermissionGrantRepository
        -> authoritative grant rows on every check
```

Organization receives one tenant-scope generation that is advanced atomically
with every Organization Store Save. The hierarchy cache is a bounded,
per-process read-model cache. It validates the authoritative generation on each
cacheable read, so sharing the cache or receiving an invalidation event is not
required for correctness.

Permission takes the safe cutover allowed by Issue #26: the current unversioned
positive grant cache is removed from the production read mainline. Current
master has no durable permission version authority shared by every legal writer,
and manufacturing one would reopen the EF authority/migration boundary across
multiple Hosts. Direct authority reads are therefore the only honest
security-consistency contract in this phase. There is no option that can
re-enable the old unversioned positive cache.

Descriptor Snapshot and Data Permission Rule cache implementations remain
deferred. Organization Identity caching remains optional and is not selected by
this draft. No negative-cache subsystem is introduced.

The central rule is:

```text
authority-generation equality = cached-read correctness
direct authority read = fallback only while freshness is unknown, not disproved
known-invalid freshness = explicit fail-closed result
cache eviction / local observation / future change fact = freshness or cost only
```

---

## 2. Current-Master Codebase Alignment

### 2.1 Descriptor Snapshot is already an immutable in-process read model

`RegistryBase<TDescriptor>` constructs and serves an immutable snapshot through
`ById`, `ByName`, `ByVersion`, and `All`. Caching it again would add a cache over
an existing read model without evidence of a material read-cost problem.

`Immutable-by-hash` remains a valid consistency class, but no Descriptor cache
implementation, provider, invalidator, or test fixture is added in Phase 9d.

### 2.2 Organization hierarchy has a real repeated materialization cost

`DefaultOrganizationHierarchyService` currently performs this work for each
ancestor or descendant query:

```text
IOrganizationStore.GetOrganizationUnitsAsync(tenantId)
    -> materialize every unit in the tenant
    -> rebuild scoped unit map
    -> rebuild parent-to-children map
    -> traverse
```

The direct-Npgsql PostgreSQL Store delivered by #69 makes the read durable, but
does not remove the repeated query and map-construction cost. A tenant-wide
immutable hierarchy snapshot is therefore the primary cache target.

Current Organization scope semantics remain normative:

| Operation | `tenantId == null` meaning |
|---|---|
| Entity identity and point read | global identity |
| Collection/query method | no tenant filter |

The hierarchy service calls a collection method. A null tenant therefore may
load rows from every tenant even though scoped traversal keys remain distinct.
Phase 9d must not cache this intentionally unscoped cross-tenant collection
read. Only a non-null, non-blank tenant ID enters the hierarchy cache.

### 2.3 One Organization generation is intentionally broader than hierarchy

`IOrganizationStore` owns four Save surfaces:

- Organization Unit;
- Position;
- Membership;
- Role Assignment.

This draft chooses one generation per normalized Organization tenant scope and
advances it for every successful Save surface. A hierarchy snapshot depends
only on Organization Units, so Position/Membership/Role saves may cause a
conservative reload. That is accepted in Phase 9d because it keeps one clear
Organization freshness contract and leaves a legal future seam for an Identity
read model without introducing multiple version streams prematurely.

The generation is a read-set freshness stamp. It is not an ETag, expected
version, or optimistic-concurrency token and does not alter #69 blind
last-committed-writer-wins behavior.

### 2.4 Organization Identity remains uncached

`DefaultOrganizationIdentityService` derives `OrganizationContext` from
Membership and Role Assignment queries. This draft does not add a second cache
surface. The broader Organization generation makes a later cache possible, but
the Phase 9d implementation boundary remains the hierarchy read model.

The implementation review may record read-count evidence. It must not implement
Identity caching merely because the generation now exists.

### 2.5 Data Permission Rule and derived scope remain uncached

`IDataPermissionScopeRuleStore.GetScopeKindAsync(...)` is a narrow authority
lookup. `DataPermissionScope` is derived from that lookup plus Organization
identity/hierarchy and must never be persisted or cached as a second authority.

A future rule cache would require both tenant and global rule generations,
because a tenant lookup can fall back to global rules:

```text
(TenantRuleGeneration, GlobalRuleGeneration)
```

Phase 9d records this consistency class only. It adds no generation table,
cache owner, or event for Data Permission Rule.

### 2.6 The existing Permission cache is unsafe after a missed invalidation

Current production code is:

```text
PermissionChecker
    -> PermissionGrantManager
    -> PermissionGrantStore
    -> PermissionGrantCacheService
    -> ICrestCacheService
```

The cache key is `ProviderType:ProviderKey`. The cached value contains global
and tenant grants for that provider and is later filtered by tenant. Entries
have a five-minute TTL but no authority version. Grant/Revoke performs a
repository mutation and then calls `RemoveAsync`.

Current code has four relevant safety gaps:

1. a process crash or cache-provider failure can lose invalidation after the
   permission mutation;
2. another process can retain a stale positive value until TTL expiry;
3. under an outer EF Unit of Work, invalidation can run before the database
   transaction commits;
4. legal permission writers are not confined to `PermissionGrantManager`;
   `TenantBootstrapper` writes through the generic repository surface.

The computed `TenantCacheKeyContributor` value in Grant/Revoke is not used by
`PermissionGrantCacheService`, so it provides no additional isolation or
freshness guarantee.

### 2.7 Permission chooses direct-authority safety, not a speculative version table

An authoritative Permission version would be safe only if every legal mutation
and version advance were one commit, every Host migrated the version authority,
every writer participated, and reads validated that version cheaply. Current
master does not have that contract.

Adding it in Phase 9d would expand into:

- the inherited generic permission repository mutation surface;
- multiple EF Core Host DbContexts and migrations;
- first-row/concurrent-version initialization semantics;
- provider-specific transaction behavior;
- seeding and tenant bootstrap cutover;
- an AOT capability that cannot be inferred from the direct-Npgsql Runtime
  provider.

That expansion is not required to close stale-positive safety. The selected
cutover is therefore:

```text
PermissionGrantStore.GetGrantsAsync
    -> query IPermissionGrantRepository every time
    -> map/filter the current committed authority result

PermissionGrantCacheService
    -> absent from production reads and writes
```

Repository or database failure propagates and denies successful authorization
completion. The code must never convert that failure into a grant and must never
fall back to an old cache entry. In-flight checks or checks inside a database
snapshot established before a concurrent revoke may linearize before that
commit. A fresh authority query/transaction established after the commit must
not return the revoked grant.

### 2.8 Existing generic caching is not the Organization correctness kernel

`ICrestCacheService` supports Memory and Redis backends, TTL, key generation,
pattern removal, and generic JSON serialization. Phase 9d does not extend this
API with versions, locks, generic negative entries, or compare-and-set.

The Organization hierarchy cache is local and typed because:

- correctness already requires an authority-generation read per use;
- per-instance single-flight is the declared stampede boundary;
- no cluster-wide one-load guarantee is required;
- no shared-cache event is needed;
- typed immutable in-process values avoid a new generated-JSON/provider
  contract;
- Redis serialization capability must not be mistaken for NativeAOT evidence.

If a future phase selects a real shared cache, it must prove that provider with
its own serialization, failure, and multi-instance contract. Phase 9d makes no
shared-cache claim.

### 2.9 #25 has no legal selected producer seam in this phase

#69 Organization Saves deliberately own independent top-level Control Plane /
reference-data commits and are not automatically enlisted in the Runtime
Outbox. Reopening that transaction boundary merely to manufacture cache events
would violate both Issues.

Phase 9d therefore emits no reliable Organization change fact. Missed-event
correctness is proved by delivering no event at all between two cache instances.
Concurrent/delayed generation observations and loads must still not regress the
newer local snapshot.

### 2.10 #68 H3 reuses checks without a Harness wrapper

The #68 H2 review admitted exactly three existing product-owned checks for #26:

- the real PostgreSQL NativeAOT publish-link-run fixture;
- runner-free provider contract-kit structure;
- dependency-boundary tests.

Phase 9d reuses them in place. It does not promote the incomplete Phase 9c
444-tuple exact-set ledger and does not create a generic Harness runner,
collector, or dashboard.

### 2.11 R2 review-blocker closure

R2 retains the R1 architecture and closes its contract gaps:

- generation authority uses an explicit `OrganizationScopeIdentity`; nullable
  tenant text no longer carries another implicit scope meaning;
- generation reads distinguish typed availability from cancellation,
  persisted/schema/contract invariant failure, and observed regression;
- only an explicit typed `Unavailable` outcome may fall back to direct
  Organization authority;
- known pre-commit failure/rollback is separated from COMMIT unknown;
- generation mismatch followed by authority failure can never serve the
  previous snapshot;
- Permission acceptance includes a legal direct repository writer that bypasses
  `PermissionGrantManager` and performs no invalidation;
- Phase 9 `Stale Security State` closure is explicitly limited to selected
  Phase 9 cache/mainline security-positive authority state;
- Permission cache retirement cannot remove unrelated Authorization caching/key
  consumers such as `AuditTenantContextResolver`.

### 2.12 R3 freeze-blocker closure

R3 retains the R2 boundary and closes the final freshness-safety gap:

- an available generation below the local observed high-water is known-invalid
  authority history, not an availability condition;
- a regressed or still-quarantined scope returns no hierarchy result and does
  not perform a direct authority-data fallback;
- quarantine is sticky across `Unavailable` and every generation that has not
  escaped its captured historical floor;
- only an eligible non-regressing generation followed by successful authority
  load and publication releases quarantine;
- generation-read status is three-state: `Available`, `Unavailable`, and the
  default-safe `Unknown`; `Unknown` is a programming/contract failure and never
  authorizes fallback.

### 2.13 R4 high-water state-machine closure

R4 retains the R3 architecture and separates two process-local meanings that
must not share one ambiguous value:

- `ObservedHighWater` is the highest valid authoritative generation this
  process has successfully observed, regardless of whether the following data
  load or cache publication succeeded;
- `QuarantineFloor` is the historical boundary captured when regression enters
  quarantine and which recovery must strictly exceed;
- a valid higher generation advances `ObservedHighWater` before cache equality
  or authority loading, so a failed load cannot make the process forget the
  observation;
- while quarantined, the highest observed recovery generation may be retried
  when it remains above `QuarantineFloor`; recovery does not require an
  unrelated later Organization mutation;
- candidate publication and logical caller completion revalidate the current
  safety state atomically, so a quarantine transition that wins the race
  prevents an older in-flight candidate from publishing or returning.

---

## 3. Goal

Close the selected cache-consistency and stale-security boundaries while
preserving the existing authority and dependency mainlines.

The phase must prove:

1. every successful Organization Save atomically advances the same tenant-scope
   generation as the saved snapshot;
2. hierarchy cache entries are used only when their generation equals the
   current authority generation;
3. a missed invalidation/event cannot keep an old Organization snapshot valid;
4. tenant A's snapshot and generation cannot satisfy tenant B;
5. null-tenant unscoped hierarchy reads bypass the cache;
6. delayed loads or observations cannot replace a newer cached generation;
7. single-flight limits only per-instance duplicate loads and never controls
   correctness;
8. failures do not become cached authority;
9. an observed authority-generation regression returns no hierarchy result and
   remains quarantined across unavailable or non-recovering observations;
10. every valid higher generation observation advances process-local
    `ObservedHighWater` even when its data load/publication later fails;
11. quarantine recovery may retry the same highest observed generation above
    `QuarantineFloor`, while an older in-flight candidate cannot return after a
    quarantine transition wins the safety-state race;
12. a default/unknown generation outcome cannot authorize direct-read fallback;
13. Permission authorization never consults an unversioned stale positive grant
   cache after the cutover;
14. a revoke committed through one instance is observed by another instance's
    next authority-based check without requiring invalidation delivery;
15. reused #68 checks retain product ownership and produce an explicit H3
    value/noise/cost/context verdict.

---

## 4. Boundary

### 4.1 In scope

- Normative cache-candidate inventory and decision record.
- One Organization tenant-scope generation contract on `IOrganizationStore`.
- InMemory atomic Save + generation behavior.
- PostgreSQL V013 generation schema and atomic Save + generation behavior.
- One bounded, local, immutable Organization hierarchy snapshot cache.
- Per-instance single-flight keyed by tenant and generation.
- Monotonic cache publication under delayed/concurrent loads.
- Cache bypass for null-tenant unscoped hierarchy reads.
- Direct-authority fallback only for explicit typed generation
  `Unavailable` on a non-quarantined scope, or local cache-infrastructure
  failure before freshness has been disproved.
- Deterministic failure and cancellation behavior.
- Complete Permission Grant cache bypass/removal from the production
  authorization mainline.
- Real multi-instance Organization tests using one shared PostgreSQL authority
  and independent local caches.
- Real multi-instance Permission revoke tests using shared committed EF
  authority and independent application scopes/instances.
- Extension of the existing PostgreSQL NativeAOT host/fixture for the
  Organization versioned-cache path.
- Reuse of the runner-free contract kit and dependency-boundary tests.
- #68 H3 observation sidecar.

### 4.2 Out of scope

- A universal versioned cache abstraction.
- A distributed cache or a new cache backend.
- A distributed lock or cluster-wide single-flight guarantee.
- Descriptor Snapshot cache implementation.
- Organization Identity cache implementation.
- Data Permission Rule cache or generation implementation.
- Cached or persisted `DataPermissionScope`.
- A Permission authority-version table or re-enabled Permission positive cache.
- A generic negative-cache service.
- Correctness based on TTL, invalidation, or change-event receipt.
- Automatic Outbox enlistment for Organization/reference-data writes.
- Changes to #69 blind-write concurrency semantics.
- UI, administration, cache inspection, or manual invalidation tools.
- Credential or session revocation freshness.
- Authentication token or role-claim freshness.
- External identity-provider state propagation.
- Durable detection or fencing of authority-history rollback across process
  restart; Phase 9d provides no durable authority epoch.
- A claim that the EF Permission provider is NativeAOT-verified because the
  direct-Npgsql Runtime fixture passes.
- Phase 9c exact-tuple ledger completion or promotion.

---

## 5. Inventory Decision

| Candidate | Authority / current read model | Read cost | Key / scope | Version / generation | Stale consequence | Security posture | Selected behavior | Consistency class | Event role | Failure fallback |
|---|---|---|---|---|---|---|---|---|---|---|
| Descriptor Snapshot | immutable `RegistryBase<TDescriptor>` snapshot | already in-memory/indexed | content hash / descriptor indexes | content hash | stale only if registry authority is violated | no mutable authorization state | **Deferred** | immutable-by-hash | none | existing registry |
| Organization Hierarchy | `IOrganizationStore` Organization Units | tenant list query plus map reconstruction per traversal | explicit tenant ID | Organization scope generation | stale hierarchy/data-scope result | authorization-adjacent; stale scope must be detected | **Implement** | generation-validated reference cache | none in Phase 9d | direct Store read |
| Organization Identity | Membership + Role Assignment derived context | two narrow authority queries plus projection | user + tenant | Organization scope generation | stale identity/data-scope result | authorization-adjacent; no cache selected | **Deferred after evidence** | generation-validated derived read model | future accelerator only | direct Store reads |
| Data Permission Rule | `IDataPermissionScopeRuleStore` | narrow prioritized lookup | tenant/global + resource/action/permission | future tenant + global pair | stale authorization scope | security-sensitive; no unversioned cache allowed | **Deferred** | tenant + global generation rule cache | future accelerator only | direct rule lookup |
| Permission Grant | EF `IPermissionGrantRepository`; current unversioned cache | provider grant query | provider type + provider key; tenant filtered after read | none on current master | stale positive authorization | fail closed; never use unvalidated positive state | **Bypass/remove cache** | direct security authority | none | authority success or fail closed |
| Negative result | derived absence | candidate-specific | owner-specific | owner generation + bounded TTL if selected | stale denial / availability | never an independent authority | **No separate entries selected** | owner generation + bounded TTL | owner only | authority |

Deferred candidates are not acceptance gaps.

An empty Organization hierarchy snapshot is a complete generation-stamped
collection value, not a separately keyed negative entry. A missing unit during
traversal is derived from that complete snapshot and receives no independent
negative cache record or TTL.

---

## 6. Consistency Classes

### 6.1 Immutable-by-hash

```text
key = canonical content hash
same hash = same immutable value
invalidation not required
```

Defined for inventory completeness; no Phase 9d implementation.

### 6.2 Generation-validated Organization read model

```text
entry = tenant ID + Organization generation + immutable hierarchy snapshot

read
    -> read current authority generation G
    -> entry.Generation == G: use entry
    -> otherwise: load authority and build a candidate stamped G
```

The equality check is required on every cacheable hierarchy read. TTL, local
eviction, or a future change notification may reduce cost but never substitutes
for the generation read.

### 6.3 Direct-authority security state

Permission has no cache-validity proof on current master. Its Phase 9d class is:

```text
authorization read
    -> current committed Permission Grant authority
    -> result

authority unavailable
    -> no successful grant decision
```

An old cache value is not a fallback. This is stronger than TTL plus
invalidation and deliberately trades read cost for a closed stale-positive
boundary.

### 6.4 Tenant + global generation rule cache

Reserved for future Data Permission Rule work:

```text
freshness = (TenantRuleGeneration, GlobalRuleGeneration)
```

No Phase 9d implementation.

### 6.5 Negative entry

If a later selected owner stores a separate absence entry, it must contain the
owner's authority generation and an explicit bounded TTL. Generation provides
correctness; TTL bounds stale-denial availability impact. Phase 9d selects no
such entry.

---

## 7. Organization Generation Contract

### 7.1 Public Store surface

The minimal contract addition is conceptually:

```csharp
public enum OrganizationScopeKind
{
    Unknown = 0,
    Global = 1,
    Tenant = 2
}

public readonly record struct OrganizationScopeIdentity
{
    public OrganizationScopeKind Kind { get; }
    public string? TenantId { get; }

    public static OrganizationScopeIdentity Global { get; }
    public static OrganizationScopeIdentity Tenant(string tenantId);

    // Construction is factory-controlled; default/Unknown is rejected.
}

public enum OrganizationScopeGenerationStatus
{
    Unknown = 0,
    Available = 1,
    Unavailable = 2
}

public readonly record struct OrganizationScopeGenerationRead
{
    public OrganizationScopeGenerationStatus Status { get; }
    public long Generation { get; }

    public static OrganizationScopeGenerationRead Available(long generation);
    public static OrganizationScopeGenerationRead Unavailable { get; }
}

public enum OrganizationHierarchyFreshnessFailureKind
{
    Unknown = 0,
    InvalidGenerationOutcome = 1,
    GenerationRegression = 2,
    QuarantinedGenerationUnavailable = 3
}

public sealed class OrganizationHierarchyFreshnessException
    : OrganizationException
{
    public OrganizationHierarchyFreshnessFailureKind FailureKind { get; }
    public long? ObservedGeneration { get; }
    public long? ObservedHighWaterGeneration { get; }
    public long? QuarantineFloorGeneration { get; }
}

public interface IOrganizationStore
{
    Task<OrganizationScopeGenerationRead> ReadScopeGenerationAsync(
        OrganizationScopeIdentity scope,
        CancellationToken cancellationToken = default);

    // Existing Save and read methods remain.
}
```

`OrganizationScopeIdentity.Global` and
`OrganizationScopeIdentity.Tenant(tenantId)` are the only valid construction
paths. `Tenant(...)` rejects null, empty, and whitespace text. `default`,
`Unknown`, or an otherwise malformed value is rejected before provider I/O.
`TenantId` is null only inside the factory-created explicit Global value; no
caller passes a nullable string to select generation scope. Provider
normalization to `(global, "")` or `(tenant, TenantId)` remains internal.

The existing nullable collection/query contract remains unchanged, but it is
not reused by the generation kernel:

```text
GetOrganizationUnitsAsync(null)
    = intentionally unfiltered collection query

ReadScopeGenerationAsync(OrganizationScopeIdentity.Global)
    = explicit global authority generation
```

There is no `ReadScopeGenerationAsync(null)` or default tenant argument.

`OrganizationScopeGenerationRead.Unavailable` is the only normal outcome that
can authorize cache bypass plus direct authority fallback, and only while the
scope is not quarantined. It is not returned for cancellation, corrupt state,
schema drift, unsupported contract version, or programming errors. PostgreSQL
maps only its existing
`RuntimePersistenceUnavailableException` category to `Unavailable` inside the
provider; invariant/contract exceptions escape unchanged. InMemory always
returns `Available(...)`.

`Available(generation)` rejects negative values. Factory-created `Unavailable`
uses canonical `Generation == 0`; consumers branch on `Status` and do not
interpret that number. `default(OrganizationScopeGenerationRead)` has
`Status == Unknown` and is deliberately not equivalent to `Unavailable`.
`Unknown`, an undefined status, a negative available generation, or any other
malformed status/value combination raises
`OrganizationHierarchyFreshnessException(InvalidGenerationOutcome)` before
cache or authority-data fallback I/O. The shared contract kit validates these
construction and consumption invariants.

`OrganizationHierarchyFreshnessException` is the explicit, provider-neutral
fail-closed signal for freshness knowledge that cannot safely authorize a
hierarchy answer. A generation regression carries the current observation,
`ObservedHighWater`, and `QuarantineFloor` when present. A quarantined
`Unavailable` outcome carries `ObservedHighWater` and `QuarantineFloor` with no
current observed generation. These failures are not provider availability
failures and must not be translated to direct-read fallback by the hierarchy
service.

A separate freshness-reader service is rejected because replacing only
`IOrganizationStore` could leave a disconnected reader implementation in DI.
Putting the method on the Store makes provider completeness compiler-visible
and keeps Save plus generation under one authority owner.

### 7.2 Generation semantics

- Type is non-negative signed 64-bit integer.
- A scope with no generation row/value reads as `0`.
- Each successful Save call advances exactly once for the saved entity's
  normalized tenant scope.
- All four Save surfaces advance the same scope generation.
- A Save of an identical snapshot still advances because the existing contract
  defines Save as a committed replacement operation, not change detection.
- Validation failure, cancellation before commit begins, known pre-commit
  failure, or known rollback advances neither data nor generation.
- COMMIT unknown is not classified as rollback and may have advanced both data
  and generation.
- Overflow fails the complete Save; wraparound is forbidden.
- The generation never decreases within an authority history.
- Callers cannot provide an expected generation.
- A returned generation grants no write ownership and cannot be used for
  optimistic concurrency.

### 7.3 Required write ordering

The observable authority rule is atomic commit:

```text
entity replacement + scope generation increment = one commit
```

The in-process implementation must publish data plus generation as one
scope-state transition. It may use an immutable per-scope state swap or make all
explicit-scope data/generation readers participate in the same scope guard; it
must not expose the new generation with the old data. PostgreSQL performs both
statements in the existing provider-owned top-level transaction and exposes
both only at COMMIT.

### 7.4 Why generation is not read after the authority value

On a cache miss, this order is forbidden:

```text
load old data
    -> concurrent mutation commits new generation
    -> read new generation
    -> incorrectly stamp old data with new generation
```

The required miss order is:

```text
read generation G
    -> load authority data
    -> stamp candidate with the earlier G
```

If a mutation commits between the two reads, the result may be conservative:
new data can be stamped with the older generation and reloaded on the next
read. It cannot be old data stamped with the newer generation.

The contract is linearizable at the successful generation observation for a
cache hit and at the authority collection observation for a miss/fallback. It
does not claim that a check started before a concurrent commit will be
retroactively changed after that commit.

### 7.5 Write failure taxonomy

| Write outcome | Required authority observation |
|---|---|
| Caller validation failure or pre-commit cancellation | no entity change and no generation change |
| Known pre-commit provider failure or known rollback | no entity change and no generation change |
| Known commit success | complete new entity snapshot and advanced generation |
| COMMIT acknowledgement unknown | do not retry automatically; fresh observation may show complete pre-commit or complete committed state, never a one-sided pair |

The COMMIT-unknown row does not claim caller ownership when another blind writer
may have committed. Exact no-concurrent old/new observation is owned by OVG11;
concurrent cases require only a complete provider-authoritative winner pair.

---

## 8. InMemory Generation Design

`InMemoryOrganizationStore` retains typed scope identity and adds one
atomically published state per normalized Organization scope. That state owns
the four entity collections plus the scope generation.

For each Save:

```text
validate and capture detached Snapshot
    -> select normalized scope
    -> acquire scope mutation guard
    -> checked next = current generation + 1
    -> build next scope state with entity replacement + next generation
    -> atomically publish the complete next scope state
    -> release guard
```

The guard is scoped by normalized tenant identity, so unrelated tenants do not
serialize their writes. Explicit-scope data and generation reads observe a
published scope state, never a partially updated pair. Unfiltered collection
queries may aggregate independently published scope states, matching their
existing no-cross-scope-transaction semantics. Reads preserve current
detached-snapshot and canonical order behavior. Generation reads honor
cancellation and return `Available(currentScopeGeneration)`.

The shared provider kit must prove generation behavior for all four Save
surfaces, not only Organization Unit.

---

## 9. PostgreSQL V013 Design

### 9.1 Catalog extension

V013 appends one table to the existing checksummed Runtime migration catalog:

```text
organization_scope_generations

tenant_scope_kind      text C not null
tenant_id              text C not null
generation             bigint not null
updated_at             timestamptz not null
PK (tenant_scope_kind, tenant_id)
CHECK normalized global/tenant identity
CHECK generation >= 1
```

Absence represents generation `0`; persisted rows begin at `1`. The table has
no foreign key to Organization entity tables. This preserves #69's lack of new
relationship constraints and allows a scope generation to survive independent
entity replacement history.

V013 must be included in:

- the frozen migration catalog and checksum validation;
- exact schema table/column/PK/check/collation manifests;
- clean bootstrap and V012-to-V013 upgrade tests;
- drift, duplicate migration, and wrong-checksum tests;
- the existing PostgreSQL AOT Host migration path.

### 9.2 Save composition

Every PostgreSQL Organization Save remains an independent
`ExecuteTopLevelAsync` operation. Inside its current transaction:

```text
upsert complete Organization snapshot
    -> upsert/increment matching scope generation with checked bigint semantics
    -> COMMIT
```

The generation statement must not open another connection or transaction. If
increment fails, the snapshot upsert rolls back. If the snapshot upsert fails,
the generation does not advance.

This does not enlist Outbox, Workflow, HumanTask, or another Runtime Store.

### 9.3 Generation read

`ReadScopeGenerationAsync` performs one indexed point read by the explicit
scope's normalized `(tenant_scope_kind, tenant_id)`. A missing row returns
`Status = Available, Generation = 0`. A provider availability failure returns
the explicit factory-created `Status = Unavailable` outcome. Providers never
return `Unknown`; an observed `Unknown`/default outcome is a provider or test
double contract defect and fails closed. Corrupt negative values, schema drift,
or other persisted/contract invariant failures propagate through the existing
provider contract; they are not converted to `Unavailable` and are not silently
normalized. Malformed typed scope is rejected before provider I/O.

### 9.4 Commit unknown

The existing top-level coordinator remains the authority for ambiguous COMMIT
acknowledgement. Phase 9d does not blindly retry a Save after commit unknown.

With no concurrent writer, fresh observation may show either complete outcome:

```text
old entity snapshot + old generation
new entity snapshot + new generation
```

It must never show a committed one-sided pair:

```text
new entity snapshot + old generation
old entity snapshot + new generation
```

With concurrent writers, observation may instead show a different complete
winner pair. The generation is not sufficient to claim whether the ambiguous
caller or a different concurrent writer won. Reconciliation may report observed
authority state, but cannot report caller success from generation movement
alone. OVG11 excludes concurrent writers so its exact oracle is old pair or the
ambiguous caller's new pair.

---

## 10. Organization Hierarchy Snapshot

### 10.1 Snapshot contents

One cache entry represents the complete explicit-tenant Organization Unit
read-set at one observed generation:

```text
OrganizationHierarchySnapshot
    Generation
    canonical detached units
    typed scoped-key -> unit map
    typed scoped-parent-key -> ordered child keys map
```

The snapshot is internal to `CrestCreates.Organization`. No cache DTO or
provider type is added to Organization Abstractions.

The cache never exposes its stored entity instances. Ancestor/descendant
results retain current detached `Snapshot()` behavior and canonical traversal
order.

### 10.2 Existing hierarchy semantics remain unchanged

- `GetAncestorsAsync` returns parent-to-root order.
- `GetDescendantsAsync` retains deterministic breadth-first order.
- the queried unit itself is not a descendant;
- `IsDescendantOfAsync(x, x)` remains false;
- missing parents stop traversal and do not fail Save;
- cycles fail with `OrganizationHierarchyException` when encountered by the
  requested traversal;
- snapshot construction does not introduce eager whole-tenant relationship
  validation;
- membership reads used by `IsUserInOrganizationAsync` remain direct;
- `IsUserInDescendantOrganizationAsync` combines direct membership authority
  with the generation-validated hierarchy snapshot.

### 10.3 Bounded local ownership

The implementation uses one process-local typed cache owner registered by
`AddOrganizationKernel`. It must have a bounded entry count and an eviction
policy. Eviction/idle-expiration settings are resource governance only; no TTL
appears in correctness assertions.

The owner distinguishes ordinary snapshot payload from the safety state needed
to interpret it. Each observed scope has these semantic values, whether or not
the implementation stores them as separate fields:

```text
ObservedHighWater
    = highest valid Available(G) observed by this process
    = advances at generation observation, before data load/publication
    = never decreases during the process lifetime
    = absent before the first valid observation; the first Available(G) sets it

QuarantineFloor
    = ObservedHighWater captured when NORMAL first detects G < ObservedHighWater
    = present only while QUARANTINED
    = does not rise merely because a later recovery generation is observed
```

Ordinary snapshot eviction cannot decrease `ObservedHighWater` or silently
clear an active `QuarantineFloor`. If the bounded owner cannot read or retain
required safety state, it fails closed rather than admitting a direct authority
fallback. The implementation plan must freeze the bounded retention/admission
mechanism and prove that capacity pressure cannot forget a higher observation
or turn a quarantined scope back into a normal scope. Process restart remains
the only non-recovery reset described in §11.1.

Only one `IOrganizationHierarchyService` production registration exists after
cutover. An uncached algorithm may remain as an internal test/core helper, but
DI must not expose parallel cached and uncached production mainlines.

---

## 11. Hierarchy Read Algorithm

For a non-null tenant:

```text
1. Validate tenant/query inputs.
2. Read explicit tenant-scope generation outcome.
3. If Status is Unknown/default/invalid, fail with
   InvalidGenerationOutcome; perform no fallback I/O.
4. Atomically apply the outcome to local freshness safety state.
5. If Unavailable:
   a. QUARANTINED -> fail with QuarantinedGenerationUnavailable; no fallback.
   b. NORMAL -> capture current ObservedHighWater (including absent), bypass
      cache, read authority directly without publication, and atomically require
      both NORMAL and the same ObservedHighWater before logically completing the
      caller.
6. Otherwise let G = Available.Generation.
7. In NORMAL with prior ObservedHighWater O (if absent, set O = G and continue):
   a. G < O -> set QuarantineFloor = O, enter QUARANTINED, and fail with
      GenerationRegression; perform no authority-data load.
   b. G >= O -> set ObservedHighWater = G before cache equality/load logic.
8. In QUARANTINED with ObservedHighWater O and QuarantineFloor F:
   a. G < O -> remain quarantined and fail with GenerationRegression.
   b. G <= F -> remain quarantined and fail with GenerationRegression.
   c. G > O -> set ObservedHighWater = G and make G eligible for recovery load.
   d. G == O and G > F -> make G eligible to retry recovery load.
9. In NORMAL, if entry.Generation == G, build the result from that snapshot and
   proceed to the final caller-completion safety check.
10. Otherwise join/create local single-flight (tenant, G).
11. Flight loads Organization Units from authority and builds candidate(G).
12. Before publication and logical caller completion, atomically re-check the
    current safety state and result generation under the same state owner.
13. A generation-stamped result may publish/return only when its generation
    equals current ObservedHighWater and, while quarantined, is strictly above
    QuarantineFloor. A direct Unavailable fallback may return only while the
    scope is still NORMAL and its admission-time ObservedHighWater is unchanged.
14. Successful non-regressing publication of an eligible recovery candidate
    releases quarantine but retains ObservedHighWater.
15. Complete only the cached, candidate, or direct-fallback result whose final
    caller-completion check won that safety-state validation race.
```

For `tenantId == null`:

```text
bypass generation cache and single-flight
    -> execute current unfiltered Store query
    -> build request-local snapshot
    -> traverse
```

No cross-tenant snapshot is retained.

### 11.1 Observed high-water, quarantine, and monotonic publication

Concurrent reads can observe G41 and G42 and complete out of order. Publication
must compare generations atomically:

```text
current absent or current.Generation < candidate.Generation
    -> candidate may replace current

current.Generation >= candidate.Generation
    -> candidate must not replace current
```

An equal-generation candidate may be discarded in favor of the already
published immutable snapshot only when the scope is not quarantined.

`ObservedHighWater` advances immediately after every valid higher
`Available(G)` observation, before cache equality, authority-data load, or
publication. A later failure does not roll it back. This prevents a cache at
G41 from becoming valid again after the process observed G42 but failed to load
the G42 snapshot.

A lower available authority generation than `ObservedHighWater` is an observed
authority-history regression. The frozen state machine is:

```text
NORMAL with ObservedHighWater O

observe Available(G < O)
    -> diagnose regression
    -> evict/ignore the cached snapshot
    -> QuarantineFloor = O
    -> enter QUARANTINED; retain ObservedHighWater = O
    -> do not load or return direct authority data
    -> fail with OrganizationHierarchyFreshnessException(
         GenerationRegression,
         observedGeneration: G,
         observedHighWaterGeneration: O,
         quarantineFloorGeneration: O)

QUARANTINED with ObservedHighWater O and QuarantineFloor F

    observe Unavailable
        -> remain quarantined
        -> do not perform availability fallback
        -> fail with QuarantinedGenerationUnavailable

    observe Available(G < O)
        -> remain quarantined
        -> do not load or return authority data
        -> fail with GenerationRegression

    observe Available(G <= F)
        -> remain quarantined
        -> do not load or return authority data
        -> fail with GenerationRegression

    observe Available(G > O)
        -> ObservedHighWater = G
        -> load recovery candidate(G)

    observe Available(G == O and G > F)
        -> retry recovery candidate(G)

    recovery load/publication failure
        -> remain quarantined
        -> retain ObservedHighWater and QuarantineFloor
        -> allow a later retry at the same ObservedHighWater when it is > F

    successful non-regressing publication at G == O and G > F
        -> release quarantine
        -> retain ObservedHighWater = O
        -> clear QuarantineFloor
```

The two values deliberately evolve differently. Observing G43 while
quarantined at floor F42 advances `ObservedHighWater` to 43. If loading G43
fails, the next G43 observation is an eligible retry because `G == O` and
`G > F`; the process need not wait for G44. If G44 was observed and its load
failed, a later G43 is rejected because `G < O44`.

An initial regression and a quarantined unavailable/older/floor generation are
known unsafe freshness states, so no hierarchy result is returned even when a
direct authority-data query could technically run. If recovery loading or
snapshot construction fails, quarantine remains and the failure propagates. If
the recovery authority load succeeds but cache publication fails, its
request-local result may be returned only after current-state revalidation; the
quarantine remains and the same highest generation may retry.

Candidate validation and logical caller completion linearize against quarantine
transition under the same per-scope safety-state owner. If a flight for G42 is
running and another request enters quarantine with floor F42 first, the G42
candidate neither publishes nor completes callers successfully. If candidate
completion wins first, that read linearizes before quarantine. An
`Unavailable` direct fallback is likewise rejected if its scope enters
quarantine or advances `ObservedHighWater` before completion. A check performed
only at flight/fallback start or only before cache publication is insufficient.

Process restart clears snapshot, `ObservedHighWater`, and `QuarantineFloor`.
Regression quarantine is a process-local safety defense. After restart,
correctness is re-established against the then-current durable authority;
durable detection of authority-history rollback across restart would require a
durable epoch/fence and is outside Phase 9d.

### 11.2 Generation-read failure taxonomy

Generation observation has six mutually exclusive outcomes:

| Outcome | Required behavior |
|---|---|
| `Available(G)` | atomically retain `max(ObservedHighWater, G)` when non-regressing, then perform NORMAL/QUARANTINED rules |
| `Unavailable`, scope not quarantined | bypass cache, perform one direct authority load, do not cache |
| `Unavailable`, scope quarantined | remain quarantined; fail with `QuarantinedGenerationUnavailable`; no fallback I/O |
| `Unknown`/default/invalid status | fail with `InvalidGenerationOutcome`; no fallback I/O |
| Cancellation | propagate cancellation immediately; perform no fallback I/O |
| Persisted/schema/contract invariant failure | propagate the exact invariant failure; do not downgrade to authority fallback |

Observed generation regression is not `Unavailable`; it follows the explicit
fail-closed quarantine policy in §11.1. The hierarchy service switches on the
status enum exhaustively; an unknown numeric enum value follows the same path
as `Unknown`, never the availability path.

The implementation must not catch an undifferentiated `Exception` around
generation reads and treat every failure as availability. In particular,
`RuntimePersistenceContractException(PersistedInvariantViolation)`, schema
drift, malformed typed scope, and cancellation cannot enter the availability
fallback.

### 11.3 Cache and authority-load failures

- Snapshot-entry lookup failure after local safety state confirms the scope is
  not quarantined: load authority directly, do not use a possibly stale value,
  and do not cache the fallback result.
- Failure to read or retain local `ObservedHighWater`/`QuarantineFloor` safety
  state: fail explicitly; it cannot be treated as an ordinary cache miss or
  authorize direct fallback.
- Generation mismatch followed by authority load failure: propagate the
  authority failure; never serve the previous snapshot.
- Generation unavailable followed by authority load failure: propagate the
  authority failure only for a non-quarantined scope; never serve any cached
  snapshot.
- Quarantined generation unavailable: propagate the explicit freshness
  failure without attempting an authority-data load.
- Generation regression below `ObservedHighWater`, or quarantined generation at
  or below `QuarantineFloor`:
  propagate the explicit freshness failure without attempting an
  authority-data load.
- Authority load failure in single-flight: propagate; store no candidate and
  release ownership.
- Snapshot construction failure: propagate; store no candidate and release
  ownership.
- Cache publication failure after a successful authority load: return the
  request-local authoritative snapshot and continue uncached.
- Ordinary snapshot eviction: behave as a miss while retaining independent
  `ObservedHighWater`/`QuarantineFloor` safety state.

Reference-data availability is preserved only where the authority can still be
read and freshness has not already been disproved. Phase 9d explicitly forbids
stale-if-error: a previous snapshot is never returned after mismatch,
unavailability, invariant failure, or regression; regressed authority data is
also never returned as a substitute.

---

## 12. Per-instance Single-flight

Single-flight is keyed by:

```text
(explicit TenantId, observed Organization Generation)
```

Required semantics:

- concurrent misses for the same key/generation share one local authority
  load;
- different tenants or generations do not share a flight;
- a failed or canceled load is never retained as a result;
- flight ownership is removed after success or failure;
- one waiter's cancellation does not permanently poison the shared key;
- waiter cancellation may detach that waiter without turning a completed value
  into failure for other waiters;
- no distributed lock, lease, fence, or global one-load claim is introduced.

The implementation plan must freeze the exact shared-load cancellation/lifetime
mechanics and prove they are bounded. It must not use an arbitrary first
waiter's cancellation token as permanent shared ownership without a release
test.

---

## 13. Permission Security Cutover

### 13.1 Production read path

`PermissionGrantStore` maps repository results directly. It no longer calls
`PermissionGrantCacheService.GetOrAddAsync`.

The cutover removes from production composition:

- `PermissionGrantCacheService` read/write use;
- `PermissionGrantCacheOptions` as a security behavior switch;
- Grant/Revoke cache removal;
- the unused `TenantCacheKeyContributor` dependency in
  `PermissionGrantManager`.

This is a Permission-cache retirement, not an Authorization-wide caching
removal. `AuditTenantContextResolver` independently consumes
`TenantCacheKeyContributor`, and `AddCrestAuthorization()` currently composes
the caching/key services that satisfy it. The implementation may remove an
assembly/package/DI dependency only after an exact consumer inventory proves no
unrelated production consumer remains. Passing Permission tests alone is not
such proof.

The old cache prefix may contain stale values after deployment. Correctness
does not require a pattern delete because no production code reads that prefix.
An optional one-time cleanup is operational hygiene only and is not part of
acceptance.

No configuration flag may restore unversioned positive caching. A future cache
requires a new approved authority-version contract and a new acceptance review.

### 13.2 Grant and revoke behavior

Grant/Revoke preserve existing normalization, tenant-scope validation,
idempotent Find behavior, repository mutation, and Unit-of-Work semantics. They
do not add generation or events in Phase 9d.

Correctness is intentionally independent of Manager participation. A legal
writer such as `TenantBootstrapper` may mutate through
`IPermissionGrantRepository`, commit without calling
`PermissionGrantManager`, and emit no cache invalidation. A fresh authorization
scope must observe that committed repository state because its read path also
uses authority directly.

After the database commit:

```text
another instance's next permission authority query
    -> cannot receive the revoked row from a stale application cache
```

The database/provider retains responsibility for transaction isolation. A
permission check whose authority observation or database snapshot linearizes
before the revoke commit may still complete with the earlier state; this is not
a stale-cache claim. Multi-instance acceptance creates a fresh authority
scope/transaction after the revoke commit.

### 13.3 Failure posture

- Repository success: evaluate exactly the returned committed grants.
- Repository cancellation: propagate cancellation; no cached fallback.
- Repository/provider failure: propagate/fail closed; no cached fallback.
- `ICrestCacheService` outage: irrelevant to Permission decisions after
  cutover.
- Old positive entry in another process/Redis: ignored.
- TTL not expired: irrelevant.
- SuperAdmin claim behavior remains the existing explicit authorization path
  and is not redefined by cache removal.

### 13.4 Stale Security State closure boundary

Phase 9d closes this exact question:

```text
Does any selected Phase 9 cache/mainline allow stale security-positive
authority state to be accepted?
```

For the selected Organization hierarchy and Permission Grant paths, the
required answer at Phase 9 closure is `No`, so this bounded
`Stale Security State` item may be recorded `CLOSED`.

This does not claim global security-state freshness. Authentication token or
role-claim freshness, SuperAdmin claim revocation, credential/session
revocation, and external identity-provider propagation remain outside #26. It
also does not claim durable detection of a database/authority rollback across
process restart; after restart, the then-current durable store is authority
because Phase 9d defines no durable epoch or fence.

---

## 14. Tenant and Key Isolation

### 14.1 Organization

The cache key contains the exact explicit tenant string using ordinal identity.
The generation authority accepts only `OrganizationScopeIdentity` and uses the
same normalized global/tenant scope rules as #69. Delimiter-based string
composition and nullable generation scope are forbidden; typed key values are
used in Store generation, cache, and single-flight registries.

Same Organization Unit ID in two tenants produces distinct maps, entries,
generations, and flights. A Save in tenant A cannot advance tenant B's
generation.

### 14.2 Permission

Permission Grant rows retain existing ProviderType/ProviderKey and
Global/Tenant scope semantics. Tenant filtering occurs over the current
authority result. Removing the cache does not broaden tenant visibility or
change the Permission key model.

---

## 15. System Invariants

1. Cache is never authority.
2. Every Organization hierarchy cache hit requires equality with the current
   authoritative tenant-scope generation.
3. Every successful Organization Save and its generation advance are one
   authority commit.
4. Generation is a freshness stamp only and cannot reject or authorize a Save.
5. A cache miss reads generation before authority data; old data is never
   stamped with a generation observed after that data read.
6. Event receipt is unnecessary for detecting a stale Organization snapshot.
7. Delayed loads/observations cannot replace a newer cached generation.
8. Tenant identity participates in Store generation, cache, snapshot-map, and
   single-flight keys.
9. Null-tenant unscoped Organization collection reads are never cached.
10. Only an explicit typed generation `Unavailable` outcome on a
    non-quarantined scope authorizes direct authority fallback; `Unknown`,
    cancellation, and invariant failures propagate without fallback.
11. `default(OrganizationScopeGenerationRead)` is `Unknown`, fails as an invalid
    generation outcome, and cannot authorize availability fallback.
12. Every valid higher `Available(G)` observation advances
    `ObservedHighWater` before cache/data loading; later failure never rolls it
    back or makes an older cache entry valid again.
13. Generation mismatch or non-quarantined unavailability followed by authority
    failure never serves the previous cached snapshot.
14. Observed generation regression below `ObservedHighWater` captures that
    value as `QuarantineFloor`, quarantines the local scope, returns no
    cached or direct authority hierarchy result, and fails explicitly.
15. `ObservedHighWater` never decreases; `QuarantineFloor` remains the entry
    boundary and is not raised merely by a failed higher recovery observation.
16. Quarantine is sticky across `Unavailable`, generation below
    `ObservedHighWater`, and generation at/below `QuarantineFloor`.
17. A quarantined scope with unavailable generation performs no direct
    authority-data fallback.
18. A recovery generation equal to `ObservedHighWater` and above
    `QuarantineFloor` remains retryable after failed recovery load/publication;
    only successful non-regressing publication releases quarantine.
19. Every cached/candidate result revalidates current safety state at logical
    caller completion; a quarantine transition that wins the race prevents an
    in-flight candidate at/below its floor from publishing or returning, and a
    direct `Unavailable` fallback returns only if NORMAL and
    `ObservedHighWater` are unchanged since admission.
20. Known pre-commit failure/rollback changes neither data nor generation;
    COMMIT unknown may expose a complete pre-commit or complete committed winner
    pair but never a one-sided committed pair.
21. Failed authority loads are not cached and release single-flight ownership.
22. Single-flight controls local load amplification, not correctness.
23. Cache resource expiration/eviction never defines freshness.
24. Permission authorization never reads the old unversioned positive cache.
25. Permission authority failure cannot fall back to a stale grant.
26. A committed Permission mutation requires no Manager participation or cache
    invalidation event to become observable by a fresh application instance.
27. Permission cache retirement does not remove unrelated Authorization
    caching/key dependencies without independent consumer proof.
28. A committed Permission revoke requires no cache invalidation event to
    become observable by another application instance.
29. No `DataPermissionScope` becomes cached or durable authority.
30. No selected path introduces runtime reflection, untyped persisted JSON, or
    a new shared-cache provider.
31. #25 transaction boundaries are not reopened for cache events.
32. H3 observations never substitute for #26 product acceptance.

---

## 16. Core Composition Cases

### 16.1 Organization missed-event case

```text
PostgreSQL authority = V1 / Generation 41

Instance A local snapshot = V1 / 41
Instance B local snapshot = V1 / 41

Instance A Save commits atomically:
    V2 + Generation 42

No invalidation or change event is delivered.

A read
    -> authority generation 42
    -> rejects 41
    -> reloads V2

B read
    -> authority generation 42
    -> rejects 41
    -> reloads V2
```

Required result: independent caches converge through shared authority
generation validation.

### 16.2 Delayed-load publication case

```text
Read R1 observes G41 and begins load
Save commits V2 / G42
Read R2 observes G42, loads V2, publishes G42
R1 completes later

R1 candidate must not replace G42
```

No event API is needed to prove non-regression.

### 16.3 Permission stale-positive case

```text
Old deployment/cache contains Role:Librarian -> Books.Delete
Instance A commits revoke
cache invalidation is lost or cache provider is unavailable

Instance B permission check
    -> does not query PermissionGrantCacheService
    -> queries committed grant authority
    -> Books.Delete absent
    -> deny
```

The stale entry may physically remain; it has no authority path.

### 16.4 Permission writer bypasses Manager

```text
Instance B has historical cached state

Instance A
    -> IPermissionGrantRepository mutation
    -> COMMIT
    -> no PermissionGrantManager
    -> no cache invalidation

Instance B fresh authorization scope
    -> ignores historical cache
    -> reads repository authority
    -> observes committed mutation
```

Required result: a legal writer does not need cache knowledge for correctness.

### 16.5 Generation mismatch followed by authority failure

```text
local cache = V1 / G41
authority commits V2 / G42

read generation -> Available(42)
authority snapshot load -> availability failure

required result
    -> propagate failure
    -> do not return V1 / G41
    -> clear single-flight ownership
```

This case prohibits stale-if-error after freshness has already been disproved.

### 16.6 Generation regression is security fail-closed

```text
ObservedHighWater = G42
authority generation observation = G41
G41 hierarchy could reintroduce a removed descendant

required result
    -> evict/ignore the cached snapshot
    -> record QuarantineFloor = G42
    -> perform no hierarchy authority-data read
    -> return no hierarchy result
    -> raise OrganizationHierarchyFreshnessException(GenerationRegression)
```

The direct authority fallback used for a normal `Unavailable` outcome is not
legal after freshness is known-invalid.

### 16.7 Quarantine cannot be bypassed by availability failure

```text
local scope is quarantined with ObservedHighWater = G42,
QuarantineFloor = G42
next generation outcome = Unavailable

required result
    -> retain ObservedHighWater and QuarantineFloor
    -> perform no hierarchy authority-data read
    -> return no hierarchy result
    -> raise OrganizationHierarchyFreshnessException(
         QuarantinedGenerationUnavailable)
```

### 16.8 Failed higher-generation load retains observation

```text
cache = V1 / G41
ObservedHighWater = G41

read #1 observes G42
    -> ObservedHighWater = G42 before load
    -> G42 authority-data load fails
    -> failure propagates; V1 / G41 is not returned

read #2 observes G41
    -> G41 < ObservedHighWater G42
    -> QuarantineFloor = G42
    -> enter quarantine and fail closed
    -> V1 / G41 does not become valid again
```

### 16.9 Failed recovery can retry the same highest generation

```text
QUARANTINED: ObservedHighWater = G42, QuarantineFloor = G42

observe G43
    -> ObservedHighWater = G43
    -> recovery load fails
    -> retain O43 / F42 and quarantine

observe G43 again
    -> G == ObservedHighWater and G > QuarantineFloor
    -> retry is eligible
    -> successful non-regressing publication releases quarantine
```

No G44 mutation is required to recover from the transient G43 load failure.

### 16.10 Quarantine wins against an older in-flight candidate

```text
R1 observes G42
    -> ObservedHighWater = G42
    -> candidate(G42) load is blocked

R2 observes G41
    -> G41 < O42
    -> enter quarantine with F42

R1 candidate(G42) completes
    -> atomically re-check current O42 / F42 / QUARANTINED
    -> candidate.Generation <= QuarantineFloor
    -> candidate neither publishes nor completes caller successfully
    -> freshness failure propagates
```

The candidate completion and quarantine transition use the same per-scope
safety-state ordering. Only the operation that wins that ordering may define
the read's linearization point.

---

## 17. Acceptance Case Matrix

| ID | Category | Case | Required result | Runner |
|---|---|---|---|---|
| OVG01 | Authority | initial scope generation | absent scope reads `0` | shared InMemory/PostgreSQL kit |
| OVG02 | Authority | Organization Unit Save | snapshot and generation advance together | shared kit |
| OVG03 | Authority | Position Save | same scope generation advances | shared kit |
| OVG04 | Authority | Membership Save | same scope generation advances | shared kit |
| OVG05 | Authority | Role Assignment Save | same scope generation advances | shared kit |
| OVG06 | Authority | known pre-commit failure / known rollback | neither data nor generation advances | shared kit + PostgreSQL failure |
| OVG07 | Authority | same logical key in two tenants | generations remain isolated | shared kit |
| OVG08 | Authority | repeated blind Save | each successful commit advances; no optimistic concurrency | shared kit |
| OVG09 | Provider | V012 upgrade to V013 | existing rows read generation `0`; first Save reaches `1` | PostgreSQL |
| OVG10 | Provider | generation overflow/corruption | complete Save/read fails closed without wrap | provider-focused |
| OVG11 | Provider | COMMIT acknowledgement unknown | fresh observation is complete old pair or complete new pair; never one-sided | real PostgreSQL commit-unknown |
| OVG12 | Contract | invalid/default/Unknown generation scope | rejected before provider I/O; collection null semantics are not reused | shared kit |
| OHC01 | Cache | same-generation hierarchy hit | Store collection loaded once and snapshot reused | Organization unit |
| OHC02 | Cache | generation changes | old entry rejected and authority reloaded | Organization unit |
| OHC03 | Cache | null tenant | unscoped query bypasses cache every time | Organization unit |
| OHC04 | Cache | same unit ID in two tenants | no map/cache/flight collision | Organization unit |
| OHC05 | Cache | delayed G41 load after G42 publication | G42 remains cached | deterministic concurrency |
| OHC06 | Cache | concurrent same tenant/generation miss | one authority load per instance | deterministic concurrency |
| OHC07 | Cache | concurrent different generation | flights remain separate; newer publication wins | deterministic concurrency |
| OHC08 | Failure | generation outcome is `Unavailable` on a normal scope | direct authority result only if NORMAL/`ObservedHighWater` remain as admitted; no cached fallback/publication | fault driver |
| OHC09 | Failure | ordinary snapshot cache read/write fails after non-quarantined safety state is known | direct/request-local authority result | fault driver |
| OHC10 | Failure | generation mismatch then authority load fails | previous snapshot is not served; failure not cached; observed generation is retained and same-generation next request may retry | fault driver |
| OHC11 | Cancellation | one waiter cancels | other waiters can complete; ownership releases | deterministic concurrency |
| OHC12 | Semantics | missing parent/cycle/order/detached result | existing hierarchy contract preserved | shared semantic suite |
| OHC13 | Failure | generation invariant/schema/contract failure | exact failure propagates; no direct-read fallback | fault driver |
| OHC14 | Cancellation | generation read is cancelled | cancellation propagates; no fallback I/O | fault driver |
| OHC15 | Failure | normal scope generation unavailable then authority fails | cached snapshot is not served | fault driver |
| OHC16 | Regression | observed generation is below `ObservedHighWater` | no cached or direct authority result; explicit regression failure; `QuarantineFloor` captures current `ObservedHighWater` | deterministic concurrency |
| OHC17 | Regression | quarantined scope observes generation `Unavailable` | no authority-data fallback; explicit quarantined-unavailable failure; quarantine remains | fault driver |
| OHC18 | Contract | generation outcome is default/`Unknown` | invalid-outcome failure; no cache or authority-data fallback | fault driver |
| OHC19 | Recovery | quarantined scope observes eligible generation above `QuarantineFloor` | successful non-regressing publication releases quarantine and retains `ObservedHighWater`; at/below floor never releases it | deterministic concurrency |
| OHC20 | Resource safety | snapshot eviction/capacity pressure while scope is quarantined | quarantine remains effective or owner fails closed; direct fallback is never re-enabled | fault driver |
| OHC21 | Resource safety | local `ObservedHighWater`/`QuarantineFloor` safety state cannot be read or retained | explicit failure; no cache or authority-data fallback | fault driver |
| OHC22 | Composition | higher generation is observed and its authority-data load fails | failure propagates; higher `ObservedHighWater` remains; an older next observation cannot revalidate old cache | deterministic concurrency |
| OHC23 | Composition | recovery generation advances `ObservedHighWater`, then recovery load fails | quarantine/floor remain; the same highest generation above floor is eligible to retry and publish | deterministic concurrency |
| OHC24 | Composition | candidate load starts before a regression enters quarantine and completes afterward | completion re-check rejects it; candidate neither publishes nor returns to caller | deterministic concurrency |
| OMI01 | Multi-instance | both instances miss all events | both reject V1 through PostgreSQL G42 | real PostgreSQL |
| OMI02 | Multi-instance | independent local caches | correctness does not require cache sharing | real PostgreSQL |
| PSC01 | Security | old positive entry exists after revoke | production check ignores it and denies | integration |
| PSC02 | Security | cache invalidation throws/is unavailable | grant/revoke/check correctness is unaffected | integration |
| PSC03 | Security | two instances share committed authority | fresh post-commit authority scope rejects revoke | real EF provider |
| PSC04 | Security | permission repository unavailable | no stale positive fallback; check fails closed | integration |
| PSC05 | Security | tenant/global grant filtering | existing scope semantics remain exact | integration |
| PSC06 | Composition | DI graph after cutover | Permission read path has no cache dependency | composition/boundary |
| PSC07 | Composition | legal repository writer bypasses Manager | fresh authorization observes commit without invalidation | real EF provider |
| PSC08 | Composition | Permission cache services are retired | unrelated Authorization caching/key consumer remains constructible and preserves behavior | composition/boundary |
| AOT01 | NativeAOT | V013 + two local hierarchy caches | original native binary proves generation reload | PostgreSQL AOT fixture |
| H301 | Harness | reuse observation | value/noise/cost/context/defects verdict recorded | review sidecar |

An optional #25 event-composition case is deliberately absent because this
phase selects no legal reliable producer seam. That absence is a design result,
not missing acceptance.

---

## 18. Normative Acceptance Test Skeleton

### Authority and provider parity

- `OrganizationScopeGeneration_Should_StartAtZero`
- `OrganizationWrite_Should_Atomically_AdvanceGeneration`
- `OrganizationSaveSurface_Should_AdvanceSharedScopeGeneration`
- `KnownPreCommitFailure_Should_AdvanceNeitherDataNorGeneration`
- `CommitUnknown_Should_NeverProduce_OneSided_Data_And_Generation`
- `OrganizationScopeIdentity_Should_Reject_DefaultUnknownAndInvalidTenant`
- `Generation_Should_Not_Change_DomainBlindWriteSemantics`
- `TenantGeneration_Should_Not_Affect_OtherTenants`
- `V013Upgrade_Should_PreserveV012Rows_AtGenerationZero`

### Hierarchy cache

- `OrganizationHierarchy_Should_Reuse_CurrentGenerationSnapshot`
- `OrganizationHierarchy_Should_Reload_WhenGenerationChanges`
- `OrganizationTenantCache_Should_Not_Cache_UnscopedCrossTenantQuery`
- `MissedInvalidation_Should_Not_Preserve_StaleOrganizationCache`
- `DelayedOlderLoad_Should_Not_RegressFreshness`
- `ConcurrentMiss_SameKeyAndGeneration_Should_SingleFlight_PerInstance`
- `FailedAuthorityLoad_Should_ClearSingleFlight`
- `GenerationMismatch_AuthorityLoadFailure_Should_NotServePreviousSnapshot`
- `GenerationUnavailable_OnNormalScope_Should_BypassCache_And_NotServeCachedSnapshot`
- `GenerationUnavailable_OnNormalScope_AuthorityLoadFailure_Should_NotServeCachedSnapshot`
- `GenerationInvariantFailure_Should_NotDowngrade_ToAuthorityFallback`
- `GenerationCancellation_Should_Propagate_WithoutFallbackIO`
- `GenerationRegression_Should_FailClosed_AndQuarantineScope`
- `QuarantinedScope_GenerationUnavailable_Should_NotFallbackToAuthority`
- `DefaultGenerationOutcome_Should_NotAuthorizeAvailabilityFallback`
- `QuarantinedScope_Should_ReleaseOnlyAfterEligibleGenerationPublication`
- `QuarantineCapacityPressure_Should_NotReenableAuthorityFallback`
- `FreshnessSafetyStateFailure_Should_NotAuthorizeAuthorityFallback`
- `ObservedHigherGeneration_LoadFailure_Should_PreserveObservedHighWater`
- `QuarantineRecoveryFailure_Should_AllowRetryAtSameHighestRecoveryGeneration`
- `InFlightCandidate_Should_NotReturn_AfterScopeEntersQuarantine`
- `CancelledWaiter_Should_Not_PoisonSharedFlight`
- `OrganizationSnapshotCacheFailure_OnNormalScope_Should_FallbackToAuthority`
- `HierarchyCache_Should_PreserveOrderingCyclesMissingParentsAndDetachedReads`

### Permission security

- `PermissionGrantStore_Should_ReadAuthority_WithoutCache`
- `PermissionRevocation_Should_NeverServe_StaleGrant`
- `PermissionOldPositiveCache_Should_BeIgnored_AfterCutover`
- `PermissionCacheFailure_Should_Not_AffectAuthorizationDecision`
- `PermissionAuthorityFailure_Should_NotFallbackToStalePositive`
- `MultiInstance_PermissionCheck_Should_ObserveCommittedRevoke`
- `PermissionRepositoryWriter_Should_BeObserved_WithoutCacheInvalidation`
- `PermissionCutover_Should_PreserveTenantAndGlobalScopeFiltering`
- `AddCrestAuthorization_Should_NotCompose_UnversionedPermissionCachePath`
- `PermissionCacheRetirement_Should_Preserve_UnrelatedAuthorizationCachingConsumers`

### Multi-instance and NativeAOT

- `MultiInstance_HierarchyCache_Should_ValidateSharedAuthorityGeneration`
- `MultiInstance_HierarchyCache_Should_NotRequireSharedCacheOrEvent`
- `VersionedOrganizationCacheAotFixture_Should_PublishLinkAndRun`

---

## 19. Required Test Architecture

### 19.1 Runner-free provider contract kit

Extend the existing runner-free Organization Store testing pattern. Shared
cases consume `IOrganizationStore` and execute the same generation semantics
through:

- InMemory driver;
- PostgreSQL direct-Npgsql driver.

The shared kit owns semantic assertions. Provider wrappers own setup, cleanup,
failure injection, restart, migration, and evidence provenance. No generic
reflection runner is introduced.

### 19.2 Cache contract driver

Hierarchy cache behavior requires a provider-neutral test driver capable of:

- counting generation and collection reads;
- blocking a specific generation/load phase;
- returning each typed generation status, including
  `default(OrganizationScopeGenerationRead)`/`Unknown`;
- throwing cancellation and persisted/schema/contract invariant failures
  independently;
- producing a lower generation than `ObservedHighWater`;
- returning `Unavailable` after the scope has entered quarantine;
- advancing `ObservedHighWater` before a deliberately failed data load;
- retrying the same highest recovery generation above `QuarantineFloor`;
- blocking an in-flight candidate, entering quarantine from another request,
  then releasing the candidate and observing publication/caller completion;
- observing publication, quarantine release, and logical caller-completion
  ordering without timing guesses;
- failing authority, ordinary snapshot read/write, or freshness-safety-state
  read/retention independently;
- constructing independent cache instances over one shared authority;
- observing publication generation without exposing production internals as a
  public framework API.

Test hooks remain internal/friend-only. Production contracts are not widened
for test convenience.

### 19.3 Real multi-instance topology

Organization multi-instance acceptance uses:

```text
one PostgreSQL 16 schema / authority
    + process/service-provider A with local cache A
    + process/service-provider B with local cache B
    + no invalidation delivery
```

Two dictionaries over an InMemory Store cannot satisfy OMI01/OMI02.

Permission security acceptance uses two independently scoped application/
DbContext instances against one real committed EF authority. The canonical
runner uses EF Core against PostgreSQL and remains distinct from the
direct-Npgsql Runtime Store fixture. The test may seed an old cache backend with
a positive value, but production authorization must not query it.

### 19.4 Existing semantic regression suites

The current #69 Organization Store and hierarchy suites remain authoritative
for:

- null/unfiltered scope asymmetry;
- typed scoped-key collision safety;
- canonical order;
- detached snapshots;
- missing references;
- cycle behavior;
- blind replacement semantics.

Phase 9d extends rather than duplicates those cases.

---

## 20. NativeAOT and Capability Evidence

Extend the existing
`CrestCreates.Runtime.Persistence.PostgreSql.AotHost` and its fixture. Do not
create a parallel cache-only AOT product.

The native scenario must:

1. publish with `-p:CrestCreatesPublishMode=aot` for linux-x64;
2. complete native link;
3. execute the original native artifact;
4. apply and validate V013 against real PostgreSQL;
5. create two independent local hierarchy cache owners over the same Store;
6. cache Organization V1/generation 1 in both;
7. Save V2/generation 2 without delivering an event;
8. prove both owners reject generation 1 and return detached V2 results;
9. prove the null-tenant unscoped path was not retained;
10. emit and assert the exact marker:

```text
CRESTCREATES_VERSIONED_ORGANIZATION_CACHE_OK
```

The existing fixture's prior markers remain required. The Permission cutover is
verified in its actual EF provider/JIT capability tier; it does not inherit a
NativeAOT claim from this Runtime fixture.

---

## 21. #68 H3 Reuse Observation

Implementation/closure produces an Issue-local sidecar, expected at:

```text
docs/review/phase-9d-h3-reuse-observations.md
```

For each admitted check, record:

| Field | Required observation |
|---|---|
| Check | NativeAOT fixture / runner-free provider kit / dependency boundaries |
| Value | Work or evidence reuse saved/strengthened |
| Misses | Relevant #26 defects the check could not express |
| Noise | False positives, orchestration-only failures, irrelevant reruns |
| Runtime Cost | Local/CI wall-clock and expensive dependencies |
| Maintenance Cost | Code, fixture, CI, and ownership upkeep introduced |
| Context Required | Architecture knowledge needed to interpret failure |
| Defects Actually Caught | Concrete defects caught, distinct from passing |
| Verdict | Keep / Narrow / Drop / Remain Review-only |

The valid conclusion may be `Reusable: yes; Useful: no`. Product acceptance
remains owned by the cases in this Spec. The Phase 9c 444-tuple ledger stays
disabled and outside H3 reuse.

---

## 22. Rejected Approaches

### 22.1 TTL plus invalidation

Rejected because a missed revoke invalidation preserves a positive grant until
TTL expiry and a missed Organization invalidation preserves a stale hierarchy.

### 22.2 Re-enable Permission cache behind an `Enabled` option

Rejected because configuration cannot create an atomic authority version. An
unsafe opt-in would preserve a second production mainline and make the security
contract deployment-dependent.

### 22.3 Add Permission generation in Phase 9d

Rejected for this phase because the current authority has multiple Host/model/
migration and generic-writer seams. Direct reads close the actual security gap
without pretending those seams have been atomically versioned. A later phase
may introduce a version only after it makes every legal writer and provider
participate in one tested authority contract.

### 22.4 Put Organization generation in a disconnected cache service

Rejected because the Store could be replaced independently in DI, breaking the
atomic owner relationship. Generation belongs to the Store provider contract.

### 22.5 Multiple Organization generation streams immediately

Rejected as premature. One broader scope generation is simpler and supports a
future Identity read model. Conservative hierarchy reloads are accepted until
measurements prove stream splitting is needed.

### 22.6 Shared Redis cache as the primary Organization cache

Rejected because shared cache is unnecessary for correctness or the declared
single-flight boundary and would add serialization/provider/AOT obligations.

### 22.7 Distributed lock for cache fill

Rejected because duplicate authority loads across processes are allowed. A
lock would add availability and fencing complexity without improving
correctness.

### 22.8 Publish Organization Outbox facts automatically

Rejected because #69 reference-data Saves are explicit top-level commits and
#25 forbids automatic enlistment. No freshness event is needed.

### 22.9 Cache the unscoped Organization collection

Rejected because null means unfiltered across global and all tenant rows. It is
outside the tenant-local cache boundary and creates avoidable isolation and
cardinality risk.

### 22.10 Persist or cache `DataPermissionScope`

Rejected because it is derived authorization context, not authority.

---

## 23. Implementation Review Guardrails

| Question | Required answer |
|---|---|
| Is cache ever accepted without an authority-generation equality check? | No |
| Is generation read after the miss authority value and then attached to it? | No |
| Does generation authority accept nullable tenant scope? | No |
| Are Global and Tenant generation scopes created through an explicit typed identity? | Yes |
| Do all four Organization Save surfaces advance the same scope generation? | Yes |
| Can data commit without generation or generation without data? | No |
| Is COMMIT unknown treated as known rollback or automatically retried? | No |
| Can COMMIT-unknown observation expose a one-sided data/generation pair? | No |
| Does generation change blind-write concurrency semantics? | No |
| Can a tenant Save advance another tenant's generation? | No |
| Can a delayed older load replace a newer snapshot? | No |
| Is null-tenant collection data retained in the cache? | No |
| Can cache failure return a stale entry? | No |
| Can generation mismatch plus authority failure return the previous entry? | No |
| Can generation cancellation trigger fallback I/O? | No |
| Can persisted/schema/contract generation failure be downgraded to availability? | No |
| Can `default`/`Unknown` generation outcome authorize availability fallback? | No |
| Does every valid higher `Available(G)` advance `ObservedHighWater` before data load/publication? | Yes |
| Can failed data load/publication roll back `ObservedHighWater`? | No |
| Can observed generation regression serve/publish the cached `ObservedHighWater` value? | No |
| Can observed generation regression return direct authority hierarchy data? | No |
| Can a quarantined scope use `Unavailable` to fall back to authority data? | No |
| Can `Available(G <= QuarantineFloor)` release quarantine? | No |
| Must a failed recovery at current `ObservedHighWater` wait for a still higher generation before retry? | No |
| Can a recovery generation equal to `ObservedHighWater` and above `QuarantineFloor` retry? | Yes |
| Does quarantine release before an eligible non-regressing generation is successfully published? | No |
| Do candidate publication and logical caller completion re-check current safety state? | Yes |
| Can an in-flight candidate at/below a newly established `QuarantineFloor` publish or return? | No |
| Can an `Unavailable` direct fallback return after quarantine or `ObservedHighWater` changes? | No |
| Can snapshot eviction or capacity pressure silently clear quarantine? | No |
| Can failed load remain the single-flight result? | No |
| Does single-flight claim cluster-wide one-load behavior? | No |
| Does cache TTL define correctness? | No |
| Does production Permission checking still call `PermissionGrantCacheService`? | No |
| Can configuration re-enable the old unversioned Permission cache? | No |
| Can Permission authority failure fall back to an old positive entry? | No |
| Does Permission bypass change tenant/global filtering? | No |
| Is old cache cleanup required for correctness? | No |
| Can Permission retirement remove unrelated `AuditTenantContextResolver` caching/key composition without consumer proof? | No |
| Can a direct `IPermissionGrantRepository` writer require invalidation for correctness? | No |
| Is Organization Identity cache mandatory? | No |
| Is Data Permission Rule cache implemented? | No |
| Is `DataPermissionScope` persisted/cached? | No |
| Is Redis/shared cache claimed or required? | No |
| Is a distributed lock introduced? | No |
| Is #25 reopened to produce cache events? | No |
| Is durable authority-history rollback detection across process restart claimed? | No |
| Does the original native binary execute the Organization V013/cache path? | Yes |
| Is EF Permission AOT support inferred from that fixture? | No |
| Are H3 observations separate from product acceptance? | Yes |
| Is the incomplete Phase 9c tuple ledger promoted? | No |

---

## 24. Exit Criteria

Phase 9d closes only when all are true:

1. The inventory decision is explicit and deferred caches are not treated as
   missing implementation.
2. `IOrganizationStore` exposes one documented generation contract accepting
   only explicit `OrganizationScopeIdentity`; nullable/default scope is not a
   valid call path.
3. InMemory and PostgreSQL run the same all-Save-surface generation cases.
4. PostgreSQL V013 is appended to the checksummed catalog and exact schema
   manifest.
5. Organization snapshot mutation and generation advancement are atomic under
   success and every known pre-commit failure/rollback case.
6. COMMIT unknown is never automatically retried and fresh observation proves
   only a complete old pair or complete new pair, never one-sided data and
   generation.
7. Existing #69 blind-write semantics remain unchanged.
8. The production hierarchy service reuses an immutable tenant snapshot only
   after authoritative generation equality.
9. Miss load ordering cannot stamp old authority data with a newer generation.
10. Delayed older loads cannot regress a newer cached snapshot.
11. Null-tenant unscoped Organization collection reads bypass cache retention.
12. Tenant keys, generations, maps, and flights remain isolated.
13. Per-instance single-flight shares only same tenant/generation loads and
    releases ownership after success, failure, and waiter cancellation cases.
14. Only explicit typed generation `Unavailable` on a non-quarantined scope
    falls back to direct Organization authority; `Unknown`/default, cancellation,
    and persisted/schema/contract invariant failure propagate without fallback
    I/O.
15. `default(OrganizationScopeGenerationRead)` cannot authorize availability
    fallback and produces the explicit invalid-outcome failure.
16. Generation mismatch or non-quarantined unavailability followed by authority
    failure never serves a previous snapshot.
17. Every valid higher generation observation advances `ObservedHighWater`
    before data load/publication; failure retains that observation and an older
    next observation cannot revalidate an older snapshot.
18. Observed regression below `ObservedHighWater` returns neither cached nor
    direct authority hierarchy data, raises the explicit regression failure,
    and captures current `ObservedHighWater` as `QuarantineFloor`.
19. A quarantined scope remains fail-closed when generation is `Unavailable`,
    below `ObservedHighWater`, or at/below `QuarantineFloor`; none performs
    authority-data fallback or releases quarantine, and snapshot eviction/
    capacity pressure cannot silently normalize the scope.
20. A failed recovery retains both values; the same generation may retry when
    it equals `ObservedHighWater` and is strictly above `QuarantineFloor`.
21. Successful non-regressing publication of that eligible generation releases
    quarantine, retains `ObservedHighWater`, and clears `QuarantineFloor`.
22. Candidate publication and logical caller completion atomically re-check
    current safety state; a candidate at/below a quarantine floor established
    while it was in flight neither publishes nor returns, and an `Unavailable`
    direct fallback returns only if NORMAL and `ObservedHighWater` remain as
    admitted.
23. Regression detection is process-local; no durable authority-history epoch,
    fencing, or rollback detection across process restart is claimed.
24. Existing hierarchy ordering, missing-parent, cycle, and detached-result
    semantics pass unchanged.
25. Two independent local caches over one real PostgreSQL authority reject a
    stale snapshot without any event delivery.
26. Permission production reads no longer depend on
    `PermissionGrantCacheService` or cache invalidation.
27. No runtime option can re-enable unversioned positive Permission caching.
28. A committed revoke is not served by another instance's fresh authority
    scope from an old positive entry, regardless of TTL or invalidation failure.
29. A legal direct `IPermissionGrantRepository` writer is observed by a fresh
    authorization scope without Manager participation or invalidation.
30. Permission provider failure never falls back to a stale positive grant.
31. Existing Permission tenant/global scope and SuperAdmin behavior remain
    unchanged outside the removed cache lane.
32. `AuditTenantContextResolver` and every unrelated Authorization caching/key
    consumer remain buildable/constructible and preserve their behavior after
    Permission-only cache retirement.
33. `Stale Security State CLOSED` is claimed only for the selected Phase 9
    cache/mainline security-positive authority boundary defined in §13.4; no
    credential/session/token/role-claim/external-identity freshness claim is
    made.
34. No Organization Identity, Data Permission Rule, Descriptor, derived scope,
    negative-cache subsystem, distributed lock, or shared-cache backend enters
    the implementation boundary.
35. The existing PostgreSQL native fixture applies V013 and executes the real
    two-cache missed-event scenario in the original binary.
36. Dependency-boundary tests remain green and no forbidden dependency is added.
37. The runner-free provider kit is extended without a generic Harness runner.
38. The H3 sidecar records value, misses, noise, runtime/maintenance cost,
    context, defects caught, and a verdict for all three reused checks.
39. The incomplete Phase 9c exact-tuple ledger remains outside closure claims.
40. A #26 product review passes before the #68 H3 reuse review.
41. The Phase 9 Overall Closure Review follows H3 and explicitly judges:

```text
Authority
Responsibility
Durable Commit
Crash Recovery
Reliable Consequences
Concurrent Ownership
Cache Freshness
Stale Security State
NativeAOT
Provider Parity
```

In that review, `Stale Security State` means exactly the bounded §13.4
question. It does not mean that every credential, token, claim, session, or
external identity state in the framework has synchronous revocation freshness.

---

## 25. Implementation-Plan Handoff

After this Spec is reviewed and frozen, the implementation plan must:

- use Case-first TDD and map every Case ID to exact test/project/runner evidence;
- identify the precise `OrganizationScopeIdentity`, three-state
  `OrganizationScopeGenerationRead`, freshness-failure contract,
  `IOrganizationStore` API, validation, and XML documentation edits;
- specify InMemory scope-state publication/guard and checked-generation
  mechanics;
- specify V013 SQL, migration checksum, schema manifest, upgrade, drift, and
  rollback tests;
- name the internal immutable snapshot/cache/single-flight files and DI cutover;
- preserve one production hierarchy registration and avoid a permanent cached/
  uncached dual mainline;
- freeze bounded snapshot/freshness-state capacity and admission, prove eviction
  or pressure cannot decrease `ObservedHighWater` or normalize a quarantined
  scope, and freeze shared-load cancellation behavior;
- specify deterministic concurrency barriers without public test hooks;
- map `Available`, non-quarantined `Unavailable`, `Unknown`/default,
  cancellation, persisted/schema/contract invariant failure, observed
  regression, and quarantined unavailability to separate executable paths;
- implement distinct `ObservedHighWater` and `QuarantineFloor` semantics even if
  represented in one internal state object: observation-time monotonic advance,
  fixed entry floor, failed-load retention, and same-highest recovery retry;
- implement fail-closed sticky regression quarantine: no cached or direct
  authority result below `ObservedHighWater` or at/below `QuarantineFloor`, no
  fallback while quarantined and unavailable, and release only after eligible
  non-regressing publication;
- linearize quarantine transition, candidate publication, and logical caller
  completion through the same per-scope safety-state owner; map OHC22–OHC24 to
  deterministic barriers and assert both no-publication and no-return;
- prove stale-if-error is impossible after mismatch, unavailability, invariant
  failure, or regression;
- separate known pre-commit rollback evidence from COMMIT-unknown complete-pair
  observation and forbid automatic retry;
- enumerate every Permission cache dependency/registration/test to retire or
  move to `99_RecycleBin` rather than deleting directly;
- preserve `AuditTenantContextResolver` and every unrelated Authorization
  caching/key dependency unless an independent exact consumer inventory proves
  it removable; Permission tests alone cannot authorize assembly-wide cleanup;
- include a direct `IPermissionGrantRepository` writer that bypasses Manager and
  invalidation in the real multi-instance Permission test topology;
- prove stale old cache values are not read without requiring destructive
  cleanup;
- provide a real EF provider multi-instance Permission topology;
- extend the existing PostgreSQL AOT Host/Fixture and assert the exact new
  marker while retaining all old markers;
- run and record the promoted dependency-boundary and runner-free checks;
- create the H3 observation sidecar without a Harness wrapper;
- update `memory.md` only after executed evidence and product review;
- stop at review if any atomicity, stale-positive, or provider-parity invariant
  cannot be executed.

This approved R4 Spec freezes the behavioral, authority, failure, and evidence
boundaries. File-by-file implementation order belongs to the implementation
plan and cannot reopen those decisions.
