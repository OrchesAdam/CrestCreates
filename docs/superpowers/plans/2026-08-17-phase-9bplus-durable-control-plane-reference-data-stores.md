# Phase 9b+ Durable Control Plane and Reference Data Stores Implementation Plan

> Implement Issue #69 through ordered Case-first TDD slices. The approved R3
> Spec is normative. This Plan fixes project placement, semantic-helper
> ownership, migration/SQL files, DTO/AOT closure, DI composition, test-driver
> shape, crash/restart mechanics, and Slice gates. It does not reopen the
> frozen design.

**Goal:** Add explicit opt-in direct-Npgsql durable providers for
`IDescriptorDraftStore`, `IOrganizationStore`, and
`IDataPermissionScopeRuleStore`, while preserving identical InMemory semantics,
Draft validator ownership, independent top-level commits, restart durability,
and real PostgreSQL NativeAOT execution.

**Spec:**
`docs/superpowers/specs/2026-08-17-phase-9bplus-durable-control-plane-reference-data-stores-design.md`

**Issue:** #69

**Recommended branch:**
`codex/issue-69-durable-control-plane-reference-data-stores`

**Baseline inspected:** `master` at `5ee96ec8`; V010 is the migration tail.

**Spec status:** APPROVED / FROZEN — R3

**Plan status:** IMPLEMENTATION HANDOFF READY — R3 REVIEW FINDINGS ADDRESSED

**Plan review revision:** R4 closes the final shared-kit accessibility, D09
ordering, F06 ownership, and C13 manifest-scope findings without changing the
frozen Spec

```text
Durable provider:        existing CrestCreates.Runtime.Persistence.PostgreSql
Migration:               V011_control_plane_reference_data_stores
Registration:            explicit base-first feature opt-in
Write boundary:          existing ExecuteTopLevelAsync
Concurrency:             blind atomic replacement; no OCC
Exact time:              created_at_utc_ticks bigint
Snapshots:               generated JSON; recursive provider DTO unions
Organization FKs:        none
Final evidence:          Case × Surface × Variant × EvidenceVectorKey × Runner ledger
NativeAOT:               publish + native link + real PostgreSQL execution
```

---

## 1. Execution Rules

### 1.1 Session preflight

Before changing a Slice:

```bash
git status --short --branch
git rev-parse HEAD
dotnet --info
```

Read the frozen Spec, this Plan, the previous Slice handoff, and the current
diff. If V011 already exists or V010 is no longer the migration tail, stop and
reconcile the migration number; never edit or renumber V001–V010.

### 1.2 TDD and handoff discipline

- Activate only the cases owned by the current Slice.
- A Red must fail for the missing behavior, not fixture/DI failure.
- Future manifest tuples stay inactive; they are not skipped tests or evidence.
- Turn the focused Red Green with the smallest mainline implementation.
- Run the Slice regression set, changed-project builds, boundary tests when
  dependencies change, and `git diff --check`.
- End each Slice with one reviewable commit and a handoff containing: commit,
  files, Red evidence, Green commands/counts, active evidence tuples, and zero
  unresolved review findings.
- Do not mark implementation complete or NativeAOT-verified before Slice 11
  runs the newly published native binary.

### 1.3 Non-negotiable boundaries

- Do not add `IDataPermissionScopeStore`, PostgreSQL `IDraftStore`, activation
  persistence, FK/cascade semantics, revision/xmin OCC, ORM, another DataSource,
  another migration runner, reflection JSON, or Runtime recovery participants.
- Do not copy `IDescriptorDraftValidator`, hierarchy traversal, primary
  selection, or DataPermission candidate priority into the Provider.
- Do not add persistence-only polymorphism metadata to Domain contracts.
- Do not use `timestamptz` for exact Draft filters or Organization ordering.
- Do not emit Accountability from the selected Stores.
- Do not auto-retry commit-unknown writes.

### 1.4 Shared semantic ownership

Provider-neutral semantics needed by both InMemory and PostgreSQL live as
`internal` helpers in the owning abstraction assembly, with narrow friend
access for the owning implementation and PostgreSQL Provider:

```text
CrestCreates.DescriptorDraft.Abstractions
    DescriptorDraftStoreSemantics
    friends: CrestCreates.DescriptorDraft,
             CrestCreates.Runtime.Persistence.PostgreSql

CrestCreates.Organization.Abstractions
    OrganizationStoreSemantics
    OrganizationScopedKey/ordering values used across providers
    DataPermissionRuleKey/Match/ResolutionPlan
    friends: CrestCreates.Organization,
             CrestCreates.Runtime.Persistence.PostgreSql
```

The public Store interfaces do not change. The internal helpers contain only
validation, typed identities, total comparers, and request-relative Rule
candidate generation. SQL, JSON DTOs, migrations, and provider failures remain
in the PostgreSQL project.

---

## 2. Ordered Delivery Map

| Slice | Deliverable | Activated cases |
|---|---|---|
| 1 | Runner-free contract kit, typed manifest, project graph | structural ledger only |
| 2 | Draft shared semantics, payload snapshot completeness, and InMemory parity | D01–D08, D11–D13, V01/V03–V05 Draft tuples |
| 3 | Organization typed identity/order/hierarchy and InMemory parity | O01–O14, O19–O22, Organization V01/V03/V05 tuples |
| 4 | DataPermission typed key/candidate semantics and InMemory parity | P01–P07, P10–P12, Rule V01–V03/V05 tuples |
| 5 | Provider project references, recursive DTO codec, generated JSON closure | D01, C13, DTO representation Reds |
| 6 | V011, complete schema/index-collation manifest, base-provider marker | C01–C03, C07–C08, C10–C11; P08 ARCH |
| 7 | PostgreSQL Descriptor Draft Store, excluding provider reconstruction | D01–D08, D11–D13; Draft V01/V03–V05; Draft F01/F02/F06/F08/F09 |
| 8 | PostgreSQL Organization Store, excluding process restart | O01–O15, O19–O22; Organization V01/V03/V05; Organization F01/F02/F06/F08/F09 |
| 9 | PostgreSQL Rule Store, authority fail-closed, feature DI, and Draft provider reconstruction | P01–P07, P10–P13; Rule V01–V03/V05; Rule F01/F02/F06/F08; C09/C14/C15; D09 |
| 10 | Process restart, crash, commit-unknown, and failure-matrix closure | D10, O16–O18, P09, F03–F05/F07; regression rerun of already-owned F06 tuples |
| 11 | NativeAOT, architecture/evidence closure, canonical regression | C04–C06, C12–C13 and every remaining tuple |

Slices are sequential. Do not begin a later Slice with an activated Red,
unreviewed shared-hotspot change, or incomplete evidence tuple from the current
Slice.

---

## 3. Final Project and File Layout

### 3.1 Existing projects modified

```text
src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/
  CrestCreates.DescriptorDraft.Abstractions.csproj
  DescriptorDraftStoreSemantics.cs                         new

src/Metadata/Draft/CrestCreates.DescriptorDraft/
  DescriptorDraftPayloadSupport.cs                          new
  InMemoryDescriptorDraftStore.cs
  SchemaDescriptorDraftPayload.cs

src/Framework/Modules/CrestCreates.Organization.Abstractions/
  CrestCreates.Organization.Abstractions.csproj
  OrganizationScopedKey.cs                                 new
  OrganizationStoreSemantics.cs                            new
  DataPermissionRuleSemantics.cs                            new

src/Framework/Modules/CrestCreates.Organization/
  InMemoryOrganizationStore.cs
  DefaultOrganizationHierarchyService.cs
  DefaultOrganizationIdentityService.cs
  InMemoryDataPermissionScopeRuleStore.cs

src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  CrestCreates.Runtime.Persistence.PostgreSql.csproj
  PostgreSqlRuntimeMigrationRunner.cs
  PostgreSqlRuntimePersistenceServiceCollectionExtensions.cs
  PostgreSqlRuntimeTransactionCoordinator.cs
  PostgreSqlRuntimeTestHooks.cs
  PostgreSqlRuntimeProviderRegistrationMarker.cs            new
  PostgreSqlControlPlaneReferenceDataPersistenceDtos.cs     new
  PostgreSqlControlPlaneReferenceDataJsonSerializerContext.cs new
  PostgreSqlDescriptorDraftSnapshotCodec.cs                 new
  PostgreSqlOrganizationSnapshotCodec.cs                    new
  PostgreSqlDescriptorDraftStore.cs                         new
  PostgreSqlOrganizationStore.cs                            new
  PostgreSqlDataPermissionScopeRuleStore.cs                 new
  PostgreSqlControlPlaneReferenceDataStoreSupport.cs        new
  PostgreSqlControlPlaneReferenceDataPersistenceServiceCollectionExtensions.cs new
```

The Provider project adds references to:

```text
CrestCreates.DescriptorDraft.Abstractions
CrestCreates.DescriptorDraft          # six concrete payload families
CrestCreates.Organization.Abstractions
```

It must not reference `CrestCreates.Organization`, Agent Control Plane,
`CrestCreates.Draft`, Platform, Web, EF Core, or another persistence provider.

### 3.2 New runner-free shared project

```text
tests/Shared/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing/
  CrestCreates.ControlPlane.ReferenceData.Persistence.Testing.csproj
  TestingBoundaryMarker.cs
  Assertions/ControlPlaneReferenceDataContractAssertions.cs
  Drivers/IDescriptorDraftStoreContractDriver.cs
  Drivers/IOrganizationStoreContractDriver.cs
  Drivers/IDataPermissionScopeRuleStoreContractDriver.cs
  Drivers/IDurableStoreContractDriver.cs
  Fixtures/ControlPlaneReferenceDataContractFixtures.cs
  Fixtures/DescriptorPayloadObservation.cs
  Cases/DescriptorDraftStoreContractCases.cs
  Cases/OrganizationStoreContractCases.cs
  Cases/DataPermissionScopeRuleStoreContractCases.cs
  Manifest/ControlPlaneReferenceDataCaseManifest.cs
  Manifest/ControlPlaneReferenceDataSpecTestSkeleton.cs
```

Project rules:

```xml
<IsTestProject>false</IsTestProject>
<IsPackable>false</IsPackable>
```

It references only `CrestCreates.DescriptorDraft.Abstractions` and
`CrestCreates.Organization.Abstractions`. Payload construction and invocation
of the real Draft validator are driver responsibilities, so the Organization
runner does not acquire a transitive dependency on the DescriptorDraft
implementation. It contains no xUnit, FluentAssertions, Npgsql, Testcontainers,
or concrete Store implementation.

Add the project to `CrestCreates.slnx`, `solutions/CrestCreates.All.slnx`, and
the relevant Runtime/Metadata/Framework solution views.

Driver intent is explicit:

```csharp
public interface IDescriptorDraftStoreContractDriver
{
    IDescriptorDraftStore Store { get; }
    IDescriptorDraftValidator Validator { get; }
    DescriptorDraft CreatePayloadVariant(DescriptorPayloadVariant variant);
    DescriptorPayloadObservation ObservePayload(
        DescriptorDraft draft,
        DescriptorPayloadVariant variant);
    DescriptorDraft CreateValidatorOwnedInvalid(
        DraftValidatorOwnedInvalidVariant variant);
    ValueTask ResetAsync();
}

public interface IOrganizationStoreContractDriver
{
    IOrganizationStore Store { get; }
    IOrganizationHierarchyService CreateHierarchyService();
    IOrganizationIdentityService CreateIdentityService();
    ValueTask ResetAsync();
}

public interface IDataPermissionScopeRuleStoreContractDriver
{
    IDataPermissionScopeRuleStore Store { get; }
    ValueTask ResetAsync();
}

public interface IDurableStoreContractDriver
{
    ValueTask ReconstructProviderAsync();
    ValueTask<ProcessScenarioResult> RunProcessScenarioAsync(
        SaveSurface surface, DurableScenario scenario);
}
```

The shared project defines its own provider-neutral variant enums and result
records. `DescriptorPayloadObservation` is a provider-neutral immutable value
tree owned by the shared kit, not a Domain descriptor instance and not a JSON
string. It contains the payload variant plus an ordered list of typed
leaves. Use this shape (factory methods enforce exactly one value arm):

```csharp
public sealed record DescriptorPayloadObservation(
    DescriptorPayloadVariant Variant,
    ImmutableArray<DescriptorPayloadObservationLeaf> Leaves);

public sealed record DescriptorPayloadObservationLeaf(
    string Path,
    ObservationValueKind Kind,
    string? Text,
    long? Integer,
    decimal? Decimal,
    bool? Boolean);

public enum ObservationValueKind
{
    Null,
    Text,
    Integer,
    Decimal,
    Boolean,
    EnumUnderlyingValue,
    Ticks
}
```

`ObservationValueKind` distinguishes Null, Text, Integer, Decimal, Boolean,
EnumUnderlyingValue, and Ticks; paths flatten lists with numeric indexes and
dictionaries with ordinally sorted escaped keys. Null has no populated arm;
each non-null leaf has exactly one populated arm. The shared fixture owns the
expected tree;
drivers only construct concrete payloads and project the returned concrete
payload into the observation. A driver must never derive the expected tree
from the object it observes. InMemory drivers do not implement process
durability; PostgreSQL drivers compose the durable capability at the runner
fixture layer.

Because three external runner assemblies implement these contracts, every type
appearing in a cross-assembly driver signature is `public`: the four driver
interfaces, `DescriptorPayloadObservation`,
`DescriptorPayloadObservationLeaf`, `ObservationValueKind`, and all shared-kit
variant/result/scenario types used by their members. Assertion implementations,
fixture builders, manifest storage, and other helpers remain `internal`. Do not
replace this public testing contract with runner-specific `InternalsVisibleTo`
friend declarations.

### 3.3 Runner files

```text
tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/
  CrestCreates.DescriptorDraft.Tests.csproj
  Persistence/InMemoryDescriptorDraftStoreContractDriver.cs new
  Persistence/DescriptorDraftStoreContractTests.cs           new

tests/Framework/Modules/CrestCreates.Organization.Tests/
  CrestCreates.Organization.Tests.csproj
  Persistence/InMemoryOrganizationStoreContractDriver.cs     new
  Persistence/InMemoryDataPermissionScopeRuleStoreContractDriver.cs new
  Persistence/OrganizationStoreContractTests.cs               new
  Persistence/DataPermissionScopeRuleStoreContractTests.cs    new

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj
  Fixtures/PostgreSqlDescriptorDraftStoreContractDriver.cs    new
  Fixtures/PostgreSqlOrganizationStoreContractDriver.cs       new
  Fixtures/PostgreSqlDataPermissionScopeRuleStoreContractDriver.cs new
  PostgreSqlControlPlaneReferenceDataContractTests.cs         new
  PostgreSqlControlPlaneReferenceDataMigrationTests.cs        new
  PostgreSqlControlPlaneReferenceDataCompositionTests.cs      new
  PostgreSqlControlPlaneReferenceDataConcurrencyTests.cs      new
  PostgreSqlControlPlaneReferenceDataFailureTests.cs          new
  PostgreSqlControlPlaneReferenceDataCorruptionTests.cs       new
  PostgreSqlControlPlaneReferenceDataRestartTests.cs          new
  PostgreSqlControlPlaneReferenceDataCrashTests.cs            new
  Fixtures/PostgreSqlCrashWorkerPath.cs                       new
  PostgreSqlRuntimeCrashTests.cs
  PostgreSqlAgentToolPreDispatchCrashTests.cs
  PostgreSqlAgentMemoryCrashTests.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/
  CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker.csproj
  ControlPlaneReferenceDataCrashScenarios.cs                  new
  Program.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/
  CrestCreates.Runtime.Persistence.PostgreSql.AotHost.csproj
  ControlPlaneReferenceDataAotScenario.cs                     new
  Program.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests/
  PostgreSqlRuntimeAotFixtureTests.cs
```

Project-reference updates:

- DescriptorDraft and Organization test projects reference the shared kit.
- PostgreSQL Tests reference the shared kit, DescriptorDraft implementation,
  and Organization implementation for real validator/hierarchy/identity runners.
- PostgreSQL Tests reference CrashWorker with
  `ReferenceOutputAssembly="false"` so every Debug/Release test build first
  builds the matching worker artifact without adding its assembly to test code.
- CrashWorker references DescriptorDraft implementation and Organization
  abstractions for surface-specific write models.
- AotHost references DescriptorDraft and Organization implementations for the
  real native scenario; the AOT Fixture remains an external publish/run test.

---

## 4. Slice 1 — Contract Kit and Inactive Evidence Oracle

### Red/structural work

1. Create the shared project and the three independent drivers.
2. Encode all 77 Case IDs and every closed dimension exactly as frozen in Spec
   §14.6. A manifest row carries CaseId, Surface, Variant,
   `EvidenceVectorKey`, Runner, Slice, and activation state. A variant with no
   internal expansion uses the single key `Default`; expanded variants use the
   exact atomic keys frozen in Appendix E.5.
3. Add a skeleton parser/guard which proves unique IDs, valid dimension members,
   and required Cartesian expansion without executing future behavior.
4. Add project/solution references and boundary tests proving the shared kit is
   runner/provider-free and each domain driver exposes only its own Store.

Do not add deliberately failing xUnit wrappers for later Slices.

### Gate

```bash
dotnet build tests/Shared/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
dotnet build CrestCreates.slnx
git diff --check
```

---

## 5. Slice 2 — Descriptor Draft InMemory Contract

### Red

Activate D01–D08/D11–D13 for the InMemory Draft runner: payload completeness,
validation ownership, cancellation, snapshots, filters, exact ticks, offsets,
replacement, isolation, and ordinal order. D01 covers all eight payload/nested
variants here rather than waiting for the Provider codec; its Schema fixture
must set `SchemaFieldDescriptor.ObjectSchema` to a non-null versioned ref. D08
must use the real `DefaultDescriptorDraftValidator` after Store round trip and
cover both supported-payload and all defined-non-payload kind mismatches.

D01 is exact observation equality, not CLR-kind/descriptor-ID equality:

```text
shared expected DescriptorPayloadObservation
    -> driver creates concrete payload
    -> Save/Get
    -> driver ObservePayload(returnedDraft, variant)
    -> exact ordered typed-leaf equality with the shared expected observation
```

The frozen expected observations enumerate every field path listed in Appendix
C.3. The Schema observation must include all four
`Fields[0].ObjectSchema.{Id,Version,SelectionMode,ExpectedContractHash}` leaves;
collection/dictionary order and explicit nulls are observable. A missing leaf,
extra leaf, wrong null, wrong value kind, or wrong value fails D01.

### Green

1. Add `DescriptorDraftStoreSemantics` for pre-cancellation,
   representation-only validation, exact tick predicates, and ordinal order.
   Closed enum validation uses explicit generated/static switches, not runtime
   reflection or `Enum.IsDefined`.
2. Preserve blank DraftId and null/blank DescriptorId/AuthorId; reject only
   non-representable row/codec inputs and undefined closed enums.
3. Update `InMemoryDescriptorDraftStore` to use the shared semantics, snapshot
   before mutation/read, and total ordering.
4. Fix `SchemaDescriptorDraftPayload.CloneField` to copy `ObjectSchema`. This is
   a domain snapshot-completeness correction required before either Store can
   satisfy D01; do not hide it in the PostgreSQL codec.
5. Add a regression assertion which mutates/reads a Schema Draft with non-null
   ObjectSchema and proves the exact ref survives Snapshot, Save, and Get.
6. Do not call the validator from the Store.

### Gate

```bash
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests --filter "FullyQualifiedName~DescriptorDraftStoreContract"
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests
dotnet build src/Metadata/Draft/CrestCreates.DescriptorDraft
git diff --check
```

---

## 6. Slice 3 — Organization InMemory Contract

### Red

Activate O01–O14/O19–O22 and applicable V cases for all four entity surfaces,
all query/read surfaces, hierarchy collision paths, exact CreatedAt order, and
detached snapshots.

### Green

1. Add internal `OrganizationScopedKey(TenantScopeKind, TenantId, Id)` in
   Organization Abstractions for friend use by the owning implementation; no
   delimiter string or empty-string semantic key escapes it.
2. Add provider-neutral Organization comparers/validation in Abstractions.
   Closed enum and scope normalization paths are explicit AOT-safe switches.
3. Convert all four InMemory dictionaries to typed keys.
4. Replace Hierarchy `CompKey`, child maps, queue, and visited sets with typed
   keys; keep missing-parent and cycle behavior unchanged.
5. Apply canonical collection order and make Primary selection
   `CreatedAt.UtcTicks -> normalized scope -> Id Ordinal`.
6. Ensure projections consume canonical order before `Distinct`.

### Gate

```bash
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests --filter "FullyQualifiedName~OrganizationStoreContract|FullyQualifiedName~OrganizationHierarchy|FullyQualifiedName~OrganizationIdentity"
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
dotnet build src/Framework/Modules/CrestCreates.Organization
git diff --check
```

---

## 7. Slice 4 — DataPermission InMemory Contract

### Red

Activate P01–P07/P10–P12 plus validation/cancellation. Include empty exact
Action/Permission, literal `"*"` rejection, tenant-before-global priority, and
the asymmetric WildcardAction/ExactPermission behavior.

### Green

1. Add typed Exact/Wildcard match values and structural tenant scope.
2. Implement the frozen request-relative candidate plan once in
   `DataPermissionRuleSemantics`.
3. Change the InMemory dictionary from a delimiter string to the typed key.
4. Deduplicate candidate tuples while preserving first occurrence.
5. Do not add the generic fourth wildcard combination.

### Gate

```bash
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests --filter "FullyQualifiedName~DataPermissionScopeRuleStoreContract|FullyQualifiedName~DataPermissionScopeProvider"
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
git diff --check
```

---

## 8. Slice 5 — Recursive DTO Codec and AOT Roots

### Red

First add these three references to
`CrestCreates.Runtime.Persistence.PostgreSql.csproj`, before adding or compiling
any DTO/codec source file:

```text
CrestCreates.DescriptorDraft.Abstractions
CrestCreates.DescriptorDraft
CrestCreates.Organization.Abstractions
```

Use ordinary compile references in the Provider csproj:

```xml
<ProjectReference Include="../../Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CrestCreates.DescriptorDraft.Abstractions.csproj" />
<ProjectReference Include="../../Metadata/Draft/CrestCreates.DescriptorDraft/CrestCreates.DescriptorDraft.csproj" />
<ProjectReference Include="../../Framework/Modules/CrestCreates.Organization.Abstractions/CrestCreates.Organization.Abstractions.csproj" />
```

They are a Slice 5 compile precondition because the DTO roots name the six
concrete Draft payload families and Organization root types. Do not defer them
to the migration/Store Slices, and do not add a reference to the Organization
implementation assembly.

Activate D01 and C13 representation tests. They cover Schema, Form,
Capability, HumanTask, Event, and Workflow with Capability/HumanTask/SubWorkflow
targets, plus malformed nested union shapes and recursive abstract/interface
inventory. C13 consists of two independent architecture tests:

```text
Domain graph inventory:
    recursively walk the six concrete DescriptorDraftPayload roots
    discover every abstract/interface/object-typed slot
    exact-set compare normalized paths with PersistenceMappingManifest

Persistence DTO graph closure:
    recursively walk every generated JSON persistence DTO root
    fail if any unresolved abstract/interface/object-typed member remains
```

The first test prevents a new Domain polymorphic member from being silently
omitted by a mapper; the second prevents an unresolved DTO arm. Test-only
reflection is permitted. Production codec/serialization reflection is not.

### Green

1. Add a versioned provider envelope and provider-owned DTO per payload family.
2. Map Workflow steps to a closed `TargetKind` plus exactly one typed ref arm.
3. Keep DescriptorKind independent from the payload discriminator.
4. Make DTO header fields nullable where invalid-but-representable Draft
   diagnostics require it; TenantId/DraftId remain non-null row address fields.
5. Add explicit roots to
   `PostgreSqlControlPlaneReferenceDataJsonSerializerContext`.
6. Serialize with generated `JsonTypeInfo`; SQL receives text/bytes and casts to
   `jsonb`. Do not enable dynamic Npgsql JSON.
7. Add an explicit test-owned `PersistenceMappingManifest` containing only
   abstract/interface/object-typed durable Domain slots which require an
   explicit persistence representation. Each entry maps that normalized
   polymorphic path to its provider DTO/value arm. The Domain walker uses public
   instance properties, unwraps nullable/collection/dictionary generic shapes,
   tracks visited `(Type, PathShape)` pairs to break cycles, and treats only an
   explicit scalar/value leaf allowlist as closed. Exact-set compare
   `DomainPolymorphicPaths` with `PersistenceMappingManifest.Paths`. Scalar,
   collection, and dictionary field/value completeness belongs to D01's
   observation oracle, not C13.
8. Add the independent DTO graph closure and production forbidden-reflection
   checks. A DTO-only walk does not satisfy C13.

### Gate

```bash
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~DescriptorDraftPayload|FullyQualifiedName~ClosedAotPersistenceMapping"
dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
git diff --check
```

---

## 9. Slice 6 — V011, Schema Manifest, and Base Provider Marker

### Red

Activate C01–C03/C07–C08/C10–C11. Verify exact tables, columns, collation,
primary keys, checks, indexes, absence of Organization FKs, immutable checksums,
validation-only drift detection, and base-neutral registration. Activate P08
here as explicit ARCH evidence that no persisted derived `DataPermissionScope`
Store/table/type exists. C03 has separate column-collation and
index-key-collation drift vectors. C09/C14/C15 remain inactive until Slice 9,
after all three concrete Store classes exist.

### Green

1. Append `V011_control_plane_reference_data_stores` to the existing catalog.
2. Add the six frozen tables, exact tick/readable timestamp pairs, JSON contract
   versions, Rule discriminators, and all checks/indexes from Spec §9.
3. Extend the complete `RuntimeSchemaManifest` and required-table inventory.
4. Extend `RuntimeSchemaIndex` with expected index-key collation metadata and
   extend `ValidateIndexAsync` to read/compare actual key collations from
   `pg_index.indcollation`/`pg_collation`. Existing index column/predicate/
   uniqueness validation remains active. V011 indexes declare the expected
   collation for every text key and null/no-collation for non-text keys.
5. Add a C03 test which drops/recreates one V011 index with the wrong key
   collation while leaving table-column collation correct; validation-only mode
   must reject it.
6. Generalize only the XML wording of `ExecuteTopLevelAsync`; do not add another
   coordinator mode.
7. Add an internal `PostgreSqlRuntimeProviderRegistrationMarker` singleton in
   the base registration and prove C08: base registration alone remains
   feature-neutral and does not replace any selected Store. The feature
   extension is intentionally not implemented in this Slice because its three
   concrete registrations do not yet exist.

### Gate

```bash
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~ControlPlaneReferenceDataMigration|FullyQualifiedName~BaseProviderRegistration"
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
git diff --check
```

---

## 10. Slice 7 — PostgreSQL Descriptor Draft Store

### Red

Activate the PostgreSQL Draft runner for D01–D08/D11–D13, Draft
V01/V03/V04/V05 tuples, Draft concurrency/failure/ambient tuples, and every
Draft member of F09. Keep D09 inactive: normative provider reconstruction
requires the feature DI extension, which is not implemented until Slice 9.

### Green

1. Pre-cancellation is the first observable method action.
2. Save: abstraction-owned representation validate -> provider-local six-arm
   payload support/discriminator lookup on the original `draft.Payload` with no
   virtual call -> `Snapshot()` exactly once -> DTO/columns from that one
   snapshot and the preselected discriminator -> notify the new multi-arrival
   after-snapshot test hook -> `ExecuteTopLevelAsync` -> one upsert -> COMMIT.
   V04 uses a test-only seventh payload whose `Snapshot()` increments a counter
   and asserts `SnapshotCallCount == 0`; rejection must precede virtual
   `Snapshot()` dispatch and PostgreSQL access.
3. Store `CreatedAt.UtcTicks` in bigint and an Offset=0 microsecond projection
   in `timestamptz`; filter only by bigint.
4. Read through generated DTO codec; verify state version, identity, payload
   arm, all structured fields, ticks, and readable projection.
5. Finalize List ordering with the shared ordinal comparer after materializing.
6. Translate only through the existing provider taxonomy.

### Gate

```bash
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~DescriptorDraft"
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests
dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
git diff --check
```

---

## 11. Slice 8 — PostgreSQL Organization Store

### Red

Activate PostgreSQL O01–O15/O19–O22, Organization V01/V03/V05 tuples, all
Organization concurrency/failure/ambient tuples, every Organization F09 field,
and no-FK missing-reference cases. Prepare reusable process fixture data/helpers
for every entity, but keep normative O16 inactive until Slice 10 owns the
independent process-restart machinery.

### Green

1. Normalize tenant scope into discriminator + value columns.
2. Upsert each entity independently with no reference lookup or FK.
3. Implement null collection tenant as unfiltered and non-null tenant as exact.
4. Use exact tick columns for all CreatedAt ordering; final materialized order
   uses shared comparers.
5. Decode JSON to detached snapshots and validate every duplicated field and
   readable projection.
6. Keep hierarchy/identity logic in the Organization domain; the Provider only
   returns canonical Store results.

### Gate

```bash
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~Organization"
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
git diff --check
```

---

## 12. Slice 9 — PostgreSQL Rule Store, Feature DI, and Draft Reconstruction

### Red

Activate PostgreSQL P01–P07/P10–P13, Rule V01/V02/V03/V05, and Rule
concurrency/failure/ambient tuples. Activate C09/C14/C15 now that the Draft,
Organization, and Rule concrete Store classes all exist. Activate D09 here
after feature composition exists. P09 remains inactive until Slice 10.

### Green

1. Persist the typed key in structured columns; Exact empty remains distinct
   from Wildcard empty through match-kind columns.
2. Query frozen candidates in priority order without broadening them.
3. Validate all tenant/match/scope discriminators during materialization before
   returning an authorization value.
4. P13 Schema leg covers all seven corruption variants with intact CHECKs and
   requires invalid raw DML rejection.
5. P13 real-Store evidence is reachability-aware:
   - `InvalidScopeKind` is not a key field, so after dropping only its CHECK the
     malformed row is hit by a valid candidate and the real Store must throw
     `PersistedInvariantViolation` during materialization.
   - the other six variants corrupt typed key columns. After dropping only the
     relevant CHECK and inserting the row, a valid candidate must not match it;
     with no other matching rows, `GetScopeKindAsync` returns null and produces
     no authorization decision.
   - an optional direct internal row-materializer unit test may feed those six
     synthetic malformed shapes and assert defensive rejection, but it is not
     a substitute for the Schema leg or no-decision Store leg.
6. Never broaden the Rule query or scan malformed rows merely to make P13 throw.
7. Implement
   `AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()` only
   now: inspect `IServiceCollection` for the Slice 6 marker plus
   `PostgreSqlRuntimePersistenceOptions`, `NpgsqlDataSource`,
   `PostgreSqlRuntimeMigrationRunner`, and
   `PostgreSqlRuntimeTransactionCoordinator`; throw a clear
   `InvalidOperationException` naming the required base method if any is
   absent; `RemoveAll` exactly the three selected Store interfaces and add one
   singleton PostgreSQL implementation for each. Repeated base-first feature
   calls remain idempotent. Activate C09 for all six Save surfaces, C14 for a
   missing base and an Options-only fake base, and C15 for repeated opt-in.
8. Close D09 through real provider reconstruction: compose provider A with base
   then feature, resolve the PostgreSQL Draft Store, Save, dispose the entire
   ServiceProvider/DataSource, compose provider B against the same schema with
   base then feature, resolve a new Draft Store, and Get the complete saved
   snapshot. Do not count a second Store resolved from provider A, and do not
   turn this into the Slice 10 subprocess restart case D10.

### Gate

```bash
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~DataPermission|FullyQualifiedName~RuleCorruption|FullyQualifiedName~ControlPlaneReferenceDataComposition"
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
git diff --check
```

---

## 13. Slice 10 — Durability, Crash, and Failure Matrix

### Red

Activate D10, O16–O18, P09, F03–F05 and F07 for every required dimension,
every snapshot corruption variant, and direct process restart for Draft, Unit,
Position, Membership, RoleAssignment, and Rule. Re-run all sixteen F06 method
tuples as a full failure-matrix regression, but activate no new F06 tuple here:
Draft F06 is owned by Slice 7, Organization F06 by Slice 8, and Rule F06 by
Slice 9.

### Green

1. Add a `ReferenceOutputAssembly="false"` ProjectReference from the PostgreSQL
   test project to CrashWorker so a clean test build produces the worker in the
   same Configuration.
2. Add one shared `PostgreSqlCrashWorkerPath` resolver. Derive Debug/Release from
   the running test assembly's `AppContext.BaseDirectory`; find the repository
   root; resolve `CrashWorker/bin/{Configuration}/net10.0/...dll`. Replace every
   existing hard-coded Debug path in Runtime, pre-dispatch, and Agent Memory
   crash tests, not only the new #69 tests.
3. Extend CrashWorker with a generic surface/scenario dispatch:
   `reference-{surface}-{before-commit|after-commit|commit-unknown}`.
4. Use the real top-level coordinator/test hook and real process kill; do not
   mock repositories or COMMIT.
5. Parent tests wait for explicit worker markers, kill the process tree where
   required, wait for backend exit, construct a fresh provider, and read state.
6. Prove old/absent before COMMIT, complete new after COMMIT, and shared
   commit-unknown taxonomy after response loss.
7. Run provider reconstruction separately from process restart.
8. Corrupt each frozen snapshot-contract variant and each structured-field
   tuple in isolated schemas; verify fail-closed reads.
9. For every F07 variant named `*InvalidJson`, insert syntactically valid
   `jsonb` which is invalid for the expected generated persistence root, such as
   a required object/property with the wrong JSON kind (`{"header":42}`) or a
   missing required arm. Do not attempt lexically malformed JSON such as
   `'{ broken json'`: PostgreSQL rejects it before a persisted-corruption read
   oracle can execute. Non-JSON F07 variants mutate the contract version,
   discriminator, or Workflow union shape named by the variant.

### Gate

```bash
dotnet build tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker -c Debug
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~ControlPlaneReferenceDataCrash|FullyQualifiedName~ControlPlaneReferenceDataRestart|FullyQualifiedName~ControlPlaneReferenceDataFailure|FullyQualifiedName~ControlPlaneReferenceDataCorruption"
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests
git diff --check
```

---

## 14. Slice 11 — NativeAOT and Mainline Closure

### Red

Extend the existing AOT fixture assertions for three Workflow target markers,
Organization, Rule, restart, and final sentinel:

```text
CRESTCREATES_DURABLE_CONTROL_PLANE_WORKFLOW_CAPABILITY_OK
CRESTCREATES_DURABLE_CONTROL_PLANE_WORKFLOW_HUMAN_TASK_OK
CRESTCREATES_DURABLE_CONTROL_PLANE_WORKFLOW_SUBWORKFLOW_OK
CRESTCREATES_DURABLE_REFERENCE_ORGANIZATION_OK
CRESTCREATES_DURABLE_REFERENCE_DATA_PERMISSION_OK
CRESTCREATES_DURABLE_CONTROL_PLANE_REFERENCE_DATA_OK
```

### Green

1. Add DescriptorDraft and Organization project references to AotHost only as
   required for real composition/domain objects.
2. Register the base PostgreSQL Provider first, then the feature opt-in.
3. Execute all three nested Workflow target DTO arms, Organization
   hierarchy/identity, tenant/global Rule priority, provider reconstruction,
   and real reads against PostgreSQL.
4. Preserve every existing Phase 9b, pre-dispatch, and Agent Memory sentinel.
5. Publish linux-x64 with `CrestCreatesPublishMode=aot`, native-link, run the
   original binary, and reject IL2026/IL3050 warnings.
6. Complete architecture guards and prove the evidence ledger has every active
   `Case × Surface × Variant × EvidenceVectorKey × Runner` tuple with no
   placeholder/skipped entry.
7. Update `memory.md` only after actual evidence is known.

### Final gate

```bash
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests -c Release
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests -c Release
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests -c Release
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests -c Release
dotnet build tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker -c Release
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests -c Release
dotnet build CrestCreates.slnx -c Release
dotnet test CrestCreates.slnx -c Release
git diff --check
```

If Docker is unavailable, use `CREST_RUNTIME_PG_CONNECTION`; do not report the
PostgreSQL/AOT gate Green without an executed real database and original native
binary.

---

## 15. Evidence Activation Summary

The manifest is the implementation ledger, but the frozen Spec remains its
oracle:

```text
Shared InMemory + PostgreSQL:
    D01–D08, D11–D13
    O01–O14, O19–O22
    P01–P07, P10–P12
    V01–V05
    F01–F02

PostgreSQL-only:
    D09–D10
    O15–O18
    P09, P13
    F03–F09

Architecture/composition/migration/AOT:
    P08
    C01–C15
```

Runner expansion must satisfy all six Save surfaces, four Organization identity
surfaces, four Organization entity surfaces, five query surfaces, seven read
surfaces, sixteen Store methods, eight Descriptor payload/nested variants,
every structured-field tuple, every Rule corruption variant, and every AOT
scenario. Atomic internal expansions are separately keyed; one Theory method or
one representative InlineData row never closes a dimension.

---

## 16. Completion Checklist

Issue #69 implementation is complete only when:

1. All 23 frozen Spec Exit Criteria are executable and Green.
2. All 77 Case IDs have complete required evidence tuples.
3. InMemory and PostgreSQL share observable semantics and no legacy fallback.
4. V011 is immutable, checksummed, repeatable, and schema-manifest complete.
5. The Provider reuses one DataSource, migration kernel, failure taxonomy, and
   top-level coordinator.
6. No Organization FK, derived Scope Store, legacy Draft Store, provider OCC,
   or Runtime transaction participant was introduced.
7. The published linux-x64 native executable runs all new real PostgreSQL
   scenarios and retains all previous sentinels.
8. Canonical Release build/test evidence and any environment limitation are
   recorded truthfully in the final handoff and `memory.md`.

Only after this checklist may the implementation be described as
`NativeAOT-verified` durable PostgreSQL support for the three selected Store
contracts. It must not claim the entire Agent Control Plane is durable.

---

# Appendix A — Exact Semantic Algorithms

This appendix is prescriptive. An implementation Agent must not replace these
algorithms with “equivalent-looking” provider-local behavior.

## A.1 Method-entry order

Every Store method follows this order:

```text
1. cancellationToken.ThrowIfCancellationRequested()
2. representation/input validation for this method
3. snapshot capture for Save, or query-plan construction for Read
4. provider operation
5. persisted-row validation for PostgreSQL Read
6. detached snapshot and canonical final ordering
```

Consequences:

- a pre-cancelled Save never calls `Snapshot()` and never opens PostgreSQL;
- an unsupported Draft payload is rejected by an implementation/provider-local
  closed CLR-type switch before virtual `Payload.Snapshot()` dispatch;
- a pre-cancelled Read never opens PostgreSQL;
- cancellation after work is ready to COMMIT does not cancel COMMIT;
- Draft semantic validation is absent from this sequence;
- caller mutation after step 3 cannot affect the persisted row.

## A.2 Descriptor Draft representation validation

Use explicit switches for closed enums. Do not use reflection-based
`Enum.IsDefined` in the NativeAOT mainline.

```text
Save(draft):
    reject draft == null
    reject draft.TenantId == null       # empty/whitespace allowed
    reject draft.DraftId == null        # empty/whitespace allowed
    reject draft.Payload == null
    reject undefined DescriptorKind underlying integer
    reject undefined Operation
    reject undefined AuthorKind
    reject undefined Status
    reject unsupported runtime Payload CLR type

    DO NOT reject:
        blank DraftId
        null/blank DescriptorId
        null/blank AuthorId
        header/payload kind mismatch
        header/payload descriptor ID mismatch
        BaseVersion/Operation inconsistency
        ProposedVersion inconsistency
```

`GetAsync` rejects runtime null TenantId/DraftId but permits blank values so a
blank validator-owned DraftId remains retrievable. `ListAsync` rejects runtime
null TenantId and validates only defined optional query enums; a null query
means no filters.

Exact Draft filter algorithm:

```text
if CreatedFrom != null:
    row.CreatedAt.UtcTicks >= CreatedFrom.Value.UtcTicks
if CreatedTo != null:
    row.CreatedAt.UtcTicks <= CreatedTo.Value.UtcTicks
```

Never compare offsets and never convert the boundary through PostgreSQL
`timestamptz`.

## A.3 Time materialization

Implement one Provider helper:

```csharp
internal static DateTimeOffset NormalizeReadableTimestamp(DateTimeOffset value)
{
    var utcTicks = value.UtcTicks;
    var truncated = utcTicks - (utcTicks % TimeSpan.TicksPerMicrosecond);
    return new DateTimeOffset(truncated, TimeSpan.Zero);
}
```

For every saved Draft/Organization snapshot:

```text
created_at_utc_ticks = snapshot.CreatedAt.UtcTicks
created_at           = NormalizeReadableTimestamp(snapshot.CreatedAt)
state_json           = snapshot retaining original DateTimeOffset offset/ticks
```

Read validation checks both:

```text
snapshot.CreatedAt.UtcTicks == created_at_utc_ticks
NormalizeReadableTimestamp(snapshot.CreatedAt) == created_at
```

Only `created_at_utc_ticks` participates in semantic filter/order SQL.

## A.4 Organization scope and key

The internal value must distinguish null/global from every tenant string:

```csharp
internal enum OrganizationTenantScopeKind { Global = 0, Tenant = 1 }

internal readonly record struct OrganizationScopedKey(
    OrganizationTenantScopeKind ScopeKind,
    string TenantId,
    string Id);
```

Factory rules:

```text
TenantId == null:
    (Global, "", Id)

TenantId != null:
    validate not empty/whitespace
    (Tenant, TenantId, Id)
```

The empty normalized value is never compared without ScopeKind. No `":"`,
`"::"`, `"*"`, interpolation, or concatenation may create an identity.

Normalized scope comparison:

```text
Global < Tenant
Tenant ties -> TenantId StringComparer.Ordinal
```

Collection comparers:

```text
OrganizationUnit:
    SortOrder -> Scope -> Id Ordinal

Position:
    Scope -> Id Ordinal

Membership and RoleAssignment:
    CreatedAt.UtcTicks -> Scope -> Id Ordinal
```

`DefaultOrganizationIdentityService` must select Primary with the exact
Membership comparer, not a shorter comparer.

## A.5 Organization query scope

```text
Point read with tenantId == null:
    global identity only

Collection/query with tenantId == null:
    no scope predicate; return global + every tenant

Collection/query with tenantId != null:
    tenant scope only; never global fallback
```

This asymmetry is intentional compatibility behavior. Do not “clean it up.”

## A.6 DataPermission typed matches

```csharp
internal enum DataPermissionMatchKind { Exact = 0, Wildcard = 1 }

internal readonly record struct DataPermissionMatch(
    DataPermissionMatchKind Kind,
    string Value);
```

Conversion:

```text
domain null -> (Wildcard, "")
domain non-null except literal "*" -> (Exact, original string)
literal "*" -> validation failure
```

Exact empty is `(Exact, "")`; it is not wildcard.

Candidate generation is exactly:

```text
for requested tenant scope, then global scope:
    1. request Action (Exact if non-null else Wildcard)
       + request Permission (Exact if non-null else Wildcard)
    2. request Action (Exact if non-null else Wildcard)
       + Permission Wildcard
    3. Action Wildcard + Permission Wildcard
```

Remove duplicate typed tuples while preserving first occurrence. For a
non-null requested Action, never generate WildcardAction + ExactPermission.

## A.7 Save/read transaction algorithm

Save pseudocode:

```csharp
public async Task SaveAsync(T value, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    ValidateRepresentation(value);
    var snapshot = value.Snapshot();
    var row = BuildRow(snapshot); // JSON and structured fields from same object
    await _coordinator.ExecuteTopLevelAsync(
        innerCt => UpsertOneRowAsync(row, innerCt), ct);
}
```

Read pseudocode:

```csharp
public async Task<T?> GetAsync(..., CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    ValidateReadInput(...);
    return await _support.ExecuteReadAsync(async connection =>
    {
        var row = await ReadRowAsync(connection, ..., ct);
        return row is null ? null : ValidateAndDetach(row);
    }, ct);
}
```

Reads use `NpgsqlDataSource.OpenConnectionAsync` directly and never enter the
Runtime transaction accessor. Saves alone use `ExecuteTopLevelAsync`.

`ExecuteReadAsync` catches `NpgsqlException` and maps it to
`RuntimePersistenceUnavailableException`; it rethrows existing
`RuntimePersistenceException` unchanged. JSON/column disagreement maps to
`RuntimePersistenceContractException(PersistedInvariantViolation)`.

---

# Appendix B — V011 SQL and Manifest Blueprint

The final migration text may be formatted differently, but names, shapes, and
semantics below are fixed. Compute the migration checksum through the existing
catalog mechanism; never hand-edit an older checksum.

## B.1 Descriptor Draft table

```sql
create table {schema}.control_plane_descriptor_drafts (
    tenant_id text collate "C" not null,
    draft_id text collate "C" not null,
    payload_type integer not null,
    descriptor_kind integer not null,
    operation integer not null,
    author_kind integer not null,
    status integer not null,
    created_at_utc_ticks bigint not null,
    created_at timestamptz not null,
    state_contract_version integer not null,
    state_json jsonb not null,
    updated_at timestamptz not null default clock_timestamp(),
    primary key (tenant_id, draft_id),
    constraint ck_cp_draft_payload_type
        check (payload_type = any (array[1,2,3,4,5,6])),
    constraint ck_cp_draft_descriptor_kind
        check (descriptor_kind = any (array[0,1,2,3,4,5,6,7,8,9])),
    constraint ck_cp_draft_operation
        check (operation = any (array[0,1,2,3])),
    constraint ck_cp_draft_author_kind
        check (author_kind = any (array[0,1,2,3,4])),
    constraint ck_cp_draft_status
        check (status = any (array[0,1,2,3,4])),
    constraint ck_cp_draft_contract_version
        check (state_contract_version = 1)
);

create index ix_cp_drafts_created
    on {schema}.control_plane_descriptor_drafts
       (tenant_id, created_at_utc_ticks, draft_id);

create index ix_cp_drafts_combined_filter
    on {schema}.control_plane_descriptor_drafts
       (tenant_id, descriptor_kind, operation, author_kind, status,
        created_at_utc_ticks, draft_id);
```

Payload type numeric mapping is fixed inside the Provider, not Domain:

```text
1 Schema
2 Form
3 Capability
4 HumanTask
5 Workflow
6 Event
```

DescriptorKind uses the actual current enum values 0–9 independently of that
mapping.

## B.2 Organization tables

Every scope table repeats this check with its own constraint name:

```sql
check (
    (tenant_scope_kind = 'global' and tenant_id = '')
    or
    (tenant_scope_kind = 'tenant' and tenant_id <> '')
)
```

```sql
create table {schema}.organization_units (
    tenant_scope_kind text collate "C" not null,
    tenant_id text collate "C" not null,
    organization_unit_id text collate "C" not null,
    parent_id text collate "C" null,
    sort_order integer not null,
    is_active boolean not null,
    created_at_utc_ticks bigint not null,
    created_at timestamptz not null,
    state_contract_version integer not null,
    state_json jsonb not null,
    updated_at timestamptz not null default clock_timestamp(),
    primary key (tenant_scope_kind, tenant_id, organization_unit_id),
    constraint ck_org_units_tenant_scope check (
        (tenant_scope_kind = 'global' and tenant_id = '')
        or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
    constraint ck_org_units_contract_version
        check (state_contract_version = 1)
);

create index ix_org_units_explicit_list
    on {schema}.organization_units
       (tenant_scope_kind, tenant_id, sort_order, organization_unit_id);
create index ix_org_units_unfiltered_list
    on {schema}.organization_units
       (sort_order, tenant_scope_kind, tenant_id, organization_unit_id);
```

```sql
create table {schema}.organization_positions (
    tenant_scope_kind text collate "C" not null,
    tenant_id text collate "C" not null,
    position_id text collate "C" not null,
    is_active boolean not null,
    created_at_utc_ticks bigint not null,
    created_at timestamptz not null,
    state_contract_version integer not null,
    state_json jsonb not null,
    updated_at timestamptz not null default clock_timestamp(),
    primary key (tenant_scope_kind, tenant_id, position_id),
    constraint ck_org_positions_tenant_scope check (
        (tenant_scope_kind = 'global' and tenant_id = '')
        or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
    constraint ck_org_positions_contract_version
        check (state_contract_version = 1)
);
```

```sql
create table {schema}.organization_memberships (
    tenant_scope_kind text collate "C" not null,
    tenant_id text collate "C" not null,
    membership_id text collate "C" not null,
    user_id text collate "C" not null,
    organization_unit_id text collate "C" not null,
    position_id text collate "C" null,
    is_primary boolean not null,
    is_active boolean not null,
    created_at_utc_ticks bigint not null,
    created_at timestamptz not null,
    state_contract_version integer not null,
    state_json jsonb not null,
    updated_at timestamptz not null default clock_timestamp(),
    primary key (tenant_scope_kind, tenant_id, membership_id),
    constraint ck_org_memberships_tenant_scope check (
        (tenant_scope_kind = 'global' and tenant_id = '')
        or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
    constraint ck_org_memberships_contract_version
        check (state_contract_version = 1)
);

create index ix_org_memberships_by_user
    on {schema}.organization_memberships
       (user_id, tenant_scope_kind, tenant_id,
        created_at_utc_ticks, membership_id);
create index ix_org_memberships_by_unit
    on {schema}.organization_memberships
       (organization_unit_id, tenant_scope_kind, tenant_id,
        created_at_utc_ticks, membership_id);
```

```sql
create table {schema}.organization_role_assignments (
    tenant_scope_kind text collate "C" not null,
    tenant_id text collate "C" not null,
    assignment_id text collate "C" not null,
    user_id text collate "C" not null,
    role_id text collate "C" not null,
    organization_unit_id text collate "C" null,
    is_active boolean not null,
    created_at_utc_ticks bigint not null,
    created_at timestamptz not null,
    state_contract_version integer not null,
    state_json jsonb not null,
    updated_at timestamptz not null default clock_timestamp(),
    primary key (tenant_scope_kind, tenant_id, assignment_id),
    constraint ck_org_roles_tenant_scope check (
        (tenant_scope_kind = 'global' and tenant_id = '')
        or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
    constraint ck_org_roles_contract_version
        check (state_contract_version = 1)
);

create index ix_org_roles_by_user
    on {schema}.organization_role_assignments
       (user_id, tenant_scope_kind, tenant_id,
        created_at_utc_ticks, assignment_id);
```

There are no Organization foreign keys. The schema-manifest
`RequiredForeignKeys` list is empty for all four tables.

## B.3 DataPermission table

```sql
create table {schema}.data_permission_scope_rules (
    tenant_scope_kind text collate "C" not null,
    tenant_id text collate "C" not null,
    resource text collate "C" not null,
    action_match_kind integer not null,
    action_value text collate "C" not null,
    permission_match_kind integer not null,
    permission_value text collate "C" not null,
    scope_kind integer not null,
    updated_at timestamptz not null default clock_timestamp(),
    primary key (
        tenant_scope_kind, tenant_id, resource,
        action_match_kind, action_value,
        permission_match_kind, permission_value),
    constraint ck_data_permission_tenant_scope check (
        (tenant_scope_kind = 'global' and tenant_id = '')
        or
        (tenant_scope_kind = 'tenant'
         and tenant_id <> '' and tenant_id <> '*')),
    constraint ck_data_permission_action_match check (
        (action_match_kind = 0 and action_value <> '*')
        or (action_match_kind = 1 and action_value = '')),
    constraint ck_data_permission_permission_match check (
        (permission_match_kind = 0 and permission_value <> '*')
        or (permission_match_kind = 1 and permission_value = '')),
    constraint ck_data_permission_scope_kind
        check (scope_kind = any (array[0,1,2,3,4,5]))
);
```

The match checks deliberately allow `(Exact, "")`. Invalid match-kind integers
fail because neither branch accepts them.

## B.4 Manifest checklist

For every new table, add exact entries for:

```text
Columns: name, information_schema type, nullability, collation
PrimaryKey: exact ordered columns
RequiredChecks: exact constraint name + normalized pg definition
RequiredIndexes: exact ordered columns, uniqueness, predicate, collation
RequiredForeignKeys: [] for all new tables
```

Update both the cheap required-table inventory and the full
`RuntimeSchemaManifest.Tables`. C03 must fail independently for a missing
column, wrong nullability, wrong collation, missing check, changed check, wrong
PK order, missing index, wrong index columns, unexpected FK, and checksum drift.

The current runner validates required checks/indexes/FKs one-by-one and an empty
`RequiredForeignKeys` list alone does not prove absence. Extend schema validation
for the six V011 tables to compare exact normalized sets:

```text
actual named CHECK constraints == manifest RequiredChecks
actual non-PK indexes          == manifest RequiredIndexes
actual foreign keys            == manifest RequiredForeignKeys
```

This exact-set comparison is mandatory for C03/C07. Account for the automatic
primary-key backing index separately so it is not mistaken for an unexpected
user index. Every new non-unique query index must use `Unique: false` in the
manifest. Do not weaken validation of existing tables to make the new exact-set
tests pass.

Extend the manifest record explicitly, for example:

```csharp
private sealed record RuntimeSchemaIndex(
    string Name,
    IReadOnlyList<string> Columns,
    string Predicate,
    bool Unique = true,
    IReadOnlyList<string?>? KeyCollations = null);
```

`KeyCollations` aligns one-for-one with `Columns`; use `"C"` for text keys and
null for non-collatable keys. It may remain null for legacy manifest entries to
avoid silently redefining already-approved migrations, but every V011 index must
declare it. `ValidateIndexAsync` must query each index key's collation OID via
`pg_index.indcollation`, resolve it through `pg_collation`, preserve key ordinal,
and compare with the manifest. Do not infer index-key collation from the table
column; C03 deliberately recreates an index with an explicit wrong collation
while the column remains correct.

V011 expected arrays are:

```text
ix_cp_drafts_created:
    ["C", null, "C"]
ix_cp_drafts_combined_filter:
    ["C", null, null, null, null, null, "C"]
ix_org_units_explicit_list:
    ["C", "C", null, "C"]
ix_org_units_unfiltered_list:
    [null, "C", "C", "C"]
ix_org_memberships_by_user:
    ["C", "C", "C", null, "C"]
ix_org_memberships_by_unit:
    ["C", "C", "C", null, "C"]
ix_org_roles_by_user:
    ["C", "C", "C", null, "C"]
```

---

# Appendix C — Persistence DTO and Codec Blueprint

## C.1 Envelope shape

Use a single generated root with closed nullable arms:

```csharp
internal sealed record DescriptorDraftPersistenceEnvelopeDto
{
    public required int StateContractVersion { get; init; }
    public required DescriptorDraftHeaderDto Header { get; init; }
    public required DescriptorDraftPayloadType PayloadType { get; init; }
    public SchemaDraftPayloadDto? Schema { get; init; }
    public FormDraftPayloadDto? Form { get; init; }
    public CapabilityDraftPayloadDto? Capability { get; init; }
    public HumanTaskDraftPayloadDto? HumanTask { get; init; }
    public WorkflowDraftPayloadDto? Workflow { get; init; }
    public EventDraftPayloadDto? Event { get; init; }
}
```

Exactly one arm must be non-null and must match `PayloadType`. Reject zero,
multiple, or mismatched arms as persisted invariant violations.

`DescriptorDraftHeaderDto` includes every Draft field except Payload. Preserve
Metadata with ordinal copy semantics. `DescriptorId` and `AuthorId` are nullable
in the DTO even though the domain source uses C# `required`; this is necessary
for validator-owned invalid states. Header `DescriptorKind` is copied exactly
and never drives payload-arm selection.

## C.2 Workflow nested union

```csharp
internal sealed record WorkflowStepPersistenceDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required WorkflowTargetKind TargetKind { get; init; }
    public CapabilityTargetPersistenceDto? Capability { get; init; }
    public HumanTaskTargetPersistenceDto? HumanTask { get; init; }
    public SubWorkflowTargetPersistenceDto? SubWorkflow { get; init; }
    public string? Condition { get; init; }
    public required string[] Transitions { get; init; }
    public string? InputMapping { get; init; }
    public string? OutputMapping { get; init; }
    public required StepErrorBehavior OnError { get; init; }
}
```

Map refs explicitly:

```text
CapabilityTarget.Capability
    -> Versioned ref DTO preserving Id/Version/SelectionMode/ExpectedContractHash

HumanTaskTarget.HumanTask
    -> Versioned ref DTO preserving Id/Version/SelectionMode/ExpectedContractHash

SubWorkflowTarget.SubWorkflow
    -> Versioned ref DTO preserving Id/Version/SelectionMode/ExpectedContractHash
```

Do not serialize `InteractionTarget`, an interface-typed descriptor ref, or an
`object` property directly.

## C.3 Six payload mappings

Create one explicit mapper in each direction for:

```text
SchemaDescriptorDraftPayload      <-> SchemaDraftPayloadDto
FormDescriptorDraftPayload        <-> FormDraftPayloadDto
CapabilityDescriptorDraftPayload  <-> CapabilityDraftPayloadDto
HumanTaskDescriptorDraftPayload   <-> HumanTaskDraftPayloadDto
WorkflowDescriptorDraftPayload    <-> WorkflowDraftPayloadDto
EventDescriptorDraftPayload       <-> EventDraftPayloadDto
```

Each DTO mirrors every persisted descriptor field present in the current source
type, including nested collections/dictionaries. Use the existing payload
`Snapshot()` implementations as the field inventory. Never call
`Payload.GetDescriptor()` to choose a DTO arm; switch on the concrete payload
CLR type. Unknown CLR types fail before mutation.

Current field inventory to copy explicitly:

```text
Schema:
    Id, Name, State, SupersededById, Version, ChangeKind
    Fields[*]: Name, FieldType, IsRequired, IsNullable, MaxLength,
               MinLength, MaxValue, MinValue, Pattern, IsCollection,
               CollectionElementType, ObjectSchema
    ValidationRules[*]: Name, Expression, ErrorMessage
    References[*]

Form:
    Id, Name, State, SupersededById, Version, Schema, LayoutColumns
    Fields[*]: SchemaFieldName, Label, Placeholder, HelpText, FormatHint,
               Order, Group, IsReadOnly, VisibilityCondition, ControlType,
               IsRequiredOverride, ValidationMessage,
               DefaultValueExpression, OptionsSource, Metadata

Capability:
    Id, Name, State, SupersededById, Version, CapabilityKind,
    InputSchema, OutputSchema, Categories, Produces, Consumes,
    SemanticTags, Permissions, RiskLevel, ProjectionKind

HumanTask:
    Id, Name, State, SupersededById, Version, Interaction,
    InputSchema, OutputSchema, AssigneeStrategy, Timeout, Permissions
    Outcomes[*]: Condition, Capability

Workflow:
    Id, Name, State, SupersededById, Version, VariableSchema,
    DefaultVariableScope
    Steps[*]: Id, Name, Target closed union, Condition, Transitions,
              InputMapping, OutputMapping, OnError

Event:
    Id, Name, State, SupersededById, Version, PayloadSchema,
    Category, Semantic, Importance, ChangeKind
```

`SchemaFieldPersistenceDto.ObjectSchema` is a nullable explicit versioned-ref
DTO carrying `Id`, `Version`, `SelectionMode`, and `ExpectedContractHash`.
Slice 2 first fixes the domain payload Snapshot clone; Slice 5 then maps this
field in both codec directions. Omitting it is a D01 failure, not an optional
future enhancement.

If any listed nested field is itself interface/abstract typed, represent it
with another explicit provider DTO/value shape and add it to the recursive
inventory test. Do not silently leave it to System.Text.Json runtime metadata.

## C.4 Codec API

```csharp
internal static class PostgreSqlDescriptorDraftSnapshotCodec
{
    internal static int GetSupportedPayloadType(
        DescriptorDraftPayload payload);

    internal static DescriptorDraftPersistenceEnvelopeDto Capture(
        DescriptorDraft snapshot,
        int payloadType);

    internal static string Serialize(
        DescriptorDraftPersistenceEnvelopeDto envelope);

    internal static DescriptorDraft DeserializeAndValidate(
        string json,
        int payloadType,
        string tenantId,
        string draftId,
        int descriptorKind,
        int operation,
        int authorKind,
        int status,
        long createdAtUtcTicks,
        DateTimeOffset readableCreatedAt,
        int stateContractVersion);
}
```

`GetSupportedPayloadType` is a null-safe explicit six-arm switch over the
original payload object. It must not call `Snapshot()`, `GetDescriptor()`,
runtime reflection, or serialization. `PostgreSqlDescriptorDraftStore.SaveAsync`
calls it before `draft.Snapshot()`. `Capture` switches again on the one captured
snapshot, verifies that its arm agrees with the preselected discriminator, and
maps it without taking another snapshot. An unsupported original CLR type
therefore has zero `Snapshot()` calls; a malformed snapshot cannot be mislabeled
as another arm.

`DeserializeAndValidate` validates persisted representation only, reconstructs
the domain Draft, calls `Snapshot()` once for detachment, and does not invoke
`IDescriptorDraftValidator`.

## C.5 JSON context roots

At minimum register the envelope, header, all six payload DTOs, every nested DTO
and array/dictionary type, the three Workflow target arms, and all four
Organization entity roots. Use `JsonSourceGenerationMode.Metadata` consistently
with the existing Provider context. C13 requires both tests defined in Slice 5:
the Domain graph walker exact-set compares every discovered polymorphic slot
against `PersistenceMappingManifest`, and the separate DTO graph walker fails
if an abstract/interface/object member remains without an explicit provider
converter/union. Passing only the DTO graph walk is insufficient.

---

# Appendix D — Store Method SQL Shapes

## D.1 Descriptor Draft

`SaveAsync`: one `insert ... on conflict (tenant_id, draft_id) do update` which
replaces every structured field, contract version, JSON, and `updated_at`.

`GetAsync`:

```sql
select tenant_id, draft_id, payload_type, descriptor_kind, operation,
       author_kind, status, created_at_utc_ticks, created_at,
       state_contract_version, state_json
from {table}
where tenant_id=@tenant and draft_id=@draft;
```

`ListAsync` uses one static optional-filter query:

```sql
where tenant_id=@tenant
  and (@has_kind=false or descriptor_kind=@kind)
  and (@has_operation=false or operation=@operation)
  and (@has_author=false or author_kind=@author)
  and (@has_status=false or status=@status)
  and (@has_from=false or created_at_utc_ticks>=@from_ticks)
  and (@has_to=false or created_at_utc_ticks<=@to_ticks)
order by draft_id collate "C";
```

After decode, sort again with `StringComparer.Ordinal`. Do not assume PostgreSQL
`C` collation is the final .NET total order.

## D.2 Organization

All four Saves use one-row upserts replacing every structured/JSON field.

Point scope predicate:

```text
tenantId == null -> scope='global', tenant=''
tenantId != null -> scope='tenant', tenant=tenantId
```

Collection predicate:

```sql
where (@has_tenant=false
       or (tenant_scope_kind='tenant' and tenant_id=@tenant))
```

Combine it with the method selector (`user_id`, `organization_unit_id`) where
applicable. SQL supplies the matching preliminary order; every materialized list
is finally sorted by the shared .NET comparer.

Never query parent/Position/Role existence in a Save.

## D.3 DataPermission

`SaveRuleAsync` upserts by the complete typed PK and replaces only `scope_kind`
and `updated_at`.

`GetScopeKindAsync`:

1. build typed candidates with the shared Organization helper;
2. create a parameterized `VALUES` table with ordinal priority;
3. join the Rule table on every key column;
4. order by candidate priority and `limit 1`;
5. validate every returned discriminator/value tuple before casting ScopeKind.

Conceptual SQL:

```sql
with candidates(priority, tenant_scope_kind, tenant_id,
                action_match_kind, action_value,
                permission_match_kind, permission_value) as (
    values
      (0,@s0,@t0,@ak0,@av0,@pk0,@pv0),
      (1,@s1,@t1,@ak1,@av1,@pk1,@pv1)
      -- exact candidate count from the deduplicated typed plan
)
select r.tenant_scope_kind, r.tenant_id,
       r.action_match_kind, r.action_value,
       r.permission_match_kind, r.permission_value,
       r.scope_kind
from candidates c
join {rules} r
  on r.tenant_scope_kind=c.tenant_scope_kind
 and r.tenant_id=c.tenant_id
 and r.resource=@resource
 and r.action_match_kind=c.action_match_kind
 and r.action_value=c.action_value
 and r.permission_match_kind=c.permission_match_kind
 and r.permission_value=c.permission_value
order by c.priority
limit 1;
```

Only placeholder count is generated dynamically; all values are Npgsql
parameters. Do not interpolate domain strings into SQL.

---

# Appendix E — Exact Case Activation Checklist

Use the exact normative test names from frozen Spec §14. This table prevents a
weaker implementation Agent from activating a case in the wrong Slice.

| Slice | Case IDs | Required runner/expansion |
|---|---|---|
| 1 | all IDs structurally | manifest/skeleton only; no future behavior Reds |
| 2 | D01–D08, D11–D13 | InMemory Draft; all payloads (Schema includes ObjectSchema), query, validator-invalid, time variants |
| 2 | V01, V03–V05 | Draft members only; every Draft Store method for V05 |
| 3 | O01–O14, O19–O22 | InMemory Organization; expand identity/entity/query/read surfaces |
| 3 | V01, V03, V05 | Organization members only; all 11 Organization methods |
| 4 | P01–P07, P10–P12 | InMemory Rule and existing Scope Provider behavior |
| 4 | V01–V03, V05 | Rule members; Save/Get cancellation |
| 5 | D01, C13 | representation/architecture runner; all 8 payload variants |
| 6 | C01–C03 | PostgreSQL migration runner |
| 6 | C07–C08, C10–C11, P08 | architecture/base composition; base remains feature-neutral; no derived Scope Store |
| 7 | D01–D08, D11–D13 | PostgreSQL Draft shared runner; D09 inactive until feature DI exists |
| 7 | V01, V03–V05 | PostgreSQL Draft members/methods |
| 7 | F01–F02, F06, F08–F09 | Draft surface/method/field members |
| 8 | O01–O15, O19–O22 | PostgreSQL Organization shared runner; O16 inactive |
| 8 | V01, V03, V05 | PostgreSQL Organization members/all methods |
| 8 | F01–F02, F06, F08–F09 | four Org Save surfaces, 11 methods, all Org fields |
| 9 | P01–P07, P10–P13 | PostgreSQL Rule shared/corruption runners; P09 inactive |
| 9 | V01–V03, V05 | PostgreSQL Rule Save/Get members |
| 9 | F01–F02, F06, F08 | Rule Save/Get members |
| 9 | C09, C14–C15 | feature composition after all three Store classes exist; C09 expands all Save surfaces |
| 9 | D09 | Draft provider reconstruction through fresh base+feature ServiceProviders |
| 10 | D10, O16–O18, P09 | independent process restart evidence |
| 10 | F03–F05 | all six Save surfaces |
| 10 | F06 | regression rerun of all 16 already-owned tuples; no new activation/OwningSlice |
| 10 | F07 | every snapshot corruption variant |
| 10 | F09 | complete field-corruption audit if any tuple remains |
| 11 | C04–C06, C12–C13 | final architecture and native runner |

### E.1 D08 expansion must be explicit

```text
DraftIdBlank: empty + whitespace
DescriptorIdBlank: null + empty + whitespace
AuthorIdBlank: null + empty + whitespace
SupportedPayloadKindMismatch: Workflow header + Schema payload
DefinedNonPayloadKindMismatch:
    Unknown + Schema payload
    DynamicApiEndpoint + Schema payload
    McpTool + Schema payload
    AgentTool + Schema payload
PayloadIdMismatch
ProposedVersionMissing: Create + Update
ProposedVersionNotInteger
ProposedVersionMismatch
CreateBaseVersionPresent
UpdateBaseVersionMissing
DeprecateBaseVersionMissing
RemoveBaseVersionMissing
```

For every D08 row:

```text
Save -> Get -> assert exact invalid state preserved
     -> call actual IDescriptorDraftValidator
     -> assert expected existing diagnostic code
```

### E.2 P13 reachability matrix

| Variant | Intact-schema evidence | Real Store after targeted CHECK removal |
|---|---|---|
| InvalidTenantScopeKind | malformed DML rejected | row cannot equal a valid candidate; result null/no decision |
| TenantScopeTupleMismatch | malformed DML rejected | row cannot equal a valid candidate; result null/no decision |
| InvalidActionMatchKind | malformed DML rejected | row cannot equal a valid candidate; result null/no decision |
| ActionWildcardValueMismatch | malformed DML rejected | row cannot equal a valid candidate; result null/no decision |
| InvalidPermissionMatchKind | malformed DML rejected | row cannot equal a valid candidate; result null/no decision |
| PermissionWildcardValueMismatch | malformed DML rejected | row cannot equal a valid candidate; result null/no decision |
| InvalidScopeKind | malformed DML rejected | valid key hits row; materializer throws PersistedInvariantViolation |

Each no-decision test uses an otherwise empty Rule table. Do not add a valid
fallback row, because that would make a non-null result ambiguous. Do not assert
that the Store materializes a corrupt key row which its valid typed JOIN cannot
reach.

### E.3 F09 implementation rule

Each `(RowSurface, Field)` tuple gets its own corrupt-row setup and real Store
read. Do not use one theory row that mutates multiple columns at once; otherwise
the first mismatch can hide missing validation for later fields.

Nullable fields require both mismatch directions where meaningful:

```text
JSON null / column non-null
JSON non-null / column null
```

If the schema forbids one direction, use an isolated schema with the relevant
constraint removed, as in P13, then discard it.

### E.4 Concurrency barrier

The existing `BlockBeforeCommit` hook is one-shot and runs after SQL mutation;
it cannot coordinate two same-PK upserts because PostgreSQL blocks the second
writer on the first row conflict. Keep that hook unchanged for its existing
crash/failure tests.

Add a separate multi-arrival test-only probe:

```csharp
internal readonly record struct ReferenceSaveSnapshotProbe(
    string Surface,
    IReadOnlyList<string?> IdentityComponents);

internal static IDisposable BlockAfterReferenceSnapshotCaptured(
    Func<ReferenceSaveSnapshotProbe, CancellationToken, ValueTask> block);

internal static ValueTask NotifyAfterReferenceSnapshotCapturedAsync(
    ReferenceSaveSnapshotProbe probe,
    CancellationToken cancellationToken);
```

Unlike `BlockBeforeCommit`, Notify reads the callback with `Volatile.Read` and
does not consume it with `Interlocked.Exchange`; the callback remains installed
until its IDisposable is disposed. Installation remains exclusive. Every #69
Save calls Notify after `Snapshot()` and complete row/JSON construction, but
before entering `ExecuteTopLevelAsync`.

F01/F02 start two Saves against the same typed identity. The callback records
both arrivals in a two-party barrier; neither Save can execute its upsert until
both immutable rows exist. Then release both and let PostgreSQL serialize the
same-PK upserts naturally. Assert:

```text
both known commits do not throw RuntimeConcurrencyException
final state deep-equals complete snapshot A or complete snapshot B
final structured columns all agree with the chosen JSON snapshot
```

Never assert which writer wins.

### E.5 Atomic evidence-vector expansion

`Variant` names the frozen Spec dimension member; `EvidenceVectorKey` names
each atomic input required inside that member. The shared kit owns an explicit
`IReadOnlyDictionary<(CaseId, Variant), ImmutableArray<EvidenceVectorKey>>`.
The skeleton expands this table into ledger rows and exact-set compares it with
the implementation manifest. Do not infer completeness from the existence of a
Theory method or inspect its `InlineData` attributes. Do not let a runner add or
remove keys.

Use `Default` as the sole key unless one of these frozen expansions applies:

```text
D08/DraftIdBlank:
    Empty | Whitespace
D08/DescriptorIdBlank:
    Null | Empty | Whitespace
D08/AuthorIdBlank:
    Null | Empty | Whitespace
D08/SupportedPayloadKindMismatch:
    WorkflowHeaderSchemaPayload
D08/DefinedNonPayloadKindMismatch:
    Unknown | DynamicApiEndpoint | McpTool | AgentTool
D08/ProposedVersionMissing:
    Create | Update

V01/DraftNullInstance, DraftNullTenantId, DraftNullDraftId, DraftNullPayload,
    DraftGetNullTenantId, DraftGetNullDraftId, DraftListNullTenantId,
    UnitNullInstance, PositionNullInstance, MembershipNullInstance,
    RoleAssignmentNullInstance, RuleNullInstance:
    Null
V01/UnitInvalidId, PositionInvalidId, MembershipInvalidId,
    MembershipInvalidUserId, MembershipInvalidOrganizationUnitId,
    RoleAssignmentInvalidId, RoleAssignmentInvalidUserId,
    RoleAssignmentInvalidRoleId, UnitPointReadInvalidId,
    PositionPointReadInvalidId, MembershipByUserInvalidUserId,
    MembershipByUnitInvalidOrganizationUnitId, RoleByUserInvalidUserId,
    RuleInvalidResource:
    Null | Empty | Whitespace
V01/UnitInvalidNonNullTenant, PositionInvalidNonNullTenant,
    MembershipInvalidNonNullTenant, RoleAssignmentInvalidNonNullTenant,
    OrganizationQueryInvalidNonNullTenant, RuleInvalidNonNullTenant:
    Empty | Whitespace                 # null is the valid global scope
V01/MembershipInvalidPositionId,
    RoleAssignmentInvalidOrganizationUnitId:
    Empty | Whitespace                 # null is a valid optional reference
```

The V01 table is stored per full `IdentityValidationVector` name, not by a
runtime wildcard; the grouped notation above expands to one dictionary entry
per named member. The skeleton fails for an unlisted identity member, a
duplicate key, or `Default` on an expanded member.

F09 uses these additional keys:

```text
OrganizationUnit.TenantScope,
Position.TenantScope,
Membership.TenantScope,
RoleAssignment.TenantScope:
    JsonGlobalColumnsExact | JsonExactColumnsGlobal

OrganizationUnit.ParentId,
Membership.PositionId,
RoleAssignment.OrganizationUnitId:
    JsonNullColumnNonNull | JsonNonNullColumnNull

every other PersistedStructuredFieldVariant:
    Mismatch
```

Every ledger assertion includes `EvidenceVectorKey`. The runner reports the
atomic key it actually executed, so two executions cannot satisfy each other
and one execution cannot claim multiple keys.

---

# Appendix F — Red/Green Failure Expectations

For each Slice, record why the first focused test is Red:

| Slice | Expected legitimate Red |
|---|---|
| 1 | missing shared project/manifest structure |
| 2 | InMemory Draft lacks validation/cancellation/total order and Schema payload Snapshot drops ObjectSchema |
| 3 | delimiter identity and incomplete Organization ordering |
| 4 | delimiter/sentinel Rule key and untyped candidate plan |
| 5 | abstract payload/InteractionTarget cannot round-trip through generated DTO root |
| 6 | V011/tables/index-collation manifest or base-provider marker absent |
| 7 | PostgreSQL Draft Store absent |
| 8 | PostgreSQL Organization Store absent |
| 9 | PostgreSQL Rule Store/feature DI absent or P13 reachable/no-decision corruption evidence missing |
| 10 | restart/crash/commit-unknown surface evidence absent |
| 11 | native output lacks new sub-markers/final sentinel |

The following are invalid Reds and must be repaired before implementation work
continues:

- project does not restore because of a wrong relative reference;
- fixture cannot start because base migration setup was omitted;
- a test intentionally throws `NotImplementedException`;
- a future Slice wrapper was activated early;
- Docker is missing when an external PostgreSQL connection was expected;
- test data itself violates an unrelated representation precondition.

---

# Appendix G — Common Wrong Implementations

Reject a change immediately if it does any of the following:

1. Serializes `DescriptorDraft` directly through abstract `Payload`.
2. Adds `[JsonPolymorphic]`/`[JsonDerivedType]` to Domain solely for PostgreSQL.
3. Uses DescriptorKind to choose the payload DTO arm.
4. Rejects `DynamicApiEndpoint + SchemaPayload` in Save or a DB CHECK.
5. Uses `Enum.IsDefined` reflection in the AOT execution path.
6. Uses `CreatedAt`/`timestamptz` instead of `UtcTicks` for exact filtering.
7. Uses SQL order as final public order without the .NET comparer.
8. Treats null Organization collection scope as global-only.
9. Concatenates tenant/id or rule dimensions into strings.
10. Adds Organization FKs because tables are relational.
11. Loads an Organization reference to validate a Save.
12. Adds WildcardAction + ExactPermission for a non-null requested Action.
13. Treats Exact empty Action/Permission as wildcard.
14. Trusts Rule CHECK constraints without provider materialization validation.
15. Builds JSON from the caller object and structured columns from a later
    snapshot, or vice versa.
16. Runs Draft validator/materializer from persistence.
17. Calls ordinary `ExecuteAsync` for a selected Save and thereby joins an
    ambient Runtime transaction.
18. Suspends an ambient transaction and secretly commits on another connection.
19. Retries after `RuntimeTransactionCommitUnknownException` inside the Store.
20. Adds a second DataSource, schema history, transaction coordinator, or ORM.
21. Adds only one crash/corruption test and claims a whole Surface dimension.
22. Runs a JIT host and labels it NativeAOT evidence.

When uncertain, stop and compare the proposed behavior against the frozen Case
ID and dimension. Do not resolve ambiguity by adding a fallback path.

---

# Appendix H — Slice Handoff Template

Copy this block into every implementation handoff:

```text
Issue: #69
Slice completed:
Commit SHA:
Baseline migration tail verified: V010/V011 as applicable

Files added:
Files modified:
Public API changes: none / list and justify

Activated Case IDs:
Activated evidence tuples (including EvidenceVectorKey):
Red command:
Red reason:
Green commands and exact counts:

Architecture/boundary command:
git diff --check result:
NativeAOT result: not applicable / exact publish and sentinel output

Known environment limitations:
Unresolved findings: must be zero
Next Slice:
Shared hotspots touched for next Agent to re-verify:
```

---

# Appendix I — File-by-File Definition of Done

This is the final implementation checklist for a weaker Agent. Do not mark a
file complete merely because it compiles.

## I.1 `DescriptorDraftStoreSemantics.cs`

Must contain:

```text
ValidateSaveInput(DescriptorDraft)
ValidateGetInput(string tenantId, string draftId)
ValidateListInput(string tenantId, DraftQuery?)
MatchesQuery(DescriptorDraft, DraftQuery?)       # used by InMemory/tests
Order(IEnumerable<DescriptorDraft>)              # DraftId Ordinal
```

Because the Abstractions project cannot reference the six concrete payload
classes, this helper validates only abstraction-owned fields/enums. Add
`DescriptorDraftPayloadSupport.EnsureSupported` in the DescriptorDraft
implementation with an explicit six-arm type switch; the InMemory Store calls
it. The PostgreSQL codec has its own required six-arm DTO switch because payload
dispatch is provider representation, not a shared semantic helper. Its
`GetSupportedPayloadType(originalPayload)` executes before `draft.Snapshot()`;
the later `Capture(snapshot, payloadType)` does not replace that precheck.

Done when:

- all Draft representation validation branches have direct V01/V03/V04 tests;
- all D08 values pass validation and reach Store round trip;
- CreatedFrom/To use UtcTicks;
- no reference to `IDescriptorDraftValidator` exists in the file.

`DescriptorDraftPayloadSupport.cs` is done when it accepts exactly the six
current public payload records, rejects a test-only seventh subtype before
mutation, leaves that subtype's `SnapshotCallCount` at zero, and contains no
reflection/assembly scan. PostgreSQL V04 independently asserts the same zero
counter through the provider-local precheck.

## I.2 `InMemoryDescriptorDraftStore.cs`

Done when every public method:

- starts with cancellation check;
- delegates validation/query/order to shared semantics;
- captures/stores/returns snapshots;
- has no runtime reflection, database concern, or validator call;
- passes the old focused tests and new shared runner.

## I.3 Organization abstraction helpers

`OrganizationScopedKey.cs` must provide only structural scope/key creation and
ordinal comparison. `OrganizationStoreSemantics.cs` must provide validation and
the four canonical comparers. `DataPermissionRuleSemantics.cs` must provide
typed keys and candidate generation.

Add narrow friend declarations in the Abstractions csproj. Do not make these
types public simply so the Provider can call them.

Done when DependencyBoundaries confirms neither abstraction project references
PostgreSQL/Npgsql or its concrete implementation.

## I.4 Organization implementation files

`InMemoryOrganizationStore.cs`:

```text
4 ConcurrentDictionary<OrganizationScopedKey, Entity>
0 string composite keys
all Saves snapshot
all Reads detach
all Lists canonical-order
```

`DefaultOrganizationHierarchyService.cs`:

```text
unitMap:     Dictionary<OrganizationScopedKey, OrganizationUnit>
childrenMap: Dictionary<OrganizationScopedKey, List<OrganizationScopedKey>>
visited:     HashSet<OrganizationScopedKey>
queue:       Queue<OrganizationScopedKey>
```

Never call `CompKey`. Child lists inherit canonical Unit order before BFS.

`DefaultOrganizationIdentityService.cs` must order active memberships/roles
with shared comparers before Primary and `Distinct` projections.

`InMemoryDataPermissionScopeRuleStore.cs` must have a typed dictionary and call
the shared candidate plan; it must contain no `"::"` or `"*"` sentinel key.

## I.5 Provider support

`PostgreSqlControlPlaneReferenceDataStoreSupport.cs` must centralize:

```text
StateContractVersion = 1
quoted Table(options, name)
NormalizeReadableTimestamp
AddJsonParameter
ExecuteReadAsync
Invariant(message)
tenant scope parameter helpers
persisted enum/discriminator readers
```

Do not duplicate these in three Store files. Do not modify Agent Memory support
to depend on the new domains; the new support may reuse truly generic Runtime
helpers but remains independently named.

`PostgreSqlRuntimeTestHooks.cs` gains the separate multi-arrival
after-reference-snapshot probe defined in Appendix E.4. Its reset type clears
only that callback. Do not change the one-shot behavior of existing command,
before-COMMIT, or write-point probes; existing #24/#55 tests depend on them.

## I.6 DTO/context/codec files

`PostgreSqlControlPlaneReferenceDataPersistenceDtos.cs` contains data shapes
only—no Npgsql, DI, Store, or validator calls.

`PostgreSqlControlPlaneReferenceDataJsonSerializerContext.cs` contains source
generation declarations only.

`PostgreSqlDescriptorDraftSnapshotCodec.cs` contains all six payload switches,
the Workflow nested switch, exact one-arm checks, domain reconstruction, and
persisted-column comparison. It exposes the pre-snapshot six-arm discriminator
lookup separately from capture; unsupported CLR payload rejection occurs before
any virtual Snapshot call.

`PostgreSqlOrganizationSnapshotCodec.cs` contains four explicit generated
serialize/deserialize paths and row-field validators. Do not deserialize with a
runtime `Type` chosen from a string.

## I.7 PostgreSQL Store classes

`PostgreSqlDescriptorDraftStore` implements only `IDescriptorDraftStore`.

`PostgreSqlOrganizationStore` implements only `IOrganizationStore`.

`PostgreSqlDataPermissionScopeRuleStore` implements only
`IDataPermissionScopeRuleStore`.

Constructor dependencies are limited to:

```text
PostgreSqlRuntimePersistenceOptions
NpgsqlDataSource                     # Reads
PostgreSqlRuntimeTransactionCoordinator  # Saves
```

plus no domain service. Stores do not inject Validator, Hierarchy, Identity,
ScopeProvider, Audit, or service provider.

Each Save executes exactly one upsert command inside exactly one top-level
transaction. Each Read opens one direct connection and never mutates state.
Immediately after immutable row construction and before the top-level
transaction, every Save calls the after-reference-snapshot probe with a stable
surface name and separate structural identity components intended only for test
diagnostics. The hook must not concatenate them into a Store key.

## I.8 DI extension

Implement this extension in Slice 9 only, after
`PostgreSqlDescriptorDraftStore`, `PostgreSqlOrganizationStore`, and
`PostgreSqlDataPermissionScopeRuleStore` compile. Slice 6 registers only the
base marker and proves base neutrality; do not create placeholder Store shells
or `NotImplementedException` registrations to make DI tests compile early.

Implementation order inside
`AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence`:

```text
1. ArgumentNullException.ThrowIfNull(services)
2. detect PostgreSqlRuntimeProviderRegistrationMarker
3. confirm Options, NpgsqlDataSource, MigrationRunner, and Coordinator descriptors
4. if marker or any core descriptor is absent:
       throw clear InvalidOperationException naming required base method
5. RemoveAll<IDescriptorDraftStore>()
6. RemoveAll<IOrganizationStore>()
7. RemoveAll<IDataPermissionScopeRuleStore>()
8. AddSingleton one PostgreSQL implementation per interface
9. return same IServiceCollection
```

Repeated feature calls must leave exactly one descriptor for each Store. Base
registration itself remains feature-neutral.

The base extension registers exactly one internal marker. C14 includes a
negative setup which manually registers only Options; feature opt-in must still
fail because that is not a complete Provider Kernel.

## I.9 Migration runner

Done only when:

- Catalog ends V010, V011 and no earlier entry changed;
- V011 checksum is stable;
- all six tables appear in required inventory and full manifest;
- manifest checks all columns/PK/checks/indexes/index-key collations/FKs;
- validation-only mode detects every C03 drift vector;
- reapply creates no second history row;
- no new migration runner/history/lock is introduced.

## I.10 Shared test kit

The kit must compile with no test SDK. Assertions throw the kit's own assertion
exception. Cases are normal async methods. xUnit attributes exist only in runner
projects.

The four driver interfaces and every type appearing in their public signatures
are `public`, including all variant/result/scenario types and the complete
payload-observation model. This is a testing-contract assembly deliberately
consumed by external runner assemblies. Helpers which do not cross that API
boundary remain `internal`; do not add runner `InternalsVisibleTo` entries.

Every manifest tuple has:

```text
CaseId
Surface
Variant
EvidenceVectorKey
RequiredRunner
OwningSlice
NormativeTestName
```

The skeleton compares the manifest with frozen Spec constants and the explicit
Appendix E.5 atomic expansion table. A missing atomic vector must fail even if
a Theory method for its parent Variant exists. `Default` is legal only for a
non-expanded Variant, and each executed case reports exactly one
`EvidenceVectorKey` into the ledger.

`DescriptorPayloadObservation.cs` contains only shared-kit value records and a
closed scalar `ObservationValueKind`; it does not reference concrete payload or
descriptor classes. `ControlPlaneReferenceDataContractFixtures` owns one exact
expected observation for every `DescriptorPayloadVariant`, covering every path
in Appendix C.3. D01 compares the driver observation after Get with that
expected tree exactly. Merely checking payload CLR type, descriptor ID, record
reference equality, or serialized JSON text does not satisfy D01.

## I.11 PostgreSQL test drivers

All three drivers share one `PostgreSqlRuntimeSchemaLease` per test but expose
only their own Store to shared cases. Driver reset uses a fresh isolated schema
or truncates only its owned V011 tables; never truncates existing Runtime,
pre-dispatch, or Agent Memory tables in a shared fixture.

Durability reconstruction disposes the ServiceProvider/DataSource, constructs a
new ServiceProvider with the same options/schema, registers base then feature,
and resolves a new Store instance. Merely resolving a second Store from the same
provider is not restart evidence.

## I.12 CrashWorker/AOT

`CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj` contains a
CrashWorker ProjectReference with `ReferenceOutputAssembly="false"`. The shared
path resolver finds repository root, reads the active configuration from the
test assembly path (`bin/{Configuration}/net10.0`), and returns the matching
worker DLL. No crash test contains `bin/Debug` or `bin/Release` literals.

CrashWorker scenario output must include surface, scenario, and logical ID so
the parent cannot read the wrong row. Flush stdout before waiting to be killed.
The parent kills the entire process tree and waits for PostgreSQL backend exit.

AotHost must execute the same Provider/codec code as production. Do not add an
AOT-only serializer, fake Store, in-memory fallback, or test-only payload DTO.
The AOT fixture asserts every sub-marker before accepting the final marker.

---

# Appendix J — Stop-and-Reconcile Conditions

Stop the active Slice and request design/merge reconciliation if any of these is
observed:

- migration tail is no longer V010 before Slice 6;
- a Store interface or enum changed after Spec freeze;
- a seventh DescriptorDraftPayload subtype or fourth InteractionTarget subtype
  exists;
- Organization null-tenant tests changed meaning;
- Provider Kernel transaction/failure APIs changed incompatibly;
- the required new dependency would violate enforced project boundaries;
- a frozen Case cannot be implemented without changing public contracts;
- NativeAOT requires reflection suppression or a runtime fallback;
- existing user changes overlap a Slice hotspot and cannot be preserved.

Do not silently adapt the frozen semantics, edit an older migration, weaken a
test, skip a Case, or introduce compatibility fallback to continue.
