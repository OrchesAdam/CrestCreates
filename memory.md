# CrestCreates Progress Memory

Last Updated: 2026-07-16 (Phase 8f Agent Tool Projection sixth review fixes)

## Purpose

This file records the current platform status for CrestCreates so future threads can resume work quickly without re-deriving prior conclusions.

---

## Approved Designs

No approved-but-unimplemented design is currently recorded here.

## Completed Features

### Phase 8f — Agent Tool Projection

Status: Implemented; provider SDK adapters and a planner/runtime loop remain
future composition concerns.

Approved mainline:

`[AgentToolSpec]` → Source Generator → independent
`AgentCapabilityToolDescriptor` + exact typed binding → immutable Agent Tool
snapshot → trusted Agent context / selection / roles → logical invocation lease
and fencing → approval / budget / governance audit →
`ICapabilityDispatcher` with `InvocationSource.Agent` → Capability Pipeline →
exact output serialization and Schema validation → independent budget and
invocation finalization.

Key decisions:

- `Agent.Tools` is an independent vertical slice; `Agent.Runtime` remains a
  future composition root and the Phase 7c Control Plane
  `AgentToolDescriptor` remains unchanged.
- MCP and Agent share only a protocol-neutral Schema/JSON projection kernel;
  Phase 8e MCP hashes, package/snapshot compatibility, E2E, and NativeAOT gates
  are mandatory migration checks.
- `AgentToolSelectionPolicy` and `AgentToolCallOrigin` are distinct safe-default
  enums; CallOrigin is part of the canonical invocation fingerprint.
- logical invocation, execution attempt/lease, and budget reservation are
  separate identities; lease expiry, renewal, atomic DispatchStarted, and
  fencing prevent stale completion without claiming distributed exactly-once.
- approval evidence may replay only for the same logical invocation and
  fingerprint; cross-node claim/replay protection belongs to a durable Host
  adapter.
- Budget uses Reserved/Released/Committed/Indeterminate and is independent from
  Invocation terminal state; Budget Committed + Invocation Indeterminate is a
  valid post-dispatch result. Unknown settlement is represented explicitly in
  Governance Audit and keeps the logical invocation Indeterminate.
- Governance Audit records denied/uncertain decisions without fabricating an
  Approval or Budget reservation; reservation/finalization uncertainty fences
  the logical invocation instead of releasing it for automatic retry.
- Agent Tool capability issues are model-facing only when the Capability result
  is the authoritative ValidationFailed code and each issue uses the fixed
  safe Schema validation-code allowlist.
- Generator governance diagnostics reject only statically provable unsafe
  combinations; Unknown side-effect semantics are resolved at startup after
  CapabilityKind is known.
- Role/Selection filtering occurs before argument traversal. Rejection audit
  fingerprints use a safe fallback when raw arguments do not match a business
  Schema and therefore never let invalid JSON types escape as runtime errors.
- Invocation terminal preparation precedes Required governance completion audit;
  Completed replay is published only after the audit is durably confirmed.
- Budget Denied is trusted only when Reservation is null and ReasonCode is
  present; malformed Denied shapes remain Indeterminate. Decision Audit is
  content-idempotent and conflicts on same-attempt differences, and Required
  Decision Audit failures return stable audit-failure outcomes.
- Invocation completion uses PrepareCompletion/PublishCompletion with a fenced
  CompletionPending state, so concurrent Acquire cannot observe Completed before
  Required Audit finalization. Completion uncertainty closes an Indeterminate
  audit checkpoint when possible.
- The invocation gate has no direct Complete shortcut. Publish returns a durable
  state and response loss is resolved through GetCompletionState without
  overwriting a possible Completed record. CompletionPending retains audit,
  budget, prepared-at, and reason metadata for reconciliation.
- All post-dispatch Indeterminate paths persist the gate state before writing
  Audit; a failed gate transition is represented with a null InvocationState.
  Gate publication rejects non-terminal success/failure outcomes.
- Required Audit finalization returns/queryable durable state. Response loss is
  resolved by AuditId: matching Completed proceeds, confirmed Indeterminate
  fences the Gate, and unknown/conflicting state remains CompletionPending.
  Published Completed is an irreversible Gate terminal state.
- All terminal Audit finalizations, including Released and Indeterminate paths,
  use direct-result plus AuditId query confirmation. BestEffort tolerates only
  unconfirmed audit state; a confirmed contradictory Indeterminate state fences
  the invocation.
- Role/Selection Decision Audit failures preserve the external UnknownTool mask.
  Rejected payloads use schema-neutral raw canonical argument hashes; a
  pre-evaluation denial records ArgumentsEvaluated=false with no hash. Malformed
  budget results retain ObservedReservation for reconciliation.
- Title is model-facing behavior and participates in Agent Tool ContractHash.
- Actual completion requires a linux-x64 NativeAOT publish-link-run fixture;
  provider adapters, a planner/runtime loop, durable stores, approval workflow,
  hot reload, and issue #61 remain out of scope.

Completed:

- Added independent Metadata Agent Tool contracts, `DescriptorKind.AgentTool`,
  canonical Contract/Definition hashing, package/snapshot round trips, and a
  strong Capability relationship without opening Agent Draft/Authoring/Control
  Plane mutation allowlists.
- Extracted the protocol-neutral Schema/JSON projection and directional
  JsonTypeInfo parity kernel. MCP remains an independent vertical slice, with
  its canonical hash golden vectors, package JSON, E2E, and NativeAOT behavior
  preserved.
- Added `[AgentToolSpecs]` / `[AgentToolSpec]` incremental generation for
  descriptors, exact input binding, exact output serialization, and
  application-owned source-generated JSON registrations.
- Added eager Active-only immutable runtime snapshots with captured Capability,
  exact Schemas, frozen JsonTypeInfo, effective governance floors, provider-
  neutral discovery, trusted role filtering, and safe SelectionPolicy ×
  CallOrigin behavior.
- Added canonical invocation fingerprints including CallOrigin; logical
  invocation leases with renewal, fencing, atomic DispatchStarted, Completed
  replay, conflict detection, and Indeterminate blocking; and explicitly
  volatile development adapters.
- Added fail-closed approval evidence binding/claim semantics, per-attempt budget
  reservation and independent settlement, two-checkpoint governance audit, and
  the fixed governed `InvocationSource.Agent` Dispatcher execution order.
- Added DI/startup fail-closed checks, usage documentation, dependency guards,
  generator-backed E2E, and a real linux-x64 NativeAOT publish-link-run fixture
  that executes discovery, exact DTO binding, Capability Handler dispatch,
  output validation, governance settlement, and terminal replay.

Focused verification on 2026-07-16: Metadata 467/467, Schema 42/42,
Agent.Tools abstractions 16/16, Agent.Tools runtime 84/84, Agent E2E 1/1,
Agent NativeAOT 1/1, MCP runtime 63/63, MCP E2E 1/1, MCP NativeAOT 1/1,
dependency boundaries 40/40, CodeGenerator 277/277, and Control Plane 484/484.
The Runtime solution build also completes with 0 errors and now includes the
Agent Tool runtime, E2E, and NativeAOT fixture projects. A full parallel test invocation
still reports environment-dependent Docker/RabbitMQ/Kafka suites and the
pre-existing DraftContractGenerator test fixture that searches for implementation
Payload types while referencing only `DescriptorDraft.Abstractions`; neither is
on the Phase 8f path.

Spec: `docs/superpowers/specs/2026-07-16-phase-8f-agent-tool-projection-design.md`
Guide: `docs/Feature/AgentTools/usage-guide.md`

### Phase 8e — MCP Tool Projection

Status: Implemented; MCP transport hosting remains a future adapter concern.

Core chain:

`[McpToolSpec]` → Source Generator → `McpToolDescriptor` + exact typed binding → immutable runtime snapshot → Host-filtered discovery/invocation → `ICapabilityDispatcher` → Capability Pipeline → Handler → runtime OutputSchema validation.

Completed:
- Metadata-owned MCP descriptor contracts avoid both Metadata-to-Integrations and Metadata-to-Runtime dependency inversions through `McpCapabilityReference`; Runtime resolves it to the captured `CapabilityDescriptor`.
- Protocol-neutral abstractions and runtime live under `src/Integrations/CrestCreates.Mcp.Abstractions` and `src/Integrations/CrestCreates.Mcp`.
- Explicit Source Generator authoring emits descriptor providers and input/output bindings without runtime reflection or dictionary fallback.
- Application-owned source-generated `JsonTypeInfo` (single context or source-generated-only resolver chain) is resolved and frozen during snapshot construction.
- Runtime snapshots contain only Active, fully validated tools and capture resolved Capability and exact Schema versions; an `IHostedService` idempotently builds Schema → Capability → MCP Tool registries before eagerly publishing the snapshot during Host startup.
- Discovery and invocation apply trusted Host exposure policy independently from Capability authorization.
- Invocation uses `InvocationSource.Mcp` and only the descriptor overload of `ICapabilityDispatcher`.
- Idempotency keys use a versioned canonical SHA-256 shape over Host id, tool contract hash, resolved Capability contract hash, and stable logical InvocationId.
- Schema execution rejects unknown/duplicate properties, startup parity is bidirectional and type-aware, and actual serialized output is validated against OutputSchema before structured content is returned.
- Supported Schema subset covers primitive scalars and primitive collections for string, bool, int, long, decimal, double, Guid/UUID, DateOnly/date, and DateTime/date-time. Pattern, ValidationRules, and unsupported References fail MCP snapshot construction.
- Diagnostics cover MCP001-MCP121 across generator and startup/runtime contract validation.
- Generator-backed E2E, dependency-boundary, Agent authoring-policy, and NativeAOT publish-and-run fixtures are present.
- MCP input parity rejects `JsonTypeInfo.IsRequired` metadata so Schema-owned presence validation always reaches the Capability Pipeline; null outer discovery/invocation contexts and blank ToolName are classified as protocol InvalidRequest. The NativeAOT fixture is explicitly a linux-x64 gate.

Focused verification: MCP/Schema/Capability/generator/E2E/boundary suites passed. The former PublishTrimmed fixture was replaced by `CrestCreates.Mcp.AotFixture`, which performs a real linux-x64 NativeAOT publish, clang native link, and native-binary execution. The fixture passed with `MCP_NATIVEAOT_OK`, 0 warnings, and 0 errors on Ubuntu clang 21.1.8.

Non-goals still open:
- Official MCP Server SDK hosting and stdio/SSE/Streamable HTTP transports
- MCP authentication/session/Tasks/progress/resources/prompts/sampling
- automatic Capability exposure, approval workflows, and hot reload
- Generated CRUD trimming-safe JSON contracts (GitHub issue #61)

Guides: `docs/Feature/MCP/arc-design.md`, `docs/Feature/MCP/usage-guide.md`
Spec: `docs/superpowers/specs/2026-07-15-phase-8e-mcp-tool-projection-design.md`

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

### Phase 8a — Capability Endpoint Projection

Status: ✅ Complete

**Spec**: `docs/superpowers/specs/2026-07-06-phase-8a-capability-endpoint-projection-design.md`
**Plan**: `docs/superpowers/plans/2026-07-07-phase-8a-capability-endpoint-projection.md`

Core chain: `[CapabilityEndpointSpec]` → SG → DescriptorProvider + BindingContract → `MapCrestCapabilityEndpoints()` → `ICapabilityDispatcher.DispatchAsync(CapabilityDescriptor, ...)`

**Positioning**: Capability→HTTP without AppService. Zero DynamicApi bridge. Phase 8a is the new mainline; old DynamicApiAotSourceGenerator is legacy AppService HTTP exposure.

**Phase roadmap**: 8a (new mainline) → 8c (legacy deprecation, ✅ complete) → 8d (AppService→Capability compatibility generator)

**DX Layering**:
- Level 0: Runtime canonical model (CapabilityEndpointDescriptor, BindingContract, Registry, MapCrestCapabilityEndpoints)
- Level 1: Explicit `[CapabilityEndpointSpec]` with full control
- Level 2: Sugar attributes (`[CapabilityEndpointSet]` + `[Post]`/`[Get]`/`[Put]`/`[Delete]`/`[Patch]`) — SG normalizes to Level 1 internally

**Four concern separation**:
1. `CapabilityEndpointDescriptor` = projection metadata (no CLR details)
2. `CapabilityEndpointBindingContract` = SG-produced `BindInputAsync` delegate (public + `[EditorBrowsable(Never)]` for cross-assembly SG output)
3. `ICapabilityDispatcher` = unified facade with `CapabilityDescriptor` overload
4. `CapabilityEndpointResultMapper` = fixed mapping table (internal)

**Attributes** (Level 1): `[CapabilityEndpointSpecs]` container, `[CapabilityEndpointSpec(capabilityId, httpMethod, routePattern)]`, `[CapabilityEndpointInput(Type, Name, Source, Required, CapabilityInputPath)]`, `[CapabilityEndpointOutput]`
**Attributes** (Level 2): `[CapabilityEndpointSet(RoutePrefix, GroupName, Tags)]`, `[Post(capabilityId, route)]`, `[Get]`, `[Put]`, `[Delete]`, `[Patch]`

**SG outputs** (CapabilityEndpointGenerator):
- `{Container}_Provider.g.cs` — `ICapabilityEndpointDescriptorProvider` with ModuleInitializer registration
- `{Container}_Bindings.g.cs` — `BindInputAsync` functions registered via ModuleInitializer into `CapabilityEndpointBindingRegistry`
- SG does NOT generate MapAll() or direct MapMethods code — registry-driven mapping only

**Runtime components**:
- `CapabilityEndpointBindingContract` (sealed record, 3 fields)
- `CapabilityEndpointBindingRegistry` (static ConcurrentDictionary, fail-fast on duplicate `(EndpointId, Version)`, `internal static void Reset()` for test isolation)
- `CapabilityEndpointJsonRuntime` (two ReadBodyAsync overloads — **[Obsolete]**, replaced by `CapabilityEndpointBodyReader.ReadNativeBodyAsync`/`ReadCompatibilityBodyAsync`)
- `CapabilityEndpointRegistryBootstrapper` (Interlocked build-once)
- `CapabilityEndpointCapabilityResolver` (Id-based: exact version → latest active → fail-closed; exact version miss throws InvalidOperationException)
- `CapabilityEndpointResultMapper` (error code → HTTP status mapping)
- `CapabilityEndpointMapper` (MapMethods with delegate closure capturing capability descriptor + binding contract)
- `AddCrestCapabilityEndpoints()` (permissive, no throw) + `MapCrestCapabilityEndpoints()` (fail-closed)

**Prerequisite**: `ICapabilityPipeline.ExecuteAsync(CapabilityDescriptor, ...)` overload — skips registry resolution, sets context Id/Name/Version/ContractHash from descriptor directly. String overload resolves then delegates to descriptor overload.

**Input materialization**:
- Body-only: `ReadBodyAsync<T>` → TInput
- Body + Route/Query/Header scalar: route values assigned to TInput writable properties by PascalCase name match; Query/Header scalars via `context.Request.Query["name"].ToString()` / `context.Request.Headers["name"].ToString()`
- Single scalar-only: parse from route/query/header
- Multi-scalar without body: CEP013 Error (not supported in 8a)

**Authorization 3-mode**: InheritCapability (pipeline checks), RequireAuthenticated (`RequireAuthorization()` + pipeline), AllowAnonymous (`AllowAnonymous()` + pipeline; Error if capability has permissions or is high risk)

**Analyzer diagnostics**: CEP001-CEP005 (Level 1 structural), CEP008 (Route+Body DTO writable property), CEP009-CEP011 (Level 2 misuse), CEP012 (non-enum non-scalar type in route binding), CEP013 (multi-scalar without body), CEP014 (non-C#-identifier Name without CapabilityInputPath), CEP015 (generic ReadBodyAsync AOT debt warning), CEP016 (Level 2 without [CapabilityEndpointSet] container)

**Route token handling**: Normalizer strips constraints (`{id:int}` → `id`), catch-all (`{**path}` → `path`), optional (`{id?}` → `id`) — aligned with validator behavior

**Clean boundary with old DynamicApi**: 6 explicit "not reusable" items. Shared: `DescriptorKind.DynamicApiEndpoint` (value=7) + `GenerateParseExpression` (CodeGenerator internal helper). `MapCrestDynamicApi()` (legacy) and `MapCrestCapabilityEndpoints()` (new) coexist without wrapping each other.

**Review iterations** (4 rounds, 30+ findings fixed):
- Round 1 (self-audit): 5 P1 — FindPropertyTypeOnType inheritance, JsonRuntime deep copy, Enum fallback crash, multi-scalar Dictionary, EndpointId prefix
- Round 2 (external): 5 findings — AllowAnonymous+Version=0 bypass, EndpointId from CapabilityId, Route+Body convention, Query/Header, RouteToBody
- Round 3 (external): 4 findings — Route token type inference, EndpointId cross-container collision, RouteToBody removal, Query/Header
- Round 4 (external): 8 findings — Exact version fallback, StringValues binding, CEP003 implicit constructor, ValidationMiddleware re-resolve, de-dup key missing version, AOT-safe body binding debt, Level 2 container check, duplicate validator registration

**Test counts**: 29 SG + 35 DynamicApi + 10 Capability + 33 Boundary — all passing. Full solution build 0 errors in 8a-affected projects.

**Known architectural items for 8d** (not blocking 8a/8c):
- AppService→Capability compatibility generator (8d scope)
- SG generates generic ReadBodyAsync — ~~CEP015 marks as debt~~ **Resolved**: CEP015 deleted, replaced by application-owned `JsonSerializerContext` + `CapabilityEndpointJsonTypeInfoResolver` + startup validation via `CapabilityEndpointJsonContractValidator`

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

### Further Dynamic API Legacy Cleanup (Phase 8c)

Status: ✅ Complete

**Spec**: `docs/superpowers/specs/2026-07-08-phase-8c-legacy-dynamic-api-boundary-design.md`
**Plan**: `docs/superpowers/plans/2026-07-08-phase-8c-legacy-dynamic-api-boundary.md`
**Architecture Note**: `docs/superpowers/specs/2026-07-08-phase-8c-legacy-dynamic-api-boundary-architecture-note.md`

8c resolves identity ambiguity between legacy and Capability-first paths — not physical elimination of dual-track coexistence.

**Deliverables** (7 PRs, 28 AC):
1. **Legacy XML docs + architecture note** — 8 legacy files annotated with compatibility-only conceptual descriptions (no forbidden CapabilityEndpoint symbol names, no cross-assembly `<see cref>`). Architecture note with 10 sections including BindingRegistry lifecycle declaration.
2. **Boundary tests** (6 tests): assembly reference boundary, project reference boundary, legacy source symbol boundary, CapabilityEndpoint mapping boundary, CapabilityEndpoint emitter boundary, Abstractions type definition boundary.
3. **EndpointId/EndpointVersion independent properties** — `[CapabilityEndpointSpec]` + 5 HTTP method attributes each gain `EndpointId` (string, default null → `endpoint:{CapabilityId}`) and `EndpointVersion` (int, default 0 → CapabilityVersion). SG resolves identity at generation time. CEP017 (whitespace EndpointId Error), CEP020 (negative EndpointVersion Error), CEP021 (Level 2 Input without route token Error). Validator checks Id whitespace.
4. **TargetProperty separation** — `[CapabilityEndpointInput]` gains `TargetProperty` (string, SG-only). BindingEmitter uses TargetProperty→PascalCase(Name) for CLR assignment (no CapabilityInputPath fallback chain). ProviderEmitter only emits CapabilityInputPath. CEP018 (missing TargetProperty on body Error), CEP019 (invalid TargetProperty identifier Error). Simple public settable property names only (no nested paths).
5. **CEP013 Error + Dictionary fallback deletion** — CEP013 upgraded from Warning to Error. Level 1 covers Route/Query/Header scalar-only combinations; Level 2 covers route tokens + explicit Input only (Level 2 does not read class-level `[CapabilityEndpointInput]`). Dictionary<string, object?> fallback deleted from BindingEmitter. Fail-closed `throw new InvalidOperationException` for multi-scalar-no-body path. Even if CEP013 is suppressed, emitter must not silently fall back to Dictionary.
6. **DynamicApiSourceGenerator recycled** — moved to `99_RecycleBin/` (not deleted per AGENTS.md rule). `DynamicApiAotSourceGenerator.cs` remains as legacy generated path.
7. **Legacy test rename** — 6 test files renamed with `Legacy` prefix (4 Web.Tests + 2 CodeGenerator.Tests). Tests still run but file/class naming marks them as compatibility-only. `AddCrestDynamicApi`/`MapCrestDynamicApi` documented as legacy in XML docs but NOT marked `[Obsolete]`.

**Key architectural decisions**:
- Level 2 does not read class-level `[CapabilityEndpointInput]` — all inputs come from HTTP method attribute Body/Input/route tokens. CEP013/CEP018/CEP019 diagnostics only apply to Level 1 for class-level inputs.
- CEP014 diagnostic message references TargetProperty for CLR assignment (not CapabilityInputPath).
- BindingEmitter TargetProperty fallback: TargetProperty → PascalCase(Name) — no CapabilityInputPath intermediate layer.
- EndpointId prefix `endpoint:{CapabilityId}` is SHOULD (not MUST) — explicit EndpointId must be non-empty with no whitespace; only conflicts with reserved legacy prefixes produce errors.

**Review iterations** (4 rounds):
- Round 1 (self-audit): 4 findings — DynamicApiSourceGenerator deleted not recycled (P0), legacy tests deleted not renamed (P1), Web.Tests legacy test not renamed (P1), Level 1 ExtractInputRecords missing IsEnum (P1). All fixed.
- Round 2 (oracle): 4 P1 findings — BindingEmitter CapabilityInputPath fallback chain, CEP014 suppression condition, Section 5.2 boundary test missing, Section 5.1 type definition test missing. All fixed.
- Round 3 (external): 2 findings — Level 2 CEP018/019/013 reading class-level `[CapabilityEndpointInput]` but Normalizer not processing them (diagnostics/generation asymmetry), XML doc TargetProperty fallback description incorrect. Fixed by removing Level 2 class-level attribute diagnostics and correcting XML doc.
- Round 4 (external): 6 findings — Level 2 Input + route token CEP013 self-contradiction (P0: Input counted as extra scalar → CEP013 fires on valid single-route-token+Input; no route token → descriptor validation fails), CEP014 diagnostic message still references CapabilityInputPath (P2), legacy test files missing compatibility-only comments (P2), boundary test missing ServiceType/ActionName patterns (P2), boundary test missing CapabilityEndpointMapper.cs in scan (P1). All fixed. CEP021 added (Level 2 Input without route token Error).

**Test counts**: 45 CapabilityEndpoint SG + 6 Boundary + 22 Legacy Web + 7 Legacy CodeGenerator — all passing. Full solution build 0 errors in 8c-affected projects.

**Boundary test coverage**: assembly reference, project reference, legacy source symbol (DynamicApiEndpointDescriptor/ServiceDescriptor/ActionDescriptor/IDynamicApiGeneratedProvider + ServiceType/ActionName patterns), CapabilityEndpoint mapping (Extensions + DescriptorValidator + CapabilityResolver + Mapper), CapabilityEndpoint emitter, Abstractions type definition.

### Phase 8d — AppService→Capability Compatibility Projection

Status: ✅ Complete

**Spec**: `docs/superpowers/specs/2026-07-09-phase-8d-appservice-compatibility-projection-design.md`
**Plan**: `docs/superpowers/plans/2026-07-09-phase-8d-appservice-compatibility-projection.md`
**Issue**: #22 (comments from #4921433901 onward reflect current design)

Core concept: Let existing `[CrestService]` AppService methods opt-in to run on the Capability Pipeline while preserving external HTTP contract. One-way migration bridge: AppService → Capability, not reverse.

**Attributes**:
- `[CapabilityCompatibilityProjection]` — class or method level opt-in (namespace: `CrestCreates.Domain.Shared.Attributes`)
- `[CapabilityCompatibilityIgnore]` — method level exclusion from projection
- `[DynamicApiIgnore]` — method level exclusion from legacy Dynamic API (existing, now also checked by compatibility generator)

**Capability ID namespace**: `compat.appservice.{kebab-case-stripped-service-name}` prefix isolates from native capabilities. Default prefix derived from service name (stripped AppService/Service suffix).

**SG outputs** (AppServiceCompatibilityGenerator, 5 files per service):
1. `GeneratedAppServiceCompatibilityCapabilities_{Name}.g.cs` — `IDescriptorProvider<CapabilityDescriptor>` with one CapabilityDescriptor per action, `ProjectionKind = AppServiceCompatibility`
2. `GeneratedAppServiceCompatibilityEndpoints_{Name}.g.cs` — `IDescriptorProvider<CapabilityEndpointDescriptor>` with endpoint descriptors + AoT-safe typed parse helpers
3. `GeneratedAppServiceCompatibilityBindings_{Name}.g.cs` — `BindInputAsync` delegates registered via ModuleInitializer into `CapabilityEndpointBindingRegistry`
4. `GeneratedAppServiceCompatibilityInvokers_{Name}.g.cs` — `ICapabilityContextAwareHandlerInvoker` per action, resolving service from DI via `context.ServiceProvider.GetRequiredService`
5. `GeneratedAppServiceCompatibilityManifest_{Name}.g.cs` — `IAppServiceCompatibilityProjectionManifestProvider` listing all projections
6. `GeneratedAppServiceCompatibilityResultContracts_{Name}.g.cs` — `[ModuleInitializer]`-registered `CapabilityEndpointResultContractRegistration.Register()` calls per endpoint

**Runtime components**:
- `ICapabilityEndpointResultContractRegistry` + `CapabilityEndpointResultContractRegistry` — per-endpoint result mapping registry keyed by `(EndpointId, Version)`, storing `Func<CapabilityExecutionResult, IResult>`. Compatibility endpoints register legacy `DynamicApiResponse` envelope semantics; native endpoints use default `CapabilityEndpointResultMapper.Map()` unchanged.
- `CapabilityEndpointResultContractRegistration` — deferred registration pattern (static Register + ApplyTo), matching `CapabilityEndpointBindingRegistry` pattern.
- `EndpointExecutionContext` — result contract context in `CrestCreates.DynamicApi` namespace (no Capability dependency).
- `CompatibilityHttpResultMapper` — neutral response envelope helper in `CrestCreates.DynamicApi`. Both legacy `DynamicApiGeneratedRuntime` and compatibility generated code call it for WrapResult/WrapGetResult/WrapVoidResult. Decouples compatibility generated code from legacy runtime.
- `CompatibilityBodyReader` — **[Obsolete]**, replaced by `CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync`. Legacy-compatible body reading for compatibility projections. Matches legacy `DynamicApiGeneratedRuntime.ReadBodyAsync` semantics: ContentLength==0 → optional?default:new T(), empty/whitespace → optional?default:new T(), invalid JSON+optional → default, invalid JSON+required → JsonException. `where T : new()` constraint.
- `AppServiceCompatibilityProjectionManifestRegistry` — static registry for manifest entries, ModuleInitializer registration.
- `AddCrestCompatibilityProjection()` — DI extension method.

**HTTP contract preservation** (P0-1, the core promise):
- `CapabilityEndpointMapper.MapResult` checks `!result.IsSuccess` FIRST — pipeline failures always use unified `CapabilityEndpointResultMapper.Map()`, never custom result contracts. Prevents compatibility projections from swallowing authorization/validation/rate-limit failures as 200 OK.
- Success responses: custom result contracts reproduce legacy `DynamicApiResponse` envelope (200+value, 200+void, 404+null for GET).
- Wrapper selection: `WrapVoidResult()` for void, `WrapGetResult(ctx.Output)` for GET non-void, `WrapResult(ctx.Output)` for other non-void.

**Symbol unification** (P0-2):
- Both `DynamicApiAotSourceGenerator` (legacy) and `AppServiceCompatibilityGenerator` (8d) check BOTH contract interface methods AND implementation methods for `[CapabilityCompatibilityProjection]`/`[CapabilityCompatibilityIgnore]`/`[DynamicApiIgnore]`.
- `HasAttributeOnContractOrImplementation` uses `FindImplementationForInterfaceMember` reverse lookup for exact symbol matching — avoids approximate signature matching that could mis-identify overloaded methods with different RefKind or generic arity.
- `EnumerateContractTypes` yields `serviceType` (class) before interfaces; C# does not propagate interface method attributes to implementing class methods, so the helper must explicitly search `serviceType.AllInterfaces`.

**No-param method handling** (P0-3):
- Envelope filter uses `a.EnvelopeTypeName is not null` (not `!a.IsSingleParam`). No-param methods return null from binding method, no empty class declaration.

**Fail-closed generation**:
- CEP037 actions are skipped (`continue`) from the actions list — no `ReadBodyAsync<T>` call emitted.
- `GenerateAll` skips ALL code generation for services with any Error-level diagnostic (CEP030/031/034/037) — service-level fail-closed, not per-action.
- `ServiceLevelFailClosed_ErrorDiagnosticSkipsEntireService` test freezes this behavior.

**Diagnostics**:
- CEP030: Class-level projection + class-level ignore conflict (Error)
- CEP031: Method-level projection + method-level ignore conflict (Error)
- CEP034: Method overload collision on CapabilityId/EndpointId (Error, fail-closed: returns empty actions model)
- CEP035: Default route prefix warning (Warning)
- CEP036: Method-level CapabilityIdPrefix/RoutePrefix on projection attribute (Warning)
- CEP037: Body parameter type does not satisfy `new()` constraint (Error) — rejects abstract, interface, array, open generic types; allows closed generic types with public parameterless constructors. Recursive `ContainsTypeParameter` helper for nested open generics.

**CapabilityDescriptor changes**:
- `ProjectionKind` property (DefinitionOnly in canonical hash, Order=100) — governance/origin metadata, not runtime contract.
- `CapabilityProjectionKind` enum: Native (0), AppServiceCompatibility (1)
- `DefinitionShapeVersion` bumped to v2 (ProjectionKind changed hash shape).

**DynamicApiConventionAnalyzer extraction**:
- 8 methods + 8 model types moved from private (in `DynamicApiAotSourceGenerator`) to internal static (in `DynamicApiConventionAnalyzer`) — shared convention derivation logic between legacy and 8d generators.

**DI unification** (P1-2):
- `AddCapabilityPipeline()` registers both `CapabilityHandlerResolver` concrete and `ICapabilityHandlerResolver` interface from the same static instance via `CapabilityHandlerResolverProvider.GetConcreteResolver()`.
- `AddCapabilityRuntime()` no longer re-registers.
- `CapabilityHandlerResolverProvider.SetResolver` is obsolete no-op; additive `Register()` is the new API.

**E2E test project**:
- `tests/Framework/Api/CrestCreates.CompatibilityProjection.E2E.Tests/` — source-generator-backed WebApplicationFactory E2E.
- `GreetingAppService` with 4 methods: GreetAsync (query-binding multi-param), GetGreetingAsync (route-binding single-param), ListGreetingsAsync (no-param, tests P0-3), DeleteGreetingAsync (void return).
- 7 success tests + 2 authorization failure tests (pipeline failure → 403/429, not 200 OK).
- `TestMarkerMiddleware` records `InvocationSource.Http` for pipeline verification.
- Added to both `CrestCreates.slnx` and `solutions/CrestCreates.All.slnx`.

**Review iterations** (5 rounds, 20+ findings fixed):
- Round 1 (external, 3 P0 + 5 P1 + 3 secondary): P0-1 HTTP contract violation (ResultContractRegistry), P0-2 method-level symbol unification, P0-3 no-param envelope CS1001, P1-1 route contract interface fallback, P1-2 DI singleton unification, P1-3 overload CEP034, P1-4 optional body descriptor, P1-5 hash version bump. All fixed.
- Round 2 (external, 1 P0 + 5 P1 + 1 P2): P0 pipeline failure swallowing (MapResult checks IsSuccess first), P1-3 CompatibilityHttpResultMapper decoupling, P1-5 AddCapabilityPipeline API stability, P1-1 CI integration (slnx), P1-2 E2E middleware, P1-4 CompatibilityBodyReader, P2 CEP036 method-level prefix warning. All fixed.
- Round 3 (1 P0 + 1 P1 + 3 P2): P0 interface method Ignore attributes not checked (HasAttributeOnContractOrImplementation), P1 CEP037 body new() constraint, P2 CEP036 class+method mix, P2 TestMarkerMiddleware InvocationSource, P2 ResultContracts skip removal. All fixed.
- Round 4 (2 P1 + 1 P2): P1-1 CEP037 SatisfiesNewConstraint rejects closed generics (fixed: accept ITypeSymbol, allow closed generics, reject arrays/open generics), P1-2 CEP037 reported but action still generated (fail-closed: skip action + service-level skip on Error diagnostics), P2 HasAttributeOnContractOrImplementation approximate signature → FindImplementationForInterfaceMember reverse lookup. All fixed.
- Round 5 (3 P2): Closed generic tests add CompilationSuccess assertions, open generic detection uses recursive ContainsTypeParameter helper, service-level fail-closed test freezes behavior. All fixed.

**Test counts**: 251 CodeGenerator + 72 DynamicApi + 3 AotFixture + 9 E2E + 35 Boundary + 137 Capability = 507 tests, all passing.

**File stats**: 17 modified + 11 new files (6 runtime/abstractions + 1 generator emitter + 4 E2E test project files), +2084/-53 lines (initial commit) + incremental review fixes.

**Key architectural decisions**:
- Compatibility projection is a one-way bridge: AppService → Capability, not reverse.
- `compat.appservice.` prefix isolates compatibility capabilities from native namespace.
- Custom result contracts only govern success responses; pipeline failures always use unified mapper.
- `CompatibilityHttpResultMapper` decouples generated code from legacy `DynamicApiGeneratedRuntime`.
- `CompatibilityBodyReader` provides legacy-compatible body reading (empty body → new T(), not BadHttpRequestException).
- Service-level fail-closed: any Error diagnostic skips entire service code generation.
- `FindImplementationForInterfaceMember` for exact interface method matching (not approximate signature).
- `ContainsTypeParameter` recursive helper for open generic detection at any nesting depth.

### Phase 8 Body Binding — Application-Owned JsonTypeInfo Architecture (2026-07-14/15)

Status: ✅ Complete (input binding only; response serialization and CRUD remain future work)

**Architecture**: The application owns the `[JsonSerializable]`-decorated `JsonSerializerContext` with its own `JsonSerializerOptions`. CrestCreates accesses `JsonTypeInfo<T>` from the application's `IOptions<JsonOptions>` at runtime — not from a framework-declared context or generated partial class. This is the correct replacement for the invalid cross-generator `[JsonSerializable]` emission approach (Roslyn SGs cannot see each other's `RegisterSourceOutput` output in the same compilation round).

**New runtime components**:
- `CapabilityEndpointJsonContractRegistry` — static type registry populated at startup by generator-emitted `[ModuleInitializer]` `RegisterBodyType(typeof(T))` calls
- `CapabilityEndpointJsonTypeInfoResolver` — resolves `JsonTypeInfo<T>` from application's `IOptions<JsonOptions>` at runtime. Fail-closed: `GetRequiredService<IOptions<JsonOptions>>()`, no fallback to reflection-based options.
- `CapabilityEndpointJsonContractValidator` — validates at startup that all registered body types have `JsonTypeInfo` available. Catches `NotSupportedException` from `options.GetTypeInfo()` for types missing from the application's `JsonSerializerContext`.
- `CapabilityEndpointBodyReader` — two public entry points:
  - `ReadNativeBodyAsync<T>` — for 8a native endpoints. Empty body → 400 BAD_REQUEST. No `emptyBodyFactory` parameter. Direct `JsonSerializer.DeserializeAsync` (STJ handles leading whitespace naturally).
  - `ReadCompatibilityBodyAsync<T>` — for 8d compatibility endpoints. Preserves legacy `CompatibilityBodyReader` empty/whitespace/null/optional semantics via `ReadToEndAsync` + `string.IsNullOrWhiteSpace`. One intentional difference: required invalid JSON throws `BadHttpRequestException` (HTTP 400) instead of raw `JsonException`.

**Generator changes**:
- 8a `CapabilityEndpointBindingEmitter` — emits `CapabilityEndpointJsonTypeInfoResolver.Resolve<T>()` + `CapabilityEndpointBodyReader.ReadNativeBodyAsync<T>()` + `RegisterBodyType(typeof(T))` in `[ModuleInitializer]`
- 8d `AppServiceCompatibilityEndpointEmitter` — emits `Resolve<T>()` + `ReadCompatibilityBodyAsync<T>()` + `RegisterBodyType(typeof(T))` in `[ModuleInitializer]`
- `DynamicApiConventionAnalyzer.ToTypeOfExpression(ITypeSymbol)` — shared helper for correct `typeof()` expressions: nullable value types use `Nullable<T>` form, nullable reference types strip `?`
- `TypeOfExpression` property on `CapabilityEndpointInputRecord` (8a) and `CompatibilityParameterModel` (8d) — replaces string-based `?`-suffix detection
- CancellationToken propagation: both 8a and 8d generators pass `context.RequestAborted`

**CRUD excluded from trimming-safe scope**: Generated CRUD DTO types are invisible to the application's `JsonSerializerContext` (same-round Roslyn SG limitation). CRUD continues using legacy `DynamicApiGeneratedRuntime.ReadBodyAsync<T>` (reflection-based). Tracked as GitHub issue #61.

**Deprecated/Obsolete components**: `CapabilityEndpointJsonRuntime`, `CompatibilityBodyReader`, three per-generator `JsonContextEmitter` classes.

**AOT fixture** (Tier 2: NativeAOT-verified):
- `tests/Framework/Api/CrestCreates.CapabilityEndpoint.AotFixture/` — publishable web host with `IsAotCompatible=true`, `PublishAot=true`, `WarningsAsErrors` for IL2026/IL2070/IL2072/IL2075/IL3050/SYSLIB1034
- `tests/Framework/Api/CrestCreates.CapabilityEndpoint.AotFixture.Tests/` — WebApplicationFactory tests (3 tests: POST body binding, GET no-param, JsonTypeInfo resolution)

**Deployment guarantee**: 8a/8d request input binding is NativeAOT-safe by construction. AotFixture validates compile-time AOT analyzers + runtime NativeAOT publish. AOT tier model: Tier 1 (Core: NativeAOT-first), Tier 2 (HTTP/MCP/Workflow: NativeAOT-verified), Tier 3 (EF Core/integrations: AOT separately declared), Tier 4 (Legacy: trimming/JIT-only).

**Response serialization debt**: Uses `Results.Json(object?)` — not `JsonTypeInfo<T>`-based. Requires migration to trimming-safe response writing.

**Review iterations** (3 rounds after initial implementation):
- Round 1 (external, 2 P0 + 5 P1): P0-1 fixture didn't test POST body, P0-2 CRUD DTOs invisible to STJ, P1-1 shared body reader changed compatibility semantics, P1-2 missing CancellationToken, P1-3 non-AOT fallback, P1-4 nullable value type typeof, P1-5 not real NativeAOT fixture. All fixed.
- Round 2 (external, 2 P1 + 4 P2): P1 leading whitespace JSON misread (single-char peek), P1 PublishTrimmed unverified (adopted Plan B: narrowed docs), P1 contradictory cross-generator visibility in arch-design.md. P2 validator test coverage, P2 compatibility exception difference documented, P2 AOT-safe terminology unified to trimming-safe/source-generated. All fixed.
- Round 3 (external, approved with minor items): Application-owned JsonTypeInfo architecture approved. 8a/8d generator wiring approved. CRUD rollback + #61 approved.

**Test counts**: 251 CodeGenerator + 72 DynamicApi + 3 AotFixture + 9 E2E + 35 Boundary + 137 Capability = 507 tests, all passing.

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
 15. Phase 8b Dynamic API Descriptor (Issue #20) — CapabilityEndpointDescriptor as projection metadata over CapabilityDescriptor, with registry, validator (route conflict, null guards, InheritCapability fail-closed), relationship extractor, canonical hash profiles, AoT-safe kind naming, boundary test. 4 review rounds, 15 findings fixed. 37 Web.Tests + 6 Metadata.Tests + 27 Boundary tests.
  16. Phase 8a Capability Endpoint Projection (Issue #19) — Capability→HTTP without AppService, zero DynamicApi bridge. SG produces DescriptorProvider + BindingContract; registry-driven mapping via MapCrestCapabilityEndpoints(); ICapabilityPipeline descriptor overload; DX Layering (Level 0/1/2); 4 review rounds, 30+ findings fixed. 29 SG + 35 DynamicApi + 10 Capability + 33 Boundary tests.
  17. Phase 8c Legacy Dynamic API Boundary (Issue #21) — legacy deprecation labeling + boundary tests + 8a debt fixes. 7 PRs, 30 ACs, 4 review rounds (16 findings total). EndpointId/EndpointVersion/TargetProperty independent properties, CEP013 Error + Dictionary fallback deletion, CEP017-021 diagnostics, DynamicApiSourceGenerator recycled to 99_RecycleBin, legacy test rename with compatibility-only annotations, boundary tests (6 tests covering assembly/project/source/emitter/mapping/Abstractions). 45 SG + 6 Boundary + 22 Legacy Web + 7 Legacy CodeGenerator tests.
  18. Phase 8d AppService→Capability Compatibility Projection (Issue #22) — opt-in migration bridge from [CrestService] AppService to Capability Pipeline preserving HTTP contract. 5 review rounds, 20+ findings fixed. ResultContractRegistry for HTTP envelope preservation, HasAttributeOnContractOrImplementation for symbol unification, CompatibilityHttpResultMapper decoupling, CompatibilityBodyReader legacy body reading, CEP030-037 diagnostics, service-level fail-closed generation, source-generator-backed E2E tests. 248 CodeGenerator + 45 DynamicApi + 9 E2E + 34 Boundary = 336 tests.
  19. Phase 8 Body Binding — Application-Owned JsonTypeInfo Architecture — replaced invalid cross-generator `[JsonSerializable]` emission with application-owned `JsonSerializerContext` + runtime `JsonTypeInfo` resolution. `CapabilityEndpointBodyReader` split into `ReadNativeBodyAsync` (8a) and `ReadCompatibilityBodyAsync` (8d). CRUD excluded from AOT-safe scope (GitHub #61). 3 review rounds. 251 CodeGenerator + 72 DynamicApi + 3 AotFixture + 9 E2E + 35 Boundary + 137 Capability = 507 tests.

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
- Phase 8a Capability Endpoint Projection is the new mainline for HTTP exposure — Capability→HTTP without AppService.
- Old DynamicApiAotSourceGenerator is legacy AppService HTTP exposure; do not extend with topology/activation/MCP.
- `MapCrestDynamicApi()` (legacy) and `MapCrestCapabilityEndpoints()` (new) coexist without wrapping each other.
- Phase 8c completed: legacy path labeled compatibility-only, boundary tests guard against cross-path contamination, EndpointId/EndpointVersion/TargetProperty independent properties, CEP013 Error + Dictionary fallback deleted, DynamicApiSourceGenerator recycled to 99_RecycleBin.
- Level 2 does not read class-level `[CapabilityEndpointInput]` — all inputs from HTTP method attribute only. Level 2 explicit Input binds a route token's type (not an additional scalar input). CEP021 fires when Input has no route token to bind to.

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

### Capability Endpoint Projection (Phase 8a)

- `CapabilityEndpointDescriptor` is a projection descriptor over `CapabilityDescriptor` — describes HTTP exposure metadata, not business logic or runtime execution
- Four concern separation: Descriptor (projection metadata) / BindingContract (SG-produced CLR binding) / Dispatcher (unified facade) / ResultMapper (fixed mapping)
- EndpointId defaults to `endpoint:{CapabilityId}` — DX shortcut; independent EndpointId/EndpointVersion resolved in 8c
- TargetProperty on `[CapabilityEndpointInput]` controls CLR property assignment (SG-only, not in descriptor); fallback to PascalCase(Name)
- CEP013 is Error (not Warning) after 8c; Level 1 covers Route/Query/Header, Level 2 covers route tokens only. Level 2 explicit Input binds a route token's type — not an additional scalar input. CEP021 fires when Input has no route token to bind to.
- Exact version resolution is fail-closed — `Version > 0` with `SelectionMode.Exact` throws on miss, no fallback to latest active
- `ICapabilityPipeline.ExecuteAsync(CapabilityDescriptor, ...)` overload skips registry resolution — descriptor overload is the authoritative path
- `ValidationMiddleware` uses `GetByVersion(id, version)` not `GetByName(name)` — respects captured descriptor version
- BindingRegistry is process-wide generated registry — no runtime unload/reload/hot projection (by design for 8a)
- ~~SG generates generic `ReadBodyAsync<T>` — AOT-safe `JsonTypeInfo<T>` overload exists but not yet used by SG (CEP015 debt)~~ **Resolved**: CEP015 deleted, replaced by application-owned `JsonSerializerContext` + `CapabilityEndpointJsonTypeInfoResolver` + startup validation
- Level 2 sugar attributes require `[CapabilityEndpointSet]` container (CEP016)
- Level 2 does not read class-level `[CapabilityEndpointInput]` — all inputs from HTTP method attribute Body/Input/route tokens
- Route token extraction strips constraints/catch-all/optional — aligned with validator behavior

### AppService→Capability Compatibility Projection (Phase 8d)

- One-way migration bridge: AppService → Capability Pipeline, not reverse. Opt-in via `[CapabilityCompatibilityProjection]` on `[CrestService]` classes or methods.
- `compat.appservice.` prefix isolates compatibility capabilities from native namespace.
- Custom result contracts only govern success responses; pipeline failures always use unified `CapabilityEndpointResultMapper.Map()` — never swallowed as 200 OK.
- `CompatibilityHttpResultMapper` is the neutral response envelope helper — both legacy and compatibility generated code call it. Decouples from `DynamicApiGeneratedRuntime`.
- `CompatibilityBodyReader` is **[Obsolete]** — replaced by `CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync`. Legacy body reading semantics preserved: empty/whitespace/null + optional → default, required → factory(). One intentional difference: required invalid JSON → `BadHttpRequestException` (HTTP 400) instead of raw `JsonException`.
- Service-level fail-closed: any Error diagnostic (CEP030/031/034/037) skips entire service code generation.
- `HasAttributeOnContractOrImplementation` uses `FindImplementationForInterfaceMember` for exact symbol matching — C# does not propagate interface method attributes to implementing class methods.
- `CapabilityDescriptor.ProjectionKind` is DefinitionOnly in canonical hash (Order=100) — governance/origin metadata, not runtime contract.
- `DynamicApiConventionAnalyzer` is the shared convention derivation layer between legacy and 8d generators.
- `CapabilityHandlerResolverProvider.SetResolver` is obsolete no-op; additive `Register()` is the new API.

### Phase 8 Body Binding — Application-Owned JsonTypeInfo (2026-07-14/15)

- Application owns `[JsonSerializable]`-decorated `JsonSerializerContext` with its own `JsonSerializerOptions`; CrestCreates accesses `JsonTypeInfo<T>` from application's `IOptions<JsonOptions>` at runtime via `CapabilityEndpointJsonTypeInfoResolver`.
- Roslyn Source Generators cannot see each other's `RegisterSourceOutput` output in the same compilation round. CrestCreates generators must NOT emit `[JsonSerializable]` partial classes expecting STJ to process them.
- `CapabilityEndpointBodyReader` has two entry points: `ReadNativeBodyAsync<T>` (8a, empty body → 400) and `ReadCompatibilityBodyAsync<T>` (8d, preserves legacy empty/whitespace/null/optional semantics).
- `CapabilityEndpointJsonTypeInfoResolver` uses `GetRequiredService<IOptions<JsonOptions>>()` — no fallback to reflection-based options. Missing JSON configuration causes explicit startup failure.
- `CapabilityEndpointJsonContractValidator` validates at startup that all registered body types have `JsonTypeInfo` available — fail-closed replacement for CEP015 compile-time warning.
- `DynamicApiConventionAnalyzer.ToTypeOfExpression(ITypeSymbol)` — shared helper for correct `typeof()` expressions: nullable value types use `Nullable<T>` form, nullable reference types strip `?`.
- CEP015 deleted (replaced by startup validation). CEP037 Error-level constructibility check applies only to compatibility path (native path uses null `emptyBodyFactory`, no body construction needed).
- CRUD body binding is NOT trimming-safe — generated DTO types invisible to application's `JsonSerializerContext`. Tracked as GitHub issue #61.
- Response serialization uses `Results.Json(object?)` — not yet trimming-safe. Future work.
- Deployment guarantee: trimming-safe by construction for 8a/8d input binding. PublishTrimmed E2E validation pending. Full NativeAOT is future target.
- Code terminology: "source-generated JSON metadata", "trimming safety" — not "AOT-safe" (which implies full NativeAOT guarantee).

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
  - `AgentToolName` — 32 canonical tool name constants (30 facade + 2 manifest query; single source of truth)
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
    - **32-tool full coverage (30 facade + 2 manifest query)**: All 30 manifest tools now enforce visibility via `_resourceResolver` + `DenyIfInvisible` pipeline
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
- **Tool count**: 30 → 32 (BuildDescriptorReviewReport + RenderDescriptorReviewReport are new facade tools; total AgentToolName constants = 32: 30 facade + 2 manifest query)
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
- **AoT safety**: TryParseReviewDecision lives in DescriptorActivationReviewDecisionParser (implementation project, not Abstractions) with JsonSerializerContext — not on the record in Abstractions for AoT safety
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

### Agent Authoring Runtime + Prompt Hash (Phase 7g Task 3, 2026-07-01)

Status: Superseded by full Phase 7g completion below.

### LLM-backed Descriptor Authoring Adapter (Phase 7g, 2026-07-01)

Status: Completed.

**New projects** (3):
- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/` — framework contracts, interfaces, AoT JSON context, diagnostic codes
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/` — provider-agnostic runtime (prompting, parsing, agent, clients, DI)
- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/` — OpenAI-compatible provider (separate from core runtime)

**New test project** (1):
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/` — 38 tests (contracts + hashing + parser + agent + golden + boundary)

**Abstractions** (17 files):
- Authoring contracts: `IDescriptorAuthoringAgent`, `DescriptorAuthoringPlan`, `DescriptorAuthoringResult`, `DescriptorDraftSet`, `DescriptorAuthoringDiagnostic`, `DescriptorAuthoringStatus`, `DescriptorAuthoringDiagnosticCodes`
- Prompt contracts: `DescriptorAuthoringPromptInput`, `DescriptorAuthoringPromptOutput`, `DescriptorAuthoringDescriptorProjection`, `DescriptorAuthoringMemoryProjection`
- Model contracts: `IDescriptorAuthoringModelClient`, `DescriptorAuthoringModelRequest`, `DescriptorAuthoringModelResponse`, `DescriptorAuthoringModelProfile`, `DescriptorAuthoringProviderProfile`
- JSON: `DescriptorAuthoringJsonSerializerContext`

**Runtime** (11 files):
- Prompting: `IDescriptorAuthoringPromptInputFactory` + `DefaultDescriptorAuthoringPromptInputFactory`, `IDescriptorAuthoringPromptInputHashService` + `DefaultDescriptorAuthoringPromptInputHashService`, `IDescriptorAuthoringPromptBuilder` + `DefaultDescriptorAuthoringPromptBuilder`
- Parsing: `IDescriptorAuthoringOutputParser` + `JsonDescriptorAuthoringOutputParser`, `DescriptorAuthoringParseContext`, `DescriptorAuthoringProviderOutputDto`
- Authoring: `LlmDescriptorAuthoringAgent`
- Clients: `RecordedDescriptorAuthoringModelClient`, `FakeDescriptorAuthoringModelClient`
- DI: `AgentAuthoringServiceCollectionExtensions`

**HTTP Provider** (6 files):
- `OpenAICompatibleDescriptorAuthoringModelClient`, `OpenAICompatibleChatRequest`, `OpenAICompatibleChatResponse`
- `IDescriptorAuthoringCredentialProvider` + `DefaultDescriptorAuthoringCredentialProvider`
- DI: `AgentAuthoringHttpServiceCollectionExtensions`

**Golden scenario**:
- `CompanyCertificationLlmFixture` — recorded provider-output JSON for company certification (HumanTask Create + Workflow Update)
- `GoldenScenarioLlmFixtureTests` — 4 tests verifying full LLM agent pipeline with recorded fixtures
- `CompanyCertificationAuthoringGoldenScenarioRunner` — added constructor overload accepting framework `IDescriptorAuthoringAgent`

**Sample migration**:
- `FakeCompanyCertificationAuthoringAgent` now consumes framework `CrestCreates.Agent.Authoring.Abstractions` types (not local sample types)
- Sample `DescriptorAuthoringResult`, `DescriptorDraftSet`, `IDescriptorAuthoringAgent`, `DescriptorAuthoringPlan` replaced by framework types
- 20 sample tests pass (including 14 golden scenario tests)

**Key invariants**:
- LLM agent only produces drafts — never activates, approves, mutates registries, executes handlers, or bypasses Control Plane review
- Prompt hashing delegates to `IAgentPromptHashService` via Prompting infrastructure — NO ad-hoc SHA-256, NO string builder, NO pipe-delimited format, NO direct `ICanonicalHashComputer` usage
- Hash metadata: `Purpose=SourceIdentity`, `Scope=InternalFull`, `ArtifactKind=AgentPromptInputEvidence` (via `CanonicalHashArtifactNames.AgentPromptInputEvidence`)
- Output evidence hash excludes `ResponseText` — uses `DescriptorAuthoringModelResponseEvidenceProjection` (safe fields only)
- Every `DescriptorAuthoringResult` carries `PromptInputEvidence` and `PromptOutputEvidence` summaries from `IAgentPromptEvidenceFactory`
- Parser validates `promptInputHash` in provider output against `DescriptorAuthoringParseContext.ExpectedPromptInputHash`
- Parser receives `DescriptorAuthoringParseContext` (tenantId, authorId, authorKind, timestamp, expectedPromptInputHash) — no hard-coded values
- `CrestCreates.Agent.DraftContracts` is NOT a dependency of Authoring runtime — authoring produces domain-level `DescriptorDraftSet`, materialization uses existing `HumanTaskDescriptorDraftPayload`/`WorkflowDescriptorDraftPayload` directly
- OpenAI client sets Authorization on each `HttpRequestMessage` (not shared `_httpClient.DefaultRequestHeaders`)
- Missing fixture in RecordedClient surfaces as `ProviderUnavailable`, not empty success
- Boundary enforced: Authoring runtime does NOT reference ControlPlane, DraftContracts, HTTP SDKs, or provider implementations
- Authoring Abstractions does NOT reference HumanTask/Workflow/Schema/Form/Event/Capability Abstractions — only `Core.Abstractions`, `Snapshot.Abstractions`, `Metadata.Abstractions`, `Metadata.ContextPack.Abstractions`, `Agent.Memory.Abstractions`, `Agent.Prompting.Abstractions`, `DescriptorDraft.Abstractions`
- Authoring runtime references HumanTask/Workflow Abstractions (for parser materialization) and Prompting.Abstractions (for evidence integration) but NOT Prompting runtime or their runtime execution projects

**Test counts**: 50 Authoring tests + 15 Prompting tests + 20 Sample tests + 48 Memory tests + 479 ControlPlane tests + 20 Boundary tests — all passing. Full solution build 0 errors.

---

### Phase 7h — Agent Prompt Contracts and Prompt Versioning (2026-07-02)

**Status**: ✅ Complete

**Spec**: `docs/superpowers/specs/2026-07-02-phase-7h-agent-prompt-evidence-contract-design.md`
**Plan**: `docs/superpowers/plans/2026-07-02-phase-7h-agent-prompt-evidence-contract.md`

**New projects**:
- `CrestCreates.Agent.Prompting.Abstractions` — prompt evidence contracts (11 files)
- `CrestCreates.Agent.Prompting` — prompt evidence runtime (5 files)
- `CrestCreates.Agent.Prompting.Tests` — 15 tests

**Prompting.Abstractions** (11 files):
- Identity: `AgentPromptTemplateId`, `AgentPromptVersion`, `AgentPromptContractVersion`, `AgentPromptModelProfileRef`, `AgentPromptProviderProfileRef`
- Evidence: `AgentPromptInputEvidence`, `AgentPromptOutputEvidence`, `AgentPromptProviderObservation`, `AgentPromptDiagnostic`, `AgentPromptPurpose`
- Summaries: `AgentPromptInputEvidenceSummary`, `AgentPromptOutputEvidenceSummary`
- Factory: `IAgentPromptEvidenceFactory`
- Projector: `IAgentPromptCanonicalPayloadProjector<T>`
- Hash: `IAgentPromptHashService`
- Request: `AgentPromptEvidenceCreationRequest<T>`
- JSON: `AgentPromptingJsonSerializerContext`
- Shape versions: `AgentPromptCanonicalShapeVersions` (InputEvidence, OutputEvidence)
- Summary factory: `AgentPromptEvidenceSummaryFactory` (static, moved from runtime to Abstractions)

**Prompting.Runtime** (5 files):
- `DefaultAgentPromptHashService` — delegates to `ICanonicalHashComputer` + `IAgentPromptCanonicalPayloadProjector<T>` registry
- `DefaultAgentPromptEvidenceFactory` — creates input/output evidence with summaries; produces `OutputHashUnavailable` diagnostic when output hash is null
- `InMemoryAgentPromptTemplateRegistry` — defensive-copy returns from Find/List
- `AgentPromptingServiceCollectionExtensions` — `AddAgentPrompting()` DI registration
- `CanonicalHashArtifactNames` — 3 new constants: `AgentPromptInputEvidence`, `AgentPromptOutputEvidence`, `AgentPromptTemplateDescriptor`

**Authoring integration** (3 new files + 8 modified):
- `DescriptorAuthoringPromptInputProjector` — `IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>` (order-independent canonical JSON)
- `DescriptorAuthoringModelResponseEvidenceProjection` — safe output record (excludes `ResponseText`)
- `DescriptorAuthoringModelResponseEvidenceProjector` — `IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>`
- `DefaultDescriptorAuthoringPromptInputHashService` → [Obsolete] adapter around `IAgentPromptHashService`; uses default template identity from `LlmDescriptorAuthoringAgentOptions`
- `DefaultDescriptorAuthoringPromptInputFactory` → no longer computes hash (returns `PromptInputHash = null`)
- `LlmDescriptorAuthoringAgent` → new `IAgentPromptEvidenceFactory` dependency; creates input/output evidence; attaches summaries on all result paths
- `LlmDescriptorAuthoringAgentOptions` → prompt template identity properties
- `DescriptorAuthoringResult` → `PromptInputEvidence`/`PromptOutputEvidence` summary properties
- `DescriptorAuthoringJsonSerializerContext` → 5 new `[JsonSerializable]` entries
- `AgentAuthoringServiceCollectionExtensions` → 2 projector registrations; `AddAgentPrompting()` called by consumer (host/Platform), NOT by Authoring

**Boundary tests** (4 new):
- `AgentPromptingAbstractions_DoesNotReferenceControlPlaneDraftContractsAuthoringHttpOrPlatform`
- `AgentPromptingRuntime_DoesNotReferenceControlPlaneDraftContractsAuthoringHttpOrPlatform`
- `AgentPrompting_DoesNotExposePromptExecutorModelClientOrCompletionService`
- `AgentAuthoringRuntime_DoesNotReferencePromptingRuntime`

**Key invariants**:
- Prompting.Abstractions does NOT reference ControlPlane, DraftContracts, Authoring.Http, or Platform
- Prompting runtime does NOT expose `IAgentPromptExecutor`, `IAgentPromptModelClient`, or `IAgentPromptCompletionService`
- Output evidence hash excludes `ResponseText` — only safe fields (ProviderName, ModelName, PromptInputHash, FailureKind, FailureDetail)
- Input evidence hash uses `CanonicalHashArtifactNames.AgentPromptInputEvidence` + `CanonicalHashPurposeNames.SourceIdentity`
- Output evidence hash uses `CanonicalHashArtifactNames.AgentPromptOutputEvidence` + `CanonicalHashPurposeNames.AuditEvidence`
- `IAgentPromptCanonicalPayloadProjector<T>` is AoT-safe — no reflection, no `JsonTypeInfo<T>`, no runtime type scanning; projectors must write one complete JSON value (WriteStartObject/EndObject)
- Missing projector throws `InvalidOperationException` instead of falling back to reflection serialization
- `AgentPromptCanonicalShapeVersions` centralizes shape version strings (not inline in DefaultAgentPromptHashService)
- `InMemoryAgentPromptTemplateRegistry` returns defensive copies from Find/List — metadata dictionaries are not shared with callers
- `AgentPromptPurpose` is a sealed record (semantic string), not an enum — extensible without recompilation
- `AgentPromptDiagnostic.Severity` uses `string` (not `SeverityLevel`) — Prompting.Abstractions does not reference Core.Abstractions
- `AgentPromptDiagnostic.Code` uses `string` (not `DiagnosticCode`) — consistent with `AgentToolDiagnostic.Code` pattern

**Test counts**: 15 Prompting + 50 Authoring + 20 Boundary — all passing. Full solution build 0 errors.

---

### Phase 7g+ — LLM-backed Agent Memory Compression and Extraction Adapter (2026-07-02)

**Status**: ✅ Complete

**New projects**:
- `CrestCreates.Agent.Memory.Llm` — LLM-backed memory compression and extraction runtime
- `CrestCreates.Agent.Memory.Llm.Tests` — 48 tests

**Memory.Llm project structure**:
- `Model/` — `AgentMemoryLlmAdapterOptions`, `AgentMemoryLlmContractVersions`, `AgentMemoryLlmModelRequest`, `AgentMemoryLlmModelResponse`, `AgentMemoryLlmProviderFailureKind`
- `Clients/` — `IAgentMemoryLlmModelClient`, `RecordedAgentMemoryLlmModelClient`, `FakeAgentMemoryLlmModelClient`
- `Prompting/` — `AgentMemoryCompressionPromptInput`, `AgentMemoryExtractionPromptInput`, `IAgentMemoryCompressionPromptBuilder`, `IAgentMemoryExtractionPromptBuilder`, `DefaultAgentMemoryCompressionPromptBuilder`, `DefaultAgentMemoryExtractionPromptBuilder`
- `Compression/` — `LlmAgentContextCompressor`, `IAgentMemoryCompressionOutputParser`, `JsonAgentMemoryCompressionOutputParser`, `AgentMemoryCompressionParseResult`, `AgentMemoryCompressedBlockDto`
- `Extraction/` — `LlmAgentMemoryExtractor`, `IAgentMemoryExtractionOutputParser`, `JsonAgentMemoryExtractionOutputParser`, `AgentMemoryExtractionParseResult`, `AgentMemoryCandidateDto`
- `Validation/` — `AgentMemoryLlmDiagnosticCodes`, `AgentMemoryLlmDiagnostics`, `AgentMemoryLlmOutputValidators`
- `CanonicalHashing/` — `AgentMemoryCompressionPromptInputProjector`, `AgentMemoryExtractionPromptInputProjector`, `AgentMemoryCompressionOutputProjector`, `AgentMemoryExtractionOutputProjector`
- `AgentMemoryLlmServiceCollectionExtensions` — `AddAgentMemoryLlmCompressor()`, `AddAgentMemoryLlmExtractor()`, `AddAgentMemoryLlm()` opt-in DI with double-registration guard

**DI registration pattern**:
- `AddAgentMemoryLlmCompressor()` / `AddAgentMemoryLlmExtractor()` are per-adapter opt-in — each replaces only its own interface
- `AddAgentMemoryLlm()` is a convenience method that calls both
- All require `AddAgentMemoryRuntime()` and `AddAgentPrompting()` first
- LLM adapters inject `IAgentContextCompressor`/`IAgentMemoryExtractor` as fallback (resolved from prior DI registration)
- `IAgentMemoryLlmModelClient` must be registered separately (no default client)
- Double-registration guard throws `InvalidOperationException` if called twice
- Input/output projectors registered for both compression and extraction

**Key invariants**:
- LLM extraction always produces `Status = AgentMemoryStatus.Candidate` — never Active/Authoritative from LLM output
- Unknown `Kind` defaults to `ProjectFact` (safest); unknown `Confidence` defaults to `Unknown`
- Content is sanitized before prompting and before storage — LLM never sees raw content
- Provider failure (rate limit, timeout, etc.) triggers fallback to deterministic extractor with per-failure-kind diagnostics
- Parse failure triggers fallback with `FallbackToDeterministicCompressor`/`FallbackToDeterministicExtractor` diagnostic
- Fallback path preserves input evidence summary and LLM-phase diagnostics
- Rejected content skipped with `ContentRejected` diagnostic (consistent with DefaultAgentContextCompressor)
- `MaxCompressedBlockCount`, `MaxCompressedBlockCharacters`, `MaxCandidateCount`, `MaxCandidateCharacters` all enforced post-parse with truncation diagnostics
- `MaxCandidateConfidence` enforced via `CapConfidence` validator (default: Medium)
- Extraction uses `ExtractionAttemptResult` (candidates + diagnostics) — no exception-based control flow
- `CanonicalHashArtifactNames` adds `AgentMemoryCompressedOutput` and `AgentMemoryCandidateOutput`
- `AgentPromptCanonicalShapeVersions` adds `MemoryCompressionOutput` and `MemoryExtractionOutput`
- Memory output hash uses `Purpose = SourceIdentity` with domain payload (not AuditEvidence)
- `IAgentPromptHashService.ComputeOutputHash` / `IAgentPromptEvidenceFactory.CreateOutputEvidence` accept optional `artifactKind`, `canonicalShapeVersion`, `purpose` parameters
- `AgentCompressedContext` and `AgentMemoryCandidate` carry `PromptInputEvidence` + `PromptOutputEvidence` summaries
- Template/Version/Contract constants in `AgentMemoryLlmContractVersions` (not options) — no over-configurable surface
- No `IAgentPromptExecutor`, `ModelClient` (non-memory), or `CompletionService` in the project
- No `DateTimeOffset.UtcNow` in production code
- No reflection (`Enum.IsDefined`, `GetTypeInfo`, `Assembly.Load`) — AoT/Trim safe
- AOT: `AgentMemoryLlmJsonSerializerContext` includes `IReadOnlyList<>` variants for all DTO types

**Boundary tests** (3):
- `AgentMemoryAbstractions_DoesNotReferenceControlPlaneAbstractions`
- `AgentMemoryProjects_DoNotReferenceForbiddenRuntimeOrPlatformLayers`
- `AgentMemoryLlm_DoesNotReferenceControlPlaneOrPlatform`

**Test counts**: 48 Memory.Llm + 15 Prompting + 50 Authoring + 21 Boundary — all passing. Full solution build 0 errors.

---

### Phase 8b — Dynamic API Descriptor (2026-07-06)

**Status**: ✅ Complete

**Spec**: `docs/superpowers/specs/2026-07-03-phase-8b-dynamic-api-descriptor-design.md`
**Plan**: `docs/superpowers/plans/2026-07-03-phase-8b-dynamic-api-descriptor.md`

**New project**:
- `CrestCreates.DynamicApi.Abstractions` — endpoint descriptor model types (10 files)

**DynamicApi.Abstractions** (10 files):
- `CapabilityEndpointDescriptor` — sealed class, implements IDescriptor + IVersionedDescriptor, references CapabilityDescriptor via `VersionedDescriptorRef<CapabilityDescriptor>`
- `CapabilityEndpointHttpMethod` — enum (None=0, Get=1, Post=2, Put=3, Patch=4, Delete=5)
- `CapabilityEndpointParameterDescriptor` — sealed record (Name, Source, IsRequired, DataType, DefaultValue)
- `CapabilityEndpointRouteTemplate` — sealed record value object
- `CapabilityEndpointRelationship` — sealed record (Endpoint → Capability relationship edge)
- `CapabilityEndpointAuthorizationMode` — enum (InheritCapability=0, RequireAuthenticated=1, AllowAnonymous=2)
- `CapabilityEndpointParameterSource` — enum (Body=0, Query=1, Route=2, Header=3)
- `CapabilityEndpointOutputMapping` — sealed record (SuccessStatusCode, ErrorStatusCode, ResponseType)
- `CapabilityEndpointInputBinding` — sealed record (Name, Source, IsRequired, DataType, DefaultValue)
- `DescriptorKind/DynamicApiEndpointDescriptorKind.cs` + `DynamicApiEndpointDescriptorKindNames.cs` — DescriptorKind.DynamicApiEndpoint = 7

**DynamicApi** (3 new files):
- `CapabilityEndpointRegistry` — Registry + ByCapabilityIdIndex (FrozenDictionary O(1) lookup by CapabilityId)
- `CapabilityEndpointDescriptorValidator` — validates identity/route/bindings/output/capability authority/projection + cross-descriptor HttpMethod+RoutePattern uniqueness + null nested metadata fail-closed + InheritCapability authority check (high-risk + no-permissions → error) + trailing slash normalization
- `CapabilityEndpointRelationshipExtractor` — Endpoint → Capability relationship extraction

**Metadata** (5 canonical hash profiles):
- `CapabilityEndpointDescriptorCanonicalHashProfile`, `CapabilityEndpointHttpMethodCanonicalHashProfile`, `CapabilityEndpointParameterDescriptorCanonicalHashProfile`, `CapabilityEndpointRouteTemplateCanonicalHashProfile`, `CapabilityEndpointRelationshipCanonicalHashProfile`
- CCHASH009 suppressed per-file via `#pragma warning disable` in CapabilityEndpointDescriptorCanonicalHashProfile.cs (governance-layer naming: CapabilityEndpoint describes "capability endpoints", not Dynamic API endpoints)

**ControlPlane** (1 modified file):
- `AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind` — added DescriptorKind.DynamicApiEndpoint range; `kind.ToString()` replaced with `DescriptorKindNames.ToCanonicalString(kind)` (AoT-safe)

**Modified files** (6):
- `DescriptorKind.cs` — DynamicApiEndpoint = 7
- `DescriptorKindNames.cs` — DynamicApiEndpoint canonical string + ToCanonicalString switch arm
- `DynamicApi.csproj` — DynamicApi.Abstractions reference
- `Metadata.csproj` — DynamicApi.Abstractions reference
- `DynamicApiModule.cs` — DI registration (AddSingleton for registry/validator/extractor, ICapabilityRegistry required/fail-closed)
- `CrestCreates.slnx` + `CrestCreates.All.slnx` + `CrestCreates.Framework.slnx` + `CrestCreates.Platform.slnx`

**Review iterations** (4 rounds, 15 findings fixed):
1. AllowAnonymous fail-open → ICapabilityRegistry required (fail-closed)
2. DI mixed Add/TryAdd → IRegistryValidator uses AddSingleton (multi-registration)
3. DescriptorKindNames switch missing default → ArgumentOutOfRangeException
4. ExtractRouteTokens missing {**path} and {id?} → catch-all/optional handling
5. CapabilityEndpointHttpMethod missing None=0 → sentinel + validator check
6. GetByCapability O(n) linear scan → ByCapabilityIdIndex O(1)
7. CCHASH009 project-wide NoWarn → per-file #pragma
8. CrestCreatesCodeGeneration=false project-wide → removed, per-file #pragma CC1001
9. DynamicApi.Abstractions missing from solution files → added to All/Framework/Platform slnx
10. Missing HttpMethod+RoutePattern conflict validation → ValidateUniqueMethodRoute + NormalizeRoutePattern
11. Null nested metadata not fail-closed → InputBindings/OutputMapping/Projection null guards
12. kind.ToString() not AoT-safe → DescriptorKindNames.ToCanonicalString()
13. InheritCapability fail-open → switch over AuthorizationMode, high-risk+no-permissions → error
14. NormalizeRoutePattern missing trailing slash → TrimEnd('/')
15. Missing Metadata → Framework/Api boundary test → MetadataProjects_DoNotReferenceFrameworkApiImplementations

**Test counts**: 37 Web.Tests + 6 Metadata.Tests + 27 Boundary — all passing. Full solution build 0 errors in Phase 8b-affected projects.

**Follow-up items** (not blocking):
- Metadata → DynamicApi.Abstractions dependency direction: Metadata is aggregation layer with 6+ existing Abstractions references; boundary test protects against implementation references. Future: consider moving hash profiles to DynamicApi side or multi-assembly registration
- ByCapabilityIdIndex is public → can be internal
- ExtractRouteTokens / NormalizeRoutePattern parsing logic sync → shared RouteTemplateParser
- GetByCapability sorting stability → add when context pack / graph display needs it

## Recommended Next Thread Entry Prompt

If a future thread should resume from this state, use a prompt like:

> Read `/memory.md` first. Continue from the current CrestCreates platform status. Treat completed items as closed unless you find contradictory code. Focus on unresolved work only. Open items: Audit Logging Task 4 governance closure, Localization, Blob/File platformization, Background Jobs / Distributed Event reliability, Phase 8 response serialization trimming safety, CRUD body binding trimming safety (#61), PublishTrimmed E2E validation. Phase 8d (AppService→Capability Compatibility Projection) and Phase 8 Body Binding (application-owned JsonTypeInfo) are complete.

---
