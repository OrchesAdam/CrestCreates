# Phase 9b+ — Durable Control Plane and Reference Data Stores Design Spec

**Date:** 2026-08-17

**Issue:** [#69 — Phase 9b+ Durable Control Plane and Reference Data Stores](https://github.com/OrchesAdam/CrestCreates/issues/69)

**Depends on:** [#24 — Phase 9b Durable Persistence Foundation](https://github.com/OrchesAdam/CrestCreates/issues/24), [#39 — Phase 9a Accountability Runtime Foundation](https://github.com/OrchesAdam/CrestCreates/issues/39)

**Builds after:** [#55 — Durable Agent Memory Store Provider](https://github.com/OrchesAdam/CrestCreates/issues/55)

**Related but excluded:** #25 Transactional Outbox, #26 cache consistency, #70/#73 Agent Tool pre-dispatch reconciliation

**Design input:** Issue comment `5311440048`, reconciled against repository state on 2026-08-17

**Status:** APPROVED / FROZEN — authorized for Implementation Plan and TDD

**Design mode:** Case-first TDD; Red → Green → Review

**Review revision:** R3 — APPROVED; design frozen for implementation

---

## 1. Decision Summary

Phase 9b+ #69 adds PostgreSQL durable providers for exactly three existing
Store surfaces:

```text
IDescriptorDraftStore
IOrganizationStore
    -> OrganizationUnit
    -> Position
    -> UserOrganizationMembership
    -> UserOrganizationRoleAssignment
IDataPermissionScopeRuleStore
```

The implementation extends the existing
`CrestCreates.Runtime.Persistence.PostgreSql` Provider Kernel. It does not add
another connection pool, transaction coordinator, migration history, provider
capability hierarchy, ORM, or persistence exception taxonomy.

The durable mainline is:

```text
Descriptor Draft / Organization / Data Permission domain contracts
    own snapshots, query meaning, rule priority, hierarchy, and identity semantics
        ↓ existing Store interfaces
PostgreSQL Control Plane / Reference Data Stores
    own durable representation, indexes, independent commit, restart survival,
    persisted invariant validation, and provider failure translation
        ↓
existing PostgreSQL DataSource + migration/schema/failure kernel
```

Before PostgreSQL is introduced, shared observable Store semantics are frozen
and applied to the InMemory implementations. This is required because current
enumeration order is not deterministic and several InMemory keys encode typed
identity through delimiter/sentinel strings.

The design freezes these decisions:

1. PostgreSQL through direct Npgsql remains the only durable provider in scope.
2. The three existing Store interfaces remain the provider boundary. #69 does
   not add a persisted `IDataPermissionScopeStore`.
3. Blind `Save` operations remain atomic snapshot replacement / last committed
   writer wins. #69 does not invent stale-writer rejection without an
   `ExpectedVersion`, `ExpectedHash`, ETag, or conditional domain mutation.
4. InMemory and PostgreSQL run the same provider-neutral semantic contract kit.
5. Every observable enumeration has one total, ordinal order, including the
   existing cross-scope Organization queries.
6. Organization `TenantId == null` has two existing meanings which must not be
   conflated: it is the global identity in persisted entities and point reads,
   while a null tenant argument on collection queries means “no tenant filter.”
7. Data Permission rule priority stays in the Organization domain and is not
   redefined by SQL query convenience.
8. Organization rows have no new cross-entity foreign keys. PostgreSQL must not
   create referential-integrity semantics absent from `IOrganizationStore`.
9. Control Plane/reference writes own an independent top-level commit boundary
   and never join the Workflow/HumanTask Runtime recovery transaction.
10. Complete source-generated JSON snapshots are stored beside the structured
    columns needed for identity, filtering, validation, and ordering.
11. Registration is explicit opt-in. Enabling base Runtime persistence alone
    remains valid without replacing these three development Stores.
12. NativeAOT verification means publish, native link, and execution of the real
    PostgreSQL round trip; analyzer or trim-only evidence is insufficient.
13. Descriptor Draft persistence owns closed DTO unions recursively through the
    entire durable payload graph; a top-level payload discriminator alone is not
    sufficient.
14. Exact instant comparison uses `.NET UtcTicks` stored as `bigint`.
    `timestamptz` is a readable UTC projection, never the exact filter key.
15. Evidence is a typed `Case × Surface × Variant` matrix. One representative
    Store or payload cannot satisfy a cross-surface invariant.
16. Draft Store representation validation never consumes semantic diagnostics
    owned by `IDescriptorDraftValidator`; invalid-but-reviewable drafts remain
    durable and reviewable through the Store-backed Control Plane mainline.

This phase makes Descriptor Drafts and selected Organization/Data Permission
reference data durable. It does **not** make the complete Agent Control Plane
durable: activation requests, activation audit artifacts, runtime activation
gates, and binding artifact resolvers have separate current ownership and are
outside these Store contracts.

---

## 2. Repository Facts That Constrain the Design

### 2.1 The real Store scope is narrower than the Issue title

The current production-facing abstractions contain the three Store interfaces
listed in Section 1. `DefaultDataPermissionScopeProvider` computes a
`DataPermissionScope` from:

```text
IDataPermissionScopeRuleStore
    + IOrganizationIdentityService
    + IOrganizationHierarchyService
```

`DataPermissionScope` is a result, not a durable authority. A new
`IDataPermissionScopeStore` would persist derived authorization context and
create a second truth source. It is forbidden.

The repository also contains `IDraftStore` / `DraftRecord`. That generic Draft
path is not the Descriptor authoring mainline consumed by Agent Control Plane;
the active path consumes `IDescriptorDraftStore`. #69 must not durable-enable
both and thereby preserve two draft mainlines.

`DefaultDescriptorActivationRequestService` currently owns activation requests
inside a private `ConcurrentDictionary`; there is no activation-request Store
contract. Introducing one, defining conditional lifecycle transitions, and
making activation handoff durable is a separate domain cutover, not an
implementation detail of `IDescriptorDraftStore`.

### 2.2 Current Store writes are blind replacement

The three Store contracts contain no:

```text
Revision
ExpectedRevision
ExpectedStateHash
ETag
conditional mutation result
```

The InMemory Stores replace the snapshot at the same logical key. Therefore the
original Issue invariant “stale version update must conflict” cannot be met
without changing the domain contract and every provider.

The corrected concurrency contract is:

> A committed blind write becomes observable as one complete snapshot.
> Concurrent blind writes retain last-committed-writer-wins replacement
> semantics. The result may be complete snapshot A or complete snapshot B, but
> never a torn or mixed snapshot.

`RuntimeConcurrencyException` is not used by these three Saves. If a later phase
requires stale-writer rejection, it must first add a domain-level conditional
write contract and implement it identically in InMemory and PostgreSQL.

### 2.3 Current Organization null scope is asymmetric

The actual `IOrganizationStore` implementation and tests establish:

| Operation shape | `tenantId == null` meaning |
|---|---|
| Entity `TenantId` | Global/reference identity |
| `GetOrganizationUnitByIdAsync` / `GetPositionByIdAsync` | Point read in global identity |
| All collection/query methods | No tenant filter; global and all tenant rows may be returned |

The last row is explicitly covered by
`GetOrganizationUnits_ReturnsAll_WhenNoTenantFilter`. #69 must not implement a
PostgreSQL-only interpretation where null collection scope means global-only.

This asymmetry is not an ideal new API design, but changing it would be a public
Organization contract cutover. #69 records and tests it instead of hiding it.
Tenant-local runtime calls must pass a non-null `tenantId`. No isolation claim
is made for an intentionally unfiltered query.

### 2.4 Current InMemory identity encoding is not a semantic contract

`InMemoryOrganizationStore` builds keys with:

```text
$"{tenantId ?? ""}:{id}"
```

`InMemoryDataPermissionScopeRuleStore` builds keys with `::` delimiters and
uses `"*"` as an internal null sentinel. These are implementation shortcuts,
not domain protocols. They permit aliasing between:

- null and empty tenant IDs;
- identity strings containing delimiters;
- a null wildcard and a literal `"*"` value.

Shared semantic stabilization replaces them with typed tuple/value keys in
InMemory and structured columns/discriminators in PostgreSQL. No provider may
expose or preserve those string protocols.

### 2.5 Current enumeration order is unspecified

The Descriptor Draft and Organization InMemory Stores enumerate
`ConcurrentDictionary.Values`. PostgreSQL would naturally return a different
order unless both implementations adopt a shared total order first.

Deterministic order is part of provider parity because it affects:

- Control Plane draft listing and context construction;
- hierarchy BFS child order;
- primary membership selection when timestamps tie;
- the order of Organization/Role/Position IDs in `OrganizationContext`;
- reproducibility across restart and provider switches.

### 2.6 Data Permission priority is already domain behavior

The current implementation and tests intentionally search all tenant rules
before all global rules:

```text
1. Tenant exact
2. Tenant wildcard permission
3. Tenant wildcard action and permission
4. Global exact
5. Global wildcard permission
6. Global wildcard action and permission
```

In particular, a tenant wildcard-permission rule wins over a global exact rule.
The earlier Phase 5e prose contains an obsolete interleaved priority example;
the current code and current tests are the source of truth for #69.

### 2.7 Descriptor Draft payloads are closed and polymorphic

`DescriptorDraft.Payload` is an abstract `DescriptorDraftPayload`. The current
closed concrete set is:

```text
SchemaDescriptorDraftPayload
FormDescriptorDraftPayload
CapabilityDescriptorDraftPayload
HumanTaskDescriptorDraftPayload
WorkflowDescriptorDraftPayload
EventDescriptorDraftPayload
```

The durable provider cannot use reflection-based polymorphic JSON or generic
`object` serialization under NativeAOT. It needs an explicit generated root for
the aggregate and each current payload graph.

The Store is not the draft validator. It must preserve a supported payload
snapshot even when `DescriptorKind`, `DescriptorId`, or proposed version is
semantically invalid; `IDescriptorDraftValidator` remains responsible for those
diagnostics. The persistence layer only rejects an unsupported CLR payload type
or corrupt persisted representation.

### 2.8 The Provider Kernel already exists

The PostgreSQL provider already owns:

- one configured `NpgsqlDataSource`;
- `PostgreSqlRuntimeMigrationRunner` with an ordered checksummed catalog;
- an advisory migration lock and validation-only startup mode;
- a schema manifest with column/key/check/index/FK validation;
- provider-neutral unavailable, persisted-contract, ambient-boundary, and
  commit-unknown failures;
- PostgreSQL Testcontainers, crash worker, and linux-x64 NativeAOT fixtures.

#69 extends these assets. A second `DbContext`, `NpgsqlDataSource`, migration
history table, or provider exception hierarchy is rejected.

### 2.9 PostgreSQL timestamps cannot preserve the Store time contract alone

`.NET DateTimeOffset.UtcTicks` counts 100-nanosecond intervals, while PostgreSQL
`timestamp`/`timestamptz` has 1-microsecond resolution. Npgsql also requires a
`DateTimeOffset` written to `timestamptz` to have Offset=0. Therefore a
`timestamptz` column cannot be the exact provider-parity key for inclusive
`DraftQuery.CreatedFrom` / `CreatedTo` comparisons or exact Organization
ordering.

Normative references:

- [DateTimeOffset.UtcTicks](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.utcticks?view=net-10.0)
- [Npgsql date/time handling](https://www.npgsql.org/doc/types/datetime.html)
- [PostgreSQL 16 date/time types](https://www.postgresql.org/docs/16/datatype-datetime.html)

#69 stores `CreatedAt.UtcTicks` as `bigint` for exact filtering, ordering, and
persisted validation. Complete JSON preserves the original offset and ticks.
The optional `timestamptz` column is derived from the UTC instant for database
readability only.

### 2.10 Draft semantic diagnostics require Store round-trip preservation

The actual `DefaultDescriptorDraftValidator` owns these current diagnostic
branches:

```text
DRAFT_ID_EMPTY
DESCRIPTOR_ID_EMPTY
AUTHOR_ID_EMPTY
KIND_PAYLOAD_MISMATCH
PAYLOAD_ID_MISMATCH
PROPOSED_VERSION_MISSING
PROPOSED_VERSION_NOT_INTEGER
PROPOSED_VERSION_MISMATCH
CREATE_BASE_VERSION_MUST_BE_EMPTY
UPDATE_BASE_VERSION_REQUIRED
DEPRECATE_BASE_VERSION_REQUIRED
REMOVE_BASE_VERSION_REQUIRED
```

The Agent Control Plane mainline resolves a Draft snapshot from
`IDescriptorDraftStore`, applies its existing kind-visibility check, and then
calls `IDescriptorDraftValidator.Validate` for visible supported kinds.
PostgreSQL `text`/JSON can represent the invalid-but-reviewable string and
version shapes involved. If the durable Store rejects them first, the existing
diagnostics disappear from the Store-backed mainline and persistence has
silently taken domain ownership. #69 therefore distinguishes row/codec
representability from Draft semantic validity throughout Sections 7, 14, and
19.

---

## 3. Goal

Deliver production durable PostgreSQL implementations for Descriptor Draft,
Organization reference data, and Data Permission rules while preserving one
shared observable Store contract across InMemory and PostgreSQL.

For the same domain dataset:

```text
InMemory observable result
    == PostgreSQL observable result

and PostgreSQL state survives:
    provider reconstruction
    process restart
    committed-write response loss
    a process crash around the commit boundary
```

Durability must not turn reference data into a Runtime recovery participant,
add provider-owned concurrency semantics, create a second authorization truth
source, or make the entire Control Plane appear durable.

---

## 4. Scope

### 4.1 In scope

- Shared semantic stabilization for the three selected Store contracts.
- Deterministic enumeration and deterministic Organization identity projection.
- Delimiter/sentinel-free InMemory identity keys.
- Explicit Store-representation input validation shared by both providers,
  without taking semantic validation from `IDescriptorDraftValidator`.
- PostgreSQL implementations of all methods on:
  - `IDescriptorDraftStore`;
  - `IOrganizationStore`;
  - `IDataPermissionScopeRuleStore`.
- Exact tenant/global composite identities.
- Complete source-generated snapshot JSON plus structured query columns.
- Provider-owned closed persistence DTOs/converters for every abstract or
  interface union reachable in a durable Descriptor Draft graph.
- Exact `DateTimeOffset` query/order parity through `UtcTicks` bigint columns.
- Existing provider-owned top-level commit mode for each Save.
- Blind replacement atomicity and last-committed-writer-wins semantics.
- Restart, process crash, concurrency, migration, and provider-switch evidence.
- V011 migration and complete schema-manifest validation.
- Explicit opt-in DI registration which replaces the three InMemory Stores.
- Shared runner-free contract kit with InMemory and PostgreSQL runners.
- A real PostgreSQL linux-x64 NativeAOT publish-link-run scenario.

### 4.2 Out of scope

- WorkflowInstance, HumanTaskInstance, DescriptorSnapshot, durable `IAuditSink`,
  and Runtime recovery changes from #24.
- Agent Memory from #55.
- Agent Tool pre-dispatch reconciliation from #70/#73.
- Outbox, inbox, broker delivery, or state-to-Accountability reliability (#25).
- Cache invalidation or distributed cache consistency (#26).
- A persisted `IDataPermissionScopeStore` or persisted derived filters.
- Changes to permission checking, grants, claims, tokens, or RBAC authority.
- `IDraftStore` / `DraftRecord` durable implementation.
- Durable activation requests, activation audit artifacts, binding artifact
  resolution, approval workflow, or runtime activation gate.
- A general repository/UoW or business ORM provider.
- Multiple databases or EF Core for this provider.
- Cross-entity Organization referential validation.
- Organization deletion, cascading delete, retention, archival, or history.
- New conditional-write/OCC APIs.
- Reliable audit delivery for these blind Saves.

### 4.3 Compatibility position

The public Store signatures remain unchanged. This phase intentionally hardens
shared semantics in ways that remove implementation accidents:

- enumerations become deterministic;
- tuple/structured identities replace delimiter strings;
- Organization non-null TenantId and Organization/query identity values must be
  non-empty; Data Permission Resource and non-null TenantId must be non-empty;
- Draft string values which the current `IDescriptorDraftValidator` diagnoses,
  including empty/whitespace DraftId and null/empty/whitespace DescriptorId or
  AuthorId, remain representable and are not rejected by the Store;
- null alone represents rule wildcard/global scope; literal `"*"` in nullable
  rule dimensions is rejected as ambiguous;
- empty non-null Data Permission Action/Permission values remain exact values;
- `Action == null && Permission != null` remains representable, but is matched
  only by the exact request-relative candidate described in Section 7.6;
- cancellation requested before mutation fails before mutation.

These are provider-parity and representation-safety corrections, not a new
domain authority model. Existing blind replacement remains unchanged.

---

## 5. System Invariants

### INV-01 — The selected Stores are the only durable scope

No derived `DataPermissionScope`, legacy `DraftRecord`, activation request, or
runtime Registry state is persisted through #69.

### INV-02 — Provider details do not cross the Store boundary

No `NpgsqlConnection`, transaction, command, PostgreSQL enum, SQL exception, or
provider registration type appears in Descriptor Draft or Organization
abstractions.

### INV-03 — Tenant/global identity is structural

Global, tenant A, and tenant B identities with the same logical ID coexist. No
empty string or sentinel is exposed through domain contracts.

### INV-04 — Explicit tenant queries are isolated

Every non-null tenant query predicates the normalized tenant scope in SQL and
returns only that exact tenant. Global fallback exists only in Data Permission
rule resolution according to its six-step priority.

### INV-05 — Null Organization collection scope remains unfiltered

PostgreSQL and InMemory preserve the current no-filter meaning for null
collection query arguments. They do not reinterpret it as global-only.

### INV-06 — Snapshot-bearing Stores never share mutable state

Descriptor Draft and all four Organization entity surfaces capture a snapshot
before persistence materialization. Every Get/List result is a fresh snapshot.
Mutating caller-owned collections after Save or a returned graph after Read
cannot mutate stored state. The fully scalar Rule Store has no entity-read
surface and is covered by exact-key replacement rather than a snapshot-read
claim.

### INV-07 — One Save exposes one complete snapshot

Structured columns and JSON snapshot are written in one SQL statement inside
one top-level transaction. Concurrent blind writes expose complete A or
complete B, never a field-level mixture.

### INV-08 — No provider-only stale-writer semantics

No hidden revision, row version, xmin check, advisory lock, or SQL predicate may
turn a blind Save into a stale-writer rejection.

### INV-09 — Observable order is total and ordinal

Every collection method returns the canonical order in Section 7. Database
collation and final materialized order cannot make results culture- or
provider-dependent.

### INV-10 — Rule priority is unchanged

Tenant wildcard rules continue to outrank global exact rules. SQL must implement
the domain priority, not redefine it.

### INV-11 — Organization persistence adds no relationship authority

Missing Parent, OrganizationUnit, Position, or role-scope rows do not cause Save
rejection merely because the durable representation uses relational tables.

### INV-12 — Reference writes do not join Runtime recovery

The selected Saves never enlist in the ambient Workflow/HumanTask Runtime
transaction. An ambient Runtime write boundary is rejected before mutation.

### INV-13 — Commit unknown is not deterministic failure

Loss of COMMIT acknowledgement is surfaced as commit-unknown. It is never
reported as rollback, validation failure, concurrency conflict, or not-found.

### INV-14 — Structured columns and JSON agree

On read, identity and every structured filter/order field must agree with the
deserialized snapshot. Disagreement or unsupported state-contract version fails
closed as `PersistedInvariantViolation`.

### INV-15 — Store emits no semantic Accountability

The provider does not call `IAuditRecorder`, `IAuditSink`, an activation auditor,
or an ad-hoc audit table. Reliable state-to-audit coupling remains #25 work.

### INV-16 — Migrations remain immutable evidence

V001–V010 are not edited. V011 is appended, checksummed, lock-protected,
repeatable, and fully represented in schema validation.

### INV-17 — NativeAOT claims require native execution

The phase is not complete until the published native executable executes all
three real PostgreSQL Stores and emits the required sentinel.

### INV-18 — Durable polymorphism is recursively closed

Every abstract/interface union reachable from a Descriptor Draft payload has a
provider-owned discriminator/DTO or explicit generated AOT-safe converter.
Registering only the concrete top-level payload type is not sufficient.

### INV-19 — Exact time semantics use ticks

All Store comparisons/orderings that observe `CreatedAt` use exact UTC ticks.
No `timestamptz` coarse prefilter may exclude a row that InMemory would include.

### INV-20 — Evidence is surface-complete

Cross-store invariants are proven for every named Save, snapshot, row, method,
payload, and nested-union variant required by the typed evidence dimensions in
Section 14.6.

### INV-21 — Persistence does not become Draft semantic validation

The Draft Store rejects only values it cannot safely represent or dispatch.
Every invalid-but-representable condition currently owned by
`IDescriptorDraftValidator` survives Save/read unchanged and remains available
to the Control Plane validation flow. Store durability must not erase a domain
diagnostic by converting it into a persistence precondition.

---

## 6. Ownership and Dependency Direction

### 6.1 Contract and semantic ownership

```text
CrestCreates.DescriptorDraft.Abstractions
    owns IDescriptorDraftStore and DescriptorDraft contracts

CrestCreates.DescriptorDraft
    owns concrete payload kinds, Snapshot behavior, validation, InMemory Store

CrestCreates.Organization.Abstractions
    owns IOrganizationStore, IDataPermissionScopeRuleStore, and domain models

CrestCreates.Organization
    owns hierarchy, identity projection, rule priority, and InMemory Stores

CrestCreates.Runtime.Persistence.PostgreSql
    owns durable rows, SQL, migrations, top-level commits, and failure mapping

tests/Shared/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing
    owns runner-free shared semantic cases and typed evidence manifest
```

The PostgreSQL provider may reference:

- Descriptor Draft abstractions and the Descriptor Draft implementation needed
  for its closed concrete payload types;
- Organization abstractions;
- existing Runtime Persistence abstractions and Provider Kernel internals.

It must not reference Agent Control Plane implementation, Workflow/HumanTask
implementations, Platform, Web, Dynamic API, or a business ORM provider.

### 6.2 Serialization ownership

The durable snapshot codec is a provider representation detail, but it consumes
domain-owned `Snapshot()` methods and explicit concrete payload types. It may
not duplicate:

- Descriptor Draft validation or materialization;
- Organization hierarchy traversal;
- primary membership selection;
- Data Permission scope resolution or rule priority.

The source-generated JSON context lives in the PostgreSQL provider project and
contains only explicit durable snapshot roots. No reflection resolver,
`object`-typed persistence envelope, assembly scan, or dynamic type-name loading
is allowed.

### 6.3 Required architecture guards

- Abstraction projects do not reference the PostgreSQL provider.
- Organization and Descriptor Draft implementations do not reference the
  PostgreSQL provider.
- PostgreSQL provider does not reference Agent Control Plane implementation.
- Shared contract kit references contracts/domain models, never a provider or a
  test runner.
- Provider source contains no EF Core or reflection serializer fallback.
- Organization schema contains no cross-entity FK constraints.

---

## 7. Frozen Shared Store Semantics

### 7.1 Store representation validation versus Draft semantic validation

Both providers validate only conditions required to address a Store row,
choose a closed persistence representation, or safely execute an existing
Organization/Rule lookup. They do so before snapshot capture, database access,
or InMemory mutation:

- a Draft instance, `TenantId`, `DraftId`, and `Payload` must be non-null;
- Draft enums persisted as structured closed values must be defined and the
  payload CLR type must be one of the six supported persistence DTO families;
- every defined `DescriptorKind`, including `Unknown`, `DynamicApiEndpoint`,
  `McpTool`, and `AgentTool`, is representable independently of payload type;
  DescriptorKind/Payload equality is not checked by the Store;
- empty or whitespace Draft `TenantId` and `DraftId` remain structurally
  representable text keys; the Store does not invent a non-empty precondition;
- Organization `TenantId == null` is global; a non-null TenantId is non-empty;
- Organization entity IDs, user IDs, OrganizationUnit IDs, Position IDs, and
  Role IDs used by the selected Store methods are non-empty where required;
- Data Permission `Resource` and a non-null TenantId are non-empty;
- nullable rule dimensions use `null` for wildcard/global;
- literal `"*"` is rejected for `Action`, `Permission`, or `TenantId` because it
  aliases the current InMemory sentinel protocol;
- non-null empty `Action` and `Permission` are valid exact values and remain
  distinct from null wildcard values;
- Organization and Rule enums persisted as structured columns must be defined.

`IDescriptorDraftValidator`, not the Store, owns Draft semantic diagnostics.
The Store must therefore preserve empty/whitespace `DraftId`; null,
empty, or whitespace `DescriptorId` and `AuthorId`; Kind/Payload and
Payload/DescriptorId mismatches; invalid
BaseVersion/Operation combinations; and missing, non-integer, or mismatched
ProposedVersion values. D08 saves and reloads each such draft, then proves the
existing validator still emits its diagnostic. V01 explicitly excludes these
validator-owned conditions.

Representation-validation failures do not become provider exceptions. A
domain diagnostic must never disappear merely because the Control Plane first
loaded the draft through a durable Store.

### 7.2 Cancellation

All methods observe a pre-cancelled token before snapshot, query, or mutation.
PostgreSQL passes cancellation to open/read/write calls. Once the work is ready
to commit, the existing coordinator completes COMMIT with
`CancellationToken.None`; loss of acknowledgement still follows the normal
commit-unknown rule rather than being rewritten as deterministic cancellation
or rollback.

### 7.3 Descriptor Draft semantics

Identity is `(TenantId, DraftId)` with ordinal string equality.

`SaveAsync` replaces the complete snapshot at that identity. `GetAsync` returns
null when absent. `ListAsync` applies all existing `DraftQuery` filters and then
returns:

```text
DraftId ASC using StringComparer.Ordinal
```

`CreatedFrom` and `CreatedTo` remain inclusive and compare exact
`DateTimeOffset.UtcTicks`. PostgreSQL predicates compare
`created_at_utc_ticks bigint` with `query.CreatedFrom.Value.UtcTicks` and
`query.CreatedTo.Value.UtcTicks`; `timestamptz` is never used as a coarse
prefilter. The exact original `DateTimeOffset` value, ticks, and non-zero offset
are returned from JSON.

The Store accepts all six supported concrete payload kinds. It does not invoke
`IDescriptorDraftValidator` and does not silently repair an invalid draft.

### 7.4 Organization point and collection semantics

All logical entity identities are:

```text
(TenantScopeKind, TenantId, Id)
```

where global is represented internally as `(global, "", Id)` and tenant scope
as `(tenant, non-empty TenantId, Id)`. The normalized representation never
escapes the provider.

Canonical collection order is:

| Method | Canonical total order |
|---|---|
| `GetOrganizationUnitsAsync` | `SortOrder`, normalized scope, `Id` ordinal |
| `GetPositionsAsync` | normalized scope, `Id` ordinal |
| `GetMembershipsByUserAsync` | `CreatedAt` instant, normalized scope, `Id` ordinal |
| `GetMembershipsByOrganizationUnitAsync` | `CreatedAt` instant, normalized scope, `Id` ordinal |
| `GetRoleAssignmentsByUserAsync` | `CreatedAt` instant, normalized scope, `Id` ordinal |

Normalized scope order is global first, then tenant scopes by TenantId ordinal.
For an explicit tenant query the scope component is constant, producing the
short forms proposed in the Issue comment.

SQL supplies an index-friendly preliminary order, but the Store finalizes every
materialized collection with the canonical .NET comparer. PostgreSQL `C`
collation and `StringComparer.Ordinal` are not assumed to have identical total
ordering for every supplementary Unicode value.

Save remains complete replacement. No Save verifies that a referenced parent,
OrganizationUnit, Position, or Role exists.

### 7.5 Organization service determinism

`DefaultOrganizationIdentityService` selects Primary membership by:

```text
CreatedAt instant ASC
normalized scope ASC (global, then TenantId Ordinal)
Id ASC using StringComparer.Ordinal
```

Active membership and role projections consume canonical Store order before
`Distinct`, so `OrganizationUnitIds`, `RoleIds`, and `PositionIds` are stable.

The Organization domain introduces one internal typed scoped identity value,
conceptually `OrganizationScopedKey(TenantScopeKind, TenantId, Id)`. Both
`InMemoryOrganizationStore` and `DefaultOrganizationHierarchyService` use it
for dictionaries, child maps, and visited sets. The hierarchy service's current
delimiter-based `CompKey` is retired; TenantId/Id values containing `:` cannot
alias another scope/key combination.

`DefaultOrganizationHierarchyService` consumes canonical OrganizationUnit
order. Descendant BFS therefore visits siblings by `SortOrder`, then Id for an
explicit tenant. Missing parents continue to stop traversal; cycles continue to
throw `OrganizationHierarchyException`. Persistence adds neither repair nor FK
rejection.

### 7.6 Data Permission rule identity and priority

A rule identity is a typed tuple:

```text
(TenantScope, Resource, ActionMatch, PermissionMatch)
```

where `ActionMatch` and `PermissionMatch` are explicit Exact/Wildcard values.
Null creates Wildcard. Any non-null string other than the reserved literal
`"*"`, including `""`, creates Exact. The match-kind discriminator, never the
stored value alone, carries wildcard meaning.

`SaveRuleAsync` replaces `ScopeKind` for the exact typed rule identity.
`GetScopeKindAsync` generates these request-relative candidates, removes exact
duplicate tuples while preserving first occurrence, and returns the first
persisted match:

```text
for requested tenant, then global:
    1. (request Action: Exact when non-null else Wildcard,
        request Permission: Exact when non-null else Wildcard)
    2. (request Action: Exact when non-null else Wildcard,
        Permission: Wildcard)
    3. (Action: Wildcard, Permission: Wildcard)
```

This preserves the existing six-level behavior. In particular, with a non-null
requested Action, `(Action: Wildcard, Permission: Exact)` is **not** a fallback
candidate. That persisted shape is observable only when the requested Action is
null and the request-relative first candidate has Wildcard Action plus Exact
Permission. PostgreSQL must not broaden the lookup to all four generic
Exact/Wildcard combinations. The method returns null when no candidate exists.

No enumeration or persisted `DataPermissionScopeRule` read API is added merely
for provider convenience.

---

## 8. Snapshot and Serialization Boundary

### 8.1 Snapshot-before-materialization

Every Save follows:

```text
validate representable identity
    -> domain Snapshot()
    -> build structured columns from that snapshot
    -> serialize that same snapshot with generated JsonTypeInfo
    -> open independent commit boundary
    -> one upsert statement
    -> COMMIT
```

Structured columns and JSON can never be built from different caller objects.

### 8.2 Descriptor Draft persistence envelope

The stored envelope contains:

- a persistence state-contract version;
- an explicit payload type discriminator from a closed enum/table;
- the complete Descriptor Draft snapshot;
- the complete concrete payload/descriptor graph;
- Metadata dictionary with ordinal key semantics.

The discriminator is not an assembly-qualified CLR type name. Unsupported
future payload types fail before mutation until the generated context and
discriminator switch are extended.

Because `DescriptorDraft.Payload` is abstract and currently has no polymorphic
JSON discriminator contract, the provider must not ask System.Text.Json to
deserialize `DescriptorDraft` through that abstract property. The AOT-safe
representation is:

```text
generated DescriptorDraft header DTO (all fields except Payload)
    + numeric closed payload discriminator
    + provider-owned concrete payload persistence DTO serialized with its
      matching generated JsonTypeInfo
```

Provider DTO nullability follows representability rather than C# `required`
syntax: `DescriptorId` and `AuthorId` may contain runtime null so the Store can
round-trip the validator-owned diagnostics. Row-address fields `TenantId` and
`DraftId` remain non-null representation requirements; empty/whitespace text is
still preserved exactly.

On read, the provider deserializes the generated envelope, selects one of the
six explicit payload DTO families, and reconstructs the domain record.
`DescriptorKind` in the header is not used as the CLR payload discriminator, so
the Store can preserve an invalid-but-reviewable kind/payload mismatch for
`IDescriptorDraftValidator` to diagnose.

The closure rule applies recursively. `WorkflowDescriptorDraftPayload` cannot
serialize its domain `WorkflowStep.Target` directly because `InteractionTarget`
is abstract and has no STJ discriminator. Its provider DTO uses a second closed
union:

```text
WorkflowStepPersistenceDto
    TargetKind = Capability | HumanTask | SubWorkflow
    CapabilityRef? | HumanTaskRef? | SubWorkflowRef?
```

Exactly one ref must agree with `TargetKind`; malformed or multiply-populated
persisted shapes fail as `PersistedInvariantViolation`. No polymorphism
attribute or database concern is added to `InteractionTarget` or other domain
contracts.

An architecture test recursively inventories all abstract/interface-typed
members reachable from the six durable payload graphs. Every discovered union
must be mapped by a provider DTO/converter and generated JSON root. Adding a new
nested union without updating this closed mapping fails the build/test gate.

### 8.3 Organization snapshots

Each Organization entity is stored as complete generated JSON plus structured
identity/query/order columns. Returning from JSON preserves the exact domain
snapshot, including the original `DateTimeOffset` offset and precision.

`created_at_utc_ticks bigint` is the exact structured order value.
`timestamptz` is populated from a UTC (`Offset=0`) projection for operator
readability only; it does not replace the JSON snapshot's `CreatedAt` or
participate in exact ordering. Read validation requires JSON
`CreatedAt.UtcTicks == created_at_utc_ticks` and separately verifies that the
readable timestamp equals the microsecond-normalized UTC projection.

### 8.4 Read validation

For each row, the provider verifies before returning:

- supported state-contract version;
- JSON is valid for the expected generated root;
- JSON identity equals structured identity after tenant normalization;
- enum/filter/order columns equal the JSON snapshot, including exact
  `CreatedAt.UtcTicks`;
- Descriptor Draft payload discriminator matches the concrete deserialized
  payload type;
- every nested closed-union discriminator has exactly one valid matching arm;
- required persisted strings are representable.

The provider does not run business validation such as draft eligibility or
Organization relationship existence. Persisted representation disagreement
fails closed with `PersistedInvariantViolation`.

The fully structured Rule row has no JSON snapshot, but it is still an
authorization authority row and follows the same fail-closed posture. Before
returning a scope, materialization validates the tenant-scope discriminator and
tuple, both match-kind discriminators and wildcard/value shapes, and the closed
`DataPermissionScopeKind`. Invalid integers are never silently cast to enum
values.

---

## 9. PostgreSQL Schema

### 9.1 Common rules

- V011 is appended to the existing catalog.
- All identity and observable ordering text uses PostgreSQL `C` collation.
- Every table has a complete primary key represented in the schema manifest.
- Every query index required by the Store contract is represented in the
  manifest; validation checks shape, predicate, uniqueness, and collation.
- `state_contract_version` is explicit and checked on every JSON snapshot
  table. The fully structured Data Permission rule table is versioned through
  the migration/schema contract and needs no per-row JSON contract version.
- Provider-maintained `updated_at` is not a domain field and is not returned.
- No table in this phase has a foreign key to another Organization table.
- Every domain `CreatedAt` column pair is materialized from the same captured
  snapshot: exact `.UtcTicks` into `bigint`, and a UTC Offset=0 value truncated
  to PostgreSQL microsecond precision into `timestamptz`. The bigint is the
  semantic comparison/order source; the timestamp is diagnostic/readable.

### 9.2 Logical tables

#### `control_plane_descriptor_drafts`

```text
tenant_id                  text C not null
draft_id                   text C not null
payload_type               integer not null
descriptor_kind            integer not null
operation                  integer not null
author_kind                integer not null
status                     integer not null
created_at_utc_ticks       bigint not null
created_at                 timestamptz not null
state_contract_version     integer not null
state_json                 jsonb not null
updated_at                 timestamptz not null
PK (tenant_id, draft_id)
```

The checks and dispatch rules are intentionally independent:

```text
payload_type:
    closed to the six current persistence payload DTO families

descriptor_kind:
    closed to every currently defined DescriptorKind enum value:
    Unknown | Schema | Capability | Event | Workflow | Form | HumanTask |
    DynamicApiEndpoint | McpTool | AgentTool

operation / author_kind / status:
    closed to every currently defined value of their respective enums

descriptor_kind == payload-derived DescriptorKind:
    NOT a persistence invariant
```

`payload_type`, never `descriptor_kind`, selects the persistence DTO arm. A
defined `descriptor_kind` without a current Draft payload family remains
storable and reviewable. Read validation requires exact JSON ↔ column equality,
but does not require DescriptorKind/Payload equality; that mismatch remains
owned by `IDescriptorDraftValidator`.

Indexes support tenant-local canonical listing and the existing DraftQuery
fields. `CreatedFrom`/`CreatedTo` predicates use only
`created_at_utc_ticks`; `created_at` is never a filter precondition. SQL may
pre-filter/order other fields, but final returned order is verified/finalized
with `StringComparer.Ordinal` after deserialization.

#### `organization_units`

```text
tenant_scope_kind          text not null
tenant_id                  text C not null
organization_unit_id       text C not null
parent_id                  text C null
sort_order                 integer not null
is_active                  boolean not null
created_at_utc_ticks       bigint not null
created_at                 timestamptz not null
state_contract_version     integer not null
state_json                 jsonb not null
updated_at                 timestamptz not null
PK (tenant_scope_kind, tenant_id, organization_unit_id)
```

#### `organization_positions`

```text
tenant_scope_kind          text not null
tenant_id                  text C not null
position_id                text C not null
is_active                  boolean not null
created_at_utc_ticks       bigint not null
created_at                 timestamptz not null
state_contract_version     integer not null
state_json                 jsonb not null
updated_at                 timestamptz not null
PK (tenant_scope_kind, tenant_id, position_id)
```

#### `organization_memberships`

```text
tenant_scope_kind          text not null
tenant_id                  text C not null
membership_id              text C not null
user_id                    text C not null
organization_unit_id       text C not null
position_id                text C null
is_primary                 boolean not null
is_active                  boolean not null
created_at_utc_ticks       bigint not null
created_at                 timestamptz not null
state_contract_version     integer not null
state_json                 jsonb not null
updated_at                 timestamptz not null
PK (tenant_scope_kind, tenant_id, membership_id)
```

#### `organization_role_assignments`

```text
tenant_scope_kind          text not null
tenant_id                  text C not null
assignment_id              text C not null
user_id                    text C not null
role_id                    text C not null
organization_unit_id       text C null
is_active                  boolean not null
created_at_utc_ticks       bigint not null
created_at                 timestamptz not null
state_contract_version     integer not null
state_json                 jsonb not null
updated_at                 timestamptz not null
PK (tenant_scope_kind, tenant_id, assignment_id)
```

The four tables have only identity/query indexes. They deliberately have no FK
from Parent, Membership, Position, or RoleAssignment columns.

#### `data_permission_scope_rules`

```text
tenant_scope_kind          text not null
tenant_id                  text C not null
resource                   text C not null
action_match_kind          integer not null
action_value               text C not null
permission_match_kind      integer not null
permission_value           text C not null
scope_kind                 integer not null
updated_at                 timestamptz not null
PK (tenant_scope_kind, tenant_id, resource,
    action_match_kind, action_value,
    permission_match_kind, permission_value)
```

Schema CHECKs require:

- `tenant_scope_kind` is exactly `global` or `tenant` and agrees with the
  normalized tenant tuple in Section 9.3;
- action and permission match kinds are exactly the closed Exact/Wildcard
  values;
- a Wildcard match kind uses the internal empty storage value;
- Exact may use any domain-valid exact string, including empty;
- `scope_kind` is one of the six defined `DataPermissionScopeKind` values.

Thus `(Wildcard, "")` and `(Exact, "")` are distinct because the kind column,
not the value alone, defines wildcard semantics. Schema checks are the first
line of defense; provider materialization repeats closed-value and tuple-shape
validation so drift/corruption cannot become an authorization decision.

### 9.3 Tenant normalization checks

Every nullable-tenant table checks:

```text
(tenant_scope_kind = 'global' and tenant_id = '')
or
(tenant_scope_kind = 'tenant' and tenant_id <> '')
```

Descriptor Draft has a non-null tenant field and therefore needs no scope-kind
column. Empty/whitespace values remain representable so the Store does not add
a semantic constraint absent from its contract or current Draft validator.

### 9.4 Migration behavior

- Apply uses the existing advisory lock and one transaction for V011 DDL plus
  history append.
- Reapply performs no destructive DDL and produces no duplicate history row.
- Validation-only mode rejects missing V011, modified checksum, unknown future
  migration, missing table/index/check, or incompatible shape.
- All V001–V010 history and schema remain valid.

---

## 10. Commit and Concurrency Model

### 10.1 Reuse the existing top-level commit mode

#55 already added
`PostgreSqlRuntimeTransactionCoordinator.ExecuteTopLevelAsync`. #69 reuses this
exact Provider Kernel primitive; it does not add another coordinator or commit
executor. The method uses the existing `NpgsqlDataSource`, creates an owned
session/transaction, and rejects an already active Runtime session.

Its current XML comment is phrased specifically for Agent Memory formal
curation. Implementation should generalize that documentation to
provider-owned top-level operations without changing the method signature or
creating a second path.

Write behavior is:

```text
if Runtime ambient transaction exists:
    fail AmbientCommitBoundaryUnsupported before mutation
else:
    open connection from existing DataSource
    begin transaction
    install the owned provider session
    execute exactly one Store upsert
    commit
```

It does not open a second pool, join the caller's Runtime transaction, or hide
an independent commit inside that transaction. Read methods use the existing
DataSource on a separate read path and do not enlist in Runtime recovery.

### 10.2 Blind upsert

Each Save is one `INSERT ... ON CONFLICT ... DO UPDATE` over one logical row.
Every structured field and JSON snapshot is replaced from the same captured
snapshot. There is no `WHERE revision = ...`, xmin predicate, or stale result.

Internal `updated_at` or diagnostic counters do not create domain OCC.

### 10.3 Concurrent writers

PostgreSQL row conflict serialization determines the final committed writer.
The final row must equal one complete submitted snapshot. Shared concurrency
cases compare against A and B, never assume which writer wins.

### 10.4 Commit acknowledgement loss

If PostgreSQL may have committed but the client cannot prove acknowledgement,
the existing top-level coordinator throws
`RuntimeTransactionCommitUnknownException`.
It must not automatically retry a possibly committed write.

Callers can reconcile Descriptor Draft and Organization Saves through their
existing point/query reads. Data Permission rule Save is an idempotent exact-key
replacement and can be intentionally retried by an upper layer, but the Store
does not conceal the unknown result.

### 10.5 Crash visibility

- crash/kill before COMMIT: old snapshot or absence remains visible;
- crash after server COMMIT: complete new snapshot is visible after restart;
- no crash window exposes only structured columns or only JSON;
- no Store write changes a Workflow/HumanTask recovery participant.

---

## 11. DI and Host Composition

### 11.1 Feature-neutral base provider

`AddCrestCreatesPostgreSqlRuntimePersistence(options)` continues to register the
DataSource, migration/startup services, Runtime participants, and base provider
capabilities. It does not automatically replace Descriptor Draft or
Organization Stores.

The migration catalog may contain V011 even when the feature registration is
not selected, as V010 already does for opt-in Agent Memory Stores. Schema
availability and DI selection remain separate concerns.

### 11.2 Explicit opt-in surface

One extension is added:

```csharp
services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
```

It requires the base PostgreSQL provider registration and replaces exactly:

```text
IDescriptorDraftStore
IOrganizationStore
IDataPermissionScopeRuleStore
```

It does not register Organization domain services, Descriptor Draft validators,
Agent Control Plane, Runtime activation, or Accountability producers. Those
remain owned by their existing module registrations.

Registration is idempotent and resolves each selected interface to exactly one
PostgreSQL singleton. A startup/composition validation gives a clear error when
the opt-in extension is used without the base Provider Kernel.

The supported registration contract is explicitly base-first:

```text
AddCrestCreatesPostgreSqlRuntimePersistence(...)
    -> AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
```

Feature-before-base registration is unsupported and fails with the same clear
composition error; the feature extension does not queue a deferred replacement.
Calling the feature extension repeatedly after the base registration remains
idempotent and does not create duplicate Store registrations.

### 11.3 Provider capability claims

The existing `IRuntimePersistenceProviderCapabilities.FullDurable` claim remains
about the Runtime provider kernel; it is not expanded to imply that every
optional Store is selected. #69 adds no second global provider-tier enum.

Selection evidence is the resolved Store type plus explicit composition tests.

---

## 12. Exception and Outcome Taxonomy

| Situation | Required outcome |
|---|---|
| Invalid/unrepresentable caller identity | `ArgumentException` before mutation |
| Pre-cancelled operation | cancellation before mutation |
| PostgreSQL unavailable before known commit | `RuntimePersistenceUnavailableException` |
| COMMIT acknowledgement unknown | `RuntimeTransactionCommitUnknownException` |
| Corrupt JSON/column mismatch/unsupported persisted version | `RuntimePersistenceContractException(PersistedInvariantViolation)` |
| Ambient Runtime write boundary | `RuntimePersistenceContractException(AmbientCommitBoundaryUnsupported)` |
| Concurrent blind Save | success for each known commit; no concurrency exception |
| Missing point read | null / empty according to existing Store contract |
| Missing Organization parent/reference | Save succeeds; existing runtime behavior applies |

Raw `PostgresException`, Npgsql timeout/connection types, SQLSTATE, table names,
and connection strings do not escape as the public failure contract.

---

## 13. AOT and Serialization

### 13.1 Generated JSON roots

`PostgreSqlControlPlaneReferenceDataJsonSerializerContext` explicitly includes:

- the Descriptor Draft durable envelope;
- provider-owned persistence DTO roots for all six current Descriptor Draft
  payload kinds;
- the closed Workflow target DTO arms for Capability, HumanTask, and
  SubWorkflow;
- every other concrete DTO/value reachable from those roots, with an
  architecture inventory proving no abstract/interface union is left to STJ
  runtime polymorphism;
- OrganizationUnit;
- Position;
- UserOrganizationMembership;
- UserOrganizationRoleAssignment.

Data Permission rules are stored in structured columns and need no reflection
serialization path.

No `DefaultJsonTypeInfoResolver`, `Type.GetType`, assembly scan,
`JsonSerializer.Serialize(object)`, or reflection fallback is allowed.

### 13.2 NativeAOT gate

The existing PostgreSQL AOT Host/fixture is extended to:

1. resolve all three Stores through the real opt-in DI composition;
2. save and reload the Workflow Descriptor Draft graph with Capability,
   HumanTask, and SubWorkflow target variants through provider-owned DTO unions;
3. save/reload Organization entities and execute hierarchy/identity projection;
4. save tenant/global Data Permission rules and prove priority;
5. reconstruct the provider and prove persisted state remains visible;
6. emit:

```text
CRESTCREATES_DURABLE_CONTROL_PLANE_REFERENCE_DATA_OK
```

The fixture publishes `linux-x64`, completes native link, executes against real
PostgreSQL, requires the new sentinel, and retains existing Phase 9b/#55
sentinels. A test-host JIT run is useful regression evidence but is not the AOT
claim.

---

## 14. Acceptance Case Matrix

Every case has a stable ID and one normative parameterized test name. Evidence
is not keyed by Case ID alone. The typed manifest expands every required
`Case × Surface × Variant × Runner` tuple and the boundary ledger rejects any
missing tuple. Shared cases run against both InMemory and PostgreSQL unless
marked PostgreSQL-only (`PG`), architecture (`ARCH`), or AOT.

### 14.1 Descriptor Draft

| ID | Required dimension | Case | Normative test |
|---|---|---|---|
| D01 | `DescriptorPayloadVariant` | complete payload/nested-union round trip | `DescriptorDraftPayloadVariant_Should_RoundTripCompleteSnapshot` |
| D02 | Draft | snapshot on write | `DescriptorDraft_Save_Should_CaptureSnapshot` |
| D03 | Draft | snapshot on read | `DescriptorDraft_Read_Should_ReturnDetachedSnapshot` |
| D04 | Draft | tenant isolation | `DescriptorDraft_SameIdInTwoTenants_Should_NotCollide` |
| D05 | `DraftQueryVariant` | every query filter and combined filter | `DescriptorDraftQueryVariant_Should_PreserveSemantics` |
| D06 | Draft | deterministic ordinal order | `DescriptorDraft_List_Should_OrderByDraftIdOrdinal` |
| D07 | Draft | complete blind replace | `DescriptorDraft_Save_Should_ReplaceCompleteSnapshot` |
| D08 | `DraftValidatorOwnedInvalidVariant` | Store round-trips validator-owned invalid draft | `DraftValidatorOwnedInvalidVariant_Should_RemainDurableAndDiagnosable` |
| D09 PG | Draft | provider reconstruction | `DescriptorDraft_Should_SurviveProviderRestart` |
| D10 PG | Draft | process restart | `DescriptorDraft_Should_SurviveProcessRestart` |
| D11 | Draft | inclusive 100ns lower/upper boundaries | `DescriptorDraft_TimeFilter_Should_PreserveHundredNanosecondBoundaries` |
| D12 | Draft | same instant with different offsets | `DescriptorDraft_TimeFilter_Should_CompareUtcTicksNotOffset` |
| D13 | Draft | non-zero offset exact round trip | `DescriptorDraft_CreatedAt_Should_PreserveOriginalOffsetAndTicks` |

`DescriptorPayloadVariant` is the closed set:

```text
Schema
Form
Capability
HumanTask
Event
WorkflowCapabilityTarget
WorkflowHumanTaskTarget
WorkflowSubWorkflowTarget
```

This covers every one of the six payload kinds and every current nested
`InteractionTarget` subtype. `DraftQueryVariant` contains DescriptorKind,
Operation, AuthorKind, Status, CreatedFrom, CreatedTo, and Combined.

### 14.2 Organization

| ID | Required dimension | Case | Normative test |
|---|---|---|---|
| O01 | `OrganizationIdentitySurface` | global/tenant same ID | `OrganizationIdentitySurface_GlobalAndTenant_Should_NotCollide` |
| O02 | `OrganizationIdentitySurface` | two tenants same ID | `OrganizationIdentitySurface_SameIdInTwoTenants_Should_NotCollide` |
| O03 | `OrganizationQuerySurface` | explicit tenant isolation | `OrganizationQuerySurface_Should_PreserveExplicitTenantIsolation` |
| O04 | `OrganizationQuerySurface` | null list remains unfiltered | `OrganizationQuerySurface_NullTenant_Should_RemainUnfiltered` |
| O05 | OrganizationUnit | Unit total order | `OrganizationUnits_Should_OrderBySortOrderScopeThenId` |
| O06 | Position | Position total order | `Positions_Should_OrderByScopeThenId` |
| O07 | MembershipByUser | membership total order | `MembershipsByUser_Should_OrderByCreatedAtScopeThenId` |
| O08 | MembershipByUnit | membership total order | `MembershipsByUnit_Should_OrderByCreatedAtScopeThenId` |
| O09 | RoleAssignment | role total order | `RoleAssignments_Should_OrderByCreatedAtScopeThenId` |
| O10 | Membership | cross-scope same timestamp and ID tie-break | `PrimaryMembership_FullTie_Should_UseNormalizedScopeThenId` |
| O11 | IdentityService | deterministic identity projection | `OrganizationIdentity_Should_BeDeterministic` |
| O12 | HierarchyService | deterministic hierarchy projection | `OrganizationHierarchy_Should_BeDeterministic` |
| O13 | OrganizationUnit | missing parent accepted | `OrganizationUnit_MissingParent_Should_NotFailSave` |
| O14 | `MissingReferenceVariant` | missing membership/role refs accepted | `OrganizationReferenceVariant_Should_NotFailSave` |
| O15 PG/ARCH | `OrganizationEntitySurface` | no new FK semantics | `OrganizationProvider_Should_NotIntroduceReferentialSemantics` |
| O16 PG | `OrganizationEntitySurface` | direct process-restart round trip | `OrganizationEntitySurface_Should_SurviveProcessRestart` |
| O17 PG | HierarchyService | hierarchy stable after restart | `OrganizationHierarchy_Should_RemainStableAfterRestart` |
| O18 PG | IdentityService | identity stable after restart | `OrganizationIdentity_Should_RemainStableAfterRestart` |
| O19 | `ScopedKeyCollisionVariant` | typed key prevents delimiter alias | `OrganizationScopedKey_Should_NotAliasDelimiterValues` |
| O20 | `OrganizationEntitySurface` | snapshot on write | `OrganizationEntitySurface_Save_Should_CaptureSnapshot` |
| O21 | `OrganizationReadSurface` | snapshot on read | `OrganizationReadSurface_Should_ReturnDetachedSnapshot` |
| O22 | `OrganizationCreatedAtVariant` | exact 100ns ordering and offset preservation | `OrganizationCreatedAtVariant_Should_PreserveExactOrderAndSnapshot` |

O10 uses two primary memberships with identical `CreatedAt` and `Id` in two
tenant scopes and proves normalized scope breaks the tie. O19 includes both
`TenantId="a:b", Id="c"` and `TenantId="a", Id="b:c"` through Store and
Hierarchy paths. O01/O02 use point reads for Unit/Position and explicit-tenant
plus unfiltered queries for Membership/RoleAssignment, proving both same-ID
rows remain independently observable on every identity surface.

### 14.3 Data Permission rules

| ID | Required dimension | Case | Normative test |
|---|---|---|---|
| P01 | Rule | tenant exact | `DataPermissionRule_Should_MatchTenantExact` |
| P02 | Rule | tenant wildcard permission | `DataPermissionRule_Should_MatchTenantWildcardPermission` |
| P03 | Rule | tenant wildcard action+permission | `DataPermissionRule_Should_MatchTenantWildcardAction` |
| P04 | Rule | global fallback | `DataPermissionRule_Should_FallBackToGlobal` |
| P05 | Rule | tenant wildcard beats global exact | `DataPermissionRule_TenantWildcard_Should_WinOverGlobalExact` |
| P06 | Rule | other tenant isolation | `DataPermissionRule_OtherTenant_Should_NotApply` |
| P07 | Rule | blind exact-key replacement | `DataPermissionRule_Save_Should_ReplaceExactRule` |
| P08 ARCH | Rule | no derived Scope Store | `DataPermissionScope_Should_RemainDerived` |
| P09 PG | Rule | process restart | `DataPermissionRule_Should_SurviveProcessRestart` |
| P10 | `RuleExactEmptyVariant` | empty exact distinct from wildcard | `DataPermissionRule_EmptyExact_Should_RemainDistinctFromWildcard` |
| P11 | Rule | wildcard-action/exact-permission not broadened | `DataPermissionRule_WildcardActionExactPermission_Should_NotMatchNonNullAction` |
| P12 | Rule | null-action request may match wildcard-action/exact-permission | `DataPermissionRule_WildcardActionExactPermission_Should_MatchNullActionRequest` |
| P13 PG | `PersistedRuleCorruptionVariant` | corrupt authority row fails closed | `PersistedRuleCorruptionVariant_Should_FailClosed` |

### 14.4 Validation and cancellation

| ID | Required dimension | Case | Normative test |
|---|---|---|---|
| V01 | `IdentityValidationVector` | representation-invalid Store input | `IdentityValidationVector_Should_FailBeforeMutation` |
| V02 | `RuleSentinelField` | literal `"*"` is rejected | `RuleSentinelField_Should_FailBeforeMutation` |
| V03 | `PersistedEnumSurface` | undefined enum is rejected | `PersistedEnumSurface_Should_FailBeforeMutation` |
| V04 | Draft | unsupported payload CLR type rejected | `UnsupportedDraftPayload_Should_FailBeforeMutation` |
| V05 | `StoreMethodSurface` | every method observes pre-cancellation | `PreCancelledStoreMethod_Should_ExitBeforeQueryOrMutation` |

`IdentityValidationVector` is an explicit manifest list, not a wildcard label;
it includes every required parameter/property for Draft, all four Organization
entity Saves and queries, and Rule Resource/Tenant. Optional Organization refs
are validated only when non-null. `RuleSentinelField` is Action, Permission, and
TenantId.

### 14.5 Cross-store concurrency, crash, and failure

| ID | Required dimension | Case | Normative test |
|---|---|---|---|
| F01 | `SaveSurface` | complete concurrent winner | `SaveSurface_ConcurrentBlindSave_Should_ExposeOneCompleteSnapshot` |
| F02 | `SaveSurface` | no false OCC | `SaveSurface_ConcurrentBlindSave_Should_NotInventStaleWriterConflict` |
| F03 PG | `SaveSurface` | crash before commit | `SaveSurface_CrashBeforeCommit_Should_NotExposePartialSnapshot` |
| F04 PG | `SaveSurface` | crash after commit | `SaveSurface_CrashAfterCommit_Should_ExposeCompleteSnapshot` |
| F05 PG | `SaveSurface` | commit response loss | `SaveSurface_CommitUnknown_Should_NotBeReportedAsDeterministicFailure` |
| F06 PG | `StoreMethodSurface` | unavailable provider taxonomy | `StoreMethodSurface_UnavailableProvider_Should_UseSharedFailureTaxonomy` |
| F07 PG | `PersistedSnapshotCorruptionVariant` | corrupt snapshot contract fails closed | `PersistedSnapshotCorruptionVariant_Should_FailClosed` |
| F08 PG | `SaveSurface` | ambient Runtime transaction | `SaveSurface_Should_RejectAmbientRuntimeTransactionBeforeMutation` |
| F09 PG | `PersistedStructuredFieldVariant` | every duplicated field mismatch fails closed | `PersistedStructuredFieldVariant_Mismatch_Should_FailClosed` |

### 14.6 Required evidence dimensions

The manifest declares these closed dimensions:

```text
SaveSurface =
    Draft | OrganizationUnit | Position | Membership | RoleAssignment | Rule

OrganizationEntitySurface =
    OrganizationUnit | Position | Membership | RoleAssignment

OrganizationIdentitySurface =
    OrganizationUnit | Position | Membership | RoleAssignment

OrganizationQuerySurface =
    Units | Positions | MembershipsByUser | MembershipsByUnit | RolesByUser

OrganizationReadSurface =
    UnitById | Units | PositionById | Positions |
    MembershipsByUser | MembershipsByUnit | RolesByUser

PersistedSnapshotRowSurface =
    Draft | OrganizationUnit | Position | Membership | RoleAssignment

StoreMethodSurface =
    DraftSave | DraftGet | DraftList |
    UnitSave | UnitGet | UnitList |
    PositionSave | PositionGet | PositionList |
    MembershipSave | MembershipsByUser | MembershipsByUnit |
    RoleSave | RolesByUser |
    RuleSave | RuleGet
```

The Spec, rather than the implementation manifest, is the oracle for every
remaining closed dimension:

```text
DescriptorPayloadVariant =
    Schema | Form | Capability | HumanTask | Event |
    WorkflowCapabilityTarget | WorkflowHumanTaskTarget |
    WorkflowSubWorkflowTarget

DraftQueryVariant =
    DescriptorKind | Operation | AuthorKind | Status |
    CreatedFrom | CreatedTo | Combined

DraftValidatorOwnedInvalidVariant =
    DraftIdBlank | DescriptorIdBlank | AuthorIdBlank |
    SupportedPayloadKindMismatch | DefinedNonPayloadKindMismatch |
    PayloadIdMismatch |
    ProposedVersionMissing | ProposedVersionNotInteger |
    ProposedVersionMismatch | CreateBaseVersionPresent |
    UpdateBaseVersionMissing | DeprecateBaseVersionMissing |
    RemoveBaseVersionMissing

IdentityValidationVector =
    DraftNullInstance | DraftNullTenantId | DraftNullDraftId |
    DraftNullPayload | DraftGetNullTenantId | DraftGetNullDraftId |
    DraftListNullTenantId |
    UnitNullInstance | UnitInvalidId | UnitInvalidNonNullTenant |
    PositionNullInstance | PositionInvalidId | PositionInvalidNonNullTenant |
    MembershipNullInstance | MembershipInvalidId |
    MembershipInvalidNonNullTenant | MembershipInvalidUserId |
    MembershipInvalidOrganizationUnitId | MembershipInvalidPositionId |
    RoleAssignmentNullInstance | RoleAssignmentInvalidId |
    RoleAssignmentInvalidNonNullTenant | RoleAssignmentInvalidUserId |
    RoleAssignmentInvalidRoleId |
    RoleAssignmentInvalidOrganizationUnitId |
    UnitPointReadInvalidId | PositionPointReadInvalidId |
    MembershipByUserInvalidUserId |
    MembershipByUnitInvalidOrganizationUnitId |
    RoleByUserInvalidUserId | OrganizationQueryInvalidNonNullTenant |
    RuleNullInstance | RuleInvalidResource | RuleInvalidNonNullTenant

PersistedEnumSurface =
    DraftDescriptorKind | DraftOperation | DraftAuthorKind | DraftStatus |
    RuleScopeKind

RuleSentinelField = Action | Permission | TenantId

RuleExactEmptyVariant = ActionEmpty | PermissionEmpty | BothEmpty

ScopedKeyCollisionVariant =
    StoreTenantDelimiter | StoreIdDelimiter |
    HierarchyTenantDelimiter | HierarchyIdDelimiter

MissingReferenceVariant =
    MembershipOrganizationUnit | MembershipPosition |
    RoleAssignmentOrganizationUnit | RoleAssignmentRole

OrganizationCreatedAtVariant =
    UnitNonZeroOffset | PositionNonZeroOffset |
    MembershipNonZeroOffset | MembershipHundredNanosecondOrder |
    RoleAssignmentNonZeroOffset | RoleAssignmentHundredNanosecondOrder

AotScenarioVariant =
    WorkflowCapabilityTarget | WorkflowHumanTaskTarget |
    WorkflowSubWorkflowTarget | Organization | Rule
```

`InvalidId` and `InvalidNonNullTenant` above expand to null where the static
contract permits runtime null, empty, and whitespace as applicable;
`InvalidPositionId` / `InvalidOrganizationUnitId` apply only when the optional
value is non-null. In D08, `DraftIdBlank` expands to empty and whitespace;
`DescriptorIdBlank` and `AuthorIdBlank` also include runtime null because those
values are JSON-representable. `SupportedPayloadKindMismatch` uses two unequal
payload-capable kinds, for example Workflow header plus Schema payload.
`DefinedNonPayloadKindMismatch` expands to `Unknown`, `DynamicApiEndpoint`,
`McpTool`, and `AgentTool` headers with a Schema payload. `ProposedVersionMissing`
covers both Create and Update. These Draft semantic inputs are deliberately not
members of `IdentityValidationVector`; D08 owns them.

For V03, `DraftDescriptorKind` means only an underlying integer which is not a
member of the currently defined `DescriptorKind` enum. A defined value without
a payload DTO family is D08 input, never V03 input.

Every duplicated domain field is also frozen as a `(RowSurface, Field)` member:

```text
PersistedStructuredFieldVariant =
    Draft.(TenantId | DraftId | PayloadDiscriminator | DescriptorKind |
           Operation | AuthorKind | Status | CreatedAtUtcTicks |
           CreatedAtReadableProjection) |
    OrganizationUnit.(TenantScope | Id | ParentId | SortOrder | IsActive |
                      CreatedAtUtcTicks | CreatedAtReadableProjection) |
    Position.(TenantScope | Id | IsActive | CreatedAtUtcTicks |
             CreatedAtReadableProjection) |
    Membership.(TenantScope | Id | UserId | OrganizationUnitId | PositionId |
                IsPrimary | IsActive | CreatedAtUtcTicks |
                CreatedAtReadableProjection) |
    RoleAssignment.(TenantScope | Id | UserId | RoleId | OrganizationUnitId |
                   IsActive | CreatedAtUtcTicks | CreatedAtReadableProjection)

PersistedSnapshotCorruptionVariant =
    DraftInvalidJson | DraftUnsupportedStateContractVersion |
    DraftInvalidPayloadDiscriminator | DraftInvalidWorkflowTargetUnionShape |
    OrganizationUnitInvalidJson |
    OrganizationUnitUnsupportedStateContractVersion |
    PositionInvalidJson | PositionUnsupportedStateContractVersion |
    MembershipInvalidJson | MembershipUnsupportedStateContractVersion |
    RoleAssignmentInvalidJson |
    RoleAssignmentUnsupportedStateContractVersion

PersistedRuleCorruptionVariant =
    InvalidTenantScopeKind | TenantScopeTupleMismatch |
    InvalidActionMatchKind | ActionWildcardValueMismatch |
    InvalidPermissionMatchKind | PermissionWildcardValueMismatch |
    InvalidScopeKind
```

`updated_at` is provider bookkeeping and is intentionally excluded from F09.
Readable `created_at` is not an ordering/filter fact, but it is included because
Section 8.3 promises validation against the microsecond-normalized UTC
projection. A Case entry declares its dimension; boundary evidence expands the
Cartesian requirement and cannot accept one representative member as coverage.

### 14.7 Migration, composition, architecture, and AOT

| ID | Required dimension | Case | Normative test |
|---|---|---|---|
| C01 PG | Migration | migration repeat | `ReapplyingMigration_Should_NotDriftSchema` |
| C02 PG | Migration | checksum drift | `MigrationValidation_Should_DetectChecksumDrift` |
| C03 PG | Migration | shape drift | `MigrationValidation_Should_DetectSchemaDrift` |
| C04 ARCH | Kernel | one DataSource/kernel | `Provider_Should_ReuseRuntimePersistenceKernel` |
| C05 ARCH | Kernel | no Runtime participant | `Provider_Should_NotExpandRuntimeRecoveryTransactionBoundary` |
| C06 ARCH | Contracts | no provider leakage | `StoreContracts_Should_NotExposeProviderTypes` |
| C07 ARCH | `OrganizationEntitySurface` | no Organization FKs | `OrganizationSchema_Should_NotContainCrossEntityForeignKeys` |
| C08 | Composition | explicit opt-in | `BaseProviderRegistration_Should_NotReplaceReferenceStores` |
| C09 | `SaveSurface` | opt-in replacement | `OptInRegistration_Should_ReplaceExactlySelectedStores` |
| C10 ARCH | Draft | no legacy Draft Store | `Provider_Should_NotImplementLegacyDraftStore` |
| C11 ARCH | Rule | no persisted derived scope | `Provider_Should_NotDefineDataPermissionScopeStore` |
| C12 AOT | `AotScenarioVariant` | native publish/run | `DurableControlPlaneReferenceDataAotFixture_Should_PublishLinkAndRun` |
| C13 ARCH | `DescriptorPayloadVariant` | recursive polymorphism closure | `DescriptorPayloadGraph_Should_HaveClosedAotPersistenceMapping` |
| C14 | Composition | feature without base fails clearly | `OptInWithoutBaseProvider_Should_FailWithClearCompositionError` |
| C15 | Composition | repeated base-first feature registration is idempotent | `RepeatedBaseFirstOptIn_Should_RemainIdempotent` |

`AotScenarioVariant` includes WorkflowCapabilityTarget,
WorkflowHumanTaskTarget, WorkflowSubWorkflowTarget, Organization, and Rule. The
fixture may execute them in one native process, but the sentinel is accepted
only when every sub-scenario marker is present.

---

## 15. Required Test Architecture

### 15.1 Shared runner-free contract kit

Create:

```text
tests/Shared/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing/
```

It is `IsTestProject=false`, references only the selected contracts/domain
models plus minimal assertions/helpers, and contains no xUnit runner,
Testcontainers, Npgsql, or provider implementation.

Its main drivers/suites are:

```text
IDescriptorDraftStoreContractDriver
IOrganizationStoreContractDriver
IDataPermissionScopeRuleStoreContractDriver
IDurableStoreContractDriver<TStoreDriver>
DescriptorDraftStoreContractCases
OrganizationStoreContractCases
DataPermissionScopeRuleStoreContractCases
ControlPlaneReferenceDataCaseManifest
ControlPlaneReferenceDataSpecTestSkeleton
```

Each domain driver exposes only its own Store and reset/lifecycle hooks. The
Descriptor Draft tests never need to reference Organization implementation, and
the Organization tests never need to reference Descriptor Draft implementation.
The PostgreSQL runner composes all three drivers at its provider fixture layer.
The generic durable capability supplies fresh-provider/process hooks without
making restart a requirement of an InMemory driver.

### 15.2 InMemory runners

The existing Descriptor Draft and Organization test projects run all shared
semantic cases against the real InMemory implementations. They do not copy case
logic into provider-specific tests.

### 15.3 PostgreSQL runners

`CrestCreates.Runtime.Persistence.PostgreSql.Tests` runs the same cases against
one isolated Testcontainers schema per test/fixture and adds:

- fresh ServiceProvider restart;
- independent process restart;
- two-writer concurrency barriers;
- provider failure translation;
- migration/manifest drift;
- corrupt-row fail-closed tests;
- ambient Runtime transaction rejection.

P13 has two independent PostgreSQL evidence legs. First, raw invalid DML against
the intact V011 schema must be rejected by the relevant CHECK. Second, in a
disposable isolated test schema only, the fixture removes the specific CHECK,
inserts the malformed authority row, and reads through the real Store to prove
provider materialization still fails closed. The schema is discarded after the
case; production code exposes no corruption bypass.

### 15.4 Crash worker

The existing PostgreSQL CrashWorker is extended with selected Store scenarios.
Fault points are around the real top-level COMMIT boundary, not a mocked
repository. The parent process kills/restarts the worker and reads through a
fresh provider.

### 15.5 Evidence manifest

Boundary tests parse the typed case manifest and prove:

- every Spec Case ID is unique and mapped;
- every Case declares a closed required dimension;
- every required `Case × Surface × Variant × Runner` tuple exists;
- every shared tuple has InMemory and PostgreSQL runners;
- every PG/ARCH/AOT tuple exists in the required runner;
- D01 covers all eight `DescriptorPayloadVariant` values;
- F01–F05 and F08 cover all six `SaveSurface` values;
- V05 and F06 cover all sixteen `StoreMethodSurface` values;
- O01/O02 cover all four `OrganizationIdentitySurface` values;
- O16 covers all four `OrganizationEntitySurface` values directly;
- D02/D03 and O20/O21 cover every applicable snapshot write/read surface;
- D08 covers all twelve current validator diagnostic codes and every declared
  input expansion in `DraftValidatorOwnedInvalidVariant`, proving both
  persistence round trip and the existing validator diagnostic;
- F07 covers every `PersistedSnapshotCorruptionVariant`;
- F09 covers every enumerated `(RowSurface, Field)` member of
  `PersistedStructuredFieldVariant`;
- P13 covers every `PersistedRuleCorruptionVariant` through both schema and
  provider fail-closed evidence where applicable;
- C14/C15 prove the explicit base-first composition and idempotence contracts;
- no placeholder, skipped default, or obsolete test is counted as evidence.

---

## 16. Implementation Slices for the Later Plan

This Spec does not implement the phase. The later implementation plan should
preserve this dependency order:

1. Freeze case manifest and shared semantic contract kit.
2. Freeze representation-versus-semantic validation ownership, then harden
   InMemory typed identities, representation validation, cancellation, and
   ordering without moving Draft diagnostics into the Store.
3. Introduce the shared typed Organization scoped key, remove delimiter keys
   from Store and Hierarchy, and stabilize deterministic identity projection.
4. Add provider-owned recursive Descriptor persistence DTO unions, generated
   durable snapshot roots, exact-tick materialization, and representation tests.
5. Append V011 and extend complete schema validation.
6. Reuse the existing top-level commit mode and add Descriptor Draft Store.
7. Add Organization Store without cross-entity FKs.
8. Add Data Permission Rule Store with typed wildcard priority, closed schema
   checks, and fail-closed authority-row materialization.
9. Add opt-in composition and architecture guards.
10. Add restart, process crash, concurrency, corruption, and migration evidence.
11. Extend linux-x64 NativeAOT publish-link-run and close evidence ledger.

No PostgreSQL table should be implemented before the shared semantic cases that
define its observable behavior are red.

---

## 17. Rejected Approaches

### 17.1 Provider-only optimistic concurrency

Rejected because the Store contracts provide no expectation token. Hidden xmin
or revision checks would make PostgreSQL semantically different from InMemory.

### 17.2 Persisting `DataPermissionScope`

Rejected because Scope is derived runtime context. Persisting it creates stale
authorization state and a second authority.

### 17.3 Treating null Organization collection scope as global

Rejected for #69 because it contradicts current implementation and tests. A
future explicit Organization query-scope cutover may replace the nullable API.

### 17.4 Database FKs for Organization relationships

Rejected because current domain Saves permit missing parents/references and
hierarchy defines the observable missing-parent behavior.

### 17.5 Joining Runtime ambient transactions

Rejected because it expands #24 recovery participants and makes a reference
Save return before its durable commit is known.

### 17.6 Hidden independent commit inside a Runtime transaction

Rejected because it surprises the caller with a committed side effect despite
outer rollback. Ambient writes fail before mutation.

### 17.7 A second PostgreSQL kernel or ORM

Rejected because #24 already provides the DataSource, migrations, failure
taxonomy, tests, and AOT foundation.

### 17.8 PostgreSQL-only deterministic ordering

Rejected because provider switching would change observable results. Ordering
is first frozen in the shared contract and InMemory implementation.

### 17.9 JSON-only unindexed tables

Rejected because identity, filtering, rule priority, order, and persisted
invariant checks need structured columns.

### 17.10 Fully relational Descriptor Draft payloads

Rejected because it duplicates descriptor schemas and makes each descriptor
evolution a persistence migration. Complete generated JSON plus structured
query columns is the correct boundary.

### 17.11 Reflection polymorphism or CLR type names

Rejected by NativeAOT-first and security/versioning requirements.

### 17.12 Durable-enabling legacy `IDraftStore`

Rejected because it preserves a second draft mainline unrelated to Agent
Control Plane's `IDescriptorDraftStore`.

### 17.13 Using `timestamptz` as the exact time key

Rejected because its microsecond representation cannot prove parity with
`.NET` 100-nanosecond `UtcTicks`. It remains only a readable UTC projection;
`created_at_utc_ticks` is the semantic filter and ordering fact.

### 17.14 Adding persistence-only polymorphism metadata to domain contracts

Rejected because database representation must not make Descriptor or Workflow
domain contracts provider-aware. Closed recursive unions belong to the
PostgreSQL provider persistence DTO boundary.

---

## 18. Review Guardrails

A review must block the implementation if any of these appears:

- a new `IDataPermissionScopeStore`;
- an `IDraftStore` PostgreSQL implementation in this phase;
- activation request durability claimed through Descriptor Draft persistence;
- `RuntimeConcurrencyException` from a blind selected Store Save;
- xmin/revision predicates absent from the domain contract;
- a second DataSource, migration runner/history, ORM context, or provider tier;
- Organization relationship FKs or cascade behavior;
- string-concatenated composite keys in Store **or hierarchy** logic;
- `"*"` or empty string carrying identity/wildcard meaning by itself; an empty
  provider storage value is allowed only when an explicit scope/match-kind
  discriminator and schema check carry the actual meaning;
- PostgreSQL collection order without matching InMemory contract cases;
- null Organization collections reinterpreted as global-only;
- Data Permission priority where global exact outranks tenant wildcard;
- generic four-combination Data Permission fallback that makes
  WildcardAction/ExactPermission match a non-null requested Action;
- `timestamptz` used as the exact Draft time predicate or Organization order
  fact instead of `created_at_utc_ticks`;
- top-level payload discrimination that leaves a nested abstract/interface
  union, including `WorkflowStep.Target`, to runtime STJ polymorphism;
- domain polymorphism attributes added only to serve PostgreSQL persistence;
- one representative Store/payload counted as evidence for a required
  Case × Surface/Variant dimension;
- DraftId blank, DescriptorId/AuthorId blank or null, kind/payload mismatch,
  payload ID mismatch, or version consistency rejected by the Store instead of
  being round-tripped for `IDescriptorDraftValidator`;
- draft validator/materializer logic copied into persistence;
- Organization identity isolation evidenced only for OrganizationUnit;
- one mismatch per table counted as complete structured-field agreement
  evidence;
- an invalid Rule discriminator/scope kind reaching an authorization result, or
  schema checks treated as a substitute for provider fail-closed validation;
- feature opt-in silently succeeding without the base Provider Kernel;
- raw provider exceptions crossing the Store boundary;
- automatic retry of a commit-unknown write;
- a reference Save joining or silently bypassing an ambient Runtime transaction;
- reflection JSON fallback or assembly-qualified payload type names;
- a NativeAOT claim without real PostgreSQL publish-link-run execution.

---

## 19. Exit Criteria

Phase 9b+ #69 is complete only when:

1. The three selected interfaces resolve to PostgreSQL through explicit opt-in
   without changing their public signatures; base-first and repeated opt-in
   composition satisfy C14/C15.
2. InMemory and PostgreSQL pass the same shared semantic cases.
3. Every current validator-owned invalid Draft variant survives Store round trip
   and still produces the existing `IDescriptorDraftValidator` diagnostic.
4. Descriptor Draft, all four Organization entities, and Data Permission rules
   survive provider reconstruction and real process restart.
5. Same logical IDs remain isolated across global and tenant scopes for Unit,
   Position, Membership, and RoleAssignment directly.
6. Explicit tenant queries never return another tenant's data.
7. Every observable enumeration and Organization projection has the frozen
   deterministic order.
8. Descriptor Draft time filters preserve inclusive 100ns boundaries, compare
   same-instant/different-offset values correctly, and return the original
   non-zero offset from JSON.
9. All six Save surfaces prove complete concurrent winners and no false OCC.
10. All six Save surfaces prove crash-before/after-commit, commit-unknown, and
   ambient Runtime transaction rejection semantics.
11. Every Store method proves pre-cancellation, and every snapshot-bearing
    surface proves snapshot-on-write/read through the required dimensions.
12. Every duplicated structured field mismatch fails closed according to F09;
    only provider-owned `updated_at` is excluded, while readable timestamp
    projections are checked according to Section 8.3.
13. Every Rule corruption variant is blocked by schema and/or provider
    validation as applicable, and none becomes an authorization decision.
14. Primary membership order includes normalized scope, and Store/Hierarchy
    typed keys prove delimiter-containing identities cannot alias.
15. Data Permission empty-exact and WildcardAction/ExactPermission behavior is
    frozen without broadening the existing candidate set.
16. Organization persistence introduces no new FK/integrity semantics.
17. V011 is checksummed, repeatable, and fully schema-manifest validated.
18. The Provider Kernel remains single and #24 Runtime recovery transactions
    have no new participants.
19. No provider details leak into Descriptor Draft or Organization abstractions.
20. Every Descriptor payload and nested union has a closed provider-owned AOT
    mapping; D01 and C13 cover all required variants.
21. The linux-x64 NativeAOT fixture publishes, links, executes the three
    Workflow target variants plus Organization and Rule scenarios against real
    PostgreSQL, and emits all sub-markers and the required final sentinel.
22. Every closed evidence dimension has exactly the members frozen in Section
    14.6; implementation manifests cannot silently redefine completeness.
23. The evidence manifest expands and satisfies every active
    `Case × Surface × Variant × Runner` tuple.

Only then may documentation describe these selected Stores as
`NativeAOT-verified` durable PostgreSQL providers. It must not describe the
entire Agent Control Plane as durable.
