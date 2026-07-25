# Control Plane JSON Contract Root Generation Implementation Plan

> **For implementers:** Execute this plan task-by-task. Do not skip the RED
> assertion, package restore, source-deletion, Global Usings, transport
> conflict, or NativeAOT gates. Checkboxes are the execution record.

**Goal:** Replace the handwritten Control Plane JSON direct-root list with a
deterministic pre-`CoreCompile` contract-surface generator while preserving the
official STJ Source Generator as the only `JsonTypeInfo` implementation,
preserving the Control Plane wire contract, and proving the build package under
clean restore and NativeAOT publish-and-run.

**Architecture:** A public marker in `Core.Abstractions` declares explicit
interface surfaces on a handwritten `JsonSerializerContext`. A dedicated
`CrestCreates.JsonContracts.BuildTasks` assembly runs after SDK compile inputs,
including `GenerateGlobalUsings`, and before `CoreCompile`. It builds a minimal
Roslyn compilation, infers direct roots, writes ordinary deterministic `.g.cs`
under `obj`, and lets the official STJ generator consume those attributes in
the same formal compilation. A generated manifest exposes Surface, Explicit,
and All direct-root sets with Internal-by-default accessibility. Runtime
reflection, assembly scanning, SG-to-SG chaining, and resolver fallback remain
forbidden.

**Tech stack:** .NET 10, MSBuild 18, Roslyn semantic APIs, System.Text.Json
source generation, xUnit 2.9.3, FluentAssertions, NativeAOT linux-x64 fixture.

**Spec:** `docs/superpowers/specs/2026-07-24-control-plane-json-contract-root-generation-design.md`

**Issue:** #58

**Branch:** `feature/issue-58-json-contract-root-generation`

**Spec status:** APPROVED

**Plan status:** APPROVED FOR IMPLEMENTATION

---

## 1. Execution rules

- Use `rtk` for every shell command.
- Do not delete or overwrite unrelated files.
- Delete only files that this Plan explicitly marks obsolete, using normal Git
  deletion. This Plan does not delete existing production source files.
- Generated fixture directories under `/tmp` may be cleaned by their owning
  test fixture.
- Use `apply_patch` for repository file edits.
- Use `CrestCreates.slnx`; do not create a `.sln`.
- Add package versions only in `Directory.Packages.props`.
- Do not add `Version` attributes to normal `PackageReference` entries.
- Do not add Roslyn to `CrestCreates.BuildTasks`; #58 owns a separate package.
- Do not modify `CrestCreates.CodeGenerator` to emit JSON context attributes.
- Do not run any runtime assembly scan or `Assembly.GetTypes()` on the
  production path.
- Do not introduce `DefaultJsonTypeInfoResolver`.
- Do not auto-register Agent or MCP JSON contributors.
- Do not use a ProjectReference as the package-contract proof.
- Do not treat analyzer success or `PublishTrimmed` as NativeAOT evidence.
- Run the focused tests immediately after each task.
- A task is complete only after its tests pass and `git diff --check` passes.
- Keep commits slice-scoped. Do not combine acceptance skeleton, semantic
  engine, package, Control Plane migration, and AOT proof in one commit.

### 1.1 RED-GREEN protocol

For every behavior task:

1. Activate the named acceptance test by removing its
   `AcceptanceSkeleton.Pending` skip.
2. Replace its placeholder with the real assertion.
3. Run only that test or test class.
4. Confirm it fails for the expected reason, not because the fixture is broken.
5. Implement the smallest correct production change.
6. Rerun the focused test.
7. Run the task-level regression set.
8. Commit only after GREEN.

Initial skeleton tests may be skipped only with this exact marker:

```csharp
[Fact(Skip = AcceptanceSkeleton.Pending)]
```

The final gate searches for `AcceptanceSkeleton.Pending` and fails if any
placeholder remains.

### 1.2 Mainline ownership

```text
JsonContractSurfaceAttribute
    → semantic MSBuild Task
    → obj/CrestCreates.JsonContracts.g.cs
    → official STJ Source Generator
    → JsonTypeInfo<T>
```

The following are not implementation alternatives:

```text
× CrestCreates ordinary SG → STJ SG
× assembly-wide public DTO scan
× runtime reflection root discovery
× custom JsonTypeInfo generator
× reflection serializer fallback
× second Agent/MCP contributor registry
```

---

## 2. Acceptance-first requirement matrix

The Case Matrix defines feature correctness. Production code is not the
requirement source; these cases are.

### 2.1 Happy cases

| ID | Case | Expected | Primary test | Implemented in |
|---|---|---|---|---|
| H01 | New request and result Tool method | request root and closed result-envelope root are generated in the same build and after rebuild | `Build_AddMethodThenRebuildUpdatesManifest` | Tasks 4, 9 |
| H02 | Inherited sub-interface method | inherited method is collected recursively | `Build_InheritedInterfaceMethods` | Task 4 |
| H03 | `Task<T>` and `ValueTask<T>` | exactly one layer is unwrapped | `Build_UnwrapsTaskAndValueTask` | Task 4 |
| H04 | `Task`, `ValueTask`, `void` | no return root | `Build_TaskValueTaskAndVoidProduceNoReturnRoot` | Task 4 |
| H05 | Manifest provider list and single result | collection root, item root, and string parameter are generated | `GeneratedSurfaceRoots_MatchExpectedToolAndManifestSurfaces` | Tasks 4, 11 |
| H06 | Ordinary pre-CoreCompile attributes | STJ consumes them and produces JsonTypeInfo in the same formal compilation | `Build_GeneratedAttributesParticipateInSameCompilation`, `Build_StjGeneratorProducesJsonTypeInfoForGeneratedRoots` | Task 8 |
| H07 | Clean package consumer | first restore/build succeeds without a task ProjectReference and receives only task/build assets | `Build_CleanCheckoutSucceedsOnFirstInvocation`, `Pack_LocalFeedConsumerGetsTaskAndTargetsOnly` | Task 10 |
| H08 | Control Plane representative roots | request, result, manifest, and extras round-trip without wire drift | `RepresentativeToolDtos_RoundTrip`, `DescriptorParser_AcceptsToolOutput` | Task 12 |
| H09 | NativeAOT | publish links and original native binary executes its named fail-closed and round-trip scenarios | `PublishAndRun_ControlPlaneJsonContracts` | Task 13 |

### 2.2 Boundary cases

| ID | Case | Expected | Primary test | Implemented in |
|---|---|---|---|---|
| B01 | Multiple business parameters | every non-excluded parameter is a root | `Build_AllBusinessParameters` | Task 4 |
| B02 | Exact infrastructure exclusion | exact configured type is excluded; assignable types are not | `Build_ExcludesInfrastructureParameters`, `Build_ExcludesConfiguredInfrastructureTypesByExactIdentity` | Task 4 |
| B03 | CancellationToken | always excluded without attribute configuration | `Build_ExcludesCancellationToken` | Task 4 |
| B04 | Excluded type appears as return | exclusion does not suppress return root | `Build_ExcludedTypesDoNotSuppressReturnRoots` | Task 4 |
| B05 | Scalar, enum, collection, nullable, closed generic | all are normal direct roots | `Build_IncludesClosedGenericAndScalarRoots`, `Build_IncludesClosedGenericCollectionScalarEnumAndNullableRoots` | Task 4 |
| B06 | `AgentToolResult<string>` | generated without BCL special case | `AgentToolResultOfString_RoundTripsWithoutSpecialCase` | Tasks 4, 12 |
| B07 | Nested DTO member added | direct-root output is unchanged; STJ handles transitive metadata | `Build_DoesNotExpandTransitiveDtoMembers` | Tasks 4, 8 |
| B08 | Diamond inheritance | method and root appear once | `Build_DiamondInheritance_DeduplicatesMethods` | Task 4 |
| B09 | Root used by multiple methods | one attribute, all deterministic provenance comments | `Build_TracksAllMethodProvenanceForSharedRoot` | Tasks 4, 6 |
| B10 | Handwritten explicit root duplicates surface root | no duplicate generated attribute; both manifest provenance sets contain it | `Build_DoesNotDuplicateExplicitExtras`, `Build_ExplicitRootStillAppearsInManifestUnion` | Tasks 5, 6 |
| B11 | No marked context | deterministic empty file overwrites stale output | `Build_NoMarkedContextWritesDeterministicEmptyOutput` | Tasks 3, 6 |
| B12 | Implicit usings only | provisional compilation resolves `Task<T>`, `CancellationToken`, `IReadOnlyList<T>` after `GenerateGlobalUsings` | `Build_ImplicitUsingsOnlySurfaceBindsAfterGenerateGlobalUsings` | Task 8 |
| B13 | Source deletion | input-set manifest changes and stale root disappears | `Build_SourceDeletionInvalidatesGeneration` | Task 9 |
| B14 | Semantic output unchanged | `.g.cs` timestamp stays unchanged while completion stamp advances | `Build_UnchangedSemanticOutputDoesNotRewriteTimestamp` | Task 9 |
| B15 | Multi-TFM | output, input manifest, and stamp are isolated per inner build | `Build_MultiTargetingProducesIndependentOutputs` | Task 9 |
| B16 | Design-time build | reuses existing output; does not become correctness authority | `Build_DesignTimeReusesExistingGeneratedFile` | Task 9 |
| B17 | Internal manifest default | Control Plane and friend tests can consume it; external assembly cannot | `Build_InternalManifestAccessibility`, `Build_InternalManifestRemainsAssemblyScoped` | Tasks 6, 8, 12 |
| B18 | Public manifest opt-in | separate assembly can consume immutable generated sets | `Build_PublicManifestAccessibility`, `Build_PublicManifestSetsAreImmutable`, `Build_PublicManifestIsConsumableFromSeparateAssembly` | Tasks 6, 8 |
| B19 | Runtime output isolation | task and Roslyn assemblies never enter `bin` or publish output | `Build_TaskDependenciesDoNotLeakToRuntimeOutput`, `Pack_TaskDependenciesDoNotLeakToPublish` | Task 10 |

### 2.3 Failure cases

| ID | Case | Expected diagnostic/result | Primary test | Implemented in |
|---|---|---|---|---|
| F01 | Non-partial, nested, generic, or non-context target | `CJC001`, build stops | `Fail_NonPartialOrNestedContext` plus focused variants | Task 3 |
| F02 | Missing/non-interface/unbound surface | `CJC002`, build stops | `Fail_NonInterfaceOrUnboundSurface` | Task 3 |
| F03 | Generic method | `CJC003`, build stops | `Fail_GenericMethod` | Task 4 |
| F04 | `ref`, `out`, `in`, pointer, function pointer, ref-like parameter | `CJC004`, build stops | `Fail_OpenGenericOrRefLikeRoot` plus focused by-ref variants | Task 4 |
| F05 | Open/unbound/ref return root | `CJC005`, build stops | `Fail_OpenGenericOrRefLikeRoot`, `Fail_ByRefReturn`, `Fail_ByRefReadonlyReturn` | Task 4 |
| F06 | Inaccessible root | `CJC006`, build stops | `Fail_InaccessibleRoot` | Task 4 |
| F07 | ErrorType / same-project SG-only root | `CJC007` with remediation, no silent skip | `Build_SameProjectSourceGeneratorOnlyRootFailsClearly` | Tasks 4, 8 |
| F08 | Manifest name collision | `CJC008`, build stops | `Fail_ManifestNameCollision` | Task 6 |
| F09 | Source read/syntax-tree construction failure | `CJC009`, build stops | `Fail_UnreadableSource` | Task 7 |
| F10 | Metadata reference load failure | `CJC010`, build stops | `Fail_UnreadableMetadataReference` | Task 7 |
| F11 | Marker/STJ identity unresolved | `CJC011`, build stops | `Fail_UnresolvedMarkerOrStjIdentity` | Task 3 |
| F12 | Unsafe/outside output path or safe write failure | `CJC012`, no outside write and previous output remains intact | `Fail_GeneratedPathOutsideIntermediateOutputPath`, `Fail_OutputWritePreservesPreviousFile` | Tasks 7, 8 |
| F13 | Manifest accessibility not Internal/Public | `CJC013`, build stops | `Fail_InvalidManifestAccessibility` | Task 5 |
| F14 | Repository and package transports compete | `CJC014` before the first custom task invocation, manifest/source write, stamp update, or Compile inclusion | `Build_RepositoryAndPackageTransportConflictFailsBeforeGeneration` | Task 10 |
| F15 | Generated source removed while stamp remains | stamp invalidated and regeneration forced | `Build_MissingGeneratedSourceInvalidatesStamp` | Task 9 |
| F16 | STJ rejects generated contract shape | formal compiler/STJ diagnostic stops build; task does not hide it | `Build_StjDiagnosticStillFailsFormalCompilation` | Task 8 |

### 2.4 Composition cases

| ID | Case | Expected | Primary test | Implemented in |
|---|---|---|---|---|
| C01 | Multiple surfaces on one context | roots merge, deduplicate, sort | `Build_MultipleSurfacesMergeDeterministically` | Tasks 4, 6 |
| C02 | Multiple contexts in one project | contexts and manifests remain isolated and ordered | `Build_MultipleContextsAreIsolatedAndSorted` | Tasks 3, 6 |
| C03 | Surface + Explicit root union | `AllDirectRootTypes = Surface ∪ Explicit` | `AllDirectRoots_EqualSurfaceUnionExplicit` | Tasks 5, 12 |
| C04 | Direct package consumer | package build assets run once | `Pack_ExactlyOnePackageTransportAndTargetsSetIsActive` | Task 10 |
| C05 | Transitive package consumer | `build/` assets do not flow; generation does not run | `Pack_TransitiveConsumerDoesNotRunGeneration` | Task 10 |
| C06 | Duplicate exact import | guarded single effective target set; no duplicate Compile item | `Build_DuplicateImportCannotRunGenerationOrAddCompileTwice` | Task 10 |
| C07 | Agent/MCP runtime composition | contributor order, ownership, opt-in, freezing unchanged | existing Agent/MCP tests plus `ControlPlaneMigration_DoesNotAddJsonContributor` | Tasks 11, 14 |
| C08 | Multiple TFM and configuration builds | no cross-inner-build cache or source contamination | `Build_MultiTargetingProducesIndependentOutputs`, `Build_DebugAndReleaseOutputsAreIsolated` | Task 9 |

---

## 3. Acceptance test skeleton

The skeleton is created before semantic production behavior. It fixes names,
fixtures, grouping, and requirement ownership.

### 3.1 Test layers

| Layer | Project | Authority |
|---|---|---|
| Pure semantic/model | `CrestCreates.JsonContracts.BuildTasks.Tests` | symbol discovery, inference, diagnostics, writer bytes |
| Real MSBuild contract | `CrestCreates.JsonContracts.Build.PackageTests` | target order, Global Usings, same-compilation STJ, incremental behavior |
| NuGet package contract | `CrestCreates.JsonContracts.Build.PackageTests` | pack/local-feed/restore/direct-vs-transitive/no leakage |
| Real Control Plane contract | existing `CrestCreates.Agent.ControlPlane.Tests` | generated manifest, JsonTypeInfo, round-trip, explicit Extras |
| Native executable | AOT fixture + fixture tests | native link and execution without reflection fallback |

### 3.2 Pure test infrastructure

Create:

```text
tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests/
├── AcceptanceSkeleton.cs
├── Contracts/
│   └── JsonContractSurfaceAttributeTests.cs
├── Infrastructure/
│   ├── JsonContractCompilationTestBase.cs
│   ├── JsonContractTestCompilation.cs
│   ├── JsonContractTestSources.cs
│   ├── JsonContractDiagnosticAssertions.cs
│   └── GeneratedSourceAssertions.cs
├── Semantic/
│   ├── ContextDiscoveryTests.cs
│   ├── SurfaceInferenceHappyTests.cs
│   ├── SurfaceInferenceBoundaryTests.cs
│   ├── SurfaceInferenceFailureTests.cs
│   └── SurfaceCompositionTests.cs
├── Generation/
│   ├── JsonContractSourceWriterTests.cs
│   └── JsonContractManifestWriterTests.cs
└── Incremental/
    ├── JsonContractInputManifestWriterTests.cs
    └── WriteIfChangedFileTests.cs
```

`JsonContractCompilationTestBase` must:

- build references from `TRUSTED_PLATFORM_ASSEMBLIES` in stable path order;
- add explicit references for `JsonSerializerContext`,
  `JsonContractSurfaceAttribute`, `Task`, and `ValueTask`;
- parse with caller-supplied `LanguageVersion`, preprocessor symbols, and
  nullable settings;
- accept multiple named source files so source deletion and partial declarations
  can be modeled;
- return the provisional `CSharpCompilation`, discovered contexts, diagnostics,
  and generated bytes separately;
- never run the official STJ generator in pure model tests.

`JsonContractTestSources` must expose small source factories:

```text
MinimalContext(surfaceSource, contextOptions)
InheritedSurface(...)
DiamondSurface(...)
MultipleParameterSurface(...)
ExplicitDuplicateSurface(...)
MultipleContextProject(...)
InvalidContext(...)
SameProjectUnresolvedType(...)
```

Factories use the exact marker namespace:

```text
CrestCreates.Core.Abstractions.Serialization
```

### 3.3 Real build/package fixture

Create:

```text
tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests/
├── AcceptanceSkeleton.cs
├── JsonContractBuildCollection.cs
├── Infrastructure/
│   ├── JsonContractContractTestBase.cs
│   ├── JsonContractBuildFixture.cs
│   ├── ConsumerProjectBuilder.cs
│   ├── DotNetProcess.cs
│   ├── DotNetProcessResult.cs
│   ├── PackageLayoutAssertions.cs
│   └── FileSnapshot.cs
├── Happy/
│   └── SameCompilationContractTests.cs
├── Boundary/
│   ├── GlobalUsingsContractTests.cs
│   ├── IncrementalContractTests.cs
│   ├── MultiTargetingContractTests.cs
│   └── ManifestAccessibilityContractTests.cs
├── Failure/
│   ├── BuildDiagnosticContractTests.cs
│   ├── SameProjectGeneratorBoundaryTests.cs
│   └── TransportConflictContractTests.cs
├── Composition/
│   ├── MultiSurfaceContractTests.cs
│   └── ImportCompositionContractTests.cs
└── Package/
    ├── PackageLayoutContractTests.cs
    ├── LocalFeedConsumerContractTests.cs
    └── PackageLeakageContractTests.cs
```

The package test project must not reference
`CrestCreates.JsonContracts.BuildTasks.csproj`.

`JsonContractBuildFixture` must:

- locate the repository root by finding `Directory.Build.props`;
- create a unique root under
  `Path.Combine(Path.GetTempPath(), "crest-json-contracts-" + guid)`;
- own `feed/`, `packages/`, `projects/`, `publish/`, and `logs/` subdirectories;
- build and pack the task project through a subprocess when requested;
- use `--disable-build-servers`;
- set `MSBUILDDISABLENODEREUSE=1`;
- set a fixture-local `NUGET_PACKAGES`;
- capture stdout and stderr without deadlock;
- enforce a five-minute command timeout;
- include full command/output in assertion failures;
- clean only its own temporary directory in `DisposeAsync`.

`JsonContractContractTestBase` must expose:

```csharp
protected Task<ProjectLayout> CreateRepositoryConsumerAsync(ConsumerSpec spec);
protected Task<ProjectLayout> CreatePackageConsumerAsync(ConsumerSpec spec);
protected Task<DotNetProcessResult> BuildAsync(ProjectLayout project, params string[] args);
protected Task<DotNetProcessResult> RebuildAsync(ProjectLayout project, params string[] args);
protected Task<DotNetProcessResult> CleanAsync(ProjectLayout project);
protected Task<DotNetProcessResult> PublishAsync(ProjectLayout project, string output);
protected string ReadGeneratedSource(ProjectLayout project, string tfm, string configuration = "Debug");
protected string ReadInputManifest(ProjectLayout project, string tfm, string configuration = "Debug");
protected FileSnapshot SnapshotGeneratedFile(ProjectLayout project, string tfm);
protected void AssertNoTaskAssemblies(string directory);
```

`ConsumerSpec` must model:

- repository or package transport;
- one or more source files;
- `TargetFramework` or `TargetFrameworks`;
- `ImplicitUsings`;
- nullable and language version;
- manifest accessibility;
- optional earlier MSBuild target;
- optional ordinary test Source Generator;
- optional duplicate/mixed import;
- expected output marker.

### 3.4 Control Plane Contract Test base

Create:

```text
tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/JsonContracts/
├── ControlPlaneJsonContractTestBase.cs
├── GeneratedRootManifestTests.cs
├── ControlPlaneJsonRoundTripTests.cs
└── ExplicitExtraJsonContractTests.cs
```

`ControlPlaneJsonContractTestBase` owns:

- `AgentControlPlaneToolJsonSerializerOptions.CreateDefault()`;
- deterministic representative `CanonicalHash`;
- deterministic `DescriptorActivationReviewDecision`;
- representative `DescriptorSearchRequest`;
- representative `AgentToolResult<string>`;
- representative `AgentToolDescriptor` and manifest list;
- helpers that serialize/deserialize only with typed `JsonTypeInfo<T>` or the
  source-generated resolver;
- JSON property-name assertions for camelCase and null omission.

It must not:

- scan the assembly;
- reflect service signatures;
- inspect all public records;
- maintain a known-exclusion list;
- recursively reconstruct member graphs.

### 3.5 Skeleton activation gate

The initial test files contain all names from Section 2 and the Spec Acceptance
Test Skeleton. Placeholder bodies reference no not-yet-created production types
and are skipped only with `AcceptanceSkeleton.Pending`.

At the end of every production task:

```bash
rtk rg -n "AcceptanceSkeleton.Pending" \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests \
  tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests \
  tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/JsonContracts
```

Expected: only tests assigned to later tasks remain.

Final expected result: no matches.

### 3.6 Approved Acceptance Test Skeleton name ledger

The names below come from Issue #58 and its approved design/plan audit. They are
compatibility names for the acceptance language used during review. They must
exist exactly as written; more focused tests from Section 2 supplement them but
do not replace them.

| Exact Issue test name | Test file | Case IDs | Activate in |
|---|---|---|---|
| `Build_InheritedInterfaceMethods` | `SurfaceInferenceHappyTests.cs` | H02 | Task 4 |
| `Build_AllBusinessParameters` | `SurfaceInferenceBoundaryTests.cs` | B01 | Task 4 |
| `Build_UnwrapsTaskAndValueTask` | `SurfaceInferenceHappyTests.cs` | H03 | Task 4 |
| `Build_IncludesClosedGenericAndScalarRoots` | `SurfaceInferenceBoundaryTests.cs` | B05 | Task 4 |
| `Build_ExcludesInfrastructureParameters` | `SurfaceInferenceBoundaryTests.cs` | B02, B03 | Task 4 |
| `Build_DoesNotExpandTransitiveDtoMembers` | `SurfaceInferenceBoundaryTests.cs` | B07 | Task 4 |
| `Build_DeduplicatesOverloadsAndRepeatedTypes` | `SurfaceInferenceBoundaryTests.cs` | B08, B09 | Task 4 |
| `Build_DoesNotDuplicateExplicitExtras` | `SurfaceInferenceBoundaryTests.cs` | B10 | Task 5 |
| `Fail_OpenGenericOrRefLikeRoot` | `SurfaceInferenceFailureTests.cs` | F04, F05 | Task 4 |
| `Fail_ByRefReturn` | `SurfaceInferenceFailureTests.cs` | F05 | Task 4 |
| `Fail_ByRefReadonlyReturn` | `SurfaceInferenceFailureTests.cs` | F05 | Task 4 |
| `Fail_NonPartialOrNestedContext` | `ContextDiscoveryTests.cs` | F01 | Task 3 |
| `Write_IsByteStable` | `JsonContractSourceWriterTests.cs` | C01, C02 | Task 6 |
| `Build_GeneratedAttributesParticipateInSameCompilation` | `SameCompilationContractTests.cs` | H06 | Task 8 |
| `Build_StjGeneratorProducesJsonTypeInfoForGeneratedRoots` | `SameCompilationContractTests.cs` | H06 | Task 8 |
| `Build_CleanCheckoutSucceedsOnFirstInvocation` | `SameCompilationContractTests.cs` | H06, H07 | Task 10 |
| `Build_AddMethodThenRebuildUpdatesManifest` | `IncrementalContractTests.cs` | H01, B14 | Task 9 |
| `Build_RemoveMethodThenRebuildRemovesRoot` | `IncrementalContractTests.cs` | B13 | Task 9 |
| `Build_SourceDeletionInvalidatesGeneration` | `IncrementalContractTests.cs` | B13 | Task 9 |
| `Build_MultiTargetingProducesIndependentOutputs` | `MultiTargetingContractTests.cs` | B15, C08 | Task 9 |
| `Fail_GeneratedPathOutsideIntermediateOutputPath` | `BuildDiagnosticContractTests.cs` | F12 | Task 8 |
| `Build_PublicManifestSetsAreImmutable` | `ManifestAccessibilityContractTests.cs` | B18 | Task 8 |
| `Build_TaskDependenciesDoNotLeakToRuntimeOutput` | `PackageLeakageContractTests.cs` | B19 | Task 10 |
| `Pack_LocalFeedConsumerGetsTaskAndTargetsOnly` | `LocalFeedConsumerContractTests.cs` | H07, C04, C05 | Task 10 |
| `GeneratedSurfaceRoots_MatchExpectedToolAndManifestSurfaces` | `GeneratedRootManifestTests.cs` | H05, C03 | Task 11 |
| `EveryGeneratedRoot_HasJsonTypeInfo` | `GeneratedRootManifestTests.cs` | H06, H08 | Task 12 |
| `ExplicitExtras_HaveJsonTypeInfo` | `ExplicitExtraJsonContractTests.cs` | B10, C03 | Task 12 |
| `RepresentativeToolDtos_RoundTrip` | `ControlPlaneJsonRoundTripTests.cs` | H08 | Task 12 |
| `DescriptorParser_AcceptsToolOutput` | `ExplicitExtraJsonContractTests.cs` | H08 | Task 12 |
| `NoAssemblyWideJsonSerializableFallbackRemains` | `GeneratedRootManifestTests.cs` | C07 | Task 12 |
| `PublishAndRun_ControlPlaneJsonContracts` | `ControlPlaneJsonContractAotFixtureTests.cs` | H09 | Task 13 |
| `ReflectionFallback_IsDisabled` | AOT fixture `Program.cs` scenario invoked by `PublishAndRun_ControlPlaneJsonContracts` | H09 | Task 13 |
| `SerializeDeserialize_RepresentativeToolRoots` | AOT fixture `Program.cs` scenario invoked by `PublishAndRun_ControlPlaneJsonContracts` | H09 | Task 13 |

Name-ledger rules:

- The first 24 entries are discoverable xUnit tests in the two Tooling test
  projects.
- The next six entries are discoverable xUnit tests in the existing Control
  Plane test project.
- `PublishAndRun_ControlPlaneJsonContracts` is the discoverable AOT xUnit gate.
- `ReflectionFallback_IsDisabled` and
  `SerializeDeserialize_RepresentativeToolRoots` are named executable
  scenarios. The fixture prints a per-scenario PASS marker before the final
  process sentinel, so the publish-and-run test proves both scenarios actually
  executed.
- A focused test may call shared fixture helpers, but no ledger entry may be a
  no-op alias whose only assertion is that another test exists.

---

## 4. Planned file structure

### 4.1 New production files

```text
src/Core/CrestCreates.Core.Abstractions/
└── Serialization/
    └── JsonContractSurfaceAttribute.cs

src/Tooling/CrestCreates.JsonContracts.BuildTasks/
├── CrestCreates.JsonContracts.BuildTasks.csproj
├── JsonContractBuildConstants.cs
├── Diagnostics/
│   ├── JsonContractDiagnostic.cs
│   ├── JsonContractDiagnosticIds.cs
│   └── JsonContractDiagnosticReporter.cs
├── Model/
│   ├── JsonContractGenerationModel.cs
│   ├── JsonContractContextModel.cs
│   ├── JsonContractRootModel.cs
│   ├── JsonContractRootProvenance.cs
│   └── JsonContractManifestAccessibility.cs
├── Semantic/
│   ├── JsonContractCompilationFactory.cs
│   ├── JsonContractSurfaceModelBuilder.cs
│   ├── JsonContractSurfaceWalker.cs
│   ├── JsonContractRootNormalizer.cs
│   └── JsonContractSymbolNames.cs
├── Generation/
│   ├── JsonContractSourceWriter.cs
│   ├── JsonContractTypeNameWriter.cs
│   └── WriteIfChangedFile.cs
├── Incremental/
│   ├── JsonContractInputManifest.cs
│   └── JsonContractInputManifestWriter.cs
├── Tasks/
│   ├── GenerateJsonContracts.cs
│   └── WriteJsonContractInputManifest.cs
└── build/
    ├── CrestCreates.JsonContracts.Build.props
    ├── CrestCreates.JsonContracts.Build.targets
    ├── CrestCreates.JsonContracts.Build.Common.props
    ├── CrestCreates.JsonContracts.Build.Common.targets
    ├── CrestCreates.JsonContracts.Build.Repository.props
    └── CrestCreates.JsonContracts.Build.Repository.targets
```

### 4.2 New test/fixture projects

```text
tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests/
tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests/
tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/
tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests/
```

### 4.3 Modified files

```text
Directory.Packages.props
CrestCreates.slnx
solutions/CrestCreates.All.slnx
solutions/CrestCreates.Runtime.slnx
solutions/CrestCreates.Metadata.slnx
solutions/CrestCreates.Platform.slnx

src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/
├── CrestCreates.Agent.ControlPlane.Abstractions.csproj
└── Json/AgentControlPlaneToolJsonSerializerContext.cs

tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/
├── CrestCreates.Agent.ControlPlane.Tests.csproj
└── ToolContractCoverageTests.cs
```

No existing source file is deleted.

---

## 5. Task 0 — Baseline evidence and direct-root audit

**Purpose:** Freeze current behavior before creating scaffolding.

**Files:**

- Read: current Control Plane context, parser, tests, project files.
- Create later in Task 1: baseline behavior tests; no production edit here.

- [ ] **Step 0.1: Verify branch and working tree**

```bash
rtk git branch --show-current
rtk git status --short
```

Expected branch:

```text
feature/issue-58-json-contract-root-generation
```

Expected pre-existing change:

```text
docs/superpowers/specs/2026-07-24-control-plane-json-contract-root-generation-design.md
```

- [ ] **Step 0.2: Build current Control Plane abstractions**

```bash
rtk dotnet build \
  src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/CrestCreates.Agent.ControlPlane.Abstractions.csproj \
  --disable-build-servers
```

Expected: PASS before migration.

- [ ] **Step 0.3: Run current contract and boundary tests**

```bash
rtk dotnet test \
  tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/CrestCreates.Agent.ControlPlane.Tests.csproj \
  --filter "FullyQualifiedName~ToolContractCoverageTests|FullyQualifiedName~ToolDtoSemanticPreservationTests|FullyQualifiedName~ToolDtoBoundaryConstraintTests" \
  --disable-build-servers
```

Expected: PASS. Record the test count in the implementation notes/commit
message; do not hardcode it into new tests.

- [ ] **Step 0.4: Audit direct context use**

```bash
rtk rg -n \
  "AgentControlPlaneToolJsonSerializerContext|AgentControlPlaneToolJsonSerializerOptions|JsonSerializer\\.(Serialize|Deserialize)|\\.Default\\.[A-Z]" \
  src tests samples \
  -g '!**/bin/**' \
  -g '!**/obj/**'
```

Expected direct Extras:

```text
DescriptorActivationReviewDecision
CanonicalHash
```

If another production direct root is found, stop and amend the Spec/Plan before
implementation. Do not silently add it during migration.

- [ ] **Step 0.5: Produce the direct context property usage ledger**

From Step 0.4 results and, only where source cannot disambiguate a generated
property, temporary compiled-assembly inspection, record one row per direct
usage:

```text
source path
line
requested JsonTypeInfo property
classification: Surface / Explicit Extra / incidental test
disposition: generated / retained handwritten / test migrated
```

Put the ledger in the implementation notes or Task 0 commit message. It is
review evidence, not a permanent runtime-reflection test. Any production usage
outside the two reviewed Explicit Extras blocks migration until its ownership
is classified.

- [ ] **Step 0.6: Verify no baseline changes**

```bash
rtk git status --short
```

No new production/test changes are expected from Task 0.

---

## 6. Task 1 — Project scaffolds, fixtures, and named acceptance skeleton

**Purpose:** Establish the full test topology before production behavior.

### 6.1 Create task project scaffold

- [ ] Create
  `src/Tooling/CrestCreates.JsonContracts.BuildTasks/CrestCreates.JsonContracts.BuildTasks.csproj`.

Initial properties:

```xml
<TargetFramework>net10.0</TargetFramework>
<AssemblyName>CrestCreates.JsonContracts.BuildTasks</AssemblyName>
<RootNamespace>CrestCreates.JsonContracts.BuildTasks</RootNamespace>
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
<IsPackable>true</IsPackable>
<PackageId>CrestCreates.JsonContracts.Build</PackageId>
<IncludeBuildOutput>false</IncludeBuildOutput>
<DevelopmentDependency>true</DevelopmentDependency>
<GenerateDependencyFile>true</GenerateDependencyFile>
<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
```

Initial package references:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.Common" PrivateAssets="all" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
<PackageReference Include="Microsoft.Build.Framework"
                  PrivateAssets="all"
                  ExcludeAssets="runtime" />
<PackageReference Include="Microsoft.Build.Utilities.Core"
                  PrivateAssets="all"
                  ExcludeAssets="runtime" />
```

Create only compile-safe API skeletons for types referenced by pure tests.
Behavior methods throw `NotImplementedException`.

### 6.2 Create test projects

- [ ] Create
  `tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests/CrestCreates.JsonContracts.BuildTasks.Tests.csproj`.
- [ ] Add ProjectReferences to:
  - task project;
  - `CrestCreates.Core.Abstractions`.
- [ ] Add xUnit, runner, test SDK, coverlet, FluentAssertions, and Roslyn
  package references without versions.
- [ ] Create
  `tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests/CrestCreates.JsonContracts.Build.PackageTests.csproj`.
- [ ] Add test packages only; do not add a task ProjectReference.
- [ ] Disable parallelization for subprocess/package contract collection.

### 6.3 Create acceptance fixtures and names

- [ ] Create every infrastructure file listed in Section 3.
- [ ] Create all Happy, Boundary, Failure, and Composition test methods from
  Section 2.
- [ ] Create every exact Issue test/scenario name from Section 3.6 in its
  assigned file.
- [ ] Mark placeholder tests with `AcceptanceSkeleton.Pending`.
- [ ] Add XML comments to each test class with its Case Matrix IDs.
- [ ] Ensure test method names contain behavior, not task numbers.

### 6.4 Verify skeleton discovery

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests/CrestCreates.JsonContracts.BuildTasks.Tests.csproj \
  --list-tests \
  --disable-build-servers

rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests/CrestCreates.JsonContracts.Build.PackageTests.csproj \
  --list-tests \
  --disable-build-servers
```

Expected:

- both projects compile;
- all named skeleton tests appear;
- no production behavior is claimed;
- skipped tests explicitly report the acceptance skeleton reason.

### 6.5 Wire solutions

Use `dotnet sln` for XML `.slnx` files:

```bash
rtk dotnet sln CrestCreates.slnx add <new-project>
rtk dotnet sln solutions/CrestCreates.All.slnx add <new-project>
```

Add:

- task project and both tooling tests to root and All;
- task project to every layered solution containing
  `ControlPlane.Abstractions`: Runtime, Metadata, Platform;
- tooling tests to Runtime/Metadata only where existing tooling-test convention
  warrants it;
- AOT projects later in Task 13.

### 6.6 Task 1 verification

```bash
rtk dotnet build \
  src/Tooling/CrestCreates.JsonContracts.BuildTasks/CrestCreates.JsonContracts.BuildTasks.csproj \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `test(json-contracts): establish acceptance fixtures and case matrix skeleton`

---

## 7. Task 2 — Public marker contract

**Requirement cases:** foundation for H01-H05, B02-B04.

**Files:**

- Create:
  `src/Core/CrestCreates.Core.Abstractions/Serialization/JsonContractSurfaceAttribute.cs`
- Modify pure test sources and marker tests.

### 7.1 Activate marker tests

Add/activate:

```text
JsonContractSurfaceAttribute_TargetsClassOnly
JsonContractSurfaceAttribute_AllowsMultipleAndIsNotInherited
JsonContractSurfaceAttribute_PreservesSurfaceType
JsonContractSurfaceAttribute_DefaultExcludedParameterTypesIsEmpty
JsonContractSurfaceAttribute_PreservesConfiguredExcludedParameterTypes
```

- [ ] Remove skeleton skips.
- [ ] Run and confirm RED because the marker does not exist.

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests \
  --filter "FullyQualifiedName~JsonContractSurfaceAttributeTests" \
  --disable-build-servers
```

### 7.2 Implement marker

Implement exactly:

```csharp
namespace CrestCreates.Core.Abstractions.Serialization;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class JsonContractSurfaceAttribute : Attribute
{
    public JsonContractSurfaceAttribute(Type surfaceType)
        => SurfaceType = surfaceType;

    public Type SurfaceType { get; }

    public Type[] ExcludedParameterTypes { get; set; } = [];
}
```

Do not add runtime registry behavior or helper APIs to Core.

### 7.3 Verify

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests \
  --filter "FullyQualifiedName~JsonContractSurfaceAttributeTests" \
  --disable-build-servers
rtk dotnet test \
  tests/Core/CrestCreates.Core.Abstractions.Tests/CrestCreates.Core.Abstractions.Tests.csproj \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `feat(core): add declarative JSON contract surface marker`

---

## 8. Task 3 — Provisional compilation and context discovery

**Requirement cases:** B11, C02, F01, F02, F11.

**Production files:**

```text
Diagnostics/JsonContractDiagnostic*.cs
Model/JsonContractGenerationModel.cs
Model/JsonContractContextModel.cs
Semantic/JsonContractCompilationFactory.cs
Semantic/JsonContractSurfaceModelBuilder.cs
Semantic/JsonContractSymbolNames.cs
```

### 8.1 Activate context discovery tests

```text
Discover_NoMarkedContextProducesEmptyGenerationModel
Discover_MultipleContextsAreIsolated
Discover_ContextsAreOrdinalSorted
Fail_NonPartialContext
Fail_NestedOrGenericContext
Fail_NonPartialOrNestedContext
Fail_NonInterfaceOrUnboundSurface
Fail_UnresolvedMarkerOrStjIdentity
```

For each failure assertion verify:

- diagnostic ID;
- severity Error;
- context/surface metadata name;
- source location;
- no partial generation model returned.

The three `Discover_*` tests assert only the semantic model. They do not write
source bytes. The Issue-compatible writer-level names
`Build_NoMarkedContextWritesDeterministicEmptyOutput` and
`Build_MultipleContextsAreIsolatedAndSorted` remain skipped until Task 6.

### 8.2 Implement compilation factory

`JsonContractCompilationFactory` accepts:

```text
AssemblyName
SourceFiles (path + text)
ReferencePaths
LangVersion
DefineConstants
Nullable
AllowUnsafeBlocks
```

Rules:

- sort source paths Ordinal before parsing;
- parse every file with the same `CSharpParseOptions`;
- create `MetadataReference` from explicit paths only;
- do not run analyzers/generators;
- do not emit;
- report read/reference exceptions through typed task diagnostics;
- retain source trees even when unrelated compiler diagnostics exist.

### 8.3 Implement context discovery

Resolve exact symbols:

```text
CrestCreates.Core.Abstractions.Serialization.JsonContractSurfaceAttribute
System.Text.Json.Serialization.JsonSerializerContext
System.Text.Json.Serialization.JsonSerializableAttribute
System.Threading.CancellationToken
System.Threading.Tasks.Task
System.Threading.Tasks.Task<T>
System.Threading.Tasks.ValueTask
System.Threading.Tasks.ValueTask<T>
```

Context validation:

- class;
- current source assembly;
- top-level;
- non-generic;
- partial;
- derives from `JsonSerializerContext`;
- unique full metadata identity.

Do not fail on the provisional context's missing STJ-generated abstract
implementation members.

### 8.4 Implement initial diagnostics

Implement:

```text
CJC001 InvalidContext
CJC002 InvalidSurface
CJC011 RequiredSymbolUnresolved
```

Use one diagnostic factory. MSBuild task logging is added later; pure model
returns typed diagnostics.

### 8.5 Verify

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests \
  --filter "FullyQualifiedName~ContextDiscoveryTests" \
  --disable-build-servers
```

Then:

```bash
rtk rg -n "AcceptanceSkeleton.Pending" \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests/Semantic
rtk git diff --check
```

Only surface/root/writer tests assigned to later tasks may remain.

**Commit:** `feat(json-contracts): add semantic compilation and context discovery`

---

## 9. Task 4 — Interface traversal and direct-root inference

**Requirement cases:** H01-H05, B01-B09, F03-F07, C01.

**Production files:**

```text
Model/JsonContractRootModel.cs
Model/JsonContractRootProvenance.cs
Semantic/JsonContractSurfaceWalker.cs
Semantic/JsonContractRootNormalizer.cs
```

### 9.1 Activate Happy tests

```text
Build_InheritedInterfaceMethods
Build_UnwrapsTaskAndValueTask
Build_TaskValueTaskAndVoidProduceNoReturnRoot
Build_MultipleSurfacesMergeDeterministically
```

Expected root assertions must compare canonical fully qualified type identities,
not simple names.

### 9.2 Implement interface traversal

For every declared surface:

1. require a closed interface;
2. visit itself and inherited interfaces;
3. use symbol identity to stop diamond repetition;
4. collect `MethodKind.Ordinary`;
5. require public instance method;
6. ignore static members and accessors;
7. retain the declaring surface and display signature as provenance;
8. sort after collection; never depend on `GetMembers()` order.

Method identity must distinguish valid overloads but merge the same inherited
original definition reached through a diamond.

### 9.3 Activate and implement parameter boundary tests

```text
Build_AllBusinessParameters
Build_ExcludesCancellationToken
Build_ExcludesConfiguredInfrastructureTypesByExactIdentity
Build_ExcludedTypesDoNotSuppressReturnRoots
```

Rules:

- visit every parameter;
- reject non-`None` `RefKind`, including `in`;
- exclude exact CancellationToken symbol;
- exclude exact configured type symbol;
- do not use base-type or interface assignability;
- do not apply parameter exclusions to return roots;
- arrays from `params` remain roots.

### 9.4 Activate and implement root normalization tests

```text
Build_IncludesClosedGenericCollectionScalarEnumAndNullableRoots
Build_IncludesClosedGenericAndScalarRoots
Build_DoesNotExpandNestedPropertyGraph
Build_DoesNotExpandTransitiveDtoMembers
Build_DiamondInheritance_DeduplicatesMethods
Build_TracksAllMethodProvenanceForSharedRoot
Build_DeduplicatesOverloadsAndRepeatedTypes
Build_ExcludesInfrastructureParameters
```

Normalization:

- nullable reference annotation erased from CLR root identity;
- `Nullable<T>` retained;
- closed generic retained as one direct root;
- arrays retained;
- scalar and enum retained;
- no property/member traversal;
- roots deduplicated by `SymbolEqualityComparer.Default`;
- provenance is a sorted set, not last-writer-wins.

### 9.5 Activate and implement failure tests

```text
Fail_GenericMethod                         → CJC003
Fail_ByRefPointerOrRefLikeRoot             → CJC004
Fail_OpenGenericRoot                       → CJC005
Fail_OpenGenericOrRefLikeRoot              → CJC004/CJC005 parameterized cases
Fail_ByRefReturn                           → CJC005
Fail_ByRefReadonlyReturn                   → CJC005
Fail_InaccessibleRoot                      → CJC006
Fail_UnresolvedPreCoreCompileRoot          → CJC007
```

`CJC007` message must say:

- the type was unresolved before `CoreCompile`;
- ordinary same-compilation Source Generator output is unavailable;
- move the contract to a referenced assembly, add an earlier MSBuild compile
  source, or retain an explicit visible root.

No invalid method is silently skipped.

Before reading `ReturnType`, inspect:

```csharp
method.ReturnsByRef
method.ReturnsByRefReadonly
```

Either flag fails the whole method with `CJC005`. A Roslyn `ReturnType` of
`SomeDto` must not cause `ref SomeDto` or `ref readonly SomeDto` to be accepted
as an ordinary root.

### 9.6 Verify full semantic inference

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests \
  --filter "FullyQualifiedName~SurfaceInference|FullyQualifiedName~SurfaceComposition" \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `feat(json-contracts): infer deterministic direct roots from interface surfaces`

---

## 10. Task 5 — Explicit roots and manifest accessibility model

**Requirement cases:** B10, B17, B18, C03, F13.

**Production files:**

```text
Model/JsonContractManifestAccessibility.cs
Semantic/JsonContractSurfaceModelBuilder.cs
Diagnostics/...
```

### 10.1 Activate explicit-root tests

```text
Build_DoesNotDuplicateExplicitJsonSerializableRoots
Build_DoesNotDuplicateExplicitExtras
Build_ExplicitRootStillAppearsInManifestUnion
```

Test handwritten attributes with:

- normal explicit root;
- explicit root also inferred from a surface;
- explicit `TypeInfoPropertyName`;
- explicit per-type generation mode.

Expected:

- explicit declaration is never rewritten;
- generated attribute is suppressed only for the exact duplicate root;
- Surface set still includes the inferred root;
- Explicit set includes the handwritten root;
- All set is their union.

### 10.2 Implement explicit attribute reading

- Resolve semantic `JsonSerializableAttribute`.
- Read the constructor `Type`.
- Treat only attributes on the marked context declarations as Explicit roots.
- Do not scan assembly DTOs.
- Preserve configuration ownership in handwritten source.
- Return `CJC007` if the explicit Type itself is an unresolved ErrorType.

### 10.3 Activate manifest accessibility tests

```text
Parse_InternalManifestAccessibility
Parse_PublicManifestAccessibility
Fail_InvalidManifestAccessibility
```

Parse only:

```text
Internal
Public
```

Use Ordinal comparison after trimming MSBuild whitespace. Do not accept aliases,
lowercase variants, empty non-default override, or arbitrary C# modifiers.

Default is Internal.

Implement `CJC013` for invalid values.

These tests stop at the parsed generation model. Writer-level
`Build_InternalManifestAccessibility` and
`Build_PublicManifestAccessibility` remain skipped until Task 6; the
cross-assembly consumption and immutability contract remains skipped until
Task 8.

### 10.4 Verify

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests \
  --filter "FullyQualifiedName~Explicit|FullyQualifiedName~ManifestAccessibility" \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `feat(json-contracts): model explicit roots and manifest accessibility`

---

## 11. Task 6 — Deterministic source and root-manifest writer

**Requirement cases:** B09-B11, B17-B18, C01-C03, F08.

**Production files:**

```text
Generation/JsonContractTypeNameWriter.cs
Generation/JsonContractSourceWriter.cs
```

### 11.1 Activate source writer tests

```text
Write_IsByteStable
Write_DoesNotContainPathOrTimestamp
Build_DeduplicatesAndOrdinalSortsRoots
Build_MultipleContextsAreIsolatedAndSorted
Build_NoMarkedContextWritesDeterministicEmptyOutput
Build_InternalManifestAccessibility
Build_PublicManifestAccessibility
Fail_ManifestNameCollision
```

### 11.2 Implement canonical type writer

Cover:

- namespace-qualified named types;
- nested named types;
- closed generics;
- arrays and ranks;
- nullable value types;
- tuples by underlying CLR type;
- `global::System.String`, not C# keyword aliases;
- nullable reference annotation removal;
- no open type parameters.

Use a dedicated `SymbolDisplayFormat`; do not concatenate simple names.

### 11.3 Implement generated partial context output

Output contract:

```text
// <auto-generated />
#nullable enable
blank line
namespace block
sorted provenance comments
sorted JsonSerializable attributes
partial context declaration
manifest declaration
```

Rules:

- UTF-8 without BOM;
- `\n` line endings;
- no timestamp/path/version banner;
- contexts sorted by full metadata name;
- roots sorted by canonical metadata name using Ordinal;
- one attribute per non-explicit Surface root;
- accessibility/modifiers compatible with handwritten context;
- no base class repetition;
- no duplicate `JsonSourceGenerationOptions`.

### 11.4 Implement manifest writer

Generate:

```text
SurfaceRootTypes
ExplicitRootTypes
AllDirectRootTypes
```

Each set uses a private
`global::System.Collections.Frozen.FrozenSet<global::System.Type>` backing
field built from generated `typeof(...)` expressions with `ToFrozenSet()`.
The accessor may remain `IReadOnlySet<Type>`, but the returned object must not
implement a mutable collection interface and must not be downcastable to
`HashSet<Type>`.

Internal mode:

```csharp
internal static class ...
internal static IReadOnlySet<Type> ...
```

Public mode:

```csharp
public static class ...
public static IReadOnlySet<Type> ...
```

Use fully qualified BCL types in generated source. The manifest may construct a
frozen set with generated `typeof(...)`; it must not inspect assemblies.

The All set must be a deterministic generated union, not a runtime scan.

### 11.5 Empty and collision behavior

- No marked context: write valid header-only source.
- Multiple contexts with distinct namespaces: emit both.
- Manifest class name collision in one namespace: return `CJC008`.
- Existing user type with generated manifest name: return `CJC008`.

### 11.6 Verify exact snapshots

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests \
  --filter "FullyQualifiedName~JsonContractSourceWriterTests|FullyQualifiedName~JsonContractManifestWriterTests" \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `feat(json-contracts): emit byte-stable STJ roots and root manifests`

---

## 12. Task 7 — Input manifest, safe writes, and MSBuild task adapters

**Requirement cases:** B13-B14, F09-F12.

**Production files:**

```text
Incremental/JsonContractInputManifest.cs
Incremental/JsonContractInputManifestWriter.cs
Generation/WriteIfChangedFile.cs
Tasks/GenerateJsonContracts.cs
Tasks/WriteJsonContractInputManifest.cs
Diagnostics/JsonContractDiagnosticReporter.cs
```

### 12.1 Activate input-manifest tests

```text
Manifest_SortsSourceAndReferencePathsOrdinal
Manifest_NormalizesPathSeparators
Manifest_IncludesCompilationProperties
Manifest_IncludesManifestAccessibility
Manifest_IncludesAllowedOutputRootAndTemporaryDirectory
Manifest_IncludesTaskSemanticVersionAndAssemblyIdentity
Manifest_SourceAdditionChangesBytes
Manifest_SourceDeletionChangesBytes
Manifest_UnchangedInputIsByteStable
```

Use JSON with fixed property order. Do not serialize with reflection-based
options inside the task; either write explicitly or use a task-owned
source-generated context if justified and packaged.

### 12.2 Activate write safety tests

```text
Write_UnchangedBytesPreserveTimestamp
Write_ChangedBytesReplaceAtomically
Task_ManifestWriterRejectsOutputPathOutsideAllowedRoot
Task_GeneratorRejectsOutputPathOutsideAllowedRoot
Task_GeneratorRejectsTemporaryDirectoryOutsideAllowedRoot
Fail_OutputWritePreservesPreviousFile
Fail_UnreadableSource
Fail_UnreadableMetadataReference
```

Atomic write algorithm:

1. create task-owned temp file inside the validated
   `CrestCreatesJsonContractTemporaryDirectory`;
2. write complete bytes;
3. flush/close;
4. compare existing bytes;
5. if equal, remove only temp and keep destination timestamp;
6. if different, atomically replace/move; if the platform cannot atomically
   move from the configured temp location, fail without altering the previous
   destination;
7. on failure, preserve previous destination;
8. report `CJC012`.

Containment validation:

1. normalize `AllowedOutputRoot`, output, and temporary paths with
   `Path.GetFullPath`;
2. compute `Path.GetRelativePath(allowedRoot, candidate)`;
3. reject a rooted relative result;
4. reject `..` or a relative path beginning with a complete `..` directory
   segment;
5. accept sibling names only when they are genuinely beneath the root;
6. perform validation before creating a directory or temporary file.

The Build-level
`Fail_GeneratedPathOutsideIntermediateOutputPath` remains skipped until Task 8,
where all four target-owned paths can be validated together.

### 12.3 Implement MSBuild task inputs

`WriteJsonContractInputManifest` inputs:

```text
SourceFiles
ReferencePaths
OutputPath
AllowedOutputRoot [Required]
TemporaryDirectory
LangVersion
DefineConstants
Nullable
AllowUnsafeBlocks
ImplicitUsings
TargetFramework
ManifestAccessibility
TaskSemanticVersion
TaskAssemblyIdentity
```

`GenerateJsonContracts` inputs:

```text
SourceFiles
ReferencePaths
OutputPath
AllowedOutputRoot [Required]
TemporaryDirectory [Required]
AssemblyName
LangVersion
DefineConstants
Nullable
AllowUnsafeBlocks
TargetFramework
ManifestAccessibility
```

Both task classes declare:

```csharp
[Required]
public string AllowedOutputRoot { get; set; } = string.Empty;
```

`WriteJsonContractInputManifest` records the normalized allowed root and
temporary directory in its fixed-order JSON. `GenerateJsonContracts` uses only
the validated temporary directory. Neither task derives authority from
`OutputPath` itself.

Task output:

```text
GeneratedContextCount
GeneratedSurfaceRootCount
GeneratedExplicitRootCount
OutputChanged
```

Tasks return `false` on any correctness diagnostic.

### 12.4 Implement diagnostic logging

Map typed diagnostic to `TaskLoggingHelper.LogError` with:

- code;
- file;
- line/column;
- context;
- surface;
- method/parameter;
- message.

Do not log an error and continue writing partial output.

### 12.5 Verify

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests \
  --filter "FullyQualifiedName~InputManifest|FullyQualifiedName~WriteIfChanged|FullyQualifiedName~TaskAdapter" \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `feat(json-contracts): add deterministic input manifest and safe task adapters`

---

## 13. Task 8 — Formal MSBuild mainline and Global Usings ordering

**Requirement cases:** H06, B12, B18, F07, F12, F16.

**Production files:**

```text
build/CrestCreates.JsonContracts.Build.props
build/CrestCreates.JsonContracts.Build.targets
build/CrestCreates.JsonContracts.Build.Common.props
build/CrestCreates.JsonContracts.Build.Common.targets
build/CrestCreates.JsonContracts.Build.Repository.props
build/CrestCreates.JsonContracts.Build.Repository.targets
```

The package and repository files are thin transport wrappers. All generation
properties and targets live in the two Common files so there is one generation
implementation.

### 13.1 Define common properties

Defaults:

```text
CrestCreatesJsonContractGenerationEnabled=true
CrestCreatesJsonContractGeneratedFile=$(IntermediateOutputPath)CrestCreates.JsonContracts.g.cs
CrestCreatesJsonContractInputManifest=$(IntermediateOutputPath)CrestCreates.JsonContracts.inputs.json
CrestCreatesJsonContractGenerationStamp=$(IntermediateOutputPath)CrestCreates.JsonContracts.stamp
CrestCreatesJsonContractTemporaryDirectory=$(IntermediateOutputPath)CrestCreates.JsonContracts.tmp
CrestCreatesJsonContractManifestAccessibility=Internal
CrestCreatesJsonContractGenerationDependsOn=
```

The normalized full `$(IntermediateOutputPath)` is passed to both custom tasks
as `AllowedOutputRoot`. It and the normalized temporary directory participate
in the input manifest.

Generated source, input manifest, generation stamp, and temporary directory
must resolve beneath `$(IntermediateOutputPath)`. The implementation must not
silently rewrite an explicitly supplied outside path to a different location.
Reject an outside path with `CJC012` before opening a file. Do not allow
source-tree output.

### 13.2 Implement target ordering

Create `ValidateCrestCreatesJsonContractPaths` using only MSBuild property
functions and the built-in `Error` task. For each task-owned candidate:

1. compute normalized root and candidate full paths;
2. compute `Path.GetRelativePath(root, candidate)`;
3. reject a rooted relative result;
4. reject `..`;
5. reject a result beginning with `..` followed by either directory separator;
6. do not use a raw `StartsWith(root)` test.

This specifically rejects:

```text
allowed: /obj/net10.0
candidate: /obj/net10.0-evil/file
```

Activate
`Fail_GeneratedPathOutsideIntermediateOutputPath` as a parameterized real-build
test covering generated file, input manifest, stamp, and temporary directory.
For each case assert:

- `CJC012`;
- no custom task invocation marker;
- no candidate file/directory;
- no stamp update;
- no generated Compile item;
- an existing valid output remains byte-identical.

Create the initial
`ValidateCrestCreatesJsonContractTransport` target in this task. At this slice
it validates the one configured repository transport and selected task path;
Task 10 extends it with package/repository conflict sentinels without changing
generation target names.

Formal dependency chain:

```text
PrepareForBuild
  → GenerateGlobalUsings
  → ResolveReferences
  → $(CrestCreatesJsonContractGenerationDependsOn)
  → ValidateCrestCreatesJsonContractPaths
  → ValidateCrestCreatesJsonContractTransport
  → PrepareCrestCreatesJsonContractInputs
  → GenerateCrestCreatesJsonContracts
  → update stamp
  → IncludeCrestCreatesJsonContractGeneratedSource
  → CoreCompile
```

`GenerateCrestCreatesJsonContracts` uses `BeforeTargets="CoreCompile"` but must
also express the explicit dependencies above. Do not rely on sibling
`BeforeTargets` import order.

`PrepareCrestCreatesJsonContractInputs`,
`GenerateCrestCreatesJsonContracts`, the success-stamp update, and
`IncludeCrestCreatesJsonContractGeneratedSource` each explicitly depend on both
validation targets. A future caller cannot invoke one of those targets directly
to bypass validation.

Declare both custom task mappings with fully qualified names:

```xml
<UsingTask
    TaskName="CrestCreates.JsonContracts.BuildTasks.WriteJsonContractInputManifest"
    AssemblyFile="$(CrestCreatesJsonContractsSelectedTaskAssembly)"
    Runtime="NET"
    Architecture="*" />
<UsingTask
    TaskName="CrestCreates.JsonContracts.BuildTasks.GenerateJsonContracts"
    AssemblyFile="$(CrestCreatesJsonContractsSelectedTaskAssembly)"
    Runtime="NET"
    Architecture="*" />
```

The `UsingTask` declarations are evaluated with the project. Correctness
requires conflict/path failure before the first custom task invocation, not
before declaration. `Runtime="NET"` intentionally binds this .NET 10/MSBuild 18
package to the .NET TaskHost and isolates its private Roslyn dependencies.

### 13.3 Capture formal source items after Global Usings

After `GenerateGlobalUsings`:

- snapshot current `@(Compile)`;
- normalize full paths;
- exclude only this task's prior generated source;
- keep `$(GeneratedGlobalUsingsFile)` when it exists;
- keep earlier MSBuild-generated sources;
- use the same filtered items for:
  - input manifest;
  - generation target direct Inputs;
  - task semantic source files.

Add an assertion/message in the test target fixture so
`Build_GlobalUsingsFileParticipatesInInputManifestAndTargetInputs` can prove the
actual file path was included.

### 13.4 Activate same-compilation test

Activate both Issue-level assertions:

```text
Build_GeneratedAttributesParticipateInSameCompilation
Build_StjGeneratorProducesJsonTypeInfoForGeneratedRoots
```

Their shared fixture contains:

- handwritten context with surface marker;
- request and result types;
- a compile-time code reference to
  `Context.Default.<GeneratedTypeInfoProperty>`;
- no handwritten `JsonSerializable` for the surface roots.

Expected RED before targets: formal compilation fails.

Expected GREEN after targets:

- task `.g.cs` exists;
- build succeeds in one invocation;
- generated context property compiles;
- no second build required.

### 13.5 Activate Global Usings test

Fixture source intentionally has no using directives and uses:

```csharp
Task<AgentToolResult<IReadOnlyList<ResultDto>>> MethodAsync(
    InvocationContext context,
    RequestDto request,
    CancellationToken cancellationToken = default);
```

Project:

```xml
<ImplicitUsings>enable</ImplicitUsings>
```

Assertions:

- build succeeds;
- no `CJC007`;
- Global Usings file path appears in input manifest;
- CancellationToken is excluded;
- request and closed result envelope are present.

### 13.6 Activate same-project SG-only failure

Create a temporary test generator project using SDK Roslyn assemblies or
fixture-local analyzer references. It emits `GeneratedRequestDto`.

Consumer handwritten surface refers to `GeneratedRequestDto`.

Expected:

- ordinary compiler would see generator output;
- JSON task fails first with `CJC007`;
- output explains pre-CoreCompile visibility;
- no incomplete root file/stamp is committed.

### 13.7 Activate STJ formal diagnostic test

Use a direct root shape rejected by STJ. Expected:

- JSON task succeeds in emitting the attribute;
- official compiler/STJ fails;
- task does not catch, downgrade, or replace STJ diagnostics.

### 13.8 Activate Public manifest cross-assembly test

Build context project with:

```xml
<CrestCreatesJsonContractManifestAccessibility>Public</...>
```

Build a second project referencing it and accessing
`SurfaceRootTypes`.

Expected: one clean build succeeds.

Also build the default Internal variant and activate
`Build_InternalManifestRemainsAssemblyScoped`. A separate consumer compilation
must fail to access the generated manifest, while the context assembly and its
declared friend test assembly remain able to consume it.

Also activate `Build_PublicManifestSetsAreImmutable`. From the separate
consumer assembly:

- retrieve all three public set instances;
- assert they do not implement `ISet<Type>` or another mutable collection
  contract;
- assert they cannot be downcast to `HashSet<Type>`;
- verify attempted mutation is unavailable without reflecting into private
  implementation state;
- verify repeated access returns the same immutable instance and set contents
  remain unchanged.

The test does not require the public property type itself to be
`FrozenSet<Type>`; it verifies the public instance cannot be mutated.

### 13.9 Verify Task 8

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests \
  --filter "FullyQualifiedName~SameCompilationContractTests|FullyQualifiedName~GlobalUsingsContractTests|FullyQualifiedName~SameProjectGeneratorBoundaryTests|FullyQualifiedName~ManifestAccessibilityContractTests" \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `feat(json-contracts): run semantic generation after SDK compile inputs`

---

## 14. Task 9 — Incremental, multi-TFM, design-time, and clean contracts

**Requirement cases:** B13-B16, C08, F15.

### 14.1 Implement success stamp

The declared incremental Output is the success stamp, not `.g.cs`.

Rules:

- generation target Inputs include input manifest, filtered sources, references,
  and task assembly;
- update stamp only after successful generation/verification;
- `.g.cs` remains write-if-changed;
- if `.g.cs` is missing, prepare target invalidates task-owned stamp;
- failed generation does not update stamp.

### 14.2 Activate incremental tests

```text
Build_AddMethodAddsRootWithoutEditingContext
Build_AddMethodThenRebuildUpdatesManifest
Build_RemoveMethodRemovesStaleRoot
Build_RemoveMethodThenRebuildRemovesRoot
Build_SourceDeletionInvalidatesInputManifest
Build_SourceDeletionInvalidatesGeneration
Build_CompilationPropertyChangeInvalidatesInputManifest
Build_AllowedOutputRootChangeInvalidatesInputManifest
Build_UnchangedInputSkipsOrDoesNotRewriteOutput
Build_UnchangedSemanticOutputDoesNotRewriteTimestamp
Build_MissingGeneratedSourceInvalidatesStamp
```

Test mechanics:

- use separate source files for removable methods;
- compare source SHA-256, not only text fragments;
- snapshot `.g.cs` timestamp and content hash;
- change a source comment to force input processing without semantic output
  change;
- assert `.g.cs` hash/timestamp unchanged;
- assert success stamp is refreshed;
- remove `.g.cs` while preserving stamp and assert regeneration.

No test may depend on a hardcoded sleep alone. If timestamp resolution requires
spacing, combine it with content hashes and target log assertions.

### 14.3 Multi-TFM fixture

Use:

```xml
<TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>
```

The fixture uses no Windows APIs. If the canonical CI SDK rejects that pair
without an additional pack, document and select another SDK-available secondary
TFM in the Plan before implementation; do not silently skip the test.

Assert each TFM has its own:

```text
obj/<Configuration>/<TFM>/CrestCreates.JsonContracts.g.cs
obj/<Configuration>/<TFM>/CrestCreates.JsonContracts.inputs.json
obj/<Configuration>/<TFM>/CrestCreates.JsonContracts.stamp
```

The generated semantic bytes may be equal, but paths, manifests, and stamps
must not cross-contaminate.

Also build Debug then Release and assert output isolation.

The outer test method retaining the Issue vocabulary is:

```text
Build_MultiTargetingProducesIndependentOutputs
```

### 14.4 Design-time fixture

1. Formal build creates output.
2. Invoke design-time build properties:

```text
DesignTimeBuild=true
BuildingInsideVisualStudio=true
SkipCompilerExecution=true
ProvideCommandLineArgs=true
```

3. Assert task semantic generation did not run.
4. Assert existing generated file was included once.
5. Remove formal output and run design-time only.
6. Assert design-time did not claim correctness or create a new source.

### 14.5 Clean fixture

Create unrelated files under:

```text
obj/<config>/<tfm>/unrelated.keep
project root/user-source.keep
```

Run clean. Assert only task-owned:

```text
.g.cs
.inputs.json
.stamp
task temp files
```

are removed. Unrelated files remain.

### 14.6 Verify

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests \
  --filter "FullyQualifiedName~IncrementalContractTests|FullyQualifiedName~MultiTargetingContractTests" \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `feat(json-contracts): close incremental multi-targeting and clean behavior`

---

## 15. Task 10 — Transport guards and NuGet package contract

**Requirement cases:** H07, B19, C04-C06, F14.

### 15.1 Implement transport markers

Use these evaluation-time properties:

```text
CrestCreatesJsonContractsSelectedTransport
CrestCreatesJsonContractsSelectedTaskAssembly
_CrestCreatesJsonContractsPackageTransportImported
_CrestCreatesJsonContractsRepositoryTransportImported
_CrestCreatesJsonContractsPropsImported
_CrestCreatesJsonContractsTargetsImported
_CrestCreatesJsonContractsTransportConflict
_CrestCreatesJsonContractsImportConflict
```

Transport values are exactly `Repository` or `Package`.

Use thin transport wrappers so import provenance is explicit:

```text
CrestCreates.JsonContracts.Build.props
    marks Package transport
    proposes package-relative task path
    guarded-imports Common.props

CrestCreates.JsonContracts.Build.targets
    marks Package targets import
    guarded-imports Common.targets

CrestCreates.JsonContracts.Build.Repository.props
    marks Repository transport
    proposes configuration-aware repository task path
    guarded-imports Common.props

CrestCreates.JsonContracts.Build.Repository.targets
    marks Repository targets import
    guarded-imports Common.targets
```

NuGet auto-imports only the package-ID-named `Build.props` and `Build.targets`.
The repository consumer manually imports only the two Repository wrappers.
Both wrappers reach the same Common implementation.

Package task path:

```text
$(MSBuildThisFileDirectory)..\tasks\net10.0\
CrestCreates.JsonContracts.BuildTasks.dll
```

Repository wrapper task path:

```text
$(MSBuildThisFileDirectory)..\bin\$(Configuration)\net10.0\
CrestCreates.JsonContracts.BuildTasks.dll
```

Wrapping above is explanatory only; implementation paths stay on one physical
XML line and are normalized before comparison.

Wrapper/import algorithm:

1. Each props wrapper sets its own provenance flag unconditionally.
2. Each wrapper records its proposed normalized task path.
3. Only the first wrapper imports Common.props; later wrappers still set their
   provenance flag, so mixed transport remains observable.
4. Common.props selects a path only when exactly one transport is active.
5. Each targets wrapper sets its own provenance flag and only the first imports
   Common.targets.
6. Common.targets defines one
   `ValidateCrestCreatesJsonContractTransport` target before generation.
7. `UsingTask` mappings may already have been evaluated, but every target that
   could invoke them or create a side effect depends explicitly on successful
   validation.

The validation target uses only conditions/properties and the built-in MSBuild
`Error` task. It must not call or load either CrestCreates custom task.

Validation requires:

- exactly one transport provenance flag;
- exactly one matching non-empty normalized task assembly path;
- one effective Common.props import;
- one effective Common.targets import;
- the selected task assembly exists before its first task invocation.

Mixed repository + package transport, competing task paths, or a second
non-identical Common definition produces `CJC014`. Exact repeated import of the
same wrapper is idempotent: one `UsingTask`, one generation target, and one
generated Compile item.

`Build_RepositoryAndPackageTransportConflictFailsBeforeGeneration` instruments
the fixture with markers around the first custom task call and every side
effect. It must prove `CJC014` occurs before:

```text
WriteJsonContractInputManifest invocation
GenerateJsonContracts invocation
input-manifest creation/change
generated-source creation/change
success-stamp creation/change
generated Compile-item inclusion
```

The test does not assert that evaluation skipped the `UsingTask` declaration.

### 15.2 Wire repository Control Plane transport only after guards pass

Do not migrate the context yet.

Add to ControlPlane abstractions project:

- non-runtime task ProjectReference:

  ```xml
  <ProjectReference
      Include="../../../Tooling/CrestCreates.JsonContracts.BuildTasks/CrestCreates.JsonContracts.BuildTasks.csproj"
      ReferenceOutputAssembly="false"
      PrivateAssets="all" />
  ```

- Repository props import before consumer properties/items that depend on its
  defaults;
- Repository targets import near the end of the project.

The ProjectReference establishes build order only. It must not become a
compiler reference, runtime dependency, analyzer, or published asset. Do not
copy the existing `CrestCreates.Modules.props` configuration-pinned
`bin/Debug` task path; this transport must honor `$(Configuration)`.

Verify the project still builds before applying surface attributes.

### 15.3 Complete package metadata

Pack layout:

```text
build/CrestCreates.JsonContracts.Build.props
build/CrestCreates.JsonContracts.Build.targets
build/CrestCreates.JsonContracts.Build.Common.props
build/CrestCreates.JsonContracts.Build.Common.targets
build/CrestCreates.JsonContracts.Build.Repository.props
build/CrestCreates.JsonContracts.Build.Repository.targets
tasks/net10.0/CrestCreates.JsonContracts.BuildTasks.dll
tasks/net10.0/CrestCreates.JsonContracts.BuildTasks.deps.json
tasks/net10.0/Microsoft.CodeAnalysis.dll
tasks/net10.0/Microsoft.CodeAnalysis.CSharp.dll
tasks/net10.0/<required private dependencies>
```

Do not pack:

```text
lib/**
Microsoft.Build.Framework.dll
Microsoft.Build.Utilities.Core.dll
```

Use explicit pack items or a verified allowlist. Do not use an unrestricted
`bin/**/*.dll` glob.

### 15.4 Activate package layout tests

```text
Pack_ContainsBuildAssetsTaskAssemblyDepsAndRoslynDependencies
Pack_DoesNotContainLibRuntimeAssembly
```

Open `.nupkg` as zip and assert exact required/forbidden paths.

### 15.5 Activate clean local-feed test

Activate:

```text
Build_CleanCheckoutSucceedsOnFirstInvocation
Pack_LocalFeedConsumerGetsTaskAndTargetsOnly
```

Process:

1. `dotnet pack` task project Release to fixture feed.
2. Create consumer with PackageReference only.
3. Write fixture-local `NuGet.Config` with local feed.
4. Restore into fixture-local package cache.
5. Build once.
6. Assert task output and STJ-generated property exist.

The consumer must not reference the task project or repository targets path.

### 15.6 Activate direct/transitive composition tests

Direct project:

```text
App → PackageReference(Build)
```

Transitive project:

```text
App → ProjectReference(ContractLibrary)
ContractLibrary → PackageReference(Build, PrivateAssets=all)
```

Expected:

- ContractLibrary runs generation;
- App does not import/run build assets transitively;
- App has no task `.g.cs` unless it directly references the package.

### 15.7 Activate mixed and duplicate import tests

```text
Build_RepositoryAndPackageTransportConflictFailsBeforeGeneration
Build_DuplicateImportCannotRunGenerationOrAddCompileTwice
Pack_ExactlyOnePackageTransportAndTargetsSetIsActive
```

Assertions:

- `CJC014` for mixed transport;
- no output/stamp created on conflict;
- exact guarded repeat logs/executes one generation;
- `@(Compile)` contains generated full path once;
- one selected task assembly path.

### 15.8 Activate leakage tests

The Issue-level aggregate is:

```text
Build_TaskDependenciesDoNotLeakToRuntimeOutput
```

Build and publish consumer. Recursively assert no file name contains:

```text
CrestCreates.JsonContracts.BuildTasks
Microsoft.CodeAnalysis
Microsoft.Build.Framework
Microsoft.Build.Utilities.Core
```

under consumer `bin` or publish output.

### 15.9 Verify

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests \
  --filter "FullyQualifiedName~Package|FullyQualifiedName~Transport|FullyQualifiedName~ImportComposition" \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `feat(json-contracts): package direct opt-in build transport`

---

## 16. Task 11 — Control Plane migration to generated direct roots

**Requirement cases:** H01, H05, B01-B06, B10, B17, C03, C07.

**Files:**

- Modify:
  `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Json/AgentControlPlaneToolJsonSerializerContext.cs`
- Confirm repository transport in `.csproj`.

### 16.1 Add context declaration

Add:

```csharp
[JsonContractSurface(
    typeof(IAgentControlPlaneToolService),
    ExcludedParameterTypes = new[] { typeof(AgentToolInvocationContext) })]
[JsonContractSurface(typeof(IAgentToolManifestProvider))]
```

Keep:

```text
JsonSourceGenerationOptions
DescriptorActivationReviewDecision explicit Extra
CanonicalHash explicit Extra
```

Set/inherit:

```xml
<CrestCreatesJsonContractManifestAccessibility>Internal</...>
```

### 16.2 First migration build before removing handwritten roots

Build with generated surface attributes while handwritten entries remain.

Expected:

- task suppresses exact duplicate Surface attrs;
- generated manifest contains Surface and Explicit sets;
- build succeeds;
- no duplicate STJ property error.

```bash
rtk dotnet build \
  src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/CrestCreates.Agent.ControlPlane.Abstractions.csproj \
  --disable-build-servers
```

Inspect:

```text
obj/Debug/net10.0/CrestCreates.JsonContracts.g.cs
obj/Debug/net10.0/CrestCreates.JsonContracts.inputs.json
```

### 16.3 Add real manifest assertions before cleanup

Activate:

```text
GeneratedSurfaceRoots_MatchExpectedToolAndManifestSurfaces
GeneratedSurfaceRoots_AreResolvedByContext
ExplicitExtras_AreResolvedByContext
AllDirectRoots_EqualSurfaceUnionExplicit
CanonicalHashParserRoot_IsExplicit
```

Run RED if generated manifest is missing/wrong.

### 16.4 Remove only transitive handwritten registrations

Classify each current attribute:

```text
Surface Root       → generated; remove handwritten attribute
Explicit Extra     → keep handwritten
Transitive Metadata → remove handwritten attribute
Unknown direct use → stop and audit; do not guess
```

Expected handwritten context after migration:

- `JsonSourceGenerationOptions`;
- two `JsonContractSurface` attributes;
- `JsonSerializable(DescriptorActivationReviewDecision)`;
- `JsonSerializable(CanonicalHash)`;
- partial class body.

Do not remove the file.

### 16.5 Verify generated roots include important boundaries

Assert manifest contains:

```text
DescriptorSearchRequest
DescriptorReviewReportDto
DescriptorReviewReportFormat
string
AgentToolResult<string>
IReadOnlyList<AgentToolDescriptor>
AgentToolDescriptor
all other closed AgentToolResult<TResult> roots
```

Assert manifest excludes:

```text
AgentToolInvocationContext
CancellationToken
member-only DTOs as Surface roots
```

### 16.6 Verify options/runtime composition unchanged

Do not modify:

```text
AgentControlPlaneToolJsonSerializerOptions.CreateDefault()
IAgentToolJsonContextContributor
IMcpToolJsonContextContributor
Agent/MCP DI registrations
```

Add `ControlPlaneMigration_DoesNotAddJsonContributor` using dependency/type
assertions without runtime assembly scan on production path.

### 16.7 Verify

```bash
rtk dotnet build \
  src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/CrestCreates.Agent.ControlPlane.Abstractions.csproj \
  --disable-build-servers

rtk dotnet test \
  tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests \
  --filter "FullyQualifiedName~GeneratedRootManifestTests|FullyQualifiedName~ExplicitExtraJsonContractTests" \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `refactor(agent): generate Control Plane JSON direct roots from surfaces`

---

## 17. Task 12 — Replace legacy coverage authority and prove wire behavior

**Requirement cases:** H08, B06, B17, C03.

**Files:**

```text
Modify: tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/ToolContractCoverageTests.cs
Create: tests/.../JsonContracts/ControlPlaneJsonContractTestBase.cs
Create: tests/.../JsonContracts/GeneratedRootManifestTests.cs
Create: tests/.../JsonContracts/ControlPlaneJsonRoundTripTests.cs
Create: tests/.../JsonContracts/ExplicitExtraJsonContractTests.cs
```

### 17.1 Remove legacy root-definition logic

From `ToolContractCoverageTests`, remove or replace:

- service signature reflection used to define roots;
- public sealed record assembly scan;
- record detection through `<Clone>$`;
- BCL result special list;
- recursive property graph used to define root ownership;
- known supporting/exclusion root lists;
- orphan-root inference based on runtime reflection.

Do not preserve those helpers under a new name.

Keep tests whose concern remains:

- manifest tool-name/service alignment if still independently valuable;
- source-generated type info availability;
- DTO boundary behavior;
- semantic wire preservation.

Root truth now comes from generated manifest plus pure semantic rule tests.

### 17.2 Activate generated manifest tests

```text
EveryGeneratedRoot_HasJsonTypeInfo
GeneratedSurfaceRoots_AreResolvedByContext
ExplicitExtras_HaveJsonTypeInfo
ExplicitExtras_AreResolvedByContext
AllDirectRoots_EqualSurfaceUnionExplicit
ControlPlaneRootManifest_IsInternal
```

For each `AllDirectRootTypes` entry:

```csharp
AgentControlPlaneToolJsonSerializerContext.Default.GetTypeInfo(type)
```

must return non-null. This iterates an explicit generated set; it does not scan
an assembly.

Explicit set must equal:

```text
DescriptorActivationReviewDecision
CanonicalHash
```

### 17.3 Activate representative round-trip tests

```text
RepresentativeToolDtos_RoundTrip
RepresentativeSurfaceRequests_RoundTrip
RepresentativeSurfaceResults_RoundTrip
AgentToolResultOfString_RoundTripsWithoutSpecialCase
ManifestListAndSingleResults_RoundTrip
DescriptorReviewReportFormat_IsDirectInputRoot
DescriptorParser_AcceptsToolOutput
DescriptorActivationReviewDecisionParser_UsesExplicitAotMetadata
CanonicalHashParserRoot_IsExplicit
SerializerOptions_ContainOnlyGeneratedResolverChain
NoAssemblyWideJsonSerializableFallbackRemains
NoAssemblyWidePublicRecordScanOrKnownExclusionList
```

Wire assertions:

- camelCase properties;
- null values omitted where configured;
- enum behavior unchanged from baseline;
- result Status/Value/Diagnostics preserved;
- manifest descriptor contract version preserved;
- Activation decision hashes preserved;
- no `DefaultJsonTypeInfoResolver`.

### 17.4 Source-level anti-regression test

`NoAssemblyWidePublicRecordScanOrKnownExclusionList` may inspect the specific
test source file text from repository root and assert removed helper identifiers
are absent:

```text
AllPublicToolContractDtos_Have_JsonTypeInfo
IsRecordType
knownSupportingTypes
bclResultTools
CollectReferencedTypes
Assembly.GetTypes
```

This is a test of test architecture, not production runtime discovery.

### 17.5 Run Control Plane tests

```bash
rtk dotnet test \
  tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests \
  --disable-build-servers
```

Expected: all pass.

Then run boundary tests:

```bash
rtk dotnet test \
  tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --disable-build-servers
```

**Commit:** `test(agent): make generated root manifest the Control Plane contract authority`

---

## 18. Task 13 — NativeAOT publish-link-run fixture

**Requirement case:** H09.

### 18.1 Create fixture project

Create:

```text
tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/
├── CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.csproj
└── Program.cs
```

Project properties:

```xml
<OutputType>Exe</OutputType>
<TargetFramework>net10.0</TargetFramework>
<PublishAot>true</PublishAot>
<IsAotCompatible>true</IsAotCompatible>
<InvariantGlobalization>true</InvariantGlobalization>
<WarningsAsErrors>$(WarningsAsErrors);IL2026;IL2070;IL2072;IL2075;IL3050;SYSLIB1034</WarningsAsErrors>
```

Reference ControlPlane.Abstractions. Do not add reflection serializer packages.

### 18.2 Implement native executable scenarios

Program must perform and verify:

1. `DescriptorSearchRequest` serialize/deserialize through generated metadata.
2. `AgentToolResult<string>` serialize/deserialize.
3. `IReadOnlyList<AgentToolDescriptor>` serialize/deserialize.
4. single `AgentToolDescriptor` serialize/deserialize.
5. `DescriptorActivationReviewDecision` deserialize using its exact generated
   property.
6. `CanonicalHash` deserialize using its exact generated property.
7. attempt an unrelated unregistered type and confirm fail-closed behavior.

The program groups these checks under two named scenarios and prints:

```text
ReflectionFallback_IsDisabled:PASS
SerializeDeserialize_RepresentativeToolRoots:PASS
```

On success print exactly:

```text
CONTROL_PLANE_JSON_CONTRACT_NATIVEAOT_OK
```

Return non-zero with a clear message on any mismatch.

### 18.3 Create fixture test

Create:

```text
tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests/
├── CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests.csproj
└── ControlPlaneJsonContractAotFixtureTests.cs
```

Test name:

```text
PublishAndRun_ControlPlaneJsonContracts
```

Use the established linux-x64 subprocess pattern:

```bash
rtk dotnet publish "<fixture>" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:CrestCreatesPublishMode=aot \
  --disable-build-servers \
  -o "<temp-output>"
```

Assertions:

- linux-x64 only; explicit xUnit skip elsewhere;
- publish exit code zero;
- no IL2026/IL3050/SYSLIB1034 warnings;
- native executable exists;
- original executable runs;
- exit code zero;
- sentinel present;
- output directory contains no task/Roslyn assemblies.

### 18.4 Wire solutions

Add both AOT projects to:

- `CrestCreates.slnx`;
- `solutions/CrestCreates.All.slnx`;
- `solutions/CrestCreates.Runtime.slnx`.

### 18.5 Verify

```bash
rtk dotnet test \
  tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests \
  --disable-build-servers
rtk git diff --check
```

**Commit:** `test(agent): add NativeAOT gate for generated Control Plane JSON roots`

---

## 19. Task 14 — Full composition and regression gates

### 19.1 Ensure zero acceptance placeholders

```bash
rtk rg -n "AcceptanceSkeleton.Pending|Fact\\(Skip" \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests \
  tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests \
  tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/JsonContracts
```

Expected: no matches.

### 19.2 Tooling tests

```bash
rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests \
  --disable-build-servers

rtk dotnet test \
  tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests \
  --disable-build-servers
```

### 19.3 Control Plane and boundary tests

```bash
rtk dotnet test \
  tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests \
  --disable-build-servers

rtk dotnet test \
  tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --disable-build-servers
```

### 19.4 Agent/MCP composition regressions

```bash
rtk dotnet test \
  tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests \
  --filter "FullyQualifiedName~JsonContext" \
  --disable-build-servers

rtk dotnet test \
  tests/Integrations/CrestCreates.Mcp.Tests \
  --filter "FullyQualifiedName~JsonContext" \
  --disable-build-servers

rtk dotnet test \
  tests/Integrations/CrestCreates.Mcp.Memory.Tests \
  --disable-build-servers
```

Expected:

- contributor ordering unchanged;
- root ownership unchanged;
- no new Control Plane contributor;
- resolver freezing unchanged.

### 19.5 Canonical build

```bash
rtk dotnet build CrestCreates.slnx --disable-build-servers
```

Expected: zero errors.

If canonical full test time is acceptable:

```bash
rtk dotnet test CrestCreates.slnx --no-build --disable-build-servers
```

Otherwise execute all touched layered solution/test projects and record the
reason full solution tests were not run.

### 19.6 Generated artifact inspection

Inspect:

```bash
rtk rg -n \
  "JsonSerializable|SurfaceRootTypes|ExplicitRootTypes|AllDirectRootTypes" \
  src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/obj/Debug/net10.0/CrestCreates.JsonContracts.g.cs
```

Confirm:

- only direct inferred roots have generated attributes;
- Explicit Extras are not duplicated;
- Control Plane manifest is internal;
- all ordering is stable;
- no absolute paths/timestamps.

### 19.7 Package/publish leakage inspection

Run package tests again after Release build and inspect fixture outputs. No task
or Roslyn runtime leakage is allowed.

### 19.8 Final diff hygiene

```bash
rtk git status --short
rtk git diff --check
rtk git diff --stat
```

Review every modified file against the Scope and Non-goals.

**Commit:** `test(json-contracts): close package composition and regression gates`

---

## 20. Task 15 — Documentation, status, and implementation evidence

This task runs only after Tasks 0-14 are GREEN. The Spec and Plan are already
approved before implementation begins.

### 20.1 Record approved deviations without changing approval status

Do not change the Spec's `APPROVED` status based on implementation outcome.
Design approval records a reviewed boundary; implementation verification is a
separate state.

Append only deviations that received explicit design review, especially:

- secondary TFM selected for the fixture;
- exact transport sentinel mechanism;
- final task dependency package allowlist;
- any additional reviewed Explicit Extra.

If implementation discovers an unapproved design change, stop, return the Spec
to review, and record the new approval separately. Do not treat passing tests as
design approval.

### 20.2 Update memory.md

Add a concise completed feature entry:

```text
Issue #58 — Control Plane JSON Contract Root Generation
Status: Implemented / NativeAOT-verified
Mainline: explicit surface → pre-CoreCompile task → STJ SG
Package: direct opt-in, no runtime leakage
Control Plane manifest: Internal
Explicit Extras: DescriptorActivationReviewDecision, CanonicalHash
Evidence: unit, MSBuild, package, Control Plane, boundary, Agent/MCP regression,
linux-x64 native publish-link-run
```

Include actual test counts and commands from the completed run, not planned
counts.

Also update the Issue implementation record/status to
`Implemented / NativeAOT-verified` with links to the actual evidence. This does
not alter the already-approved Spec/Plan status.

### 20.3 Verify documentation references

- Spec links to Issue #58 and comment.
- Plan links to Spec.
- `memory.md` links to both.
- No document claims Source Generator chaining.
- No document claims NativeAOT from analyzers alone.

### 20.4 Final documentation verification

```bash
rtk git diff --check
rtk rg -n \
  "runtime reflection fallback|assembly-wide public|SG.*STJ|NativeAOT-friendly" \
  docs/superpowers/specs/2026-07-24-control-plane-json-contract-root-generation-design.md \
  docs/superpowers/plans/2026-07-24-control-plane-json-contract-root-generation.md \
  memory.md
```

Review matches manually for forbidden or obsolete claims.

**Commit:** `docs: record Issue 58 JSON contract generation completion`

---

## 21. Per-diagnostic acceptance map

| Diagnostic | Required tests |
|---|---|
| `CJC001` | non-partial, nested, generic, non-JsonSerializerContext |
| `CJC002` | missing Type, non-interface, open interface |
| `CJC003` | generic interface method |
| `CJC004` | ref/out/in/pointer/function-pointer/ref-like parameter |
| `CJC005` | open/unbound root within nested generic/array, ref return, ref readonly return |
| `CJC006` | inaccessible root from generated partial |
| `CJC007` | ErrorType and same-project ordinary SG-only type |
| `CJC008` | user/generated manifest name collision |
| `CJC009` | unreadable source/syntax-tree construction exception |
| `CJC010` | unreadable/invalid metadata reference |
| `CJC011` | marker or STJ required symbol unresolved |
| `CJC012` | generated/manifest/stamp/temp path escape plus manifest/source write failure with previous file preservation |
| `CJC013` | invalid manifest accessibility |
| `CJC014` | mixed transport, competing task path, second effective target import; no custom task invocation or side effect |

For every diagnostic:

- severity is Error;
- build/task returns failure;
- no partial `.g.cs` replaces a prior valid file;
- stamp is not advanced;
- message includes actionable identity/location;
- a focused unit test and, where MSBuild-specific, a real build contract test
  exist.

---

## 22. Final exit checklist

### Developer experience

- [ ] Adding a Control Plane tool changes DTO/interface only.
- [ ] Clean first build produces STJ metadata in the same compilation.
- [ ] Multiple business parameters need no context edit.
- [ ] `AgentToolResult<string>` needs no exception.

### Semantic correctness

- [ ] Inherited/diamond interfaces are deterministic.
- [ ] Exact exclusions work only on parameters.
- [ ] Scalars/enums/collections/nullables/closed generics are direct roots.
- [ ] Nested property graphs are not promoted to roots.
- [ ] `ref` and `ref readonly` returns fail with `CJC005`.
- [ ] Every generated root has method provenance.
- [ ] Every invalid root fails closed.

### SDK/MSBuild correctness

- [ ] Semantic analysis runs after `GenerateGlobalUsings`.
- [ ] Actual Global Usings `.g.cs` is in source inputs and manifest.
- [ ] Source addition/deletion invalidates generation.
- [ ] Semantic no-op preserves generated source timestamp.
- [ ] Success stamp prevents repeated stale execution.
- [ ] Missing output invalidates stamp.
- [ ] Multi-TFM/Debug/Release outputs are isolated.
- [ ] Design-time build reuses only prior formal output.
- [ ] Clean removes only task-owned intermediate artifacts.
- [ ] Generated source, manifest, stamp, and temp directory are all
  boundary-contained by normalized `IntermediateOutputPath`.
- [ ] Changing `AllowedOutputRoot` invalidates the input manifest.

### Manifest/API correctness

- [ ] Surface, Explicit, and All sets are correct.
- [ ] Internal is default.
- [ ] Control Plane manifest is Internal.
- [ ] Public mode is proven from a separate assembly.
- [ ] Public manifest instances cannot be downcast or mutated as mutable sets.
- [ ] #58 does not wire cross-assembly contributors.

### Transport/package correctness

- [ ] One inner build has one transport and task path.
- [ ] Mixed repository/package transport fails with `CJC014`.
- [ ] Conflict failure precedes the first custom task invocation and every
  task-owned side effect.
- [ ] Both task mappings use `Runtime="NET"` and `Architecture="*"`.
- [ ] Duplicate guarded import cannot generate/include twice.
- [ ] Package uses `build/`, not `buildTransitive/`.
- [ ] Local-feed PackageReference-only first build succeeds.
- [ ] Transitive consumer does not run generation.
- [ ] `.deps.json` and private task dependencies are present.
- [ ] Microsoft.Build host assemblies are not packed.
- [ ] No task/Roslyn assemblies leak to runtime or publish.

### Control Plane correctness

- [ ] Handwritten context retains only options, surfaces, and reviewed Extras.
- [ ] Explicit Extras are exactly Decision and CanonicalHash unless review
  records another direct root.
- [ ] Legacy public-record scan is removed.
- [ ] Legacy known-exclusion and BCL special lists are removed.
- [ ] Representative wire JSON is unchanged.
- [ ] Activation parser remains typed/AOT-safe.
- [ ] Agent/MCP contributor composition is unchanged.

### Executable evidence

- [ ] Pure semantic/model tests pass.
- [ ] Real MSBuild contract tests pass.
- [ ] NuGet package tests pass.
- [ ] Control Plane tests pass.
- [ ] Boundary tests pass.
- [ ] Agent/MCP JSON regressions pass.
- [ ] Canonical solution build passes.
- [ ] linux-x64 NativeAOT publish links and original binary executes.
- [ ] No `AcceptanceSkeleton.Pending` or fixture Skip remains except
  platform-specific AOT skip.
- [ ] `git diff --check` passes.

---

## 23. Proposed commit sequence

1. `test(json-contracts): establish acceptance fixtures and case matrix skeleton`
2. `feat(core): add declarative JSON contract surface marker`
3. `feat(json-contracts): add semantic compilation and context discovery`
4. `feat(json-contracts): infer deterministic direct roots from interface surfaces`
5. `feat(json-contracts): model explicit roots and manifest accessibility`
6. `feat(json-contracts): emit byte-stable STJ roots and root manifests`
7. `feat(json-contracts): add deterministic input manifest and safe task adapters`
8. `feat(json-contracts): run semantic generation after SDK compile inputs`
9. `feat(json-contracts): close incremental multi-targeting and clean behavior`
10. `feat(json-contracts): package direct opt-in build transport`
11. `refactor(agent): generate Control Plane JSON direct roots from surfaces`
12. `test(agent): make generated root manifest the Control Plane contract authority`
13. `test(agent): add NativeAOT gate for generated Control Plane JSON roots`
14. `test(json-contracts): close package composition and regression gates`
15. `docs: record Issue 58 JSON contract generation completion`

Do not squash away the acceptance-first and package/AOT evidence before review.
