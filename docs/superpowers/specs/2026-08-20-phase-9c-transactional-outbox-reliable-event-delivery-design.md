# Phase 9c — Transactional Outbox & Reliable Event Delivery Design Spec

- **Date:** 2026-08-20
- **Issue:** [#25 — Phase 9c Transactional Outbox & Reliable Event Delivery](https://github.com/OrchesAdam/CrestCreates/issues/25)
- **Depends on:** [#39 — Phase 9a Accountability Runtime Foundation](https://github.com/OrchesAdam/CrestCreates/issues/39), [#24 — Phase 9b Durable Persistence Foundation](https://github.com/OrchesAdam/CrestCreates/issues/24)
- **Current-master baseline:** `d20341f0`
- **Status:** R4 APPROVED / FROZEN; implementation awaits an approved implementation plan
- **Provider baseline:** InMemory `FullSemantic`; PostgreSQL 16 direct-Npgsql `FullDurable`; migration catalog currently ends at V011

---

## 1. Decision Summary

Phase 9c closes the reliable side-effect boundary of the existing durable
Runtime commit kernel. It is not a generic EventBus rewrite, broker project, or
second Unit-of-Work framework.

The canonical mainline is:

```text
Runtime mutation candidate
    -> allocate producer-owned stable MessageId
    -> serialize one typed payload with generated JsonTypeInfo
    -> build immutable OutboxMessage + structured Integrity
    -> IRuntimeTransactionCoordinator.ExecuteAsync(...)
         + authoritative Runtime state mutation
         + ITransactionalOutboxWriter.AppendAsync(...)
    -> COMMIT
    -> active ContractId + RequiredConsumerId composition probe
    -> IOutboxDispatchStore.ClaimAsync(...)
         + lease owner
         + monotonically increasing fencing token
    -> explicit ContractId -> typed delivery handler
         -> declared required consumers
    -> Ack / Retry / DeadLetter on the same fenced generation
```

The PostgreSQL provider already owns the ambient connection/transaction session.
The Outbox writer joins that session without exposing `DbTransaction`,
`NpgsqlTransaction`, connection objects, SQLSTATE, or provider exceptions through
Runtime contracts. The InMemory provider extends its existing staged transaction
state with the same observable semantics.

Phase 9c closes two production golden mainlines:

```text
A. HumanTask completion
   HumanTask Completed + HumanTaskCompletedEvent Outbox append
       -> persisted required-consumer delivery
       -> durable Workflow resume acceptance

B. Workflow Accountability
   committed Workflow transition + prepared safe AuditEnvelope Outbox append
       -> Accountability-internal prepared recorder
       -> all configured Accountability sinks
```

Delivery is at-least-once. The phase does not claim exactly-once handler effects,
distributed transactions, global ordering, per-key ordering, or reliable delivery
through Kafka/RabbitMQ.

---

## 2. Current-Master Codebase Alignment

This Spec supersedes the older broad EventBus interpretation of Issue #25 and is
derived from the current code.

### 2.1 Existing transaction authority is sufficient

Current facts:

- `IRuntimeTransactionCoordinator` exposes only Required callback execution.
- `PostgreSqlRuntimeTransactionCoordinator` owns the ambient session and maps
  ambiguous COMMIT acknowledgement to
  `RuntimeTransactionCommitUnknownException`.
- Runtime Stores automatically join the ambient provider session.
- Phase 9b already proves atomic multi-Store suspension commits.
- InMemory clones/stages one complete Runtime persistence state and publishes it
  only after invariant validation.
- Phase 9b closure explicitly leaves only the same-transaction Outbox enlistment
  probe to #25.

Therefore Phase 9c adds a transaction participant. It does not add propagation
options, expose provider handles, or change the coordinator abstraction.

### 2.2 HumanTask completion currently mixes business and delivery state

`DefaultHumanTaskRuntime.CompleteAsync` currently:

```text
HumanTask -> Completed
    -> Store.UpdateAsync commits
    -> ILocalEventBus.PublishAsync synchronously
    -> on delivery failure, HumanTask -> CompletionDispatchFailed
```

This creates a post-commit crash window and represents transport failure as a
business lifecycle state. `CompletionDispatchFailed`, its error/attempt fields,
and `IHumanTaskCompletionFailurePolicy` are compatibility/recovery machinery for
that split path, not the target model.

Phase 9c changes the authority split:

```text
HumanTask lifecycle authority: Created / Assigned / Completed / Cancelled
Outbox delivery authority: Pending / Leased / Delivered / DeadLettered
```

No new completion may enter `CompletionDispatchFailed` after the cutover.

### 2.3 Workflow lifecycle observers are deliberately best-effort

`WorkflowLifecycleEventPublisher` invokes observers under a bounded budget,
logs exceptions/timeouts, and swallows them. This is correct for notifications,
but it cannot be the acknowledgement boundary for a reliable Accountability
fact.

`WorkflowAccountabilityObserver` currently projects a lifecycle event into an
`AuditEnvelope` after state persistence. `DefaultAuditRecorder` then owns a
second, important pipeline: candidate validation, sanitization, protected-fact
comparison, safe-snapshot validation, integrity hashing, and sink fan-out.

Phase 9c extracts both boundaries without duplicating either implementation:

1. Workflow's pure lifecycle-to-candidate mapping moves to a producer factory.
2. Accountability's existing pre-sink pipeline becomes an explicit preparation
   operation shared by immediate recording and Outbox production.
3. Workflow persists the prepared safe `AuditEnvelope`, including
   `Sanitization` and `Integrity`, with the state transition.
4. Outbox delivery revalidates the prepared envelope and fans it out without
   running the sanitizer again.

The trusted-entry boundary does not expand: public `IAuditRecorder` retains only
`RecordAsync(candidate)`. Prepared-envelope validation/recording is
Accountability-internal and cannot be invoked by ordinary producers.

The reliable path persists that prepared envelope, not a lifecycle event that a
later code version would re-project and not a raw candidate that a later
sanitizer version would transform differently.

The existing lifecycle publisher remains for non-authoritative best-effort
observers. The Accountability observer is removed from that lane to prevent a
second write path for the same `AuditId`.

### 2.4 Existing Local Event and DLQ implementations are adapters, not authority

Current Local Event/DLQ code contains properties that cannot become the Phase 9c
mainline:

- runtime `MakeGenericMethod`/`MethodInfo.Invoke` dispatch for untyped events;
- `JsonSerializer.Serialize(object, Type)` and `Deserialize(..., Type)`;
- assembly-qualified CLR type names as persisted reconstruction authority;
- newly generated DLQ MessageIds rather than producer-owned identities;
- EF Core DLQ transactions independent from Runtime state and Outbox state;
- independent DLQ insert followed by a separate Outbox mutation window.

Phase 9c may use the generic Local Event interface through an explicitly typed
adapter, but persisted Outbox delivery never stores or resolves a CLR type name.
The Outbox's `DeadLettered` transition is the terminal reliability authority.
Existing `DeadLetterMessage`/DLQ surfaces may receive a pure diagnostic
projection; they are not a second persistence truth and are not required for
acknowledgement.

### 2.5 Control Plane/reference-data writes are a separate semantic boundary

Issue #69 Stores use provider-owned `ExecuteTopLevelAsync` boundaries and reject
an ambient Runtime transaction. Sharing the same PostgreSQL data source,
migration catalog, or transaction implementation does not implicitly enlist
those writes in the Runtime Outbox.

Outbox append is always explicit. No transaction interceptor silently creates a
message for every provider write.

### 2.6 R2 review-blocker closure

R2 closes the `CHANGES_REQUIRED — NOT_READY_FOR_FREEZE` ledger without changing
the architecture:

- every successful claim consumes AttemptCount; over-budget claims are
  terminalization-only;
- required handler/sink absence is composition failure and cannot DeadLetter a
  valid durable fact;
- HumanTask commit unknown requires fresh observation before another command;
- conflicting append throws a transaction-aborting provider-neutral exception;
- Accountability, not Workflow, owns AuditEnvelope delivery;
- sink membership is evaluated at delivery attempt time;
- PostgreSQL V012/provider preflight owns legacy failure-row detection;
- accepted append eligibility begins at provider insertion time;
- handler registry caches metadata while scoped handlers resolve per delivery
  scope.

### 2.7 R3 composition and trusted-entry closure

R3 closes the `CHANGES_REQUIRED — VERY_CLOSE_TO_FREEZE` ledger:

- Workflow-correlated HumanTask facts persist a stable required continuation
  consumer ID; zero LocalEvent handlers cannot authorize Ack;
- public `IAuditRecorder` remains candidate-only, while prepared recording is
  an Accountability-internal trusted path;
- startup/readiness plus atomic Claim validate all active ContractIds and
  required-consumer IDs against current registries;
- HumanTask commit-unknown observation distinguishes a matching durable result
  from a different concurrent winner without claiming caller ownership;
- exact Ack/DeadLetter terminal replay returns `AlreadyApplied`, with
  cross-terminal/different-fence cases remaining fail-closed.

### 2.8 R4 final correctness closure

R4 closes the final correctness ledger without expanding #25 into a durable
Workflow scheduler:

- the Workflow required-consumer obligation ends at a durable resume transition
  carrying the applied CompletionEventId; waiting-key absence alone is never
  duplicate proof;
- only the contract handler and persisted RequiredConsumerIds control Outbox
  Ack/Retry/DeadLetter; generic LocalEvent compatibility handlers are
  best-effort;
- unsupported active requirements make `ClaimAsync` throw one exact
  provider-neutral `OutboxCompositionException` before mutation;
- generic reliable Accountability composition requires at least one configured
  sink, while PostgreSQL sink durability is a FullDurable evidence requirement,
  not an `IAuditSink` capability claim.

---

## 3. Goal

Provide one reliable, typed, NativeAOT-verified internal delivery path from an
authoritative Runtime state mutation to an eventual handler attempt.

The phase must prove:

1. Runtime state and its delivery fact never commit split-brain.
2. Stable producer identity survives retries, lease recovery, restart, and
   publish/ack ambiguity.
3. Only the current unexpired fencing generation may mutate delivery state.
4. Pending, retry-due, and expired-lease messages recover after process restart.
5. Poison messages leave the active loop through one atomic Outbox terminal
   transition with diagnostic context.
6. Missing deployment composition leaves durable facts recoverable and makes
   the Host unhealthy rather than terminalizing data.
7. HumanTask completion reliably reaches exactly one durable Workflow resume
   acceptance for the same CompletionEventId.
8. Every committed Workflow lifecycle transition reliably presents its frozen,
   prepared `AuditEnvelope` to the Accountability-owned reliable handler.
9. The persisted payload is dispatched by a real linux-x64 NativeAOT binary
   against PostgreSQL.

---

## 4. Boundary

### 4.1 In scope

- Immutable Outbox message and structured replay identity.
- Immutable required-consumer obligations for conditional cross-module Ack.
- Immutable HumanTask completion obligations captured at task creation.
- Producer-owned `MessageId` allocated before the transaction.
- Generated/source-generated JSON payload serialization by exact `JsonTypeInfo<T>`.
- A transactional append writer which fails closed without an ambient supported
  Runtime transaction.
- A distinct dispatch Store for claim, Ack, Retry, and DeadLetter semantics.
- InMemory and PostgreSQL implementations of one shared semantic contract.
- PostgreSQL V012 in the existing checksummed migration/schema catalog.
- Provider-authoritative lease time, monotonically increasing fencing tokens,
  bounded handler-invocation eligibility, and deterministic retry scheduling.
- One hosted/internal dispatcher path using explicit `ContractId` handlers.
- Active Pending/Leased composition validation before Claim.
- HumanTask completion -> explicit required-consumer registry -> durable
  Workflow continuation acceptance, with typed Local Event only as an optional
  compatibility lane.
- Workflow started/suspended/resumed/completed/failed -> frozen prepared
  `AuditEnvelope` -> Accountability-internal prepared recording.
- Accountability candidate preparation separated from sink fan-out while the
  ordinary `IAuditRecorder.RecordAsync` path composes those same operations.
- Outbox-owned terminal dead-letter state and safe failure diagnostics.
- Same-transaction, rollback, commit-unknown, restart, concurrency, ack-loss,
  crash-worker, and NativeAOT evidence.
- Explicit upgrade handling for pre-existing `CompletionDispatchFailed` rows.

### 4.2 Out of scope

- Generic EventBus replacement.
- Kafka, RabbitMQ, cloud queue, or multiple delivery provider support.
- Exactly-once delivery or exactly-once external/business side effects.
- A generic consumer Inbox or framework-wide handler idempotency database.
- Global ordering, tenant ordering, event-name ordering, or aggregate ordering.
- Distributed saga/compensation orchestration.
- Crash-safe scheduling/liveness for arbitrary post-resume Workflow execution;
  `RunAsync` recovery is a Workflow Runtime concern, not the HumanTask Outbox
  Ack obligation.
- Distributed transactions across databases, sinks, or brokers.
- Control Plane/reference-data automatic Outbox enlistment.
- Agent Memory/accountability mutation atomicity backfill.
- Cache invalidation/version semantics (#26 consumes reliable facts later).
- Admin UI, product query API, manual replay API, retention, purge, archival,
  WORM, compliance export, or DLQ dashboard.
- Deleting delivered/dead-lettered rows.
- Handler lease renewal. The configured handler deadline must be shorter than
  the lease; an overrun loses ownership and may redeliver.
- Guaranteeing all arbitrary existing `ILocalEventHandler<T>` implementations
  are idempotent. First-party golden handlers must be made duplicate-safe.

### 4.3 Compatibility position

- Existing `ILocalEventBus`, broker implementations, and DLQ APIs remain
  compatibility/integration surfaces. Phase 9c does not extend them into the
  durable authority.
- `CompletionDispatchFailed` and `IHumanTaskCompletionFailurePolicy` become
  obsolete compatibility-only recovery surfaces. New mainline code does not
  write or consult them.
- Pre-existing `CompletionDispatchFailed` rows cannot be blindly converted:
  one or more old synchronous handlers may already have committed side effects.
  PostgreSQL V012 migration/preflight owns their detection through a direct
  existence check and fails cutover with one safe deterministic error. This
  does not add a status-enumeration API to `IHumanTaskInstanceStore`. Operators
  must complete the existing explicit recovery protocol, or explicitly
  reconcile the rows, before rerunning the upgrade. V012 never silently marks
  them Completed or fabricates an Outbox message.
- Existing best-effort Workflow observers continue to receive lifecycle
  notifications after known commit. They do not participate in Outbox Ack.
- `WorkflowAccountabilityObserver` is retired as a registered observer; its pure
  mapping semantics move to the producer-side envelope factory.
- `HumanTaskCompletedWorkflowSubscriber` is retired from the generic
  `ILocalEventHandler<HumanTaskCompletedEvent>` enumerable in the reliable Host
  and becomes the Workflow-owned required continuation consumer. The generic
  typed LocalEvent surface remains available for optional compatibility
  handlers; its zero-handler success is not a reliable continuation Ack.
- First-party business-critical completion handlers migrate to stable consumer
  IDs captured on new HumanTasks. Cutover must detect or explicitly reconcile
  any pre-existing active standalone task that requires such a migrated
  consumer but lacks the durable obligation; it cannot silently treat that
  handler as optional.

---

## 5. Ownership and Dependency Direction

### 5.1 Canonical ownership

| Concern | Canonical owner |
|---|---|
| Runtime state transition meaning | Workflow / HumanTask Runtime |
| Runtime transaction/session | Runtime Persistence kernel/provider |
| Immutable delivery identity and lifecycle contracts | Runtime Delivery Abstractions |
| Message integrity projection | Runtime Delivery Runtime using Canonical Hash Runtime |
| Claim/lease/fencing SQL and provider clock | provider implementation |
| Retry/permanent-failure policy | Runtime Delivery Runtime |
| HumanTask payload mapping and optional typed Local Event compatibility bridge | HumanTask Runtime |
| Immutable HumanTask completion obligation set | HumanTask creation/runtime |
| HumanTask required-consumer contract/validation | HumanTask Runtime |
| Workflow continuation required-consumer implementation | Workflow Runtime |
| Durable continuation acceptance discriminator | Workflow Runtime + Runtime Persistence provider |
| Workflow -> candidate `AuditEnvelope` mapping | Workflow Runtime |
| Audit preparation, trusted prepared validation, and sink fan-out | Accountability Runtime |
| `audit-envelope/v1` typed delivery adapter | Accountability Runtime |
| Active requirement query/atomic Claim guard | Outbox dispatch Store/provider |
| Terminal delivery state | Outbox dispatch Store |
| Legacy DLQ view | compatibility/diagnostic projection only |

### 5.2 Project shape

Proposed placement:

```text
src/Runtime/Eventing/
  CrestCreates.Runtime.Delivery.Abstractions/
    immutable message/replay contracts
    writer and dispatch Store contracts
    claim/fencing/failure/result contracts
    typed handler + active composition contracts

  CrestCreates.Runtime.Delivery/
    message factory + canonical projector
    handler registry/startup validator
    retry policy + dispatcher + hosted service

src/Runtime/Persistence/
  CrestCreates.Runtime.Persistence.InMemory/
    InMemory transactional writer/dispatch Store participant

src/Persistence/
  CrestCreates.Runtime.Persistence.PostgreSql/
    PostgreSQL writer/dispatch Store
    V012 + exact schema manifest

src/Runtime/HumanTask/
  CrestCreates.HumanTask.Abstractions/
    reliable completion consumer contract + stable consumer ID
    immutable creation/instance completion obligations

src/Runtime/Workflow/
  CrestCreates.Workflow/
    Workflow continuation required-consumer implementation/registration
```

Dependency rules:

```text
Runtime.Delivery.Abstractions
    -> Core.Abstractions
    -> Metadata.Abstractions (CanonicalHash contract only)

Runtime.Delivery
    -> Runtime.Delivery.Abstractions
    -> Metadata canonical hash runtime
    -> Microsoft.Extensions.Hosting/DI

HumanTask / Workflow
    -> Runtime.Delivery.Abstractions
    -> existing Runtime Persistence Abstractions

Accountability
    -> Runtime.Delivery.Abstractions

Workflow
    -> Accountability.Abstractions (preparation/recording contracts only)

Provider implementations
    -> Runtime.Delivery.Abstractions
    -> existing domain abstractions as already permitted
```

Forbidden:

- Runtime Delivery Abstractions -> Npgsql, EF Core, HumanTask, Workflow,
  Accountability, Platform, or Web.
- Runtime Delivery Runtime -> HumanTask, Workflow, Accountability, broker
  implementations, or concrete persistence providers.
- HumanTask/Workflow -> concrete Outbox provider.
- producer/handler -> `IOutboxDispatchStore`.
- provider -> concrete Workflow/HumanTask Runtime.
- Workflow -> `IAuditSink` or a concrete Accountability implementation.
- producer modules -> Accountability internal prepared recorder/fan-out.

Typed contract handlers remain in the producer/consumer module so the generic
dispatcher never learns domain payload types.

---

## 6. Immutable Message and Replay Contract

### 6.1 OutboxMessage

Conceptual contract:

```csharp
public sealed record OutboxMessage
{
    public required string MessageId { get; init; }
    public required string ContractId { get; init; }
    public required string EventName { get; init; }
    public required int EventVersion { get; init; }

    public string? TenantId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public ImmutableArray<string> RequiredConsumerIds { get; init; } = [];

    public required ReadOnlyMemory<byte> PayloadUtf8 { get; init; }
    public required CanonicalHash Integrity { get; init; }
}
```

The public implementation must expose an immutable snapshot. A mutable caller
array cannot be retained; creation and reads copy payload bytes.

Normative rules:

- `MessageId` is allocated by the producer before the transaction begins.
- The Store never generates or replaces `MessageId`.
- `ContractId` is a stable semantic ID such as
  `crest.humantask.completed/v1` or
  `crest.accountability.audit-envelope/v1`.
- `ContractId` is the only handler/codec lookup key. It is not a CLR type,
  assembly name, descriptor lookup, or arbitrary user input.
- `EventName` and `EventVersion` are immutable diagnostic/business semantics,
  not reconstruction authority.
- `TenantId == null` is exact host scope, never wildcard.
- payload is exactly one bounded UTF-8 JSON value produced with an exact
  generated `JsonTypeInfo<T>`.
- `OccurredAt` is producer fact time. Delivery, claim, failure, and terminal
  timestamps are separate mutable delivery metadata.
- correlation and causation remain distinct; neither is inferred from trace ID,
  tenant, MessageId, or CLR type.
- `RequiredConsumerIds` is a bounded, ordinal-sorted, duplicate-free immutable
  set of semantic consumer obligations. It is not a CLR/service type name.
- an empty set means the contract handler alone owns reliable delivery;
  non-empty IDs must be composed and execute successfully before Ack.
- no mutable delivery property is part of the immutable message.

### 6.2 Message factory and JSON contract

The only supported creation shape is conceptually:

```csharp
public interface IOutboxMessageFactory
{
    OutboxMessage Create<TPayload>(
        OutboxMessageMetadata metadata,
        TPayload payload,
        JsonTypeInfo<TPayload> jsonTypeInfo);
}
```

The factory:

1. validates semantic IDs, bounds, version, time, tenant, correlations, and
   required-consumer IDs;
2. serializes with the supplied generated `JsonTypeInfo<TPayload>`;
3. copies the exact UTF-8 bytes;
4. computes one structured message Integrity;
5. returns an immutable message.

Forbidden mainline APIs:

```text
JsonSerializer.Serialize(object)
JsonSerializer.Serialize(value, Type)
JsonSerializer.Deserialize(bytes, Type)
Type.GetType
AssemblyQualifiedName
DefaultJsonTypeInfoResolver
runtime assembly scan
runtime MakeGenericMethod dispatch
```

### 6.3 Integrity

The dedicated canonical profile is:

```text
ArtifactKind          = RuntimeOutboxMessage
Purpose               = Integrity
Scope                 = InternalFull
ContractVersion       = canonical-hash-v1
CanonicalShapeVersion = runtime-outbox-message-v1
```

The projection includes, in fixed field order:

```text
MessageId
ContractId
EventName
EventVersion
Tenant scope kind + TenantId
CorrelationId
CausationId
OccurredAt
RequiredConsumerIds in exact ordinal order
exact PayloadUtf8 bytes
```

It excludes every delivery-state field. The provider persists the complete
structured `CanonicalHash`, not only `.Value`.

For the Accountability contract there are intentionally two different hashes:

- `AuditEnvelope.Integrity` proves the prepared safe accountability fact using
  the Accountability canonical profile;
- `OutboxMessage.Integrity` proves the complete delivery envelope, including
  metadata and the exact serialized AuditEnvelope bytes, using the Outbox
  canonical profile.

Neither substitutes for the other. The typed Accountability handler verifies
both before sink fan-out.

### 6.4 Append replay result

```csharp
public enum OutboxAppendStatus
{
    Accepted,
    Duplicate
}
```

For global `MessageId` identity:

| Existing | Incoming | Writer observation |
|---|---|---|
| none | valid message | Accepted |
| same MessageId + exact structured Integrity | any current delivery state | Duplicate |
| same MessageId + different structured Integrity | any | throw `OutboxMessageConflictException` |

Duplicate is idempotent append success and never resets delivery state. A
provider-neutral `OutboxMessageConflictException` is a mandatory
transaction-invalidating condition, not an ignorable result value; it never
overwrites the accepted message. Tenant is part of Integrity, so reusing a
MessageId in another tenant throws the same conflict exception rather than
creating a second identity.

---

## 7. Producer and Dispatcher Contract Split

### 7.1 Transactional writer

```csharp
public interface ITransactionalOutboxWriter
{
    ValueTask<OutboxAppendResult> AppendAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default);
}
```

Rules:

- it joins the current ambient Runtime transaction;
- it fails before mutation when no supported ambient Runtime transaction exists;
- it never opens and commits an independent short transaction;
- it does not claim, Ack, Retry, DeadLetter, or query product history;
- Accepted/Duplicate may let the producer transaction proceed;
- conflicting replay always throws `OutboxMessageConflictException` from
  `AppendAsync`; canonical producers cannot receive and ignore a Conflict
  result;
- provider failure remains provider-neutral;
- caller cancellation before COMMIT may roll back; ambiguous COMMIT remains
  `RuntimeTransactionCommitUnknownException` and is never reclassified as
  rollback.

### 7.2 Dispatch Store

```csharp
public interface IOutboxDispatchStore
{
    ValueTask<IReadOnlyList<OutboxDeliveryClaim>> ClaimAsync(
        OutboxClaimRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<OutboxDeliveryMutationResult> AckAsync(
        OutboxDeliveryLease lease,
        CancellationToken cancellationToken = default);

    ValueTask<OutboxDeliveryMutationResult> RetryAsync(
        OutboxDeliveryLease lease,
        OutboxDeliveryFailure failure,
        TimeSpan delay,
        CancellationToken cancellationToken = default);

    ValueTask<OutboxDeliveryMutationResult> DeadLetterAsync(
        OutboxDeliveryLease lease,
        OutboxDeliveryFailure failure,
        CancellationToken cancellationToken = default);
}
```

Composition readiness uses a narrow provider-neutral internal contract:

```csharp
internal interface IOutboxCompositionProbe
{
    ValueTask<ActiveOutboxRequirements> GetActiveRequirementsAsync(
        CancellationToken cancellationToken = default);
}
```

`ActiveOutboxRequirements` contains only distinct bounded ContractId and
RequiredConsumerId sets for Pending/Leased records. It is not a message query,
payload reader, replay API, or product surface. PostgreSQL and InMemory expose
it only to Delivery composition/runtime through narrow internal/friend access.

`ClaimAsync` has one provider-neutral composition-failure contract:

```csharp
public sealed class OutboxCompositionException : Exception
{
    public ImmutableArray<string> UnsupportedContractIds { get; }
    public ImmutableArray<string> UnsupportedRequiredConsumerIds { get; }
}
```

The implementation snapshots bounded, ordinal-sorted safe semantic IDs and
contains no provider exception, SQL, payload, connection, or handler type. An
unsupported active requirement always throws this type before mutation.
Database/network/provider availability failures never use this type.

Producer transaction ownership and dispatcher lifecycle ownership are separate.
There is no broad `IOutboxStore` combining both.

### 7.3 Delivery state

Persisted state is conceptually:

```text
Status             Pending | Leased | Delivered | DeadLettered
AttemptCount       successful claim-generation count; increments on every claim
AvailableAt        next provider-authoritative eligibility time
LeaseOwnerId       current process/worker semantic ID
FencingToken       starts at 0; increments on each successful claim generation
LeaseExpiresAt     provider-authoritative time
LastFailureCode    bounded safe semantic code
LastFailureAt      provider time
DeliveredAt        terminal provider time
DeadLetteredAt     terminal provider time
```

No raw exception, stack trace, SQLSTATE, connection string, payload excerpt,
handler CLR name, or secret is persisted in failure metadata.

### 7.4 Claim eligibility and ordering

A claim may select only:

```text
supported ContractId + supported RequiredConsumerIds
and
(Pending with AvailableAt <= provider clock
 or
 Leased with LeaseExpiresAt <= provider clock)
```

It atomically:

1. locks/selects a bounded batch;
2. sets `Status = Leased`;
3. sets owner and new expiry;
4. increments `FencingToken` and `AttemptCount`;
5. returns a detached immutable message and lease.

Every successful claim consumes one delivery-attempt budget unit, including a
claim after which the process dies, the Host cancels, or the handler is never
entered. For `MaxAttempts = N`, claims 1 through N may invoke the handler. Any
claim returning `AttemptCount > N` is terminalization-only: the dispatcher must
not invoke the handler and must use the current valid fence to DeadLetter with
`DELIVERY_ATTEMPT_BUDGET_EXHAUSTED`.

If the terminalization-only worker itself dies before DeadLetter commits, a
later claim increments the generation/count again but remains
terminalization-only. Thus `MaxAttempts` bounds handler-invocation eligibility,
not the numeric counter under repeated process death. Restart never resets the
counter.

Selection uses a deterministic tie-break for observability:

```text
AvailableAt -> OccurredAt -> MessageId ordinal
```

This is not a delivery-order guarantee. Concurrent workers and retries may
observe another order.

### 7.5 Fenced mutations

Ack/Retry/DeadLetter succeeds only when all match:

```text
MessageId
Status = Leased
LeaseOwnerId
FencingToken
unexpired LeaseExpiresAt at provider mutation time
```

After expiry, the old owner is stale even if no newer worker has claimed yet.
It cannot Ack, schedule Retry, or DeadLetter. A newer claim increments the token.

Mutation outcomes distinguish at least:

```text
Applied
AlreadyApplied        # exact idempotent terminal replay when provable
StaleFence
TerminalConflict
NotFound
```

`bool` is insufficient because it hides ownership and terminal ambiguity.

Terminal response-loss replay is normative:

```text
Ack(exact final lease) after Delivered
    -> AlreadyApplied

DeadLetter(exact final lease + exact final safe failure) after DeadLettered
    -> AlreadyApplied

terminal replay with a different fence
    -> StaleFence or TerminalConflict, never AlreadyApplied

Ack against DeadLettered, DeadLetter against Delivered, or same final fence
with different DeadLetter failure
    -> TerminalConflict
```

`AlreadyApplied` never changes timestamps, failure metadata, immutable message,
AttemptCount, or terminal status. Retry does not promise `AlreadyApplied` because
its successful transition returns the row to Pending and may be followed by a
new generation.

### 7.6 State transitions

```text
Append Accepted
    -> Pending, AttemptCount=0, FencingToken=0
       CreatedAt=AvailableAt=UpdatedAt=provider current time

Claim
    Pending/expired Leased -> Leased, AttemptCount+1, FencingToken+1

Ack(valid unexpired fence)
    Leased -> Delivered

Retry(valid unexpired fence)
    Leased -> Pending, AvailableAt=provider clock+delay

DeadLetter(valid unexpired fence)
    Leased -> DeadLettered
```

Retry/Ack/DeadLetter clear active lease fields. The final fencing generation is
retained sufficiently to classify exact terminal response-loss/replay without
reopening the record.

Delivered and DeadLettered are terminal for normal claim. Phase 9c exposes no
reset/requeue/delete operation.

---

## 8. Dispatcher Runtime

### 8.1 Typed handler registry

```csharp
public interface IOutboxDeliveryHandler
{
    ValueTask<OutboxDeliveryOutcome> DeliverAsync(
        OutboxDeliveryContext context,
        CancellationToken cancellationToken = default);
}
```

Handlers are explicitly registered through a generic registration API such as
`AddOutboxDeliveryHandler<THandler>(contractId)`. Registration creates immutable
metadata mapping the exact ordinal `ContractId` to a scope-local resolver; the
registry never indexes by resolving or caching handler instances from the root
provider. At delivery time it creates a scope and resolves `THandler` from that
scope. Duplicate/blank ContractIds fail startup.

No handler assembly scan, type-name resolution, reflection invocation, untyped
payload fallback, root resolution of a scoped handler, or handler-instance cache
exists.

The two enabled golden compositions declare these required contracts at startup:

```text
crest.humantask.completed/v1
crest.accountability.audit-envelope/v1
```

An enabled required contract without registration is a deployment/composition
failure. Startup validates metadata and resolves every enabled handler once in a
disposable validation scope; it caches neither that scope nor those instances.
Failure prevents dispatcher startup and therefore precedes Claim.

Required-consumer registrations follow the same metadata-versus-instance
lifetime rule and are resolved in that disposable validation scope.

Required downstream domain consumers use a second explicit semantic registry.
Registration metadata maps stable consumer IDs to scope-local resolvers without
caching instances. For Phase 9c the Workflow module registers exactly:

```text
crest.workflow.humantask-continuation/v1
```

The registry proves both composition and the exact consumer to invoke; an
arbitrary `IEnumerable<ILocalEventHandler<T>>` is never required-consumer
authority.

### 8.2 Active composition probe

Active Outbox requirements are the distinct:

```text
ContractId values
RequiredConsumerIds values
```

from Pending and Leased rows. Delivered and DeadLettered rows impose no current
registration obligation.

A provider-neutral internal composition probe compares active requirements with
the current contract-handler and required-consumer registries:

```text
active ContractIds          subset-of registered ContractIds
active RequiredConsumerIds  subset-of registered RequiredConsumerIds
```

The probe runs before hosted dispatch starts and participates in readiness. Any
unsupported active requirement makes the Host unhealthy, leaves every message
unchanged, and prevents Claim. It exposes only distinct bounded semantic IDs,
not payloads or product-history enumeration.
The startup validator represents the mismatch with the same
`OutboxCompositionException` data contract used by Claim; health output exposes
only a bounded safe code/count, not the full ID set.

Each `OutboxClaimRequest` carries both immutable supported-ID sets. Claim repeats
the active-subset guard atomically before selecting rows, closing the race
between readiness probe and Claim. If an unsupported active requirement exists,
Claim throws `OutboxCompositionException` before lease, fence, or AttemptCount
mutation. Providers then select only supported ContractIds whose
required-consumer set is supported.

Restoring removed registration makes the unchanged Pending/expired-leased
message eligible without reset/requeue. Removing a handler/required consumer is
therefore forbidden while any active durable fact still requires it. Missing
composition is never classified as a poison fact or permanent delivery outcome.

### 8.3 Delivery outcomes

```text
Delivered
RetryableFailure(code)
PermanentFailure(code)
```

The dispatcher also classifies thrown handler exceptions through one bounded
failure classifier:

- invalid message integrity, invalid JSON contract, payload/metadata identity
  mismatch, and Accountability rejection/conflict are permanent message/data
  failures;
- transient sink/provider/dependency failure is retryable;
- missing required handler, declared required consumer, or required
  Accountability sink is a deployment/composition failure: it makes the Host
  unhealthy and performs no delivery-state mutation;
- caller/Host cancellation does not mutate delivery state and lets the lease
  expire;
- an unknown exception is retryable until `MaxAttempts`, then terminal;
- a retryable failure on claim N where `N >= MaxAttempts` DeadLetters under the
  current fence;
- a claim where `N > MaxAttempts` never invokes the handler and DeadLetters with
  `DELIVERY_ATTEMPT_BUDGET_EXHAUSTED`.

The failure classifier cannot convert composition failure into
`PermanentFailure`. Fixing composition and restarting the Host must allow the
same pending/expired-leased message to deliver without reset or requeue.
`OutboxCompositionException` is also never classified as a transient Store
failure: it fails readiness/stops claiming until deployment composition changes.

### 8.4 Retry schedule

One deterministic retry policy maps `AttemptCount` and safe failure code to a
bounded delay. Options freeze positive bounds for:

- batch size;
- polling interval;
- lease duration;
- handler timeout;
- maximum attempts;
- base and maximum retry delay.

Startup requires:

```text
HandlerTimeout < LeaseDuration
MaxAttempts > 0
all delays/timeouts within contract maxima
```

Random jitter is excluded from the semantic tests. If operational jitter is
later added, it must be bounded and must not alter retry identity or failure
classification.

### 8.5 Publish/Ack ambiguity

```text
handler succeeded
    -> process dies or Ack does not commit
    -> lease expires
    -> same MessageId may be delivered again
```

This is the intended at-least-once behavior. The dispatcher never generates a
new MessageId for an attempt. Consumers receive MessageId in the delivery
context and own duplicate-safe business behavior.

If Ack committed but its acknowledgement was lost, the record remains Delivered
and is not normally claimed again. The caller must not infer rollback from the
acknowledgement exception.

### 8.6 Hosted execution

The production dispatcher is a hosted service over an internal
`DispatchBatchAsync` component. Tests call the bounded component directly; they
do not depend on polling sleeps.

Each batch:

1. proves active requirements are supported and claims through one scoped Store
   call carrying both registry ID sets;
2. creates a fresh DI scope per message or bounded unit;
3. resolves the exact handler;
4. executes under a timeout shorter than the lease;
5. applies one fenced terminal/retry mutation;
6. logs only safe identity/failure fields.

The worker has one stable process-lifetime `LeaseOwnerId`; it is not a hostname,
secret, connection ID, or random value per claim.

---

## 9. PostgreSQL V012 Provider Design

### 9.1 One existing provider kernel

V012 is appended to `PostgreSqlRuntimeMigrationRunner.Catalog` and
`RuntimeSchemaManifest`. It uses the existing:

- `NpgsqlDataSource`;
- ambient transaction accessor/coordinator;
- schema option and safe identifier quoting;
- checksummed migration history/advisory lock;
- exact table/column/check/index/FK validation;
- provider-neutral exception mapping;
- PostgreSQL AOT Host and CrashWorker projects.

No separate EventBus migration runner, `DbContext`, data source, transaction
accessor, or schema history table is introduced.

### 9.2 Logical schema

V012 adds the authoritative Outbox table:

```text
runtime_outbox_messages

Immutable fact
  message_id                 text collate C primary key
  contract_id                text collate C not null
  event_name                 text collate C not null
  event_version              integer not null
  tenant_scope_kind          text collate C not null
  tenant_id                  text collate C not null
  correlation_id             text collate C null
  causation_id               text collate C null
  occurred_at                timestamptz not null
  required_consumer_ids_json jsonb not null
  payload_utf8               bytea not null
  integrity_json             jsonb not null
  created_at                 timestamptz not null

Mutable delivery state
  status                     integer not null
  attempt_count              integer not null
  available_at               timestamptz not null
  lease_owner_id             text collate C null
  fencing_token              bigint not null
  lease_expires_at           timestamptz null
  last_failure_code          text collate C null
  last_failure_at            timestamptz null
  delivered_at               timestamptz null
  dead_lettered_at           timestamptz null
  updated_at                 timestamptz not null
```

The schema has exact checks for:

- host/tenant scope representation;
- positive event version;
- required-consumer storage root is a JSON array;
- non-negative attempts and fencing token;
- closed status values;
- Pending has no active lease or terminal timestamp;
- Leased has owner/expiry and no terminal timestamp;
- Delivered has only `delivered_at` terminal time;
- DeadLettered has only `dead_lettered_at` terminal time;
- terminal records have no active lease;
- payload is non-empty and bounded in application before SQL.

Required claim index is partial/bounded over active statuses and includes the
eligibility/tie-break fields. Exact SQL/index shape is an implementation-plan
decision, but schema validation must detect missing, extra, changed, or wrongly
collated authority columns/indexes/checks.

No FK links the Outbox row to Workflow or HumanTask. Delivery evidence must
survive later business-state lifecycle and must not make persistence the domain
transition authority.

V012 also adds the closed durable representation chosen for the Workflow
continuation-acceptance discriminator in §11.3. Whether this is a dedicated
immutable receipt table or an equivalent Workflow persistence structure is an
implementation-plan decision, but it must:

- preserve CompletionEventId + HumanTaskKey + WorkflowKey identity across later
  Workflow state transitions;
- join the same resume transaction and provider kernel;
- have InMemory FullSemantic parity and PostgreSQL exact schema validation;
- never be reconstructed from logs, waiting-key absence, or Outbox delivery
  state.

V012/provider codecs also preserve the immutable required completion-consumer
set captured on each HumanTask. Workflow correlation always adds the Workflow
continuation ID by canonical producer rule. Any first-party standalone
required-consumer migration and active-row cutover preflight must be explicit in
the implementation plan and exact schema manifest.

### 9.3 Append and claim mechanics

- Append uses `INSERT ... ON CONFLICT DO NOTHING`, followed by a same-session
  structured Integrity read/compare. Exact equality returns Duplicate;
  inequality throws provider-neutral `OutboxMessageConflictException` before
  the producer delegate can complete.
- Append requires `_coordinator.RequireSession()` and therefore cannot commit
  independently.
- Accepted append assigns `created_at`, `available_at`, and `updated_at` from
  the same provider-authoritative current-time observation. It never derives
  delivery eligibility from producer `OccurredAt`.
- Required consumer JSON uses a source-generated persistence DTO. Append and
  materialization fail closed unless it is a bounded string array in strict
  ordinal order with no blanks or duplicates.
- Claim uses one atomic SQL transaction and PostgreSQL row locking such as
  `FOR UPDATE SKIP LOCKED` plus an update/returning step.
- Before locking candidates, that transaction rejects any Pending/Leased row
  whose ContractId or expanded required-consumer ID is absent from the request's
  supported sets; rejection performs no row update.
- Eligibility, expiry, Ack, Retry, DeadLetter timestamps use PostgreSQL provider
  time (`clock_timestamp()` semantics), not a caller-supplied wall clock.
- Every state mutation predicates on owner/token/status/expiry and updates only
  mutable columns.
- Immutable columns are never part of a delivery-state `SET` clause.

### 9.4 Commit-unknown

For a producer transaction:

```text
Known rollback
    authoritative state absent/pre-state
    Outbox message absent

Known commit
    authoritative state present/post-state
    Outbox message present

Commit unknown
    caller cannot infer either result
    fresh durable observation may show both or neither
    fresh durable observation must never show exactly one
```

No automatic replay of the producer delegate is permitted.

---

## 10. InMemory Full-Semantic Provider

The InMemory transaction state gains Outbox messages and delivery state. Its
transaction clone/commit publishes state plus Outbox together under the same
gate.

It also stages the Workflow continuation-acceptance discriminator with the
resume transition so the required-consumer duplicate cases are provider-parity
semantics, not PostgreSQL-only behavior.

It must pass the same provider-neutral cases for:

- Accepted/Duplicate/conflict-exception append;
- ambient transaction requirement;
- commit/rollback atomicity;
- immutable snapshots;
- due/not-due claim;
- active ContractId/RequiredConsumerId composition probing and atomic Claim
  rejection;
- single claim generation;
- lease expiry/new fence;
- stale Ack/Retry/DeadLetter rejection;
- Ack, retry schedule, and terminal dead-letter;
- exact Ack/DeadLetter AlreadyApplied replay;
- publish/ack redelivery semantics.

The InMemory provider uses an injected deterministic `TimeProvider` for claim
and transition time. It makes no process durability, restart, migration, or
database NativeAOT claim.

Provider-specific wrappers must invoke the same shared static cases; they may
not duplicate assertions in PostgreSQL-only tests.

---

## 11. Golden Mainline A — HumanTask Completion to Workflow

### 11.1 Producer transaction

Before transaction:

1. Load the exact tenant-scoped HumanTask.
2. Require Created or Assigned.
3. Validate the HumanTask Pin and outcome.
4. validate/capture immutable result state.
5. allocate one stable `CompletionEventId`; use it as Outbox `MessageId`.
6. build the final `HumanTaskCompletedEvent`.
7. serialize it with a generated
   `JsonTypeInfo<HumanTaskCompletedEvent>`.
8. build the immutable Outbox message:

```text
ContractId   = crest.humantask.completed/v1
EventName    = humantask.completed
EventVersion = 1
MessageId    = CompletionEventId
TenantId     = HumanTask tenant
OccurredAt   = HumanTask CompletedAt
Payload      = HumanTaskCompletedEvent
RequiredConsumerIds = immutable HumanTask completion obligations
    + (WorkflowKey is not null
        ? crest.workflow.humantask-continuation/v1
        : no Workflow continuation obligation)
```

Required completion obligations are captured on the HumanTask at creation and
cannot be added opportunistically at delivery time. Workflow-created tasks add
the continuation consumer automatically. A standalone producer adds a stable
consumer ID only when that downstream action is required for business
correctness; otherwise it adds none.

Transaction:

```text
IRuntimeTransactionCoordinator.ExecuteAsync
    -> HumanTaskStore.UpdateAsync(candidate Completed, expectedRevision)
    -> ITransactionalOutboxWriter.AppendAsync(message)
    -> COMMIT
```

Known success returns Completed. Known failure/rollback exposes the prior task
and no message. Commit unknown preserves the standard observation rule and does
not authorize blind command replay.

After `RuntimeTransactionCommitUnknownException`, the caller must freshly read
the exact tenant-scoped HumanTask before deciding what to do:

```text
Created or Assigned
    -> the observed completion did not commit;
       caller may issue a new completion attempt

Completed with CompletionEventId
    -> proves that one completion won, not that this caller's ambiguous
       completion won;
       compare persisted Outcome/Result with the caller's original intent;

       exact intent match
           -> business reconciliation may treat the current durable state as
              satisfied, while preserving the persisted CompletionEventId;

       intent differs
           -> another completion won;
              surface conflict/current committed state, not caller success;

       in both cases do not invoke completion again and do not allocate another
       identity

Completed without CompletionEventId
    -> fail closed as incompatible/corrupt durable state;
       do not fabricate an identity or completion message
```

`CompleteAsync` does not promise idempotent reconciliation of a blindly replayed
command in Phase 9c. Stable MessageId retry semantics apply to delivery of an
already durable Outbox fact, not to a new producer command invocation. A
committed `CompletionEventId` is immutable and is the only identity of that
completion fact.

Intent comparison is exact persisted semantics:

```text
Outcome: ordinal equality after the same canonical outcome normalization used
         by completion validation

Result:  both null, or exact ordinal TypeId + nullable SchemaRef value equality
         + exact ordinal JsonPayload
```

No semantic JSON reformatting, object deserialization, caller-owned command ID,
or inferred ownership is introduced. A matching value proves state satisfaction,
not cryptographic ownership of the ambiguous caller attempt.

No synchronous `ILocalEventBus.PublishAsync` occurs on the producer path.

### 11.2 Typed delivery

The HumanTask module registers one explicit handler for
`crest.humantask.completed/v1`.

It:

1. verifies ContractId/EventName/version and `MessageId == EventId`;
2. deserializes with the exact generated HumanTask JSON metadata;
3. verifies payload tenant/key identity against Outbox metadata;
4. verifies required-consumer metadata matches payload correlation exactly;
5. resolves and executes every persisted required consumer through the exact
   semantic registry; for `WorkflowKey != null` this includes exactly
   `crest.workflow.humantask-continuation/v1`;
6. invokes the generic compile-time typed
   `ILocalEventDispatcher.DispatchAsync<HumanTaskCompletedEvent>` path for
   remaining optional compatibility handlers under a bounded best-effort lane;
7. returns Delivered after the contract handler has validated the fact and every
   persisted required consumer has returned Accepted/Duplicate.

Required-consumer rules are closed:

```text
WorkflowKey != null
    -> exactly the Workflow continuation consumer ID is persisted
    -> the consumer must be composed and execute successfully before Ack

WorkflowKey == null
    -> no Workflow continuation consumer is required
    -> other explicitly persisted required consumer IDs, if any, still apply
```

The current zero-handler success behavior of typed LocalEvent dispatch is never
used to prove Workflow continuation. Missing required-consumer composition is
caught by the active composition probe before Claim and therefore cannot Ack or
consume AttemptCount.

Reliable downstream Ack authority is exactly:

```text
Outbox ContractId handler
    + persisted RequiredConsumerIds
```

Generic `ILocalEventHandler<HumanTaskCompletedEvent>` registrations are not
implicit reliable consumers. Their absence, exception, timeout, or cancellation
under the compatibility-lane budget is logged safely and does not cause Outbox
Retry/DeadLetter or block Ack. Host/dispatcher cancellation may still prevent
the Ack mutation and cause normal redelivery; that is dispatcher ownership, not
optional-handler authority. A
handler required for business correctness must migrate to a stable required
consumer ID captured on the HumanTask and persisted in the Outbox Integrity.
Adding/removing optional LocalEvent handlers across deployments never changes
the reliable obligation set.

The implementation plan must inventory current first-party handlers, including
Procurement and Activation Review, classify each as required or compatibility,
and migrate every required one to stable consumer registration plus producer-
captured obligation. Merely proving an optional handler duplicate-safe does not
make it a reliable Ack participant.

It never calls the untyped reflection dispatcher and never sends the event back
through `ILocalEventBus`, which would create a second DLQ/retry authority.

### 11.3 Duplicate behavior

The Outbox may invoke required consumers more than once with the same EventId.
Waiting-key absence alone is not duplicate proof.

The Workflow required consumer persists an immutable continuation-acceptance
discriminator containing at least:

```text
Tenant scope
CompletionEventId
HumanTaskKey
WorkflowKey
accepted Workflow from/to revision
```

The discriminator is committed atomically with:

```text
Workflow Suspended -> Running
WaitingHumanTaskKey -> null
workflow.resumed prepared AuditEnvelope Outbox append
```

It must survive later Workflow state transitions long enough to reconcile any
redelivery of the HumanTask Outbox message. A dedicated receipt row or an
equivalent closed persisted Workflow representation is an implementation-plan
choice; an in-memory marker, waiting-key absence, or log entry is insufficient.
V012/schema manifest and both provider implementations must include the chosen
durable representation.

Required-consumer observations are:

```text
waiting correlation present + exact request
    -> CAS resume + discriminator commit
    -> Accepted

exact CompletionEventId + exact HumanTask/Workflow identity already persisted
    -> Duplicate

same correlation with a different CompletionEventId, or same CompletionEventId
with different durable identity
    -> Conflict; never Duplicate

waiting correlation absent + no exact discriminator
    -> fail closed; never infer success
```

Crash or commit-response loss after the resume transaction but before consumer
return is reconciled through the exact discriminator. A race still has at most
one accepted resume transition.

Accepted/Duplicate satisfies the persisted consumer obligation. Identity
Conflict is a permanent safe delivery failure; ambiguous resume COMMIT remains
retryable until the exact discriminator is freshly observed.

The reliable consumer obligation ends at Accepted/Duplicate durable resume
acceptance. Post-resume `IWorkflowExecutionRunner.RunAsync` execution and its
process-crash liveness belong to Workflow Runtime. The Workflow consumer may
attempt that work after acceptance, but its success, failure, or non-execution
does not control HumanTask Outbox Ack/Retry/DeadLetter and #25 makes no durable
Workflow scheduler claim.

### 11.4 Retirement of delivery-failure business state

After cutover:

- `CompleteAsync` never writes `CompletionDispatchFailed`;
- delivery attempts/errors live only in Outbox state;
- `CompletionDispatchError`, `CompletionDispatchFailedAt`, and
  `CompletionDispatchAttemptCount` are not mutated by the mainline;
- `IHumanTaskCompletionFailurePolicy` is not resolved by the mainline;
- cancellation treats Completed as terminal without a delivery-specific branch.

Compatibility members may remain obsolete for one migration window, but tests
and samples must not use them as the current recovery path.

---

## 12. Golden Mainline B — Workflow Transition to Accountability

### 12.1 Producer candidate and prepared AuditEnvelope

The Workflow module extracts the pure mapping currently in
`WorkflowAccountabilityObserver` into a
`WorkflowAccountabilityEnvelopeFactory`.

For every supported lifecycle transition:

```text
workflow.started
workflow.suspended
workflow.resumed
workflow.completed
workflow.failed
```

the producer allocates lifecycle `EventId` and `AuditId`, builds the typed
`WorkflowLifecycleEvent`, and immediately builds the complete producer-owned
`AuditEnvelope` candidate. Workflow fact meaning is then frozen: Actor, Action,
Target, Outcome, OccurredAt, causality, descriptor context, Runtime references,
and `AuditId` cannot change.

Before opening the Runtime transaction, Workflow calls the Accountability-owned
`IAuditEnvelopePreparer.PrepareAsync(candidate)`. Preparation is the exact
pre-sink sequence extracted from `DefaultAuditRecorder`:

```text
structural + candidate validation
    -> sanitizer
    -> protected-fact comparison
    -> safe-snapshot validation
    -> canonical integrity hash
    -> immutable prepared AuditEnvelope
```

`AuditEnvelopePreparationResult` is an explicit Accepted/Rejected result. An
accepted envelope has non-null, internally consistent `Sanitization` and
`Integrity`; a rejected result contains only existing safe issue codes. The
preparer performs no sink/network write and does not expose `IAuditSink`.

`IAuditRecorder.RecordAsync(candidate)` remains the only ordinary public
recording API. Its implementation composes the same preparer followed by the
same internal sink fan-out; there is no parallel validation/sanitization
implementation and no public prepared-envelope overload.

Inside `CrestCreates.Accountability`, the Accountability-owned Outbox handler
uses an internal `PreparedAuditRecorder`/equivalent trusted component. It is not
exported through `Accountability.Abstractions`, public DI, or friend access for
producer modules. That internal path:

1. validates the safe-envelope shape and structured Integrity metadata;
2. recomputes the canonical hash and requires exact equality;
3. fans the envelope out to the configured sinks;
4. never invokes `IAuditSanitizer` again.

The prepared envelope, not the candidate, is serialized into the Outbox. This
keeps the safe payload and Integrity stable if a retry occurs after a sanitizer
or deployment version change. Public callers cannot manufacture a hash with the
public `IAuditIntegrityHasher` and use it to bypass sanitization because no
public prepared-recording entry exists. Sink ownership remains inside
Accountability rather than leaking into Workflow or Delivery.

`OccurredAt` is allocated once with the transition candidate before the first
durable write. Preparation rejection prevents the transition attempt; sink
availability does not participate in preparation. The fact becomes authoritative
only if the state + Outbox transaction commits. This replaces the Phase 9a
post-save allocation wording so atomic payload construction is possible without
provider time leaking into the domain contract.

### 12.2 Atomic transition

Every Workflow state transition persists:

```text
Workflow state post-image
    + LastLifecycleAuditId = AuditEnvelope.AuditId
    + Outbox message carrying that AuditEnvelope
```

in one existing Runtime transaction.

This applies at the actual write owners:

- initial Workflow Add (`workflow.started`);
- suspension committer (`workflow.suspended`) beside task/receipt/state;
- continuation CAS (`workflow.resumed`);
- runner terminal CAS (`workflow.completed` / `workflow.failed`).

Outbox identity:

```text
ContractId   = crest.accountability.audit-envelope/v1
EventName    = accountability.audit-envelope
EventVersion = 1
MessageId    = AuditEnvelope.AuditId
TenantId     = AuditEnvelope.TenantId
OccurredAt   = AuditEnvelope.OccurredAt
Payload      = prepared safe AuditEnvelope with Sanitization + Integrity
RequiredConsumerIds = []
```

The post-commit lifecycle event uses the same allocated IDs and OccurredAt. A
known commit may notify remaining best-effort observers. Commit unknown does not
fabricate a best-effort notification; the Outbox remains authoritative if the
commit actually succeeded.

### 12.3 Accountability delivery result

The Accountability module registers the explicit typed handler for
`crest.accountability.audit-envelope/v1`.

It verifies message/payload identity, deserializes with
`AccountabilityJsonSerializerContext`, verifies the prepared envelope's
Integrity, and invokes the Accountability-internal prepared recorder with that
exact envelope. Delivery never re-projects the lifecycle event and never
re-runs the sanitizer. `IAuditRecorder` remains the public candidate-only path.

Classification:

| Recorder observation | Delivery decision |
|---|---|
| `Recorded` and every configured sink Accepted/Duplicate | Delivered / Ack |
| `PartiallyRecorded` with only provider failures | Retryable; identical AuditId |
| any sink Conflict | Permanent failure / DeadLetter |
| `Rejected` | Permanent failure / DeadLetter |
| `Failed` without Conflict | Retryable until MaxAttempts |
| `NoSinkConfigured` | composition failure; no message mutation; Host unhealthy |

Ack requires every currently configured sink to have Accepted/Duplicate. One
accepted sink plus one unavailable sink is not treated as full delivery; replay
is safe because accepted sinks return Duplicate for the same `AuditId` and hash.
Because retries reuse the persisted prepared envelope, a sanitizer upgrade
between partial acceptance and retry cannot create a different hash for the
same `AuditId`.

Sink membership is delivery-time composition, not an immutable producer fact;
Phase 9c does not persist `RequiredSinkIds`. Removing a sink ends that sink's
obligation for future attempts. Adding a sink makes it participate in subsequent
attempts, including an already Pending or expired-leased message. In both cases
the persisted prepared envelope, AuditId, and Integrity remain unchanged.

When reliable Workflow Accountability is enabled, generic composition requires
at least one configured `IAuditSink`. Missing sink composition fails startup
before Claim. DI composition is immutable for the worker lifetime, so
`NoSinkConfigured` is unreachable on the supported path after validation. If
observed as an invariant violation, the worker stops after no
Ack/Retry/DeadLetter mutation; it does not continue reclaiming under the same
broken Host. Restarting with repaired composition delivers the existing
message.

`IAuditSink` exposes no durability capability, so generic composition does not
label a sink durable. Phase 9c FullDurable evidence must explicitly compose
`PostgreSqlAuditSink`, restart/recreate the provider, and prove
Accepted/Duplicate persistence. Outbox durability proves the delivery fact;
final sink durability remains an implementation/evidence property of the chosen
sink.

### 12.4 Observer lane remains separate

```text
Reliable accountability lane
    Workflow transaction -> AuditEnvelope Outbox
        -> Accountability-internal prepared recorder

Best-effort notification lane
    known committed transition -> WorkflowLifecycleEventPublisher
        -> non-Accountability observers
```

Best-effort observer failure never changes Workflow state, Outbox state, or
Accountability Ack. Reliable delivery failure never rewrites Workflow business
state.

---

## 13. Dead-Letter Semantics

`DeadLetterAsync` is an atomic terminal update of the Outbox row under the
current valid fence.

It must never perform:

```text
insert independent EF DLQ row
    -> crash
    -> Outbox remains active
```

Terminal state retains:

- original immutable message and Integrity;
- final AttemptCount and fencing generation;
- safe final failure code/time;
- `DeadLetteredAt`.

Existing `DeadLetterMessage`/`IDeadLetterStore` may be adapted as a read-only
diagnostic projection when their field semantics can be represented without a
fake CLR type name. Such a projection:

- does not allocate a new MessageId;
- does not deserialize/re-serialize payload;
- does not control claim eligibility;
- does not mark the Outbox delivered/retried;
- is not required for transaction closure.

Because the current legacy record requires `PayloadTypeFullName`, no Phase 9c
mainline code may populate that field with an assembly-qualified name. Evolving
the legacy diagnostic contract is a separate compatibility task, not a reason
to weaken Outbox AOT rules.

---

## 14. System Invariants

### INV-01 — State and message share one commit

The authoritative Runtime mutation and corresponding Outbox append are visible
together or neither is visible.

### INV-02 — Writer requires the Runtime transaction

The transactional writer cannot open an independent commit and fails closed
without the ambient supported Runtime transaction.

### INV-03 — Commit unknown is not rollback

Fresh observation after an ambiguous COMMIT shows both mutation and message or
neither, never a split pair.

### INV-04 — Message identity is producer-owned

The producer allocates MessageId before the transaction; Stores and dispatch
attempts never replace it.

### INV-05 — Replay is exact and conflict-safe

Same MessageId plus equal full structured Integrity is Duplicate. Any immutable
difference throws `OutboxMessageConflictException`, aborts the producer
transaction, and never overwrites the accepted message. Conflict is not an
ignorable append result.

### INV-06 — Immutable message never changes

Claim, Retry, Ack, DeadLetter, restart, and compatibility projection cannot
mutate message metadata, payload bytes, or Integrity.

### INV-07 — One valid fencing generation owns mutation

Only the current matching, unexpired lease owner/token may Ack, Retry, or
DeadLetter.

### INV-08 — Lease expiry invalidates the old owner

An expired owner is stale even before a new claim. A newer claim increments the
fencing token.

### INV-09 — Publish/Ack ambiguity permits duplicate delivery

Handler success before durable Ack may redeliver the same MessageId. The
framework does not claim exactly-once effects.

### INV-10 — Retry metadata is not fact metadata

Attempts, availability, lease, failure, and terminal timestamps never
participate in logical message Integrity.

### INV-11 — Recovery is durable

Pending, retry-due, and expired-lease PostgreSQL records are claimable after a
fresh process/provider restart.

### INV-12 — Terminal states stay terminal

Delivered and DeadLettered records are not normally claimed, retried, reset, or
deleted by Phase 9c.

### INV-13 — Payload dispatch is explicit and AOT-safe

ContractId selects one explicitly registered typed handler; payload uses exact
generated JSON metadata. Contract/required-consumer registry metadata is cached,
scoped instances are not. No runtime CLR type reconstruction exists.

### INV-14 — HumanTask business state excludes delivery failure

Completion delivery failure changes only Outbox state, never a Completed
HumanTask's lifecycle status or output/outcome.

### INV-15 — Workflow Accountability is not best-effort observer work

The prepared safe Workflow `AuditEnvelope`, including its Integrity, is appended
with the committed transition. Delivery validates and fans out that frozen
envelope without re-running the sanitizer; lifecycle observer success is
irrelevant to reliable Ack.

### INV-16 — Control Plane writes are not implicitly enlisted

Sharing the provider kernel never creates an Outbox message without an explicit
producer append.

### INV-17 — Dead letter is one Outbox terminal transition

An external/legacy DLQ write cannot be the atomic reliability authority.

### INV-18 — No ordering overclaim

Deterministic claim tie-breaks do not create a global, tenant, aggregate, or
ContractId delivery-order guarantee.

### INV-19 — Provider tiers remain explicit

InMemory proves semantics only. PostgreSQL proves durability, restart,
migration, crash, and NativeAOT.

### INV-20 — NativeAOT requires publish, link, and execution

Analyzer, generated metadata, unit tests, or publish-only success do not close
Phase 9c.

### INV-21 — Every claim consumes handler-attempt budget

AttemptCount is the claim-generation count. Claims through MaxAttempts may
invoke the handler; later claims are terminalization-only and never invoke it.
Restart and claim-before-handler crashes do not reset this budget.

### INV-22 — Composition failure preserves durable facts

Missing active handler/required-consumer or required sink composition fails
health/startup before normal claim and never converts a valid message to
DeadLettered. Atomic Claim rechecks active requirements without mutation.
Repairing composition makes the unchanged message deliverable.

### INV-23 — HumanTask commit unknown requires observation

A caller cannot blindly replay completion after ambiguous COMMIT. Fresh
Created/Assigned permits a new attempt. Completed with CompletionEventId proves
one winner, not caller ownership; exact intent comparison distinguishes satisfied
state from a different concurrent winner without creating another identity.

### INV-24 — Sink membership is delivery-time composition

Sink additions/removals affect later attempts without changing AuditId,
prepared AuditEnvelope bytes, or Integrity. Phase 9c persists no required sink
set.

### INV-25 — Workflow continuation is an explicit Ack obligation

A Workflow-correlated HumanTask fact persists the stable Workflow continuation
consumer ID. Ack requires that exact consumer to be composed and complete;
zero optional LocalEvent handlers is never continuation evidence. Standalone
HumanTasks persist no Workflow continuation obligation. Consumer completion is
the durable resume transition plus exact applied CompletionEventId
discriminator; waiting-key absence alone is insufficient, and post-resume
Workflow execution liveness is not part of Ack.

### INV-26 — Prepared Audit recording is a trusted internal entry

Public `IAuditRecorder` accepts candidates and always executes preparation.
Only Accountability-internal code may validate/fan out a prepared envelope;
ordinary callers cannot use a public hasher to bypass sanitization.

### INV-27 — Exact terminal replay is idempotent

Exact final-fence Ack/DeadLetter response-loss replay returns AlreadyApplied and
cannot reopen or mutate terminal state. Different-fence, different-failure, or
cross-terminal replay fails closed.

### INV-28 — Reliable downstream obligations are persisted and closed

Only the ContractId handler and immutable RequiredConsumerIds may control
Outbox Ack/Retry/DeadLetter. Generic LocalEvent compatibility handlers are
bounded best-effort; any business-critical consumer must have a stable persisted
ID.

### INV-29 — Claim composition failure has one typed meaning

Unsupported active ContractId/RequiredConsumerId always throws
`OutboxCompositionException` before mutation. Infrastructure/provider failure
never uses that type and composition failure is never transient retry.

### INV-30 — Sink durability is evidence-specific

Reliable Workflow Accountability generically requires at least one configured
sink. Only FullDurable evidence with `PostgreSqlAuditSink` may claim sink
durability; `IAuditSink` gains no capability surface.

---

## 15. Case Matrix

### 15.1 Append and atomicity

| ID | Case | Expected |
|---|---|---|
| A01 | append in Runtime transaction | Accepted and visible after commit |
| A02 | append without ambient transaction | fail before mutation |
| A03 | exact replay | Duplicate; state unchanged |
| A04 | conflicting replay | conflict exception; producer transaction aborts; original unchanged |
| A05 | state update + append known commit | both visible |
| A06 | state update + append rollback | neither post-state nor message |
| A07 | commit acknowledgement unknown | fresh observation both or neither |
| A08 | append failure | authoritative state rolls back |
| A09 | same MessageId in another tenant | conflict exception; producer transaction aborts |
| A10 | Control Plane Save | no Runtime Outbox enlistment |
| A11 | accepted append initial times | CreatedAt/AvailableAt/UpdatedAt use one provider-time observation |
| A12 | same MessageId changes required consumer set | conflict exception; producer transaction aborts |

### 15.2 Claim, lease, and fencing

| ID | Case | Expected |
|---|---|---|
| L01 | claim due Pending | one leased generation |
| L02 | claim not-yet-due Pending | not selected |
| L03 | two workers race | one active owner/token |
| L04 | lease expires | newer generation may claim |
| L05 | expired old owner Ack | StaleFence; no mutation |
| L06 | expired old owner Retry | StaleFence; no mutation |
| L07 | expired old owner DeadLetter | StaleFence; no mutation |
| L08 | old generation after new claim | all mutations fenced |
| L09 | valid Ack | Delivered terminal |
| L10 | valid Retry | Pending with provider-clock delay |
| L11 | valid DeadLetter | DeadLettered terminal |
| L12 | Delivered/DeadLettered claim | never selected |
| L13 | pending unregistered ContractId | Host/Claim composition failure; no lease or attempt mutation |
| L14 | exact final-fence Ack replay | AlreadyApplied; Delivered unchanged |
| L15 | exact final-fence/failure DeadLetter replay | AlreadyApplied; DeadLettered unchanged |
| L16 | terminal replay with different fence | StaleFence or TerminalConflict; unchanged |
| L17 | cross-terminal or changed-failure replay | TerminalConflict; unchanged |

### 15.3 Failure and recovery

| ID | Case | Expected |
|---|---|---|
| R01 | crash after commit before claim | Pending recovered |
| R02 | crash after claim before handler | expired lease recovered |
| R03 | handler transient failure | same MessageId retried |
| R04 | handler success then process dies before Ack | same MessageId may redeliver |
| R05 | Ack commits but response is lost | Delivered remains terminal |
| R06 | permanent payload/data-contract violation | DeadLettered |
| R07 | retryable failure reaches MaxAttempts | DeadLettered |
| R08 | restart with retry not due | remains unclaimable |
| R09 | restart after retry due | claimable |
| R10 | caller/Host cancellation during handler | no stale state mutation; lease recovers |
| R11 | repeated claim-before-handler crashes | every claim consumes budget |
| R12 | claim count exceeds MaxAttempts | no handler invocation; fenced budget-exhausted DeadLetter |
| R13 | restart after consumed attempts | attempt budget is not reset |

### 15.4 HumanTask golden mainline

| ID | Case | Expected |
|---|---|---|
| H01 | completion known commit | Completed + Outbox together |
| H02 | completion rollback | prior task + no message |
| H03 | completion commit unknown | both/neither; caller must observe before another command |
| H04 | delivery dependency failure | task remains Completed; Outbox retries |
| H05 | duplicate delivery | one Workflow continuation transition |
| H06 | crash after completion before dispatch | durable Workflow resume is eventually accepted |
| H07 | poison completion payload | task remains Completed; message DeadLettered |
| H08 | pre-existing CompletionDispatchFailed | deployment preflight blocks silent cutover |
| H09 | observation is Completed after commit unknown | persisted winner's CompletionEventId preserved; no second completion |
| H10 | observation is Created/Assigned after commit unknown | caller may issue a new completion attempt |
| H11 | blind completion replay after commit unknown | unsupported; cannot allocate second committed identity |
| H12 | observed Completed lacks CompletionEventId | fail closed; do not fabricate message identity |
| H13 | Workflow-correlated completion | required continuation consumer persisted and must succeed before Ack |
| H14 | Workflow continuation consumer absent | composition failure; no Ack or attempt mutation |
| H15 | zero optional LocalEvent handlers for correlated task | cannot substitute for required continuation consumer |
| H16 | standalone completion | no Workflow continuation consumer required |
| H17 | Completed observation differs from ambiguous caller intent | concurrent winner conflict/current state; not caller success |
| H18 | Completed observation matches caller intent | state may be treated satisfied without claiming caller ownership |
| H19 | crash after resume/discriminator commit before consumer return | redelivery proves exact CompletionEventId Duplicate and may Ack |
| H20 | waiting cleared but different/no applied CompletionEventId | Conflict/fail closed; never Duplicate |
| H21 | resume accepted but post-resume RunAsync absent/fails | required consumer succeeds; Outbox may Ack |
| H22 | optional LocalEvent compatibility handler fails | safe log only; Outbox Ack obligation unchanged |
| H23 | business-critical completion consumer | stable ID captured at task creation and persisted in Outbox |

### 15.5 Workflow Accountability golden mainline

| ID | Case | Expected |
|---|---|---|
| W01 | each five lifecycle transitions commits | matching state + prepared AuditEnvelope message |
| W02 | state Store failure | no Accountability message |
| W03 | best-effort observer failure | message/state unaffected |
| W04 | Accountability sink unavailable | retry same AuditId |
| W05 | partial sink acceptance | accepted sink Duplicate on retry; remaining sink retried |
| W06 | Accountability Conflict | DeadLettered with safe code |
| W07 | duplicate delivery | original AuditId preserved; no new fact identity |
| W08 | lifecycle mapping code changes after append | persisted AuditEnvelope remains unchanged |
| W09 | sanitizer version changes after partial delivery | retry preserves original safe payload and Integrity |
| W10 | ordinary public Audit recording | always executes preparation/sanitization |
| W11 | prepared Audit Outbox delivery | internal validation/fan-out; sanitizer not re-run |
| W12 | ordinary caller seeks prepared recording entry | no public API/DI/friend bypass exists |
| W13 | reliable Accountability generic composition | at least one configured sink; no generic durability claim |
| W14 | FullDurable Accountability evidence | PostgreSqlAuditSink survives restart and returns Duplicate |

### 15.6 Composition, upgrade, and sink membership

| ID | Case | Expected |
|---|---|---|
| C01 | required handler missing | startup unhealthy; no message mutation |
| C02 | reliable Accountability has no configured sink | startup unhealthy; no DeadLetter |
| C03 | composition repaired | existing Pending message delivers without reset |
| C04 | sink removed before retry | removed sink obligation ends; envelope unchanged |
| C05 | sink added before retry | new sink participates; envelope unchanged |
| C06 | legacy completion-failure row exists | V012/provider preflight fails safely; no Runtime enumeration API |
| C07 | scoped handler registration | metadata cached; instance resolved only from delivery scope |
| C08 | active message has unsupported ContractId | Host unhealthy; no Claim/mutation |
| C09 | active message has unsupported required consumer | Host unhealthy; no Claim/mutation |
| C10 | terminal message handler/consumer removed | current composition remains valid |
| C11 | unsupported active registration restored | unchanged message becomes deliverable |
| C12 | unsupported fact appears between readiness and Claim | atomic Claim guard rejects without mutation |
| C13 | atomic Claim sees unsupported requirement | throws OutboxCompositionException; no mutation |
| C14 | database unavailable during Claim | infrastructure failure, never OutboxCompositionException |
| C15 | active legacy standalone task requires migrated consumer but lacks obligation | cutover blocks or row is explicitly reconciled; never silent optional delivery |

### 15.7 NativeAOT and provider evidence

| ID | Case | Expected |
|---|---|---|
| N01 | V012 apply/reapply/validate | exact schema and checksum |
| N02 | fresh provider claims pending/retry/expired lease | recovery passes |
| N03 | native HumanTask payload dispatch | durable Workflow resume acceptance succeeds |
| N04 | native AuditEnvelope dispatch | PostgreSqlAuditSink Accepted/Duplicate persistence |
| N05 | native crash windows | state/message and lease recovery invariants hold |
| N06 | binary/static AOT guard | no reflection/type-name payload reconstruction |
| N07 | native active composition probe | ContractId/required-consumer requirements validated |
| N08 | native correlated HumanTask delivery | required continuation executes before Ack |
| N09 | native optional LocalEvent failure | logged best-effort; Ack unaffected |

---

## 16. TDD Test Architecture

### 16.1 Runner-free shared contract kit

```text
tests/Shared/CrestCreates.Runtime.Delivery.Testing/
  Contracts/
    IOutboxContractDriver
  Cases/
    OutboxAppendContractCases
    OutboxAtomicityContractCases
    OutboxDispatchContractCases
    OutboxFencingContractCases
    OutboxAttemptBudgetContractCases
    OutboxInitialTimeContractCases
    OutboxCompositionContractCases
    OutboxTerminalReplayContractCases
  Assertions/
    OutboxContractAssertions
  Fixtures/
    OutboxContractData
```

It references only public provider-neutral contracts, contains static async
cases, has no xUnit/Npgsql/Testcontainers dependency, and exposes no provider
transaction.

Both providers wrap the same cases:

```text
CrestCreates.Runtime.Persistence.InMemory.Tests
CrestCreates.Runtime.Persistence.PostgreSql.Tests
```

The existing runner-free
`tests/Shared/CrestCreates.Runtime.Persistence.Testing` kit gains closed
`WorkflowContinuationAcceptanceContractCases` for atomic resume/discriminator,
exact Duplicate, different-ID Conflict, and commit-response-loss observation.
InMemory and PostgreSQL invoke those same cases; this behavior is not copied
into provider-specific assertions.

The driver may:

- execute a producer transaction;
- append/read through test-side observations;
- create due/not-due messages;
- claim explicit registered ContractIds as named owners;
- inspect distinct active ContractId/RequiredConsumerId requirements;
- exercise repeated claim generations and terminalization-only claims;
- advance a semantic test clock where the provider supports it;
- Ack/Retry/DeadLetter using returned leases;
- return detached snapshots.

Restart/process-kill/schema behavior remains PostgreSQL-specific evidence.

### 16.2 PostgreSQL fixture and crash worker

Extend the existing isolated-schema fixture and CrashWorker. Required windows:

```text
CW01 crash before producer COMMIT
CW02 commit then process exits before application acknowledgement
CW03 commit then process exits before dispatcher claim
CW04 claim then process exits before handler
CW04B repeat claim-before-handler process exits through MaxAttempts, then
      recover with terminalization-only claim and zero handler invocation
CW05 handler side effect succeeds then process exits before Ack
CW06 retry state commits then process exits
CW07 Workflow resume + applied CompletionEventId commits, then process exits
     before required-consumer return/Outbox Ack
```

The parent waits for the worker/backend connection to exit, creates a fresh
provider/process, and observes only durable public/provider-test-driver state.
An exception hook is supplementary and does not replace subprocess evidence.

### 16.3 Evidence ownership

- shared kit: semantic provider parity;
- PostgreSQL integration: SQL concurrency, exact schema, restart, provider
  clock, commit unknown;
- Workflow/HumanTask suites: producer mapping and duplicate-safe domain behavior;
- Procurement acceptance: real approval chain without old failure policy;
- CrashWorker: process loss windows;
- AOT Host/Fixture: publish-link-run against real PostgreSQL.

---

## 17. Normative Acceptance Test Skeleton

Names below are requirements ledger entries. The implementation plan may add
tests but cannot silently rename/remove them.

### 17.1 Contracts and architecture

```text
OutboxContracts_Should_Not_ExposeProviderTypes
RuntimeDeliveryAbstractions_Should_Not_ReferenceDomainOrProviderImplementations
RuntimeDeliveryRuntime_Should_Not_ReferenceHumanTaskWorkflowOrAccountability
ProducerModules_Should_Not_ReferenceOutboxDispatchStore
TransactionalOutboxWriter_Should_FailWithoutAmbientRuntimeTransaction
OutboxMainline_Should_Not_UseRuntimeTypeNamesOrReflectionSerialization
OutboxHandlerRegistry_Should_RejectDuplicateContractId
OutboxHandlerRegistry_Should_CacheMetadata_NotScopedInstances
ScopedOutboxHandler_Should_BeResolved_FromDeliveryScope
RequiredConsumerRegistry_Should_CacheMetadata_NotScopedInstances
OutboxPayload_Should_RequireGeneratedJsonTypeInfo
ExistingEventBusAndDlq_Should_Not_BeOutboxAuthority
ControlPlane_Save_Should_Not_Enlist_Runtime_Outbox
Missing_RequiredContractHandler_Should_Fail_Composition_Without_MessageMutation
ActiveMessage_WithUnsupportedContract_Should_Fail_Composition
UnsupportedActiveContract_Should_Remain_Unmodified
TerminalMessage_Should_Not_Require_CurrentHandlerRegistration
OutboxCompositionException_Should_Not_ExposeProviderDetails
IAuditSink_Should_Not_GainDurabilityCapability
IAuditRecorder_Should_Not_Expose_PreparedEnvelopeBypass
PreparedAuditRecording_Should_Be_AccountabilityInternal
```

### 17.2 Append and atomicity

```text
State_Commit_Should_Atomically_Create_Outbox_Message
Rolled_Back_State_Should_Not_Create_Outbox_Message
CommitUnknown_Should_Never_Expose_Split_State_And_Outbox
OutboxAppendFailure_Should_Rollback_RuntimeMutation
Append_Replay_With_SameIntegrity_Should_Be_Duplicate
OutboxConflict_Should_Abort_RuntimeTransaction
IgnoredConflict_Should_Not_Be_Possible_On_CanonicalProducerPath
Duplicate_Should_Not_Abort_RuntimeTransaction
Append_Duplicate_Should_Not_Reset_DeliveryState
SameMessageId_InDifferentTenant_Should_Abort_RuntimeTransaction
AcceptedAppend_Should_Use_ProviderClock_ForInitialAvailability
RequiredConsumerIds_Should_Participate_In_OutboxIntegrity
ImmutablePayload_Should_Not_Change_AfterCallerMutation
Retry_Should_Not_Mutate_LogicalPayload
```

### 17.3 Claim, lease, fencing, and terminal state

```text
Pending_Message_Should_Be_Claimed_With_FirstFence
NotYetDue_Message_Should_Not_Be_Claimed
Concurrent_Dispatchers_Should_Respect_FencingToken
ExpiredLease_Should_Allow_NewerGeneration
Expired_Owner_Should_Not_Acknowledge_NewerLease
Stale_Owner_Should_Not_Schedule_Retry
Stale_Owner_Should_Not_DeadLetter
Valid_Owner_Should_Acknowledge_To_Delivered
Retry_Should_Use_ProviderClock_And_Preserve_MessageId
Poison_Message_Should_Move_To_DeadLetter
Delivered_Message_Should_Not_Be_Claimed
DeadLettered_Message_Should_Not_Be_Claimed
DeadLetter_Should_Be_One_OutboxTerminalTransition
UnregisteredContract_Should_Not_BeClaimed_OrConsumeAttemptBudget
Repeated_ClaimCrash_Should_Consume_AttemptBudget
AttemptBudgetExhausted_Should_DeadLetter_Without_HandlerInvocation
Ack_Replay_With_ExactTerminalFence_Should_Be_AlreadyApplied
DeadLetter_Replay_With_ExactTerminalFence_Should_Be_AlreadyApplied
TerminalReplay_With_DifferentFence_Should_Be_StaleOrConflict
AlreadyApplied_Should_Not_Reopen_TerminalState
UnsupportedActiveRequirement_Should_Throw_ProviderNeutralCompositionFailure
CompositionFailure_Should_Not_Be_Classified_As_TransientStoreFailure
```

### 17.4 Recovery and ambiguity

```text
Pending_Message_Should_Be_Recovered_After_Restart
RetryDue_Message_Should_Be_Recovered_After_Restart
ExpiredLease_Should_Recover_After_Restart
Publish_ResponseLoss_Should_Redeliver_SameMessageId
Ack_ResponseLoss_AfterCommit_Should_Remain_Delivered
Crash_BeforeProducerCommit_Should_ExposeNeitherStateNorOutbox
Crash_AfterProducerCommit_Should_RecoverPendingMessage
Crash_AfterClaim_Should_RecoverExpiredLease
Crash_AfterHandlerBeforeAck_Should_PermitSameMessageRedelivery
Restart_Should_Not_Reset_AttemptBudget
CompositionRecovery_Should_Allow_ExistingPendingMessage_To_Deliver
RestoredContractRegistration_Should_Allow_PendingDelivery
```

### 17.5 HumanTask mainline

```text
HumanTask_Completion_Should_Commit_Completed_And_Outbox
HumanTask_CompletionRollback_Should_ExposeNeitherPostStateNorOutbox
HumanTask_Delivery_Failure_Should_Not_Create_CompletionDispatchFailed
HumanTask_Completion_Should_Not_Publish_Synchronously
HumanTask_OutboxHandler_Should_Use_TypedLocalEventDispatch
Duplicate_HumanTask_Delivery_Should_Not_Duplicate_Continuation
HumanTask_CrashAfterCommit_Should_Eventually_Accept_WorkflowResume
HumanTask_PoisonDelivery_Should_Preserve_CompletedBusinessState
Legacy_CompletionDispatchFailed_Should_Block_SilentCutover
Legacy_CompletionDispatchFailed_Preflight_Should_Be_V012ProviderOwned
Legacy_ActiveHumanTask_RequiredConsumerGap_Should_BlockSilentCutover
HumanTask_CommitUnknown_Should_Require_Observation_Before_CommandReplay
Completed_HumanTask_AfterCommitUnknown_Should_Preserve_OriginalCompletionEventId
CommitUnknown_Recovery_Should_Not_Create_SecondCompletionIdentity
Completed_HumanTask_WithoutCompletionEventId_Should_FailClosed
WorkflowCorrelated_HumanTask_Should_Require_ContinuationConsumer
Missing_WorkflowContinuationConsumer_Should_Not_Ack_Outbox
Missing_WorkflowContinuationConsumer_Should_Fail_Composition
Standalone_HumanTask_Should_Not_Require_WorkflowContinuationConsumer
Zero_LocalEventHandlers_Should_Not_Prove_WorkflowContinuation
CommitUnknown_CompletedObservation_Should_Not_ProveCallerOwnership
CommitUnknown_DifferentCompletionWinner_Should_Not_BeReported_AsCallerSuccess
CommitUnknown_ConcurrentWinner_Should_Not_Create_SecondCompletion
Crash_After_ResumeCommit_Before_ConsumerReturn_Should_Reconcile_SameCompletion
Duplicate_Continuation_Should_Prove_AppliedCompletionIdentity
Different_CompletionId_Should_Not_Be_Treated_As_Duplicate
ReliableContinuationAck_Should_Not_Require_PostResume_WorkflowExecution
Optional_LocalEventHandler_Failure_Should_Not_Block_OutboxAck
Optional_LocalEventHandler_Should_Not_Be_ImplicitReliableConsumer
ReliableAck_Should_Depend_Only_On_PersistedConsumerObligations
Required_BusinessConsumer_Should_Require_StableConsumerId
FirstParty_RequiredCompletionHandlers_Should_Use_StableConsumerIds
Procurement_Mainline_Should_Not_Register_CompletionFailurePolicy
```

### 17.6 Workflow Accountability mainline

```text
Workflow_Started_Should_Commit_Accountability_Fact
Workflow_Suspended_Should_Commit_Accountability_Fact
Workflow_Resumed_Should_Commit_Accountability_Fact
Workflow_Completed_Should_Commit_Accountability_Fact
Workflow_Failed_Should_Commit_Accountability_Fact
Workflow_StateFailure_Should_Not_Append_AccountabilityFact
Workflow_BestEffortObserverFailure_Should_Not_Change_Outbox
Workflow_Accountability_Should_Persist_Final_AuditEnvelope_NotLifecycleEvent
Workflow_Accountability_Should_Persist_PreparedEnvelope_WithIntegrity
Workflow_AccountabilityObserver_Should_Not_Remain_ReliableWritePath
Duplicate_Accountability_Delivery_Should_Preserve_AuditId
Partial_AccountabilitySinkFailure_Should_Retry_Until_AllAccepted
Accountability_Retry_AfterSanitizerUpgrade_Should_Preserve_Integrity
Accountability_Preparation_Should_Be_SinglePath_ForImmediateAndOutboxRecording
Workflow_Should_Not_Reference_IAuditSink
Accountability_OutboxHandler_Should_Be_Owned_By_Accountability
OutboxPreparedAuditPath_Should_Not_Invoke_Sanitizer
OrdinaryAuditRecording_Should_Always_Invoke_Preparation
Accountability_Conflict_Should_DeadLetter
Missing_RequiredAccountabilitySink_Should_Not_DeadLetter_Message
ReliableWorkflowAccountability_Should_Require_AtLeastOneConfiguredSink
FullDurableAccountability_Should_Use_PostgreSqlAuditSink
Removed_AccountabilitySink_Should_End_FutureAttemptObligation
Added_AccountabilitySink_Should_Participate_In_SubsequentAttempt
BestEffort_WorkflowObservers_Should_Not_Participate_In_ReliableAck
```

### 17.7 Migration and NativeAOT

```text
V012_Should_Extend_Existing_RuntimeMigrationCatalog
V012_Should_Validate_ExactOutboxSchema
V012_Should_Persist_WorkflowContinuationAcceptanceDiscriminator
V012_Should_Reject_ChangedAppliedChecksum
V012_Should_Reject_OutboxSchemaDrift
ActiveRequirementsProbe_Should_Pass_SharedContractKit
WorkflowContinuationAcceptance_Should_Pass_SharedContractKit
AtomicClaim_Should_Reject_UnsupportedActiveRequirement_WithoutMutation
PostgreSqlOutbox_Should_Pass_SharedContractKit
InMemoryOutbox_Should_Pass_SharedContractKit
Persisted_HumanTaskPayload_Should_Dispatch_Under_NativeAot
Required_WorkflowContinuationConsumer_Should_Execute_Under_NativeAot
WorkflowContinuationAcceptance_Should_Reconcile_Under_NativeAot
Optional_LocalEventFailure_Should_Not_Block_NativeOutboxAck
Persisted_AuditEnvelope_Should_Dispatch_Under_NativeAot
ActiveCompositionProbe_Should_Execute_Under_NativeAot
PostgreSqlOutboxFixture_Should_PublishLinkAndRunNativeBinary
NativeBinary_Should_Emit_ReliableDeliverySentinel
```

### 17.8 Invariant-to-test ledger

| Invariant | Primary evidence |
|---|---|
| INV-01 | state commit/rollback/append-failure cases |
| INV-02 | writer-without-ambient case |
| INV-03 | commit-unknown + crash response-loss cases |
| INV-04 | HumanTask CompletionEventId and Workflow AuditId cases |
| INV-05 | append duplicate/conflict cases |
| INV-06 | caller mutation + retry payload cases |
| INV-07 | concurrent/stale-owner cases |
| INV-08 | expired-owner three-mutation matrix |
| INV-09 | handler-before-Ack crash case |
| INV-10 | retry immutable-payload case |
| INV-11 | pending/retry/expired restart cases |
| INV-12 | terminal-not-claimed cases |
| INV-13 | generated JSON + reflection guard + native dispatch |
| INV-14 | HumanTask delivery failure/poison cases |
| INV-15 | five Workflow transition + observer separation cases |
| INV-16 | Control Plane non-enlistment case |
| INV-17 | atomic DeadLetter case |
| INV-18 | concurrency order non-claim architecture assertion |
| INV-19 | both shared runners + provider capability tests |
| INV-20 | native publish-link-run fixture |
| INV-21 | repeated claim-crash, over-budget no-handler, restart cases |
| INV-22 | active handler/consumer probe, missing sink, atomic Claim guard, and repaired-composition cases |
| INV-23 | HumanTask commit-unknown intent/winner reconciliation cases |
| INV-24 | sink add/remove across partial-delivery retry cases |
| INV-25 | continuation acceptance discriminator/crash/identity cases |
| INV-26 | public Audit API/internal prepared-path architecture cases |
| INV-27 | exact Ack/DeadLetter terminal replay matrix |
| INV-28 | optional LocalEvent failure + persisted required-consumer cases |
| INV-29 | typed composition exception vs infrastructure failure cases |
| INV-30 | configured-sink composition + PostgreSqlAuditSink evidence cases |

---

## 18. Red-Green-Review Implementation Slices

This is sequencing guidance for the later implementation plan, not authorization
to code from an unreviewed draft.

### Slice 1 — Freeze contracts and inactive acceptance ledger

Red:

- exact normative test names and evidence ledger;
- dependency/provider/reflection guards;
- registry metadata/scoped-lifetime, active-requirement, required-consumer, and
  public Audit trusted-entry guards;
- exact `OutboxCompositionException` and unchanged `IAuditSink` surface guards;
- shared driver interfaces with inactive wrappers.

Green:

- Delivery Abstractions contract shapes only.

Review:

- no broad `IOutboxStore`;
- no provider handle or EventBus rewrite;
- ContractId metadata is cached without resolving scoped handler instances;
- public `IAuditRecorder` remains candidate-only;
- composition mismatch has one typed provider-neutral exception;
- public contracts express all ownership/ambiguity states.

### Slice 2 — Immutable message and InMemory append semantics

Red:

- bounds, snapshot, generated JSON, RequiredConsumerIds Integrity,
  duplicate/conflict-abort, and initial provider-time cases.

Green:

- message factory/canonical projector;
- InMemory transactional participant.

Review:

- exact immutable field closure;
- required-consumer obligations are immutable and integrity-protected;
- no delivery metadata in Integrity;
- conflicting append cannot be ignored by a canonical producer;
- no reflection fallback.

### Slice 3 — InMemory dispatch semantics

Red:

- due/claim/race/expiry/stale mutation/retry/terminal matrix;
- composition exception versus transient Store failure;
- exact Ack/DeadLetter AlreadyApplied terminal-replay matrix;
- repeated claim-crash budget, over-budget no-handler, and restart persistence.

Green:

- InMemory dispatch Store, retry policy, typed registry, bounded dispatcher.

Review:

- one fence authority;
- exact terminal replay is provider-parity behavior;
- every claim consumes budget and over-budget claims cannot invoke handlers;
- provider clock ownership;
- no ordering/exactly-once claim.

### Slice 4 — PostgreSQL V012 and shared parity

Red:

- migration/schema/collation/check/index drift;
- provider-owned legacy completion-failure preflight;
- durable Workflow continuation-acceptance discriminator representation;
- active ContractId/RequiredConsumerId probe and atomic Claim guard parity;
- shared kit wrappers;
- real concurrent two-provider claims.

Green:

- V012/schema manifest;
- PostgreSQL writer/dispatch Store;
- base provider DI.

Review:

- one existing provider kernel;
- resume discriminator participates in exact schema/provider parity;
- unsupported active requirements cannot be leased or mutate AttemptCount;
- no HumanTask status-enumeration API added for migration preflight;
- immutable columns never updated;
- no independent append transaction.

### Slice 5 — Phase 9b deferred enlistment and commit unknown

Red:

- neutral state+append commit/rollback;
- commit-unknown both-or-neither;
- append conflict rolls back state;
- HumanTask observation-before-command-replay and concurrent-winner intent
  comparison cases.

Green:

- minimum producer composition seams.

Review:

- no automatic delegate replay;
- no blind HumanTask completion replay, false caller ownership, or second
  completion identity;
- provider exceptions remain internal.

### Slice 6 — Claim worker, retry, Ack loss, restart, DeadLetter

Red:

- crash/restart windows CW03-CW06;
- continuation-acceptance crash window CW07;
- missing active handler/required-consumer/sink composition and recovery;
- unsupported requirement appearing between readiness and Claim;
- terminal dead-letter atomicity.

Green:

- hosted worker and failure classifier.

Review:

- cancellation leaves lease recovery path;
- deployment composition cannot DeadLetter a valid durable fact;
- legacy DLQ is projection only.

### Slice 7 — HumanTask cutover

Red:

- state+event atomicity;
- no synchronous publish/failure state;
- required Workflow continuation consumer, zero-LocalEvent-handler, standalone,
  exact applied-identity duplicate, optional-handler failure, and standalone
  cases;
- legacy-row preflight.

Green:

- transactional completion producer;
- generated HumanTask payload context;
- required-consumer metadata/registry and typed Local Event compatibility path;
- Workflow continuation consumer moved out of generic LocalEvent enumerable;
- durable resume acceptance discriminator committed with Workflow CAS;
- optional LocalEvent lane bounded and excluded from Ack authority;
- required first-party business consumers migrated to stable IDs captured at
  HumanTask creation;
- sample/first-party duplicate-safety fixes.

Review:

- old failure policy is absent from production mainline;
- correlated completion cannot Ack without executed continuation consumer;
- waiting-key absence alone cannot prove duplicate continuation;
- post-resume RunAsync liveness is not an Outbox Ack condition;
- delivery failure never mutates business status.

### Slice 8 — Workflow Accountability cutover

Red:

- five transition atomic cases;
- partial sink retry/conflict and sink membership add/remove;
- configured-sink composition and PostgreSqlAuditSink FullDurable evidence;
- best-effort observer separation.

Green:

- pure Workflow candidate factory;
- one Accountability preparation pipeline shared by immediate recording and
  Outbox production;
- prepared-envelope validation/fan-out path that does not re-run sanitization;
- Accountability-internal prepared recorder with no public/friend/DI bypass;
- transition-owned append;
- Accountability-owned typed delivery handler;
- removal of registered Accountability observer path.

Review:

- Outbox persists the prepared safe AuditEnvelope, not a lifecycle event or raw
  candidate;
- public Audit recording always prepares, while internal Outbox delivery never
  re-runs sanitization;
- generic `IAuditSink` composition makes no durability-tier claim;
- same IDs/times feed post-commit notification;
- no double recording path.

### Slice 9 — PostgreSQL crash and NativeAOT closure

Red:

- CrashWorker matrix;
- CW07 durable resume-acceptance response-loss reconciliation;
- native active-composition and required Workflow consumer assertions;
- exact native markers and fixture assertions.

Green:

- AOT Host scenario executes both golden paths against real PostgreSQL.

Review:

- original native executable runs;
- source-generated payload dispatch is observable;
- no analyzer/publish-only substitution.

### Slice 10 — Closure review

- run canonical build, focused suites, full PostgreSQL suite, boundary suite,
  crash matrix, and NativeAOT fixture;
- produce a Phase 9c closure review with claimed vs locally/CI-verified evidence;
- update `memory.md` only with actually executed support claims;
- freeze only after every exit gate is executable.

---

## 19. NativeAOT Exit Evidence

Extend the existing PostgreSQL AOT Host/Fixture rather than create a parallel
provider fixture.

The native scenario must:

1. apply/validate V012 against real PostgreSQL;
2. create and suspend a Workflow with a HumanTask;
3. commit HumanTask completion + Outbox message;
4. terminate/rebuild the dispatcher service provider;
5. validate active ContractId/required-consumer composition;
6. claim and deserialize the persisted HumanTask payload;
7. execute the required Workflow continuation consumer and observe one durable
   resume transition plus exact applied CompletionEventId discriminator;
8. fail an optional typed LocalEvent compatibility handler and prove it cannot
   block HumanTask Outbox Ack;
9. observe the Workflow transition's persisted prepared `AuditEnvelope`
   message, including Sanitization and Integrity;
10. deliver it through the Accountability-internal prepared path to
   `PostgreSqlAuditSink` without re-running sanitization;
11. replay the same AuditId and observe Duplicate, not a second fact;
12. exercise exact terminal AlreadyApplied replay and an expired
    lease/fencing rejection;
13. emit exact sentinels:

```text
CRESTCREATES_RUNTIME_OUTBOX_OK
CRESTCREATES_HUMANTASK_RELIABLE_DELIVERY_OK
CRESTCREATES_WORKFLOW_ACCOUNTABILITY_DELIVERY_OK
```

The fixture must:

- publish with `-p:CrestCreatesPublishMode=aot` for linux-x64;
- complete native link;
- execute the original native artifact, not `dotnet <dll>`;
- use a real PostgreSQL connection string;
- assert every marker and exit code;
- retain the publish/run log as Issue-local evidence.

---

## 20. Implementation Review Guardrails

Any answer in the unsafe column blocks the affected Slice:

| Question | Required answer |
|---|---|
| Does Outbox create another UoW/transaction abstraction? | No |
| Does any public contract expose provider types? | No |
| Can append commit without ambient Runtime transaction? | No |
| Can state commit when required append conflicts/fails? | No |
| Can a canonical producer receive and ignore a Conflict result? | No |
| Does exception imply producer rollback after COMMIT started? | No |
| Can HumanTask completion be blindly replayed after commit unknown? | No |
| Does Completed after commit unknown prove the ambiguous caller won? | No |
| Can a stale/expired fence Ack, Retry, or DeadLetter? | No |
| Does every successful claim consume attempt budget? | Yes |
| Can an over-budget claim invoke the handler? | No |
| Can retry mutate payload or generate MessageId? | No |
| Do RequiredConsumerIds participate in immutable Outbox Integrity? | Yes |
| Can delivery retry re-run sanitization or change Audit Integrity? | No |
| Does persisted payload contain a CLR/assembly type name? | No |
| Does reliable dispatch use reflection reconstruction/invocation? | No |
| Is legacy EF DLQ the terminal authority? | No |
| Can missing handler/sink composition DeadLetter a valid fact? | No |
| Does Claim select a ContractId absent from registry metadata? | No |
| Can Claim proceed while any active ContractId/required consumer is unsupported? | No |
| Does unsupported active composition throw `OutboxCompositionException`? | Yes |
| Can provider availability failure use `OutboxCompositionException`? | No |
| Do terminal messages require current handler/consumer registration? | No |
| Does registry indexing retain a scoped handler instance? | No |
| Can HumanTask delivery failure change business status? | No |
| Can zero LocalEvent handlers authorize Workflow-correlated completion Ack? | No |
| Does a Workflow-correlated completion persist its required consumer ID? | Yes |
| Is cleared WaitingHumanTaskKey sufficient duplicate proof? | No |
| Does reliable continuation Ack require post-resume RunAsync success? | No |
| Can optional LocalEvent failure block Outbox Ack? | No |
| Must every business-critical downstream consumer have a persisted stable ID? | Yes |
| Does Workflow reliable Accountability depend on observer success? | No |
| Is a lifecycle event persisted for future Audit re-projection? | No |
| Is a raw Audit candidate persisted for future sanitization? | No |
| Does immediate and Outbox Audit recording share one preparation implementation? | Yes |
| Does public `IAuditRecorder` expose prepared-envelope recording? | No |
| Is prepared-envelope fan-out callable outside Accountability internals? | No |
| Does Accountability own the AuditEnvelope delivery handler? | Yes |
| Is sink membership evaluated at delivery attempt time? | Yes |
| Does generic `IAuditSink` expose/claim a durability tier? | No |
| Does FullDurable Accountability evidence use `PostgreSqlAuditSink`? | Yes |
| Is `AuditEnvelope.AuditId` also the Outbox MessageId? | Yes |
| Is `HumanTaskCompletedEvent.EventId` also the MessageId? | Yes |
| Do InMemory/PostgreSQL run the same semantic cases? | Yes |
| Does V012 extend the existing migration/schema catalog? | Yes |
| Are Control Plane writes excluded unless explicitly produced? | Yes |
| Does the native binary execute both persisted payload types? | Yes |
| Does exact final-fence Ack/DeadLetter replay return AlreadyApplied? | Yes |

---

## 21. Exit Criteria

Phase 9c closes only when all are true:

1. One provider-neutral contract separates transactional append from dispatch
   lifecycle ownership.
2. InMemory and PostgreSQL pass the same append/claim/fence/retry/terminal
   semantic kit.
3. PostgreSQL V012 is part of the one checksummed catalog and exact schema
   manifest.
4. State + Outbox known commit/rollback and commit-unknown both-or-neither are
   executable against real PostgreSQL.
5. Same MessageId exact replay is Duplicate; conflicting replay throws a
   transaction-aborting provider-neutral exception; neither overwrites or
   resets accepted state.
6. Two real PostgreSQL dispatchers produce one active fencing owner.
7. Expired/stale owners cannot Ack, Retry, or DeadLetter.
8. Pending, retry-due, and expired-lease records recover through a fresh
   provider/process.
9. Publish/Ack loss redelivers the same MessageId without an exactly-once claim.
10. Poison messages reach Outbox-owned DeadLettered terminal state atomically,
    while missing active handler/required-consumer or required sink composition
    leaves valid durable facts recoverable and makes the Host unhealthy.
11. HumanTask completion commits Completed + Outbox and no longer synchronously
    publishes or creates `CompletionDispatchFailed`.
12. Duplicate HumanTask delivery produces at most one durable Workflow resume
    acceptance for the same CompletionEventId in the golden path.
13. Pre-existing legacy completion-dispatch failures cannot be silently lost
    during upgrade, and V012/provider preflight detects them without expanding
    the HumanTask Store query contract; active standalone tasks requiring a
    migrated stable consumer obligation are likewise blocked or explicitly
    reconciled.
14. Workflow started/suspended/resumed/completed/failed each commit their
    prepared safe `AuditEnvelope`, including stable Integrity, with state.
15. Accountability delivery retries partial sink failures with the same AuditId
    and dead-letters rejection/conflict.
16. Best-effort Workflow observers remain independent and the old
    Accountability observer is not a second reliable write path.
17. Control Plane/reference-data writes do not implicitly append.
18. No persisted type name, reflection JSON fallback, untyped payload dispatch,
    or provider handle exists in the mainline.
19. CrashWorker proves producer and dispatcher crash windows against PostgreSQL.
20. The linux-x64 native binary publishes, links, runs both golden payload paths,
    and emits all exact sentinels.
21. The canonical solution build, focused tests, full PostgreSQL suite,
    dependency boundaries, evidence ledger, and NativeAOT gate are green.
22. A closure review records evidence provenance and only then updates
    `memory.md` to implemented/verified status.
23. Every claim consumes AttemptCount; claims above MaxAttempts never invoke a
    handler and eventually terminalize under a valid fence without restart
    resetting the budget.
24. HumanTask commit-unknown recovery requires fresh observation and cannot
    create a second CompletionEventId for an already committed completion.
25. Handler/required-consumer registry indexing caches immutable registration
    metadata only; each instance is resolved from its delivery scope.
26. Accountability owns the AuditEnvelope delivery adapter, and sink membership
    changes affect subsequent attempts without mutating the prepared fact.
27. Accepted append initializes CreatedAt, AvailableAt, and UpdatedAt from one
    provider-authoritative time observation rather than OccurredAt.
28. Workflow-correlated HumanTask messages persist the exact continuation
    consumer obligation; Ack requires durable resume acceptance with an exact
    applied-identity discriminator, while post-resume RunAsync liveness is not
    an Ack condition.
29. Public `IAuditRecorder` remains candidate-only and always prepares;
    prepared-envelope validation/fan-out is Accountability-internal and does
    not re-run sanitization.
30. Startup/readiness and atomic Claim prove every active ContractId and
    RequiredConsumerId is currently supported; terminal messages impose no such
    obligation and unsupported active facts remain unchanged.
31. HumanTask commit-unknown reconciliation treats Completed as proof of one
    winner only; exact intent comparison distinguishes satisfied state from a
    different concurrent winner without reporting false caller success.
32. Exact final-fence Ack and DeadLetter replay returns AlreadyApplied and
    leaves terminal state unchanged; different-fence/failure and cross-terminal
    replay fail closed.
33. Outbox Ack/Retry/DeadLetter authority is exactly the ContractId handler plus
    persisted RequiredConsumerIds; optional LocalEvent failure is bounded,
    logged, and cannot block Ack.
34. Unsupported active requirements make `ClaimAsync` throw
    `OutboxCompositionException` before mutation; provider availability failure
    uses a different contract and composition failure is never transient.
35. Reliable Workflow Accountability generically requires at least one
    configured sink without changing `IAuditSink`; only FullDurable evidence
    composed with `PostgreSqlAuditSink` claims sink persistence.

The four final gates are:

```text
A. Atomicity
   Runtime fact and Outbox never split-brain.

B. Ownership
   lease expiry, concurrency, and Ack loss cannot let a stale fence advance state.

C. Recovery
   every post-commit/pre-Ack crash window returns to retryable or terminal-diagnostic state.

D. Real mainlines
   HumanTask Completed -> durable event -> declared required consumer
       -> durable Workflow resume acceptance
   Workflow committed transition -> durable AuditEnvelope
       -> Accountability-internal prepared recording
   both execute through PostgreSQL restart/crash and NativeAOT evidence.
```

---

## 22. Implementation-Plan Handoff

After design approval, the implementation plan must:

- preserve the Slice order above and use Case-first TDD;
- name exact files/projects and focused commands;
- include a complete Case ID -> acceptance name -> runner/evidence ledger;
- include V012 SQL/schema-manifest details and legacy preflight mechanics;
- include DI registration/cutover order so no Host briefly has two reliable
  producer paths;
- include required-composition startup validation and registered-ContractId
  / RequiredConsumerId active probing and atomic Claim guards without
  scoped-instance capture;
- specify the Workflow continuation required-consumer port/registration and
  removal from the generic LocalEvent enumerable;
- choose and fully specify the V012 durable continuation-acceptance
  discriminator representation, replay/conflict rules, and CW07 synchronization;
- specify bounded optional LocalEvent dispatch that cannot affect Outbox
  delivery outcome, plus first-party required/compatibility classification;
- list every generated JSON root and paired ContractId handler;
- specify provider-clock, initial eligibility, retry option bounds, and
  over-budget terminalization mechanics;
- specify HumanTask commit-unknown observation/reconciliation and V012 legacy
  preflight mechanics without a new Runtime enumeration API;
- specify exact RuntimeStateValue intent comparison for concurrent completion
  winners;
- keep public `IAuditRecorder` candidate-only and name the exact
  Accountability-internal prepared validation/fan-out files;
- specify exact Ack/DeadLetter AlreadyApplied predicates and shared cases;
- map unsupported active composition only to `OutboxCompositionException` and
  keep infrastructure failure distinct;
- specify delivery-time Accountability sink membership tests;
- require `PostgreSqlAuditSink` only in FullDurable evidence without adding an
  `IAuditSink` durability capability;
- specify CrashWorker parent/child synchronization without public test hooks;
- specify exact NativeAOT publish/run commands and markers;
- stop at a Review gate if any invariant is not executable;
- avoid implementation-plan expansion into brokers, Inbox, cache consistency,
  UI, retention, or exactly-once work.

This draft freezes behavior and boundaries once approved. SQL statement layout,
internal class names, batching mechanics, and exact file-by-file edits remain
implementation-plan decisions constrained by this Spec.
