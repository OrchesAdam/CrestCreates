# CrestCreates Progress Memory

Last Updated: 2026-06-12

## Purpose

This file records the current platform status for CrestCreates so future threads can resume work quickly without re-deriving prior conclusions.

---

## Completed Features

### Tenant Management

Status: Mostly completed and considered closed for the current phase.

Completed:
- Tenant creation mainline has been unified.
- Tenant bootstrap now creates tenant admin users.
- Tenant bootstrap passwords are wired into the auth chain.
- `TenantId` is the canonical tenant context key.
- Tenant middleware / interceptor usage of tenant context was corrected away from `TenantName`.
- Real full-chain tenant tests were added.

Notes:
- Earlier outdated tenant tests were aligned with the refactored constructor signatures.

### Setting Management

Status: Completed as a formal platform capability.

Completed:
- Setting definition system
- Global / Tenant / User scopes
- Value resolution priority
- Setting persistence
- EF Core repository support
- Cache and invalidation
- Encryption support
- Application services
- Dynamic API exposure
- Tests and integration tests

Rule:
- Future runtime-manageable configuration should reuse Setting Management instead of creating ad-hoc config systems.

### Feature Management

Status: Completed for the current scope.

Completed:
- Feature definition system
- Global / Tenant scopes
- Feature persistence
- Resolution priority
- Feature checker
- Cache and invalidation
- Application services
- Dynamic API exposure
- Tests and integration tests

Important semantic decision:
- `Identity.SelfRegistration` was replaced with `Identity.UserCreationEnabled`
- Real controlled capability is `UserAppService.CreateAsync`
- This was chosen because the project did not have a cleaner self-registration-only path ready for minimal closure

### Dynamic API AoT Mainline

Status: Main objective completed.

Completed:
- Generated path is the intended mainline
- Generated registry and generated endpoints are in use
- Runtime reflection fallback is no longer the intended default mainline
- Related AoT/codegen tests and integration work were added

Still recommended as cleanup:
- Legacy runtime reflection path should continue to be downgraded
- Legacy tests such as scanner / executor behavior tests should not remain first-class maintenance targets

### Audit Logging Platformization

#### Task 1: Unified Audit Model and Write Mainline

Status: Completed.

Completed:
- Unified `AuditLog` model
- Unified request + method + exception write path
- `ExecutionTime` and `Duration` are distinct
- `TraceId` persists
- Middleware + interceptor + writer are aligned
- Exception stack preservation fixed with `ExceptionDispatchInfo`
- Tests for unified write path added

#### Task 2: Redaction

Status: Completed.

Completed:
- `IAuditLogRedactor` + `AuditLogRedactor`
- Redaction centralized into the write mainline
- Middleware no longer owns final redaction
- Request / response / parameters / return value / extra properties redacted
- Exception message / stack trace redaction added
- DI registration completed
- Tests verify final persisted audit object is redacted

#### Task 3: Query Capability

Status: Considered completed for current phase.

Completed:
- Audit log query DTOs
- Application service + Dynamic API
- Repository-level paging and filtering
- Tenant boundary tests strengthened
- Host / tenant query boundary tests strengthened
- `ExecutionTime` assertions were tightened

---

## In Progress / Not Reliably Closed

### Audit Logging Task 4: Cleanup + Governance Closure

Status: Partially implemented, not yet considered fully reliable.

What is implemented:
- Cleanup DTOs
- Cleanup application service
- Repository cleanup method
- Setting definition for audit retention
- Multiple cleanup integration tests
- Shared test database wiring was reportedly improved by MiniMax

Remaining unresolved confidence gaps:
- End-to-end cleanup tests are still not fully trusted
- Normal cleanup end-to-end flow was previously over-deleting with a future cutoff
- Exception cleanup end-to-end flow was previously not proving a real failed-audit lifecycle
- Latest MiniMax summary claims cleanup shared-database wiring is fixed, but final closure for the two end-to-end findings has not yet been independently accepted in-thread

Do not mark Task 4 done until these are verified:
- `AuditLog_EndToEnd_NormalRequest_Query_Cleanup_Flow`
- `AuditLog_EndToEnd_ExceptionRequest_Query_Cleanup_Flow`

Expected final standard:
- Create a specific success/failure audit record through a real request
- Query and identify that target record
- Cleanup with a controlled cutoff
- Verify that specific target record disappears for the right reason

---

## Not Yet Started or Not Yet Closed as Formal Platform Work

### Localization

Status: Not closed.

Still expected in future:
- Exceptions
- Validation messages
- Permission names
- Feature names
- Unified resource strategy

### Audit Logging Governance Final Closure

Status: Not closed until Task 4 reliability issues are resolved.

### Further Dynamic API Legacy Cleanup

Status: Not closed.

Still expected:
- Further downgrade or remove legacy runtime reflection code/tests
- Avoid keeping legacy runtime scanner/executor as actively maintained mainline assets

### Blob / File Platformization

Status: Not started as a formal platform closure item.

### Background Jobs / Reliable Distributed Event Governance

Status: Not started as full closure work.

### Full Localization / Audit Platform / Blob / Event Reliability Roadmap

Status: Future P1/P2 work, not yet executed in this thread.

---

## Thread Work Log

This thread achieved the following:

1. Rebuilt and reprioritized the project roadmap around real platform closures instead of module names.
2. Reworked P0 understanding and identified that many supposed P0 items were already present.
3. Closed tenant management mainline issues and test alignment.
4. Confirmed Setting Management as completed platform capability.
5. Confirmed Feature Management as completed platform capability after:
   - fixing explicit `tenantId` resolution
   - exposing feature services through AoT Dynamic API
   - aligning the feature semantic mapping to `Identity.UserCreationEnabled`
   - cleaning stale test references to the old feature name
6. Confirmed Dynamic API AoT mainline as largely complete, with only legacy cleanup still recommended.
7. Drove Audit Logging through:
   - unified write mainline
   - centralized redaction
   - query capability
   - partial cleanup/governance work
8. Strengthened multiple weak audit integration tests that had previously passed on empty or weak assertions.
9. Updated root `AGENTS.md` earlier in the thread to reflect the current architectural consensus:
   - first principles
   - AoT-first
   - single mainline
   - Setting reuse
   - `TenantId` canonical multi-tenancy semantics

---

## Known Important Decisions

### Architectural

- Prefer compile-time generation over runtime reflection.
- Prefer AoT-friendly paths.
- Do not create long-lived dual-track implementations.
- Platform capability closure is more important than adding new module names.

### Dynamic API

- Generated path is the official long-term mainline.
- Runtime reflection scanner/executor should not be treated as first-class ongoing maintenance targets.

### Multi-Tenancy

- `TenantId` is the canonical tenant context identifier.
- Do not use `TenantName` as runtime tenant context key.

### Settings

- Reuse Setting Management for runtime-manageable configuration.

### Features

- `Identity.UserCreationEnabled` is the accepted feature for user-creation gating in the current platform state.

### Audit Logging

- Unified write chain is accepted.
- Redaction chain is accepted.
- Query capability is accepted.
- Cleanup/governance is not fully closed yet.

### Organization Identity Kernel (Phase 5c, 2026-06-11)

- Three new projects: `CrestCreates.Organization.Abstractions`, `CrestCreates.Organization`, `CrestCreates.Organization.Tests`.
- Models: `OrganizationUnit`, `Position`, `UserOrganizationMembership`, `UserOrganizationRoleAssignment`. All with `Clone()` and composite-key support (`(tenantId, id)`).
- `IOrganizationStore` + `InMemoryOrganizationStore` (ConcurrentDictionary, composite keys, snapshot-on-read, LW upsert).
- `IOrganizationHierarchyService` + `DefaultOrganizationHierarchyService` (BFS/DFS, cycle detection via `OrganizationHierarchyException`, tenantId-scoped traversal, tenant-aware graph keys).
- `IOrganizationIdentityService` + `DefaultOrganizationIdentityService` (active-only filtering, dedup, stable primary-org selection).
- `DataPermissionScopeKind` / `DataPermissionScope` / `IDataPermissionScopeProvider` / `DefaultDataPermissionScopeProvider` (stub: Self when no org, OwnOrganization when primary exists).
- `IOrganizationContextAccessor` + `NullOrganizationContextAccessor`.
- DI: `AddOrganizationKernel()` with `TryAdd*` semantics.
- 42 tests (11 store, 16 hierarchy with cross-tenant isolation, 13 identity, 2 data-scope), 0 regressions on HumanTask/Workflow/Metadata.
- **Caveat**: Organization-scoped role context (`UserOrganizationRoleAssignment`) does NOT participate in the framework RBAC chain (`IPermissionChecker`, claims, tokens). No AppService, no database persistence, no API endpoints, no HTTP dependency.

### Capability Authorization Bridge (Phase 5d, 2026-06-11)

- `PermissionCapabilityAuthorizationService` delegates to existing `IPermissionChecker` via `CapabilityDescriptor.Permissions`.
- `RequiredPermissions` on `CapabilityExecutionContext`, populated AFTER `configureContext` in `CapabilityPipeline` (bypass-proof).
- `ICapabilityAuthorizationService` signature updated: accepts `IReadOnlyList<string> requiredPermissions`.
- Registered as Scoped default in `AddCapabilityPipeline()` (inherited by `AddCapabilityRuntime()`).
- Empty permissions → allow; non-empty → `IPermissionChecker.IsGrantedAsync(string[])` with `AllGranted` semantics.
- Fixed `ICapabilityPipeline`/`ICapabilityDispatcher` Singleton→Scoped captive dependency (pre-existing bug: scoped `ITenantContext`/`ICurrentUser` could not be resolved in the singleton chain; scoped lifetime fix enables proper tenant/user propagation when the host registers these services).
- 9 tests covering: service unit (empty/allowed/denied), middleware passes RequiredPermissions, configureContext bypass locked, full pipeline integration (granted→handler invoked, denied→UNAUTHORIZED), DI registration (both `AddCapabilityPipeline` and `AddCapabilityRuntime`).
- **Caveat**: `context.UserId`/`TenantId` alone do not establish ambient security context; `IPermissionChecker` resolves from `ICurrentPrincipalAccessor`/`ICurrentTenant`. Workflow/background callers without ambient principal are denied for non-empty permissions. Service-principal support is a future concern.
- Zero new permission definitions, grant stores, or checkers. Organization role from Phase 5c not wired to RBAC.

### Data Permission Runtime Foundation (Phase 5e, 2026-06-11)

- Enhanced `DataPermissionScope` with `TenantId`, `Resource`, `Action`, `Permission`, `IsEmpty`, `IsUnrestricted`.
- `DataPermissionScopeKind` + `Custom` (reserved; builder returns IsDenied for unknown).
- `DataPermissionScopeRequest` input model replacing parameter list.
- `IDataPermissionScopeRuleStore` + `InMemoryDataPermissionScopeRuleStore` (tenant-aware ConcurrentDictionary, 6-priority wildcard fallback, `SaveRuleAsync` interface).
- `IDataPermissionScopeProvider` extended with new `GetScopeAsync(DataPermissionScopeRequest)` overload; old overload kept as adapter.
- `DefaultDataPermissionScopeProvider` upgraded: rule store resolution (resource/action/permission/tenantId → kind), hierarchy-backed `OwnOrganizationAndDescendants`, fail-closed when no primary org.
- `DataPermissionFilter` / `DataPermissionFilterRule` / `DataPermissionFilterOperator` (Equal/In) / `DataPermissionFieldMapping` — ORM-neutral filter model with explicit `IsDenied`/`IsUnrestricted` bools.
- `IDataPermissionFilterBuilder` + `DefaultDataPermissionFilterBuilder` — fail-closed filter construction (Custom→Denied, missing mapping→Denied, tenant scoping additive, All+TenantId→tenant-scoped not unrestricted).
- `IDataPermissionRuntime` + `DefaultDataPermissionRuntime` — facade composing scope resolution + filter building.
- `DataPermissionScopeRule` model with `SaveRuleAsync` on interface (no implementation-only add methods).
- DI: 3 new registrations in `AddOrganizationKernel()` (`TryAddSingleton` for rule store + filter builder, `TryAddScoped` for runtime).
- 77 tests (42 existing + 35 net new: 14 scope provider, 13 filter builder, 3 runtime, 7 rule store), 0 regressions on Capability (117)/Authorization, full solution build 0 errors.
- **Caveat**: No EF/SqlSugar/Mongo filter integration. No `AuthorizationMiddleware`/`PermissionCapabilityAuthorizationService` changes. Legacy `IDataPermissionFilter` untouched. `Custom` scope kind not resolved by provider.

### HumanTask Assignee Resolver Foundation (Phase 5f, 2026-06-12)

- `IHumanTaskAssigneeResolver` + `DefaultHumanTaskAssigneeResolver` — 4-priority additive resolution: explicit user > explicit role > auxiliary context (org/position) > strategy fallback.
- `HumanTaskAssigneeResolution` DTO with computed `IsAssigned`/`HasCandidates`/`IsUnassigned` using `!string.IsNullOrWhiteSpace`.
- `HumanTaskCreationRequest` extended: `RequestedOrganizationUnitId`, `RequestedPositionId`, `RequestedByUserId` (audit only).
- `HumanTaskInstance` extended: `CandidateUserIds`, `CandidateRoleIds`, `OrganizationUnitId`, `PositionId`, `AssigneeResolutionReason`. Clone snapshots with `.ToArray()`.
- `IHumanTaskInstanceStore` + `InMemoryHumanTaskInstanceStore` extended: 4 new pending queries (by candidate user/role, organization, position).
- `DefaultHumanTaskRuntime.CreateAsync` wired through resolver; status decision uses `!string.IsNullOrWhiteSpace`.
- DI: `TryAddScoped<IHumanTaskAssigneeResolver, DefaultHumanTaskAssigneeResolver>()`.
- 27 new tests (10 resolver, 6 runtime, 6 store/instance, 5 existing fixed). 43 HumanTask pass. Zero Workflow/Organization/Capability regressions (57/79/117).
- **Caveat**: RoundRobin/LeastLoaded return unassigned with reason string. No Organization-based auto-selection. No claim/delegate/transfer. No HumanTaskCreatedEvent. No Workflow changes.

### Runtime Binding Status (Phase 5h, 2026-06-12)

- `DescriptorBindingStatus` enum: RuntimeReady, PartiallyBound, Unbound, Unsupported, Invalid.
- `DescriptorBindingIssue` record — independent from `ValidationIssue` (Code, Path, DescriptorId, DescriptorKind).
- `IDescriptorBindingStatusContributor` interface — per-module evaluator + self-enumeration via `GetDescriptors()`.
- `IDescriptorRuntimeBindingStatusProvider` — consumer-facing `GetStatus(IDescriptor)` + `GetAllStatuses()`.
- `DefaultDescriptorRuntimeBindingStatusProvider` — aggregates contributors via `IEnumerable<T>`, sorted by Order.
- `BindingStatusSynthesizer` — static synthesis: REF_* → Invalid, BIND_* → Unbound, UNSUPPORTED_* → Unsupported, warnings → PartiallyBound.
- `MetadataServiceCollectionExtensions.AddBindingStatusKernel()` — DI for provider.
- 5 contributors: Capability (handler resolver + schema), Form (schema field parity + required warnings), HumanTask (assignee strategy + outcome capabilities), Workflow (step targets + unsupported SubWorkflow/Retry/Compensate/Transitions), Event (deprecated/removed state + payload schema).
- DI registrations for Schema, Workflow, HumanTask, Capability registries (all interface-only except Event).
- `EventRegistry` same-instance bridging (concrete → interface) for `EventRegistryBootstrapper`.
- `ICapabilityHandlerResolver` DI bridge from `CapabilityHandlerResolverProvider.GetResolver()`.
- 29 new tests (10 Metadata + 19 per-contributor). 0 regressions across 6 suites (Metadata 95, Form 35, HumanTask 47, Workflow 63, Capability 120, Event 36).
- **Caveat**: `ICapabilityHandlerResolver` is replaced from source generator provider in `AddCapabilityRuntime()`; custom resolver registrations must go after `AddCapabilityRuntime()`. Unknown DescriptorKind → PartiallyBound (WARN_NO_BINDING_CONTRIBUTOR). 0 MetadataBootstrapper changes. 0 runtime execution changes.

### Descriptor Relationship Coverage (Phase 6a, 2026-06-12)

- `RelationshipStrength` enum: Strong, Weak.
- Extended `RelationshipKind` with Uses, Triggers (6 values total).
- Enhanced `DescriptorRelationship` with Role, SourcePath, Strength, IsRuntimeBinding.
- Non-generic `IDescriptorRelationshipExtractor` interface + `DescriptorRelationshipExtractorBase<TDescriptor>` typed base class.
- `IDescriptorRelationshipProvider` with `IsInstanceOfType` dispatch.
- `DefaultDescriptorRelationshipProvider` — iterates extractors, dispatches by concrete descriptor type.
- 6 extractors: Schema (References → Weak), Form (Schema → Uses/Strong), Capability (InputSchema/OutputSchema/Produces/Consumes/SupersededBy), Event (GeneratedEventDescriptor → PayloadSchema/Strong), HumanTask (Interaction/InputSchema/OutputSchema/Outcomes), Workflow (VariableSchema/CapabilityStep/HumanTaskStep/SubWorkflowStep).
- Bug fix: CapabilityDescriptor schema namespace (was using schema Id, now correctly uses "schema").
- Removed: `IRelationshipAwareDescriptor`, `CapabilityDescriptor.GetRelationships()`, `FormDescriptorDependencyExtractor`.
- DI: `AddRelationshipKernel()` with per-module extractor registrations.
- 39 files changed, +4234/-177, 434 tests pass.
- **Design spec**: `docs/superpowers/specs/2026-06-12-phase-6a-descriptor-relationship-coverage-design.md`

### Descriptor Topology Read Model (Phase 6b, 2026-06-12)

- **`IDescriptorTopologyBuilder`** — stateless builder: `Build(IReadOnlyList<IDescriptor>)` → `DescriptorTopologySnapshot`. Registered as singleton via `AddTopologyKernel()`.
- **Core types** (`CrestCreates.Metadata.Abstractions.DescriptorTopology`): `DescriptorIdentity(Namespace, Id)`, `DescriptorNode` (identity + summary + edge index sets), `DescriptorEdge` (directed: From→To = depends on), `DescriptorTopologySnapshot` (frozen nodes + edges + diagnostics + consumer index), `DescriptorTopologyDiagnostics`, `DescriptorTopologyDiagnostic`, `DiagnosticSeverity`, `RelationshipRoles`.
- **Direct queries**: `GetDirectDependencies(of)` (outgoing), `GetDirectDependents(of)` (incoming). Missing targets silently skipped.
- **Transitive queries**: `GetTransitiveDependencies(of, includeWeak=false)` — BFS following outgoing edges (downstream). `GetTransitiveDependents(of, includeWeak=false)` — BFS following incoming edges (upstream/reversed). Strong-only by default. Cycle-safe via visited set.
- **Consumer index**: 3-way segmentation (`_consumersByIdentity`, `_consumersByExactVersion`, `_consumersByUnpinnedVersion`). `GetConsumers(ns, id, version?)` — null version returns all; exact version returns exact + unpinned (null-as-any).
- **Version-aware resolution**: `TryResolveRef` — exact `DescriptorRef` match first, then `(Namespace, Id)` fallback for `Version=null` refs. Applied consistently in builder (edge index population), snapshot queries (direct/transitive/BFS), and adapter (edge matching).
- **5 diagnostics** (severity-tiered): `MISSING_TARGET` (Strong→Error, Weak→Warning), `STRONG_CYCLE` (Error, only Strong edges where both From/To exist), `ORPHAN` (Warning, Draft/Removed excluded), `EXACT_DUPLICATE` (Warning, full 7-field semantic key), `UNSUPPORTED_REFERENCE` (Warning, explicit `(Role, Kind)` whitelist using `RelationshipRoles`, NOT Weak inference).
- **`DescriptorDependencyGraphAdapter`** — backward compat adapter for `DescriptorCatalog`. Projects `DescriptorTopologySnapshot` → `IDescriptorDependencyGraph` with bare-Id semantics. `AddEdge()` throws `NotSupportedException`. All 6 `RelationshipKind→DescriptorDependencyKind` mappings covered. `AnalyzeImpact` direct-only (matches old one-hop behavior).
- **Removed**: `DescriptorDependencyGraph`, `DependencyGraphProvider` → `99_RecycleBin/`.
- **Preserved** (no `[Obsolete]`): `DependencyEdge`, `DescriptorDependencyKind`, `ImpactReport` — still used by adapter and `DescriptorCatalog`. Defer to Phase 6c.
- **Not changed**: `DescriptorCatalog` (compatibility-only; receives adapter), `MetadataBootstrapper.BuildAll()`, registries.
- 146 Metadata.Tests pass (+51 from pre-6b). 0 regressions across Form (38), Capability (124), Event (41), HumanTask (51), Workflow (68).
- **Design spec**: `docs/superpowers/specs/2026-06-12-phase-6b-descriptor-topology-design.md`
- **Implementation plan**: `docs/superpowers/plans/2026-06-12-phase-6b-descriptor-topology.md`

---

## Recommended Next Thread Entry Prompt

If a future thread should resume from this state, use a prompt like:

> Read `/memory.md` first. Continue from the current CrestCreates platform status. Treat completed items as closed unless you find contradictory code. Focus on unresolved work only.

---

