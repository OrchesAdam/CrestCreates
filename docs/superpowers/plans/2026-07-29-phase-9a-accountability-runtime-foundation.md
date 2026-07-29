# Phase 9a Accountability Runtime Foundation Implementation Plan

> Implement the approved contract through four ordered, independently green PR
> slices. The Spec is normative; this Plan defines execution order, file
> ownership, TDD checkpoints, and verification evidence without expanding the
> Phase 9a boundary.

**Goal:** Establish one immutable, AOT-safe Accountability source model and
write boundary, migrate HTTP/AOP and resolved Capability execution to it, add
post-save Workflow lifecycle facts, and prove the real Procurement HTTP →
Capability → Workflow mainline through the existing NativeAOT fixture.

**Spec:** `docs/superpowers/specs/2026-07-28-phase-9a-accountability-runtime-foundation-design.md`

**Issue:** #39

**Branch:** `feature/phase-9a-accountability-runtime-foundation-39`

**Spec status:** APPROVED

**Plan status:** APPROVED FOR IMPLEMENTATION

```text
PR 1 readiness: PASS
PR 2 readiness: PASS
PR 3 readiness: PASS
PR 4 readiness: PASS
Phase 9b reusable test boundary: PASS
NativeAOT/AOP evidence path: PASS
```

Mandatory implementation amendments are part of this approved Plan:

1. move `AuditedMoAttribute` with a concrete-assembly type forward;
2. use a fully typed, opaque AOP bridge contract;
3. normalize Tags with `StringComparer.Ordinal`;
4. document prompt-return requirements for asynchronous sinks/observers; and
5. register AuditLogging composition through the existing Module mainline.

---

## 1. Execution rules

- Use `rtk` for every shell command and `apply_patch` for file edits.
- Before any build, test, inventory, or mutation, run this mandatory preflight:

  ```bash
  rtk --version
  rtk dotnet --info
  ```

  If either command is unavailable or fails, stop immediately and report the
  environment problem. Do not silently fall back to unwrapped shell commands.
- Begin each behavior with one named failing test from Spec §18. Confirm the
  failure is caused by the missing behavior, not a broken fixture.
- Make the smallest production change that turns the focused test green, then
  run the owning project and the slice regression set.
- Keep the four PR slices ordered: PR 1 → PR 2 → PR 3 → PR 4. A later slice may
  depend on earlier contracts; an earlier slice may not reference a later
  producer.
- Keep each slice buildable and reviewable. If published as stacked PRs, each PR
  targets its immediate predecessor; if delivered on one branch, preserve the
  same four commit/review boundaries.
- Do not introduce reflection JSON, runtime type scanning,
  `DefaultJsonTypeInfoResolver`, `Dictionary<string, object>` payloads,
  `object? Payload`, or new `IL2026`/`IL3050` suppression.
- Do not add an Accountability→AuditLogging/Capability/Workflow/HumanTask/
  ASP.NET dependency.
- Do not register `AuditLogService`, `ICapabilityAuditStore`, or a process-local
  side dictionary as `IAuditSink`.
- Do not create a second hash implementation. Accountability uses the existing
  Canonical Hash Runtime projection API and structured `CanonicalHash` values.
- Do not use TraceId, SpanId, ASP.NET TraceIdentifier, a Workflow lifecycle ID,
  or HumanTaskInstanceId as invented causation.
- Do not make post-fact audit failure replace an HTTP/method/Capability result,
  original exception, or committed Workflow state.
- Do not expand Phase 9a into durable stores, queries, retention, Outbox,
  HumanTask decision accountability, Agent governance replacement, Event
  accountability, or lineage readers.
- Preserve unrelated user changes. Never delete files directly; move obsolete
  files to `99_RecycleBin/` when retention is unnecessary.
- Keep the Spec status independent from implementation status. Update
  `memory.md` to Implemented only after the final executable gates pass.
- `IAuditRecorder.RecordAsync` always treats its token as caller cancellation.
  A producer whose business effect is already committed passes
  `CancellationToken.None`; the recorder never guesses whether a token came
  from a business operation.

## 2. Ordered delivery map

| PR | Deliverable | Depends on | Must not include |
|---|---|---|---|
| 1 | Contract kernel, immutable operation context, generated JSON, runner-free Testing boundary scaffold | Approved Spec | recorder, sinks, producer adapters |
| 2 | validation, sanitizer, protected projection, canonical hash, recorder, reusable sink cases, in-memory sink | PR 1 | HTTP/Capability/Workflow migration |
| 3 | HTTP, AOP, Capability adapters and legacy unwiring | PR 2 | Workflow state migration, Procurement closure |
| 4 | HumanTask trigger identity, Workflow lifecycle adapter, Procurement/AOT closure | PR 3 | durable/query provider work |

The intended dependency shape after PR 4 is:

```text
Core.Abstractions + Metadata.Abstractions
    ↑
Accountability.Abstractions
    ↑
Accountability → Metadata Canonical Hash Runtime
    ↑
AuditLogging / Capability / Workflow producer adapters

HumanTask completion EventId → Workflow continuation trigger identity
Dynamic API → Capability only (no direct Accountability dependency)
```

## 3. Project and solution map

### New production projects

```text
src/Runtime/Audit/CrestCreates.Accountability.Abstractions/
src/Runtime/Audit/CrestCreates.Accountability/
```

Dependencies:

- `CrestCreates.Accountability.Abstractions` references only
  `CrestCreates.Core.Abstractions`, `CrestCreates.Metadata.Abstractions`, and the
  JSON Contract BuildTasks as build-only infrastructure.
- `CrestCreates.Accountability` references Abstractions and
  `CrestCreates.Metadata` for `ICanonicalHashComputer`.
- `CrestCreates.AuditLogging(.Abstractions)`, Capability, and Workflow reference
  Accountability Abstractions only.
- HumanTask Abstractions receives only the required EventId string contract; it
  does not need an Accountability dependency.

### New test-support and test projects

```text
tests/Shared/CrestCreates.Accountability.Testing/
tests/Runtime/Audit/CrestCreates.Accountability.Abstractions.Tests/
tests/Runtime/Audit/CrestCreates.Accountability.Tests/
```

`CrestCreates.Accountability.Testing` is a non-test runtime library located
under `tests/Shared`: set `IsTestProject=false`, reference only
Accountability Abstractions, and reference no test SDK, xUnit runner, or
concrete Accountability runtime. It owns:

```text
IAuditSinkContractDriver
AuditSinkContractCases
AuditSinkContractFixture
AuditSinkContractAssertions
```

The cases expose framework-neutral asynchronous methods and throw a dedicated
contract assertion exception on failure. Concrete provider test projects own
their `[Fact]`/`[Theory]` wrappers. The dependency shape is binding:

```text
Accountability.Testing → Accountability.Abstractions
Accountability.Tests → Accountability.Testing
Phase9b.Provider.Tests → Accountability.Testing
```

`Phase9b.Provider.Tests` must never reference
`CrestCreates.Accountability.Tests` or the InMemory sink.

### Solution wiring

Add production and test projects to:

- `CrestCreates.slnx`
- `solutions/CrestCreates.All.slnx`
- `samples/ProcurementApproval/CrestCreates.Sample.ProcurementApproval.slnx`
  where required by the sample/AOT closure

The primary integration change owns shared solution files. Do not let producer
tasks independently rewrite the same `.slnx` files.

## 4. Requirement-to-test ledger

Spec §18 contains the full binding acceptance skeleton. The following ledger
controls slice ownership; every named Spec test must exist and be green before
its owning PR closes.

| ID | Owning tests | Requirement |
|---|---|---|
| C01 | `AuditEnvelopeContractTests` | immutable v1 shape, required fields, stable kinds, hard limits |
| C02 | `AuditCausalityContractTests` | cause/parent/previous separation and immutable AsyncLocal frames |
| C03 | `AuditDataContractTests` | no capture default, bounded candidate/safe JSON, hash basis |
| C04 | `AuditRuntimeSemanticMappingTests` | explicit InvocationSource and runtime-reference mappings |
| C05 | generated JSON round-trip/architecture tests | BuildTasks-derived roots, no reflection resolver |
| R01 | `DefaultAuditRecorderTests` | validation → sanitization → protected comparison → hash → all sinks |
| R02 | `AuditSanitizationRuleRegistryTests` | one owner per Kind, PolicyVersion/RuleVersion separation, deny unknown |
| R03 | shared `AuditSinkContractCases` + `InMemoryAuditSinkContractTests` | reusable provider contract boundary, thread-safe Accepted/Duplicate/Conflict and first acceptance metadata |
| R04 | `AccountabilityCompositionTests` | explicit sink composition and production-host requirement |
| H01 | `HttpAccountabilityAdapterTests` | safe route, typed outcome, terminal OccurredAt, no body/header capture |
| H02 | `AuditedMethodAccountabilityTests` | one method fact, nested scope, no reflection serialization, outcome preservation |
| P01 | `CapabilityAccountabilityMiddlewareTests` | resolved execution fact, actor/source/hash, result closure, original outcome |
| P02 | Capability architecture/compatibility tests | legacy stores unwired and never accepted sinks |
| W01 | `WorkflowAccountabilityObserverTests` | save-before-fact, fixed target/outcome, actor/origin, exact causality |
| W02 | HumanTask runtime/Workflow continuation tests | stable completion EventId and trigger propagation |
| A01 | `AccountabilityArchitectureTests` | dependency direction and forbidden fallback guards |
| E01 | Procurement acceptance tests | HTTP → Capability → Workflow correlation and linkage |
| E02 | Procurement NativeAOT fixture | publish, native link, run original binary, sentinel |

---

## 5. PR 1 — Contract kernel and generated JSON

### Task 1.1 — Scaffold projects and freeze dependency boundaries

**Files**

- Add both production `.csproj` files, both test `.csproj` files, and the
  non-test `tests/Shared/CrestCreates.Accountability.Testing` project.
- Modify `CrestCreates.slnx` and `solutions/CrestCreates.All.slnx`.
- Add `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/AccountabilityArchitectureTests.cs`.

**RED**

- Add architecture tests proving Abstractions cannot reference ASP.NET Core,
  AuditLogging, Capability, Workflow, HumanTask, Agent, Persistence, or Platform.
- Add tests proving producer projects may reference Accountability Abstractions
  but never the concrete runtime or `IAuditSink` implementation types.
- Add a source/assembly guard forbidding `object` payload fields, mutable
  envelope collections, reflection JSON, and Accountability warning suppression.
- Add boundary gates:
  `AccountabilityTestingReferencesNoConcreteAccountabilityRuntime`,
  `AccountabilityTestingReferencesNoTestRunnerPackage`, and
  `DurableProviderCanReuseContractCasesWithoutReferencingInMemorySink`.

**GREEN**

- Create minimal projects with the exact dependency direction from §3.
- Create the shared Testing project with only its driver/case/assertion API;
  provider-independent case implementations land with the PR 2 sink work.
- Opt Abstractions into JSON Contract BuildTasks using the existing repository
  props/targets and build-only ProjectReference pattern used by Control Plane.
- Add InternalsVisibleTo only for the owning runtime/tests where needed.

**Focused verification**

```bash
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk dotnet build src/Runtime/Audit/CrestCreates.Accountability.Abstractions
rtk dotnet build src/Runtime/Audit/CrestCreates.Accountability
```

### Task 1.2 — Add immutable v1 contracts and semantic constants

**New Abstractions areas**

```text
Contracts/AuditEnvelope.cs
Contracts/AuditActor.cs
Contracts/AuditAction.cs
Contracts/AuditTarget.cs
Contracts/AuditOutcome.cs
Contracts/AuditDescriptorContext.cs
Contracts/AuditRuntimeContext.cs
Contracts/AuditDataSnapshot.cs
Contracts/AuditEvidenceReference.cs
Contracts/AuditPayload.cs
Contracts/AuditSanitizationStamp.cs
Contracts/AuditTagMap.cs
Semantics/AuditSemanticNames.cs
Validation/AuditContractLimits.cs
Composition/IAccountabilityRuntimeMarker.cs
```

Use `ImmutableArray<T>` and `ImmutableSortedDictionary<string,string>` exactly
as approved. `AuditTagMap.Empty` is the only default Tags instance and is built
with `ImmutableSortedDictionary.Create<string,string>(StringComparer.Ordinal)`;
`AuditEnvelope.Tags` defaults to it. Clone accepted `JsonElement` values; never
expose a mutable list or producer-owned JSON document lifetime to a sink.

Freeze the v1 maxima from Spec §10, including candidate/safe envelope limits.
Unknown extension Kinds pass the stable grammar; Outcome Status and
AuditDataHashBasis remain closed.

**RED tests**

- All `AuditEnvelopeContractTests` and `AuditDataContractTests` names from Spec
  §18.1.
- Explicit boundary cases for null Tenant, empty CorrelationId, self
  Parent/Previous, duplicate references/tags, unknown Outcome, invalid
  CanonicalHash metadata, oversized candidate Payload/artifact/envelope, and
  sanitized output overflow.
- `RejectsDefaultImmutableArrayState` for every immutable-array contract and
  `RejectsNullImmutableSortedDictionaryDespiteNullableWarnings` for every
  immutable-map contract. A default array is not treated as a valid empty array.
- `AuditTagMapEmptyUsesOrdinalComparer` and
  `EnvelopeDefaultsToOrdinalAuditTagMap` freeze the PR 1 contract default.

**GREEN**

- Add the immutable records, stable semantic constants, closed outcome/hash
  basis contracts, validation issue codes, and exact hard-limit constants.
- Define the ordinal tag factory/default and freeze normalization as a recorder
  requirement implemented in PR 2; PR 1 adds no recorder behavior.
- Keep `Sanitization` and `Integrity` recorder-owned and nullable on the producer
  candidate.

### Task 1.3 — Add operation context, origin, identity, and clock seams

**New files**

```text
Context/AuditOperationContext.cs
Context/IAuditOperationContextAccessor.cs
Context/AuditOrigin.cs
Identity/IAuditIdentityGenerator.cs
```

The concrete accessor belongs to PR 2, but the contract lands here. Use
`EnclosingAuditId`, not a required generic AuditId. IDs remain strings;
Accountability Audit/Operation/lifecycle IDs use one injectable identity seam.
The HumanTask completion EventId remains a persisted transport identity
owned by HumanTask and does not force an Accountability dependency into that
runtime. Runtime timestamps use `TimeProvider`; do not scatter
`DateTimeOffset.UtcNow` through Accountability adapters.

**RED tests**

- Root and nested operation mapping.
- Parent/Previous never substitute for each other.
- Trace identifiers never substitute for business correlation/causation.
- Stable identity generator seams are injectable in tests.

### Task 1.4 — Add Recorder/Sink/Sanitizer/Hash interfaces and result contracts

**New Abstractions areas**

```text
Recording/IAuditRecorder.cs
Recording/AuditRecordResult.cs
Recording/AuditRecordIssue.cs
Sinks/IAuditSink.cs
Sinks/AuditSinkWriteResult.cs
Sinks/AuditSinkFailure.cs
Sanitization/IAuditSanitizer.cs
Sanitization/IAuditPayloadSanitizationRule.cs
Sanitization/IAuditDataArtifactSanitizationRule.cs
Hashing/IAuditIntegrityHasher.cs
Json/AccountabilityJsonSerializerContext.cs
```

Contract rules:

- `AuditRecordResult` stores SinkResults, SinkFailures, and Issues separately.
- Every hash field is a structured `CanonicalHash`.
- `IsAccepted` derives from Accepted/Duplicate SinkResults.
- Sink result identity/hash echoes are validated by the recorder in PR 2.
- `ProcessedAt` is attempt metadata; `FirstAcceptedAt` is provider-local; there
  is no Envelope RecordedAt.
- Sanitizer composition PolicyVersion and per-rule RuleVersion are distinct.
- `IAccountabilityRuntimeMarker` is a pure presence contract. The concrete
  runtime registers it with `IAuditRecorder`; producer-owned startup validators
  use both registrations to prove the Foundation is present.
- `IAuditSink.WriteAsync` XML documentation is normative: implementations MUST
  return their `ValueTask` promptly and MUST NOT perform unbounded synchronous
  blocking before returning it. Recorder timeout bounds asynchronous completion
  after invocation returns; it does not isolate a contract-violating synchronous
  blocker. The recorder does not use `Task.Run` as provider isolation.
- Except for caller cancellation, the recorder contract does not expose
  infrastructure exceptions. Candidate/rule violations return `Rejected` with
  stable issues; unexpected validation/projector/hasher/recorder faults return
  `Failed` with `AUDIT_RECORDER_INTERNAL_FAILURE`, log the original exception,
  and call no sink. Sink exceptions remain per-provider `SinkFailures`.

**RED tests**

- Generated JSON round-trip covers every direct root derived from
  `[JsonContractSurface(typeof(IAuditSink), ...)]`.
- Public API tests prove no `object`, mutable collection, string hash downgrade,
  AcceptedSinkIds backing state, or global record timestamp exists.
- Result-contract tests freeze caller cancellation as `OperationCanceledException`
  rather than a fabricated Failed result, and freeze the stable recorder
  internal-failure issue code.
- `SinkContractDocumentsPromptReturnRequirement` freezes the provider contract.

### Task 1.5 — PR 1 gate

```bash
rtk dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Abstractions.Tests
rtk dotnet build tests/Shared/CrestCreates.Accountability.Testing
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk dotnet build CrestCreates.slnx
rtk git diff --check
```

**PR 1 exit**

- Phase 9b-visible contracts compile and round-trip through generated STJ.
- The operation context contract can represent an operation without inventing
  an enclosing fact.
- No recorder, sink implementation, or producer migration has leaked into PR 1.

---

## 6. PR 2 — Recorder, sanitizer, canonical hash, and in-memory sink

### Task 2.1 — Implement immutable AsyncLocal operation scopes

**Files**

```text
src/Runtime/Audit/CrestCreates.Accountability/Context/AuditOperationContextAccessor.cs
tests/Runtime/Audit/CrestCreates.Accountability.Tests/Context/AuditOperationContextAccessorTests.cs
```

**RED**

- `ParallelSiblingScopesDoNotInterfere`
- `ChildScopeDoesNotMutateParentExecutionContext`
- `NestedAwaitPreservesCurrentScope`
- `OutOfOrderDisposeFailsWithoutCorruptingParent`

**GREEN**

- Store one immutable frame containing Context + Parent in `AsyncLocal<Frame?>`.
- Push replaces the current frame; dispose succeeds only when the exact frame is
  current, then restores Parent.
- Never put a mutable `Stack<T>` inside AsyncLocal.

### Task 2.2 — Implement candidate and safe-snapshot validation

**Files**

```text
Validation/AuditEnvelopeValidator.cs
Validation/AuditCandidateSafetyWalker.cs
Validation/AuditCanonicalHashValidator.cs
Validation/AuditValidationResult.cs
CanonicalHashing/AccountabilityCanonicalProjectionWriter.cs
```

**RED**

- Candidate Payload, single artifact, and total envelope overflow fail before a
  sanitizer rule is invoked.
- `TagSnapshotUsesOrdinalComparer`, `TagOrderingIsCultureIndependent`,
  `TagHashIsStableAcrossCultures`, `OrdinalDuplicateTagIsRejected`, and
  `CanonicalWriterDoesNotTrustInputDictionaryOrder`. Run the culture-sensitive
  cases under `en-US`, `tr-TR`, and `zh-CN` and require the same hash.
- Safe output limits run again after sanitization.
- Required fields, semantic grammar, closed Outcome, self relations, duplicate
  references/tags, JSON duplicate properties, and hash metadata return stable
  field paths/codes.

**GREEN**

- Candidate pre-sanitization size and structural safety use the separate
  `AuditCandidateSafetyWalker` so oversized untrusted input never reaches a
  rule. It may reject early but is not the safe-envelope hash projection.
- Validate tag keys/values, reject ordinal duplicates, and rebuild both the
  candidate snapshot and sanitizer output as new `StringComparer.Ordinal`
  immutable dictionaries. Never preserve a producer/rule comparer.
- Safe-envelope `MaxSafeEnvelopeBytes` uses the same
  `AccountabilityCanonicalProjectionWriter` as integrity hashing, writing into
  a counting `IBufferWriter<byte>`. There is exactly one field traversal, null
  policy, ordering policy, `JsonElement` traversal, and CanonicalHash metadata
  projection for safe size and hash.
- Canonical projection explicitly orders Tags with `StringComparer.Ordinal`; it
  never trusts the dictionary's comparer or enumeration order.
- Do not reflection-serialize the candidate or safe Envelope to learn its size.
- Return `AuditRecordIssue` values only; never include rejected values or raw
  exception messages.

### Task 2.3 — Implement sanitizer registries and protected-fact equality

**Files**

```text
Sanitization/DefaultAuditSanitizer.cs
Sanitization/AuditPayloadSanitizationRuleRegistry.cs
Sanitization/AuditDataArtifactSanitizationRuleRegistry.cs
Sanitization/AuditProtectedFactProjector.cs
Sanitization/AuditProtectedFactComparer.cs
```

**RED**

- Duplicate Kind owners fail startup.
- Unknown Kind and rule exception reject without fallback.
- PolicyVersion and RuleVersion remain distinct in the stamp/rule IDs.
- Payload rules cannot change Kind/Version; artifact rules cannot change Kind.
- A structurally valid rewrite of Actor, causality, Action, Target, Outcome,
  Runtime, Descriptor, or Evidence returns
  `AUDIT_SANITIZER_REWROTE_PROTECTED_FACT` and calls no sink.
- DisplayName, SafeSummary, Tags, DataSnapshot, and Payload data may be minimized.

**GREEN**

- Resolve rules by exact ordinal Kind, with no wildcard/pass-through fallback.
- Compare a hand-written protected projection before safe-snapshot validation.
- Apply the returned Sanitization stamp only after the sanitizer returns an
  Envelope whose Sanitization/Integrity fields are still null.

### Task 2.4 — Integrate the existing Canonical Hash Runtime

**Files**

```text
CanonicalHashing/AccountabilityCanonicalProjectionWriter.cs
CanonicalHashing/DefaultAuditIntegrityHasher.cs
```

**RED**

- Same sanitized fact and policy produce the same structured hash.
- Ordered/unordered fields follow the Spec canonical rules.
- Integrity and attempt/provider times do not enter the projection.
- Sanitization stamp, AuditId, ContractVersion, OccurredAt, ParentAuditId, and
  PreviousAuditId do enter the projection.
- The measured safe-envelope byte count equals the exact canonical projection
  bytes passed to the hash runtime for the same Envelope.

**GREEN**

- Add `CanonicalHashArtifactNames.AccountabilityRecord` in Metadata Abstractions.
- Use `ICanonicalHashComputer.ComputeFromProjection` with a hand-written
  `Utf8JsonWriter` projection and the exact v1 metadata from Spec §9.5. The
  counting writer and hash writer both call this one authoritative projection.
- Do not add direct SHA-256 calls or `JsonSerializer.Serialize(object)`.

### Task 2.5 — Implement recorder and deterministic multi-sink fan-out

**Files**

```text
Recording/DefaultAuditRecorder.cs
Recording/AuditWriteBudget.cs
Identity/DefaultAuditIdentityGenerator.cs
```

**RED**

- Validation occurs before sanitization; protected comparison and safe
  validation occur before hash/sinks.
- `CallerCancellationBeforeProcessingCancelsAttempt`.
- `OneHungSinkDoesNotPreventOtherSinksFromBeingAttempted`.
- `AllSinksShareOneTotalWriteBudget`.
- `SynchronousSinkThrowDoesNotPreventLaterSinkStart`.
- `TimeoutResultsRemainInDeterministicSinkOrder`.
- Conflict remains in SinkResults, provider exceptions in SinkFailures, and
  recorder rejection in Issues.
- SinkId/AuditId/Integrity echo mismatch becomes
  `AUDIT_SINK_RESULT_MISMATCH` under the registered SinkId.
- Accepted/Duplicate determine IsAccepted and Capability closure later.
- Hasher/projector/internal recorder faults return Failed with
  `AUDIT_RECORDER_INTERNAL_FAILURE`, expose no raw exception, and call no sink.
- `HasherFailureReturnsStableFailedResultAndCallsNoSink` and
  `ProjectorFailureReturnsStableFailedResultAndCallsNoSink` freeze unexpected
  internal-fault behavior; rule exceptions remain stable sanitizer rejection.
- `HungSinkTestUsesIncompleteAsyncOperation` constructs the hung provider from
  a never-completing Task returned immediately as `ValueTask`; it must not use
  `Thread.Sleep`, `.Wait()`, `.Result`, or `GetAwaiter().GetResult()`.

**GREEN**

- Materialize the immutable safe Envelope once and pass the same snapshot to
  every sink.
- Honor the caller token at every pre-fan-out stage. Cancellation before or
  during the recorder attempt propagates `OperationCanceledException`; it is
  not converted to a Sink timeout or Failed result.
- Sort registered sinks by `Sink.Id` using ordinal comparison. In that order,
  start every sink call before awaiting any one sink. Catch a synchronous throw
  while obtaining a `ValueTask` as that sink's provider failure and continue
  starting later sinks.
- Give all started calls one shared total `AccountabilityOptions.WriteTimeout`,
  race their aggregate against the single deadline, and cancel the shared sink
  token when it expires. A sink that ignores cancellation cannot extend the
  recorder attempt: incomplete sinks become `AUDIT_SINK_TIMEOUT`, and their late
  tasks are safely observed to prevent unobserved exceptions.
- Once fan-out starts, every sink is invoked even if caller cancellation arrives
  while the short synchronous start loop is running. Caller cancellation and
  the total timeout both race the aggregate; caller cancellation wins as
  `OperationCanceledException`, while only deadline expiry creates
  `AUDIT_SINK_TIMEOUT` failures.
- The total deadline starts before the ordered invocation loop, but can only
  govern calls after they return an awaitable. A sink that blocks synchronously
  violates `IAuditSink`; no finite async algorithm can start later sinks in that
  case without thread isolation, which Phase 9a intentionally rejects.
- Aggregate results in ordinal `Sink.Id` order regardless of completion order.
  The budget is total, never `sink count × WriteTimeout`.
- A producer whose business effect is already committed calls the recorder with
  `CancellationToken.None`; the recorder then applies this finite total budget.
  Producer adapter tests, not recorder tests, prove canceled business tokens do
  not suppress a post-commit attempt.
- Stamp ProcessedAt once per recorder attempt after aggregation.

### Task 2.6 — Implement in-memory sink and shared provider contract suite

**Files**

```text
Sinks/InMemoryAuditSink.cs
Sinks/InMemoryAuditSinkEntry.cs
tests/Shared/CrestCreates.Accountability.Testing/Sinks/IAuditSinkContractDriver.cs
tests/Shared/CrestCreates.Accountability.Testing/Sinks/AuditSinkContractCases.cs
tests/Shared/CrestCreates.Accountability.Testing/Sinks/AuditSinkContractFixture.cs
tests/Shared/CrestCreates.Accountability.Testing/Sinks/AuditSinkContractAssertions.cs
tests/Runtime/Audit/CrestCreates.Accountability.Tests/Sinks/InMemoryAuditSinkContractTests.cs
```

**RED/GREEN matrix**

- New ID → Accepted.
- Same ID/full structured Integrity → Duplicate with original FirstAcceptedAt
  when known.
- Same ID/different Integrity → Conflict with ExistingIntegrity and no overwrite.
- Concurrent identical writes produce one accepted snapshot.
- Write/read return deep snapshots and deterministic order.
- In-memory registration is explicit development/test behavior only.
- The InMemory xUnit wrapper calls the shared cases. A compile-only fake durable
  provider wrapper demonstrates reuse without referencing the InMemory sink or
  the concrete Accountability runtime.

### Task 2.7 — DI, options, and startup composition

**Files**

```text
DependencyInjection/AccountabilityServiceCollectionExtensions.cs
Startup/AccountabilityCompositionValidator.cs
Options/AccountabilityOptions.cs
```

`AddAccountability()` registers `IAccountabilityRuntimeMarker` and
`IAuditRecorder`, but no sink. Library default
RequireAtLeastOneSink=false. First-party production hosts set it true; tests and
development explicitly call `AddInMemoryAccountability()`.

The operational `WriteTimeout` default is five seconds and must be finite and
positive. Hosts may lower or replace it through options; it is not Envelope or
hash contract metadata.

The Foundation's own `IHostedService` composition validator checks duplicate
Sink.Id, duplicate sanitization Kind ownership, and the required-sink option at
Generic Host `StartAsync`. `AddAccountabilityWithoutSinkIsAllowedWhenRequireSinkFalse`
and `ProducerHostWithFoundationButRequiredSinkMissingFailsDuringStartup` freeze
the two sink modes. A Null recorder is forbidden.

This validator does not prove that a producer remembered to add the Foundation;
producer-owned startup validators land with the producer migrations in PR 3/4.

### Task 2.8 — PR 2 gate

```bash
rtk dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Abstractions.Tests
rtk dotnet build tests/Shared/CrestCreates.Accountability.Testing
rtk dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk dotnet build CrestCreates.slnx
rtk git diff --check
```

**PR 2 exit**

- Every sink receives only one validated, sanitized, immutable, hashed snapshot.
- Replay/conflict and rejection/provider failure are unambiguous.
- Phase 9b can reuse the provider contract suite without redefining the sink.

---

## 7. PR 3 — HTTP, AOP, and Capability adapters

### Task 3.1 — Convert HTTP middleware to a root Accountability fact

**Primary files**

```text
src/Runtime/Audit/CrestCreates.AuditLogging/Middlewares/AuditLoggingMiddleware.cs
src/Runtime/Audit/CrestCreates.AuditLogging/Modules/AuditLoggingModule.cs
src/Runtime/Audit/CrestCreates.AuditLogging/Services/AuditLogWriter.cs
src/Runtime/Audit/CrestCreates.AuditLogging.Abstractions/Options/AuditLoggingOptions.cs
```

Add producer-owned helpers beside the middleware for safe route/actor/runtime
mapping and HTTP outcome classification. Phase 9a adds no public rejection
feature without a first-party writer.

**RED**

- HTTP success, generic 4xx, 5xx, unhandled exception, and RequestAborted map
  exactly as Spec §12.1.
- Action/Target use METHOD + normalized endpoint route template, never
  `GetDisplayUrl()` or query.
- RequestId, Activity TraceId, and SpanId remain separate.
- Authorization/Cookie headers, bodies, IP, User-Agent, exception message, and
  stack are absent by default.
- Audit failure cannot replace the HTTP status/result/original exception.
- `PostCommitProducerDoesNotPassCancelledBusinessToken` proves the terminal
  middleware uses `CancellationToken.None` for its bounded recorder attempt.

**GREEN**

- Allocate HTTP AuditId/OperationId/CorrelationId before `next` and push the
  operation scope with EnclosingAuditId=HTTP AuditId.
- Resolve Actor/Tenant only from trusted framework contexts.
- Record after terminal outcome observation and always restore/pop scope.
- Every current numeric 4xx remains failed with `HTTP_<status>`; Phase 9a does
  not claim a built-in HTTP `rejected` mainline. A typed rejection adapter is
  deferred until a real first-party authorization/validation/governance
  producer can set it. Do not add a test-only Feature or string-keyed Items
  protocol. `NoBuiltInHttpRejectedPathWithoutTypedFirstPartyProducer` freezes
  this boundary.
- Keep `AuditContext` as compatibility observation state only. The middleware's
  authoritative write is `IAuditRecorder`.
- Register HTTP helpers, `IAuditedMethodAccountabilityRuntime`, and the
  AuditLogging producer validator through
  `AuditLoggingModule.OnConfigureServices`; do not introduce a parallel Runtime
  registration extension.

### Task 3.2 — Make every `[AuditedMo]` invocation a separate fact

**Primary files**

```text
src/Runtime/Audit/CrestCreates.AuditLogging.Abstractions/Annotations/AuditedMoAttribute.cs
src/Runtime/Audit/CrestCreates.AuditLogging.Abstractions/MethodAccountability/IAuditedMethodAccountabilityRuntime.cs
src/Runtime/Audit/CrestCreates.AuditLogging.Abstractions/MethodAccountability/IAuditedMethodInvocationState.cs
src/Runtime/Audit/CrestCreates.AuditLogging.Abstractions/MethodAccountability/AuditedMethodInvocationDescriptor.cs
src/Runtime/Audit/CrestCreates.AuditLogging.Abstractions/MethodAccountability/AuditedMethodInvocationOutcome.cs
src/Runtime/Audit/CrestCreates.AuditLogging/Interceptors/AuditedMethodAccountabilityRuntime.cs
src/Runtime/Audit/CrestCreates.AuditLogging/Interceptors/AuditedMethodInvocationState.cs
src/Runtime/Audit/CrestCreates.AuditLogging/Properties/TypeForwards.cs
src/Runtime/Audit/CrestCreates.AuditLogging/CrestCreates.AuditLogging.csproj
src/Runtime/Audit/CrestCreates.AuditLogging.Abstractions/CrestCreates.AuditLogging.Abstractions.csproj
```

**RED**

- Separate fact per invocation, including multiple/nested methods.
- Standalone methods create a root operation.
- `MethodContext.Datas` retains immutable per-invocation ID/time/scope state.
- IncludeParameters/IncludeResult never reflection-serialize arguments/results.
- Audit failure preserves returned result and original method exception.
- Scope always disposes on success, exception, and cancellation.
- `AuditFailureDoesNotReplaceMethodResult` and
  `AuditFailureDoesNotReplaceOriginalMethodException` cover the complete
  post-fact failure invariant.
- A canceled method/business token does not suppress the terminal method fact;
  the runtime calls the recorder with `CancellationToken.None`.
- `AuditedMoAttributeHasSingleTypeDefinition`,
  `OldAssemblyQualifiedNameResolvesForwardedType`,
  `ForwardedTypeLivesInAuditLoggingAbstractions`,
  `ConcreteAuditLoggingAssemblyContainsTypeForward`, and
  `SourceConsumerSeesNoAmbiguousAttributeType` freeze binary/source identity.
- `MethodRuntimeContractContainsNoObjectState`,
  `AttributeDependsOnlyOnAuditLoggingAbstractionsContracts`,
  `ConcreteInvocationStateDoesNotLeakIntoAttributeAssembly`,
  `SuccessExceptionCancellationUseOneExitPath`, and
  `MethodContextDatasUsesOpaqueTypedState` freeze the bridge boundary.

**GREEN**

- Move the declarative attribute into AuditLogging Abstractions and reference
  `Rougamo.Extensions.DependencyInjection.Abstractions` plus Accountability
  Abstractions; `Rougamo.Fody` belongs only to assemblies that contain woven
  methods.
- Preserve namespace `CrestCreates.AuditLogging.Interceptors`, public type name,
  constructor, and public property surface. Remove the old concrete-assembly
  definition and add exactly one concrete-assembly forward:

  ```csharp
  using System.Runtime.CompilerServices;
  using CrestCreates.AuditLogging.Interceptors;

  [assembly: TypeForwardedTo(typeof(AuditedMoAttribute))]
  ```

  This preserves the old assembly-qualified binary reference without defining
  a second Attribute type.
- Move the obsolete concrete source file to a Phase 9a folder under
  `99_RecycleBin/` after transferring its definition; do not delete it directly
  and do not leave a compiled duplicate behind.
- Freeze the public bridge as four Abstractions contracts: the runtime
  interface, marker-only `IAuditedMethodInvocationState`, typed invocation
  descriptor, and typed outcome descriptor/closed outcome kind. Public methods
  pass the opaque state interface, never `object`, a dictionary, concrete state,
  `MethodContext`, parameters, return values, or `Exception` instances.
- Freeze the bridge signatures and closed outcome vocabulary as:

  ```csharp
  public interface IAuditedMethodAccountabilityRuntime
  {
      IAuditedMethodInvocationState Enter(
          AuditedMethodInvocationDescriptor descriptor);

      void SetOutcome(
          IAuditedMethodInvocationState state,
          AuditedMethodInvocationOutcome outcome);

      ValueTask ExitAsync(IAuditedMethodInvocationState state);
  }

  public interface IAuditedMethodInvocationState
  {
  }

  public enum AuditedMethodOutcomeKind
  {
      Succeeded = 1,
      Failed = 2,
      Cancelled = 3
  }
  ```

  `AuditedMethodInvocationDescriptor` has required MethodId, ActionName, and
  StartedAt. `AuditedMethodInvocationOutcome` has required Kind and optional
  stable SafeCode. The bridge accepts no business cancellation token; Exit owns
  the bounded post-fact call with `CancellationToken.None` internally.
- Enter is deliberately synchronous and Rougamo forces `OnEntry` to execute
  synchronously. This is required for the immutable AsyncLocal operation frame
  to remain visible to the woven business method; an async entry hook would
  restore its caller ExecutionContext before the method starts. HTTP composition
  supplies the runtime through an AuditLogging-owned ambient frame and must not
  replace the Host ServiceProvider through `DependencyInjection.StaticAccessor`.
- The lifecycle is binding: Enter returns opaque typed state; Success,
  Exception, or Cancellation reports only a typed outcome; Exit is the single
  fact-materialization/recording/scope-disposal path. The descriptor contains
  stable MethodId, ActionName, and StartedAt; the outcome contains only closed
  Kind and optional stable SafeCode.
  The attribute is a thin woven forwarder: it resolves
  `IAuditedMethodAccountabilityRuntime`, passes a typed method identity/outcome
  descriptor with no arguments/result/exception detail, and keeps the returned
  `IAuditedMethodInvocationState` in `MethodContext.Datas`. That Rougamo carrier
  is invocation-local state only and is never part of `AuditEnvelope`.
- The concrete AuditLogging runtime implements entry/outcome/exit handling,
  allocates method IDs, pushes the scope, observes duration/outcome, records with
  `CancellationToken.None` after the invocation terminates, and always disposes
  the scope. The declaration project contains no recorder implementation.
- Preserve the legacy attribute flags as inert compatibility API until an
  explicit typed snapshot provider exists.
- Remove the `JsonSerializer.Serialize(object)` calls and `IL2026` suppressions.

### Task 3.3 — Migrate resolved Capability execution to IAuditRecorder

**Primary files**

```text
src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs
src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionResult.cs
src/Runtime/Capability/CrestCreates.Capability.Abstractions/Execution/InvocationSource.cs
src/Runtime/Capability/CrestCreates.Capability/Middleware/AuditMiddleware.cs
src/Runtime/Capability/CrestCreates.Capability/CapabilityPipeline.cs
src/Runtime/Capability/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs
src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointMapper.cs
src/Runtime/Agent/CrestCreates.Agent.Tools/Invocation/AgentToolInvoker.cs
src/Integrations/CrestCreates.Mcp/McpToolInvoker.cs
```

**RED**

- All resolved executions entering middleware emit a fact for returned success,
  returned failure, CapabilityFailureException, cancellation, timeout result,
  and unknown exception.
- `CAPABILITY_NOT_FOUND` remains explicitly outside execution-fact coverage.
- Actor follows effective invocation authority; CapabilityId is never an actor.
- Full descriptor `CanonicalHash`, stable source, correlation, cause, parent,
  duration, terminal OccurredAt, and ExecutionId are retained.
- AuditRecordId is attached only if at least one sink Accepted/Duplicate,
  including results constructed by the outer catch.
- Dynamic API never writes TraceIdentifier into CausationId.
- Trusted AgentId maps to Actor `agent`; missing/untrusted AgentId maps to the
  explicit unknown actor and is never inferred from CapabilityId.
- MCP has request/invocation/session/host identity but no trusted client
  identity in its current contract. It therefore maps Actor to
  `unknown/unknown`; those protocol IDs remain runtime references. A future
  trusted MCP client identity may map to `mcp-client/<id>` without changing the
  current fallback.
- `AgentSourceWithoutTrustedAgentIdUsesUnknownActor`,
  `AgentSourceWithExplicitAgentIdUsesAgentActor`, and
  `McpSourceWithoutTrustedClientIdentityUsesUnknownActor` freeze these cases;
  `McpRequestIdentityIsRuntimeReferenceNotActorIdentity` prevents inference.
- Returned/converted terminal paths call the recorder with
  `CancellationToken.None`; cancellation still maps the business Capability
  outcome to cancelled but does not suppress the bounded post-fact attempt.

**GREEN**

- Stop `CapabilityExecutionContext.CorrelationId` from generating an unrelated
  value before ambient bridging; fill ambient/new-root context in the pipeline.
- Freeze the execution propagation surface as:

  ```csharp
  public CanonicalHash CapabilityContract { get; internal set; }
  public string CorrelationId { get; set; } = string.Empty;
  public string? CausationId { get; set; }
  public string? ParentAuditId { get; set; }
  public AuditActor? AccountabilityActor { get; set; }
  public ImmutableArray<AuditRuntimeReference> AccountabilityRuntimeReferences { get; set; } = [];
  public string? AuditRecordId { get; internal set; }
  public string? ExecutionId { get; internal set; }
  ```

  The initialized empty runtime-reference array is valid; a default
  `ImmutableArray` injected at a public boundary is rejected.
- Preserve the full descriptor structured hash while retaining the old string
  `.Value` projection only as obsolete compatibility state.
- Add an explicit exhaustive mapper for current `InvocationSource` members:
  Http→http, Workflow→workflow, HumanTask→human-task, Agent→agent, Mcp→mcp,
  Event→integration, BackgroundJob→system, and Internal→system.
- Allocate ExecutionId/AuditId before `next`, push the Capability scope, observe
  and record every returned/thrown path, then preserve the original path.
- Store accepted AuditRecordId in execution context so outer catch conversion can
  copy it into the final result without losing the original stack internally.
- Agent and MCP invokers set the explicit Actor/runtime-reference properties
  directly; `Items` remains compatibility/business execution state and is not
  the authoritative Accountability identity channel.

### Task 3.4 — Unwire legacy append-only audit stores

**Files**

```text
src/Runtime/Capability/CrestCreates.Capability.Abstractions/ICapabilityAuditStore.cs
src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionRecord.cs
src/Runtime/Capability/CrestCreates.Capability/NullCapabilityAuditStore.cs
src/Runtime/Capability/CrestCreates.Capability/InMemoryCapabilityAuditStore.cs
src/Runtime/Audit/CrestCreates.AuditLogging/Services/AuditLogService.cs
```

**Work**

- Mark legacy Capability contracts obsolete and remove them from default DI and
  AuditMiddleware.
- Keep a pure sanitized Envelope→CapabilityExecutionRecord mapping for
  compatibility/export tests only.
- Keep AuditLog persistence/query APIs available to legacy callers but unwired
  from Accountability fan-out.
- Do not implement `LegacyCapabilityAuditStoreSink` or `LegacyAuditLogSink`.
- Replace old mainline store assertions with InMemoryAuditSink assertions.

Required architecture tests prove mapping does not count as acceptance, only a
contract-compliant sink can close Capability AuditRecordId, and no side
dictionary claims durable idempotency.

### Task 3.5 — Add producer-side startup validation and migrate composition

`AddAccountability()` cannot validate a host that never called it. Each enabled
producer therefore owns and registers an independent Generic Host startup
validator:

```text
AuditLoggingAccountabilityCompositionValidator : IHostedService
CapabilityAccountabilityCompositionValidator   : IHostedService
```

At `StartAsync`, each validator resolves both `IAccountabilityRuntimeMarker`
and `IAuditRecorder`; absence throws a stable
`AccountabilityCompositionException` before the host accepts work. The
validators live in their producer projects and depend only on Accountability
Abstractions. Registration has one mainline per producer:

```text
AuditLoggingModule.OnConfigureServices
    → AuditLoggingAccountabilityCompositionValidator
    → IAuditedMethodAccountabilityRuntime
    → HTTP Accountability helpers

AddCapabilityPipeline
    → CapabilityAccountabilityCompositionValidator

AddCapabilityRuntime
    → AddCapabilityPipeline (no second registration list)
```

Use `TryAddEnumerable` for hosted validators so composed registration paths
yield one instance. Do not add a parallel public `AddAuditLogging()` mainline;
the existing Module owns AuditLogging registration. If a private registration
helper is needed, only the Module calls it and it remains the single list. These
validators do not depend on a validator registered by the missing Foundation.
Non-Generic-Host unit tests invoke the validator explicitly; production startup
proof uses `Host.StartAsync`.

The existing `CrestCreates.Application.AuditLog.AddAuditLogging()` registers
application/query services only. Inventory and retain it in that role; it is
not an AuditLogging Runtime composition root and must not receive a second copy
of the producer validator/runtime helper registrations.

**Required RED tests**

- `CapabilityHostWithoutAccountabilityFailsDuringStartup`
- `AuditLoggingHostWithoutAccountabilityFailsDuringStartup`
- `AddAccountabilityWithoutSinkIsAllowedWhenRequireSinkFalse`
- `ProducerHostWithFoundationButRequiredSinkMissingFailsDuringStartup`
- `AuditLoggingModuleRegistersOneCompositionValidator`
- `CapabilityRegistrationPathsRegisterOneCompositionValidator`
- `NoParallelAuditLoggingRegistrationMainline`

Before changing registrations, save the output of this inventory in the PR
evidence and classify every match as unit fixture, integration host, sample,
Platform host, or NativeAOT fixture:

```bash
rtk rg -n "AddCapabilityRuntime\(|AddCapabilityPipeline\(|AddAuditLogging\(|UseAuditLogging\(|AuditLoggingModule" --glob '*.cs' src tests samples
```

Migrate every classified host/fixture in the same slice. Production/sample/AOT
hosts call `AddAccountability()` and set the required-sink policy explicitly;
tests/dev fixtures call `AddAccountability()` plus explicit InMemory
registration unless the test intentionally proves startup failure. This
inventory includes current Agent/MCP AOT and E2E fixtures, Dynamic API AOT/E2E,
Capability tests, LibraryManagement, Platform Web, Procurement, and Descriptor
Control Plane hosts.

### Task 3.6 — PR 3 gate

```bash
rtk dotnet test tests/Runtime/Audit/CrestCreates.AuditLogging.Tests
rtk dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests
rtk dotnet test tests/Framework/Api/CrestCreates.DynamicApi.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.E2E.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.E2E.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk dotnet build CrestCreates.slnx
rtk git diff --check
```

Also search the changed mainline for forbidden remnants:

```bash
rtk rg -n "JsonSerializer.Serialize\(|IL2026|LegacyCapabilityAuditStoreSink|LegacyAuditLogSink|CausationId = context.TraceIdentifier" src/Runtime/Audit src/Runtime/Capability src/Runtime/Agent src/Integrations/CrestCreates.Mcp src/Framework/Api/CrestCreates.DynamicApi samples/ProcurementApproval
```

Review every `JsonSerializer.Serialize(` match in the owned diff. The final
Roslyn architecture test resolves each invocation symbol and rejects object,
object-array, runtime-`Type`, or overloads without generated `JsonTypeInfo` /
generated metadata; a textual zero-match shortcut is not acceptable. All other
forbidden matches must be zero in the Phase 9a producer mainline.

**PR 3 exit**

- HTTP, method, and resolved Capability facts compose through one operation
  context and one recorder.
- Legacy stores remain compatibility artifacts, never sink authorities.
- All post-fact failure paths preserve the business outcome.
- Capability and AuditLogging hosts fail at startup, not first invocation, when
  the Foundation is absent.

---

## 8. PR 4 — Workflow lifecycle and Procurement mainline

PR 4 retains one external slice but has four mandatory, independently green
commit/review boundaries. Do not squash them until review and NativeAOT
diagnosis are complete:

```text
4A HumanTask EventId persistence/recovery
4B Workflow typed lifecycle/origin migration
4C Workflow Accountability observer, causality, and post-commit budget
4D Procurement + AOP + NativeAOT closure
```

Each boundary runs its owning project plus all earlier PR 4 projects before the
next boundary begins.

### Task 4.1 — Give HumanTask completion a stable trigger EventId

**Primary files**

```text
src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs
src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs
src/Runtime/HumanTask/CrestCreates.HumanTask/DefaultHumanTaskRuntime.cs
tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests/HumanTaskRuntimeTests.cs
```

**RED/GREEN**

- Allocate a producer EventId before the completion state save.
- Persist `CompletionEventId` in HumanTaskInstance and copy it in Snapshot.
- Publish the same EventId after save and reuse it during completion-dispatch
  recovery; do not manufacture a new cause on retry.
- A failed HumanTask save publishes no completion EventId/event.
- This identity does not claim HumanTask decision Accountability.

### Task 4.2 — Persist Workflow origin and lifecycle sequence state

**Primary files**

```text
src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/WorkflowExecutionRequest.cs
src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/WorkflowContinuationRequest.cs
src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs
src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/WorkflowLifecycleEvent.cs
```

**Work**

- Add optional AuditOrigin to execution request.
- Add TriggerOperationId/TriggerAuditId to continuation request.
- Persist AuditOrigin and LastLifecycleAuditId on WorkflowInstance and Snapshot.
- Replace lifecycle `object? Payload` with the complete typed event fields from
  Spec §14.2.
- Preserve HumanTask result only in existing Workflow continuation/business
  state; never expose it through the lifecycle fact.

**RED**

- Snapshot preserves origin/last linkage.
- Parent and Previous remain distinct after suspend/reload/resume.
- HumanTaskInstanceId is a runtime reference only.

### Task 4.3 — Add lifecycle factory, observer fan-out, and adapter

**New/modified Workflow files**

```text
WorkflowLifecycleEventFactory.cs
IWorkflowLifecycleObserver.cs
WorkflowLifecycleEventPublisher.cs
IWorkflowPostCommitNotificationBudget.cs
DefaultWorkflowPostCommitNotificationBudget.cs
WorkflowPostCommitNotificationOptions.cs
WorkflowAccountabilityObserver.cs
WorkflowAccountabilityCompositionValidator.cs
WorkflowServiceCollectionExtensions.cs
```

The producer-owned Accountability observer lives in Workflow so Accountability
never references Workflow.

**RED/GREEN**

- Publisher invokes observers in DI registration order and attempts every
  observer; one observer failure is diagnosed and does not roll back state or
  suppress later observers.
- `IWorkflowLifecycleObserver` XML documentation is normative: implementations
  MUST return their `ValueTask` promptly and MUST NOT perform unbounded
  synchronous blocking before returning it. Notification timeout governs
  asynchronous completion only; Workflow does not wrap observers in `Task.Run`.
- `AddWorkflowEngine()` registers its own
  `WorkflowAccountabilityCompositionValidator : IHostedService`. It checks
  `IAccountabilityRuntimeMarker` and `IAuditRecorder` during Host startup;
  registration uses `TryAddEnumerable`, and
  `WorkflowHostWithoutAccountabilityFailsDuringStartup` is binding.
- `WorkflowEngineRegistersOneCompositionValidator`,
  `ObserverContractDocumentsPromptReturnRequirement`, and
  `HungObserverTestUsesIncompleteAsyncOperation` are binding. The hung observer
  returns a never-completing Task immediately and uses no blocking wait.
- `OwnedSinkAndObserverImplementationsContainNoBlockingWait` is a Roslyn/source
  architecture guard over first-party implementations; it rejects `.Wait()`,
  `.Result`, `Thread.Sleep`, and `GetAwaiter().GetResult()`.
- Observer maps Actor=workflow, InitiatedBy=stored origin, Target=Workflow
  instance, descriptor id/version/hash, run ExecutionId, and canonical runtime
  references.
- Started/suspended/resumed are indeterminate; completed is succeeded; failed is
  failed with a stable safe ReasonCode/fallback `WORKFLOW_FAILED`.
- Action EventType uses the explicit five-value mapping, never enum ToString.

### Task 4.4 — Enforce run operation causality and post-save publication

**Primary files**

```text
src/Runtime/Workflow/CrestCreates.Workflow/WorkflowEngine.cs
src/Runtime/Workflow/CrestCreates.Workflow/WorkflowExecutionRunner.cs
src/Runtime/Workflow/CrestCreates.Workflow/WorkflowContinuationService.cs
src/Runtime/Workflow/CrestCreates.Workflow/HumanTaskCompletedWorkflowSubscriber.cs
```

**Execution rules**

1. Allocate one WorkflowRunOperationId per Execute/Continue.
2. Capture actual EnclosingAuditId and Workflow Actor in the run scope.
3. Capture PreviousAuditId before assigning the new lifecycle AuditId.
4. Mutate state and persist origin/linkage/status.
5. If save fails, publish no lifecycle event/fact.
6. After save, stamp committed-transition OccurredAt and enter a Workflow-owned
   post-commit notification budget; never pass the original business token.
7. Start/call every observer in DI registration order before awaiting any one.
   Catch synchronous/async observer failures independently and share one finite
   notification deadline across the fan-out. Timed-out observers are diagnosed;
   no observer outcome rolls back committed state.
8. The Accountability observer records with `CancellationToken.None`; the
   Recorder then applies its own total write budget.

`WorkflowPostCommitNotificationOptions.Timeout` defaults to five seconds and
must be finite and positive. The Workflow-owned budget has no dependency on
`AccountabilityOptions`; it bounds lifecycle notification rather than sink I/O.
Its deadline cannot isolate a contract-violating observer that blocks before
returning its awaitable; the prompt-return XML contract is the boundary.

**Required post-commit tests**

- `CommittedTransitionStillNotifiesObserversWhenBusinessTokenIsCancelled`
- `CancelledBeforeStoreSaveProducesNoTransitionFact`
- `OneObserverFailureDoesNotSuppressLaterObserver`
- `NotificationTimeoutDoesNotRollbackCommittedState`

Mapping:

- started cause/parent = initiating operation/enclosing audit;
- initial suspend/complete/fail cause = current run operation and previous =
  prior lifecycle AuditId;
- resumed cause = completion EventId, parent = trigger AuditId when it exists,
  previous = suspended AuditId;
- post-resume terminal cause = continuation run operation, previous = resumed
  AuditId.

No mapping may use previous lifecycle EventId/AuditId or HumanTaskInstanceId as
direct cause.

Before 4C, inventory every Workflow composition root and classify/migrate each
unit fixture, integration host, sample, Platform host, and AOT fixture:

```bash
rtk rg -n "AddWorkflowEngine\(" --glob '*.cs' src tests samples
```

Every non-negative host adds the Foundation and explicit sink policy before its
startup gate is enabled.

### Task 4.5 — Migrate Procurement acceptance to Accountability

**Primary files**

```text
samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/Program.cs
samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Application/ProcurementApplicationService.cs
samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Application/CrestCreates.Sample.Procurement.Application.csproj
samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Application/FodyWeavers.xml
samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.Tests/Acceptance/*
samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.AotFixture.Tests/ProcurementAotFixtureTests.cs
samples/ProcurementApproval/scripts/run-nativeaot-golden-scenario.sh
```

**Work**

- Register `AddAccountability`, explicit InMemoryAuditSink, and production-host
  sink requirement for the scenario; remove `AddInMemoryCapabilityAudit`.
- Enable AuditLogging HTTP middleware on the actual sample route.
- Migrate MCP/Agent/HumanTask/Capability source assertions from
  ICapabilityAuditStore to filtered AuditEnvelope facts where applicable.
- Apply `[AuditedMo]` to the real
  `ProcurementApplicationService.SubmitAsync` method. Reference the declarative
  AuditLogging Abstractions API from the Application project, add
  `Rougamo.Fody` and `FodyWeavers.xml` to that same Application project, and
  keep AuditLogging runtime registration in the Host. The Host project must not
  claim weaving for a method compiled into another assembly.
- Freeze the actual native call chain as HTTP generated endpoint → Capability
  dispatcher/middleware → `SubmitProcurementRequestHandler` → woven
  `ProcurementApplicationService.SubmitAsync` → Workflow. No standalone probe
  method or Host-only woven method can satisfy the gate.
- Assert HTTP → submit Capability → workflow.started → workflow.suspended share
  CorrelationId and use exact cause/parent/previous semantics.
- Assert the woven Method fact shares HTTP CorrelationId and parents to its
  actual immediate Capability fact; that Capability fact parents to the HTTP
  fact. `MethodFactParentsToActualEnclosingHttpFact` remains an AOP adapter test
  for the separate case where HTTP is the immediate enclosing scope, while the
  Procurement native gate proves the truthful Method → Capability → HTTP chain.
- Assert structured descriptor hashes, Actor/source mappings, no raw body/token/
  DTO/exception/HumanTask visible data, and Capability AuditRecordId resolution.

**Mandatory weaving/mainline gates**

- `WovenMethodAssemblyContainsRougamoWeaver`
- `NativeScenarioActuallyInvokesWovenMethod`
- `MethodFactSharesHttpCorrelationId`
- `MethodFactParentsToActualEnclosingHttpFact` for direct HTTP composition
- `ProcurementMethodParentsCapabilityWithHttpAncestor`
- `NoStandaloneProbeCanSatisfyMainlineAopGate`

### Task 4.6 — Extend the existing NativeAOT publish-link-run gate

The existing Procurement fixture remains authoritative. Do not create another
sample or treat analyzer/build-only success as NativeAOT evidence.

The published original linux-x64 binary must exercise the real HTTP,
Capability, Workflow, and woven AOP path and print:

```text
CRESTCREATES_ACCOUNTABILITY_OK
```

The fixture test must fail if native publish/link fails, the executable exits
non-zero, any scenario marker is missing, or reflection/AOT warnings appear in
the Accountability path. It also inspects the Application build/publish
artifacts to prove Rougamo wove the assembly containing `SubmitAsync`, and the
sentinel is emitted only after an observed Method fact for that exact method.

### Task 4.7 — Final solution, CI, and memory closure

**Files**

- Canonical and Procurement `.slnx` files.
- Existing CI contract/NativeAOT gate scripts where the Procurement fixture is
  already invoked.
- `memory.md` only after executable evidence is complete.
- This Plan's task checkboxes/evidence section.

Update memory from Approved Design to Implemented/NativeAOT-verified only after
all focused, regression, boundary, sample, and native gates pass. Record exact
counts and the original binary sentinel.

### Task 4.8 — PR 4 gate

Run and record these internal boundary gates before the aggregate PR gate:

```bash
# 4A
rtk dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests

# 4B
rtk dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
rtk dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests --filter "FullyQualifiedName~Lifecycle|FullyQualifiedName~Origin|FullyQualifiedName~Snapshot"

# 4C
rtk dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
rtk dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests

# 4D
rtk dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
rtk dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
rtk dotnet test samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.Tests
rtk dotnet test samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.AotFixture.Tests -c Release
```

Each block must be green before the next commit/review boundary starts. Use the
actual final test namespace in the 4B filter; do not accept a zero-test run.

Aggregate PR gate:

```bash
rtk dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
rtk dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
rtk dotnet test samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.Tests
rtk dotnet test samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.AotFixture.Tests -c Release
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk dotnet build CrestCreates.slnx -c Release
rtk git diff --check
```

**PR 4 exit**

- Workflow lifecycle fact semantics match Spec §14 exactly and occur only after
  successful state save.
- HumanTask completion identity is stable through dispatch recovery without
  claiming full HumanTask Accountability.
- Procurement proves the full responsibility chain and AOP separation.
- The original NativeAOT binary prints the Accountability sentinel.

---

## 9. Full final verification order

Run focused projects first so a failure remains attributable, then run the
canonical solution gates:

```bash
rtk dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Abstractions.Tests
rtk dotnet build tests/Shared/CrestCreates.Accountability.Testing
rtk dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Tests
rtk dotnet test tests/Runtime/Audit/CrestCreates.AuditLogging.Tests
rtk dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests
rtk dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
rtk dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
rtk dotnet test tests/Framework/Api/CrestCreates.DynamicApi.Tests
rtk dotnet test samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk dotnet test samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.AotFixture.Tests -c Release
rtk dotnet build CrestCreates.slnx -c Release
rtk dotnet test
rtk git diff --check
```

Final source guards:

```bash
rtk rg -n "JsonSerializer.Serialize\(" src/Runtime/Audit src/Runtime/Capability src/Runtime/Workflow src/Runtime/Agent src/Integrations/CrestCreates.Mcp samples/ProcurementApproval
rtk rg -n "DefaultJsonTypeInfoResolver|#pragma warning disable IL2026|LegacyCapabilityAuditStoreSink|LegacyAuditLogSink" src/Runtime/Audit src/Runtime/Capability src/Runtime/Workflow src/Runtime/Agent src/Integrations/CrestCreates.Mcp samples/ProcurementApproval
rtk rg -n "CausationId.*TraceIdentifier|CausationId.*HumanTaskInstanceId|CausationId.*PreviousAuditId" src samples/ProcurementApproval
rtk rg -n "Thread\.Sleep|\.Wait\(|\.Result\b|GetAwaiter\(\)\.GetResult\(" src/Runtime/Audit/CrestCreates.Accountability/Sinks src/Runtime/Workflow/CrestCreates.Workflow/WorkflowLifecycleEventPublisher.cs src/Runtime/Workflow/CrestCreates.Workflow/WorkflowAccountabilityObserver.cs
```

Review every serializer match against the Roslyn architecture test that resolves
the selected overload and static argument type; `object`, `object[]`, runtime
`Type`, and calls without generated metadata are forbidden. Do not infer safety
from a narrow grep. Other forbidden matches must be absent from the mainline;
compatibility source types may remain only where explicitly obsolete and
unwired. Blocking-wait matches in first-party Sink/Observer implementations are
forbidden and are also enforced by the symbol-aware architecture test.

## 10. Review gates

After each task, inspect the entire owned diff and confirm:

1. cause, enclosing parent, and lifecycle previous are different relations;
2. producer meaning survives sanitization protected-projection comparison;
3. candidate and safe snapshots both obey hard byte limits;
4. all hash values remain structured CanonicalHash contracts;
5. Conflict, provider failure, and rejection remain separately observable;
6. no legacy append-only store participates in acceptance;
7. Actor means effective authority while technical execution stays in Runtime;
8. OccurredAt, ProcessedAt, and FirstAcceptedAt remain distinct;
9. post-fact failure never rewrites business outcome or committed state;
10. shared sink cases remain runner-free and concrete-runtime-free;
11. recorder fan-out and Workflow notification each use one total budget;
12. every producer validates Foundation presence at Host startup;
13. AOP evidence comes from the assembly containing the real invoked method;
14. the moved Attribute has one definition plus the concrete-assembly forward;
15. every Tags snapshot/hash uses ordinal semantics across cultures;
16. async provider deadlines rely on documented prompt return, never `Task.Run`;
17. no generated/AOT/reflection fallback or HumanTask/Agent overclaim appears.

## 11. Completion evidence checklist

- [x] PR 1 contract/JSON/boundary gates green.
- [x] PR 2 recorder/sanitizer/hash/in-memory sink gates green.
- [x] PR 3 HTTP/AOP/Capability and Dynamic API regression gates green.
- [x] PR 4 HumanTask/Workflow/Procurement gates green.
- [x] Shared Accountability.Testing boundary has no runner/runtime dependency
      and a durable provider can reuse its cases without the InMemory sink.
- [x] Composition inventories classify production producer roots (AuditLogging module,
      `AddCapabilityPipeline`/`AddCapabilityRuntime`, `AddWorkflowEngine`) and their
      focused test fixtures; intentionally foundation-free unit fixtures are covered
      by the producer composition-validator tests.
- [x] PR 4A/4B/4C/4D were independently green before the aggregate PR 4 gate.
- [x] AuditedMoAttribute type-forward and typed bridge compatibility gates pass.
- [x] Ordinal Tags culture/hash gates pass.
- [x] Sink and Workflow Observer prompt-return contracts and blocking guards pass.
- [ ] All Spec §18 named acceptance tests exist and pass.
- [x] Canonical `CrestCreates.slnx` Release build has zero errors.
- [ ] Full test suite has zero failures.
- [x] linux-x64 PublishAot completes native link and runs the original binary.
- [x] `CRESTCREATES_ACCOUNTABILITY_OK` is present in native output.
- [x] Forbidden-pattern and dependency-boundary searches are clean.
- [x] `memory.md` records Implemented evidence with the full-suite environment
      limitations explicitly classified below.
- [ ] Issue #39 receives exact test/build/AOT evidence and Phase 9b can start
      without changing the Phase 9a contract.

### Current execution note (2026-07-29)

The Phase 9a focused Release builds/tests, boundary suite, Procurement acceptance
suite (36/36), AOP compatibility tests, and the original linux-x64 NativeAOT
publish-link-run gate are green. The canonical `CrestCreates.slnx` Release build
also completed successfully with zero errors (172 warnings). The full Release
test command completed with failures outside the Phase 9a contract: Docker-backed
integration suites cannot start Docker in this environment, and unrelated
pre-existing dependency/mock suites fail independently. Those failures remain
unchecked rather than being represented as a false repository-wide green gate.
The remaining unchecked item is therefore the environment-blocked full-suite
gate, not a reopened contract decision.
