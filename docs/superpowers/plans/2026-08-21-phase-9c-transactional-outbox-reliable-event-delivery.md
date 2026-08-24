# Phase 9c — Transactional Outbox & Reliable Event Delivery Implementation Plan

> Implement Issue #25 through ordered Case-first TDD slices. The approved R4
> Spec is normative. This Plan fixes project placement, contract shapes,
> continuation-acceptance persistence, V012 schema, provider mechanics,
> HumanTask/Workflow/Accountability cutover order, first-party consumer
> migration, crash-process evidence, and NativeAOT closure. It does not reopen
> the frozen design.

**Goal:** Add one provider-neutral transactional Outbox mainline to the existing
Runtime commit kernel, with FullSemantic InMemory parity, FullDurable PostgreSQL
delivery, reliable HumanTask continuation acceptance, reliable prepared
Workflow Accountability delivery, and real linux-x64 NativeAOT execution.

**Spec:**
`docs/superpowers/specs/2026-08-20-phase-9c-transactional-outbox-reliable-event-delivery-design.md`

**Issue:** #25

**Branch:** `codex/phase-9c-transactional-outbox`

**Frozen Spec commit:** `8aef242e`

**Current-master baseline inspected by the Spec:** `d20341f0`

**Migration baseline:** V011 is the current checksummed catalog tail.

**Spec status:** R4 APPROVED / FROZEN

**Plan status:** R4 / APPROVED; implementation remediation in progress on PR #80

```text
Delivery contracts:      new Runtime.Delivery.Abstractions project
Delivery runtime:        new Runtime.Delivery project
Transactional writer:    joins existing Runtime transaction only
Dispatch store:          separate claim/Ack/Retry/DeadLetter owner
Continuation proof:      dedicated immutable acceptance table/store
HumanTask obligations:   creation request/policy; frozen on instance
Accountability payload:  prepared safe AuditEnvelope, never re-sanitized
Provider closure:        InMemory FullSemantic + PostgreSQL FullDurable
Migration:               V012_transactional_outbox
NativeAOT:               extend existing PostgreSQL AOT Host/Fixture
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
diff. If V012 already exists, V011 is no longer the migration tail, or master
has changed a touched transaction/migration/runtime surface, stop and reconcile
the Plan before editing. Never edit or renumber V001-V011.

### 1.2 Case-first TDD discipline

- Activate only the manifest entries owned by the current Slice.
- A Red must fail because the specified behavior is missing, not because the
  fixture, DI graph, database, or generated JSON context is broken.
- Shared semantic cases are static runner-free methods. Provider wrappers must
  invoke them and must not copy their assertions.
- Turn the focused Red Green with the smallest canonical-mainline change.
- Run changed-project builds, focused tests, relevant shared runners, boundary
  tests after dependency changes, and `git diff --check`.
- End each Slice with one reviewable commit and a handoff containing commit,
  files, Red evidence, Green commands/counts, active manifest entries, and
  unresolved findings.
- Do not claim NativeAOT verification before the newly published native binary
  completes Slice 9.
- Do not update `memory.md` to implemented before Slice 10 closure evidence.

### 1.3 Non-negotiable boundaries

- Do not add another Unit of Work, transaction coordinator, migration runner,
  Npgsql data source, DbContext, broker, Inbox, replay API, retention API, or
  product Outbox query API.
- Do not expose Npgsql/provider handles, SQLSTATE, payload CLR type names,
  reflection JSON, runtime handler scans, or `MakeGenericMethod` dispatch in
  the Outbox mainline.
- Do not let producer modules resolve `IOutboxDispatchStore`.
- Do not let Runtime Delivery reference HumanTask, Workflow, Accountability,
  Platform, Web, or a concrete provider.
- Do not use generic LocalEvent registrations as reliable Ack obligations.
- Do not make `IAuditSink` advertise a durability tier.
- Do not make waiting-key absence prove Workflow continuation success.
- Do not make post-resume `RunAsync` success an Outbox Ack condition.
- Do not auto-replay a producer delegate after commit unknown.
- Do not write `CompletionDispatchFailed` from the new mainline or retain the
  Procurement failure policy as the recovery path.
- Do not make the legacy EF DLQ a second terminal authority.
- Do not turn Agent Control Plane's existing in-memory ActivationRequest
  authority into a durable Store as part of Phase 9c.

### 1.4 Commit order

```text
Slice 1 contracts/tests
    -> Slice 2 immutable message/InMemory append
    -> Slice 3 InMemory dispatch
    -> Slice 4 V012/PostgreSQL parity
    -> Slice 5 neutral transaction and ambiguity closure
    -> Slice 6 hosted recovery/CrashWorker
    -> Slice 6.5 Accountability preparation foundation
    -> Slice 7 HumanTask/Workflow continuation cutover
    -> Slice 8 Workflow Accountability cutover
    -> Slice 9 NativeAOT/process evidence
    -> Slice 10 closure review
```

A later Slice cannot begin with an activated Red, an unreviewed shared-hotspot
change, or a missing evidence tuple from the current Slice.

---

## 2. Locked Implementation Decisions

### 2.1 Project boundary

Create exactly two production projects under the existing Eventing grouping:

```text
src/Runtime/Eventing/CrestCreates.Runtime.Delivery.Abstractions/
src/Runtime/Eventing/CrestCreates.Runtime.Delivery/
```

`Runtime.Delivery.Abstractions` references only Core.Abstractions and
Metadata.Abstractions, plus DI abstractions needed for explicit resolver
metadata. `Runtime.Delivery` references Delivery.Abstractions, Metadata, and
Hosting/DI. Domain handlers stay in their owning modules.

The abstraction assembly grants narrow `InternalsVisibleTo` access to:

```text
CrestCreates.Runtime.Delivery
CrestCreates.HumanTask
CrestCreates.Runtime.Persistence.InMemory
CrestCreates.Runtime.Persistence.PostgreSql
CrestCreates.Runtime.Delivery.Tests
CrestCreates.Runtime.Persistence.InMemory.Tests
CrestCreates.Runtime.Persistence.PostgreSql.Tests
```

This is used only for the active-requirement probe and test drivers. It is not
a product payload/history surface.

`CrestCreates.HumanTask.Abstractions` separately grants narrow friend access
for the internal obligation-preflight interface/result to exactly:

```text
CrestCreates.HumanTask
CrestCreates.Runtime.Persistence.InMemory
CrestCreates.Runtime.Persistence.PostgreSql
CrestCreates.HumanTask.Tests
CrestCreates.Runtime.Persistence.InMemory.Tests
CrestCreates.Runtime.Persistence.PostgreSql.Tests
```

The public creation-policy registration remains usable by first-party domain
modules; the provider preflight itself is not public API.

### 2.2 Workflow continuation discriminator

Choose a dedicated immutable receipt rather than modifying Workflow state:

```text
WorkflowContinuationAcceptance
    TenantScope
    CompletionEventId
    HumanTaskKey
    WorkflowKey
    Outcome
    Result
    WorkflowFromRevision
    WorkflowToRevision
    Integrity
    AcceptedAt observation only
```

The public Workflow abstraction owns the record, write result, and
`IWorkflowContinuationAcceptanceStore`. InMemory and PostgreSQL implement it.
The receipt is inserted in the same Runtime transaction as:

```text
Workflow Suspended -> Running
WaitingHumanTaskKey -> null
workflow.resumed prepared AuditEnvelope Outbox append
```

Identity rules:

```text
same CompletionEventId + exact receipt Integrity     -> Duplicate
same HumanTaskKey + different CompletionEventId      -> Conflict
same CompletionEventId + different durable identity -> Conflict
no waiting Workflow + no exact receipt               -> Conflict/fail closed
```

The receipt survives later Workflow revisions. Redelivery checks the receipt
before querying the old waiting correlation.

The receipt Integrity uses this frozen canonical profile:

```text
ArtifactKind          = RuntimeWorkflowContinuationAcceptance
Purpose               = Integrity
Scope                 = InternalFull
ContractVersion       = canonical-hash-v1
CanonicalShapeVersion = runtime-workflow-continuation-acceptance-v1
```

Its fixed projection order is:

```text
1. Tenant scope kind + TenantId
2. CompletionEventId
3. HumanTaskKey
4. WorkflowKey
5. canonical Outcome already persisted by HumanTask completion
6. Result identity: null marker, or ordinal TypeId + nullable SchemaRef + exact JsonPayload
7. WorkflowFromRevision
8. WorkflowToRevision
```

`AcceptedAt` is observation metadata and is excluded. The writer recomputes
the profile from the proposed receipt before persistence; same EventId/keys
with a changed Outcome or Result is Conflict, never Duplicate.

### 2.3 Required HumanTask completion obligations

Keep the existing HumanTask descriptor canonical profiles exactly at
`humantask-contract-hash-v1` and `humantask-definition-hash-v1`; Phase 9c does
not add an obligation field to `HumanTaskDescriptor`, bump either canonical
shape, or introduce legacy pin-profile resolution.

Add an ordinal-sorted, duplicate-free `RequiredCompletionConsumerIds` contract
to `HumanTaskCreationRequest`. Add immutable creation-time obligation-policy
registrations keyed by exact HumanTask descriptor ID/version. `PrepareAsync`
freezes the union on `HumanTaskInstance`:

```text
creation-request declared IDs
    + registered creation-policy IDs matching the resolved descriptor ID/version
    + crest.workflow.humantask-continuation/v1 when WorkflowKey != null
```

The instance snapshot is immutable for the rest of its lifecycle. Completion
copies the exact set to Outbox `RequiredConsumerIds`. Delivery never adds an ID
from current DI.

Before returning from `PrepareAsync`, HumanTask validates the complete frozen
union—including caller-declared request IDs, policy IDs, and automatic Workflow
continuation ID—against the active non-generic HumanTask completion-consumer
metadata catalog. The final set must be a subset of registered stable IDs; an
unknown/typo ID fails task creation before persistence. This is creation-time
composition validation only and never becomes delivery-time obligation
inference.

The metadata-only registration shape is fixed:

```text
HumanTaskCompletionObligationPolicyRegistration
    HumanTaskDescriptorId
    HumanTaskDescriptorVersion
    RequiredConsumerId
```

It has no resolver delegate and no execution method. Duplicate exact triples
collapse; distinct consumer IDs for one descriptor version are allowed. Any
blank/invalid field fails bootstrap before task creation, and every policy ID
must match exactly one generic typed required-consumer registration in the
active composition.

PostgreSQL stores the obligation set in an authority column
`required_consumer_ids_json`; `HumanTaskInstance` state JSON does not become a
second authority. Mark `HumanTaskInstance.RequiredCompletionConsumerIds` with
`[JsonIgnore]`; the PostgreSQL provider materializer validates the authority
column and restores the snapshot field through narrow internal access.

First-party classifications are fixed:

| Consumer | Stable ID | Classification | Declaration point |
|---|---|---|---|
| Workflow continuation | `crest.workflow.humantask-continuation/v1` | required when WorkflowKey exists | HumanTask canonical creation rule |
| Descriptor Activation Review | `crest.agent-control-plane.activation-review/v1` | required | exact descriptor/version creation policy |
| Procurement decision | `crest.sample.procurement.decision/v1` | required | exact `ht_procurement_approval` descriptor/version creation policy |
| Any remaining `ILocalEventHandler<HumanTaskCompletedEvent>` | none | optional compatibility | current DI only |

Workflow, Activation Review, and Procurement handlers are removed from the
generic LocalEvent enumerable. Activation and Procurement must treat an already-applied
matching business decision as Duplicate/satisfied and a conflicting decision
as permanent conflict; Phase 9c adds no generic Inbox.

There is one execution authority only:
`IOutboxRequiredConsumer<HumanTaskCompletedEvent>` and its generic Delivery
registration. A separate HumanTask creation-policy registration contains only
exact descriptor ID/version applicability plus required consumer ID. The
policy is evaluated while creating a task and supplies the same bounded
metadata to upgrade preflight; it is never a second consumer resolver and can
never add an obligation at delivery time. Activation Review and Procurement
register their exact descriptor versions; Workflow correlation remains the
canonical creation rule.

### 2.4 Accountability trusted entry

Add public candidate-only preparation contracts to
`CrestCreates.Accountability.Abstractions`:

```text
IAuditEnvelopePreparer.PrepareAsync(candidate)
AuditEnvelopePreparationResult = Accepted(prepared) | Rejected(safe issues)
```

Extract exactly one preparation pipeline from `DefaultAuditRecorder`. Add
internal `PreparedAuditRecorder` and `AuditSinkFanOut` inside
`CrestCreates.Accountability`. The internal recorder is resolved only by the
Accountability-owned Outbox handler and `DefaultAuditRecorder`; it is not public
DI, friend API, or an Abstractions contract.

```text
DefaultAuditRecorder
    candidate -> IAuditEnvelopePreparer -> PreparedAuditRecorder

Accountability Outbox handler
    persisted prepared envelope -> validate/re-hash -> PreparedAuditRecorder
```

The second path never invokes the sanitizer. `IAuditRecorder` and `IAuditSink`
retain their existing public shapes.

### 2.5 Delivery registration without reflection

Explicit registration stores immutable resolver metadata:

```text
OutboxDeliveryHandlerRegistration
    ContractId
    Func<IServiceProvider, IOutboxDeliveryHandler>

OutboxRequiredConsumerRegistration<TPayload>
    ConsumerId
    Func<IServiceProvider, IOutboxRequiredConsumer<TPayload>>

OutboxRequiredConsumerMetadata
    ConsumerId

OutboxRequiredConsumerValidationRegistration
    ConsumerId
    Action<IServiceProvider> ValidateResolution
```

Every generic DI extension call emits one closed-generic resolver registration,
one non-generic immutable metadata entry, and one non-generic validation
delegate compiled by that generic call. The global composition catalog consumes
metadata only; its validator invokes only validation delegates in a disposable
scope. The HumanTask handler consumes an
`IOutboxRequiredConsumerResolver<HumanTaskCompletedEvent>` closed catalog. No
catalog or validation entry stores `Type`, CLR payload identity, a boxed
resolver, or a consumer instance. The closed resolver passes the already-
deserialized typed payload to the consumer without reflection, double
deserialization, or caching a scoped instance. Duplicate ConsumerId values
fail globally even when emitted by different payload types.

Startup opens a disposable scope and resolves every enabled handler/consumer
once. Runtime delivery opens a fresh scope per message. Duplicate IDs fail
composition before Claim.

### 2.6 Exact bounded defaults

Add `OutboxContractLimits` and validated `OutboxDeliveryOptions`:

| Value | Bound/default |
|---|---|
| semantic identifier | nonblank, max 256 UTF-16 chars |
| failure code / lease owner ID | nonblank, max 128 chars |
| required consumer count | 0..32 |
| payload bytes | 1..1,048,576 |
| batch size | 1..256; default 32 |
| polling interval | 10 ms..1 min; default 1 s |
| lease duration | 1 s..15 min; default 30 s |
| handler timeout | 100 ms and `< LeaseDuration`; default 20 s |
| maximum handler attempts | 1..100; default 8 |
| base retry delay | 10 ms..1 min; default 1 s |
| maximum retry delay | base..1 h; default 5 min |

Retry is deterministic exponential backoff capped at `MaximumRetryDelay`; no
jitter enters semantic tests. Optional HumanTask compatibility dispatch uses a
separate total budget no greater than `HandlerTimeout`; timeout/failure is
safely logged and swallowed.
`HumanTaskDeliveryOptions.MaximumDetachedOptionalExecutions` is bounded 1..256
with default 32.

Required consumers execute in ordinal consumer-ID order only to make attempts
deterministic. That order is not a business sequencing contract. If consumer A
accepts and consumer B returns a retryable failure, the message is retried;
consumer A must return Duplicate on the next attempt before B can accept. A
required-consumer Conflict is permanent and fails closed. Phase 9c adds no
cross-consumer transaction and makes no exactly-once claim.

### 2.7 Background-safe Procurement decision fact

Procurement authorization, tenant admission, the `procurement-manager` role,
and requester/approver separation are checked once by
`ProcurementApprovalTaskService.CompleteAsync` before HumanTask completion is
accepted. Its completion Result changes from a bare comment to a generated-JSON
`ProcurementHumanTaskDecisionFact`:

```text
RuntimeState TypeId = crest.sample.procurement.humantask-decision/v1
RequestId           Guid
ApproverId          nonblank ordinal string
Comment             exact admitted comment/reason string
```

Outcome remains the canonical HumanTask completion Outcome. Tenant identity
remains the exact `HumanTaskCompletedEvent.HumanTaskKey.TenantId`/Outbox tenant
scope and must match before application.

`ProcurementRuntimeStateContractContributor` registers that stable TypeId with
`ProcurementJsonContext.Default.ProcurementHumanTaskDecisionFact`; capture and
restore use no resolver fallback or CLR type-name identity.

The background required consumer restores this durable fact and dispatches the
existing internal Apply Approval/Rejection capability through
`ICapabilityDispatcher` with `InvocationSource.HumanTask`. Its
`configureContext` callback sets `TenantId` from the event key and `UserId`
from the persisted approver before the complete Capability Pipeline runs. It
does not call `ProcurementApplicationService` directly, mutate
`SampleExecutionIdentity`, or fabricate request ambient context.

The public decision-admission capabilities retain their `procurement.approve`
permission and the command service retains tenant, role, and separation-of-duty
checks. Only the internal `ApplyApprovalDecisionCapability` and
`ApplyRejectionDecisionCapability` descriptors remove that request-time
permission: their handlers already require `InvocationSource.HumanTask`, and
the durable fact represents an admitted decision. Validation, tenant handling,
Audit, rate limiting, idempotency, metrics, event publishing, and the existing
Apply handlers remain on the unique Capability mainline. A fresh background
scope has no ambient tenant/user; durable context must survive the pipeline
unchanged.

Exact already-applied decisions return Duplicate; the opposite or same outcome
with a changed approver/comment returns Conflict. Fresh service-provider
retries therefore preserve admitted tenant/actor semantics without re-running
request ambient authorization.

### 2.8 Async startup topology

Database-backed preflight never executes through synchronous
`IBootstrapValidator.Validate()` and never uses `GetAwaiter().GetResult()` or
another sync-over-async bridge. Reuse `BootstrapCoordinator`/`IBootstrapTask`
to freeze this required topology:

```text
runtime-schema-compatibility
    PostgreSqlRuntimeMigrationRunner.ApplyAsync/validate V012
        -> runtime-delivery-durable-composition
             -> Outbox active-requirements check
             -> HumanTask legacy-obligation check
                 -> OutboxCompositionReadiness = Ready
                     -> OutboxHostedService may Claim
```

`PostgreSqlRuntimeSchemaCompatibilityHostedService` becomes the same-instance
bridge for both its compatibility `IHostedService` and required
`IBootstrapTask` roles. `StartAsync` and `ExecuteAsync` share one thread-safe
once-only `EnsureSchemaReadyAsync`; this preserves the one migration runner and
prevents double apply. The provider registers the concrete singleton and
bridges both interfaces to that instance.

The InMemory provider registers a required no-op
`InMemoryRuntimeSchemaCompatibilityBootstrapTask` with the same TaskId, so the
topology is identical without pretending to run a migration. Exactly one
provider task may own that ID. Tighten `BootstrapCoordinator` so a duplicate
TaskId or a dependency missing from a required task fails startup instead of
being silently ignored; topology correctness never depends on DI order.

`OutboxDurableCompositionBootstrapTask` depends on the exact TaskId
`runtime-schema-compatibility` and asynchronously invokes all immutable
`IOutboxDurableCompositionCheck` contributors. Delivery owns the active-message
check; HumanTask owns the obligation-gap check. The Delivery extension ensures
one `BootstrapCoordinator` hosted registration. The worker `StartAsync` starts
its loop without blocking later hosted-service startup, but the loop awaits
`OutboxCompositionReadiness`; Claim also repeats the provider composition guard.
Registration order therefore cannot permit a pre-V012 query or Claim.

The internal contributor contract is deliberately narrow:

```csharp
internal interface IOutboxDurableCompositionCheck
{
    string CheckId { get; }
    ValueTask ValidateAsync(CancellationToken cancellationToken);
}
```

The task rejects blank/duplicate CheckIds, executes checks in ordinal CheckId
order, and opens the internal one-way readiness signal only after all return.
Checks expose no rows or provider handles and throw only the already-defined
provider-neutral composition/infrastructure failures.

Any schema, probe, or obligation failure keeps readiness closed and fails the
required bootstrap task/Host startup. InMemory executes the same composition
task asynchronously after its explicit no-op schema-ready task. Pure in-memory
shape/options checks may remain synchronous `IBootstrapValidator` work; durable
I/O may not.

### 2.9 Canonical completion outcome and exact first-party replay

`DefaultHumanTaskRuntime.CompleteAsync` resolves the requested outcome exactly
once with `CompletionOutcomeMatcher.Resolve`. It immediately converts the
matched `CompletionCondition` to its canonical ordinal name (for example every
case variant of approve becomes `Approve`) and persists that canonical value
in HumanTask state, `HumanTaskCompletedEvent`, Outbox payload, Workflow
continuation receipt, and Workflow variables. Workflow and the continuation
canonical writer never reimplement case normalization.

Required first-party consumers classify replay using the complete admitted
decision identity, not terminal status alone:

```text
same target/request + canonical decision + actor + exact admitted payload
    -> Duplicate
same terminal outcome with changed actor/payload, or opposite outcome
    -> Conflict
```

Procurement compares persisted `RequestId`, outcome, `ApproverId`, and exact
comment/reason. Activation Review extends its internal
`ActivationResourceSnapshot` with the applied CompletionEventId and a detached
closed `DescriptorActivationReviewDecision` snapshot; it compares the exact
request, decision, actor kind/ID, reason, decided time, tenant/correlation, and
evidence/envelope hashes. Exact replay is Duplicate; any changed field is
Conflict. This is consumer-specific idempotency inside the existing in-memory
authority, not a generic Inbox or a durability upgrade.

### 2.10 Non-cooperative optional compatibility handlers

Each delivery attempt computes one monotonic
`AttemptDeadline = invocation start + HandlerTimeout`. Contract validation and
required consumers consume that same deadline. The optional lane receives only:

```text
min(HumanTask optional compatibility budget, remaining AttemptDeadline)
```

It never receives a fresh full timeout after required consumers finish. If no
time remains, optional dispatch is skipped/logged and reliable Ack proceeds.

The optional HumanTask compatibility lane runs in a separate child scope under
a bounded execution tracker. Timeout stops awaiting the optional task and
allows reliable Ack; cancellation cooperation is not assumed. A timed-out
task retains its child scope until eventual completion, with fault observation
and deferred disposal. The tracker caps detached executions at 32; when full,
later optional dispatch is safely skipped/logged rather than delaying Ack.
Required consumers never use this lane or tracker.

---

## 3. Ordered Delivery Map

| Slice | Deliverable | Primary cases |
|---|---|---|
| 1 | projects, contracts, inactive manifests, architecture guards | ARCH01-ARCH16 |
| 2 | message factory/hash + InMemory transactional append | A01-A12 |
| 3 | InMemory claim/fence/retry/terminal/dispatcher semantics | L01-L17, R03-R05, R07-R13 |
| 4 | V012, continuation table, PostgreSQL stores, shared parity | C06, C08-C15, N01-N02, SCHEMA01-SCHEMA02 |
| 5 | neutral transaction enlistment + HumanTask ambiguity observation | A05-A08, H09-H12, H17-H18 |
| 6 | ordered async composition, worker, restart, process crash | R01-R13, C01-C03, C07-C14, BOOT01-BOOT03, CW01-CW06 + CW04B |
| 6.5 | Accountability preparation/handler + Workflow producer foundation | W08-W12 foundation only |
| 7 | HumanTask completion, required consumers, and reliable resume cutover | H01-H23, MRC01-MRC04, PROC01-PROC07, ACT01-ACT02, OUT01-OUT02, OPT01-OPT02, HOC01, RCA01-RCA02, CW07 |
| 8 | Workflow prepared Accountability cutover | W01-W14, C02-C05 |
| 9 | PostgreSQL CrashWorker and NativeAOT executable | N01-N09, CW01-CW07 + CW04B |
| 10 | full regression, evidence review, memory closure | INV-01-INV-30, exits 1-35 |

---

## 4. Final Project and File Layout

### 4.1 New Delivery contracts project

```text
src/Runtime/Eventing/CrestCreates.Runtime.Delivery.Abstractions/
  CrestCreates.Runtime.Delivery.Abstractions.csproj
  Properties/AssemblyInfo.cs
  Contracts/OutboxContractLimits.cs
  Messages/OutboxMessage.cs
  Messages/OutboxMessageMetadata.cs
  Messages/IOutboxMessageFactory.cs
  Messages/OutboxMessageConflictException.cs
  Stores/ITransactionalOutboxWriter.cs
  Stores/IOutboxDispatchStore.cs
  Stores/OutboxAppendResult.cs
  Stores/OutboxClaimRequest.cs
  Stores/OutboxDeliveryClaim.cs
  Stores/OutboxDeliveryLease.cs
  Stores/OutboxDeliveryFailure.cs
  Stores/OutboxDeliveryMutationResult.cs
  Stores/OutboxDeliveryStatus.cs
  Composition/ActiveOutboxRequirements.cs
  Composition/IOutboxCompositionProbe.cs
  Composition/IOutboxDurableCompositionCheck.cs
  Composition/OutboxCompositionException.cs
  Handlers/IOutboxDeliveryHandler.cs
  Handlers/IOutboxRequiredConsumer.cs
  Handlers/OutboxDeliveryContext.cs
  Handlers/OutboxDeliveryOutcome.cs
  Handlers/OutboxRequiredConsumerResult.cs
  Registration/OutboxDeliveryHandlerRegistration.cs
  Registration/OutboxRequiredConsumerRegistration.cs
  Registration/OutboxRequiredConsumerMetadata.cs
  Registration/OutboxRequiredConsumerValidationRegistration.cs
  Registration/IOutboxRequiredConsumerResolver.cs
  Registration/OutboxRegistrationServiceCollectionExtensions.cs
```

Public contracts expose no provider type. `IOutboxCompositionProbe` and
`ActiveOutboxRequirements` are internal friend contracts.

### 4.2 New Delivery runtime project

```text
src/Runtime/Eventing/CrestCreates.Runtime.Delivery/
  CrestCreates.Runtime.Delivery.csproj
  Bootstrap/RuntimeDeliveryServiceCollectionExtensions.cs
  Bootstrap/OutboxCompositionValidator.cs
  Bootstrap/OutboxDurableCompositionBootstrapTask.cs
  Bootstrap/OutboxCompositionReadiness.cs
  Bootstrap/OutboxActiveRequirementsCompositionCheck.cs
  Bootstrap/OutboxDeliveryOptionsValidator.cs
  CanonicalHashing/OutboxCanonicalProjectionWriter.cs
  Messages/DefaultOutboxMessageFactory.cs
  Registration/OutboxDeliveryHandlerRegistry.cs
  Registration/OutboxRequiredConsumerRegistry.cs
  Dispatch/OutboxDispatcher.cs
  Dispatch/OutboxHostedService.cs
  Dispatch/OutboxFailureClassifier.cs
  Dispatch/OutboxRetryPolicy.cs
  Dispatch/OutboxDeliveryOptions.cs
  Dispatch/OutboxCompositionState.cs
```

`OutboxDispatcher.DispatchBatchAsync` is the bounded testable core. The hosted
service only polls, delegates, and applies cancellation/lifecycle rules.

### 4.3 Existing Runtime projects modified

```text
src/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory/
  Bootstrap/InMemoryRuntimeSchemaCompatibilityBootstrapTask.cs new
  Kernel/InMemoryRuntimePersistenceState.cs
  Transactions/InMemoryRuntimeTransactionCoordinator.cs
  InMemoryRuntimePersistenceServiceCollectionExtensions.cs
  Stores/InMemoryTransactionalOutboxWriter.cs                 new
  Stores/InMemoryOutboxDispatchStore.cs                       new
  Stores/InMemoryWorkflowContinuationAcceptanceStore.cs      new
  Stores/InMemoryHumanTaskCompletionObligationPreflight.cs   new

src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/
  Properties/AssemblyInfo.cs
  HumanTaskCreationRequest.cs
  HumanTaskInstance.cs
  Delivery/HumanTaskCompletionConsumerIds.cs                 new
  Delivery/HumanTaskCompletionObligationPolicyRegistration.cs new
  Delivery/HumanTaskCompletionObligationRequirement.cs       new
  Delivery/HumanTaskCompletionObligationPreflightResult.cs   new
  Delivery/IHumanTaskCompletionObligationPreflight.cs        new internal

src/Runtime/HumanTask/CrestCreates.HumanTask/
  DefaultHumanTaskRuntime.cs
  HumanTaskServiceCollectionExtensions.cs
  Delivery/HumanTaskCompletionObligationCompositionCheck.cs   new
  Delivery/HumanTaskCompletedOutboxHandler.cs                new
  Delivery/HumanTaskCompletionCompatibilityDispatcher.cs     new
  Delivery/OptionalCompatibilityExecutionTracker.cs          new
  Delivery/HumanTaskDeliveryJsonSerializerContext.cs          new
  Delivery/HumanTaskDeliveryOptions.cs                        new

src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/
  Delivery/WorkflowContinuationAcceptance.cs                 new
  Delivery/WorkflowContinuationAcceptanceWriteResult.cs      new
  Delivery/IWorkflowContinuationAcceptanceStore.cs           new
  Delivery/WorkflowContinuationAcceptanceCanonicalWriter.cs  new internal

src/Runtime/Workflow/CrestCreates.Workflow/
  WorkflowEngine.cs
  WorkflowExecutionRunner.cs
  WorkflowSuspensionCommitter.cs
  WorkflowContinuationService.cs
  WorkflowServiceCollectionExtensions.cs
  WorkflowLifecycleEventFactory.cs
  Accountability/WorkflowAccountabilityEnvelopeFactory.cs    new
  Accountability/WorkflowAccountabilityOutboxProducer.cs      new
  Delivery/WorkflowHumanTaskContinuationConsumer.cs           new
  HumanTaskCompletedWorkflowSubscriber.cs                     retire to recycle bin
  Accountability/WorkflowAccountabilityObserver.cs            retire to recycle bin

src/Runtime/Audit/CrestCreates.Accountability.Abstractions/
  Preparation/IAuditEnvelopePreparer.cs                       new
  Preparation/AuditEnvelopePreparationResult.cs               new

src/Runtime/Audit/CrestCreates.Accountability/
  Bootstrap/AccountabilityServiceCollectionExtensions.cs
  Bootstrap/AccountabilityCompositionValidator.cs
  Recording/DefaultAuditRecorder.cs
  Preparation/DefaultAuditEnvelopePreparer.cs                 new
  Recording/AuditSinkFanOut.cs                                new
  Recording/PreparedAuditRecorder.cs                          new
  Delivery/AccountabilityOutboxHandler.cs                     new

src/Metadata/CrestCreates.Metadata/
  Bootstrap/BootstrapCoordinator.cs
```

Retired source files move under `99_RecycleBin/phase-9c/` in the implementation
Slice; they are never directly deleted.

### 4.4 PostgreSQL provider files

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  CrestCreates.Runtime.Persistence.PostgreSql.csproj
  PostgreSqlRuntimeMigrationRunner.cs
  PostgreSqlRuntimeSchemaCompatibilityHostedService.cs
  PostgreSqlRuntimeJsonSerializerContext.cs
  PostgreSqlRuntimePersistenceServiceCollectionExtensions.cs
  PostgreSqlHumanTaskInstanceStore.cs
  PostgreSqlTransactionalOutboxWriter.cs                     new
  PostgreSqlOutboxDispatchStore.cs                           new
  PostgreSqlWorkflowContinuationAcceptanceStore.cs           new
  PostgreSqlHumanTaskCompletionObligationPreflight.cs        new
  PostgreSqlOutboxRowCodec.cs                                new
```

The provider references Delivery.Abstractions only. It does not reference the
Delivery runtime or concrete HumanTask/Workflow implementations.

### 4.5 First-party consumer migrations

```text
src/Runtime/Agent/CrestCreates.Agent.ControlPlane/
  AgentControlPlaneServiceCollectionExtensions.cs
  ActivationResourceSnapshot.cs
  Activation/DefaultActivationReviewOrchestrator.cs
  Activation/DescriptorActivationReviewHumanTaskEventHandler.cs

samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Contracts/
  ProcurementContractIds.cs
  ProcurementHumanTaskDecisionFact.cs                        new
  Json/ProcurementJsonContext.cs

samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/
  Program.cs
  ProcurementDescriptorCatalog.cs
  ProcurementHumanTaskIntegration.cs
  ProcurementRuntimeStateContractContributor.cs              new
```

Rename implementations to `...CompletionConsumer` only where it improves
clarity; stable semantic IDs, not CLR names, are persisted.

### 4.6 Test and solution files

```text
tests/Shared/CrestCreates.Runtime.Delivery.Testing/
  CrestCreates.Runtime.Delivery.Testing.csproj
  TestingBoundaryMarker.cs
  Contracts/IOutboxContractDriver.cs
  Assertions/OutboxContractAssertions.cs
  Fixtures/OutboxContractData.cs
  Cases/OutboxAppendContractCases.cs
  Cases/OutboxAtomicityContractCases.cs
  Cases/OutboxDispatchContractCases.cs
  Cases/OutboxFencingContractCases.cs
  Cases/OutboxAttemptBudgetContractCases.cs
  Cases/OutboxInitialTimeContractCases.cs
  Cases/OutboxCompositionContractCases.cs
  Cases/OutboxTerminalReplayContractCases.cs
  Manifest/Phase9cCaseManifest.cs
  Manifest/Phase9cAcceptanceManifest.cs
  Manifest/Phase9cSupplementalAcceptanceManifest.cs
  Evidence/Phase9cEvidenceLedger.cs

tests/Shared/CrestCreates.Runtime.Persistence.Testing/
  Contracts/IRuntimePersistenceContractDriver.cs
  Cases/WorkflowContinuationAcceptanceContractCases.cs       new

tests/Runtime/Eventing/CrestCreates.Runtime.Delivery.Tests/  new project
tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests/
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests/
tests/Runtime/Workflow/CrestCreates.Workflow.Tests/
tests/Runtime/Audit/CrestCreates.Accountability.Tests/
tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/
tests/Boundary/CrestCreates.DependencyBoundaries.Tests/
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests/
samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.Tests/Acceptance/
```

Add the four new projects to `CrestCreates.slnx`,
`solutions/CrestCreates.All.slnx`, and the relevant Runtime/Persistence solution
views. The shared kit has `IsTestProject=false`, no xUnit, no FluentAssertions,
no Npgsql, and no concrete provider reference.

---

## 5. Contract and Provider Mechanics

### 5.1 Immutable message factory

`DefaultOutboxMessageFactory.Create<TPayload>` validates metadata and the exact
generated `JsonTypeInfo<TPayload>`, serializes once to owned UTF-8 bytes, sorts
and snapshots required IDs, and computes the `RuntimeOutboxMessage` Integrity
projection. `OutboxMessage` copies payload bytes on construction and on any
public memory exposure; caller mutation cannot alter the accepted fact.

The projection order is exactly the Spec order. Delivery fields never enter the
hash. `OutboxMessageConflictException` is thrown by canonical writers and is
not representable as an ignorable append result.

The internal pure `WorkflowContinuationAcceptanceCanonicalWriter` lives in
`CrestCreates.Workflow.Abstractions`, not Workflow implementation. Existing
friend access lets Workflow Runtime, InMemory, and PostgreSQL use this single
integrity authority without reversing provider dependencies. It requires the
already-canonical HumanTask Outcome and writes the exact eight-field
`runtime-workflow-continuation-acceptance-v1` projection in §2.2.
It writes a Result null marker or exact ordinal `RuntimeStateValue` identity and
never canonicalizes JSON semantically. Both receipt Stores use this writer for
Accepted/Duplicate/Conflict decisions.

### 5.2 Writer versus dispatch lifecycle

`ITransactionalOutboxWriter.AppendAsync`:

- requires the provider's current ambient Runtime transaction;
- appends Accepted or proves exact Duplicate;
- throws conflict before the producer delegate completes;
- cannot Claim or mutate delivery state.

`IOutboxDispatchStore`:

- owns its own short provider transaction per bounded dispatch operation;
- cannot enlist in a producer transaction;
- claims only supported active facts;
- uses fenced Ack/Retry/DeadLetter outcomes.

The InMemory coordinator adds a provider-internal dispatch critical section
over the same committed state gate. It does not expose another public
transaction abstraction.

### 5.3 Terminal replay evidence

V012 persists terminal fence proof separately from active lease fields:

```text
terminal_lease_owner_id
terminal_fencing_token
terminal_failure_code
```

Ack/DeadLetter copy the final active lease identity into these fields while
clearing `lease_owner_id` and `lease_expires_at`. Exact replay returns
`AlreadyApplied`; changed owner/token/failure or cross-terminal replay returns
`StaleFence`/`TerminalConflict`. Retry clears active lease fields and creates no
terminal proof.

### 5.4 PostgreSQL claim transaction

One PostgreSQL transaction performs:

1. active Pending/Leased requirement expansion;
2. exact subset validation against supported ContractId/consumer arrays;
3. `OutboxCompositionException` before any update when a visible unsupported
   requirement exists;
4. supported eligible row selection ordered by
   `available_at, occurred_at, message_id collate "C"`;
5. `FOR UPDATE SKIP LOCKED`;
6. lease/fence/AttemptCount update and detached row return.

The selection predicate repeats supported-ID filtering so a concurrent newly
committed unsupported fact cannot be leased between the guard and selection.
It remains unchanged and is rejected on the next active-requirement guard.

PostgreSQL uses `clock_timestamp()` for eligibility, lease expiry, failure, and
terminal timestamps. InMemory uses its injected `TimeProvider`.

### 5.5 Composition lifecycle

`OutboxCompositionValidator` remains synchronous and performs only pure
registration/options checks through `IBootstrapValidator`:

- validates duplicate/blank registrations and options;
- resolves every non-generic contract handler in a disposable scope;
- validates exact one-to-one ConsumerId agreement among global metadata,
  closed resolver registrations, and non-generic validation entries;
- invokes each compile-time validation delegate in that scope to prove its
  scoped typed consumer factory resolves, without accessing the resolver or
  knowing its payload type.

`OutboxDurableCompositionBootstrapTask` performs the asynchronous provider
checks after schema readiness, records only safe counts/codes in
`OutboxCompositionState`, and opens `OutboxCompositionReadiness` only on full
success. Neither validator blocks on a Task.

`ClaimAsync` repeats the guard and throws the exact public
`OutboxCompositionException`. Provider/network/database failures remain
`RuntimePersistenceUnavailableException` or another provider-neutral
infrastructure exception and never become composition failure.

Missing required Accountability sink is checked before the worker starts.
`NoSinkConfigured` observed after successful startup is an invariant violation:
the worker stops without Ack/Retry/DeadLetter mutation.

### 5.6 HumanTask obligation upgrade preflight

Freeze this narrow internal HumanTask abstraction:

```csharp
internal interface IHumanTaskCompletionObligationPreflight
{
    ValueTask<HumanTaskCompletionObligationPreflightResult> ValidateAsync(
        IReadOnlyList<HumanTaskCompletionObligationRequirement> requirements,
        CancellationToken cancellationToken);
}
```

Each requirement contains only an exact descriptor ID/version applicability
key and one stable required consumer ID. The result contains a provider-neutral
safe code, total matching-gap count, and a bounded sample of tenant-scoped
HumanTask identities; it never returns entities or an enumerable query surface.
The sample bound is `min(10, gap count)` and identities are ordinally ordered.

`HumanTaskCompletionObligationCompositionCheck` derives requirements from the
same immutable creation-policy registrations used by `PrepareAsync`, implements
the async durable-composition contributor, and calls the provider
implementation only from the ordered bootstrap task. InMemory evaluates its
committed snapshot under the provider state gate. PostgreSQL performs one
server-side active-row query: it matches descriptor ID/version from
`human_task_pin_json`, tests the authority JSON array for the required ID, and
returns only `count(*)` plus the first ten ordinal tenant/key identities. It
does not deserialize or enumerate task rows into Runtime. Both implementations
are registered by their existing provider DI extensions; HumanTask owns the
validator, and the provider never references Activation Review or Procurement.
`HumanTaskServiceCollectionExtensions` registers the policy catalog and async
composition check; `InMemoryRuntimePersistenceServiceCollectionExtensions` and
`PostgreSqlRuntimePersistenceServiceCollectionExtensions` each register their
single internal `IHumanTaskCompletionObligationPreflight` implementation. A
missing implementation fails bootstrap with a safe HumanTask composition code.

---

## 6. V012 Exact Schema and Upgrade Rules

### 6.1 Catalog entry

Append only:

```text
V012_transactional_outbox
```

to `PostgreSqlRuntimeMigrationRunner.Catalog`, then add every new/altered table,
column, collation, check, index, unique key, and FK to `RuntimeSchemaManifest`.
Migration checksum and exact manifest drift tests are mandatory.

### 6.2 `runtime_outbox_messages`

```text
message_id                    text collate C primary key
contract_id                   text collate C not null
event_name                    text collate C not null
event_version                 integer not null
tenant_scope_kind             text collate C not null
tenant_id                     text collate C not null
correlation_id                text collate C null
causation_id                  text collate C null
occurred_at                   timestamptz not null
required_consumer_ids_json    jsonb not null
payload_utf8                  bytea not null
integrity_json                jsonb not null
created_at                    timestamptz not null
status                        integer not null
attempt_count                 integer not null
available_at                  timestamptz not null
lease_owner_id                text collate C null
fencing_token                 bigint not null
lease_expires_at              timestamptz null
last_failure_code             text collate C null
last_failure_at               timestamptz null
terminal_lease_owner_id       text collate C null
terminal_fencing_token        bigint null
terminal_failure_code         text collate C null
delivered_at                  timestamptz null
dead_lettered_at              timestamptz null
updated_at                    timestamptz not null
```

Status values are `0 Pending`, `1 Leased`, `2 Delivered`, `3 DeadLettered`.
Checks close tenant scope, event version, JSON array root, nonnegative counters,
and every active/terminal column shape. No FK links Outbox to domain tables.

Create one partial claim index over active states with keys:

```text
(status, available_at, lease_expires_at, occurred_at, message_id collate C)
where status in (0, 1)
```

### 6.3 `runtime_workflow_continuation_acceptances`

```text
completion_event_id           text collate C primary key
tenant_scope_kind             text collate C not null
tenant_id                     text collate C not null
human_task_instance_id        text collate C not null
workflow_instance_id          text collate C not null
workflow_from_revision        bigint not null
workflow_to_revision          bigint not null
integrity_json                jsonb not null
receipt_json                  jsonb not null
accepted_at                   timestamptz not null
```

Add:

- check `workflow_to_revision = workflow_from_revision + 1`;
- tenant-scope check;
- unique key on `(tenant_scope_kind, tenant_id, human_task_instance_id)`;
- deferrable initially-deferred RESTRICT FKs to Workflow and the reciprocal
  Workflow/HumanTask key, matching the existing Runtime kernel.

The primary key catches same CompletionEventId/different identity; the unique
HumanTask key catches different CompletionEventId/same waiting task.
`receipt_json` contains the complete closed
`WorkflowContinuationAcceptance`, including the producer-canonical Outcome and
exact RuntimeStateValue Result identity. `integrity_json` contains the complete
structured `RuntimeWorkflowContinuationAcceptance` hash from §2.2. The row
codec re-hashes on read and treats receipt/hash disagreement as a persisted
invariant violation; `accepted_at` never participates in equality.

### 6.4 HumanTask obligation authority column

Alter `runtime_human_task_instances`:

```text
required_consumer_ids_json jsonb not null
```

V012 performs the safe staged migration in this exact order:

```text
ADD column with temporary default '[]'::jsonb
backfill every workflow-correlated row with crest.workflow.humantask-continuation/v1
validate array-root/element invariants
add and validate:
    workflow_instance_id is null
    OR required_consumer_ids_json @> '["crest.workflow.humantask-continuation/v1"]'
DROP DEFAULT
```

The final manifest requires no column default and the Workflow-correlation
CHECK. The PostgreSQL Store always writes the authority column explicitly and
fails before SQL when a correlated candidate lacks the continuation ID. The
InMemory Store enforces the same candidate invariant. The migration never
creates an Outbox message for an already completed task.

Before V012 mutation, the migration runner performs the existing-scope direct
preflight for any `CompletionDispatchFailed` status row and fails with one safe
deterministic code. It does not add a status enumeration method to
`IHumanTaskInstanceStore`.

After migration, a narrow HumanTask composition preflight compares active
`Created`/`Assigned` task pins and obligations with every applicable creation-
policy requirement, regardless of whether `workflow_instance_id` is null.
Request history is not reconstructed. Any active matched task whose authority
column lacks a required business consumer ID blocks startup. Operators must
explicitly reconcile the row after proving completion has not occurred; the
runtime never silently treats it as optional. Workflow correlation is
orthogonal: V012 backfills the continuation ID, while this provider-side
preflight detects every missing registered business-consumer ID.

`HumanTaskInstance.RequiredCompletionConsumerIds` is `[JsonIgnore]` for
`PostgreSqlRuntimeJsonSerializerContext`; only
`required_consumer_ids_json` is authoritative. A closed provider materializer
restores it after deserializing `state_json`, and round-trip/drift tests fail if
the property appears in `state_json` or the two sources can disagree.

### 6.5 JSON roots

Extend source-generated contexts only:

```text
PostgreSqlRuntimeJsonSerializerContext
    WorkflowContinuationAcceptance
    ImmutableArray<string> or closed persistence DTO

HumanTaskDeliveryJsonSerializerContext
    HumanTaskCompletedEvent

AccountabilityJsonSerializerContext
    existing AuditEnvelope root reused

ProcurementJsonContext
    ProcurementHumanTaskDecisionFact
```

No resolver fallback is allowed in production or fixtures.

---

## 7. Golden Mainline Cutovers

### 7.1 HumanTask completion transaction

Refactor `DefaultHumanTaskRuntime.CompleteAsync`:

1. load and validate the exact tenant-scoped task;
2. reject the legacy failure state on the new mainline;
3. resolve once and replace the request case variant with the canonical
   `CompletionCondition` name;
4. allocate `CompletionEventId` before opening the transaction;
5. build final Completed candidate and typed event with that canonical Outcome;
6. create the Outbox message with the frozen instance obligations;
7. execute `UpdateAsync + AppendAsync` in
   `IRuntimeTransactionCoordinator.ExecuteAsync`;
8. return only after known commit;
9. on commit unknown, surface the existing exception and require fresh
   observation/intent comparison.

Remove synchronous `ILocalEventBus.PublishAsync`, failure-policy resolution,
and dispatch-failure state writes from this path. `CancelAsync` treats Completed
as terminal and has no delivery-failure branch.

`HumanTaskCompletedOutboxHandler` validates and deserializes the fact, executes
every persisted required consumer in ordinal order through the one generic
`IOutboxRequiredConsumer<HumanTaskCompletedEvent>` registry, then invokes the
generic typed LocalEvent dispatcher under the compatibility budget. It never
consults HumanTask creation-policy metadata while delivering. Only the
contract handler and required consumer Accepted/Duplicate results determine
Delivered.

If a later consumer fails transiently after an earlier one accepted, the whole
message retries and the earlier consumer must prove Duplicate. Ordinal order
provides deterministic attempts only; consumers cannot depend on one another's
order. Any Conflict is permanent/fail-closed and prevents Ack.

The Procurement consumer restores `ProcurementHumanTaskDecisionFact`, verifies
its event/task tenant identity, and dispatches the existing internal Apply
capability through `ICapabilityDispatcher` with explicit persisted tenant and
approver context. Request-scoped identity services are not part of this
delivery graph or authorization decision.

### 7.2 Workflow continuation acceptance

Refactor the internal continuation core into:

```text
AcceptReliableAsync(request)
    -> receipt lookup by CompletionEventId
    -> receipt lookup by HumanTaskKey for different-ID conflict
    -> load exact waiting Workflow
    -> construct resumed candidate and one lifecycle event
    -> prepare AuditEnvelope before transaction
    -> construct receipt with the already-canonical Outcome/exact Result identity
    -> transaction: receipt + Workflow CAS + Audit Outbox
    -> Accepted

ContinueAsync compatibility wrapper
    -> AcceptReliableAsync
    -> best-effort lifecycle notification
    -> attempt RunAsync
```

The required consumer calls the reliable core. After Accepted/Duplicate it may
attempt `RunAsync`, but catches/logs its failure and returns the durable
acceptance result. Commit unknown is reconciled by a fresh receipt observation;
exact receipt is Duplicate, absent receipt remains retryable, mismatch is
permanent Conflict.

Receipt equality means exact
`runtime-workflow-continuation-acceptance-v1` Integrity from §2.2. A repeated
CompletionEventId with changed Outcome, Result, keys, or transition revisions
is Conflict even when the old waiting correlation is already cleared.

### 7.3 Workflow Accountability transition owners

`WorkflowAccountabilityEnvelopeFactory` contains the pure mapping currently in
`WorkflowAccountabilityObserver`. `WorkflowAccountabilityOutboxProducer`
prepares the candidate and builds the message.

At each write owner:

```text
WorkflowEngine                workflow.started
WorkflowSuspensionCommitter   workflow.suspended
WorkflowContinuationService   workflow.resumed
WorkflowExecutionRunner       workflow.completed / workflow.failed
```

allocate the lifecycle identity/time once, prepare before the transaction, and
commit state plus Outbox in the existing coordinator. Publish the same
`WorkflowLifecycleEvent` only after known commit to remaining best-effort
observers. Commit unknown publishes nothing synchronously.

During the Slice 7-to-8 transition, `workflow.resumed` is already Outbox-owned:
the legacy `WorkflowAccountabilityObserver` must explicitly ignore that one
event in the same Slice 7 commit, while unrelated best-effort observers still
receive it. The observer continues to own only the other four Accountability
transitions until Slice 8 replaces them and removes the registration/source.
There is never a synchronous plus Outbox dual Accountability write for resume.

Remove `WorkflowAccountabilityObserver` registration and source. Workflow never
resolves `IAuditSink` or the internal prepared recorder.

### 7.4 Accountability delivery classification

`AccountabilityOutboxHandler` verifies both Outbox Integrity and prepared
envelope Integrity, then calls the internal prepared recorder.

```text
all configured sinks Accepted/Duplicate -> Delivered
partial provider failure                -> RetryableFailure
sink Conflict                           -> PermanentFailure
prepared validation rejection           -> PermanentFailure
internal/provider failure               -> RetryableFailure
NoSinkConfigured                         -> composition stop; no mutation
```

Sink membership is read from the scoped recorder on every attempt. Adding or
removing a sink affects the next attempt without changing the persisted
envelope. FullDurable evidence explicitly uses `PostgreSqlAuditSink` and proves
restart Duplicate.

---

## 8. Test Manifest and Evidence Ownership

### 8.1 Manifest rules

`Phase9cAcceptanceManifest` contains all 145 exact names from Spec §17.
`Phase9cCaseManifest` contains A01-A12, L01-L17, R01-R13, H01-H23, W01-W14,
C01-C15, N01-N09, ARCH01-ARCH16, exactly CW01, CW02, CW03, CW04, CW04B,
CW05, CW06, CW07, MRC01-MRC04, PROC01-PROC07, RCA01-RCA02, BOOT01-BOOT03,
SCHEMA01-SCHEMA02, OPT01-OPT02, HOC01, ACT01-ACT02, and OUT01-OUT02. The separate
supplemental acceptance manifest contains 25 Plan-only names; it does not
change or masquerade as the frozen 145-name Spec set.

Each Case entry records:

```text
CaseId
Slice
RequiredRunner = Shared | InMemory | PostgreSql | Domain | Boundary |
                 CrashWorker | Aot
EvidenceVector = semantic | sql-concurrency | restart | process-crash |
                 composition | native
```

Each normative or supplemental acceptance entry records its exact name and one
or more Case IDs. This permits multiple crash windows to prove one frozen
acceptance without duplicating the acceptance name.

The ledger fails for missing names, duplicate names, unknown Case IDs, active
entries without an owning runner, or a claimed Slice with no recorded evidence.
Future Slice entries are inactive data, not skipped tests.

### 8.2 Case-to-runner mapping

| Cases | Acceptance ownership | Required runners |
|---|---|---|
| ARCH01-ARCH16 | Spec 17.1 contract-only names | Delivery tests + Boundary tests |
| A01-A04, A09-A12 | Spec 17.2 append/replay/identity names | Shared + InMemory + PostgreSQL |
| A05-A08 | Spec 17.2 atomicity/commit-unknown names | Shared + both providers + PostgreSQL integration |
| L01-L12 | Spec 17.3 basic claim/fence/terminal names | Shared + both providers |
| L13-L17 | Spec 17.3 composition/terminal replay names | Shared + both providers + PostgreSQL integration |
| R01-R13 | Spec 17.4 and attempt-budget names | Shared + PostgreSQL restart/CrashWorker where required |
| H01-H18 | Spec 17.5 through concurrent-winner cases | HumanTask/Workflow domain + provider atomicity |
| H19-H23 | Spec 17.5 discriminator/optional/required consumer names | Workflow shared acceptance + HumanTask + CrashWorker |
| W01-W12 | Spec 17.6 transition/preparation/sink names | Workflow + Accountability + PostgreSQL |
| W13-W14 | configured-sink and FullDurable names | Accountability composition + PostgreSqlAuditSink restart |
| C01-C15 | Spec 17.1/17.3/17.6/17.7 composition and migration names | Delivery + provider + migration |
| N01-N09 | Spec 17.7 migration/native names | PostgreSQL + native AOT Host/Fixture |
| CW01-CW07 + CW04B | exact process crash windows in §8.4 | CrashWorker parent/child + PostgreSQL |
| MRC01-MRC04 | Plan-only multi-required-consumer retry/ordering/conflict | Delivery + HumanTask + Procurement/Workflow |
| PROC01-PROC02 | Plan-only background identity/tenant preservation | Procurement acceptance + fresh DI scope |
| RCA01-RCA02 | Plan-only continuation Integrity profile/placement/conflict | Workflow shared acceptance + both providers + Boundary |
| BOOT01-BOOT03 | ordered async schema/composition readiness | Metadata bootstrap + Delivery + PostgreSQL |
| SCHEMA01-SCHEMA02 | final HumanTask obligation-column fail-closed schema | PostgreSQL migration/store |
| OPT01-OPT02 | non-cooperative timeout and remaining attempt budget | HumanTask + Delivery |
| HOC01 | creation-time request obligation composition | HumanTask + Delivery |
| PROC03-PROC07 | Capability mainline and exact Procurement replay | Procurement acceptance + Capability |
| ACT01-ACT02 | exact Activation Review replay | Agent Control Plane |
| OUT01-OUT02 | producer-owned canonical Outcome | HumanTask + Workflow |

The exact acceptance names remain single-source in the frozen Spec and are
copied byte-for-byte into the manifest. Test methods may wrap one manifest name
but cannot rename it.

### 8.3 Shared driver shape

`IOutboxContractDriver` exposes only provider-neutral test operations:

```text
ExecuteProducerAsync
AppendAsync
ObserveMessageAsync
ClaimAsync
AckAsync / RetryAsync / DeadLetterAsync
ObserveActiveRequirementsAsync
AdvanceProviderClockAsync when supported
RecreateProviderAsync when supported
```

It never exposes a transaction handle, SQL, provider exception, mutable store
row, or product replay API.

Extend `IRuntimePersistenceContractDriver` with continuation acceptance setup
and observation methods. Both provider wrappers invoke
`WorkflowContinuationAcceptanceContractCases` for Accepted, exact Duplicate,
different-ID Conflict, and response-loss reconciliation.

### 8.4 CrashWorker windows

Extend the existing CrashWorker command table:

```text
outbox-cw01-before-producer-commit
outbox-cw02-producer-commit-no-response
outbox-cw03-commit-before-claim
outbox-cw04-claim-before-handler
outbox-cw04b-repeat-claim-crash
outbox-cw05-handler-before-ack
outbox-cw06-retry-committed
outbox-cw07-resume-accepted-before-consumer-return
```

The CaseManifest maps every command, including windows that share a normative
acceptance:

| Case | Durable boundary and acceptance evidence |
|---|---|
| CW01 | before producer commit; `Crash_BeforeProducerCommit_Should_ExposeNeitherStateNorOutbox` |
| CW02 | producer commit/no response; `Crash_AfterProducerCommit_Should_RecoverPendingMessage` |
| CW03 | committed Pending before Claim; `Pending_Message_Should_Be_Recovered_After_Restart` |
| CW04 | claimed before handler; `Crash_AfterClaim_Should_RecoverExpiredLease` |
| CW04B | repeated claim/crash generations; `Repeated_ClaimCrash_Should_Consume_AttemptBudget` |
| CW05 | required handler accepted before Ack; `Crash_AfterHandlerBeforeAck_Should_PermitSameMessageRedelivery` |
| CW06 | Retry mutation committed before process exit; `RetryDue_Message_Should_Be_Recovered_After_Restart` |
| CW07 | receipt + resume + Audit Outbox committed before consumer return; `Crash_After_ResumeCommit_Before_ConsumerReturn_Should_Reconcile_SameCompletion` |

The child prints one exact sentinel only after reaching the named durable or
pre-durable boundary, flushes stdout, and waits. The parent kills the entire
process tree, waits for the PostgreSQL backend to disappear, creates a fresh
provider, and observes durable state. No public production test hook is added.

### 8.5 Native evidence

Extend the existing AOT Host/Fixture. Do not create another native project.
The native executable must:

1. apply and validate V012;
2. create/suspend a Workflow with HumanTask;
3. complete HumanTask and persist Outbox atomically;
4. rebuild provider/dispatcher composition;
5. claim typed HumanTask payload;
6. execute required continuation and persist exact acceptance receipt;
7. fail an optional LocalEvent handler and still Ack;
8. claim the resulting prepared Workflow AuditEnvelope;
9. deliver through Accountability internal prepared recording;
10. use `PostgreSqlAuditSink`, restart, and observe Duplicate;
11. prove exact terminal AlreadyApplied and stale fence behavior;
12. emit:

```text
CRESTCREATES_RUNTIME_OUTBOX_OK
CRESTCREATES_HUMANTASK_RELIABLE_DELIVERY_OK
CRESTCREATES_WORKFLOW_ACCOUNTABILITY_DELIVERY_OK
```

The fixture publishes linux-x64 with `CrestCreatesPublishMode=aot`, completes
native link, executes the original native artifact, and asserts exit code,
markers, and absence of IL2026/IL3050 warnings.

### 8.6 Complete acceptance ledger

Runner codes:

```text
DEL Delivery unit/architecture    SH shared provider kit
IM  InMemory wrapper              PG PostgreSQL integration/migration
HT  HumanTask domain              WF Workflow domain
AUD Accountability domain        ACT Agent Control Plane
PROC Procurement acceptance      BND dependency boundaries
CR  CrashWorker parent/child      AOT native Host/Fixture
```

Every Spec §17 name appears exactly once in this ledger. Multiple acceptance
names may intentionally map to one semantic Case because they prove different
facets of the same setup.

#### Contracts and architecture

| Case | Exact acceptance name | Runner |
|---|---|---|
| ARCH01 | `OutboxContracts_Should_Not_ExposeProviderTypes` | DEL+BND |
| ARCH02 | `RuntimeDeliveryAbstractions_Should_Not_ReferenceDomainOrProviderImplementations` | BND |
| ARCH03 | `RuntimeDeliveryRuntime_Should_Not_ReferenceHumanTaskWorkflowOrAccountability` | BND |
| ARCH04 | `ProducerModules_Should_Not_ReferenceOutboxDispatchStore` | BND |
| A02/ARCH05 | `TransactionalOutboxWriter_Should_FailWithoutAmbientRuntimeTransaction` | SH+IM+PG |
| ARCH06/N06 | `OutboxMainline_Should_Not_UseRuntimeTypeNamesOrReflectionSerialization` | DEL+BND+AOT |
| ARCH07 | `OutboxHandlerRegistry_Should_RejectDuplicateContractId` | DEL |
| ARCH08 | `OutboxHandlerRegistry_Should_CacheMetadata_NotScopedInstances` | DEL |
| ARCH09 | `ScopedOutboxHandler_Should_BeResolved_FromDeliveryScope` | DEL |
| ARCH10 | `RequiredConsumerRegistry_Should_CacheMetadata_NotScopedInstances` | DEL |
| ARCH11 | `OutboxPayload_Should_RequireGeneratedJsonTypeInfo` | DEL+AOT |
| ARCH12 | `ExistingEventBusAndDlq_Should_Not_BeOutboxAuthority` | BND |
| A10 | `ControlPlane_Save_Should_Not_Enlist_Runtime_Outbox` | PG |
| C01 | `Missing_RequiredContractHandler_Should_Fail_Composition_Without_MessageMutation` | DEL+PG |
| C08 | `ActiveMessage_WithUnsupportedContract_Should_Fail_Composition` | SH+IM+PG |
| C08 | `UnsupportedActiveContract_Should_Remain_Unmodified` | SH+IM+PG |
| C10 | `TerminalMessage_Should_Not_Require_CurrentHandlerRegistration` | SH+IM+PG |
| ARCH13 | `OutboxCompositionException_Should_Not_ExposeProviderDetails` | DEL+BND |
| ARCH14 | `IAuditSink_Should_Not_GainDurabilityCapability` | AUD+BND |
| ARCH15 | `IAuditRecorder_Should_Not_Expose_PreparedEnvelopeBypass` | AUD+BND |
| ARCH16 | `PreparedAuditRecording_Should_Be_AccountabilityInternal` | AUD+BND |

#### Append and atomicity

| Case | Exact acceptance name | Runner |
|---|---|---|
| A01 | `State_Commit_Should_Atomically_Create_Outbox_Message` | SH+IM+PG |
| A06 | `Rolled_Back_State_Should_Not_Create_Outbox_Message` | SH+IM+PG |
| A07 | `CommitUnknown_Should_Never_Expose_Split_State_And_Outbox` | PG+CR |
| A08 | `OutboxAppendFailure_Should_Rollback_RuntimeMutation` | SH+IM+PG |
| A03 | `Append_Replay_With_SameIntegrity_Should_Be_Duplicate` | SH+IM+PG |
| A04 | `OutboxConflict_Should_Abort_RuntimeTransaction` | SH+IM+PG |
| A04 | `IgnoredConflict_Should_Not_Be_Possible_On_CanonicalProducerPath` | DEL+SH |
| A03 | `Duplicate_Should_Not_Abort_RuntimeTransaction` | SH+IM+PG |
| A03 | `Append_Duplicate_Should_Not_Reset_DeliveryState` | SH+IM+PG |
| A09 | `SameMessageId_InDifferentTenant_Should_Abort_RuntimeTransaction` | SH+IM+PG |
| A11 | `AcceptedAppend_Should_Use_ProviderClock_ForInitialAvailability` | SH+IM+PG |
| A12 | `RequiredConsumerIds_Should_Participate_In_OutboxIntegrity` | DEL+SH |
| A03 | `ImmutablePayload_Should_Not_Change_AfterCallerMutation` | DEL+SH |
| R03 | `Retry_Should_Not_Mutate_LogicalPayload` | SH+IM+PG |

#### Claim, lease, fencing, and terminal state

| Case | Exact acceptance name | Runner |
|---|---|---|
| L01 | `Pending_Message_Should_Be_Claimed_With_FirstFence` | SH+IM+PG |
| L02/R08 | `NotYetDue_Message_Should_Not_Be_Claimed` | SH+IM+PG |
| L03 | `Concurrent_Dispatchers_Should_Respect_FencingToken` | SH+IM+PG |
| L04 | `ExpiredLease_Should_Allow_NewerGeneration` | SH+IM+PG |
| L05/L08 | `Expired_Owner_Should_Not_Acknowledge_NewerLease` | SH+IM+PG |
| L06 | `Stale_Owner_Should_Not_Schedule_Retry` | SH+IM+PG |
| L07 | `Stale_Owner_Should_Not_DeadLetter` | SH+IM+PG |
| L09 | `Valid_Owner_Should_Acknowledge_To_Delivered` | SH+IM+PG |
| L10 | `Retry_Should_Use_ProviderClock_And_Preserve_MessageId` | SH+IM+PG |
| L11/R06 | `Poison_Message_Should_Move_To_DeadLetter` | DEL+SH+IM+PG |
| L12 | `Delivered_Message_Should_Not_Be_Claimed` | SH+IM+PG |
| L12 | `DeadLettered_Message_Should_Not_Be_Claimed` | SH+IM+PG |
| L11 | `DeadLetter_Should_Be_One_OutboxTerminalTransition` | SH+IM+PG |
| L13 | `UnregisteredContract_Should_Not_BeClaimed_OrConsumeAttemptBudget` | SH+IM+PG |
| R11 | `Repeated_ClaimCrash_Should_Consume_AttemptBudget` | SH+PG+CR |
| R12 | `AttemptBudgetExhausted_Should_DeadLetter_Without_HandlerInvocation` | DEL+SH+PG+CR |
| L14 | `Ack_Replay_With_ExactTerminalFence_Should_Be_AlreadyApplied` | SH+IM+PG |
| L15 | `DeadLetter_Replay_With_ExactTerminalFence_Should_Be_AlreadyApplied` | SH+IM+PG |
| L16 | `TerminalReplay_With_DifferentFence_Should_Be_StaleOrConflict` | SH+IM+PG |
| L14/L15 | `AlreadyApplied_Should_Not_Reopen_TerminalState` | SH+IM+PG |
| C13 | `UnsupportedActiveRequirement_Should_Throw_ProviderNeutralCompositionFailure` | SH+IM+PG |
| C13/C14 | `CompositionFailure_Should_Not_Be_Classified_As_TransientStoreFailure` | DEL+PG |

#### Recovery and ambiguity

| Case | Exact acceptance name | Runner |
|---|---|---|
| R01 | `Pending_Message_Should_Be_Recovered_After_Restart` | PG+CR |
| R09 | `RetryDue_Message_Should_Be_Recovered_After_Restart` | PG+CR |
| R02 | `ExpiredLease_Should_Recover_After_Restart` | PG+CR |
| R04 | `Publish_ResponseLoss_Should_Redeliver_SameMessageId` | SH+PG+CR |
| R05 | `Ack_ResponseLoss_AfterCommit_Should_Remain_Delivered` | SH+PG |
| CW01 | `Crash_BeforeProducerCommit_Should_ExposeNeitherStateNorOutbox` | CR |
| CW02 | `Crash_AfterProducerCommit_Should_RecoverPendingMessage` | CR |
| CW04 | `Crash_AfterClaim_Should_RecoverExpiredLease` | CR |
| CW05 | `Crash_AfterHandlerBeforeAck_Should_PermitSameMessageRedelivery` | CR |
| R13 | `Restart_Should_Not_Reset_AttemptBudget` | PG+CR |
| C03 | `CompositionRecovery_Should_Allow_ExistingPendingMessage_To_Deliver` | PG |
| C11 | `RestoredContractRegistration_Should_Allow_PendingDelivery` | SH+PG |

#### HumanTask mainline

| Case | Exact acceptance name | Runner |
|---|---|---|
| H01 | `HumanTask_Completion_Should_Commit_Completed_And_Outbox` | HT+IM+PG |
| H02 | `HumanTask_CompletionRollback_Should_ExposeNeitherPostStateNorOutbox` | HT+IM+PG |
| H04 | `HumanTask_Delivery_Failure_Should_Not_Create_CompletionDispatchFailed` | HT |
| H01 | `HumanTask_Completion_Should_Not_Publish_Synchronously` | HT |
| H13/H22 | `HumanTask_OutboxHandler_Should_Use_TypedLocalEventDispatch` | HT |
| H05 | `Duplicate_HumanTask_Delivery_Should_Not_Duplicate_Continuation` | HT+WF |
| H06 | `HumanTask_CrashAfterCommit_Should_Eventually_Accept_WorkflowResume` | PG+CR |
| H07 | `HumanTask_PoisonDelivery_Should_Preserve_CompletedBusinessState` | HT+PG |
| H08 | `Legacy_CompletionDispatchFailed_Should_Block_SilentCutover` | PG |
| H08/C06 | `Legacy_CompletionDispatchFailed_Preflight_Should_Be_V012ProviderOwned` | PG+BND |
| C15 | `Legacy_ActiveHumanTask_RequiredConsumerGap_Should_BlockSilentCutover` | HT+PG |
| H03/H10 | `HumanTask_CommitUnknown_Should_Require_Observation_Before_CommandReplay` | HT+PG |
| H09 | `Completed_HumanTask_AfterCommitUnknown_Should_Preserve_OriginalCompletionEventId` | HT+PG |
| H11 | `CommitUnknown_Recovery_Should_Not_Create_SecondCompletionIdentity` | HT+PG |
| H12 | `Completed_HumanTask_WithoutCompletionEventId_Should_FailClosed` | HT |
| H13 | `WorkflowCorrelated_HumanTask_Should_Require_ContinuationConsumer` | HT+WF |
| H14 | `Missing_WorkflowContinuationConsumer_Should_Not_Ack_Outbox` | DEL+HT |
| H14/C09 | `Missing_WorkflowContinuationConsumer_Should_Fail_Composition` | DEL+HT |
| H16 | `Standalone_HumanTask_Should_Not_Require_WorkflowContinuationConsumer` | HT |
| H15 | `Zero_LocalEventHandlers_Should_Not_Prove_WorkflowContinuation` | HT+WF |
| H17 | `CommitUnknown_CompletedObservation_Should_Not_ProveCallerOwnership` | HT |
| H17 | `CommitUnknown_DifferentCompletionWinner_Should_Not_BeReported_AsCallerSuccess` | HT |
| H17/H18 | `CommitUnknown_ConcurrentWinner_Should_Not_Create_SecondCompletion` | HT+PG |
| H19/CW07 | `Crash_After_ResumeCommit_Before_ConsumerReturn_Should_Reconcile_SameCompletion` | WF+PG+CR |
| H19 | `Duplicate_Continuation_Should_Prove_AppliedCompletionIdentity` | WF+SH+IM+PG |
| H20 | `Different_CompletionId_Should_Not_Be_Treated_As_Duplicate` | WF+SH+IM+PG |
| H21 | `ReliableContinuationAck_Should_Not_Require_PostResume_WorkflowExecution` | WF+HT |
| H22 | `Optional_LocalEventHandler_Failure_Should_Not_Block_OutboxAck` | HT+DEL |
| H22 | `Optional_LocalEventHandler_Should_Not_Be_ImplicitReliableConsumer` | HT+DEL |
| H22 | `ReliableAck_Should_Depend_Only_On_PersistedConsumerObligations` | HT+DEL |
| H23 | `Required_BusinessConsumer_Should_Require_StableConsumerId` | HT+BND |
| H23 | `FirstParty_RequiredCompletionHandlers_Should_Use_StableConsumerIds` | ACT+PROC+BND |
| H23 | `Procurement_Mainline_Should_Not_Register_CompletionFailurePolicy` | PROC+BND |

#### Workflow Accountability mainline

| Case | Exact acceptance name | Runner |
|---|---|---|
| W01 | `Workflow_Started_Should_Commit_Accountability_Fact` | WF+IM+PG |
| W01 | `Workflow_Suspended_Should_Commit_Accountability_Fact` | WF+IM+PG |
| W01 | `Workflow_Resumed_Should_Commit_Accountability_Fact` | WF+IM+PG |
| W01 | `Workflow_Completed_Should_Commit_Accountability_Fact` | WF+IM+PG |
| W01 | `Workflow_Failed_Should_Commit_Accountability_Fact` | WF+IM+PG |
| W02 | `Workflow_StateFailure_Should_Not_Append_AccountabilityFact` | WF+IM+PG |
| W03 | `Workflow_BestEffortObserverFailure_Should_Not_Change_Outbox` | WF |
| W08 | `Workflow_Accountability_Should_Persist_Final_AuditEnvelope_NotLifecycleEvent` | WF+AUD |
| W08 | `Workflow_Accountability_Should_Persist_PreparedEnvelope_WithIntegrity` | WF+AUD |
| W03 | `Workflow_AccountabilityObserver_Should_Not_Remain_ReliableWritePath` | WF+BND |
| W07 | `Duplicate_Accountability_Delivery_Should_Preserve_AuditId` | AUD+PG |
| W04/W05 | `Partial_AccountabilitySinkFailure_Should_Retry_Until_AllAccepted` | AUD+DEL |
| W09 | `Accountability_Retry_AfterSanitizerUpgrade_Should_Preserve_Integrity` | AUD |
| W10/W11 | `Accountability_Preparation_Should_Be_SinglePath_ForImmediateAndOutboxRecording` | AUD |
| W12 | `Workflow_Should_Not_Reference_IAuditSink` | BND |
| W11 | `Accountability_OutboxHandler_Should_Be_Owned_By_Accountability` | AUD+BND |
| W11 | `OutboxPreparedAuditPath_Should_Not_Invoke_Sanitizer` | AUD |
| W10 | `OrdinaryAuditRecording_Should_Always_Invoke_Preparation` | AUD |
| W06 | `Accountability_Conflict_Should_DeadLetter` | AUD+DEL |
| C02 | `Missing_RequiredAccountabilitySink_Should_Not_DeadLetter_Message` | AUD+DEL |
| W13 | `ReliableWorkflowAccountability_Should_Require_AtLeastOneConfiguredSink` | AUD |
| W14 | `FullDurableAccountability_Should_Use_PostgreSqlAuditSink` | PG+AOT |
| C04 | `Removed_AccountabilitySink_Should_End_FutureAttemptObligation` | AUD+DEL |
| C05 | `Added_AccountabilitySink_Should_Participate_In_SubsequentAttempt` | AUD+DEL |
| W03 | `BestEffort_WorkflowObservers_Should_Not_Participate_In_ReliableAck` | WF+AUD |

#### Migration and NativeAOT

| Case | Exact acceptance name | Runner |
|---|---|---|
| N01 | `V012_Should_Extend_Existing_RuntimeMigrationCatalog` | PG |
| N01 | `V012_Should_Validate_ExactOutboxSchema` | PG |
| N01 | `V012_Should_Persist_WorkflowContinuationAcceptanceDiscriminator` | PG |
| N01 | `V012_Should_Reject_ChangedAppliedChecksum` | PG |
| N01 | `V012_Should_Reject_OutboxSchemaDrift` | PG |
| C08-C12 | `ActiveRequirementsProbe_Should_Pass_SharedContractKit` | SH+IM+PG |
| H19-H20 | `WorkflowContinuationAcceptance_Should_Pass_SharedContractKit` | SH+IM+PG |
| C12-C13 | `AtomicClaim_Should_Reject_UnsupportedActiveRequirement_WithoutMutation` | SH+IM+PG |
| N02 | `PostgreSqlOutbox_Should_Pass_SharedContractKit` | SH+PG |
| N02 | `InMemoryOutbox_Should_Pass_SharedContractKit` | SH+IM |
| N03 | `Persisted_HumanTaskPayload_Should_Dispatch_Under_NativeAot` | AOT |
| N08 | `Required_WorkflowContinuationConsumer_Should_Execute_Under_NativeAot` | AOT |
| N03/N08 | `WorkflowContinuationAcceptance_Should_Reconcile_Under_NativeAot` | AOT |
| N09 | `Optional_LocalEventFailure_Should_Not_Block_NativeOutboxAck` | AOT |
| N04 | `Persisted_AuditEnvelope_Should_Dispatch_Under_NativeAot` | AOT |
| N07 | `ActiveCompositionProbe_Should_Execute_Under_NativeAot` | AOT |
| N01/N02/N03/N04/N05/N06/N07/N08/N09 | `PostgreSqlOutboxFixture_Should_PublishLinkAndRunNativeBinary` | AOT |
| N01/N02/N03/N04/N05/N06/N07/N08/N09 | `NativeBinary_Should_Emit_ReliableDeliverySentinel` | AOT |

### 8.7 Plan-only supplemental acceptance ledger

These names strengthen implementation evidence without editing the frozen Spec
or the exact 145-name normative manifest.

| Case | Exact supplemental acceptance name | Runner |
|---|---|---|
| MRC01 | `Partial_RequiredConsumerFailure_Should_Retry_Message` | DEL+HT |
| MRC02 | `PreviouslyAcceptedRequiredConsumer_Should_Be_Duplicate_On_Retry` | DEL+HT+PROC+WF |
| MRC03 | `RequiredConsumer_Order_Should_Not_Be_BusinessContract` | DEL+HT |
| MRC04 | `RequiredConsumer_Conflict_Should_FailClosed` | DEL+HT |
| PROC01 | `Procurement_RequiredConsumer_Should_Not_Depend_On_RequestAmbientIdentity` | PROC+BND |
| PROC02 | `RequiredConsumer_Retry_AfterFreshServiceProvider_Should_Preserve_TenantSemantics` | PROC |
| RCA01 | `WorkflowContinuationAcceptance_Integrity_Should_Use_FrozenV1Projection` | WF+SH+IM+PG+BND |
| RCA02 | `Same_CompletionEventId_WithChangedOutcomeOrResult_Should_Conflict` | WF+SH+IM+PG |
| BOOT01 | `DB_CompositionPreflight_Should_Run_After_RuntimeSchemaCompatibility` | DEL+PG |
| BOOT02 | `HumanTaskObligationPreflight_Should_Run_After_V012` | HT+PG |
| BOOT03 | `DB_CompositionPreflight_Should_Not_Use_SyncOverAsync` | DEL+BND+PG |
| PROC03 | `Procurement_RequiredConsumer_Should_Dispatch_Through_CapabilityPipeline` | PROC |
| PROC04 | `Procurement_BackgroundDispatch_Should_Use_DurableTenantAndActor` | PROC |
| PROC05 | `Procurement_InternalApply_Should_Not_Reauthorize_RequestAmbientIdentity` | PROC+BND |
| SCHEMA01 | `WorkflowCorrelated_HumanTask_Row_Should_Require_ContinuationConsumerId` | PG |
| SCHEMA02 | `HumanTask_ObligationColumn_Should_Not_Keep_FailOpenEmptyDefault` | PG |
| OPT01 | `NonCooperative_OptionalHandler_Should_Not_Prevent_ReliableAckProgress` | HT+DEL |
| PROC06 | `Procurement_ExactDecisionReplay_Should_Be_Duplicate` | PROC |
| PROC07 | `Procurement_ChangedDecisionIdentity_Should_Conflict` | PROC |
| ACT01 | `ActivationReview_ExactDecisionReplay_Should_Be_Duplicate` | ACT |
| ACT02 | `ActivationReview_ChangedDecisionIdentity_Should_Conflict` | ACT |
| OUT01 | `HumanTask_Completion_Should_Persist_One_CanonicalOutcome` | HT |
| OUT02 | `WorkflowContinuation_Should_Reuse_PersistedCanonicalOutcome` | HT+WF |
| OPT02 | `OptionalCompatibility_Should_Use_RemainingDeliveryAttemptBudget` | HT+DEL |
| HOC01 | `HumanTaskCreation_Should_Reject_UnregisteredRequiredConsumerId` | HT+DEL |

---

## 9. Slice-by-Slice Red-Green-Review

### Slice 1 — Contracts, projects, and inactive ledger

**Red**

- Add the exact 145-name normative and 25-name supplemental inactive
  acceptance manifests plus separate completeness tests.
- Add architecture tests for provider-type leakage, project references,
  reflection/type-name payload paths, writer/store split, registry lifetimes,
  non-generic consumer metadata versus closed typed resolver catalogs,
  continuation canonical writer placement in Workflow.Abstractions,
  candidate-only `IAuditRecorder`, unchanged `IAuditSink`, and exact
  `OutboxCompositionException` shape.
- Add shared driver interfaces and inactive provider wrappers.

**Green**

- Create both Delivery projects and contract types only.
- Add project references, friend boundaries, DI registration metadata, and
  solution entries.
- Add HumanTask creation-policy/preflight contract shapes and friend boundaries
  with every related acceptance still inactive.
- Add public `IAuditEnvelopePreparer` result contracts only; implementation is
  deferred to Slice 6.5.

**Focused commands**

```bash
dotnet build src/Runtime/Eventing/CrestCreates.Runtime.Delivery.Abstractions
dotnet build src/Runtime/Eventing/CrestCreates.Runtime.Delivery
dotnet test tests/Runtime/Eventing/CrestCreates.Runtime.Delivery.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

**Review gate:** no broad store, no public probe/history API, no domain
dependency, no scoped instance cached, and all future manifest entries remain
inactive.

### Slice 2 — Immutable message and InMemory append

**Red:** A01-A04, A09-A12 plus immutable caller mutation, generated JSON,
required-consumer Integrity, append-without-ambient, and conflict-aborts-state.

**Green**

- Implement limits, canonical writer, and message factory.
- Add Outbox collection/delivery snapshot to
  `InMemoryRuntimePersistenceState.Clone` and commit publication.
- Implement transactional InMemory writer requiring the ambient context.
- Add test-side observation through the shared driver only.

**Commands**

```bash
dotnet test tests/Runtime/Eventing/CrestCreates.Runtime.Delivery.Tests --filter "FullyQualifiedName~Message"
dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests --filter "FullyQualifiedName~OutboxAppend"
dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.Tests
```

**Review gate:** payload is a detached byte snapshot; provider time initializes
delivery fields; Duplicate never resets state; conflict throws inside the
producer transaction.

### Slice 3 — InMemory dispatch and runtime worker core

**Red:** L01-L17, R03-R05, R07, R10-R13, composition exception versus store
failure, terminal response-loss matrix, registry scope tests, and optional
bounded direct-dispatch tests.

**Green**

- Implement InMemory dispatch Store with injected `TimeProvider`.
- Implement registry, options validation, retry policy, classifier, and bounded
  `DispatchBatchAsync`.
- Implement the polling shell but keep its production hosted registration
  inactive until Slice 6 adds the ordered readiness barrier; test the bounded
  component directly.

**Commands**

```bash
dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests --filter "FullyQualifiedName~Outbox"
dotnet test tests/Runtime/Eventing/CrestCreates.Runtime.Delivery.Tests
```

**Review gate:** every claim increments attempts/fence; over-budget claims do
not invoke handlers; expired owners cannot mutate; optional observers have no
Ack authority; no sleep-based semantic tests.

### Slice 4 — V012 and PostgreSQL shared parity

**Red**

- V012 apply/reapply/checksum/schema/collation/check/index drift.
- SCHEMA01-SCHEMA02: correlated-row containment CHECK, explicit provider
  insert, and proof that the temporary empty-array default is absent from the
  final manifest.
- CompletionDispatchFailed direct preflight.
- Required consumer authority-column round trip and workflow backfill.
- `[JsonIgnore]`/closed-materializer proof that the authority set is absent
  from PostgreSQL `state_json`.
- Outbox append/claim/fence parity and two-provider concurrent claim.
- Active composition probe and Claim guard.
- Continuation acceptance Store shared cases, including RCA01-RCA02.
- InMemory/PostgreSQL bounded HumanTask obligation-preflight shared cases for
  workflow-correlated and standalone active rows.

**Green**

- Append V012 and exact schema manifest.
- Implement PostgreSQL writer, dispatch Store, row codec, continuation store,
  HumanTask obligation column codec, and provider-side bounded preflight.
- Register provider participants base-first.

**Commands**

```bash
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~Outbox|FullyQualifiedName~V012|FullyQualifiedName~ContinuationAcceptance"
dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests --filter "FullyQualifiedName~ContinuationAcceptance"
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

**Review gate:** one provider kernel/catalog/data source; append requires ambient
session; dispatch owns separate transactions; unsupported active facts mutate
nothing; immutable columns never appear in delivery-state `SET` clauses; the
final obligation column has no default and correlated rows cannot omit the
continuation ID.

### Slice 5 — Neutral atomicity and HumanTask ambiguity observation

**Red:** A05-A08, H09-H12, H17-H18; neutral both-or-neither,
append-conflict rollback, Created/Assigned versus Completed observation, exact
RuntimeStateValue intent comparison, and concurrent-winner cases. H01-H03 stay
inactive; no production HumanTask completion writes an Outbox message here.

**Green**

- Complete neutral writer enlistment tests using a test-owned Runtime mutation;
  do not wire a production HumanTask or Workflow Outbox producer.
- Surface commit unknown without delegate replay.
- Implement exact HumanTask observation helper without a caller-ownership
  claim or new command-id protocol.

**Commands**

```bash
dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests --filter "FullyQualifiedName~CommitUnknown|FullyQualifiedName~OutboxAtomicity"
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~OutboxAtomicity|FullyQualifiedName~CommitUnknown"
```

**Review gate:** known rollback exposes neither post-state nor message; commit
unknown is not rollback; blind completion replay cannot create a second
CompletionEventId; the existing HumanTask synchronous completion path remains
unchanged until the atomic Slice 7 cutover, so no temporary dual mainline exists.

### Slice 6 — Worker, composition recovery, restart, and CrashWorker

**Red:** R01-R13, C01-C03, C07-C14, BOOT01-BOOT03, CW01-CW06 plus CW04B,
Ack-loss, retry-due restart, attempt-budget restart, repaired composition,
schema-before-probe ordering, no sync-over-async, and unsupported fact
appearing between readiness and Claim.

**Green**

- Complete hosted lifecycle and composition state handling.
- Bridge the existing PostgreSQL compatibility service into the required
  `IBootstrapTask` topology with once-only schema readiness; add the async
  durable-composition task/readiness barrier and make the worker await it.
- Register the matching InMemory no-op schema task and make required missing or
  duplicate Bootstrap TaskIds fail closed.
- Add PostgreSQL restart fixtures and CrashWorker scenarios CW01-CW06 plus
  CW04B.
- Keep legacy DLQ as an optional diagnostic projection only.

**Commands**

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~BootstrapCoordinator"
dotnet build tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~OutboxCrash|FullyQualifiedName~OutboxRestart|FullyQualifiedName~Composition"
```

**Review gate:** cancellation/worker death leaves lease recovery; missing
composition never consumes attempts or terminalizes; exact Ack-loss stays
Delivered; PostgreSQL probes cannot run before V012; no durable check is
reachable through synchronous `Validate()`; Claim remains closed until the
composition-ready barrier opens.

### Slice 6.5 — Accountability preparation and delivery foundation

**Red:** candidate/prepared trusted-entry boundaries, ordinary recording always
prepares, prepared recording never sanitizes, scoped sink fan-out,
Accountability handler Integrity validation/classification, Workflow envelope
mapping parity, and no public prepared bypass. Do not activate the five
Workflow transition atomicity acceptances yet.

**Green**

- Extract `DefaultAuditRecorder` into the single
  `IAuditEnvelopePreparer -> PreparedAuditRecorder -> AuditSinkFanOut` pipeline.
- Implement and register the Accountability-owned Outbox handler and its
  no-sink composition guard.
- Implement `WorkflowAccountabilityEnvelopeFactory` and
  `WorkflowAccountabilityOutboxProducer` as reusable preparation/building
  components, without cutting over any transition owner or removing the old
  observer.
- Keep the existing immediate Accountability path behavior-compatible while
  making the trusted internal entry available to the future resume message.

**Commands**

```bash
dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Tests
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests --filter "FullyQualifiedName~AccountabilityEnvelopeFactory"
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

**Review gate:** Slice 7 can construct and deliver a real prepared
`workflow.resumed` Audit Outbox fact; no temporary lifecycle payload, fake
producer, public bypass, or transition cutover has been introduced.

### Slice 7 — HumanTask and required Workflow continuation cutover

**Red:** H01-H23, MRC01-MRC04, PROC01-PROC07, ACT01-ACT02, OUT01-OUT02,
OPT01-OPT02, HOC01, RCA01-RCA02, existing HumanTask canonical hash/pin v1 compatibility,
first-party consumer inventory, Activation/Procurement creation-policy IDs,
all-active legacy gap preflight,
zero-handler behavior, optional failure, receipt response loss, changed-intent
conflict, and no post-resume RunAsync Ack dependency.

**Green**

- Freeze creation-request/policy obligations into HumanTask instance/provider
  without changing `HumanTaskDescriptor` or either v1 canonical profile.
- Reject any request-declared obligation absent from active HumanTask completion
  consumer metadata before task persistence.
- In one commit, replace completion synchronous publish/failure-state behavior
  with the Completed + Outbox transaction; H01-H03 activate only here.
- Add HumanTask typed handler and bounded compatibility dispatcher.
- Replace Workflow LocalEvent subscriber with required consumer.
- Implement continuation receipt + Workflow CAS + prepared
  `workflow.resumed` Audit Outbox transaction and CW07.
- In the same commit, make the legacy Accountability observer ignore only
  `workflow.resumed`; keep unrelated best-effort lifecycle observers active.
- Migrate Activation Review and Procurement registrations/declarations.
- Capture `ProcurementHumanTaskDecisionFact` at admitted completion and make
  its required consumer dispatch the internal Apply capability through the
  full pipeline with explicit persisted tenant/approver facts.
- Remove request-time permissions only from internal HumanTask-source Apply
  descriptors; retain public admission permissions and source guards.
- Canonicalize Outcome once in HumanTask and add exact Procurement/Activation
  replay classification plus non-cooperative optional-handler containment using
  only the remaining attempt deadline.
- Remove Procurement LocalEvent bus/failure-policy recovery authority.

**Commands**

```bash
dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests --filter "FullyQualifiedName~Continuation|FullyQualifiedName~HumanTask"
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~ActivationReview"
dotnet test samples/ProcurementApproval/tests/CrestCreates.Sample.Procurement.Tests --filter "FullyQualifiedName~Acceptance"
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~ContinuationAcceptance|FullyQualifiedName~OutboxCw07"
```

**Review gate:** only persisted IDs block Ack; waiting-key absence is not proof;
RunAsync failure does not reverse acceptance; no new mainline writes or reads
`CompletionDispatchFailed`; existing HumanTask v1 pins remain executable;
multi-consumer retry makes no ordering or exactly-once claim; background
Procurement delivery reads no request ambient identity and still traverses the
Capability Pipeline; exact changed first-party decision identity conflicts;
canonical Outcome has one producer owner; a non-cooperative optional handler
cannot hold reliable Ack or extend the delivery attempt deadline; unregistered
request obligations cannot be frozen into a new task.

### Slice 8 — Workflow Accountability cutover

**Red:** remaining W01-W07 and W13-W14 transition/composition cases, C02-C05,
regression facets of W08-W12, the remaining four transition atomic cases,
sanitizer-upgrade retry, sink add/remove, conflict, no-sink composition, and
PostgreSqlAuditSink restart Duplicate. The Slice 7 resumed path stays Green.

**Green**

- Reuse the Slice 6.5 preparation, prepared recorder, handler, envelope factory,
  and producer without creating another path.
- Add atomic prepared Accountability append at started, suspended, completed,
  and failed transition owners; `workflow.resumed` remains the Slice 7 path.
- Retire the Accountability observer registration/source.
- Require at least one configured sink only when reliable Workflow
  Accountability is enabled.

**Commands**

```bash
dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Tests
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests --filter "FullyQualifiedName~Accountability"
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~AccountabilityOutbox|FullyQualifiedName~AuditSink"
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

**Review gate:** Workflow never sees sinks; persisted payload is prepared and
stable; ordinary recording always sanitizes; Outbox delivery never sanitizes;
there is one reliable write path.

### Slice 9 — Process crash and NativeAOT closure

**Red:** N01-N09, all CW parent assertions, exact native markers, generated JSON
root guards, native active composition, optional LocalEvent failure, required
consumer receipt, PostgreSqlAuditSink restart, terminal replay, and stale fence.

**Green**

- Extend CrashWorker and PostgreSQL AOT Host/Fixture only.
- Ensure the AOT Host uses real `AddRuntimeDelivery`, HumanTask, Workflow,
  Accountability, PostgreSQL provider, and hosted/bounded dispatcher code.
- Run both payload paths from persisted bytea through generated metadata.

**Commands**

```bash
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~OutboxCrash"
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests --filter "FullyQualifiedName~TransactionalOutbox"
```

The fixture itself must execute:

```bash
dotnet publish tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/CrestCreates.Runtime.Persistence.PostgreSql.AotHost.csproj -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot
```

**Review gate:** native link completes, original executable runs against real
PostgreSQL, all three markers appear, and analyzer/publish-only evidence is not
accepted as execution proof.

### Slice 10 — Canonical regression and closure review

Run:

```bash
dotnet build
dotnet test tests/Runtime/Eventing/CrestCreates.Runtime.Delivery.Tests
dotnet test tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests
dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Tests
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests
dotnet test
git diff --check
```

Produce `docs/review/phase-9c-transactional-outbox-closure-review.md` with:

- Case/acceptance/runner evidence counts;
- local versus CI provenance;
- migration checksum/schema evidence;
- CrashWorker window results;
- native publish/run command, artifact, markers, and logs;
- every INV-01..INV-30 and exit 1..35 disposition;
- explicit non-claims for ordering, exactly-once, Inbox, brokers, retention,
  post-resume Workflow scheduler liveness, and durable Agent Control Plane
  ActivationRequest persistence.

The closure review states explicitly: Phase 9c makes the Activation Review
HumanTask delivery fact durable and invokes its required consumer at least
once, but does not upgrade the existing in-memory ActivationRequest authority
to FullDurable persistence. FullDurable golden evidence remains
HumanTask-to-Workflow and Workflow-to-Accountability.

Implementation review update (2026-08-24): B2 fail-closed proof and provider
clock work are present, but the review identified remaining protocol,
first-party consumer, migration, and evidence gaps. Phase 9c must not be
described as closed until the frozen V012 contract and closure ledger pass.

The remaining closure work is evidence execution: the first-class commit-
unknown caller observation API and CrashWorker/native golden-path runs must be
recorded before the implementation can be marked closed.

---

## 10. Final Review Guardrails

Every Slice review answers:

| Question | Required answer |
|---|---|
| Is there one Runtime transaction authority? | Yes |
| Can append commit outside that authority? | No |
| Are writer and dispatch lifecycle separate? | Yes |
| Can a producer resolve the dispatch Store? | No |
| Can conflict be ignored by the canonical producer? | No |
| Does commit unknown trigger automatic replay? | No |
| Are message bytes/required IDs immutable and integrity-protected? | Yes |
| Does every claim consume an attempt? | Yes |
| Can an over-budget claim invoke a handler? | No |
| Can an expired/stale owner mutate delivery state? | No |
| Is exact terminal replay distinguishable from conflict? | Yes |
| Can unsupported composition lease or mutate a fact? | No |
| Is composition failure exactly `OutboxCompositionException`? | Yes |
| Can infrastructure failure use that exception? | No |
| Are required consumers only persisted stable IDs? | Yes |
| Can an optional LocalEvent handler block Ack? | No |
| Does Workflow continuation have durable applied identity? | Yes |
| Is waiting-key absence sufficient duplicate proof? | No |
| Does HumanTask Ack require post-resume RunAsync? | No |
| Can delivery failure change HumanTask business status? | No |
| Is every Workflow transition paired with prepared Audit Outbox? | Yes |
| Does public `IAuditRecorder` remain candidate-only? | Yes |
| Can Outbox Audit delivery re-run sanitization? | No |
| Can Workflow resolve an Audit sink? | No |
| Does generic sink composition claim durability? | No |
| Does FullDurable evidence use PostgreSqlAuditSink? | Yes |
| Does Phase 9c claim durable ActivationRequest authority? | No |
| Are both providers driven by the same semantic cases? | Yes |
| Is V012 in the one existing catalog/manifest? | Yes |
| Does the original native binary execute both persisted payloads? | Yes |

Any unsafe answer blocks the Slice handoff.

---

## 11. Implementation Handoff Template

At the end of each Slice record:

```text
Slice:
Commit:
Files added/modified/moved:
Activated Case IDs:
Activated acceptance names:
Red command and missing-behavior signal:
Green focused commands and counts:
Shared runner evidence:
PostgreSQL/Crash/AOT evidence where applicable:
Boundary review:
Spec/Plan deviations:
Unresolved findings:
Next Slice prerequisites:
```

No implementation deviation may silently change the frozen Spec. If code facts
make a frozen invariant unimplementable, stop at the current Review gate and
return to design review rather than adding a fallback or weakening evidence.
