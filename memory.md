# CrestCreates Progress Memory

Last Updated: 2026-06-30 (Issues #35 + #46 closed)
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

10. Reorganized the repository into layer-oriented physical roots:
    - `src/Core`
    - `src/Metadata`
    - `src/Framework`
    - `src/Runtime`
    - `src/Persistence`
    - `src/Integrations`
    - `src/Platform`
    - `tests/*`
    - `solutions/*`
11. Added new focused solution files under `solutions/`, with `solutions/CrestCreates.All.slnx` as the canonical full solution.
12. Preserved existing public namespaces and project identities during the physical move to avoid coupling folder migration with API breakage.
13. Added dependency-boundary tests for Core/Metadata/Runtime project-reference constraints.
14. Agent Control Plane descriptor visibility closure (Issue #40) — multiple review rounds:
    - Audited PR-A/PR-B/PR-C implementation against design spec — identified 10 findings (2 critical, 3 medium, 4 low, 1 build); fixed all 10
    - First external code review (8 findings: 4 P1, 4 P2) — all verified and fixed: nested projection defense-in-depth, visible-universe-first package construction, explicit denied kind → Denied, single DI policy truth, null-on-projection-failure, audit dedup, two-pass anti-probing resolver, memory.md accuracy
    - Second review (9 findings: 5 P1, 4 P2) — 2026-06-20: all 9 resolved (nested projection, readiness, draft-kind deny, comparison validation, package projector new-descriptor fix, evidence filtering, audit contract, TOCTOU single-snapshot, memory.md accuracy)
    - Third review (7 findings: 4 P1, 3 P2) — 2026-06-20: resolved with recursive nested projection, (Ns,Id) identity model, namespace-aware comparison, invalid vs denied error code split, and 8 regression tests (226 total)
    - Fourth review (8 findings: 4 P1, 4 P2) — 2026-06-20: derived summaries leaking through MaxSeverity/MaxLevel/MaxDecision/evidence maxima, version-ignoring identity (v1 visible makes denied v2 visible), BaseVersion ignored in draft comparison, projection failure silently persisted as mutation success, package bare-ID contract limitation (deferred), empty paths leaking traversal existence, test coverage gaps, memory.md test count inconsistency. All resolved except P2 package contract (deferred as type-design issue beyond projector scope).
    - 226 ControlPlane + 7 Boundary tests pass, full solution build 0 errors

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
- Models: `OrganizationUnit`, `Position`, `UserOrganizationMembership`, `UserOrganizationRoleAssignment`. All with `Snapshot()` (ISnapshotable<T>) and composite-key support (`(tenantId, id)`).
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

### Impact Analysis Engine (Phase 6c, 2026-06-13)

- `IDescriptorImpactAnalyzer` — consumes `DescriptorTopologySnapshot` + `DescriptorChangeSet`, BFS upstream traversal, per-terminal-segment severity with RuntimeBoost (Descriptor→Runtime upgrade, no double-counting), fan-out-safe unpinned resolution, advisory edge filtering, depth limiting.
- `IDescriptorChangeSetBuilder` — diffs `before`/`after` `IReadOnlyList<IDescriptor>` inventories into `DescriptorChangeSet` with state-aware transition detection (Removed/Deprecated/Activated/StateChanged/ContractHashChanged/Updated) and priority dedup.
- Core types (15 files under `DescriptorImpact/`): 3 enums (`DescriptorChangeKind`, `DescriptorImpactSeverity`, `DescriptorImpactRuntimeArea`), 10 records (`DescriptorChange`, `DescriptorChangeSet`, `DescriptorImpactPathSegment`, `DescriptorImpactPath`, `AffectedDescriptor`, `DescriptorImpactDiagnostic`, `DescriptorImpactAnalysisReport`, `DescriptorImpactAnalysisOptions`), 2 interfaces (`IDescriptorImpactAnalyzer`, `IDescriptorChangeSetBuilder`).
- 3 diagnostic code categories: `IMPACT_TOPOLOGY_*` (MISSING_TARGET, STRONG_CYCLE, UNSUPPORTED_REFERENCE — re-mapped from topology snapshot), `IMPACT_*` (AMBIGUOUS_UNPINNED_TARGET, UNRESOLVED_CONSUMER, PATH_TRUNCATED, SKIPPED_WEAK_PATH).
- Analyzer builds internal 3-index lookup (exact, identity, fan-out-aware impactIncoming) from `topology.Nodes`/`topology.Edges` — does NOT use `DescriptorNode.IncomingEdgeIndices` (Phase 6b's FirstOrDefault is not fan-out-safe).
- Severity model: table base → transitive attenuation (depth≥2) → RuntimeBoost (Descriptor→Runtime only, no double-count). Advisory edge predicate (`IsAdvisory`): Weak References/DependsOn/SupersededBy/SubWorkflowStep, runtime edges never advisory.
- `AddDescriptorImpactAnalysis()` DI registration (TryAddSingleton for both services). No scoped dependencies.
- 45 new tests (11 ChangeSetBuilder + 20 Analyzer + 14 Severity). 191 Metadata.Tests pass. 0 regressions across 6 suites (513 total).
- No changes to Phase 6a/6b types or legacy `DescriptorCatalog.AnalyzeImpact()`.
- **Design spec**: `docs/superpowers/specs/2026-06-13-phase-6c-impact-analysis-engine-design.md`
- **Implementation plan**: `docs/superpowers/plans/2026-06-13-phase-6c-impact-analysis-engine.md`

### Compatibility / Breaking Change Analyzer (Phase 6d, 2026-06-13)

- `IDescriptorCompatibilityAnalyzer` — consumes `(before inventory, after inventory, DescriptorChangeSet, DescriptorImpactAnalysisReport)` and produces a rule-based `DescriptorCompatibilityReport` with deterministic findings, max level, and diagnostics. Stateless singleton.
- `IDescriptorCompatibilityRule` — public interface (`RuleId`, `CanAnalyze`, `Analyze`) for future module-owned rules. 7 concrete rules: Generic + Schema + Form + Capability + Event + HumanTask + Workflow.
- Core types (9 files under `DescriptorCompatibility/`): 2 enums (`DescriptorCompatibilityLevel` with Unsupported=0 for MaxLevel exclusion, `DescriptorCompatibilityFindingKind`), 4 records (`DescriptorCompatibilityDiagnostic`, `DescriptorCompatibilityFinding`, `DescriptorCompatibilityReport`, `DescriptorCompatibilityAnalysisOptions`), 2 interfaces (`IDescriptorCompatibilityAnalyzer`, `IDescriptorCompatibilityRule`), 1 orchestrator (`DescriptorCompatibilityAnalyzer`).
- Generic rules cover all 7 `DescriptorChangeKind` values without descriptor internals. Uses Phase 6c affected consumers for severity.
- Descriptor-specific rules fire on `ContractHashChanged`/`Updated` and compare before/after internals: Schema (14 rules), Form (9 rules), Capability (7 rules with SecuritySensitive for permissions/risk), Event (7 rules for both EventDescriptor and GeneratedEventDescriptor), HumanTask (8 rules with SecuritySensitive for permissions), Workflow (6 rules).
- `DescriptorCompatibilityLevel.Unsupported = 0` — `MaxLevel` uses natural `Max()` over classified findings; Unsupported only when ALL findings are Unsupported. Means "insufficient rule knowledge", not "more severe than Breaking."
- **6c severity is never projected into 6d compatibility.** High impact ≠ Breaking; Low impact ≠ Compatible.
- Impact diagnostics mapped to compatibility diagnostics: `IMPACT_TOPOLOGY_*` → `COMPAT_*`, `IMPACT_PATH_TRUNCATED` → `COMPAT_ANALYSIS_INCOMPLETE`, `IMPACT_AMBIGUOUS_UNPINNED_TARGET` → `COMPAT_VERSION_AMBIGUITY`.
- No topology access — compatibility rules consume Phase 6c report only. No lifecycle governance, no migration generation.
- Data-permission scope rules reserved but NOT implemented — no descriptor owns data-permission scope today.
- `AddDescriptorCompatibilityAnalysis()` DI registration (TryAddSingleton).
- 50 new tests (13 generic + 8 schema + 5 form + 5 capability + 4 event + 5 humantask + 5 workflow + 5 diagnostics). 244 Metadata.Tests pass (194 pre-existing + 50 new). 0 regressions.
- **Design spec**: `docs/superpowers/specs/2026-06-13-phase-6d-compatibility-breaking-change-analyzer-design.md`
- **Implementation plan**: `docs/superpowers/plans/2026-06-13-phase-6d-compatibility-breaking-change-analyzer.md`

### Descriptor Package / Manifest / Snapshot (Phase 6f, 2026-06-15)

- Upgraded `DescriptorPackage`, `DescriptorManifest`, `DescriptorManifestEntry`, `DescriptorSnapshot`, `SnapshotEntry` in-place.
- Removed per-kind manifest entry lists (`Schemas`, `Capabilities`, …) — replaced by flat `DescriptorEntries`.
- `IDescriptorPackageBuilder` + `DefaultDescriptorPackageBuilder` — stateless singleton, explicit inventory input, consumes optional 6b/6c/6d/6e reports.
- `DescriptorPackageHashComputer` — explicit field encoding with escaped string fields (`Esc`), null sentinels (`\\0`), ordinal ordering (`StringComparer.Ordinal`), invariant formatting (`CultureInfo.InvariantCulture`), SHA-256. `ContentHash`/`EvidenceHash`/`EnvelopeHash` marked `[Obsolete]` — replaced by canonical hash infrastructure in Phase 7e.1.
- `DescriptorPackageEvidence` + `EvidenceFinding` — aggregated evidence summary from topology/impact/compatibility/lifecycle reports.
- `DescriptorPackageRelationshipEntry` — flattened relationship facts with `SourcePath` preservation from topology edges.
- `DescriptorPackageDiagnostic` + 12 self-consistency diagnostic codes.
- `IDescriptorPackageDiffer` + `DescriptorPackageDiffer` — shallow structural diff (added/removed refs, changed hashes, state changes, strong-typed metadata changes).
- `IDescriptorPackageSerializer` + `DescriptorPackageSerializer` — JSON round-trip for metadata/evidence packages (no descriptor payload).
- `AddDescriptorPackaging()` DI registration (TryAddSingleton for builder, differ, serializer).
- `DescriptorSnapshotBuilder.TakeSnapshot()` marked `[Obsolete]` — no DI delegation.
- 41 new tests (6 hash computer + 20 builder + 8 diff + 4 serializer + 3 DI). 333 Metadata.Tests pass. 0 regressions across existing suites.
- **Design spec**: `docs/superpowers/specs/2026-06-15-phase-6f-descriptor-package-manifest-snapshot-design.md`
- **Implementation plan**: `docs/superpowers/plans/2026-06-15-phase-6f-descriptor-package.md`

### Descriptor Stable Hash Builder Public Surface (Phase 6g, 2026-06-16)

- `IDescriptorStableHashBuilder` interface + `DescriptorStableHashes` record in `CrestCreates.Metadata.Abstractions`.
  - `Build(IDescriptor)` returns `DescriptorStableHashes(ContractHash, DefinitionHash, RuntimeHash?, BindingHash?)`.
- `DescriptorStableHashBuilder` implementation in `CrestCreates.Metadata` — **fully AoT-safe** string concatenation (SHA-256), zero `JsonSerializer.Serialize` calls, zero IL2026 trim warnings.
  - ContractHash: per-kind switch extraction of semantically meaningful fields with canonical ordering.
  - DefinitionHash: exhaustive per-kind field enumeration with canonical ordering (sorted collections, sorted dictionary keys).
  - `InteractionTarget` subtypes (`CapabilityTarget`, `HumanTaskTarget`, `SubWorkflowTarget`) handled explicitly via `AppendTargetRef` switch — **both** hashes correctly capture target ref changes.
  - `FormFieldDescriptor.Metadata` dictionary keys sorted for canonical ordering.
- `DescriptorHashComputer` (static class) marked `[Obsolete]` — delegates to `DescriptorStableHashBuilder`. **Note**: static `Builder` instance bypasses DI.
- `AddDescriptorStableHash()` DI registration.
- 18 tests: same/recreated stability, optional field→definition change, required field addition/removal→contract change, permission change→both hashes, form label→contract stable, workflow step id→definition change, workflow target ref change→**both** hashes change, DI resolution, cross-instance stability, HumanTask/Event stability, optional field contract hash behavior (exclusion policy deferred).
- `CompanyCertificationChangeScenarios` — `"INVALIDATED"` sentinels replaced; 12 control-plane tests pass.
- **Schema field inclusion policy (v2 resolved)**: ContractHash now includes only required fields (`IsRequired=true`) via `SchemaRequiredFieldCanonicalHashProfile` + `RequiredSchemaFieldCanonicalHashFilter`. Optional field additions change DefinitionHash only (emit `DefinitionHashChanged`), not ContractHash. See Phase 6h.
- **Workflow step ordering**: Steps are hashed in list order (NOT sorted by Id), because step order is semantically meaningful for workflow execution.
- **Design spec**: GitHub issue #29.

### Canonical Hash Profile Semantics v2 (Phase 6h, 2026-06-23)

- **Union profiles replace CustomWriter**: `[CanonicalHashUnionProfile]` + `[CanonicalHashUnionCase]` declare discriminated unions. SG generates exhaustive current-compilation switch writers. `[CanonicalHashField.CustomWriter]` is `[Obsolete]` and triggers CCHASH015. `InteractionTargetCanonicalHashProfile` declares Capability, HumanTask, Workflow cases. `WorkflowStep.Target` uses `ValueProfile = typeof(InteractionTargetCanonicalHashProfile)`.
- **Schema ContractHash v2 is required-binding surface**: Only schema fields with `IsRequired=true` are included via `SchemaRequiredFieldCanonicalHashProfile` + `RequiredSchemaFieldCanonicalHashFilter`. DefinitionHash includes all fields via `SchemaFieldCanonicalHashProfile`.
- **DefinitionHashChanged**: `DescriptorChangeKind.DefinitionHashChanged` tracks definition-only changes separate from `ContractHashChanged`. Optional field addition changes DefinitionHash and emits `DefinitionHashChanged`; it does not change Schema ContractHash v2. Compatibility rules use this distinction for severity grading.
- **Filter**: `[CanonicalHashField.Filter]` is a collection-only semantic projection applied before ordering and writing.
- Profile shape versions bumped to v2 (`schema-contract-hash-v2`, `schema-definition-hash-v2`).

### Agent Control Plane Tool Surface (Phase 7c, 2026-06-18)

- **Two new projects** under `src/Runtime/Agent/`:
  - `CrestCreates.Agent.ControlPlane.Abstractions` — all public types, interfaces, enums
  - `CrestCreates.Agent.ControlPlane` — default implementations
- **One new test project** under `tests/Runtime/Agent/`:
  - `CrestCreates.Agent.ControlPlane.Tests` — 276 tests, all passing

- **Core abstractions** (32 tool manifest entries, AOT-safe, no runtime reflection):
  - `IAgentControlPlaneToolService` — facade interface with 32 tool methods across 7 waves
  - `AgentToolInvocationContext` — tenant, actor, correlation, tool name, invocation source
  - `AgentToolResult<T>` — strongly typed result with Status/Diagnostics/AuditRecord
  - `AgentToolResultStatus` — Success/SucceededWithDiagnostics/Denied/Failed/NotFound/InvalidRequest
  - `AgentToolAuthorizationPolicy` — AllowAll/ReadOnly/ProductionDefaults (legacy, superseded by AgentToolAuthorizationOptions)
  - `AgentToolAuthorizationMode` — DevelopmentAllowAll/ExplicitPolicy/DenyAll
  - `AgentToolAuthorizationOptions` — mode-driven options with category-aware defaults (AllowReadOnlyToolsByDefault, AllowMutationToolsByDefault, AllowActivationHandoffToolsByDefault), explicit allow/deny lists
  - `AgentToolName` — 30 canonical tool name constants (single source of truth)
  - `AgentToolPermissionRequirement` — extended with ToolCategory and IsReadOnly for category-aware authorization
  - `IAgentToolAuthorizationService` + `DefaultAgentToolAuthorizationService` — mode-driven authorization: DevelopmentAllowAll/ExplicitPolicy/DenyAll, category-aware defaults, deny-overrides-allow, legacy policy compatibility
  - `IAgentToolManifestProvider` + `StaticAgentToolManifestProvider` — hardcoded 32-tool manifest
  - `IAgentToolInvocationAuditor` + `InMemoryAgentToolInvocationAuditor` — audit recording
  - `AgentToolInvocationAuditRecord` — full touch-point tracking (descriptors, drafts, reviews, fix proposals, package previews, activation requests)

- **P0 Security Fix — ToolName Integrity** (2026-06-19):
  - `ExecuteAsync<T>` now accepts `expectedToolName` parameter and validates `context.ToolName == expectedToolName` before manifest lookup, authorization, or action execution
  - If mismatch: returns `InvalidRequest` with `TOOL_NAME_MISMATCH` diagnostic, creates audit record with authoritative tool name
  - All 22 tool methods pass `AgentToolName.XXX` constants as expectedToolName
  - Authorization service uses `expectedToolName` (not `context.ToolName`) for tool-name deny checks
  - 5 boundary tests: ToolNameMismatch_IsRejected_BeforeAuthorization, DeniedToolName_CannotBeBypassed_BySpoofedContextToolName, Audit_UsesExpectedToolName_NotCallerSuppliedSpoofedName, SubmitActivationRequest_WithContextToolNameBuildMetadataContextPack_IsRejected, ManifestLookup_UsesExpectedToolName

- **P1 Changes** (2026-06-19):
  - `SucceededWithDiagnostics` status added to `AgentToolResultStatus` — for tool results that succeeded but produced diagnostics the caller should acknowledge
  - `ProductionDefaults` authorization policy — denies DraftCreate/Update/Cancel, FixApplyToDraft, ActivationRequestSubmit/Cancel; allows read/context/review-suggest/package-preview tools
  - Default `AddAgentControlPlane()` DI registration uses `ProductionDefaults` instead of `AllowAll` (use `AllowAll` explicitly for dev/test)

- **Authorization Policy Hardening — Issue #40** (2026-06-19):
  - New `AgentToolAuthorizationMode` enum: DevelopmentAllowAll, ExplicitPolicy, DenyAll
  - New `AgentToolAuthorizationOptions` record with category-aware defaults:
    - `AllowReadOnlyToolsByDefault` (true by default)
    - `AllowMutationToolsByDefault` (false by default)
    - `AllowActivationHandoffToolsByDefault` (false by default)
    - `AllowedPermissions` / `DeniedPermissions` / `AllowedToolNames` / `DeniedToolNames` — deny always overrides allow
  - `AgentToolPermissionRequirement` extended with `ToolCategory` and `IsReadOnly` for category-aware decisions
  - `DefaultAgentToolAuthorizationService` rewritten: mode-driven, category-aware, deny-overrides-allow
  - Legacy `AgentToolAuthorizationPolicy` still accepted via compatibility constructor (PolicyToOptions conversion)
  - DI: `AddAgentControlPlane()` overload accepting `AgentToolAuthorizationOptions`
  - 28 authorization tests covering: mode-based defaults, explicit allow/deny, deny-overrides-allow, category toggles, legacy compatibility, DenyAll mode, tool name integrity

- **Permission boundary**: every tool invocation → tool name integrity check → manifest lookup → permission check → service invocation → audit recording
- **Runtime boundary**: Agent CANNOT approve, activate, execute runtime handlers, mutate runtime registries, or become governance authority
- **Activation is handoff only**: `SubmitActivationRequest` creates a `Submitted` record, does not execute activation. `Approved`/`Rejected` are terminal states. No `ApproveActivationRequest` tool exists.
- **Fix proposals are suggestions**: `ApplyFixProposalToDraft` only updates draft, never patches active descriptors
- **Review pass ≠ activation approval**

- **Wave 1** (Context Read): GetDescriptorByRef, SearchDescriptors, ListDescriptorRelationships, GetTopologySummary, BuildMetadataContextPack, BuildRuntimeScenarioContextPack
- **Wave 2** (Draft): CreateDescriptorDraft, UpdateDescriptorDraft, GetDescriptorDraft, ListDescriptorDrafts, CancelDescriptorDraft, CompareDescriptorDraft
- **Wave 3** (Review): ValidateDescriptorDraft, ReviewDescriptorDraft, GetDraftReviewResult, ListDraftReviewResults, ExplainDiagnostics
- **Wave 4** (Fix Proposal): SuggestDescriptorDraftFixes, GetFixProposal, ListFixProposals, ApplyFixProposalToDraft
- **Wave 5** (Package Preview): PreviewDescriptorPackage, BuildPackageEvidencePreview, BuildActivationReadinessPreview, GetPackagePreview
- **Wave 6** (Activation Handoff): SubmitActivationRequest, GetActivationRequestStatus, CancelActivationRequest
- **Wave 7** (Manifest Query): ListAgentTools, GetAgentToolDescriptor

  - **Code Review Finding Fixes** (2026-06-19):
    - P1: `ReviewDescriptorDraft` corrected to `IsReadOnly=false` — it persists review results and changes draft status to Reviewed
    - P2: `SuggestDescriptorDraftFixes` corrected to `IsReadOnly=false` — it creates and persists fix proposals
    - P2: Legacy empty `AgentToolAuthorizationPolicy` no longer silently maps to `DevelopmentAllowAll`; all legacy policies now map to `ExplicitPolicy` with read-only defaults
    - P2: Manifest permissions now carry `ToolCategory` and `IsReadOnly` metadata so adapters consuming the manifest directly can make category-aware authorization decisions
    - Added `ManifestClassificationTests` — table-driven test with 30 expected classifications, 4 test methods verifying every manifest tool's classification matches its actual side effects
    - P2: `PreviewDescriptorPackage` and `BuildPackageEvidencePreview` corrected to `IsReadOnly=false` — they persist state (`_packagePreviews`/`_evidencePreviews`) referenced by activation handoff
    - P2: Legacy `AgentToolAuthorizationPolicy.AllowAll` doc corrected — it no longer claims equivalence to `DevelopmentDefaults` since PolicyToOptions maps all legacy policies to `ExplicitPolicy`
    - P1: `DeniedDescriptorKinds` was inert in real facade chain — redesigned with two-phase authorization:
      - Phase 1 (coarse auth): tool-name integrity → manifest lookup → permission/category/actor/mode deny — no store access
      - Phase 2 (kind resolution): `kindResolver` lambda runs after coarse auth, resolving descriptor kind from authoritative store/catalog
      - Phase 3 (kind deny): `IsDescriptorKindDenied(kind)` — fail-closed: if `DeniedDescriptorKinds` is configured and kind is null (unresolvable), invocation is denied
      - All 30 tool methods use `kindResolver`: direct (request), draft store, catalog, indirect (activation request → draft, review result → draft, fix proposal → draft, package preview → draft)
      - Aggregate queries (context pack, topology, search, lists) pass `null` kindResolver — no single kind target
      - `IAgentToolAuthorizationService.IsDescriptorKindDenied(string?)` added for standalone kind-deny check
      - `GetDescriptorByRef` kind resolution now handles versioned refs
      - Coarse auth gates resource access — denied tools never touch stores
      - Added `DescriptorKindDenyTests` — 12 facade-level regression tests covering: direct kind, draft store, catalog, fail-closed, fail-open-when-no-kinds-denied, catalog failure, coarse-auth-gates-store, versioned ref

  - **Descriptor Visibility Closure — Issue #40** (2026-06-20, review-hardened):
    - **Production closed-world semantics**: Empty `AllowedDescriptorKinds` = nothing visible; open-world development permits valid kinds unless explicitly denied
    - **Deny-wins semantics**: `DeniedDescriptorKinds` always overrides `AllowedDescriptorKinds` — no allow-list escape hatch
    - **Explicit denied kind → Denied (not empty Success)**: Per spec §6.2, when a caller explicitly supplies a denied DescriptorKind filter, the response is `Denied` with `DESC_KIND_DENIED`, not an empty `Success`
    - **30-tool full coverage**: All 30 manifest tools now enforce visibility via `_resourceResolver` + `DenyIfInvisible` pipeline
    - **9 resource shapes covered**: None, DirectKind, SingleDescriptor, SingleDraft, Aggregate, Graph, ContextPack, Indirect, Nested — bidirectionally mapped per tool
    - **Owner-kind resolution for indirect artifacts**: Reviews, proposals, previews, activations inherit visibility from their owning Draft's `DescriptorKind` — no orphaned visibility gaps
    - **Two-pass ref resolution (anti-probing)**: `AgentControlPlaneResourceResolver.ResolveDescriptor(ref, scope)` resolves version-pinned and unambiguous refs against the full catalog (so denied kinds resolve to snapshots for Denied responses), but resolves ambiguous unpinned refs against visible descriptors only (preventing denied version count leakage)
    - **Visible-universe-first package construction**: `PreviewDescriptorPackageAsync` and `BuildPackageEvidencePreviewAsync` build the visible universe BEFORE constructing packages, so package hashes are derived from visible descriptors only — no hash side-channel
    - **Nested projection (defense-in-depth)**: `AgentDraftArtifactVisibilityProjector.ProjectReview` filters `ProposedInventory` and `Diagnostics` (omits diagnostics with denied DescriptorKind). Topology/Impact/Compatibility/Governance are retained as-is in the review result — the primary defense is that packages and context packs are built from visible descriptors only. `ProjectPackage` returns null on projection failure instead of a malformed object. `ProjectEvidence` passes through (safe because evidence is built from visible-only package)
    - **AoT-safe kind validation**: `AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind` — range-based check (Schema=0 to HumanTask=5) replacing `Enum.IsDefined`
    - **TryCreate error handling**: `AgentVisibleDescriptorUniverse.TryCreate` returns `UniverseCreationResult` (Created/InvalidKindDetected) instead of throwing `InvalidOperationException`
    - **Audit persistence + dedup**: `ExecuteAsync` pipeline persists `result.AuditRecord` if present; `InMemoryAgentToolInvocationAuditor` deduplicates by `AuditId` to prevent double-write artifacts
    - **Single DI policy truth**: All three `AddAgentControlPlane` overloads register `AgentToolAuthorizationOptions` as singleton and construct `DefaultAgentControlPlaneToolService` via `ActivatorUtilities.CreateInstance` — legacy policy overload converts to options before registration
    - **Storage migration**: `_packagePreviews`/`_evidencePreviews`/`_activationRequests` changed from bare entry types to owner-bearing snapshot types (`PackagePreviewResourceSnapshot`, `EvidencePreviewResourceSnapshot`, `ActivationResourceSnapshot`)
    - **Dead code removed**: `ExplainCode`, `SuggestRemediation`, `SuggestFixTools` static methods
    - **276 ControlPlane tests + 7 Boundary tests pass**, full solution build 0 errors
    - **Design spec**: `docs/superpowers/specs/2026-06-19-agent-descriptor-kind-visibility-closure-design.md`
    - **Implementation plans**: PR-A (`plans/2026-06-19-agent-visibility-pr-a-policy-pipeline.md`), PR-B (`plans/2026-06-19-agent-visibility-pr-b-aggregate-graph-context.md`), PR-C (`plans/2026-06-19-agent-visibility-pr-c-indirect-nested.md`)

  - **162→276 tests across 17 test classes**: StaticManifest (10), Authorization (29), InMemoryAuditor (6), PermissionBoundary (10), RuntimeBoundary (10), ToolNameBoundary (6), ManifestClassification (4), DescriptorKindDeny (12), Wave1-6 (12+12+10+9+10+12), VisibilityCoverage (1), DraftArtifactVisibilityProjector (7), VisibleDescriptorUniverse (8), DescriptorKindPolicyEvaluator (4), NestedProjectionRegression (19)
- **Design spec**: `docs/superpowers/specs/2026-06-18-phase-7c-agent-control-plane-tool-surface-design.md`
- **Implementation plan**: `docs/superpowers/plans/2026-06-18-phase-7c-agent-control-plane-tool-surface.md`
   - **Tool DTO & JSON Contract — Issue #41** (2026-06-21):
     - **P0 Projection DTOs** (replacing unsafe upstream types in tool contracts):
       - `DescriptorSummaryDto` — replaces `IDescriptor?` in `DraftComparisonResult.CurrentActiveDescriptor`
       - `AgentDescriptorDraftDto` — replaces `DescriptorDraft` in all tool results; nested `AgentDraftPayloadDto` with discriminator + 6 optional sub-records (Capability/Workflow/HumanTask/Form/Event/Schema)
       - `AgentReviewResultDto` — replaces `DescriptorDraftReviewResult` in all tool results; includes `ProposedInventorySummary`, `TopologySummary`, `ImpactAnalysisSummary`, `CompatibilitySummary`, `GovernanceSummary`
       - `AgentDraftPayloadDto` — nested one-of shape: `Discriminator` + `Capability?`/`Workflow?`/`HumanTask?`/`Form?`/`Event?`/`Schema?` sub-records; invariant: only the sub-record matching Discriminator may be non-null
     - **Request-side closure**: `CreateDescriptorDraftRequest.Payload` and `UpdateDescriptorDraftRequest.Payload` changed from `DescriptorDraftPayload` to `AgentDraftPayloadDto`
     - **Source-Generated JSON Contract**:
       - `AgentControlPlaneToolJsonSerializerContext` — registers all 32 tool request/result DTOs + stable upstream value objects + temporary upstream aggregates
       - `AgentControlPlaneContractVersion.Current = "7c.v1"` — machine-readable contract version
       - `AgentControlPlaneToolJsonSerializerOptionsFactory.CreateDefault()` — pre-configured `JsonSerializerOptions` using source-generated metadata
     - **Projection helpers** (in `CrestCreates.Agent.ControlPlane/Projections/`):
       - `DescriptorSummaryDtoProjection.FromDescriptor(IDescriptor?)` — safe `IDescriptor` → `DescriptorSummaryDto` mapping
       - `AgentDescriptorDraftDtoProjection.FromDraft(DescriptorDraft)` / `ToDomainPayload(AgentDraftPayloadDto)` — bidirectional draft mapping; `ToDomainPayload` enforces discriminator invariant (throws on mismatch)
       - `AgentReviewResultDtoProjection.Project(DescriptorDraftReviewResult, deniedKinds?)` — review result projection with optional denied-kind filtering for visibility closure
     - **FromDraft uses IDescriptor interface** (not concrete casts): `MapPayload` sub-mappers accept `IDescriptor` and use safe `as` casts for kind-specific properties, enabling test descriptors and future descriptor types
     - **Boundary constraint tests**: recursive type graph check — no `IDescriptor`, `IServiceProvider`, `object`/`dynamic`/`JsonElement` in any DTO property chain (including nested generics, collections, nullable)
     - **Semantic preservation tests**: round-trip serialization, context pack ref preservation, review diagnostics preservation, fix proposal risk/approval semantics, activation request handoff-only invariant, payload discriminator invariant
     - **Visibility closure regression test**: `AgentReviewResultDtoProjection_DeniedKinds_DoNot_Appear_In_ProjectedSummary` — denied kinds filtered from ProposedInventory, Topology, ImpactAnalysis
     - **Manifest set-equality coverage tests**: 4 tests verifying manifest tool names = contract registrations = JsonTypeInfo set; facade vs manifest query tool distinction; no orphan contract types
     - **276 ControlPlane tests + 7 Boundary tests pass**, full solution build 0 errors
     - **Design spec**: `docs/superpowers/specs/2026-06-21-phase-7c-tool-dto-json-contract-design.md`
     - **Implementation plan**: `docs/superpowers/plans/2026-06-21-phase-7c-tool-dto-json-contract.md`

   - **Code Review Fix — #41 Round 1** (2026-06-21):
     - P1-1: `DescriptorKind == Payload.Discriminator` consistency check in Create/Update — mismatch returns `InvalidRequest` + `KindDiscriminatorMismatch` diagnostic
     - P1-2: DTO→domain mapping silently dropped fields — added missing reference fields (InputSchema/OutputSchema/FormSchema → `DescriptorRef?`, VariableSchema/Interaction/Timeout/PayloadSchema/Importance/ChangeKind); `MergeToDomainPayload` for updates (merge semantics, not replace); Version → `int?`
     - P2-3: Coverage gate rewritten to detect missing request type registrations; manifest/contract set-equality assertions
     - P2-4: `AgentToolDescriptor.ContractVersion` defaults to `AgentControlPlaneContractVersion.Current`
     - P2-5: Topology edge filtering — `MapTopologySnapshot` filters edges by visible node refs; edge count assertions
     - **280 ControlPlane tests + 7 Boundary tests pass**

   - **Code Review Fix — #41 Round 2** (2026-06-21):
     - P0: `TryValidatePayload` — non-throwing one-of validation before `ToDomainPayload`/`MergeToDomainPayload`; invalid payloads return `InvalidRequest` + `InvalidPayloadOneOf` diagnostic, not `Failed`
     - P1: `AllPublicToolContractDtos_Have_JsonTypeInfo` — reflection-based coverage gate verifying all public sealed records in Abstractions have JsonTypeInfo registrations
     - 4 new discriminator invariant tests (missing branch, mixed branches Create+Update, SaveAsync never called)
     - **285 ControlPlane tests + 7 Boundary tests pass**

   - **Draft Payload Contract Source Generator — Issue #42** (2026-06-21):
     - **New project**: `CrestCreates.Agent.DraftContracts` — source-generated DTO + projection + manifest types from contract spec files
     - **New generator**: `AgentDraftContractGenerator` in `CrestCreates.CodeGenerator` — reads `[AgentDraftContractSpec]` attributes + per-kind spec files, generates:
       - `Agent*DraftPayloadDto` — 6 payload DTOs with typed fields matching domain descriptor properties
       - `Agent*DraftPatchDto` — 6 patch DTOs (all fields optional) for update/merge operations
       - `Agent*ChangedFields` — 6 changed-field tracking enums for merge semantics
       - `AgentDraftPayloadProjection` — static projection class with `FromDomain`, `Create`, `Merge`, `TryValidatePayload` methods
       - `AgentDraftContractManifest` — static manifest class listing all contract types for coverage gates
     - **Contract spec files**: 6 per-kind spec files in `CrestCreates.Agent.DraftContracts/ContractSpecs/` classifying every field as Required/Optional/SchemaRef/Enum/Collection/Navigation/Calculated with domain mapping metadata
     - **Diagnostic descriptors**: ADP001–ADP010 covering spec validation errors (missing spec, unknown kind, duplicate field, missing kind accessor, etc.)
     - **Migration**: Hand-written `AgentDescriptorDraftDtoProjection.cs` (670 lines) → generated `AgentDraftPayloadProjection.g.cs` (558 lines); 7 hand-written DTO files in Abstractions → project reference to DraftContracts + global using aliases
     - **285 ControlPlane tests + 21 DraftContracts integration tests + 8 generator unit tests + 7 Boundary tests pass** (321 total)
      - **Design spec**: `docs/superpowers/specs/2026-06-21-phase-7c-agent-draft-payload-contract-source-generator-design.md`
      - **Implementation plan**: `docs/superpowers/plans/2026-06-21-phase-7c-agent-draft-payload-contract-source-generator.md`

### Review Report & Fix Proposal Contract (Phase 7d, 2026-06-22)

- **Review Report DTOs** (5 new types): `DescriptorReviewReportDto` (13 typed sections + top-level Recommendations + source binding fields: ReviewResultId, DraftVersion, SourceReviewHash, TemplateVersion), `DescriptorReviewReportSectionDto` (Kind, SectionId, Title, Order, IsEmpty, OverallSeverity, Items), `DescriptorReviewReportItemDto` (ItemId with disambiguation suffix, ReasonCode, MessageTemplateId, Message, Severity, Parameters, RelatedDiagnosticIds, RelatedDescriptorIds), `DescriptorReviewRecommendationDto` (RecommendationId, ReasonCode, Message, Kind, IsActionable, RelatedItemIds), `DescriptorReviewReportBuildRequest` (ReviewResult, Draft, VisibilityApplied — fail-fast InvalidOperationException when false)
- **New enums**: `DescriptorReviewReportSectionKind` (13 values, 1-based), `DescriptorReviewSeverity` (Info=1/Warning=2/Error=3/Blocker=4), `DescriptorReviewRecommendationKind` (6 values including RequestActivationHandoff), `DescriptorReviewReportFormat` (Markdown=1/PlainText=2)
- **FixProposal contract upgrade** (breaking): `FixProposal` gains Kind, Title, Explanation, ReasonCode, Applicability, IsExecutable (aggregation: Applicability==CurrentMutableDraft && Actions.All(a=>a.IsExecutable)), RequiresManualAction, BlocksActivationUntilResolved (explanation-not-gate), ContractVersion; `FixProposalAction` gains TargetPath (was Path), Kind (was ActionKind), JsonElement? CurrentValue/ProposedValue (was string, via JsonSerializer.SerializeToElement+.Clone for AOT safety), IsExecutable, SafetyLevel
- **New enums**: `FixProposalKind` (9 values: CreateMissingDescriptor=1 to SetRequiredField=9; default mapping → MarkRequiresReview), `FixProposalApplicability` (4 values incl. CurrentMutableDraft), `FixProposalActionSafetyLevel` (4 values), `FixProposalActionKind` expanded to 10 (SetValue=1 to ManualActionRequired=10)
- **Report Builder**: `IDescriptorReviewReportBuilder`/`DefaultDescriptorReviewReportBuilder` (1,046 lines) — 13 Build*Section methods, SHA256 ReportId via IDescriptorStableHashBuilder, deterministic .OrderBy() on all iteration, fail-fast on VisibilityApplied=false, TimeProvider injection, DeriveRecommendations
- **Report Renderer**: `IDescriptorReviewReportRenderer`/`DefaultDescriptorReviewReportRenderer` — Markdown + PlainText, reads DTO Message directly, ContractVersion validation (UnsupportedReportContractVersion on mismatch), no external services/LLM
- **Message Template Catalog**: `IDescriptorReviewMessageTemplateCatalog`/`DefaultDescriptorReviewMessageTemplateCatalog` — 31 templates, regex-based parameter substitution, TemplateVersion="7d.v1"
- **Service integration**: `BuildDescriptorReviewReportAsync` + `RenderDescriptorReviewReportAsync` (2 new tools), single-action constraint on ApplyFixProposal (UnsupportedMultiActionFixProposal diagnostic), Applicability check (FIX_PROPOSAL_NOT_APPLICABLE), MapDiagnosticToFixProposalKind (RATIONALE_EMPTY/INTENT_EMPTY→SetRequiredField, default→MarkRequiresReview)
- **Runtime guardrails** (Phase 7d follow-up #44): Proposal-level IsExecutable guard (`NON_EXECUTABLE_FIX_PROPOSAL` diagnostic, rejects before action-level checks, no draft mutation); SetValue JsonElement ValueKind validation (`FIX_ACTION_VALUE_KIND_NOT_SUPPORTED` diagnostic, rejects Object/Array/Number/True/False, allows String/Null/missing)
- **Contract version**: bumped to "7d.v1"
- **Tool count**: 30 → 34 (BuildDescriptorReviewReport + RenderDescriptorReviewReport + 2 convenience)
- **Code review**: 3 rounds, 4 Critical + 7 Important + 4 Minor fixed; 2 issues rejected with technical reasoning (I#6 YAGNI, M#13 no NRE path)
- **Test suites**: 8+ test files, 75+ new tests total — Builder (20+), Renderer (7+), Catalog (8), FixProposal (26+), Service Integration (8), Coverage (5+), Semantic Preservation, Wave4 updates
- **479 ControlPlane tests + 11 Boundary tests pass**
- **Design spec**: `docs/superpowers/specs/2026-06-22-phase-7d-review-report-fix-proposal-design.md`
- **Implementation plan**: `docs/superpowers/plans/2026-06-22-phase-7d-review-report-fix-proposal.md`

### Safe Activation Workflow (Phase 7e, 2026-06-24)

- **Core activation models** (11 new types in `Activation/` sub-namespace): `DescriptorActivationActorKind`, `BindingHashes`, `ActivationBindingSnapshot`, `DescriptorActivationPolicy`, `DescriptorActivationEligibility`, `DescriptorActivationDecision`, `DescriptorActivationReviewDecision`, `DescriptorActivationReviewOutcome`, `DescriptorActivationAuditRecord`, `DescriptorActivationReviewTaskInput`
- **Service interfaces** (5): `IDescriptorActivationRequestService`, `IDescriptorActivationPolicyProvider`, `IDescriptorActivationAuditor`, `IActivationEvidenceRechecker`, `IRuntimeActivationGate`
- **Orchestrator**: `IActivationReviewOrchestrator` + `DefaultActivationReviewOrchestrator` — creates HumanTask for review-required requests, processes review decisions
- **Event handler**: `DescriptorActivationReviewHumanTaskEventHandler` — subscribes to HumanTaskCompletedEvent, routes to RequestService
- **Single-track principle**: ToolService delegates ALL activation logic to IDescriptorActivationRequestService
- **Policy-driven eligibility**: AutoActivatable / RequiresHumanReview / NotActivatable
- **Evidence binding**: ActivationBindingSnapshot captures review+package+evidence hashes at request time; IActivationEvidenceRechecker verifies no drift before gate execution
- **BindingHashes**: 5 top-level slots (SourceReviewHash, ReviewManifestHash, PackageHashes, ContractHash, DefinitionHash) with nested `DescriptorPackageHashSet` for atomic package hash storage
- **Runtime gate**: IRuntimeActivationGate is the ONLY component that mutates runtime state
- **Safety-first default**: EvaluateGovernance defaults to ReviewRequired (not Allowed)
- **Audit trail**: IDescriptorActivationAuditor records all decisions with ordering
- **AoT safety**: TryParseReviewDecision moved to DescriptorActivationReviewDecisionParser with JsonSerializerContext
- **Tenant isolation**: DescriptorActivationReviewDecision carries TenantId/CorrelationId; HumanTask callback resolves from instance
- **Contract version**: bumped to "7e.v1"
- **471 ControlPlane tests + 11 Boundary tests pass**
- **Design spec**: Phase 7e issue #17
- **Caveat**: No HTTP/MCP adapter. No persistent store for package previews or activation requests (in-memory ConcurrentDictionary). Integration with human governance approval path is outside this tool surface.

### Canonical Evidence Hashing Migration (Phase 7e.1, 2026-06-27)

- **Package canonical hash infrastructure**: `IDescriptorPackageCanonicalHashComputer` + `DefaultDescriptorPackageCanonicalHashComputer` — produces `DescriptorPackageHashSet` (PackageManifestHash, PackageEvidenceHash, PackageEvidenceEnvelopeHash) using canonical JSON writers + SHA-256
- **3 new artifact kinds**: `CanonicalHashArtifactKind.PackageManifest=5`, `PackageEvidence=6`, `PackageEvidenceEnvelope=7`
- **5 canonical JSON writers**: `ManifestCanonicalHashWriter`, `EvidenceCanonicalHashWriter`, `EnvelopeCanonicalHashWriter`, `ReviewResultSourceBindingProjection`, `ReviewResultIntegrityProjection` — deterministic ordering, ordinal sorting, invariant formatting; **all use PascalCase field names via `nameof()`** (e.g., `nameof(DescriptorManifest.FormatVersion)`) matching SG-generated writer convention
- **DescriptorPackageEvidenceEnvelope**: sealed record with PackageId, PackageVersion, CreatedAt, CreatedBy, Source, PackageManifestHash, PackageEvidenceHash + Metadata
- **DescriptorPackageCanonicalShapeVersions**: 3 shape versions (PackageManifestV1, PackageEvidenceV1, PackageEvidenceEnvelopeV1)
- **DescriptorDraftReviewCanonicalShapeVersions**: 2 shape versions (SourceBindingV1, IntegrityV1)
- **CanonicalHashContractVersions.DescriptorHash**: public constant `"canonical-hash-v1"` in Metadata.Abstractions
- **DescriptorDraft review hash service**: `IDescriptorDraftReviewHashService` + `DefaultDescriptorDraftReviewHashService` — computes SourceReviewHash and ReviewManifestHash via canonical projections
- **Package model migration**: `DescriptorPackage` gains `Hashes` (DescriptorPackageHashSet) + `EvidenceEnvelope` (DescriptorPackageEvidenceEnvelope); `DescriptorManifest.ContentHash/EvidenceHash/EnvelopeHash` **completely removed** (not just Obsolete); `DescriptorPackage.ContentHash` falls back to `Hashes?.PackageManifestHash.Value ?? string.Empty`
- **Builder migration**: `DefaultDescriptorPackageBuilder` injects `IDescriptorPackageCanonicalHashComputer`, replaces 3 legacy hash calls
- **ToolService cleanup**: Removed ~100 lines of ad-hoc hash helpers from `DefaultAgentControlPlaneToolService`, injected `IDescriptorDraftReviewHashService`
- **ReportBuilder migration**: `DefaultDescriptorReviewReportBuilder` injects `IDescriptorDraftReviewHashService`, removed `ComputeSourceReviewHash`
- **BindingHashes redesign**: **7 flat `CanonicalHash` slots** — `SourceReviewHash`, `ReviewManifestHash`, `PackageManifestHash`, `PackageEvidenceHash`, `PackageEvidenceEnvelopeHash`, `ContractHash`, `DefinitionHash` — plus `PackageHashes` convenience accessor that constructs `DescriptorPackageHashSet` from the 3 package slots
- **Activation binding validator**: `ActivationBindingHashValidator` validates: (a) per-slot `ArtifactKind` and `Purpose` semantic expectations via 7-slot metadata table, (b) per-slot `Scope` matching (all slots = `InternalFull`), (c) non-empty `Algorithm`/`AlgorithmVersion`/`ContractVersion`/`CanonicalShapeVersion` on every slot, (d) `AlgorithmVersion`/`ContractVersion` consistency across all hashes
- **Validator integration at 3 gates**: RequestService (submit), EvidenceRechecker (recheck — malformed = drift), RuntimeActivationGate (before execution)
- **Resolver split**: `IActivationBindingArtifactResolver` split into `StorePackageHashes(tenantId, previewId, DescriptorPackageHashSet)` and `StoreEvidenceHashes(tenantId, evidencePreviewId, DescriptorPackageHashSet)`; `ResolvedBindingArtifacts` carries both `CurrentPackageHashes` and `CurrentEvidenceHashes`; Rechecker compares package vs evidence hashes independently
- **Package preview reuse**: `BuildPackageEvidencePreviewAsync` reuses existing package preview (Path A) when same `(TenantId, DraftId, ScopeFingerprint)` key exists, `DraftVersion` matches, and `VisibleDescriptorSetHash` matches; otherwise creates fresh package + evidence previews with identical `DescriptorPackageHashSet` (Path B); ScopeFingerprint = deterministic from `AgentDescriptorVisibilityScope` (Mode + AllowedKinds + DeniedKinds)
- **VisibleDescriptorSetHash**: length-prefixed encoding (`{len}:{value}`) for FullId, Kind, Version — collision-resistant, InvariantCulture formatting; computed from `universe.VisibleDescriptors` (catalog identity, not proposed inventory)
- **DescriptorActivationDiagnosticCodes.BindingHashValidationFailed**: new diagnostic code
- **Golden tests**: 8 sensitivity tests for package manifest/evidence, 8 for review source-binding/integrity, canonical sorting tests, 7-field metadata assertions, VisibleUniverseChange regression test, SameIdSameKindDifferentNamespace + SameFullIdSameKindDifferentVersion isolation tests
- **Guard tests**: `AgentActivationCanonicalHashGuardTests` — boundary guard for canonical hash production
- **39 files changed** (434 insertions, 339 deletions), 14 new files
- **Test results**: 471 ControlPlane, 439 Metadata, 57 DescriptorDraft, 11 DependencyBoundaries — all passing

### Code Review Findings — Phase 7e.1 (2026-06-27)

5 code review rounds with 16 total findings. All resolved except Finding 2 (multi-scope key, pushback x3):

| Finding | Severity | Description | Resolution |
|---------|----------|-------------|------------|
| R1-F1 | Critical | Package hash resolver keyed by previewId but resolves by reviewResultId | Fixed: resolver uses PackagePreviewId for package hashes |
| R1-F2 | Critical | ActivationBindingHashValidator doesn't validate per-slot ArtifactKind/Purpose | Fixed: 7-slot metadata table with ArtifactKind + Purpose + Scope validation |
| R1-F3 | High | BindingHashes 5-slot nested vs 7-slot flat contract | Fixed: 7 flat slots + PackageHashes convenience accessor (hybrid design) |
| R1-F4 | High | Backward-compat writes to Obsolete DescriptorManifest string fields | Fixed: removed string writes, deleted ContentHash/EvidenceHash/EnvelopeHash fields |
| R1-F5 | High | Canonical writers hash payload only, not metadata envelope | Pushback: deliberate design across entire canonical hash infrastructure, not Phase 7e.1-specific |
| R2-F1 | Critical | PackageManifestHash producer/validator Purpose mismatch (Integrity vs AuditEvidence) | Fixed: validator aligned to Integrity |
| R2-F3 | Medium | DescriptorManifest still has public settable string hash fields | Fixed: changed to `{ get; init; }`, then fully removed |
| R3-F1 | High | Validator doesn't check Scope, Algorithm, AlgorithmVersion, ContractVersion, CanonicalShapeVersion | Fixed: added Scope + mandatory metadata field validation |
| R3-F2 | High | Resolver doesn't distinguish package vs evidence preview hashes | Fixed: split StorePackageHashes/StoreEvidenceHashes, separate dictionaries |
| R3-F3 | Medium | DescriptorManifest string hash fields still present | Fixed: fully removed |
| R4-F1 | High | BuildPackageEvidencePreviewAsync doesn't reuse existing package preview | Fixed: Path A (reuse) + Path B (fresh build) with ScopeFingerprint + DraftVersion identity |
| R5-F1 | High | ScopeFingerprint = policy identity, not visible universe identity | Fixed: added VisibleDescriptorSetHash (catalog identity) |
| R5-F2 | Medium | DraftVersion not in _latestPackageByDraft key | Pushback: draft version monotonic, A/B/A impossible |
| R5-F3 | Medium | A/B/A test uses empty catalog | Fixed: added VisibleUniverseChange regression test |
| R6-F1 | High | VisibleDescriptorSetHash only used Id, not FullId/Kind/Version | Fixed: length-prefixed encoding with FullId + Kind + Version |
| R6-F2 | High | _latestPackageByDraft still single-value mapping | Pushback x3: A/B/A scenarios don't occur in practice |
| R6-F3 | Medium | Tests don't isolate namespace vs version changes | Fixed: added SameIdSameKindDifferentNamespace + SameFullIdSameKindDifferentVersion tests |
| R7-F1 | High | Store uses visibleProposed, compare uses universe.VisibleDescriptors | Fixed: both now use universe.VisibleDescriptors |
| R7-F2 | High | ComputeVisibleDescriptorSetHash uses ambiguous delimiters | Fixed: length-prefixed encoding |
| R7-F3 | Medium | Test doesn't isolate namespace or version | Fixed: separate test cases |

### Semantic String Governance (2026-06-25)

- **Core value objects** (11 types in `CrestCreates.Core.Abstractions.Identity`): `ErrorCode`, `PermissionName`, `PolicyName`, `FeatureName`, `SettingName`, `DiagnosticCode`, `EventName`, `CapabilityId`, `WorkflowId`, `HumanTaskId`, `MessageTemplateId` — each with `XxxValue` const string + typed property, inline validation, safe implicit conversion to string, private constructor + static factory for constrained types
- **SeverityLevel** value object: private constructor, static factory properties (Info/Warning/Error), get-only Value, implicit conversion to string
- **CrestErrorCodes** centralized: `General`, `Validation`, `Authorization`, `NotFound`, `Concurrency`, `PreconditionRequired` — replaces 6 inline `"CrestError.X"` literals across exception classes
- **Typed exception overloads**: `CrestException(ErrorCode)`, `CrestBusinessException(ErrorCode)`, `CrestPermissionException(PermissionName)`, `CrestValidationException(ErrorCode)` — existing string overloads preserved for backward compat
- **Framework constant classes**: `FeatureManagementErrorCodes` (7 entries), `SchemaValidationErrorCodes` (8 entries), `MetadataContextPackDiagnosticCodes` (3 entries), `DescriptorPackageDiagnosticCodes` (12 diagnostic code entries; severity helpers removed — use `DescriptorPackageDiagnosticSeverity` enum directly)
- **Agent constant classes**: `DescriptorActivationDiagnosticCodes` (35 entries), `DescriptorActivationHumanTaskIds` (2 entries), `DescriptorActivationMessageTemplateIds` (8 entries), `AgentToolPermissionNames` (20 entries + RuntimePrefix), `AgentToolDiagnosticCodes` (53 entries), `DescriptorReviewReportMessageTemplateIds` (31 entries), `DescriptorDraftDiagnosticCodes` (12 entries)
- **Tooling constant classes** (netstandard2.0 — const string only, no typed properties): `CanonicalHashDiagnosticCodes` (28 entries), `ObjectMappingDiagnosticCodes` (12 entries), `CodeGeneratorDiagnosticCodes` (4 entries)
- **Generated permission shape**: `XxxPermissions.Create` → `XxxPermissions.CreateValue` (const string) + `XxxPermissions.Create` (typed PermissionName property); `GetAllPermissions()` yields `XxxValue` strings
- **Architecture guard**: `SemanticStringGuardTests` in DependencyBoundaries — 6 forbidden patterns (ACTIVATION_*, CCHASH*, OM*, FIELD_REQUIRED, descriptor-activation-review, agent.*), definition file exemptions, `// semantic-string-guard: allow` opt-out for test fixtures
- **Test coverage**: 25 value object tests, 4 exception tests, 431 Agent tests, 9 boundary tests, 158 CodeGenerator tests (0 failures)
- **Design spec**: `docs/superpowers/specs/2026-06-25-semantic-string-governance-design.md`
- **Implementation plan**: `docs/superpowers/plans/2026-06-25-semantic-string-governance.md`

### Agent Memory Runtime (2026-06-29)

Status: Abstractions + in-memory runtime implemented.

Projects:
- `CrestCreates.Agent.Memory.Abstractions` — contracts, interfaces, AoT JSON context
- `CrestCreates.Agent.Memory` — in-memory default implementations + DI registration
- `CrestCreates.Agent.Memory.Tests` — 11 tests (4 contract + 5 main chain + 2 boundary)

Abstractions (8 enums, 17 records, 10 interfaces, 1 JSON context):
- Enums: `AgentSourceKind`, `AgentMemoryConfidence`, `AgentMemoryStatus`, `AgentMemoryKind`, `AgentConversationRole`, `AgentMemoryDiagnosticSeverity`, `AgentMemorySourceExpansionStatus`, `AgentMemoryOperationKind`
- Records: `AgentContextSourceRef`, `AgentContextEvidenceRef`, `AgentMemoryDiagnostic`, `AgentActorContext`, `AgentConversationTurn/Record`, `AgentTaskEvent/Record`, `SanitizedAgentContent`, `AgentCompressedContextBlock/Context`, `AgentMemoryCandidate/Item`, `AgentMemoryQuery`, `AgentMemoryPack`, `AgentMemoryOperationRequest`, `AgentSourceExpansionResult`, `AgentAuthoringRequest/Context`
- Interfaces: `IAgentConversationStore`, `IAgentTaskHistoryStore`, `IAgentCompressedContextStore`, `IAgentMemoryStore`, `IAgentMemoryContentSanitizer`, `IAgentContextCompressor`, `IAgentMemoryExtractor`, `IAgentMemoryPromotionService`, `IAgentMemoryRetriever`, `IAgentContextSourceExpander`, `IAgentAuthoringContextBuilder`
- JSON: `AgentMemoryJsonSerializerContext` with 13 `[JsonSerializable]` types for AoT

Runtime implementations:
- Stores: `InMemoryAgentConversationStore`, `InMemoryAgentTaskHistoryStore`, `InMemoryAgentCompressedContextStore`, `InMemoryAgentMemoryStore` — all use ConcurrentDictionary with snapshot semantics (ToArray on collection fields)
- Sanitization: `DefaultAgentMemoryContentSanitizer` — SHA256 canonical hash, empty-content rejection
- Compression: `DefaultAgentContextCompressor` — conversation/task → compressed context blocks
- Extraction: `DefaultAgentMemoryExtractor` — one candidate per compressed block, Low confidence
- Promotion: `DefaultAgentMemoryPromotionService` — Promote/Reject/Supersede/Archive with status guards and chain linking
- Recall: `DefaultAgentMemoryRetriever` — query filtering + character budget, `DefaultAgentContextSourceExpander` — switch-dispatched source expansion
- Authoring: `DefaultAgentAuthoringContextBuilder` — assembles MetadataContextPack + MemoryPack

Dependency boundaries enforced:
- Memory.Abstractions does NOT reference ControlPlane.Abstractions
- Memory runtime does NOT reference ControlPlane, Framework Api/Web, Platform, or persistence providers

Main chain flow: SaveConversation → Compress → ExtractCandidates → Promote → Recall → BuildAuthoringContext

### Boundary Snapshot Migration + Package Diagnostic Severity (Issues #35 + #46, 2026-06-30)

Status: Completed.

**#46 Package Diagnostic Severity:**
- `DescriptorPackageDiagnosticSeverity` enum (Info/Warning/Error) added to `CrestCreates.Metadata.Abstractions/DescriptorPackage/`
- `DescriptorPackageDiagnostic.Severity` migrated from `SeverityLevel` to `DescriptorPackageDiagnosticSeverity`
- `DescriptorPackageDiagnosticCodes.SeverityError/Warning/Info` helpers removed (no longer needed — use enum directly)
- `DefaultDescriptorPackageBuilder` updated: 8 assignments + `CompanyCertificationControlPlaneReport` string comparison → enum equality
- `EvidenceFinding.Severity` and `EvidenceFindingCount.Severity` remain `SeverityLevel` — they are NOT package diagnostic models
- `MapLevelToSeverity` unchanged (feeds `EvidenceFinding.Severity`)

**#35 ISnapshotable<T> Boundary Migration:**
- `ISnapshotable<T>.Snapshot()` is now the sole boundary-copy verb across the entire codebase
- Zero `Clone()` / `CreateClone()` references remain in src/, tests/, or samples/
- Hard migration: no `[Obsolete]` bridges, no dual-path period

Models migrated (30 production types):
- **Metadata.Abstractions**: `EvidenceFinding`, `EvidenceFindingCount`, `DescriptorPackageEvidence`, `DescriptorManifest`, `DescriptorManifestEntry`, `DescriptorSnapshot`, `SnapshotEntry`, `DescriptorPackage` (explicit interface impl due to `Snapshot` property collision)
- **DescriptorDraft.Abstractions**: `DescriptorDraftPayload` (abstract `Snapshot()`), `DescriptorDraft`, `CapabilityDescriptorDraftPayload`, `EventDescriptorDraftPayload`, `FormDescriptorDraftPayload`, `HumanTaskDescriptorDraftPayload`, `SchemaDescriptorDraftPayload`, `WorkflowDescriptorDraftPayload`
- **Workflow.Abstractions**: `WorkflowInstance`
- **HumanTask.Abstractions**: `HumanTaskInstance`
- **Organization.Abstractions**: `OrganizationUnit`, `Position`, `UserOrganizationMembership`, `UserOrganizationRoleAssignment`
- **Agent.Memory.Abstractions** (16 types): `AgentContextSourceRef`, `AgentContextEvidenceRef`, `AgentMemoryDiagnostic`, `AgentMemoryInvocationContext`, `AgentConversationTurn`, `AgentConversationRecord`, `AgentTaskEvent`, `AgentTaskRecord`, `SanitizedAgentContent`, `AgentCompressedContextBlock`, `AgentCompressedContext`, `AgentMemoryCandidate`, `AgentMemoryItem`, `AgentMemoryPack`, `AgentMemoryOperationRequest`, `AgentSourceExpansionResult`, `AgentAuthoringRequest`, `AgentAuthoringContext`
- **Samples**: `DescriptorDraftSet`, `DescriptorAuthoringResult`

Store migration (all `.Clone()`/`.CreateClone()` → `.Snapshot()`):
- `InMemoryDescriptorDraftStore`, `InMemoryWorkflowInstanceStore`, `InMemoryHumanTaskInstanceStore`, `InMemoryOrganizationStore`, `DefaultOrganizationHierarchyService`
- Agent Memory stores: `InMemoryAgentMemoryStore` (5 call sites), `InMemoryAgentConversationStore` (read path), `InMemoryAgentCompressedContextStore` (both paths), `InMemoryAgentTaskHistoryStore` (read path + list) — write paths with sanitization transforms kept inline

Project references added for `CrestCreates.Snapshot.Abstractions`:
- Metadata.Abstractions, DescriptorDraft.Abstractions, Workflow.Abstractions, HumanTask.Abstractions, Organization.Abstractions, Agent.Memory.Abstractions

Test updates:
- Removed `LegacyBridgeModel` + 2 bridge-pattern tests from `ISnapshotableContractTests` (no longer applicable after hard migration)
- New tests: `PackageDiagnostics_UsePackageSeverityEnum`, `SaveAsync_StoresSnapshot_NotOriginalDraftReference`, `DescriptorPackageEvidence_Snapshot_CopiesNestedCollections`
- 1263 tests across 9 affected projects pass, 0 failures, 0 regressions
- Dependency boundary tests pass (13/13)

**Design spec**: `docs/superpowers/specs/2026-06-30-boundary-snapshot-and-package-diagnostic-severity-design.md`
**Implementation plan**: `docs/superpowers/plans/2026-06-30-boundary-snapshot-and-package-diagnostic-severity.md`

### AI-assisted Descriptor Authoring Golden Scenario (Phase 7f, 2026-06-30)

Status: Implemented.

Sample-level orchestration proving intent → AgentAuthoringContext → DescriptorDraftSet → review/fix → activation evidence binding → RuntimeActivationGate → fresh activated runtime host execution.

Key components:
- `FakeCompanyCertificationAuthoringAgent` — deterministic fake consuming only `AgentAuthoringContext`; no DI, no runtime services, no LLM calls
- `CompanyCertificationDescriptorCloner` — deep-copy helpers for all descriptor kinds used in the sample
- `CompanyCertificationAuthoringGoldenScenarioRunner` — orchestrates authoring → draft set review → activation → fresh host runtime proof
- `CompanyCertificationAuthoringGoldenScenarioReport` — captures full pipeline result (authoring, review, activation, runtime proof fields)
- `CompanyCertificationChangeScenarios.FromInventory` — builds change scenario from explicit descriptor inventory

Key invariants:
- Draft set is atomic — any single draft failure fails the entire set
- Review uses `IDescriptorDraftReviewService` (not ad-hoc approval)
- Activation binding uses 7-slot `BindingHashes` with `IActivationBindingArtifactResolver` storage
- Runtime proof exercises fresh host built from approved final inventory (not the original host)
- `AgentMemoryPack.IsAuthoritative` is always false — metadata wins over conflicting memory
- Fake agent has no constructor dependencies — cannot access RuntimeActivationGate or runtime handlers
- 14 tests in `CompanyCertificationAuthoringGoldenScenarioTests`

---

## Recommended Next Thread Entry Prompt

If a future thread should resume from this state, use a prompt like:

> Read `/memory.md` first. Continue from the current CrestCreates platform status. Treat completed items as closed unless you find contradictory code. Focus on unresolved work only.

---
