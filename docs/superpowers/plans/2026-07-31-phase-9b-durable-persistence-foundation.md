# Phase 9b Durable Persistence Foundation Implementation Plan

> Implement the approved durability contracts through ordered TDD slices.
> The Spec is normative. This Plan owns project placement, dependency edges,
> file changes, SQL/migration layout, provider mechanics, fixtures, and
> executable evidence without reopening the approved design.

**Goal:** Deliver one provider-neutral Runtime persistence mainline with a Full
Semantic InMemory provider and a Full Durable direct-Npgsql PostgreSQL provider.
Atomically commit Workflow suspension, restore exact pinned descriptors and
registered state after restart, preserve optimistic concurrency and tenant
isolation, persist immutable Snapshot evidence, implement the Phase 9a durable
Audit sink contract, and execute the real PostgreSQL path under NativeAOT.

**Spec:** `docs/superpowers/specs/2026-07-31-phase-9b-durable-persistence-foundation-design.md`

**Issue:** #24

**Branch:** `feature/phase-9b-durable-persistence-foundation-24`

**Spec status:** APPROVED / Ready for Implementation Plan

**Plan status:** APPROVED / Ready for Implementation

```text
Dependency graph: FIXED BY PLAN
TDD acceptance scaffold: REQUIRED FIRST
InMemory semantic tier: FULL
PostgreSQL durable tier: FULL
Outbox delivery: OUT OF SCOPE
Agent Tool reconciliation: #70
NativeAOT evidence: PUBLISH + LINK + RUN REQUIRED
```

---

## 1. Execution Rules

- Use `rtk` for every shell command and `apply_patch` for file edits.
- Before the first build/test command in every implementation session:

  ```bash
  rtk --version
  rtk dotnet --info
  rtk git status --short
  ```

- Preserve unrelated worktree changes. Do not stage or rewrite files outside the
  active Slice.
- Never delete files directly. Move retired files to
  `99_RecycleBin/Phase9bDurablePersistence/` and update all references.
- Start every behavior with a named failing test from Spec §15. Confirm that the
  failure is caused by missing behavior, not a broken fixture.
- Make the smallest production change that turns the focused case green. Then
  run the owning project, Slice regression projects, dependency boundaries, and
  `rtk git diff --check`.
- Do not add reflection JSON, assembly scanning,
  `DefaultJsonTypeInfoResolver`, Npgsql dynamic JSON POCO mapping, open durable
  `object?`, or a generic Repository/UoW fallback.
- Do not expose Npgsql, ADO.NET connections/transactions, SQLSTATE, provider
  enums, or provider exceptions from Runtime/Metadata/Workflow/HumanTask/
  Accountability abstractions.
- All SQL values are parameters. No caller data is concatenated into SQL.
  Identifiers are fixed constants except the validated/quoted configured schema.
- Do not automatically replay a transaction delegate after an ambiguous COMMIT.
- Do not scan all persisted Descriptor Pins at Host startup.
- Do not implement Outbox append/delivery, Agent Memory durability, or Agent
  Tool pre-dispatch reconciliation.
- Do not make post-commit Accountability or local-event failure reinterpret a
  committed state transition.
- Keep InMemory and PostgreSQL behind the same Runner →
  `IRuntimeTransactionCoordinator` → Store path. There is no InMemory shortcut
  in Workflow/HumanTask business code.
- Do not mark `memory.md` Implemented or NativeAOT-verified until the final
  original native binary executes successfully against PostgreSQL.

---

## 2. Ordered Delivery Map

| Slice | Deliverable | Depends on | Must not include |
|---|---|---|---|
| 1 | Acceptance scaffold, project graph, Runtime Persistence contract kernel | Approved Spec | Store implementations, SQL |
| 2 | Runtime State and Descriptor Pin cutover | Slice 1 | Workflow suspension persistence |
| 3 | Full Semantic InMemory provider and atomic suspension mainline | Slice 2 | Npgsql, migrations |
| 4 | Direct-Npgsql Provider Kernel and migration system | Slice 3 | Workflow/HumanTask PostgreSQL mapping |
| 5 | PostgreSQL Snapshot/Workflow/HumanTask/Receipt Stores and lazy recovery | Slice 4 | durable Audit sink, NativeAOT claim |
| 6 | Durable Audit sink and #25 enlistment probe | Slice 5 | Outbox product API/delivery |
| 7 | Independent crash/response-loss and NativeAOT evidence | Slice 6 | new product scope |
| 8 | Repository regression, docs/evidence, final review gates | Slice 7 | feature expansion |

Each Slice is independently buildable and reviewable. If published as stacked
PRs, each Slice targets its predecessor. If delivered on one branch, preserve
the same commit/review boundaries.

---

## 3. Final Dependency Graph

### 3.1 Production graph

```text
# A -> B means project A references project B.

Runtime.Persistence.Abstractions
    -> Core.Abstractions
    -> Metadata.Abstractions

Workflow.Abstractions
    -> Runtime.Persistence.Abstractions
HumanTask.Abstractions
    -> Runtime.Persistence.Abstractions

Workflow
    -> Workflow.Abstractions
    -> Runtime.Persistence.Abstractions
HumanTask
    -> HumanTask.Abstractions
    -> Runtime.Persistence.Abstractions

Runtime.Persistence
    -> Runtime.Persistence.Abstractions
    -> Schema.Abstractions
    # provider-neutral Runtime State registry/composition only

Runtime.Persistence.InMemory
    -> Runtime.Persistence.Abstractions
    -> Workflow.Abstractions
    -> HumanTask.Abstractions
    -> Metadata.Abstractions
    X  Runtime.Persistence
    # Full Semantic Provider; no process durability claim

Runtime.Persistence.PostgreSql
    -> Runtime.Persistence.Abstractions
    -> Workflow.Abstractions
    -> HumanTask.Abstractions
    -> Metadata.Abstractions
    -> Accountability.Abstractions
    X  Runtime.Persistence
    # Full Durable Provider; direct Npgsql

Runtime.Persistence.Testing
    -> Runtime.Persistence.Abstractions
    -> Workflow.Abstractions
    -> HumanTask.Abstractions
    -> Metadata.Abstractions
    X  Runtime.Persistence
    X  every provider
```

Binding rules:

- `Runtime.Persistence.Abstractions` references Core/Metadata abstractions only.
  It never references Workflow, HumanTask, Accountability, or any provider.
- Workflow/HumanTask Abstractions may reference
  `Runtime.Persistence.Abstractions`.
- `Runtime.Persistence` references only
  `Runtime.Persistence.Abstractions`, `Schema.Abstractions`, and DI/hosting
  abstractions. `Schema.Abstractions` is used only by Runtime State startup
  validation; it is not a provider dependency.
- `Runtime.Persistence.Testing` directly references
  `CrestCreates.Runtime.Persistence.Abstractions`,
  `CrestCreates.Workflow.Abstractions`,
  `CrestCreates.HumanTask.Abstractions`, and
  `CrestCreates.Metadata.Abstractions`. It must not reference
  `CrestCreates.Runtime.Persistence` or a provider.
- InMemory has those same four direct Abstractions references. It must not
  reference `CrestCreates.Runtime.Persistence`.
- PostgreSQL has those same four direct Abstractions references plus
  `CrestCreates.Accountability.Abstractions`. It must not reference
  `CrestCreates.Runtime.Persistence`.
- Workflow and HumanTask concrete runtimes reference persistence abstractions,
  not providers.
- Host composition registers provider-neutral Runtime State composition and
  the selected provider separately:

  ```csharp
  services.AddRuntimePersistence();
  services.AddCrestCreatesPostgreSqlRuntimePersistence(options);
  ```

- Providers implement Stores, the coordinator, and provider capabilities.
  They do not discover or build Runtime State contributors.
- Composition roots select exactly one Runtime provider.

This avoids:

```text
Workflow.Abstractions
    -> Runtime.Persistence.Abstractions
    -> Workflow.Abstractions
```

and avoids turning Persistence Abstractions into an unbounded Runtime Common
assembly.

### 3.2 Project placement

```text
src/Runtime/Persistence/
  CrestCreates.Runtime.Persistence.Abstractions/
  CrestCreates.Runtime.Persistence/
  CrestCreates.Runtime.Persistence.InMemory/

src/Persistence/
  CrestCreates.Runtime.Persistence.PostgreSql/
```

The direct-Npgsql project remains under `src/Persistence`; it is infrastructure
implementing Runtime contracts. Existing dependency policy is refined to allow
Runtime **Abstractions** while continuing to reject Workflow/HumanTask concrete
implementations.

### 3.3 Boundary-test correction

The existing
`PersistenceProjects_DoNotReferenceRuntimeWorkflowAgentOrHumanTask` uses broad
path fragments such as:

```text
src/Runtime/Workflow/CrestCreates.Workflow
```

which also match `CrestCreates.Workflow.Abstractions`. Slice 1 changes the guard
to reject exact concrete project paths/names and adds dedicated positive and
negative tests:

```text
PersistenceProjects_MayReferenceRuntimeAbstractionsButNotConcreteRuntimes
RuntimePersistenceAbstractions_DoNotReferenceWorkflowHumanTaskOrProviders
WorkflowAndHumanTaskAbstractions_ShouldNotReferencePersistenceImplementationsOrProviders
PostgreSqlRuntimeProvider_DoesNotReferenceConcreteRuntimeImplementations
RuntimePersistenceTesting_DoesNotReferenceRuntimePersistenceConcrete
RuntimePersistenceProviders_DoNotReferenceRuntimePersistenceConcrete
WorkflowRuntime_ShouldNotReferenceRuntimePersistenceConcrete
HumanTaskRuntime_ShouldNotReferenceRuntimePersistenceConcrete
RuntimePublicContracts_DoNotExposeProviderTypes
```

---

## 4. Project and Solution Map

### 4.1 New production projects

```text
src/Runtime/Persistence/CrestCreates.Runtime.Persistence.Abstractions/
  CrestCreates.Runtime.Persistence.Abstractions.csproj

src/Runtime/Persistence/CrestCreates.Runtime.Persistence/
  CrestCreates.Runtime.Persistence.csproj

src/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory/
  CrestCreates.Runtime.Persistence.InMemory.csproj

src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  CrestCreates.Runtime.Persistence.PostgreSql.csproj
```

### 4.2 New test-support and test projects

```text
tests/Shared/CrestCreates.Runtime.Persistence.Testing/
  CrestCreates.Runtime.Persistence.Testing.csproj

tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.Tests/
  CrestCreates.Runtime.Persistence.Tests.csproj

tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests/
  CrestCreates.Runtime.Persistence.InMemory.Tests.csproj

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/
  CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker.csproj

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/
  CrestCreates.Runtime.Persistence.PostgreSql.AotHost.csproj

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests/
  CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests.csproj
```

### 4.3 Package changes

Modify `Directory.Packages.props`:

```xml
<PackageVersion Include="Npgsql" Version="10.0.3" />
```

This patch version was rechecked against NuGet on 2026-07-31. Slice 4 records
the resolved package version in its evidence; it does not silently downgrade or
inherit the EF provider's transitive Npgsql version.

The PostgreSQL provider references `Npgsql` directly. It does not depend on
`Npgsql.EntityFrameworkCore.PostgreSQL`.

The PostgreSQL test project references:

```text
Microsoft.NET.Test.Sdk
xUnit
FluentAssertions
Testcontainers.PostgreSql
Npgsql
```

The AOT Host references the production provider and required Runtime projects;
the AOT Fixture test owns Testcontainers and launches the published native Host
with a connection string.

Project-reference/package ledger:

| Project | Project references | Packages/tooling |
|---|---|---|
| Runtime Persistence Abstractions | Core.Abstractions; Metadata.Abstractions | none |
| Runtime Persistence | Runtime Persistence Abstractions; Schema.Abstractions | DI.Abstractions; Options |
| InMemory provider | `CrestCreates.Runtime.Persistence.Abstractions`; `CrestCreates.Workflow.Abstractions`; `CrestCreates.HumanTask.Abstractions`; `CrestCreates.Metadata.Abstractions` | DI.Abstractions |
| PostgreSQL provider | `CrestCreates.Runtime.Persistence.Abstractions`; `CrestCreates.Workflow.Abstractions`; `CrestCreates.HumanTask.Abstractions`; `CrestCreates.Metadata.Abstractions`; `CrestCreates.Accountability.Abstractions` | Npgsql; Hosting/DI/Options/Logging abstractions |
| Runtime Persistence.Tests | Runtime Persistence + Abstractions | Test SDK; xUnit; FluentAssertions; JSON Contract BuildTasks |
| Runtime Persistence.Testing | `CrestCreates.Runtime.Persistence.Abstractions`; `CrestCreates.Workflow.Abstractions`; `CrestCreates.HumanTask.Abstractions`; `CrestCreates.Metadata.Abstractions`; explicitly no `CrestCreates.Runtime.Persistence` | none; `IsTestProject=false` |
| InMemory provider tests | Runtime Persistence concrete host composition; InMemory provider; shared Persistence Testing | Test SDK; xUnit; FluentAssertions |
| PostgreSQL provider tests | Runtime Persistence concrete host composition; PostgreSQL provider; both shared contract kits | Test SDK; xUnit; FluentAssertions; Testcontainers.PostgreSql |
| CrashWorker | PostgreSQL provider and public Runtime abstractions | executable; no test SDK |
| AotHost | PostgreSQL provider, Runtime Persistence, Workflow, HumanTask, Metadata, Accountability | executable; JSON Contract BuildTasks |
| AotFixture.Tests | AotHost project path only for publish orchestration, not runtime execution reference | Test SDK; xUnit; FluentAssertions; Testcontainers.PostgreSql |

`Runtime.Persistence.Tests` and `AotHost` import the existing JSON Contract Root
BuildTasks props/targets exactly as Accountability does. No test project adds
handwritten `[JsonSerializable]`.

### 4.4 Solution changes

Modify:

- `CrestCreates.slnx`
- `solutions/CrestCreates.All.slnx`
- `solutions/CrestCreates.Runtime.slnx`

The Runtime solution includes the contract/runtime/InMemory projects, the
PostgreSQL provider, shared test kit, focused provider tests, crash worker, AOT
Host, and AOT fixture. Do not add these projects to unrelated sample solution
files.

Projects enter all three owning solution files only in their owning Slice:

| Slice | Projects added to solutions |
|---|---|
| 1 | Runtime Persistence Abstractions; Runtime Persistence; Runtime Persistence.Tests; Runtime Persistence.Testing |
| 3 | Runtime Persistence.InMemory; Runtime Persistence.InMemory.Tests |
| 4 | Runtime Persistence.PostgreSql; Runtime Persistence.PostgreSql.Tests |
| 7 | PostgreSql.CrashWorker; PostgreSql.AotHost; PostgreSql.AotFixture.Tests |

No earlier Slice adds a placeholder project that stays red until a later
provider/fixture Slice.

### 4.5 Verified current-mainline cutover inventory

The following is repository fact, not a discovery task deferred to
implementation:

| Current owner | Current fact | Planned cutover |
|---|---|---|
| `Workflow.Abstractions/IWorkflowInstanceStore.cs` | `SaveAsync` upsert and bare string lookups | Slice 2: Add/CAS and `RuntimeInstanceKey` |
| `HumanTask.Abstractions/IHumanTaskInstanceStore.cs` | `SaveAsync`, bare string lookups, tenantless list queries | Slice 2: Add/CAS plus key/scope on every query |
| `WorkflowInstance.cs`, `HumanTaskInstance.cs` | `ConcurrencyStamp` and shallow `object?` snapshots | Slice 2: `Revision`, Pins, immutable state envelopes |
| `WorkflowExecutionRunner`, `WorkflowContinuationService` | failed/completed/continuation paths mutate the loaded instance before Store success | Slices 2–3: detached working copy and transition builders for every Workflow write |
| `DefaultHumanTaskRuntime` | completion/cancel/dispatch-failure paths mutate the loaded instance before Store success | Slices 2–3: detached HumanTask transition builder for every CAS write |
| `WorkflowStepResult.cs`, requests/events | durable-capable `object?` values and bare completion IDs | Slice 2: captured state and exact keys |
| `HumanTaskStepExecutor.cs` | calls `IHumanTaskRuntime.CreateAsync`, which persists before Workflow suspension | Slice 3: prepare-only intent and runner-owned transaction |
| Workflow/HumanTask service extensions | silently register separate legacy InMemory Stores | Slice 2: remove defaults; temporary adapters are explicitly wired only to close compilation; Slice 3 selects one full provider |
| Metadata runtime Store exceptions | Store failures are owned by `Metadata.Abstractions`, including a generic not-found type | Slice 1: move only Runtime persistence failures to Runtime Persistence; sample/business not-found remains sample-owned |
| `DescriptorSnapshot.cs` | evidence-only DTO; there is no durable Snapshot Store contract | Slice 1: Metadata-owned Store/result/hash contracts; Slice 2: canonical persistence hasher |
| `CrestCreates.Samples.DescriptorControlPlane` | wires SQLite Workflow/HumanTask Stores into the Runtime mainline | Slice 2: retire those two legacy Runtime Stores and explicitly wire temporary compile adapters; Slice 3 replaces them with Full Semantic InMemory; SQLite remains only for sample business/control-plane data |
| Agent Control Plane activation handler | looks up a task by bare ID and casts `instance.Input`/event `Result` | Slice 2: exact event keys and explicit Runtime State restore |
| Procurement sample | builds open input/result values and manually registers old InMemory Stores | Slices 2–3: application state contributor plus selected provider composition |
| Phase 9a shared sink kit | runner-free contract-case pattern already exists | Slice 1: mirror this pattern; do not invent a test runner abstraction |
| JSON Contract Root BuildTasks | generated `JsonTypeInfo` and `AllDirectRootTypes` manifest already exist | Slice 2: consume them; do not introduce handwritten or reflection fallback roots |

Before modifying public Store or event contracts, refresh this inventory with:

```bash
rtk rg -n "IWorkflowInstanceStore|IHumanTaskInstanceStore" src tests samples \
  --glob '*.cs'
rtk rg -n "InputVariables|Dictionary<string, object\\?>|object\\? (Input|Output|Result)" \
  src/Runtime tests/Runtime samples --glob '*.cs'
rtk rg -n "RuntimeStoreException|RuntimeConcurrencyException|RuntimeEntityNotFoundException" \
  src tests samples --glob '*.cs'
```

Any newly discovered production implementation or composition root is added to
the current Slice file ledger before code is changed.

### 4.6 Slice buildability and RED discipline

Every Slice ends with all projects affected by that Slice's public API changes
compiling. “A later Slice will fix the callers” is not an acceptable
intermediate state.

RED is staged so tests remain runnable:

1. repository/file-system architecture tests fail by assertion while a project
   is absent;
2. create the empty project and add its project reference;
3. add behavior tests against compiling contract shells;
4. implement the minimum behavior to turn them green.

Provider-specific existence and behavior tests are introduced only in the
Slice that creates that provider. For example,
`PostgreSqlProvider_Should_ReferenceRuntimeAbstractionsOnly` belongs to Slice 4
and `WorkflowRuntimeProviders_Should_DeclareExplicitSupportTier` first becomes
executable in Slice 3. Slice 1 must not stay red waiting for a later provider.

`RuntimeAbstractions_Should_Not_ExposeProviderTypes` inspects the public
constructors, base types, interfaces, methods, parameters, return types,
properties, fields, and generic arguments of Runtime Persistence, Workflow,
HumanTask, Metadata Snapshot, and Accountability abstractions. It rejects
assemblies/namespaces from Npgsql, EF Core, and `System.Data.Common` connection,
command, or transaction types.

Compiled-assembly and Roslyn semantic tests are the primary guards:

- public API inspection resolves actual assembly/type identities, including
  generic arguments and inherited surfaces;
- a Roslyn semantic test resolves each `JsonSerializer.Serialize`/
  `Deserialize` invocation symbol and rejects reflection/type-based overloads,
  including aliases and fully qualified calls;
- another semantic test rejects binding to `DefaultJsonTypeInfoResolver` and
  Npgsql dynamic JSON opt-ins;
- project-reference inspection rejects provider packages in abstractions.

The Boundary test project reuses its existing
`Microsoft.CodeAnalysis.CSharp` reference; no production project gains a Roslyn
dependency.

Source-text/`rg` checks remain a quick auxiliary audit only; they are never the
sole evidence for provider leakage or reflection fallback.

---

## 5. Slice 1 — Acceptance Scaffold and Contract Kernel

### 5.1 RED-A — freeze project and dependency boundaries

Create:

```text
tests/Boundary/CrestCreates.DependencyBoundaries.Tests/
  RuntimePersistenceArchitectureTests.cs
```

Modify:

```text
tests/Boundary/CrestCreates.DependencyBoundaries.Tests/
  DependencyBoundaryTests.cs
```

Add failing tests:

```text
RuntimeAbstractions_Should_Not_ExposeProviderTypes
RuntimeProjects_Should_Not_ReferencePostgreSqlProvider
RuntimePersistenceAbstractions_DoNotReferenceWorkflowHumanTaskOrProviders
PersistenceProjects_MayReferenceRuntimeAbstractionsButNotConcreteRuntimes
RuntimePersistenceTesting_ShouldBeRunnerFree
RuntimePersistenceTesting_DoesNotReferenceRuntimePersistenceConcrete
WorkflowRuntime_ShouldNotReferenceRuntimePersistenceConcrete
HumanTaskRuntime_ShouldNotReferenceRuntimePersistenceConcrete
```

These tests inspect repository paths/XML/source text and compile without a
reference to the not-yet-created projects. They fail by assertion because the
Runtime Persistence Abstractions and shared Testing project roots do not exist.
The PostgreSQL reference-direction test is deliberately deferred to Slice 4;
the Store API and provider-tier behavior tests are deliberately deferred to
Slices 2 and 3.

Run RED:

```bash
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~RuntimePersistence"
```

Record the expected failing test names. A compile error is not accepted as the
RED evidence for this stage.

### 5.2 GREEN-A — create contract shells and runner-free test kit

Create project files:

```text
src/Runtime/Persistence/CrestCreates.Runtime.Persistence.Abstractions/
  CrestCreates.Runtime.Persistence.Abstractions.csproj

src/Runtime/Persistence/CrestCreates.Runtime.Persistence/
  CrestCreates.Runtime.Persistence.csproj

tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.Tests/
  CrestCreates.Runtime.Persistence.Tests.csproj

tests/Shared/CrestCreates.Runtime.Persistence.Testing/
  CrestCreates.Runtime.Persistence.Testing.csproj
  Assertions/RuntimePersistenceContractAssertionException.cs
  Assertions/RuntimePersistenceContractAssertions.cs
  Fixtures/RuntimePersistenceContractFixture.cs
  TestingBoundaryMarker.cs
```

Exact project references:

```text
Runtime.Persistence.Abstractions
    -> Core.Abstractions
    -> Metadata.Abstractions

Runtime.Persistence
    -> Runtime.Persistence.Abstractions
    -> Schema.Abstractions
    -> Microsoft.Extensions.DependencyInjection.Abstractions
    -> Microsoft.Extensions.Options

Runtime.Persistence.Testing
    -> Runtime.Persistence.Abstractions
    -> Metadata.Abstractions
    -> Workflow.Abstractions
    -> HumanTask.Abstractions
    -> no provider and no concrete Runtime
```

The shared Testing project is runner-free:

- `IsTestProject=false`;
- no xUnit/test SDK;
- references only public abstraction projects;
- the assertion helper, fixture ownership rules, and marker are real compiling
  infrastructure, not empty future case shells;
- it does not create Store/State/Snapshot/transaction drivers until Slice 2
  has landed the final contracts those cases exercise.

Add these four Slice 1 projects to `CrestCreates.slnx`,
`solutions/CrestCreates.All.slnx`, and `solutions/CrestCreates.Runtime.slnx`
before the next RED stage.

### 5.3 RED-B — key, state-envelope, transaction, and tier contracts

Create:

```text
tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.Tests/
  Keys/RuntimeInstanceKeyTests.cs
  Keys/RuntimeTenantScopeTests.cs
  State/RuntimeStateValueTests.cs
  Transactions/RuntimeTransactionContractShapeTests.cs
  Providers/RuntimePersistenceProviderTierTests.cs
```

Add runnable RED cases:

```text
RuntimeInstanceKey_Should_RequireExplicitTenantScope
RuntimeInstanceKey_Should_RejectBlankInstanceId
RuntimeInstanceKey_DefaultValue_ShouldBeRejectedAtStoreBoundary
RuntimeTenantScope_Null_ShouldMeanExactHostNotWildcard
RuntimeStateValue_ShouldDistinguishAbsentFromTypedNull
RuntimeTransactionCoordinator_ShouldExposeRequiredPropagationOnly
RuntimeProviderTierContract_ShouldDistinguishFullSemanticAndFullDurable
```

Run:

```bash
rtk dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.Tests \
  --filter "FullyQualifiedName~RuntimeInstanceKey|FullyQualifiedName~RuntimeTenantScope|FullyQualifiedName~RuntimeStateValue|FullyQualifiedName~RuntimeTransaction|FullyQualifiedName~RuntimeProviderTier"
```

### 5.4 GREEN-B — create the minimal public contract kernel

Create under `CrestCreates.Runtime.Persistence.Abstractions`:

```text
Keys/RuntimeInstanceKey.cs
Keys/RuntimeTenantScope.cs

State/RuntimeStateValue.cs
State/RuntimeStateBag.cs
State/IRuntimeStateContractRegistry.cs
State/IRuntimeStateContractContributor.cs
State/IRuntimeStateContractBuilder.cs
State/RuntimeStateLimits.cs

Transactions/IRuntimeTransactionCoordinator.cs
Transactions/RuntimeTransactionCommitUnknownException.cs

Providers/RuntimePersistenceProviderTier.cs
Providers/IRuntimePersistenceProviderCapabilities.cs

Errors/RuntimePersistenceException.cs
Errors/RuntimeConcurrencyException.cs
Errors/RuntimeDuplicateEntityCode.cs
Errors/RuntimeDuplicateEntityException.cs
Errors/RuntimePersistenceUnavailableException.cs
Errors/RuntimePersistenceContractErrorCode.cs
Errors/RuntimePersistenceContractException.cs
Errors/RuntimeStateContractException.cs
```

Create under `CrestCreates.Metadata.Abstractions`:

```text
Persistence/IDescriptorSnapshotStore.cs
Persistence/DescriptorSnapshotWriteResult.cs
Persistence/DescriptorSnapshotWriteStatus.cs
Persistence/IDescriptorSnapshotPersistenceHasher.cs
```

The Snapshot Store contract returns only `DescriptorSnapshot`/`SnapshotEntry`
evidence. The hash abstraction returns a structured persistence hash and has no
Descriptor-resolution method.

Contract rules in code/XML docs:

- `RuntimeInstanceKey.InstanceId` nonblank;
- the unavoidable `default(RuntimeInstanceKey)` value is invalid and every
  Store entry validates it before observing provider state;
- `RuntimeTenantScope(null)` is exact host;
- `RuntimeStateValue? null` means no value;
- typed null is a non-null envelope with payload `null`;
- untyped null capture fails;
- transaction propagation is Required only;
- public failures are provider-neutral.
- `RuntimeDuplicateEntityException.Code` distinguishes
  `DuplicateInstance`; it never carries a database constraint name.
- `RuntimePersistenceContractException.Code` includes
  `ActiveStepCorrelationConflict` and `WaitingTaskCorrelationConflict`;
  provider detail, SQLSTATE, and constraint names remain internal.
- Full Semantic means Add/CAS + atomic multi-Store transaction + rollback;
  Full Durable adds restart/migration/crash/database-AOT evidence.

The only contributor-facing builder contract in Abstractions is:

```csharp
public interface IRuntimeStateContractBuilder
{
    void Add<T>(
        string typeId,
        JsonTypeInfo<T> jsonTypeInfo,
        IReadOnlySet<Type> allDirectRootTypes,
        DescriptorRef? schemaRef = null);
}
```

There is no public registration record in Abstractions. The typed registration
object and mutable builder implementation belong to the concrete Runtime
Persistence project in Slice 2.

Do not implement `RuntimeStateContractRegistry` in Slice 1. The concrete
registry, builder, startup validator, factory, and DI registration are Slice 2
behavior driven by generated-JSON tests. `CrestCreates.Runtime.Persistence`
contains only its assembly marker in this Slice.

### 5.5 Retire old exception ownership without creating a generic repository layer

Current files:

```text
src/Metadata/CrestCreates.Metadata.Abstractions/
  RuntimeStoreException.cs
  RuntimeConcurrencyException.cs
  RuntimeEntityNotFoundException.cs
```

Move them to:

```text
99_RecycleBin/Phase9bDurablePersistence/MetadataRuntimeStoreExceptions/
```

In the same commit:

- add a Runtime Persistence Abstractions project reference to Workflow and
  HumanTask concrete projects;
- update the four production consumers in
  `InMemoryWorkflowInstanceStore.cs`, `WorkflowContinuationService.cs`,
  `InMemoryHumanTaskInstanceStore.cs`, and `DefaultHumanTaskRuntime.cs`;
- update Workflow/HumanTask tests that name Runtime persistence exceptions;
- map Workflow/HumanTask `GetAsync` absence to the owning domain operation's
  existing invalid-operation/domain failure;
- map Update of a missing row or revision mismatch to
  `RuntimeConcurrencyException`;
- map Add of an existing instance key to
  `RuntimeDuplicateEntityException`;
- replace the sample-owned Company Certification Stores' use of the Metadata
  not-found type with a sample-owned `CompanyCertificationNotFoundException`
  (or `KeyNotFoundException`) and do not add a Runtime Persistence reference to
  that business Store;
- update the two legacy SQLite Runtime Stores only to consume the new Runtime
  Persistence exception authority while retaining the old Store interfaces
  temporarily. Slice 1 does not anticipate or implement the Slice 2 Add/CAS
  contract.

In Slice 2, migrate those SQLite Runtime Stores to the final Add/CAS contracts
only if required to keep the atomic public-contract cutover compiling, then
retire them within that same Slice as specified in §6.6.

Do not create a Metadata → Runtime.Persistence type forward because it would
invert dependencies. Do not recreate `RuntimeEntityNotFoundException` in
Runtime Persistence: its Store contract has no valid operation that throws it,
and Runtime Persistence is not a general repository-exception library. Do not
leave old and new Runtime concurrency/duplicate exception authorities compiled
at the end of the Slice.

### 5.6 Review gate

Run:

```bash
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~RuntimePersistence"
rtk dotnet build src/Runtime/Persistence/CrestCreates.Runtime.Persistence.Abstractions
rtk dotnet build src/Runtime/Persistence/CrestCreates.Runtime.Persistence
rtk dotnet build tests/Shared/CrestCreates.Runtime.Persistence.Testing
rtk dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.Tests
rtk dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests \
  --filter "FullyQualifiedName~InMemoryWorkflowInstanceStore|FullyQualifiedName~WorkflowContinuation"
rtk dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests \
  --filter "FullyQualifiedName~InMemoryHumanTaskInstanceStore|FullyQualifiedName~HumanTaskRuntime"
rtk dotnet build samples/CrestCreates.Samples.DescriptorControlPlane
rtk git diff --check
```

Review:

- no Workflow/HumanTask reference from persistence abstractions;
- no Npgsql reference outside the future provider;
- no xUnit in shared test kit;
- no implementation hidden in abstractions;
- no old/new Runtime persistence exception dual truth;
- no Company Certification dependency on Runtime Persistence;
- no test introduced in Slice 1 remains red waiting for InMemory/PostgreSQL.

---

## 6. Slice 2 — Runtime State and Descriptor Pin Cutover

### 6.1 RED — Runtime State contracts

Create:

```text
tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.Tests/
  State/RuntimeStateContractRegistryTests.cs
  State/RuntimeStateSnapshotTests.cs
  Json/TestRuntimeStateJsonSerializerContext.cs
  Json/TestRuntimeStateContractContributor.cs
  Fixtures/MutableNestedRuntimeState.cs
```

The test JSON context uses existing BuildTasks:

```csharp
[JsonContractExplicitRoot(typeof(MutableNestedRuntimeState))]
public sealed partial class TestRuntimeStateJsonSerializerContext
    : JsonSerializerContext;
```

Add RED cases:

```text
RuntimeStateContractRegistry_Should_RejectDuplicateTypeId
RuntimeStateContractRegistry_Should_RequireGeneratedRootManifest
RuntimeStateContractStartup_ShouldRejectMissingSchemaRef
RuntimeStateContractStartup_ShouldRejectNonExactSchemaRef
RuntimeStateContractStartup_ShouldRejectWrongDescriptorKind
RegisteredStatePayload_ShouldRoundTripWithExactClrType
RegisteredStatePayload_ShouldPreserveStableTypeIdAcrossClrRename
UnregisteredStatePayload_ShouldFailBeforeTransaction
UntypedNullStatePayload_ShouldFailBeforeTransaction
TypedNullStatePayload_ShouldRoundTripWithTypeId
Snapshot_Should_DeepCopyRegisteredStatePayload
NestedPayloadMutation_ShouldNotAffectLaterRead
OversizedStatePayload_ShouldFailBeforeSql
RuntimeStateMainline_Should_Not_UseReflectionFallback
RuntimeStateMainline_ShouldNotBindReflectionSerializationOverloads
RuntimeStateMainline_ShouldNotReferenceDefaultJsonTypeInfoResolver
StoreContracts_Should_Not_ExposeUpsertSaveAsync
WorkflowStoreQueries_ShouldRequireRuntimeInstanceKey
HumanTaskListQueries_ShouldRequireRuntimeTenantScope
```

Run:

```bash
rtk dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.Tests \
  --filter "FullyQualifiedName~RuntimeState"
```

### 6.2 GREEN — generated-only state capture

Create:

```text
src/Runtime/Persistence/CrestCreates.Runtime.Persistence/
  State/RuntimeStateContractRegistry.cs
  State/RuntimeStateContractBuilder.cs
  State/RuntimeStateContractStartupValidator.cs
  State/TypedRuntimeStateRegistration.cs
  State/BuiltInRuntimeStateContractContributor.cs
  Json/BuiltInRuntimeStateJsonSerializerContext.cs
  RuntimePersistenceServiceCollectionExtensions.cs
```

`RuntimeStateContractBuilder` is the only implementation of
`IRuntimeStateContractBuilder`. `TypedRuntimeStateRegistration<T>` is internal
and is the only registration representation. There is no second
`RuntimeStateContractRegistration` type in either project.

Implement typed closed generic registrations holding:

```text
TypeId
CLR Type identity
optional exact SchemaRef
JsonTypeInfo<T>
```

The contributor-facing builder call matches the Slice 1 interface exactly:

```csharp
builder.Add<T>(
    string typeId,
    JsonTypeInfo<T> jsonTypeInfo,
    IReadOnlySet<Type> allDirectRootTypes,
    DescriptorRef? schemaRef = null);
```

The contributor passes the generated context property's exact
`JsonTypeInfo<T>` and that context's generated
`...RootManifest.AllDirectRootTypes`. The builder rejects the registration when
`typeof(T)` is absent from that set. Runtime code never reflects over a
`JsonSerializerContext` to discover properties or manifests.

Implementation rules:

- generic typed capture/restore invokes STJ overloads accepting
  `JsonTypeInfo<T>`;
- non-null untyped capture performs only exact dictionary lookup by
  `value.GetType()` and dispatches to a prebuilt typed registration;
- no `JsonSerializer.Serialize(object, Type, ...)`;
- no `DefaultJsonTypeInfoResolver`;
- no assembly scan;
- contributor order and duplicate diagnostics are ordinal/deterministic;
- payload character/byte limits are checked before returning a state value;
- restore validates TypeId and optional SchemaRef before deserialization;
- each restore creates a new object graph.

Schema validation has one concrete owner and one existing dependency:

```text
CrestCreates.Runtime.Persistence
    -> CrestCreates.Schema.Abstractions
    -> ISchemaRegistry
```

At startup, every non-null `SchemaRef` must:

1. have namespace `schema`;
2. carry a non-null positive exact version;
3. resolve through `ISchemaRegistry.GetByVersion(ref.Id, ref.Version.Value)`;
4. return a descriptor whose namespace, ID, version, and
   `DescriptorKind.Schema` exactly match the Ref.

Missing references, versionless/non-exact references, wrong namespaces/kinds,
and a registry result with the wrong descriptor identity fail startup before
provider initialization. Restore repeats exact `SchemaRef` equality against
the registered contract and fails closed before deserialization; it never
selects active/latest Schema versions.

The startup cases use:

```text
missing       = DescriptorRef("schema", missingId, exactVersion)
non-exact     = DescriptorRef("schema", existingId, version: null)
wrong kind    = DescriptorRef("workflow", existingId, exactVersion)
```

The wrong-kind case is rejected by the namespace/kind policy before querying
`ISchemaRegistry`; the missing case proves the exact version lookup itself.

The framework-owned built-in contributor is explicit and generated. Its stable
root ledger is fixed in this Plan:

| TypeId | CLR contract |
|---|---|
| `crest.runtime/string/v1` | `string` |
| `crest.runtime/boolean/v1` | `bool` |
| `crest.runtime/int32/v1` | `int` |
| `crest.runtime/int64/v1` | `long` |
| `crest.runtime/decimal/v1` | `decimal` |
| `crest.runtime/guid/v1` | `Guid` |
| `crest.runtime/date-time-offset/v1` | `DateTimeOffset` |
| `crest.runtime/state-bag/v1` | immutable ordinal `RuntimeStateBag` of `RuntimeStateValue` entries |

It does not register `object`, `JsonElement`, arbitrary dictionaries, or
assembly-qualified types. Application/domain records remain
application-owned explicit roots and contributors.

`HumanTaskStepExecutor` wraps Workflow variables in `RuntimeStateBag` before
preparing a correlated task. The bag owns an immutable ordinal map and is one
registered state value; it never recreates
`Dictionary<string, object?>`. Add
`RuntimeStateBag_ShouldRoundTripOrdinallyWithoutObjectPayload`.

`RuntimeStateBag` is a cross-Runtime immutable persistence value contract owned
by `CrestCreates.Runtime.Persistence.Abstractions`. Workflow/HumanTask may
construct it without referencing concrete Runtime Persistence. Its generated
JSON context, contributor, typed registration, and registry remain in
`CrestCreates.Runtime.Persistence`.

Startup validation compares each registration with the BuildTasks-generated
`AllDirectRootTypes` manifest and fails before provider initialization. Add a
test-only transaction driver counter proving every registration/capture failure
occurs before `IRuntimeTransactionCoordinator.ExecuteAsync`.

Contributor discovery is explicit DI composition:

```text
services.AddRuntimePersistence()
services.AddSingleton<IRuntimeStateContractContributor, BuiltIn...>()
application composition adds each application contributor explicitly
startup builder enumerates only registered contributors
```

There is no assembly scan, `Activator.CreateInstance`, or fallback resolver.
The builder collects candidate registrations, sorts them by TypeId and CLR
contract full name using ordinal comparison, and then validates; duplicate
TypeId/CLR diagnostics are deterministic regardless of DI registration order.

### 6.3 RED — Descriptor Pin contracts

Create tests:

```text
tests/Metadata/Core/CrestCreates.Metadata.Tests/
  RuntimeDescriptorPinTests.cs
  RuntimeDescriptorPinResolverTests.cs
```

Add:

```text
RuntimeDescriptorPin_ShouldRequireExactVersion
RuntimeDescriptorPin_ShouldPreserveStructuredCanonicalHashes
RuntimeDescriptorPinResolver_ShouldReturnValidatedDescriptorObject
RuntimeDescriptorPinResolver_ShouldRejectMissingDescriptor
RuntimeDescriptorPinResolver_ShouldRejectContractHashMismatch
RuntimeDescriptorPinResolver_ShouldRejectDefinitionHashMismatch
RuntimeDescriptorPinResolver_ShouldRejectHashProfileMismatch
DescriptorSnapshot_Should_Not_Be_ExecutableAuthority
DescriptorPinWithoutSnapshotId_ShouldResolveFromRegistry
```

### 6.4 GREEN — Metadata-owned Pin resolution

Create:

```text
src/Metadata/CrestCreates.Metadata.Abstractions/
  Runtime/RuntimeDescriptorPin.cs
  Runtime/ResolvedRuntimeDescriptor.cs
  Runtime/IRuntimeDescriptorPinResolver.cs
  Runtime/RuntimeDescriptorPinValidationException.cs

src/Metadata/CrestCreates.Metadata/
  Runtime/RuntimeDescriptorPinResolver.cs
  Persistence/DescriptorSnapshotPersistenceHasher.cs
  Persistence/DescriptorSnapshotPersistenceCanonicalWriter.cs
```

The resolver accepts:

```text
IVersionedDescriptorRegistry<TDescriptor>
IDescriptorStableHashBuilder
expected namespace/kind policy
```

It resolves exact ID/version, computes structured hashes, compares the complete
records, and returns that exact descriptor object. No Global Registry lookup and
no validate-then-requery.

The Snapshot persistence hasher uses `ICanonicalHashComputer` and an explicit
writer over every persisted Snapshot field, ordered entry, and ordered
relationship. Both providers consume
`IDescriptorSnapshotPersistenceHasher`; neither provider implements its own
JSON-text comparison.

### 6.5 Change durable model fields

Modify:

```text
src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/
  WorkflowInstance.cs
  WorkflowStepResult.cs
  WorkflowExecutionRequest.cs
  WorkflowContinuationRequest.cs
  IWorkflowInstanceStore.cs

src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/
  HumanTaskInstance.cs
  HumanTaskCreationRequest.cs
  HumanTaskCompletionRequest.cs
  HumanTaskCompletedEvent.cs
  IHumanTaskInstanceStore.cs
```

Required cutover:

```text
ConcurrencyStamp -> Revision
string lookup -> RuntimeInstanceKey / RuntimeTenantScope
SaveAsync upsert -> AddAsync + UpdateAsync(expectedRevision)
durable object? -> RuntimeStateValue
bare completion IDs -> exact HumanTask/Workflow keys
Workflow/HumanTask version fields -> owning RuntimeDescriptorPin
```

The final Store surfaces are fixed:

```csharp
public interface IWorkflowInstanceStore
{
    Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken = default);
    Task UpdateAsync(WorkflowInstance instance, long expectedRevision, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetByWaitingHumanTaskAsync(RuntimeInstanceKey humanTaskKey, CancellationToken cancellationToken = default);
}

public interface IHumanTaskInstanceStore
{
    Task AddAsync(HumanTaskInstance instance, CancellationToken cancellationToken = default);
    Task UpdateAsync(HumanTaskInstance instance, long expectedRevision, CancellationToken cancellationToken = default);
    Task<HumanTaskInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(RuntimeInstanceKey workflowKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(RuntimeTenantScope scope, string assigneeUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(RuntimeTenantScope scope, string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(RuntimeTenantScope scope, string roleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(RuntimeTenantScope scope, string organizationUnitId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(RuntimeTenantScope scope, string positionId, CancellationToken cancellationToken = default);
}
```

After these final Store, State, transaction, and Snapshot contracts compile,
create the provider-neutral case kit once:

```text
tests/Shared/CrestCreates.Runtime.Persistence.Testing/
  Workflow/IWorkflowInstanceStoreContractDriver.cs
  Workflow/WorkflowInstanceStoreContractCases.cs
  HumanTask/IHumanTaskInstanceStoreContractDriver.cs
  HumanTask/HumanTaskInstanceStoreContractCases.cs
  Snapshots/IDescriptorSnapshotStoreContractDriver.cs
  Snapshots/DescriptorSnapshotStoreContractCases.cs
  Transactions/IRuntimeTransactionContractDriver.cs
  Transactions/RuntimeTransactionContractCases.cs
  State/IRuntimeStateContractDriver.cs
  State/RuntimeStateContractCases.cs
```

Each case file contains real assertions against the final contracts. Drivers
expose provider-neutral actions and committed observations, never provider
handles, connections, or transactions. Slices 3 and 5 add only provider driver
implementations and xUnit `[Fact]` wrappers; they do not fork or rewrite the
shared cases.

All key/scope and query-string arguments are validated before provider access.
There is no host-or-all-tenants overload and no optional tenant parameter.

Store mutation semantics are fixed:

- Add accepts a detached candidate with `Revision == 0`, persists a detached
  row at revision 1, and does not mutate the candidate.
- Update accepts a detached post-state candidate whose `Revision` still equals
  `expectedRevision`, persists a detached row at `expectedRevision + 1`, and
  does not mutate the candidate.
- the Runtime keeps the caller-visible pre-state unchanged while a transaction
  is open;
- only after the root coordinator returns success does the Runtime publish a
  committed snapshot with the new revision;
- rollback or `RuntimeTransactionCommitUnknownException` leaves the
  caller-visible instance at the known pre-state until reconciliation.

Add shared cases:

```text
StoreWrites_ShouldNotMutateCallerCandidateBeforeCommit
RolledBackSuspension_ShouldLeaveCallerInstanceAtPreRevision
CommitUnknown_ShouldNotAdvanceCallerVisibleRevision
```

The detached rule is Runtime-wide, not suspension-only. The Slice 2 cutover
inventory therefore includes:

```text
Workflow execution working copy
Workflow failed terminal post-state
Workflow completed terminal post-state
Workflow continuation/resume post-state
HumanTask completion post-state
HumanTask cancellation post-state
HumanTask completion-dispatch-failure post-state
```

For every transition:

```text
loaded/caller-visible pre-state
    -> deep detached working/post-state
    -> UpdateAsync(postState, preState.Revision)
    -> commit success
    -> construct/return the committed snapshot at Revision + 1
```

No Runtime mutates the loaded/caller-visible pre-state before the Store
operation succeeds. Rollback, CAS failure, persistence failure, or commit
unknown preserves that pre-state and revision. Workflow step executors receive
the detached working copy, so a step mutation cannot leak back into the
caller's pre-state before the terminal/suspension commit.

Durable-capable request/event surfaces are also fixed:

```text
WorkflowExecutionRequest
    WorkflowId
    TenantId
    Origin
    IReadOnlyDictionary<string, RuntimeStateValue> InputVariables

HumanTaskCreationRequest
    descriptor selection fields
    RuntimeStateValue? Input
    RuntimeInstanceKey? WorkflowKey

HumanTaskCompletionRequest
    RuntimeInstanceKey HumanTaskKey
    RuntimeStateValue? Result

HumanTaskCompletedEvent
    RuntimeInstanceKey HumanTaskKey
    RuntimeInstanceKey? WorkflowKey
    RuntimeDescriptorPin HumanTaskPin
    stable EventId
    outcome
    RuntimeStateValue? Result

WorkflowContinuationRequest
    RuntimeInstanceKey HumanTaskKey
    RuntimeInstanceKey WorkflowKey
    stable completion/trigger identity
    outcome
    RuntimeStateValue? Result
```

Descriptor IDs/versions remain only where they select or describe a
Descriptor; they are not reused as instance keys. Compatibility aliases such as
`HumanTaskId => HumanTaskInstanceId` are removed because they encode the
ambiguity this Phase is closing.

Canonical durable identity fields are not duplicated:

```text
WorkflowInstance
    RuntimeInstanceKey Key
    RuntimeDescriptorPin WorkflowPin
    long Revision
    RuntimeInstanceKey? WaitingHumanTaskKey

HumanTaskInstance
    RuntimeInstanceKey Key
    RuntimeDescriptorPin HumanTaskPin
    long Revision
    RuntimeInstanceKey? WorkflowKey
```

The old `InstanceId`/`TenantId`, `Id`/`TenantId`,
`Workflow`/`HumanTaskId`+version, `WaitingHumanTaskId`, and
`WorkflowInstanceId` property pairs do not remain as separately settable state.
Read-only convenience projections are allowed only when they derive from the
canonical key/pin and cannot diverge.

`WorkflowExecutionRequest` mainline accepts captured
`RuntimeStateValue` entries. Remove or temporarily compile-fence the old
Dictionary overload; it cannot silently serialize open objects.

Update hand-written Snapshot methods to copy collections containing immutable
state envelopes. Remove comments that claim opaque value reference sharing is
acceptable.

The public API cutover is one compile-closed change:

1. change Persistence Abstractions types and the Workflow/HumanTask Abstractions
   project references;
2. change both Store interfaces and all durable models/events;
3. update the two existing built-in InMemory Stores to the new API as
   **temporary compile adapters** so the repository compiles;
4. update every production consumer and test double;
5. remove all old overloads before the Slice ends;
6. remove Store registration from
   `WorkflowServiceCollectionExtensions` and
   `HumanTaskServiceCollectionExtensions`;
7. make every affected test/sample composition root explicitly register the
   two temporary adapters until Slice 3 replaces that registration.

The temporary Stores are replaced, not wrapped, by the Full Semantic provider
in Slice 3. They do not register
`IRuntimePersistenceProviderCapabilities`, are not selected by
`AddWorkflowEngine()`/`AddHumanTaskRuntime()`, and are not advertised as a
supported Runtime provider. Slice 2 is a compile-closed migration state, not a
runnable/supported provider boundary and cannot be released as Phase 9b.

### 6.6 Compile blast-radius closure

The current repository has consumers outside the two Runtime test projects.
Modify the exact affected areas:

```text
src/Runtime/Workflow/CrestCreates.Workflow/
  WorkflowEngine.cs
  WorkflowExecutionRunner.cs
  WorkflowContinuationService.cs
  HumanTaskCompletedWorkflowSubscriber.cs
  WorkflowServiceCollectionExtensions.cs        # remove default Store choice
  InMemoryWorkflowInstanceStore.cs              # temporary, retired Slice 3

src/Runtime/HumanTask/CrestCreates.HumanTask/
  DefaultHumanTaskRuntime.cs
  HumanTaskServiceCollectionExtensions.cs       # remove default Store choice
  InMemoryHumanTaskInstanceStore.cs             # temporary, retired Slice 3

src/Runtime/Agent/CrestCreates.Agent.ControlPlane/
  Activation/DescriptorActivationReviewHumanTaskEventHandler.cs

tests/Runtime/Workflow/CrestCreates.Workflow.Tests/
tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests/
tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/

samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/
samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.Tests/

samples/CrestCreates.Samples.DescriptorControlPlane/
tests/Framework/Testing/CrestCreates.Samples.Tests/
```

Every composition root in this ledger explicitly registers the temporary
Workflow and HumanTask adapters for Slice 2. This registration is intentionally
mechanical and carries no provider capability; it exists only so the contract
cutover is compile/test closed. No default Runtime extension silently installs
those adapters.

Agent Control Plane and Procurement event handlers inject
`IRuntimeStateContractRegistry`, restore the exact registered TypeId, and then
pass a typed value to their existing parsers. They do not cast
`RuntimeStateValue`, parse payload JSON directly, or use `JsonElement`.

Add application-owned generated roots/contributors for:

```text
DescriptorActivationReviewTaskInput
DescriptorActivationReviewDecision/result contract actually emitted by the review task
CertificationSubmitInput
CertificationReviewInput
```

The exact contributor lives with the assembly that owns each CLR contract. The
Host composition registers the contributor; Runtime Persistence does not
reference sample or Agent Control Plane assemblies.

Concrete JSON-root changes:

- add `DescriptorActivationReviewTaskInput` as an explicit root of the existing
  `AgentControlPlaneToolJsonSerializerContext`; reuse its already explicit
  `DescriptorActivationReviewDecision` root; add a contributor in the concrete
  Agent Control Plane project, which receives a direct Runtime Persistence
  Abstractions reference and can access the generated internal manifest through
  the existing `InternalsVisibleTo`;
- Procurement Workflow state uses the framework-generated Guid/string/state-bag
  roots. Its existing HTTP/Capability `ProcurementJsonContext` is not registered
  as Runtime State and is not used to persist payloads;
- add `CompanyCertificationRuntimeStateJsonSerializerContext` and
  `CompanyCertificationRuntimeStateContractContributor` to the sample, rooted
  through BuildTasks for `CertificationSubmitInput` and
  `CertificationReviewInput`;
- do not use the existing handwritten `SampleSqliteJsonContext` as a Runtime
  State registry. It remains only for sample-owned SQLite business data until a
  separate cleanup.

Application TypeIds are fixed:

```text
crest.agent-control-plane/descriptor-activation-review-task-input/v1
crest.agent-control-plane/descriptor-activation-review-decision/v1
sample.company-certification/submit-input/v1
sample.company-certification/review-input/v1
```

Where the owning Descriptor catalog provides an exact versioned Schema, the
registration carries that `SchemaRef`; otherwise it is explicitly null. The
TypeId never embeds the CLR namespace/assembly name.

The DescriptorControlPlane sample does not remain a hidden third Runtime
provider. In this Slice:

- move `SqliteWorkflowInstanceStore.cs`,
  `SqliteHumanTaskInstanceStore.cs`, and `SqliteRuntimeStoreDiagnostics.cs` to
  `99_RecycleBin/Phase9bDurablePersistence/LegacySqliteRuntimeProvider/`;
- remove Runtime Workflow/HumanTask table creation from
  `SqliteDatabaseInitializer`;
- keep SQLite only for `ICompanyCertificationStore` and other sample-owned
  control-plane data;
- move tests whose sole assertion is SQLite Runtime Store persistence to
  `99_RecycleBin/Phase9bDurablePersistence/LegacySqliteRuntimeProviderTests/`;
- keep the sample golden behavior tests, temporarily using the updated built-in
  InMemory Stores until Slice 3 changes composition to the Full Semantic
  provider.

### 6.7 Compile sweep

Use:

```bash
rtk rg -n "ConcurrencyStamp|GetByIdAsync\\(string|GetAsync\\(string|GetByWaitingHumanTaskId|Dictionary<string, object\\?>|object\\? (Input|Output|Result)" \
  src/Runtime tests/Runtime samples
```

Update compile-time call sites without adding compatibility fallback.

Run:

```bash
rtk dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.Tests
rtk dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests \
  --filter "FullyQualifiedName~RuntimeDescriptorPin"
rtk dotnet build solutions/CrestCreates.Runtime.slnx
rtk dotnet build samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host
rtk dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests \
  --filter "FullyQualifiedName~CompanyCertification"
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~RuntimePersistence"
rtk git diff --check
```

### 6.8 Review gate

- all persisted/event state is `RuntimeStateValue`;
- typed null and absent value are distinct;
- exact Pin version and structured hash metadata are mandatory;
- models have one concurrency truth (`Revision`);
- no Store still advertises upsert semantics;
- no Runtime State context/root uses handwritten `[JsonSerializable]`; an
  unrelated existing sample business serializer is not treated as Runtime
  State evidence.
- no SQLite Store remains wired into the Workflow/HumanTask mainline;
- every changed public contract consumer in Runtime, Agent Control Plane, and
  both golden samples compiles;
- Workflow/HumanTask default extensions register no Store;
- temporary built-in Stores are explicit compile adapters with no provider
  capability and are scheduled for removal in the immediately following Slice.

---

## 7. Slice 3 — Full Semantic InMemory Provider and Atomic Suspension

### 7.1 RED — provider support tier and Store contracts

Create:

```text
tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests/
  InMemoryRuntimeContractTestBase.cs
  Drivers/InMemoryWorkflowInstanceStoreContractDriver.cs
  Drivers/InMemoryHumanTaskInstanceStoreContractDriver.cs
  Drivers/InMemoryDescriptorSnapshotStoreContractDriver.cs
  Drivers/InMemoryRuntimeTransactionContractDriver.cs
  Drivers/InMemoryRuntimeStateContractDriver.cs
  Workflow/InMemoryWorkflowInstanceStoreContractTests.cs
  HumanTask/InMemoryHumanTaskInstanceStoreContractTests.cs
  Snapshots/InMemoryDescriptorSnapshotStoreContractTests.cs
  Transactions/InMemoryRuntimeTransactionContractTests.cs
  State/InMemoryRuntimeStateContractTests.cs
  Composition/InMemorySuspensionAtomicityTests.cs
  Architecture/InMemoryProviderDependencyTests.cs
```

Add cases:

```text
WorkflowRuntimeProviders_Should_DeclareExplicitSupportTier
InMemoryRuntimeProvider_ShouldPassAtomicSuspensionContractCases
InMemoryRuntimeProvider_ShouldPassRollbackContractCases
InMemoryRuntimeProvider_ShouldNotClaimProcessDurability
WorkflowAdd_WithRegisteredState_ShouldPersistExactPinAndRevisionOne
Create_ShouldNotOverwriteExistingInstance
Update_ShouldNotInsertMissingInstance
ConcurrentTransition_FromSameRevision_ShouldAllowOneWinner
TenantScopedLookup_ShouldNotReturnOtherTenantInstance
HostAndTenantSameId_ShouldRemainDistinct
WaitingHumanTaskCorrelation_ShouldBeTenantScopedAndUnique
QueryResults_ShouldHaveDeterministicOrder
NestedRuntimeTransaction_ShouldJoinOuterCommit
NestedRuntimeTransaction_ShouldRollbackWithOuterFailure
ConcurrentUseOfAmbientSession_ShouldFailClosed
StoreOperationOutsideAmbient_ShouldUseOneShortTransaction
CancellationBeforeRootCommit_ShouldRollbackAllParticipants
WorkflowComposition_WithoutRuntimeProvider_ShouldFailValidation
InMemoryProvider_ShouldNotReferenceRuntimePersistenceConcrete
```

### 7.2 GREEN — shared in-memory transaction kernel

Create:

```text
src/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory/
  Kernel/InMemoryRuntimePersistenceState.cs
  Kernel/InMemoryRuntimeTransactionContext.cs
  Kernel/InMemoryRuntimeTransactionAccessor.cs
  Transactions/InMemoryRuntimeTransactionCoordinator.cs
  Stores/InMemoryWorkflowInstanceStore.cs
  Stores/InMemoryHumanTaskInstanceStore.cs
  Stores/InMemoryDescriptorSnapshotStore.cs
  Stores/InMemoryWorkflowSuspensionReceiptStore.cs
  Configuration/InMemoryRuntimePersistenceServiceCollectionExtensions.cs
  InMemoryRuntimeProviderCapabilities.cs
```

`InMemoryRuntimeProviderCapabilities` implements the public capability
contract with:

```text
Tier = FullSemantic
SupportsAddAndCompareAndSwap = true
SupportsAtomicMultiStoreTransactions = true
SupportsRollback = true
SupportsProcessDurability = false
SupportsRestartRecovery = false
SupportsMigrations = false
SupportsDatabaseNativeAotEvidence = false
```

Workflow startup validation rejects an absent capability registration or a tier
below Full Semantic before the first execution. It does not infer support from
concrete type names.

The InMemory state, session accessor, coordinator, capabilities, and Stores are
singletons. Registration rejects an already selected Runtime provider rather
than silently winning/losing through `TryAdd`.

`AddCrestCreatesInMemoryRuntimePersistence()` registers only the InMemory
Stores/coordinator/capabilities. It neither calls `AddRuntimePersistence()` nor
enumerates `IRuntimeStateContractContributor`; the Host owns both explicit
calls.

Root transaction algorithm:

1. acquire one provider-owned `SemaphoreSlim`;
2. snapshot the committed dictionaries and provider generation;
3. bind the staged state to an `AsyncLocal` transaction context;
4. nested coordinator calls join the same staged state;
5. Stores read/write only staged state while ambient;
6. success atomically replaces committed state and increments generation;
7. exception/cancellation discards staged state;
8. release session/lock;
9. Store operations outside ambient execute through their own root transaction.

The single-writer local algorithm is acceptable for a test/local provider. CAS
still checks instance Revision, so concurrency semantics match PostgreSQL even
though local commits are serialized.

Detect concurrent fan-out against one ambient context with an interlocked
in-use guard and throw `RuntimePersistenceContractException`.

### 7.3 Retire old provider implementations

Move:

```text
src/Runtime/Workflow/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs
src/Runtime/HumanTask/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs
```

to:

```text
99_RecycleBin/Phase9bDurablePersistence/LegacyInMemoryStores/
```

Verify their DI registrations remain absent from:

```text
src/Runtime/Workflow/CrestCreates.Workflow/
  WorkflowServiceCollectionExtensions.cs

src/Runtime/HumanTask/CrestCreates.HumanTask/
  HumanTaskServiceCollectionExtensions.cs
```

The default registrations were already removed in Slice 2. Slice 3 verifies
they remain absent and removes the temporary adapter types themselves; it does
not perform a second registration cutover.

Replace every explicit temporary-adapter registration from Slice 2 with:

```csharp
services.AddCrestCreatesInMemoryRuntimePersistence();
```

Do not keep forwarding wrapper Stores in Workflow/HumanTask assemblies.

Update every current composition root in the same Slice:

```text
tests/Runtime/Workflow/CrestCreates.Workflow.Tests/
tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests/
tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/
samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/Program.cs
samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioHost.cs
```

`AddWorkflowEngine()` and `AddHumanTaskRuntime()` register Runtime behavior only;
they no longer choose persistence. Tests that intentionally mock Stores may
register their mock plus a test Full Semantic capability declaration. Ordinary
tests and samples use the actual InMemory provider.

### 7.4 RED — prepare without persistence

Modify/add tests:

```text
tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests/
  HumanTaskInstancePreparerTests.cs
  HumanTaskRuntimeTests.cs
  HumanTaskDetachedTransitionTests.cs

tests/Runtime/Workflow/CrestCreates.Workflow.Tests/
  WorkflowSuspensionCommitterTests.cs
  WorkflowSuspensionAtomicityTests.cs
  WorkflowRuntimeTests.cs
  WorkflowDetachedTransitionTests.cs
```

RED cases:

```text
HumanTaskPreparer_ShouldNotPersist
HumanTaskStepExecutor_ShouldReturnPreparedSuspensionIntent
SuspensionCommit_Should_AtomicallyPersistWorkflowAndHumanTask
StaleWorkflowRevision_ShouldRollbackInsertedHumanTask
CrossTenantWorkflowHumanTaskCorrelation_ShouldFail
FailedSuspensionCommit_ShouldNotPublishLifecycleEvent
SuccessfulSuspensionCommit_ShouldPublishAfterCommit
FailedWorkflowUpdate_ShouldLeaveCallerAtPreState
FailedHumanTaskCompletion_ShouldLeaveCallerAtPreState
FailedCancellation_ShouldLeaveCallerAtPreState
CommitUnknownDuringContinuation_ShouldNotAdvanceCallerRevision
SuspendedWorkflow_ShouldNotSetCompletedAt
CompletedWorkflow_ShouldSetCompletedAt
FailedWorkflow_ShouldSetCompletedAt
```

### 7.5 GREEN — runner-owned suspension

Create:

```text
src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/
  IHumanTaskInstancePreparer.cs
  PreparedHumanTaskInstance.cs

src/Runtime/HumanTask/CrestCreates.HumanTask/
  DefaultHumanTaskInstancePreparer.cs
  Transitions/HumanTaskInstanceTransitionBuilder.cs

src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/
  WorkflowSuspensionIntent.cs
  WorkflowSuspensionReceipt.cs
  WorkflowSuspensionReceiptWriteResult.cs
  WorkflowSuspensionReceiptWriteStatus.cs
  WorkflowSuspensionReconciliationResult.cs
  WorkflowSuspensionReconciliationStatus.cs
  WorkflowSuspensionCommitUnknownException.cs
  IWorkflowSuspensionReceiptStore.cs
  IWorkflowSuspensionReconciler.cs

src/Runtime/Workflow/CrestCreates.Workflow/
  IWorkflowSuspensionCommitter.cs              # internal
  DefaultWorkflowSuspensionCommitter.cs
  DefaultWorkflowSuspensionReconciler.cs
  Transitions/WorkflowInstanceTransitionBuilder.cs
  WorkflowSuspensionOperationCanonicalWriter.cs
  WorkflowSuspensionOperationHashService.cs
```

Modify:

```text
src/Runtime/HumanTask/CrestCreates.HumanTask/
  DefaultHumanTaskRuntime.cs

src/Runtime/Workflow/CrestCreates.Workflow/
  HumanTaskStepExecutor.cs
  WorkflowExecutionRunner.cs
  WorkflowEngine.cs
  WorkflowContinuationService.cs
  HumanTaskCompletedWorkflowSubscriber.cs
  WorkflowServiceCollectionExtensions.cs
```

Mainline:

```text
HumanTaskStepExecutor
    -> IHumanTaskInstancePreparer.PrepareAsync
    -> WorkflowSuspensionIntent (no Store call)

WorkflowExecutionRunner
    -> validate/capture state and Pins before transaction
    -> DefaultWorkflowSuspensionCommitter
       -> IRuntimeTransactionCoordinator
          -> HumanTaskStore.AddAsync
          -> optional Snapshot evidence validation
          -> WorkflowStore.UpdateAsync(expectedRevision)
          -> ReceiptStore.AddAsync
       -> commit
    -> lifecycle/accountability notification
```

`DefaultWorkflowSuspensionCommitter` receives the original pre-state and a
separate post-state candidate. Neither Store mutates either object. When
`ExecuteAsync` returns successfully, the committer returns a committed snapshot
at `expectedRevision + 1`; the Runner then replaces its local reference/state.
On rollback or commit-unknown it returns no committed snapshot.

The same transition discipline is applied to every non-suspension update:

- `WorkflowExecutionRunner` deep-copies the caller-visible instance into one
  working copy before executing steps.
- `WorkflowInstanceTransitionBuilder` creates detached failure, completion,
  and continuation candidates without mutating the pre-state.
- suspension clears `CurrentStepId`, sets `WaitingHumanTaskKey`, and leaves
  `CompletedAt` null; only terminal Completed/Failed candidates set
  `CompletedAt`;
- `WorkflowContinuationService` validates the Pin and transition against the
  loaded pre-state, persists a detached Running candidate with CAS, publishes
  `workflow.resumed` only after success, and passes the committed snapshot to
  the Runner.
- `HumanTaskInstanceTransitionBuilder` creates detached completion,
  cancellation, completion-event-identity, recovered-completion, and
  dispatch-failure candidates.
- `DefaultHumanTaskRuntime` persists each candidate with the pre-state revision
  and replaces its local reference only after Store success. A dispatch
  failure is a second explicit transition from the already committed
  completion snapshot; if that update fails, neither the original loaded
  object nor the committed completion snapshot is mutated in place.

Each builder deep-copies mutable collections and Runtime State envelopes
through the registered state contract. It is internal Runtime logic, not a
public persistence extension point. Lifecycle/completion events are built from
the committed snapshot and remain post-commit.

The operation hash service uses the existing Canonical Hash Runtime with an
explicit writer over keys, revisions, Pins, correlation, and state-value
digests. It contains no payload text in public diagnostics.

The writer orders dictionaries/collections ordinally and writes each state
envelope as TypeId, optional exact SchemaRef, and the exact generated JSON
payload bytes. It writes the complete structured Pin hash metadata, not only
digest strings. No `ToString()`, runtime reflection, or JSON property-order
comparison participates.

The narrow receipt contract is fixed before either provider implements it:

```text
AddAsync(receipt)
GetAsync(RuntimeTenantScope, suspensionOperationId)
```

`AddAsync` is immutable: same operation identity/integrity is Duplicate, while
different integrity is Conflict. It never updates. The reconciliation result
is one of `Committed`, `NotCommitted`, `InvariantViolation`, or
`Indeterminate`; it contains provider-neutral evidence only.

`WorkflowSuspensionIntent.SuspensionOperationId` is allocated before the
transaction and is required/nonblank. The same intent retains the same
operation identity, HumanTask key, expected Workflow revision, and operation
integrity across reconciliation or a caller-controlled identical retry. The
coordinator never generates a replacement identity and never replays the
delegate. Add:

```text
SuspensionIntent_ShouldRetainStableOperationIdentityAcrossRetry
CommitUnknown_ShouldReturnStableSuspensionOperationIdentity
```

Suspension retry/reconciliation order is fixed:

1. compute and validate the full operation integrity before transaction entry;
2. read Receipt by exact tenant scope + operation ID;
3. matching Receipt → reconcile lineage and return `Committed` without writes;
4. conflicting Receipt → `InvariantViolation` without writes;
5. no Receipt → attempt the one suspension transaction;
6. task/receipt unique conflict caused by a concurrent identical attempt, or a
   commit-unknown outcome → leave the delegate and perform fresh read-only
   reconciliation;
7. only exact `NotCommitted` permits the caller to invoke the same intent once
   again; the coordinator itself never retries.

An existing task with no matching Receipt, a Receipt with missing/incompatible
rows, or a different integrity is not treated as idempotent success.

The committer translates `RuntimeTransactionCommitUnknownException` into
`WorkflowSuspensionCommitUnknownException`, carrying only the stable suspension
operation ID and exact Workflow/HumanTask keys required to call the public
reconciler. It does not retain the Npgsql exception or claim committed/rolled
back.

`DefaultHumanTaskRuntime.CreateAsync` uses the same preparer and then performs a
standalone Add through the selected coordinator. It does not duplicate
descriptor/assignee logic. It returns a new committed snapshot at revision 1
only after the coordinator succeeds; it never treats the unsaved prepared
instance as committed.

### 7.6 Existing test migration

Modify:

```text
tests/Runtime/Workflow/CrestCreates.Workflow.Tests/
  InMemoryWorkflowInstanceStoreTests.cs
  WorkflowContinuationTests.cs
  WorkflowRuntimeTests.cs
  WorkflowEngineTests.cs
  WorkflowCompositionTests.cs

tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests/
  InMemoryHumanTaskInstanceStoreTests.cs
  HumanTaskRuntimeTests.cs

samples/ProcurementApproval/
  src/CrestCreates.Sample.Procurement.Host/Program.cs
  src/CrestCreates.Sample.Procurement.Host/ProcurementHumanTaskIntegration.cs
  src/CrestCreates.Sample.Procurement.Host/ProcurementGoldenScenario.cs
  tests/CrestCreates.Sample.Procurement.Tests/Acceptance/GoldenSampleAcceptanceTests.cs
  tests/CrestCreates.Sample.Procurement.Tests/Acceptance/SecondRoundArchitectureAcceptanceTests.cs
```

Move legacy Store-specific tests that assert obsolete shallow/upsert behavior
to:

```text
99_RecycleBin/Phase9bDurablePersistence/LegacyStoreTests/
```

Keep behavior tests and migrate them to shared contract cases.

### 7.7 Review gate

Run:

```bash
rtk dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests
rtk dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
rtk dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk dotnet build solutions/CrestCreates.Runtime.slnx
rtk git diff --check
```

Review:

- InMemory passes atomic suspension and rollback, not merely Store cases;
- Runner contains no provider branch;
- HumanTask executor contains no Store write;
- one failed CAS rolls back task and receipt;
- post-commit notifications cannot roll back or reinterpret state;
- no process durability/restart claim is attached to InMemory.
- every composition root selects a provider explicitly;
- provider support is validated by capabilities, not concrete-type checks;
- operation identity and receipt integrity are stable before transaction entry.
- failure/completion/continuation/cancel/dispatch-failure paths never mutate the
  loaded pre-state before CAS commit succeeds.

---

## 8. Slice 4 — Direct-Npgsql Provider Kernel and Migrations

### 8.1 RED — PostgreSQL fixture and migration contract

Create:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  Fixtures/PostgreSqlRuntimeCollection.cs
  Fixtures/PostgreSqlRuntimeCollectionFixture.cs
  Fixtures/PostgreSqlRuntimeSchemaLease.cs
  Infrastructure/PostgreSqlRuntimeTestServiceProviderFactory.cs
  Infrastructure/PostgreSqlRuntimeDatabaseInspector.cs
  Migrations/PostgreSqlRuntimeMigrationTests.cs
  Architecture/PostgreSqlProviderPublicApiTests.cs
```

Fixture:

```text
one postgres:16-alpine container per xUnit collection
one itest_{guid} schema per test
fresh NpgsqlDataSource and IServiceProvider factory
no shared tracking connection
explicit schema cleanup owned by the lease
```

RED cases:

```text
Migration_ShouldCreateSchemaFromEmptyDatabase
Migration_ShouldReapplyWithoutMutation
Migration_ShouldResumeAfterInterruptedAttempt
Migration_ShouldRejectChangedAppliedChecksum
Migration_ShouldRejectUnknownNewerSchema
MigrationHistoryTable_WithUnexpectedShape_ShouldFailClosed
ValidationOnly_OnEmptyDatabase_ShouldFailWithoutCreatingSchemaOrHistory
RuntimePublicContracts_DoNotExposeProviderTypes
PostgreSqlProvider_ShouldNotDependOnEntityFrameworkCore
PostgreSqlProvider_ShouldNotEnableDynamicJson
PostgreSqlProvider_ShouldNotReferenceRuntimePersistenceConcrete
```

### 8.2 GREEN — provider configuration and session kernel

Create:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  Configuration/PostgreSqlRuntimePersistenceOptions.cs
  Configuration/PostgreSqlRuntimePersistenceOptionsValidator.cs
  Configuration/PostgreSqlRuntimeKernelServiceCollectionExtensions.cs
  Kernel/PostgreSqlRuntimeDataSourceFactory.cs
  Kernel/PostgreSqlRuntimeSession.cs
  Kernel/PostgreSqlRuntimeSessionAccessor.cs
  Kernel/PostgreSqlRuntimeCommandFactory.cs
  Kernel/PostgreSqlRuntimeExceptionTranslator.cs
  Transactions/PostgreSqlRuntimeTransactionCoordinator.cs
  Health/PostgreSqlRuntimeSchemaCompatibilityValidator.cs
```

Options:

```text
ConnectionString
SchemaName = crest_runtime
CommandTimeout
ApplyMigrations = false by default
```

Validate SchemaName against a narrow identifier grammar, then quote through
Npgsql identifier quoting. No request-time dynamic identifier is accepted.

Slice 4 is an incomplete provider-kernel project, not a selectable Runtime
provider. It does not create/register
`IRuntimePersistenceProviderCapabilities`, any Runtime Store, or `IAuditSink`.
Provider capability registration is introduced only in Slice 5 after all Full
Semantic Stores pass their RED contract cases. It declares `FullSemantic`
during Slices 5–6; the `FullDurable` declaration is withheld until Slice 7
produces the required crash and database NativeAOT evidence.

Kernel DI lifetimes are fixed:

```text
NpgsqlDataSource                              singleton
PostgreSqlRuntimeSessionAccessor             singleton
IRuntimeTransactionCoordinator               singleton
PostgreSqlRuntimeMigrator                    singleton
Schema compatibility hosted validator        singleton/hosted service
```

`PostgreSqlRuntimeKernelServiceCollectionExtensions` is internal and exists for
provider composition/tests. It registers only the data source, internal session
accessor, coordinator, migrator, and schema validator. It does not expose a
public “select PostgreSQL provider” extension and cannot satisfy Workflow
startup provider-tier validation.

`PostgreSqlRuntimeTransactionCoordinator`:

- `NpgsqlDataSource` singleton;
- root Execute opens one connection and Read Committed transaction;
- internal `AsyncLocal<PostgreSqlRuntimeSession?>`;
- nested Execute joins;
- Store outside ambient opens its own root transaction;
- interlocked guard rejects parallel use of one session;
- commit success wins over late cancellation;
- connection loss during COMMIT becomes
  `RuntimeTransactionCommitUnknownException`;
- no execution-strategy retry.

Provider exceptions are logged internally and translated without exposing an
Npgsql exception as a public inner exception.

Kernel exception translation is phase-aware:

| Provider observation | Public result |
|---|---|
| connection/timeout failure before COMMIT begins | `RuntimePersistenceUnavailableException` |
| caller cancellation before COMMIT begins | rollback, then `OperationCanceledException` |
| connection loss or cancellation/ack ambiguity after COMMIT is sent | `RuntimeTransactionCommitUnknownException` |
| COMMIT acknowledged, token cancelled immediately afterward | success; never rewritten as cancellation |

Store JSON/CAS/constraint translation is added after Store RED tests in Slice
5. Slice 4 contains no dormant Store-specific translation branch.

Add provider-kernel tests:

```text
ProviderTimeoutBeforeCommit_ShouldTranslateToPersistenceUnavailable
CancellationAfterCommitAcknowledgement_ShouldNotRewriteOutcome
CommitAcknowledgementAmbiguity_ShouldWinOverCancellation
ProviderExceptions_ShouldNotEscapeAsInnerExceptions
```

Internal session ownership is fixed:

```text
PostgreSqlRuntimeSessionAccessor
    -> internal current session only
PostgreSqlRuntimeTransactionCoordinator
    -> sole root connection/transaction owner
future provider-co-located Stores (Slices 5 and 6)
    -> request the current internal session
    -> otherwise ask the coordinator to run one short Required transaction
```

No Store opens an independent connection while an ambient Runtime transaction
exists. `AsyncLocal` is cleared in `finally`, and the session is marked disposed
before its connection is returned to the data source.

### 8.3 Migration layout

Create:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  Migrations/PostgreSqlRuntimeMigration.cs
  Migrations/PostgreSqlRuntimeMigrationCatalog.cs
  Migrations/PostgreSqlRuntimeMigrator.cs
  Migrations/PostgreSqlRuntimeMigrationChecksum.cs
  Migrations/Sql/V001__workflow_humantask_receipts.sql
  Migrations/Sql/V002__descriptor_snapshots.sql
  Migrations/Sql/V003__accountability_sink.sql
```

Migration mode is branched before any DDL or apply-lock acquisition.

`ApplyMigrations=false` validation-only algorithm:

1. inspect configured schema existence without creating it;
2. missing schema → fail compatibility validation;
3. inspect migration history table existence without creating it;
4. missing history table → fail compatibility validation;
5. validate exact history-table shape;
6. load the embedded catalog and compare the database ledger;
7. pending, missing, changed, gapped, or newer migration → fail closed;
8. return compatible without executing DDL or acquiring the migration apply
   advisory lock.

`ApplyMigrations=true` apply algorithm, under the advisory lock:

1. create configured schema if missing;
2. bootstrap-create `crest_runtime_schema_migrations` if missing;
3. inspect and validate the exact history-table shape;
4. load ordered embedded migration catalog;
5. compare database version/checksum ledger;
6. reject gaps, changed checksums, and newer versions;
7. apply each pending SQL resource in its own transaction;
8. write its history row in the same transaction;
9. validate schema/history/catalog compatibility again before success.

`ValidationOnly_OnEmptyDatabase_ShouldFailWithoutCreatingSchemaOrHistory`
catches the compatibility exception, then queries PostgreSQL from an inspector
connection and proves that neither the configured schema nor
`crest_runtime_schema_migrations` was created.

Bootstrap has sole ownership of the configured schema and migration history
table. Its fixed table is:

```text
crest_runtime_schema_migrations
    version       integer     not null primary key
    logical_name  text        not null
    checksum      char(64)    not null
    applied_at    timestamptz not null
```

When the table already exists, the migrator inspects `pg_catalog` for exact
column names/types, nullability, primary key, and absence of unexpected
columns. Any mismatch fails closed before reading or applying the migration
catalog. The bootstrap DDL is a fixed provider constant; it is not V000/V001
and is never included in the version checksum ledger.

Migration identity/checksum rules:

- embedded SQL resources have fixed logical names matching `VNNN__name.sql`;
- version numbers are positive, unique, and contiguous from V001;
- checksum is lowercase SHA-256 over the exact embedded UTF-8 resource bytes;
  no newline normalization occurs at runtime;
- the history row stores version, logical name, checksum, and applied time;
- an applied version with a different name/checksum fails closed;
- a database version above the catalog maximum fails closed;
- `ApplyMigrations=false` executes zero DDL, acquires no migration apply lock,
  and fails for missing schema/history or any pending/incompatible migration;
- `ApplyMigrations=true` holds one connection-level PostgreSQL advisory lock
  derived from the fixed provider namespace plus validated schema name, applies
  pending versions, validates again, and releases the lock in `finally`;
- every migration plus its history insert is one transaction, so an interrupted
  version is either fully applied/recorded or absent.

Table rules fixed in SQL:

```text
tenant_scope_kind smallint not null
tenant_id text not null
check host => empty tenant_id; tenant => nonempty tenant_id
all instance PK/FK/unique predicates start with tenant scope
revision bigint not null
status integer not null
pins/state/provider DTO JSON stored as jsonb parameters
```

No EF migration, DbContext, LINQ query, or generated ORM model enters this
project.

### 8.4 Fixed relational schema

V001 creates:

```text
runtime_workflow_instances
runtime_human_task_instances
runtime_workflow_suspension_receipts
```

`runtime_workflow_instances`:

```text
tenant_scope_kind       smallint    not null
tenant_id               text        not null
instance_id             text        not null
revision                bigint      not null
workflow_pin            jsonb       not null
status                  smallint    not null
current_step_id         text        null
step_index              integer     not null
waiting_human_task_id   text        null
started_at              timestamptz not null
completed_at            timestamptz null
updated_at              timestamptz null
audit_origin            jsonb       null
variables               jsonb       not null
step_variables          jsonb       not null
step_results            jsonb       not null
error_message           text        null
last_lifecycle_audit_id text        null
primary key (tenant_scope_kind, tenant_id, instance_id)
check (revision >= 1)
check exact host/tenant encoding
```

`runtime_human_task_instances`:

```text
tenant_scope_kind                 smallint    not null
tenant_id                         text        not null
instance_id                       text        not null
revision                          bigint      not null
human_task_pin                    jsonb       not null
status                            smallint    not null
workflow_instance_id              text        null
workflow_step_id                  text        null
input_state                       jsonb       null
output_state                      jsonb       null
outcome                           text        null
assignee_user_id                  text        null
assignee_role_id                  text        null
candidate_user_ids                jsonb       not null
candidate_role_ids                jsonb       not null
organization_unit_id              text        null
position_id                       text        null
assignee_resolution_reason        text        null
created_at                        timestamptz not null
completed_at                      timestamptz null
cancelled_at                      timestamptz null
cancellation_reason               text        null
completion_dispatch_error         text        null
completion_dispatch_failed_at     timestamptz null
completion_dispatch_attempt_count integer     not null
completion_event_id               text        null
primary key (tenant_scope_kind, tenant_id, instance_id)
check (revision >= 1)
check exact host/tenant encoding
foreign key (tenant_scope_kind, tenant_id, workflow_instance_id)
    -> runtime_workflow_instances(scope, tenant, instance)
    deferrable initially deferred
```

For reciprocal correlation, HumanTask has a unique constraint on
`(tenant_scope_kind, tenant_id, workflow_instance_id, instance_id)`.
Workflow has a deferred composite foreign key:

```text
(tenant_scope_kind, tenant_id, instance_id, waiting_human_task_id)
    -> runtime_human_task_instances(
         tenant_scope_kind, tenant_id, workflow_instance_id, instance_id)
```

This proves that the waiting task points back to that exact Workflow. The
waiting-task uniqueness constraint is explicitly tenant-scoped:

```sql
create unique index ux_runtime_workflow_waiting_human_task
on runtime_workflow_instances (
    tenant_scope_kind,
    tenant_id,
    waiting_human_task_id
)
where waiting_human_task_id is not null;
```

It prevents one task from suspending two Workflows within the same exact tenant
scope while allowing the same task ID in host and different tenant scopes. A
partial unique index on HumanTask
`(tenant_scope_kind, tenant_id, workflow_instance_id, workflow_step_id)` where
the task has neither `completed_at` nor `cancelled_at` prevents two live tasks
for the same step in one tenant scope.

`runtime_workflow_suspension_receipts`:

```text
tenant_scope_kind       smallint    not null
tenant_id               text        not null
suspension_operation_id text        not null
workflow_instance_id    text        not null
human_task_instance_id  text        not null
workflow_from_revision  bigint      not null
workflow_to_revision    bigint      not null
operation_integrity     jsonb       not null
receipt_payload         jsonb       not null
committed_at            timestamptz not null
primary key (tenant_scope_kind, tenant_id, suspension_operation_id)
foreign keys to exact tenant-scoped Workflow and HumanTask
check (workflow_to_revision = workflow_from_revision + 1)
```

V002 creates:

```text
runtime_descriptor_snapshots
    snapshot_id             text        primary key
    package_id              text        not null
    package_version         text        not null
    created_at              timestamptz not null
    persistence_integrity   jsonb       not null
    snapshot_payload        jsonb       not null

runtime_descriptor_snapshot_entries
    snapshot_id             text        not null references snapshots
    descriptor_namespace    text        not null
    descriptor_id           text        not null
    descriptor_version      text        not null
    contract_hash           text        not null
    definition_hash         text        not null
    entry_payload           jsonb       not null
    primary key (
      snapshot_id, descriptor_namespace, descriptor_id, descriptor_version)
```

The parent payload preserves relationships and complete detached evidence; the
entry table provides exact Pin evidence lookup. Both are inserted in one
transaction. Duplicate/Conflict compares `persistence_integrity`, never JSON
text.

V003 creates:

```text
runtime_accountability_envelopes
    sink_id             text        not null
    audit_id            text        not null
    integrity           jsonb       not null
    envelope_payload    jsonb       not null
    first_accepted_at   timestamptz not null default transaction_timestamp()
    primary key (sink_id, audit_id)
```

All text identifiers are bounded by pre-SQL contract validation. Every `jsonb`
value is written with explicit generated serialization and
`NpgsqlDbType.Jsonb`.

### 8.5 Kernel boundary

Slice 4 creates only migration/bootstrap/history SQL resources. It creates no
Workflow, HumanTask, Snapshot, Receipt, or Audit DML constants; no provider DTO,
mapper, Store, Sink, or full-provider DI registration exists before its owning
RED Slice.

### 8.6 Review gate

Run:

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~Migration|FullyQualifiedName~Architecture"
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~RuntimePersistence|FullyQualifiedName~PersistenceProjects"
rtk dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
rtk git diff --check
```

Review:

- direct `Npgsql` package only;
- fixed parameterized SQL;
- no dynamic JSON mapping;
- checksum/advisory-lock/startup rules executable;
- provider sessions are internal;
- no automatic transaction replay.
- no Runtime Store/Sink type, DML SQL, provider record/mapper, or Full Durable
  capability is present.

---

## 9. Slice 5 — PostgreSQL Stores, Receipts, and Lazy Recovery

### 9.1 RED — provider contract wrappers

Create:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  PostgreSqlRuntimeContractTestBase.cs
  Drivers/PostgreSqlWorkflowInstanceStoreContractDriver.cs
  Drivers/PostgreSqlHumanTaskInstanceStoreContractDriver.cs
  Drivers/PostgreSqlDescriptorSnapshotStoreContractDriver.cs
  Drivers/PostgreSqlRuntimeTransactionContractDriver.cs
  Drivers/PostgreSqlRuntimeStateContractDriver.cs
  Workflow/PostgreSqlWorkflowInstanceStoreContractTests.cs
  HumanTask/PostgreSqlHumanTaskInstanceStoreContractTests.cs
  Snapshots/PostgreSqlDescriptorSnapshotStoreContractTests.cs
  Transactions/PostgreSqlRuntimeTransactionContractTests.cs
  State/PostgreSqlRuntimeStateContractTests.cs
  Composition/PostgreSqlSuspensionAtomicityTests.cs
  Recovery/PostgreSqlWorkflowRecoveryTests.cs
```

Every shared semantic case that InMemory runs also runs against PostgreSQL:

```text
Add/CAS
tenant/host isolation
correlation uniqueness
detached/deep snapshots
deterministic ordering
nested transaction joining
rollback
atomic suspension
stale revision rollback
```

For PostgreSQL, `HostAndTenantSameId_ShouldRemainDistinct` inserts both rows
through production Stores and verifies both remain present through a fresh
read, proving the database key/unique constraints as well as query scoping.
`SameWaitingHumanTaskIdAcrossTenants_ShouldRemainDistinct` creates two
suspensions with the same Workflow/task IDs in different tenant scopes and
proves both commits succeed and both correlations remain independently
queryable.

PostgreSQL-only RED cases:

```text
DescriptorSnapshotStore_ShouldSurviveRestart
DescriptorSnapshotStore_ShouldReturnDuplicateForIdenticalContent
DescriptorSnapshotStore_ShouldRejectSameIdentityDifferentContent
DescriptorSnapshotStore_ShouldReturnDetachedSnapshot
DescriptorPinWithSnapshotId_ShouldRequireMatchingEvidenceEntry
SnapshotEntry_ShouldNotReplaceRegistryResolution
Restart_WithMatchingDescriptorPin_ShouldResumeWorkflow
Restart_WithMissingWorkflowDescriptor_ShouldFailClosed
Restart_WithMissingHumanTaskDescriptor_ShouldFailClosed
Restart_WithMismatchedDefinitionHash_ShouldFailClosed
Restart_WithMismatchedContractHash_ShouldFailClosed
Restart_WithMismatchedHashProfile_ShouldFailClosed
FailedPinValidation_ShouldNotChangeRevisionOrStatus
UnknownStateTypeId_OnRestart_ShouldFailClosedWithoutMutation
MismatchedStateSchemaRef_ShouldFailClosed
HostStartup_WithUnresolvedDormantPin_ShouldSucceedSchemaValidation
Execution_WithUnresolvedDormantPin_ShouldFailClosed
IdenticalSuspensionRetry_ShouldNotCreateSecondHumanTask
ConflictingSuspensionOperationRetry_ShouldFailClosed
HostScopeUniqueConstraint_ShouldRejectDuplicateId
SameWaitingHumanTaskIdAcrossTenants_ShouldRemainDistinct
Restart_CompleteHumanTask_ShouldPersistCompletionAndAllowContinuation
HumanTaskCompletionCommitted_BeforeContinuationDelivery_ShouldRemainDurable
FreshProvider_ShouldRecoverSuspendedWorkflowWithoutProviderObjectLeak
ProviderCommitAcknowledgementLoss_ShouldTranslateToCommitUnknown
MatchingCommittedReceipt_ShouldReconcileAsCommitted
MissingReceiptWithExactPreState_ShouldReconcileAsNotCommitted
DuplicateInstanceKey_ShouldTranslateToDuplicateEntity
DuplicateReceiptIdentity_ShouldReturnDuplicateOrConflict
ActiveStepCorrelationConflict_ShouldNotTranslateToDuplicateEntity
ProviderConstraintName_ShouldNotEscapePublicFailure
PostgreSqlStorePayloadLimits_ShouldRejectInvalidOptions
PostgreSqlProvider_BeforeAotEvidence_ShouldNotClaimFullDurable
```

### 9.2 GREEN — provider persistence DTOs and generated JSON

Create:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  Serialization/PostgreSqlRuntimeJsonSerializerContext.cs
  Serialization/PostgreSqlRuntimeJsonSerializer.cs
  Models/PostgreSqlWorkflowInstanceRecord.cs
  Models/PostgreSqlHumanTaskInstanceRecord.cs
  Models/PostgreSqlDescriptorSnapshotRecord.cs
  Models/PostgreSqlWorkflowSuspensionReceiptRecord.cs
  Mapping/PostgreSqlWorkflowInstanceMapper.cs
  Mapping/PostgreSqlHumanTaskInstanceMapper.cs
  Mapping/PostgreSqlDescriptorSnapshotMapper.cs
  Mapping/PostgreSqlSuspensionReceiptMapper.cs
```

The provider context uses existing JSON Contract BuildTasks explicit roots.
Its Slice 5 explicit root ledger contains exactly the four provider record
types listed above plus the closed nested value records they require. It does not add a root
for `object`, a provider assembly scan, or Npgsql dynamic JSON.

Mapping rules:

- provider DTOs never escape;
- each read creates new domain collections and immutable state envelopes;
- runtime state payloads are never materialized as `object` by the provider;
- pins persist complete structured hashes;
- enums store stable numeric columns plus validated JSON where appropriate;
- JSON parameters use explicit `NpgsqlDbType.Jsonb`;
- Npgsql dynamic POCO JSON remains disabled.

For every JSON column the mapper has two named methods:

```text
ToRecord(domain)          # creates a detached provider DTO
FromRecord(record)        # validates then creates a fresh domain snapshot
```

Reader code first loads scalars and raw JSON text/bytes, then deserializes with
the exact generated `JsonTypeInfo<TRecord>`. A malformed provider record throws
`RuntimePersistenceContractException` with a safe failure ID; it is never
returned as a partially populated domain instance.

Npgsql JSON access uses `GetFieldValue<string>` (or raw UTF-8 bytes where the
driver supports the same AOT-safe path) and string/byte parameters explicitly
typed `NpgsqlDbType.Jsonb`. It never calls `EnableDynamicJson`, requests a POCO
from `GetFieldValue<TRecord>`, or delegates CLR type selection to Npgsql.

### 9.3 GREEN — Store implementations

Create:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  Stores/PostgreSqlWorkflowInstanceStore.cs
  Stores/PostgreSqlHumanTaskInstanceStore.cs
  Stores/PostgreSqlDescriptorSnapshotStore.cs
  Stores/PostgreSqlWorkflowSuspensionReceiptStore.cs
  Sql/PostgreSqlWorkflowSql.cs
  Sql/PostgreSqlHumanTaskSql.cs
  Sql/PostgreSqlDescriptorSnapshotSql.cs
  Sql/PostgreSqlSuspensionReceiptSql.cs
  Configuration/PostgreSqlRuntimePersistenceServiceCollectionExtensions.cs
  PostgreSqlRuntimeProviderCapabilities.cs
```

The four DML classes are fixed internal parameterized command constants created
only after the Slice 5 Store RED cases. Store files bind explicit parameters
and map readers by fixed ordinal/name. No Workflow/HumanTask Runtime project
contains SQL.

This Slice also extends `PostgreSqlRuntimePersistenceOptions` with bounded
Runtime State and Snapshot payload limits and adds validator cases before the
Stores consume them. Audit limits remain owned by Slice 6
`PostgreSqlAuditSinkOptions`.

CAS update shape:

```sql
update ... set revision = revision + 1, ...
where tenant_scope_kind = @tenant_scope_kind
  and tenant_id = @tenant_id
  and instance_id = @instance_id
  and revision = @expected_revision
returning revision;
```

Rules:

- Add never updates;
- Update never inserts;
- zero updated rows is `RuntimeConcurrencyException`;
- task insert rolls back if later Workflow CAS fails;
- Snapshot immutable-content decision uses a dedicated canonical persistence
  hash, not JSON string equality;
- list ordering is explicit `CreatedAt`, then ordinal ID;
- waiting-task and workflow-step correlations use tenant-scoped unique
  constraints.

Constraint handling is owned by the Store whose operation established the
invariant:

- Workflow/HumanTask Add maps only its exact instance primary key to
  `DuplicateInstance`;
- Receipt Add catches only its exact operation-identity key, reads the accepted
  row in the same transaction/session, and compares canonical integrity to
  return `Duplicate` or `Conflict`;
- active-step and reciprocal waiting-task constraints map to their distinct
  provider-neutral contract codes;
- every other constraint identity fails closed as an unknown persistence
  contract violation.

The public result/exception contains no SQLSTATE, relation, index, or constraint
name. Tests inspect the concrete exception object and message, not only its
type, to prove provider identifiers do not escape.

Store-specific exception translation is added in this Slice, after its RED
cases:

| Provider observation | Public result |
|---|---|
| Workflow/HumanTask instance primary-key violation during Add | `RuntimeDuplicateEntityException(code: DuplicateInstance)` |
| suspension receipt operation-identity violation | Receipt Store reads the accepted row and returns `Duplicate` for identical integrity or `Conflict` for different integrity |
| pending Workflow-step unique violation | `RuntimePersistenceContractException(code: ActiveStepCorrelationConflict)` |
| reciprocal waiting-task unique/FK conflict | `RuntimePersistenceContractException(code: WaitingTaskCorrelationConflict)` |
| CAS `UPDATE ... RETURNING` returns no row | `RuntimeConcurrencyException` |
| invalid persisted JSON/enum/key invariant | `RuntimePersistenceContractException` |

The translator branches on internal, migration-owned constraint identity only
after confirming SQLSTATE `23505`/`23503`. An unknown constraint, a constraint
from the wrong operation, or a cross-operation collision becomes a sanitized
`RuntimePersistenceContractException`; it is never guessed to be a duplicate
instance.

The public
`AddCrestCreatesPostgreSqlRuntimePersistence(...)` extension composes the
Slice 4 kernel and registers the four singleton stateless Stores plus
`IRuntimePersistenceProviderCapabilities`. It rejects a second selected
Runtime provider, does not use `TryAdd` ambiguity, does not call
`AddRuntimePersistence()`, and does not discover Runtime State contributors.
Host composition must call both extensions explicitly.

`PostgreSqlRuntimeProviderCapabilities` initially declares:

```text
Tier = FullSemantic
SupportsAddAndCompareAndSwap = true
SupportsAtomicMultiStoreTransactions = true
SupportsRollback = true
SupportsProcessDurability = true
SupportsRestartRecovery = true
SupportsMigrations = true
SupportsDatabaseNativeAotEvidence = false
```

Process durability, restart, and migration behavior are already executable,
but the tier remains `FullSemantic` because `FullDurable` is an evidence-gated
claim that also requires Slice 7 crash and database NativeAOT proof. Add:

```text
PostgreSqlProvider_BeforeAotEvidence_ShouldNotClaimFullDurable
```

### 9.4 GREEN — Receipt reconciliation

`IWorkflowSuspensionReceiptStore` is a Workflow-owned narrow contract. It is not
a generic Outbox/operation framework.

PostgreSQL receipt:

```text
immutable
same suspension transaction
stable operation ID
structured operation integrity
no delivery state
```

Reconciliation:

```text
matching receipt + compatible current lineage -> Committed
no receipt + exact pre-state + no task -> NotCommitted
receipt/content conflict or partial lineage -> InvariantViolation
unavailable/inconclusive -> Indeterminate
```

No automatic replay occurs inside the coordinator. The Workflow suspension
service decides whether an exact NotCommitted result permits one identical
caller-controlled retry.

The PostgreSQL test assembly receives `InternalsVisibleTo` only for the provider
test project. A provider-internal transaction-command seam simulates loss of
the COMMIT acknowledgement after the command has been sent and asserts
`RuntimeTransactionCommitUnknownException`; it is not registered in production
DI and is not public. Slice 7 separately proves the durable application-response
loss outcome across a real worker process.

### 9.5 GREEN — lazy Pin recovery

Modify:

```text
src/Runtime/Workflow/CrestCreates.Workflow/
  WorkflowExecutionRunner.cs
  WorkflowContinuationService.cs

src/Runtime/HumanTask/CrestCreates.HumanTask/
  DefaultHumanTaskRuntime.cs
```

Load/transition path:

1. load exact tenant-scoped instance;
2. resolve Pin lazily from the current type-specific Registry;
3. validate optional Snapshot evidence;
4. restore registered state;
5. execute the exact object returned by the resolver.

Host startup does not enumerate Workflow/HumanTask rows. Add a startup
composition test proving an unresolved dormant Pin does not fail schema startup,
then an execution test proving the same instance fails closed without revision
change.

Add the complete restart mainline:

```text
fresh provider
    -> load suspended Workflow + waiting HumanTask by exact keys
    -> validate reciprocal correlation
    -> validate both Pins
    -> restore registered state
    -> complete HumanTask through CAS
    -> observe durable completion
    -> deliver the existing local completion event in-process
    -> continue Workflow through CAS
```

`Restart_CompleteHumanTask_ShouldPersistCompletionAndAllowContinuation` proves
the happy chain. `HumanTaskCompletionCommitted_BeforeContinuationDelivery_ShouldRemainDurable`
suppresses delivery after the completion commit and proves only the HumanTask
completion is durable; it explicitly does not expect Workflow resume.
`FreshProvider_ShouldRecoverSuspendedWorkflowWithoutProviderObjectLeak`
disposes the first service provider and data source, builds a second provider,
and asserts no connection/session/tracking identity is retained.

### 9.6 Review gate

Run:

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~StoreContract|FullyQualifiedName~Suspension|FullyQualifiedName~Recovery"
rtk dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests
rtk dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
rtk dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk git diff --check
```

Review:

- InMemory/PostgreSQL semantic cases are identical, not “where applicable”;
- only durable/restart/migration cases are PostgreSQL-only;
- Snapshot never constructs Descriptor payloads;
- dormant Pin startup and lazy execution semantics are proven separately;
- Slice 5 proves provider COMMIT ambiguity and read-only Receipt
  reconciliation, not application response loss;
- failed recovery leaves rows/revisions unchanged.
- public PostgreSQL provider selection appears after all four Store families
  pass their shared semantic cases, but its tier remains Full Semantic;
- no Audit record/mapper/DML/Sink registration exists yet.

---

## 10. Slice 6 — Durable Audit Sink and #25 Enlistment Probe

### 10.1 RED — reuse Phase 9a contract cases

Create:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  Audit/PostgreSqlAuditSinkContractDriver.cs
  Audit/PostgreSqlAuditSinkContractTests.cs
  Audit/PostgreSqlAuditSinkRestartTests.cs
  Composition/PostgreSqlEnlistmentProbeTests.cs
```

Reference:

```text
tests/Shared/CrestCreates.Accountability.Testing
tests/Shared/CrestCreates.Runtime.Persistence.Testing
```

Do not reference concrete `CrestCreates.Accountability.Tests`.

Run all existing `AuditSinkContractCases` plus:

```text
AcceptedAuditEnvelope_ShouldSurviveRestart
IdenticalAuditRetry_ShouldReturnDuplicate
ConflictingAuditRetry_ShouldReturnConflict
ConflictingAuditRetry_ShouldNotOverwriteAcceptedEnvelope
PostgreSqlAuditSink_ShouldPassSharedContractCases
DurableAuditSink_ShouldNotAddProductQueryInterface
StateCommit_ShouldNotClaimAuditDeliveryGuarantee
PostgreSqlAuditSink_ShouldRejectInvalidStableSinkOptions
```

### 10.2 GREEN — PostgreSQL IAuditSink

Create:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  Audit/PostgreSqlAuditSink.cs
  Audit/PostgreSqlAuditSinkOptions.cs
  Audit/PostgreSqlAuditSinkReader.cs        # internal/test driver seam only
  Models/PostgreSqlAuditEnvelopeRecord.cs
  Mapping/PostgreSqlAuditEnvelopeMapper.cs
  Sql/PostgreSqlAuditSinkSql.cs
```

This Slice extends `PostgreSqlRuntimeJsonSerializerContext` with the Audit
record and its closed nested records only after the Audit RED cases exist.
`PostgreSqlAuditSinkSql` owns the fixed parameterized Sink DML; no Audit DTO,
mapper, DML constant, or Sink registration exists in Slices 4 or 5.

Write algorithm:

1. serialize the safe immutable envelope with
   `AccountabilityJsonSerializerContext`;
2. execute parameterized
   `INSERT ... ON CONFLICT (sink_id, audit_id) DO NOTHING RETURNING first_accepted_at`;
3. when no row returns, read the accepted structured Integrity and
   `first_accepted_at` in the same short Runtime transaction;
4. exact structured equality → Duplicate;
5. difference → Conflict;
6. never update accepted envelope or first acceptance time.

`sink_id` is a required stable option with a bounded nonblank value. It allows
multiple logical sinks to share one database without changing Phase 9a's
`AuditId` semantics. `first_accepted_at` comes from PostgreSQL and the
Duplicate result returns the original value.

The internal reader supports provider contract tests. No public product query
interface is added.

The sink does not sanitize and does not join Workflow state transactions by
default. It only persists envelopes it receives after Phase 9a sanitization.

After the shared and restart cases turn green,
`AddCrestCreatesPostgreSqlRuntimePersistence(...)` is extended to register the
singleton `IAuditSink` and its stable options. This is the only Audit DI change;
it does not change the already-selected provider tier or create an Outbox
delivery claim.

### 10.3 RED/GREEN — #25 enlistment probe

Create test-only provider-co-located probe:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  Composition/TestPostgreSqlEnlistmentProbeStore.cs
```

Cases:

```text
OutboxStore_ShouldBeAbleToEnlistWithoutProviderLeak
EnlistedProbeAndState_ShouldCommitTogether
EnlistedProbeAndState_ShouldRollbackTogether
```

The probe public interface contains only strings/value records. Its PostgreSQL
implementation uses the provider's internal Store registration/session
mechanism exactly as #25 would.

The PostgreSQL fixture creates
`itest_runtime_enlistment_probe(scope_key text, probe_id text, value text, ...)`
inside the test-owned schema; it is not a production migration. The probe Store
uses the current internal session and a fixed parameterized INSERT. Tests call
it once beside Workflow state inside one `IRuntimeTransactionCoordinator`
delegate, then observe both values through fresh reads after commit/rollback.

Do not add:

- `IOutboxStore`;
- outbox table;
- delivery state;
- dispatcher/retry;
- public Npgsql session participant.

This Plan fixes one internal provider mechanism:

```text
internal IPostgreSqlRuntimeStoreSessionAccessor
    + provider-co-located Store registration
```

#25 adds its PostgreSQL Outbox Store to the same PostgreSQL provider assembly,
where it can consume the internal session accessor. It does not require a
friend extension assembly or a new Runtime public contract.

The test project receives friend access solely to exercise this internal
extension seam. No Runtime abstraction gains an enlistment callback,
`DbConnection`, `DbTransaction`, `NpgsqlConnection`, or generic provider
session.

### 10.4 Review gate

Run:

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~Audit|FullyQualifiedName~Enlistment"
rtk dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk git diff --check
```

Review:

- every Phase 9a shared sink case executes;
- accepted row survives a fresh provider;
- Duplicate/Conflict compare structured Integrity;
- no product reader appeared;
- no state-to-Audit reliability claim;
- probe proves transaction composition without implementing Outbox.
- Audit DTO/mapper/DML/Sink and `IAuditSink` registration were introduced only
  after Slice 6 RED tests.

---

## 11. Slice 7 — Crash, Response Loss, and NativeAOT

### 11.1 Independent crash worker

Create:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/
  Program.cs
  CrashWorkerOptions.cs
  CrashWorkerScenario.cs
  CrashWorkerProtocol.cs
```

Supported scenarios:

```text
crash-after-human-task-insert
commit-without-response
```

The worker communicates only bounded test protocol messages over stdout or a
test-owned local IPC channel. It never exposes production test hooks.

Use newline-delimited protocol tokens only:

```text
READY
HUMAN_TASK_INSERTED
SUSPENSION_COMMITTED
```

Connection strings and payloads are never written to stdout. The parent applies
a bounded timeout to every expected token and captures stderr for diagnostics.
For each worker, the parent allocates a bounded random test-run ID and the
worker sets Npgsql `Application Name` to
`crest_phase9b_crash_<testRunId>` through `NpgsqlConnectionStringBuilder`.
The name contains no tenant, instance, connection-string, or payload data.

For `crash-after-human-task-insert`:

1. start the real suspension transaction;
2. execute HumanTask INSERT;
3. confirm the command completed on PostgreSQL;
4. signal parent `HUMAN_TASK_INSERTED`;
5. wait without updating Workflow/committing;
6. parent terminates worker;
7. parent queries `pg_stat_activity` from its inspector connection until the
   exact worker `application_name` has no remaining backend, with a bounded
   timeout;
8. fresh provider observes pre-suspension Workflow, no task, no receipt.

The worker uses the production coordinator and production Stores directly
inside one transaction delegate, pauses after the real
`PostgreSqlHumanTaskInstanceStore.AddAsync` returns, and intentionally has not
called Workflow CAS or receipt Add. This avoids a production fault hook. The
separate provider composition test proves the real suspension committer invokes
the same ordered Store sequence.

For `commit-without-response`:

1. execute the real suspension committer with a parent-supplied stable
   operation identity;
2. after the coordinator returns from COMMIT, terminate with the protocol's
   dedicated “application response lost” exit code before writing a success
   response;
3. fresh provider reconciles the immutable receipt as committed.

This process case proves loss of the application response after a durable
commit. It does not pretend to inject TCP loss into Npgsql's COMMIT
acknowledgement; the provider-internal case in Slice 5 proves the typed
`RuntimeTransactionCommitUnknownException` mapping.

### 11.2 Crash tests

Create:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  Crash/PostgreSqlSuspensionCrashTests.cs
  Crash/PostgreSqlCommitResponseLossTests.cs
```

Cases:

```text
Crash_BetweenHumanTaskAndWorkflowWrite_ShouldExposeNoPartialSuspension
CommitResponseLoss_Should_PreserveCommittedStateForReconciliation
```

An exception/fault-injection unit test may supplement these but does not replace
the independent process.

### 11.3 NativeAOT Host

Create:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/
  Program.cs
  PostgreSqlRuntimeAotScenario.cs
  Json/PostgreSqlRuntimeAotStateJsonSerializerContext.cs
  State/MutableNestedAotState.cs
  Descriptors/AotWorkflowDescriptorProvider.cs
  Descriptors/AotHumanTaskDescriptorProvider.cs
```

Project properties:

```xml
<PublishAot>true</PublishAot>
<InvariantGlobalization>true</InvariantGlobalization>
```

The Host receives connection/schema through environment variables or explicit
arguments. It does not start Docker.

The fixture sets `ApplyMigrations=true` for the first provider and
`ApplyMigrations=false` for the fresh provider. The second provider must pass
schema validation without reapplying SQL.

Native scenario:

1. schema validation/migration;
2. generated Runtime State registration;
3. create pinned Workflow;
4. atomic HumanTask suspension;
5. dispose first service provider/data source;
6. create fresh provider;
7. resolve exact Pins;
8. restore exact mutable nested CLR state;
9. write AuditEnvelope;
10. retry and observe Duplicate;
11. print:

```text
CRESTCREATES_RUNTIME_PERSISTENCE_OK
CRESTCREATES_RUNTIME_STATE_AOT_OK
CRESTCREATES_DURABLE_AUDIT_OK
```

No reflection resolver, dynamic JSON, direct legacy Store, or mocked database is
allowed in the Host.

### 11.4 NativeAOT fixture

Create:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests/
  PostgreSqlRuntimeAotFixtureTests.cs
```

The xUnit fixture:

1. verifies that the authoritative execution platform is Linux x64;
2. starts `postgres:16-alpine`;
3. creates an isolated schema;
4. publishes:

   ```bash
   rtk dotnet publish \
     tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost \
     -c Release \
     -r linux-x64 \
     --self-contained true \
     -p:CrestCreatesPublishMode=aot \
     --disable-build-servers \
     -o <temp-output>
   ```

5. verifies the output executable is a native ELF artifact;
6. launches that original executable with the connection/schema;
7. asserts exit code and all three sentinels;
8. retains publish/run output on failure;
9. cleans only its test-owned temporary output and schema.

Linux x64 is the authoritative Phase 9b NativeAOT evidence platform. On a
non-Linux-x64 host, the fixture must use an explicitly configured Linux
container/CI runner or report `environment-blocked`. It must not attempt to
execute a Linux ELF directly, and a skipped/non-executed fixture is never
reported as green NativeAOT evidence.

Required tests:

```text
RegisteredStatePayload_ShouldRoundTripUnderNativeAot
PostgreSqlRuntimeFixture_ShouldPublishLinkAndRunNativeBinary
NativeBinary_ShouldExecuteSuspensionAndAuditRetryAgainstPostgreSql
NativeBinary_ShouldEmitRuntimePersistenceSentinel
NativeAotPublish_ShouldCompleteNativeLink
NativeBinary_ShouldValidateAndMigratePostgreSqlSchema
NativeBinary_ShouldRecoverAtomicSuspensionAfterFreshProvider
NativeBinary_ShouldResolveExactStructuredDescriptorPins
NativeBinary_ShouldReturnDuplicateForIdenticalAuditRetry
NativeBinary_ShouldHaveNoDynamicJsonOrReflectionFallback
PostgreSqlProvider_AfterVerifiedFixture_ShouldDeclareFullDurable
```

The fixture validates ELF magic bytes on the published executable, launches
that exact file directly (not `dotnet <dll>` and not a copied/rebuilt artifact),
and records its SHA-256 before and after execution. The static AOT guard scans
the Host/provider source for `DefaultJsonTypeInfoResolver`, dynamic JSON,
reflection serialization overloads, and forbidden suppressions; the runtime
sentinels remain required because a source scan alone is not evidence.

Only after both the independent crash case and the authoritative Linux x64
publish/link/run fixture pass does Slice 7 change
`PostgreSqlRuntimeProviderCapabilities` to:

```text
Tier = FullDurable
SupportsDatabaseNativeAotEvidence = true
```

`PostgreSqlProvider_AfterVerifiedFixture_ShouldDeclareFullDurable` freezes that
final declaration. If the fixture is skipped or environment-blocked, the
provider retains its pre-evidence Full Semantic declaration and Slice 7 cannot
close.

### 11.5 Review gate

Run:

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~Crash|FullyQualifiedName~ResponseLoss"
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests \
  -c Release
rtk git diff --check
```

Review:

- crash evidence crosses a real process boundary;
- response loss is not simulated as rollback;
- original native executable runs against PostgreSQL;
- all three sentinels are emitted;
- warnings are reviewed rather than suppressed;
- no AOT claim is inherited by unrelated EF/integration providers.
- the Provider advertises Full Durable only after crash and native fixture
  evidence pass.

---

## 12. Slice 8 — Repository Closure and Evidence

### 12.1 Focused regression matrix

Run:

```bash
rtk dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.Tests
rtk dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests -c Release
rtk dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
rtk dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
rtk dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Abstractions.Tests
rtk dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

Run Procurement regression because its golden path composes Workflow/HumanTask
and generated JSON:

```bash
rtk dotnet test samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.AcceptanceTests
rtk dotnet test samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.AotFixture.Tests -c Release
```

### 12.2 Solution builds

```bash
rtk dotnet build solutions/CrestCreates.Runtime.slnx -c Release
rtk dotnet build CrestCreates.slnx -c Release
```

Then, when the environment supports Docker-backed repository suites:

```bash
rtk dotnet test CrestCreates.slnx -c Release
```

Report environment-blocked external suites separately. Do not convert them into
false green claims.

### 12.3 Compiled architecture evidence and supplemental static audit

First run the compiled public-surface and Roslyn semantic architecture tests
from Slices 1, 2, and 4. These are authoritative for provider type leakage,
reflection JSON overload binding, aliases, and fully qualified invocations.
Then run the following `rg` commands as supplemental repository hygiene:

Run:

```bash
rtk rg -n "GetAsync\\(string|GetByIdAsync\\(string|GetByWaitingHumanTaskId|ConcurrencyStamp" \
  src/Runtime tests/Runtime
rtk rg -n "Dictionary<string, object\\?>|object\\? (Input|Output|Result)" \
  src/Runtime/Workflow src/Runtime/HumanTask
rtk rg -n "DefaultJsonTypeInfoResolver|EnableDynamicJson|JsonSerializer\\.Serialize\\([^,]+\\)" \
  src/Runtime/Persistence src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
rtk rg -n "DbContext|DbTransaction|Npgsql(Connection|Transaction|Command|Exception)" \
  src/Runtime/Persistence/CrestCreates.Runtime.Persistence.Abstractions \
  src/Runtime/Workflow/CrestCreates.Workflow.Abstractions \
  src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions
rtk git diff --check
```

Every hit is classified. Production-mainline hits for the forbidden patterns
block closure.

### 12.4 Documentation

Modify:

```text
memory.md
docs/Feature/RuntimePersistence/arch-design.md
docs/Feature/RuntimePersistence/usage-guide.md
```

Document:

- provider selection;
- InMemory Full Semantic versus PostgreSQL Full Durable tier;
- application Runtime State registration;
- exact Pin/lazy recovery requirement;
- migrations/startup options;
- #25 delivery limitation;
- NativeAOT verified scope.

Update `memory.md` only with actual evidence counts and support tier.

### 12.5 Issue evidence

Post an Issue #24 completion comment containing:

- implemented commit/PR links;
- focused test counts;
- crash/response-loss evidence;
- migration cases;
- native publish/link/run command and sentinels;
- explicit statement that #25 reliable delivery and #70 reconciliation remain
  out of scope.

Do not close #24 until all Exit Criteria pass.

---

## 13. Requirement-to-Test Ledger

### 13.1 Case Matrix

| Case | Primary executable test | Owning Slice / project |
|---|---|---|
| H01 | `WorkflowAdd_WithRegisteredState_ShouldPersistExactPinAndRevisionOne` | S3/S5 · shared Store cases |
| H02 | `SuspensionCommit_Should_AtomicallyPersistWorkflowAndHumanTask` | S3/S5 · provider composition |
| H03 | `Restart_WithMatchingDescriptorPin_ShouldResumeWorkflow` | S5 · PostgreSQL recovery |
| H04 | `Restart_CompleteHumanTask_ShouldPersistCompletionAndAllowContinuation` | S5 · PostgreSQL recovery |
| H05 | `Restart_WithMatchingDescriptorPin_ShouldResumeWorkflow` | S5 · PostgreSQL recovery |
| H06 | `DescriptorSnapshotStore_ShouldSurviveRestart` | S5 · PostgreSQL Snapshot |
| H07 | `AcceptedAuditEnvelope_ShouldSurviveRestart` | S6 · PostgreSQL Audit |
| H08 | `Migration_ShouldCreateSchemaFromEmptyDatabase` | S4 · PostgreSQL migration |
| B01 | `TenantScopedLookup_ShouldNotReturnOtherTenantInstance` | S3/S5 · shared Store cases |
| B02 | `HostAndTenantSameId_ShouldRemainDistinct`; PostgreSQL constraint companion `SameWaitingHumanTaskIdAcrossTenants_ShouldRemainDistinct` | S3/S5 · shared Store + PostgreSQL constraint cases |
| B03 | `RuntimeTenantScope_Null_ShouldMeanExactHostNotWildcard` | S1 · Runtime Persistence.Tests |
| B04 | `ConcurrentTransition_FromSameRevision_ShouldAllowOneWinner` | S3/S5 · shared Store cases |
| B05 | `IdenticalSuspensionRetry_ShouldNotCreateSecondHumanTask` | S3/S5 · suspension/receipt |
| B06 | `ConflictingSuspensionOperationRetry_ShouldFailClosed` | S3/S5 · suspension/receipt |
| B07 | `RegisteredStatePayload_ShouldPreserveStableTypeIdAcrossClrRename` | S2 · Runtime State |
| B08 | `TypedNullStatePayload_ShouldRoundTripWithTypeId` | S2 · Runtime State |
| B09 | `DescriptorSnapshotStore_ShouldReturnDuplicateForIdenticalContent` | S3/S5 · shared Snapshot cases |
| B10 | `IdenticalAuditRetry_ShouldReturnDuplicate` | S6 · PostgreSQL Audit |
| B11 | `NestedRuntimeTransaction_ShouldJoinOuterCommit` | S3/S5 · shared transaction cases |
| B12 | `QueryResults_ShouldHaveDeterministicOrder` | S3/S5 · shared Store cases |
| B13 | `HostStartup_WithUnresolvedDormantPin_ShouldSucceedSchemaValidation`; `Execution_WithUnresolvedDormantPin_ShouldFailClosed` | S5 · PostgreSQL recovery |
| F01 | `Crash_BetweenHumanTaskAndWorkflowWrite_ShouldExposeNoPartialSuspension` | S7 · crash worker |
| F02 | `Restart_WithMissingWorkflowDescriptor_ShouldFailClosed` | S5 · PostgreSQL recovery |
| F03 | `Restart_WithMissingHumanTaskDescriptor_ShouldFailClosed` | S5 · PostgreSQL recovery |
| F04 | `Restart_WithMismatchedDefinitionHash_ShouldFailClosed` | S5 · PostgreSQL recovery |
| F05 | `Restart_WithMismatchedContractHash_ShouldFailClosed` | S5 · PostgreSQL recovery |
| F06 | `Restart_WithMismatchedHashProfile_ShouldFailClosed` | S5 · PostgreSQL recovery |
| F07 | `UnregisteredStatePayload_ShouldFailBeforeTransaction` | S2 · Runtime State |
| F08 | `UnknownStateTypeId_OnRestart_ShouldFailClosedWithoutMutation` | S5 · PostgreSQL recovery |
| F09 | `MismatchedStateSchemaRef_ShouldFailClosed` | S5 · PostgreSQL recovery |
| F10 | `CrossTenantWorkflowHumanTaskCorrelation_ShouldFail` | S3/S5 · suspension |
| F11 | `StaleWorkflowRevision_ShouldRollbackInsertedHumanTask` | S3/S5 · suspension |
| F12 | `CommitResponseLoss_Should_PreserveCommittedStateForReconciliation` | S7 · response-loss worker |
| F13 | `DescriptorSnapshotStore_ShouldRejectSameIdentityDifferentContent` | S3/S5 · shared Snapshot cases |
| F14 | `ConflictingAuditRetry_ShouldReturnConflict` | S6 · PostgreSQL Audit |
| F15 | `Migration_ShouldRejectChangedAppliedChecksum` | S4 · PostgreSQL migration |
| F16 | `Migration_ShouldRejectUnknownNewerSchema` | S4 · PostgreSQL migration |
| F17 | `ConcurrentUseOfAmbientSession_ShouldFailClosed` | S3/S5 · shared transaction cases |
| F18 | `OversizedStatePayload_ShouldFailBeforeSql` | S2 · Runtime State |
| C01 | `EnlistedProbeAndState_ShouldCommitTogether` | S6 · PostgreSQL composition |
| C02 | `EnlistedProbeAndState_ShouldRollbackTogether` | S6 · PostgreSQL composition |
| C03 | `HumanTaskCompletionCommitted_BeforeContinuationDelivery_ShouldRemainDurable` | S5 · PostgreSQL recovery |
| C04 | `StateCommit_ShouldNotClaimAuditDeliveryGuarantee` | S6 · boundary/composition |
| C05 | `InMemoryRuntimeProvider_ShouldPassAtomicSuspensionContractCases` | S3 · InMemory provider |
| C06 | `FreshProvider_ShouldRecoverSuspendedWorkflowWithoutProviderObjectLeak` | S5 · PostgreSQL recovery |
| A01 | `NativeAotPublish_ShouldCompleteNativeLink` | S7 · AOT fixture |
| A02 | `NativeBinary_ShouldValidateAndMigratePostgreSqlSchema` | S7 · AOT Host |
| A03 | `RegisteredStatePayload_ShouldRoundTripUnderNativeAot` | S7 · AOT Host |
| A04 | `NativeBinary_ShouldRecoverAtomicSuspensionAfterFreshProvider` | S7 · AOT Host |
| A05 | `NativeBinary_ShouldResolveExactStructuredDescriptorPins` | S7 · AOT Host |
| A06 | `NativeBinary_ShouldReturnDuplicateForIdenticalAuditRetry` | S7 · AOT Host |
| A07 | `NativeBinary_ShouldHaveNoDynamicJsonOrReflectionFallback` plus runtime sentinel tests | S7 · static guard + AOT Host |

### 13.2 Provider-tier ledger

| Contract family | InMemory | PostgreSQL |
|---|---:|---:|
| Add/CAS Store cases | required | required |
| Tenant/host isolation | required | required |
| Atomic suspension/rollback | required | required |
| Nested transaction semantics | required | required |
| Deep/detached state snapshots | required | required |
| Deterministic ordering | required | required |
| Process restart recovery | no claim | required |
| Independent crash evidence | no claim | required |
| Migrations/schema compatibility | no claim | required |
| Database NativeAOT execution | no claim | required |
| Durable Audit acceptance | no claim; existing Phase 9a in-memory contract remains | required |

There is no “where applicable” for semantic cases.

---

## 14. Review Gates

### Gate A — Boundary

Pass when:

- Runtime Persistence Abstractions has no Workflow/HumanTask/provider edge;
- providers reference abstractions only;
- no provider type appears in public Runtime contracts;
- old Store/exception mainlines are retired.

### Gate B — Invariants

Pass when:

- INV-01 through INV-19 each have an executable primary test;
- InMemory and PostgreSQL both pass atomic suspension and CAS;
- Pin/state failures preserve revision/status;
- no reliable-delivery claim enters Phase 9b.

### Gate C — Case Matrix

Pass when:

- every H/B/F/C/A row maps to a concrete test;
- B13 proves lazy dormant-Pin behavior;
- crash and response-loss are distinct cases.

### Gate D — NativeAOT

Pass when:

- publish uses `CrestCreatesPublishMode=aot`;
- native link completes;
- the original ELF artifact runs;
- the real PostgreSQL provider executes;
- all sentinels appear;
- no unsupported provider inherits the result.

### Gate E — Mainline uniqueness

Pass when searches show:

- no executor-owned HumanTask persistence;
- no tenantless Store lookup;
- no upsert `SaveAsync`;
- no durable `object?`;
- no Snapshot-to-executable conversion;
- no InMemory provider branch in Workflow Runner.

---

## 15. Exit Criteria

Implementation is complete only when:

1. The Spec remains `APPROVED` and all Plan Slices are green.
2. Shared semantic cases pass for both InMemory and PostgreSQL.
3. PostgreSQL-only restart/migration/crash/NativeAOT cases pass.
4. A real worker crash after HumanTask INSERT exposes no partial suspension.
5. COMMIT response loss reconciles through the immutable receipt.
6. Matching Pins resume; missing/mismatched/dormant Pins fail lazily and leave
   durable state unchanged.
7. Registered state restores exact CLR semantics; unregistered/unknown state
   fails closed.
8. Host/two-tenant same IDs remain isolated by API and SQL constraints.
9. Same-revision concurrency has exactly one winner.
10. Snapshot Store remains evidence-only and immutable.
11. PostgreSQL `IAuditSink` passes all Phase 9a shared cases and restart cases.
12. #25 enlistment probe commits/rolls back with state without provider leakage.
13. The original native binary executes PostgreSQL State, Pin, suspension, and
    Audit retry paths and emits all sentinels.
14. Boundary/focused/Runtime solution/canonical build gates pass.
15. Documentation and `memory.md` report only verified support levels.
16. Issue #24 contains the superseding design authority and final evidence.

---

## 16. Plan Review Finding Closure

This ledger records where each Plan-review correction is closed without
reopening the approved Spec.

| Finding | Closure in this Plan |
|---|---|
| P-01 Dependency graph | §3.1 and §4.3 fix exact direct references. Testing, InMemory, and PostgreSQL explicitly reject `CrestCreates.Runtime.Persistence`; Host separately calls `AddRuntimePersistence()` and one provider registration. Slice 1/3/4 architecture tests enforce the graph. |
| P-02 Slice 1 Store cases | §5.2 creates only assertion/fixture/marker infrastructure. §6.5 creates real Store/State/Snapshot/transaction drivers and cases after final contracts compile. Slices 3/5 add wrappers only. |
| P-03 temporary Store advertisement | §6.5–§6.6 remove default Workflow/HumanTask Store registration in Slice 2 and explicitly wire capability-free compile adapters. Slice 2 is not a supported provider boundary. §7.3 replaces the explicit adapters with Full Semantic InMemory. |
| P-04 generic not-found leak | §5.4–§5.5 omit `RuntimeEntityNotFoundException`, define null/CAS/Add semantics, and keep Company Certification failures sample-owned with no Runtime Persistence dependency. |
| P-05 Runtime State ownership | §5.4 fixes only `IRuntimeStateContractBuilder` in Abstractions. §6.2 owns `RuntimeStateContractBuilder`, `TypedRuntimeStateRegistration<T>`, and the registry in concrete Runtime Persistence, validates exact Schema refs through `ISchemaRegistry`, and names all startup/recovery failures. |
| P-06 migration history ownership | §8.1 and §8.3 give schema/history bootstrap one owner, validate exact history shape, and remove the table from V001 in §8.4. |
| P-07 unique-conflict classification | §5.4 defines provider-neutral codes; §9.3 maps exact instance, receipt, active-step, and reciprocal-waiting constraints separately and prevents provider names from escaping. |
| P-08 detached transition scope | §6.5 fixes the Runtime-wide rule; §7.4–§7.5 add detached builders and failure tests for Workflow failure/completion/continuation and HumanTask completion/cancel/dispatch-failure. |
| N-01 Npgsql patch | §4.3 locks direct Npgsql `10.0.3` and requires resolved-version evidence in Slice 4. |
| N-02 AOT platform | §11.4 makes Linux x64 authoritative; non-Linux execution is container/CI-backed or environment-blocked, never false green. |
| N-03 crash backend identity | §11.1 assigns a sanitized unique Npgsql Application Name and waits for the exact `pg_stat_activity` backend to disappear. |
| N-04 regex evidence | §4.6 and §12.3 make compiled public-API and Roslyn semantic tests authoritative; `rg` is supplemental only. |

Second-round closure:

| Finding | Closure in this Plan |
|---|---|
| R2-01 Slice production ownership | §8.2/§8.5 make Slice 4 a non-selectable kernel with migration SQL only. §9.2–§9.3 introduce the four Store DTO/mappers/DML/Stores and public Full Durable composition after Store RED. §10.2 introduces Audit DTO/mapper/DML/Sink and `IAuditSink` registration after Audit RED. |
| R2-02 validation-only mutation | §8.1/§8.3 require `ValidationOnly_OnEmptyDatabase_ShouldFailWithoutCreatingSchemaOrHistory`; `ApplyMigrations=false` executes zero DDL and acquires no apply lock, while only the true branch bootstraps/applies under advisory lock. |
| R2-03 tenant-scoped waiting uniqueness | §8.4 fixes the exact three-column partial unique index. §9.1 proves both Store visibility and database constraints with `HostAndTenantSameId_ShouldRemainDistinct` and `SameWaitingHumanTaskIdAcrossTenants_ShouldRemainDistinct`. |
| R2-04 SQLite cutover timing | §5.5 limits Slice 1 to exception-authority migration under old Store interfaces; §6.6 owns any necessary Add/CAS compile migration and same-Slice retirement. |
| N2-01 solution timing | §4.4 assigns every new project to its owning Slice and corrects the Slice 1 count to four. |
| N2-02 boundary-test naming | §3.3 uses `WorkflowAndHumanTaskAbstractions_ShouldNotReferencePersistenceImplementationsOrProviders`. |
| N2-03 suspension time semantics | §7.4–§7.5 require Suspended to leave `CompletedAt` null and terminal Completed/Failed to set it. |

Third-round closure:

| Finding | Closure in this Plan |
|---|---|
| R3-01 RuntimeStateBag ownership | §5.4 owns `RuntimeStateBag` in Persistence Abstractions; §6.2 retains only generated JSON/registration/registry implementation in concrete Runtime Persistence. §3.3/§5.1 prohibit Workflow/HumanTask concrete Runtime references. |
| R3-02 evidence-gated Full Durable | §9.3 registers PostgreSQL as Full Semantic with durable behavior flags but no database-AOT evidence. §11.4 changes the tier to Full Durable only after crash and authoritative native fixture evidence pass. |
| R3-03 response-loss authority | §9.1 keeps only COMMIT-acknowledgement and Receipt reconciliation cases. §11.2 is the sole owner of `CommitResponseLoss_Should_PreserveCommittedStateForReconciliation`; F12 remains mapped only to Slice 7. |

These closure rows do not reinterpret the approved Boundary, Invariants, or
Case Matrix.

---

## 17. First Implementation Command Sequence

When implementation is authorized, begin with Slice 1 only:

```bash
rtk --version
rtk dotnet --info
rtk git status --short
rtk git branch --show-current
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~RuntimePersistence"
```

The branch check must print
`feature/phase-9b-durable-persistence-foundation-24`; the branch already exists
and must not be recreated.

Then create the failing architecture/contract tests before any provider project
or SQL implementation.
