# Phase 9a — Accountability Runtime Foundation Design Spec

**Issue:** [#39 — Phase 9a — Accountability Runtime Foundation](https://github.com/OrchesAdam/CrestCreates/issues/39)  
**Architecture decision:** [#38 — Upgrade AuditLogging into Accountability Runtime](https://github.com/OrchesAdam/CrestCreates/issues/38)  
**Parent:** [#23 — Phase 9 Production Providers](https://github.com/OrchesAdam/CrestCreates/issues/23)  
**Blocks:** [#24 — Phase 9b Durable Runtime Store Provider](https://github.com/OrchesAdam/CrestCreates/issues/24)  
**Date:** 2026-07-28  
**Last revised:** 2026-07-29  
**Status:** APPROVED  
**Approval:** Boundary PASS · Invariants PASS · Case Matrix PASS · Acceptance Test Skeleton PASS · Phase 9b Readiness PASS

---

## 1. Objective

Phase 9a establishes one framework-wide Accountability Runtime Foundation.

It does not build a larger technical log table. It defines immutable,
correlatable, responsibility-bearing runtime facts and one write boundary that
future durable providers can implement without redefining audit semantics:

```text
Runtime Producer
    → producer-owned adapter
    → immutable AuditEnvelope candidate
    → IAuditRecorder
    → validate candidate
    → sanitize / minimize
    → validate sink snapshot
    → canonical AuditEvidence hash
    → IAuditSink[]
```

The governing distinction is:

> AuditLogging records technical observations. Accountability records what a
> runtime claims happened and who or what bears responsibility for the action.

An Accountability fact should be able to answer:

```text
Which effective authority principal bears responsibility for the action?
What was the primary target?
What outcome did the runtime observe?
Which upstream runtime operation directly caused it?
Which descriptor versions and contract hashes governed it?
Which request, capability execution, workflow instance, or task instance was involved?
Which capture and sanitization policy was applied before a sink saw the record?
```

Phase 9a establishes semantics and the first HTTP, AOP, Capability, and Workflow
adapters. It does not claim legal evidence, complete compliance, tamper-proof
storage, reliable delivery, or complete business lineage.

---

## 2. Issue Content and Accepted Boundary

Issue #39 requires:

- `AuditEnvelope`, Actor, Action, Target, Outcome, Runtime, Descriptor, and Data
  contracts;
- `IAuditRecorder` and `IAuditSink`;
- an in-memory sink and minimal sanitization/hash extension points;
- HTTP/AOP, Capability, and minimal Workflow lifecycle adapters;
- verification of correlation, causation, actor, target, outcome, descriptor,
  and runtime context;
- existing local audit models to become adapters or compatibility models rather
  than framework-wide sources of truth.

The corrected Phase 9 order is:

```text
Phase 8 Projection / Exposure closure
    ↓
Phase 9a Accountability Runtime Foundation (#39)
    ↓
Phase 9b Durable Runtime Store Provider (#24)
```

Phase 9b must be able to implement the finalized `IAuditSink` contract and pass
the shared sink contract suite. It must not decide again whether `AuditLog`,
`CapabilityExecutionRecord`, or another local DTO is the durable authority.

---

## 3. Current Repository Facts

This design is based on the current code, not only the issue sketch.

### 3.1 HTTP/AOP AuditLogging today

Current mainline:

```text
AuditLoggingMiddleware
    → mutable AsyncLocal AuditContext
    → AuditedMoAttribute mutates the same context
    → AuditLogWriter
    → AuditLogService
    → AuditLog entity
```

Relevant facts:

- `AuditContext` combines HTTP, method, exception, raw body, and arbitrary
  `Dictionary<string, object>` data.
- Multiple audited methods in one request overwrite the same Service/Method/
  Parameters/ReturnValue fields. Only the last enrichment survives.
- `AuditedMoAttribute` serializes `object` arguments and results without
  `JsonTypeInfo` and suppresses `IL2026`.
- `AuditLoggingMiddleware` uses `GetDisplayUrl()`, so its current URL may contain
  sensitive or high-cardinality query data.
- `TraceIdentifier` is stored as `TraceId`; it is an ASP.NET request identifier,
  not necessarily the current Activity trace id.
- `HideErrors=false` can throw an audit failure after the business pipeline has
  already run.

Therefore HTTP and method invocation must become separate facts. The mutable
`AuditContext` may remain as an HTTP compatibility buffer, but it cannot remain
the framework-wide responsibility model.

### 3.2 Capability audit today

Current mainline:

```text
Capability AuditMiddleware
    → CapabilityExecutionRecord
    → ICapabilityAuditStore
```

Relevant facts:

- `CapabilityExecutionContext` already has `CapabilityContractHash`,
  `CorrelationId`, `CausationId`, `TenantId`, `UserId`, and `InvocationSource`.
- `CapabilityExecutionRecord` drops `CausationId` and the contract hash.
- `AuditMiddleware` allocates an ExecutionId, records in `finally`, and returns
  the result before it can attach `CapabilityExecutionResult.AuditRecordId`.
- The outer `CapabilityPipeline` converts thrown cancellation and exceptions to
  result objects after the audit middleware has rethrown them.
- Dynamic API currently assigns `CausationId = HttpContext.TraceIdentifier`
  while `CorrelationId` keeps an unrelated generated default. This is not a
  valid responsibility chain.
- `ICapabilityAuditStore` defaults to a Null implementation and
  `AddInMemoryCapabilityAudit()` replaces it with the test store.

The Capability adapter must use `IAuditRecorder` as its only main write boundary.
The append-only local store may remain only as an unwired obsolete API or pure
mapping target; it cannot masquerade as a contract-compliant sink.

### 3.3 Workflow lifecycle today

Relevant facts:

- `WorkflowLifecycleEventPublisher` is currently a no-op.
- `WorkflowLifecycleEvent` contains only event type, workflow/instance IDs,
  status, timestamp, and `object? Payload`.
- `WorkflowExecutionRequest` and `WorkflowInstance` do not preserve an initiating
  actor, correlation, causation, parent audit ID, or invocation source.
- `WorkflowInstance.Snapshot()` cannot preserve fields that do not exist.
- Started, suspended, resumed, completed, and failed events are already published
  after the corresponding instance save. That ordering must be preserved.
- Suspend/resume and process recovery cannot rely on an HTTP context or ambient
  `AsyncLocal` value.

Workflow therefore needs a typed observer surface, a typed lifecycle event, and
a persisted minimal audit origin. Accountability must not move persistence or
state transitions into an observer.

### 3.4 Existing platform infrastructure to reuse

- Canonical Hash Runtime already provides AOT-safe `Utf8JsonWriter` projection
  hashing through `ICanonicalHashComputer.ComputeFromProjection`.
- JSON Contract BuildTasks already generate `[JsonSerializable]` roots from
  `[JsonContractSurface]` before CoreCompile and are the repository mainline.
- Procurement Approval already composes HTTP → Capability → Workflow → HumanTask,
  has an existing linux-x64 NativeAOT publish-link-run fixture, and currently
  asserts the legacy in-memory Capability audit store.
- Agent Tool Governance auditing is a pre-dispatch/finalization control protocol.
  It participates in whether execution is allowed and cannot be replaced by a
  post-fact `IAuditSink`.

Phase 9a must reuse these facilities rather than introduce a second hash system,
handwritten JSON root list, new golden sample, or weakened Agent governance path.

---

## 4. Concept Boundary

| Concept | Responsibility | May drive or block business execution? |
|---|---|---:|
| Technical log | diagnostics, exceptions, operational messages | No |
| Trace / Span | call topology, timing, distributed observation | No |
| Business event | notify consumers of a business state change | Yes |
| Governance checkpoint | prove required preconditions before or after dispatch | Yes |
| Accountability fact | immutable post-fact responsibility claim | No |

`TraceId` and `SpanId` are observation fields only. They must not be substituted
for CorrelationId, CausationId, ActorId, ParentAuditId, or PreviousAuditId.

An OpenTelemetry span event may export an Accountability summary, but it is not
the authoritative CrestCreates Accountability contract or sink.

---

## 5. Scope

### 5.1 Phase 9a includes

- `CrestCreates.Accountability.Abstractions` and `CrestCreates.Accountability`.
- Immutable v1 Envelope and value contracts.
- One ambient, stack-safe operation context for synchronous producer composition.
- Persistable `AuditOrigin` for durable runtime hand-off.
- Candidate validation, default-deny sanitization, canonicalization, and
  Canonical Hash Runtime integration.
- Explicit multi-sink write results, idempotency, and conflict semantics.
- Thread-safe in-memory sink and reusable sink contract tests.
- Generated JSON contract roots and NativeAOT publish-link-run evidence.
- Separate HTTP and method-invocation facts.
- Capability result/exception/cancellation facts and `AuditRecordId` closure.
- Workflow started/suspended/resumed/completed/failed facts after store save.
- Pure compatibility mappings for legacy HTTP and Capability models; append-only
  legacy stores are not Accountability sinks.
- Procurement HTTP → Capability → Workflow mainline acceptance.

### 5.2 Phase 9a excludes

- Database/file durable sinks, Outbox, reliable delivery, or cross-sink atomicity.
- Exactly-once distributed claims, global ordering, or distributed clocks.
- Retention, archival, cleanup, export, query API, or lineage reader.
- Hash chains, signatures, WORM storage, or tamper-evident checkpoints.
- Full HumanTask assignment/claim/delegation/decision accountability.
- Event publish/consume/retry/dead-letter accountability.
- Descriptor Registry or Activation accountability.
- Agent Memory accountability.
- Replacement of Agent Tool Governance Auditor.
- Complete authorization-decision evidence.
- Default request, response, argument, result, prompt, memory, or visible-data
  capture.
- Using Accountability facts to trigger business behavior.
- A global post-commit strict switch that turns an audit failure into an apparent
  business failure.

---

## 6. System Invariants

### INV-1 — AuditEnvelope is the only framework-wide source model

After Phase 9a, new runtime responsibility facts go through `IAuditRecorder`.
Runtime-specific typed payloads remain allowed; independent runtime-specific
source models and primary audit stores do not.

### INV-2 — Producer owns fact meaning

The producer adapter owns Actor, Action, Target, Outcome, OccurredAt,
CorrelationId, CausationId, ParentAuditId, PreviousAuditId, descriptor refs, and
runtime refs. The recorder may validate, minimize, canonicalize, hash, and stamp
attempt metadata in its result.
It must not reinterpret failure as success or system automation as a human.

`AuditActor` means the effective responsibility/authority principal under which
the action occurred. The technical component that executed code belongs in
Runtime Context. For example, a user-triggered Capability has the user Actor;
a Workflow lifecycle transition has the Workflow instance Actor and preserves
the initiating user or Agent in `InitiatedBy`.

### INV-3 — Causality fields have distinct meanings

```text
CorrelationId
    Groups facts belonging to the same business operation.

CausationId
    Identifies the direct upstream runtime operation/event/decision.

ParentAuditId
    Identifies the producer-allocated AuditId of a directly enclosing
    responsibility scope when that ID is known.

PreviousAuditId
    Identifies the previous AuditId in the same subject/lifecycle sequence.

TraceId / SpanId
    Observation-only identifiers.
```

Containment and sequence are different relations. `ParentAuditId` must never
represent "happened before"; `PreviousAuditId` must never claim containment.
Neither field is referential integrity across every best-effort sink. Phase 9a
permits a dangling relation when the referenced write later fails or different
sinks partially accept a fan-out. Producers must never invent either ID from a
TraceId, CorrelationId, username, runtime entity ID, or another unrelated field.

### INV-4 — Every sink sees only the safe snapshot

The fixed order is:

```text
candidate validation
    → sanitization/minimization
    → safe-snapshot validation
    → canonicalization
    → hash
    → sinks
```

Sanitizer failure or an unrecognized raw payload is `Rejected`; the recorder
must not fall back to the original candidate.

### INV-5 — Hash is not redaction or truth

`SHA-256(secret)` is not safe redaction for low-entropy data. The RecordHash
means only that the same sanitized canonical fact under the same profile yields
the same digest. It does not prove truth, completeness, non-deletion, durable
retention, or tamper resistance.

### INV-6 — No raw data capture by default

Built-in Phase 9a adapters default to `DataSnapshot = null` and `Payload = null`.
They do not capture Authorization/Cookie headers, tokens, bodies, arguments,
returns, exception messages/stacks, Agent content, or HumanTask visible data.

### INV-7 — Post-fact recording cannot rewrite business outcome

HTTP, method, Capability, and Workflow adapters are post-fact/best-effort.
Once a business state is committed, a sink failure must not roll it back or
replace the original result/exception. Post-fact writes use a bounded audit
write budget independent from an already-cancelled business token.

### INV-8 — Required audit is a different protocol

Required pre-dispatch/finalization guarantees need a governance checkpoint,
transactional Outbox, durable acceptance receipt, fencing/lease, and explicit
reconciliation. `AccountabilityOptions.RequireAtLeastOneSink` is startup
validation only; it does not turn post-fact writes into required audit.

### INV-9 — AuditId is immutable and conflict-safe

For one sink and AuditId:

```text
new AuditId                      → Accepted
same AuditId + same RecordHash   → Duplicate (idempotent success)
same AuditId + different hash    → Conflict (never overwrite)
```

### INV-10 — No reflection JSON fallback

The mainline forbids `JsonSerializer.Serialize(object)`, untyped polymorphic
serialization, `Dictionary<string, object>` payloads, `object? Payload`,
`DefaultJsonTypeInfoResolver`, and new Accountability `IL2026` suppressions.

### INV-11 — Collections received by a sink are immutable snapshots

An `IReadOnlyList` backed by a mutable List or Array is insufficient. Envelope
collections use immutable values, and JsonElement values are cloned before
acceptance. The in-memory sink snapshots on write and read.

### INV-12 — Dependency direction is one-way

Producer modules depend on Accountability Abstractions and `IAuditRecorder`.
They never depend on `IAuditSink` implementations. Accountability does not
reference AuditLogging, Capability, Workflow, ASP.NET Core, Agent, or HumanTask.

Dynamic API does not need a direct Accountability reference. It stops assigning
TraceIdentifier as CausationId; the Capability pipeline obtains the current HTTP
operation from the shared accessor.

---

## 7. Module and Dependency Shape

Use the repository's existing Runtime/Audit grouping:

```text
src/Runtime/Audit/
├── CrestCreates.Accountability.Abstractions/
├── CrestCreates.Accountability/
├── CrestCreates.AuditLogging.Abstractions/
└── CrestCreates.AuditLogging/
```

Tests follow the same grouping:

```text
tests/Runtime/Audit/
├── CrestCreates.Accountability.Abstractions.Tests/
├── CrestCreates.Accountability.Tests/
└── CrestCreates.AuditLogging.Tests/
```

Allowed dependencies:

```text
Accountability.Abstractions → Core.Abstractions
Accountability.Abstractions → Metadata.Abstractions (CanonicalHash contract)
Accountability              → Accountability.Abstractions
Accountability              → Metadata (ICanonicalHashComputer implementation)

AuditLogging.Abstractions   → Accountability.Abstractions
AuditLogging                → Accountability.Abstractions
Capability                  → Accountability.Abstractions
Workflow                    → Accountability.Abstractions
```

Forbidden dependencies:

```text
Accountability(.Abstractions) → ASP.NET Core / AuditLogging / Capability / Workflow
Capability / Workflow         → concrete Accountability runtime or any IAuditSink type
Producer Handler              → IAuditSink
```

The abstractions project opts into existing JSON Contract BuildTasks with the
same project reference and repository props/targets used by Control Plane.

---

## 8. Core Contract

The following shapes are normative in meaning. Names may receive minor
implementation-level adjustments, but Phase 9b-visible semantics may not drift.

### 8.1 Immutable Envelope

```csharp
public sealed record AuditEnvelope
{
    public int ContractVersion { get; init; } = 1;

    public required string AuditId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }

    public string? TenantId { get; init; }
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? ParentAuditId { get; init; }
    public string? PreviousAuditId { get; init; }

    public required AuditActor Actor { get; init; }
    public required AuditAction Action { get; init; }
    public required AuditTarget Target { get; init; }
    public required AuditOutcome Outcome { get; init; }

    public AuditRuntimeContext Runtime { get; init; } = AuditRuntimeContext.Empty;
    public AuditDescriptorContext Descriptors { get; init; } = AuditDescriptorContext.Empty;
    public AuditDataSnapshot? DataSnapshot { get; init; }
    public ImmutableArray<AuditEvidenceReference> Evidence { get; init; } = [];
    public AuditPayload? Payload { get; init; }
    public ImmutableSortedDictionary<string, string> Tags { get; init; }
        = ImmutableSortedDictionary<string, string>.Empty;

    // Recorder-owned after sanitization.
    public AuditSanitizationStamp? Sanitization { get; init; }
    public CanonicalHash? Integrity { get; init; }
}
```

A producer candidate must leave `Sanitization` and `Integrity` null. The
recorder rejects producer-supplied values, applies the sanitization stamp, then
sets structured Integrity before invoking any sink.

`Integrity` is excluded from the producer-fact hash. The Sanitization stamp is
included because the applied policy is part of the safe fact. `AuditId`,
ContractVersion, and OccurredAt are included. A shared `RecordedAt` does not
exist: processing time belongs to the recorder attempt and first-acceptance time
belongs to each provider.

For duration-bearing actions (`http.request`, `method.invoke`, and
`capability.execute`), `OccurredAt` is the terminal outcome observation time and
`Runtime.Duration` is elapsed execution duration. For Workflow lifecycle facts,
`OccurredAt` is the committed transition time. Adapters may not independently
reinterpret it as start time.

### 8.2 Actor

```csharp
public sealed record AuditActor
{
    public required string Kind { get; init; }
    public required string Id { get; init; }
    public string? DisplayName { get; init; } // non-authoritative
    public AuditActorReference? InitiatedBy { get; init; }
    public AuditActorReference? OnBehalfOf { get; init; }
    public string? DelegationId { get; init; }
    public string? ImpersonationId { get; init; }
}

public sealed record AuditActorReference(string Kind, string Id);
```

Core stable kinds are constants, not a closed enum:

```text
user, anonymous, system, workflow, human-task, agent,
integration, scheduler, mcp-client, unknown
```

An extension kind must satisfy the stable semantic-name grammar. CLR type names
and localized display strings are not persistence semantics.

The Actor is the effective authority principal, not necessarily the process or
class that executed instructions. Technical executors are represented by
`service`, `application`, and other Runtime References. `InitiatedBy` preserves
the principal that caused an autonomous runtime actor to begin work;
`OnBehalfOf` preserves delegated authority when that relation is explicitly
known. Producers do not infer either relation from display names.

### 8.3 Action, Target, and Outcome

```csharp
public sealed record AuditAction(string Kind, string Name);
public sealed record AuditTarget(string Kind, string Id, string? Version = null);

public sealed record AuditOutcome
{
    public required string Status { get; init; }
    public string? Code { get; init; }
    public string? SafeSummary { get; init; }
}
```

Phase 9a Action kinds:

```text
http.request
method.invoke
capability.execute
workflow.lifecycle
```

Core Outcome statuses:

```text
succeeded, failed, rejected, cancelled, skipped, indeterminate
```

`SafeSummary` is bounded and cannot contain an exception stack, raw exception
message, external response, or unredacted content.

### 8.4 Descriptor and Runtime contexts

Descriptor context is extensible rather than a fixed Schema/Capability/
Workflow field matrix:

```csharp
public sealed record AuditDescriptorContext
{
    public ImmutableArray<AuditDescriptorReference> Items { get; init; } = [];
    public string? SnapshotId { get; init; }
    public CanonicalHash? SnapshotHash { get; init; }
    public static AuditDescriptorContext Empty { get; } = new();
}

public sealed record AuditDescriptorReference
{
    public required string Kind { get; init; }
    public required string Id { get; init; }
    public required int Version { get; init; }
    public CanonicalHash? ContractHash { get; init; }
}
```

Items canonical-sort by Kind → Id → Version → ContractHash.Value.

```csharp
public sealed record AuditRuntimeContext
{
    public string? InvocationSource { get; init; }
    public string? ExecutionId { get; init; }
    public string? RequestId { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public TimeSpan? Duration { get; init; }
    public ImmutableArray<AuditRuntimeReference> References { get; init; } = [];
    public static AuditRuntimeContext Empty { get; } = new();
}

public sealed record AuditRuntimeReference(string Kind, string Id);
```

Core runtime reference kinds include `capability-execution`,
`workflow-instance`, `workflow-run-operation`, `workflow-step`,
`human-task-instance`, `event-instance`, `agent-session`, `agent-invocation`,
`approval`, `budget-reservation`, `invocation-lease`, `service`,
`runtime-instance`, and `application`. First-party producers include the stable
service/application reference when known; a runtime-instance reference is
optional and must not contain a host name or ephemeral secret.

Persisted `InvocationSource` values use an explicit exhaustive mapping, never
`enum.ToString()`:

```text
HTTP        → http
Workflow    → workflow
HumanTask   → human-task
Agent       → agent
MCP         → mcp
Integration → integration
System      → system
```

Adding or renaming an enum member does not change persistence semantics without
a contract change and exhaustive mapping test.

### 8.5 Data, evidence, and payload

```csharp
public sealed record AuditDataSnapshot
{
    public required string CapturePolicyId { get; init; }
    public required int CapturePolicyVersion { get; init; }
    public ImmutableArray<AuditDataArtifact> Artifacts { get; init; } = [];
}

public sealed record AuditDataArtifact
{
    public required string Kind { get; init; }
    public CanonicalHash? ContentHash { get; init; }
    public AuditDataHashBasis? ContentHashBasis { get; init; }
    public JsonElement? SanitizedValue { get; init; }
}

public enum AuditDataHashBasis
{
    Source,
    Sanitized
}

public sealed record AuditEvidenceReference(
    string Kind,
    string Id,
    CanonicalHash? Hash = null);

public sealed record AuditPayload
{
    public required string Kind { get; init; }
    public required int Version { get; init; }
    public required JsonElement Data { get; init; }
}

public sealed record AuditSanitizationStamp
{
    public required string PolicyId { get; init; }
    public required int PolicyVersion { get; init; }
    public ImmutableArray<string> AppliedRuleIds { get; init; } = [];
}
```

`Payload.Data` must be created from a producer-owned source-generated
`JsonTypeInfo<T>`. It is bounded, cloned, sanitized, and canonicalized before
hashing. It cannot duplicate Actor, Action, Target, Outcome, descriptors, or
causality already carried by the Envelope.

No built-in Phase 9a adapter needs a payload to express its required fact.
When `ContentHash` is present, `ContentHashBasis` is required and states whether
the digest was computed over the source content or the sanitized value. The
field must not imply that source content was retained.

### 8.6 Operation context and persisted origin

Synchronous nested producers need one shared, stack-safe context:

```csharp
public sealed record AuditOperationContext
{
    public required string CorrelationId { get; init; }
    public required string OperationId { get; init; }
    public string? EnclosingAuditId { get; init; }
    public required AuditActor Actor { get; init; }
    public string? TenantId { get; init; }
    public required string InvocationSource { get; init; }
}

public interface IAuditOperationContextAccessor
{
    AuditOperationContext? Current { get; }
    IDisposable Push(AuditOperationContext context);
}
```

The implementation uses an immutable AsyncLocal frame chain, never a mutable
`Stack<T>`:

```text
Frame = Context + ParentFrame
Push  = AsyncLocal.Value = new Frame(context, current)
Pop   = require ReferenceEquals(current, frame), then restore frame.Parent
```

ExecutionContext copies may share immutable parent frames safely; parallel
sibling scopes cannot mutate one another. Out-of-order dispose fails without
changing the current frame. The accessor is composition context only and is
never a durable source of truth.

`OperationId` identifies the operation that directly causes nested work.
`EnclosingAuditId` is nullable because an operation does not automatically have
an enclosing Accountability fact. HTTP, method, and Capability scopes set it to
their already allocated fact AuditId; Workflow run scopes carry only an actual
enclosing fact from the call boundary and never substitute a lifecycle-sequence
ID.

Durable runtimes persist a minimal origin:

```csharp
public sealed record AuditOrigin
{
    public required string CorrelationId { get; init; }
    public string? UpstreamOperationId { get; init; }
    public string? UpstreamAuditId { get; init; }
    public required AuditActor InitiatingActor { get; init; }
    public required string InvocationSource { get; init; }
}
```

Mapping is exact for the first nested fact: UpstreamOperationId → CausationId
and UpstreamAuditId → ParentAuditId. Lifecycle sequence is persisted separately
and maps only to `PreviousAuditId`. No trace identifier or runtime entity ID
substitution is allowed.

---

## 9. Recorder, Sanitizer, Hash, and Sink Contracts

### 9.1 Recorder result

```csharp
public interface IAuditRecorder
{
    ValueTask<AuditRecordResult> RecordAsync(
        AuditEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public enum AuditRecordStatus
{
    Recorded,
    PartiallyRecorded,
    Rejected,
    Failed,
    NoSinkConfigured
}

public sealed record AuditRecordResult
{
    public required string AuditId { get; init; }
    public required AuditRecordStatus Status { get; init; }
    public required DateTimeOffset ProcessedAt { get; init; }
    public CanonicalHash? RecordHash { get; init; }
    public ImmutableArray<AuditSinkWriteResult> SinkResults { get; init; } = [];
    public ImmutableArray<AuditSinkFailure> SinkFailures { get; init; } = [];
    public ImmutableArray<AuditRecordIssue> Issues { get; init; } = [];

    public bool IsAccepted => SinkResults.Any(x =>
        x.Status is AuditSinkWriteStatus.Accepted or AuditSinkWriteStatus.Duplicate);
}

public sealed record AuditSinkFailure(string SinkId, string Code);
public sealed record AuditRecordIssue(string Code, string? Path = null);
```

`ProcessedAt` is the time this recorder attempt completed, including a rejected
or no-sink attempt. It is attempt metadata, not fact metadata and not proof that
any sink persisted the candidate.

`SinkResults` preserves every Accepted, Duplicate, and Conflict result.
`SinkFailures` contains provider throws/timeouts/unavailability with stable
SinkId and safe error Code only. `Issues` contains candidate validation,
sanitizer rejection, or safe-snapshot validation failures as stable Code plus an
optional bounded field Path; it never includes the rejected value, raw provider
exception, or connection detail. `AcceptedSinkIds` is a derived view over
Accepted/Duplicate `SinkResults`, never a second stored state.

### 9.2 Sink result is explicit

`ValueTask WriteAsync(...)` is insufficient for the Phase 9b contract because it
cannot distinguish first acceptance, idempotent replay, and content conflict.
The normative boundary is:

```csharp
public interface IAuditSink
{
    string Id { get; }

    ValueTask<AuditSinkWriteResult> WriteAsync(
        AuditEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public enum AuditSinkWriteStatus
{
    Accepted,
    Duplicate,
    Conflict
}

public sealed record AuditSinkWriteResult
{
    public required string SinkId { get; init; }
    public required string AuditId { get; init; }
    public required CanonicalHash Integrity { get; init; }
    public required AuditSinkWriteStatus Status { get; init; }
    public CanonicalHash? ExistingIntegrity { get; init; } // Conflict only
    public DateTimeOffset? FirstAcceptedAt { get; init; }
}
```

Sink IDs are stable semantic IDs, not CLR type names. A Conflict never overwrites
the first accepted snapshot. `FirstAcceptedAt` is optional provider-local
metadata: Accepted may return the provider's first acceptance time; Duplicate
returns the original time when the provider can supply it; Conflict may return
the existing record's first acceptance time. It is not part of `AuditEnvelope`
or its integrity hash.

Because the result echoes identity, the recorder validates that returned
SinkId equals the registered `IAuditSink.Id`, AuditId equals the candidate
AuditId, and Integrity equals the candidate structured `CanonicalHash` across
Algorithm, AlgorithmVersion, ArtifactKind, optional DescriptorKind, Purpose,
Scope, ContractVersion, CanonicalShapeVersion, and Value. Any mismatch is a
provider failure, not an Accepted/Duplicate/Conflict result.
Conflict requires `ExistingIntegrity`, which must differ from incoming
Integrity; Accepted/Duplicate must not return `ExistingIntegrity`. Structured
hash equality compares the entire CanonicalHash contract metadata and Value.

### 9.3 Multi-sink aggregation

The recorder attempts every registered sink in deterministic ordinal Sink.Id
order. Duplicate counts as accepted because the identical record already exists.

| Sink outcomes | Recorder status |
|---|---|
| all Accepted/Duplicate | Recorded |
| at least one accepted and at least one conflict/failure | PartiallyRecorded |
| no accepted sink and at least one configured sink | Failed |
| no sinks | NoSinkConfigured |
| candidate/sanitizer/safe snapshot invalid | Rejected |

Conflicts always remain visible in `SinkResults`; they are never collapsed into
`SinkFailures`. Rejection issues always remain visible in `Issues`. Duplicate
sink IDs are a startup error. Phase 9a does not provide a transaction across
sinks or a false global record time.

### 9.4 Sanitization contract

`IAuditSanitizer` returns an immutable safe snapshot plus a stable policy ID and
version:

```csharp
public interface IAuditSanitizer
{
    ValueTask<AuditSanitizationResult> SanitizeAsync(
        AuditEnvelope candidate,
        CancellationToken cancellationToken = default);
}

public sealed record AuditSanitizationResult
{
    public required AuditEnvelope Envelope { get; init; }
    public required AuditSanitizationStamp Stamp { get; init; }
}

public interface IAuditPayloadSanitizationRule
{
    string Kind { get; }
    int RuleVersion { get; }
    AuditPayload Sanitize(AuditPayload payload);
}

public interface IAuditDataArtifactSanitizationRule
{
    string Kind { get; }
    int RuleVersion { get; }
    AuditDataArtifact Sanitize(AuditDataArtifact artifact);
}
```

The default policy is deny-raw:

- Payload and DataArtifact use separate typed rule registries;
- one stable Kind has exactly one active owner in its registry; duplicate Kind
  registration is a startup failure;
- wildcard rules and fallback pass-through are forbidden;
- `AuditSanitizationStamp.PolicyVersion` is the version of the complete
  sanitizer composition policy; each active rule has its own positive
  `RuleVersion`; Phase 9a does not perform runtime version negotiation;
- rules are selected by exact ordinal Kind and artifacts execute in canonical
  Kind/order after duplicate validation;
- `AppliedRuleIds` uses deterministic `payload:<kind>:v<version>` or
  `artifact:<kind>:v<version>` values in ordinal order, where `v<version>` is
  that rule's `RuleVersion`, not the composition PolicyVersion;
- unknown kinds, rule exceptions, invalid rule output, and policy mismatch are
  Rejected with stable issue codes;
- built-in HTTP/AOP/Capability/Workflow facts succeed because they default to no
  captured data;
- a sanitizer exception never falls back to the producer candidate.

Sanitization runs after bounded candidate validation and before any sink. The
recorder validates each rule output and the complete sanitizer output again.
`AuditSanitizationResult.Envelope` must still leave `Sanitization` and
`Integrity` null; the recorder applies the returned Stamp and computed Integrity
only after protected-fact comparison and safe-snapshot validation.

The recorder also computes a hand-written protected-fact projection before and
after `IAuditSanitizer`. These fields must compare exactly:

```text
ContractVersion
AuditId
OccurredAt
TenantId
CorrelationId / CausationId
ParentAuditId / PreviousAuditId

Actor.Kind / Actor.Id
Actor.InitiatedBy / Actor.OnBehalfOf
Actor.DelegationId / Actor.ImpersonationId

Action
Target
Outcome.Status / Outcome.Code

Runtime identity fields, duration, and references
Descriptor context
Evidence references
```

The sanitizer may minimize, remove, or transform only:

```text
Actor.DisplayName
Outcome.SafeSummary
Tags
DataSnapshot
Payload
```

Payload rules may transform only `Payload.Data`; Kind and Version remain equal
to the candidate. Artifact rules may transform only the sanitized value/hash
fields and must return the original Kind. The top-level sanitizer may minimize
by removing a Payload, DataSnapshot, or artifact, but neither it nor a rule may
add a new Payload/artifact Kind that did not exist in the candidate. A future
contract must explicitly define any Kind mapping.

Any protected-projection difference, Payload Kind/Version rewrite, or retained
Artifact Kind rewrite returns Rejected with
`AUDIT_SANITIZER_REWROTE_PROTECTED_FACT`; no sink is called. Structural
safe-snapshot validation still runs after this equality check. Thus a
structurally valid Actor or Outcome cannot silently replace the producer-owned
fact meaning.

### 9.5 Canonical hash integration

`IAuditIntegrityHasher` is the Accountability-facing adapter that returns the
structured `CanonicalHash`; its default implementation delegates to the shared
Canonical Hash Runtime. Do not create an ad hoc Accountability SHA-256 service. Add
`CanonicalHashArtifactNames.AccountabilityRecord` and project the sanitized
Envelope through the existing `ICanonicalHashComputer.ComputeFromProjection`.

```csharp
public interface IAuditIntegrityHasher
{
    CanonicalHash Compute(AuditEnvelope sanitizedCanonicalEnvelope);
}
```

Normative metadata:

```text
ArtifactKind          = AccountabilityRecord
Purpose               = AuditEvidence
Scope                 = InternalFull
ContractVersion       = canonical-hash-v1 (existing runtime contract)
CanonicalShapeVersion = accountability-record-hash-v1
AlgorithmVersion      = existing Canonical Hash Runtime algorithm version
```

The hand-written projector uses `Utf8JsonWriter`; it never uses reflection or
`JsonSerializer(object)`. It canonical-sorts unordered collections and object
properties. Arrays inside a producer payload preserve declared order. Duplicate
JSON object property names are rejected.

### 9.6 In-memory sink

`InMemoryAuditSink` is explicitly development/test only. It must:

- be thread-safe;
- snapshot on write and read;
- expose deterministic read order to its concrete test API;
- implement Accepted/Duplicate/Conflict exactly;
- never register implicitly as a production default;
- make no multi-process or restart durability claim.

The public `IAuditSink` remains write-only. Shared contract tests use a
test-side read probe/driver so Phase 9b providers can verify persistence without
polluting the runtime contract with a general query API.

---

## 10. Normative v1 Limits and Data Minimization

`AuditContractLimits` defines hard upper bounds. Options may lower them but may
not raise them without a contract version change.

Contract v1 freezes these maxima. Candidate limits are evaluated before any
sanitization rule; safe limits are evaluated again after sanitization. Both use
bounded AOT-safe UTF-8 JSON measurement without reflection:

```text
MaxIdentifierLength          = 256 characters
MaxSemanticKindLength        = 128 characters
MaxActionNameLength          = 512 characters
MaxSafeSummaryLength         = 1,024 characters
MaxTags                      = 32
MaxTagKeyLength              = 128 characters
MaxTagValueLength            = 512 characters
MaxDescriptorReferences      = 32
MaxRuntimeReferences         = 64
MaxEvidenceReferences        = 64
MaxDataArtifacts             = 16
MaxSingleArtifactBytes       = 65,536 bytes
MaxPayloadBytes              = 65,536 bytes
MaxCandidateEnvelopeBytes    = 262,144 bytes
MaxSafeEnvelopeBytes         = 262,144 bytes
```

`MaxPayloadBytes` and `MaxSingleArtifactBytes` apply independently to both the
input candidate and sanitized output. They are not merely post-sanitization
limits. `MaxCandidateEnvelopeBytes` prevents an oversized candidate from
reaching the sanitizer; `MaxSafeEnvelopeBytes` bounds the exact snapshot sent to
sinks. Candidate overflow returns `AUDIT_LIMIT_EXCEEDED` and invokes neither
sanitization rules nor sinks.

Validation is ordinal and deterministic. Empty/whitespace IDs, self-parenting,
invalid versions, duplicate references, duplicate tags, unknown Outcome status,
invalid CanonicalHash metadata, and invalid JsonElement shapes are rejected.
`ParentAuditId` and `PreviousAuditId` must each differ from `AuditId`. They are
validated as independent relations; graph loop checks that require a lineage
reader remain deferred.

Stable semantic Kinds use the ordinal grammar
`^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$`. Artifact bytes are measured over the
candidate representation before rules and the sanitized representation after
rules. Passing the candidate limit does not authorize raw capture or retention.

Unknown extension semantic Kinds are accepted when their grammar and bounds are
valid because Kind is an extensible semantic namespace. Outcome Status is a
closed v1 state machine and unknown values are rejected. Stable issue codes
include at minimum:

```text
AUDIT_REQUIRED_FIELD_MISSING
AUDIT_INVALID_SEMANTIC_KIND
AUDIT_UNKNOWN_OUTCOME_STATUS
AUDIT_SELF_RELATION
AUDIT_DUPLICATE_REFERENCE
AUDIT_LIMIT_EXCEEDED
AUDIT_INVALID_HASH_METADATA
AUDIT_UNKNOWN_SANITIZATION_RULE
AUDIT_SANITIZATION_RULE_FAILED
AUDIT_SANITIZED_OUTPUT_INVALID
AUDIT_SANITIZER_REWROTE_PROTECTED_FACT
AUDIT_SINK_RESULT_MISMATCH
```

The default adapters do not include IP address or User-Agent in the
Accountability fact. Those remain technical observation fields and may be added
only by an explicit bounded/sanitized capture policy.

---

## 11. JSON Contract and NativeAOT

The abstractions project declares one generated context:

```csharp
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonContractSurface(
    typeof(IAuditSink),
    ExcludedParameterTypes = new[] { typeof(CancellationToken) })]
public sealed partial class AccountabilityJsonSerializerContext
    : JsonSerializerContext;
```

BuildTasks generate all direct roots before CoreCompile. STJ source generation
owns transitive metadata. There is no handwritten root ledger or runtime scan.

Producer-specific payload DTOs remain in producer assemblies and use their own
source-generated contexts before becoming JsonElement. Accountability
Abstractions does not reference Capability, Workflow, Agent, MCP, or HumanTask
payload types.

NativeAOT acceptance extends the existing Procurement fixture and script. It
must publish with `CrestCreatesPublishMode=aot`, complete native link, execute
the original linux-x64 binary, exercise the real HTTP → Capability → Workflow
Accountability path, and print:

```text
CRESTCREATES_ACCOUNTABILITY_OK
```

The fixture must also invoke one woven `[AuditedMo]` method and assert its
separate `method.invoke` fact. If the existing Procurement application is not a
Rougamo weaving consumer, add a dedicated AOT fixture method/project to the same
sample solution. A JIT-only test is not sufficient to claim the AOP adapter
NativeAOT-verified.

---

## 12. HTTP and AOP Adapter

### 12.1 HTTP root scope

Before calling `next`, the middleware allocates:

- HTTP AuditId;
- an HTTP operation ID independent from ASP.NET request and Activity IDs;
- a new CorrelationId unless a trusted host integration explicitly provides one;
- Actor and Tenant from trusted framework contexts.

It pushes an `AuditOperationContext` for the request. Child method/Capability/
Workflow producers consume the scope. The HTTP fact is recorded after the
response outcome is known and before the scope is popped.

Observation mapping:

```text
Action.Kind       = http.request
Action.Name       = METHOD + normalized endpoint route template
Target.Kind       = http.endpoint
Target.Id         = endpoint ID or METHOD + route template
Runtime.RequestId = HttpContext.TraceIdentifier
Runtime.TraceId   = Activity.Current.TraceId when present
Runtime.SpanId    = Activity.Current.SpanId when present
```

The full URL and query string are not used. Request/response bodies are not
captured by the Phase 9a adapter.

`OccurredAt` is the terminal outcome observation time. HTTP status alone does
not prove governance rejection. Outcome mapping is:

| Observed path | Outcome |
|---|---|
| completed response below 400 | succeeded |
| typed authorization/validation/governance rejection | rejected; stable framework code |
| `OperationCanceledException` or `RequestAborted` | cancelled |
| unhandled exception | failed; Code=`UNHANDLED_EXCEPTION` |
| 5xx response without observed exception | failed; Code=`HTTP_<status>` |
| generic unclassified 4xx | failed; Code=`HTTP_<status>` |

An adapter may use `rejected` only when a typed framework classification proves
that a rule rejected the request. It must not infer that meaning from 400, 401,
403, 404, 409, 422, or 429 alone.

Authenticated actor mapping uses the trusted NameIdentifier claim as Actor.Id;
Name is display-only. Anonymous requests use Kind/Id `anonymous`. Missing actor
data uses explicit `unknown`; username is never promoted to identity.

### 12.2 Method fact

Each `[AuditedMo]` invocation allocates a distinct AuditId and operation ID.
Rougamo `MethodContext.Datas` holds the invocation state and ambient scope so
nested calls compose correctly and the scope is disposed in `OnExitAsync`.

```text
Action.Kind = method.invoke
Action.Name = explicit actionName or stable Namespace.Type.Method
Target.Kind = application.method
Target.Id   = stable Namespace.Type.Method
```

Inside HTTP:

```text
CorrelationId = HTTP CorrelationId
CausationId    = enclosing operation ID
ParentAuditId  = enclosing scope's EnclosingAuditId
```

Standalone methods create a root correlation and explicit unknown/system actor
when no trusted identity is available.

`IncludeParameters` and `IncludeResult` remain legacy API flags but do not cause
the Accountability mainline to serialize arbitrary objects. A future explicit
AOT-safe `IAuditDataSnapshotProvider` may restore selected capture.

### 12.3 Legacy HTTP compatibility

`AuditLog`/`AuditLogService` provide ordinary append persistence, not conditional
insert plus existing-integrity lookup. Therefore they are not registered as an
`IAuditSink` in Phase 9a and do not count toward recorder acceptance or
`AuditRecordId` closure.

A pure `AuditEnvelope → AuditLog` mapping may remain for migration/export tests,
but mapping does not confer sink semantics. A future legacy persistence adapter
may implement `IAuditSink` only after it independently satisfies the complete
Accepted/Duplicate/Conflict contract with durable conditional insert by AuditId.
No process-local side dictionary may pretend to supply those guarantees.

`AuditLogWriter` may remain as a compatibility adapter into `IAuditRecorder`; it
must not also write `AuditLog` directly. Independent double write is forbidden.

`HideErrors=false` cannot make a post-commit Accountability failure replace the
HTTP outcome. Required sink presence is handled at startup, not in middleware
`finally`.

---

## 13. Capability Adapter

### 13.1 Context propagation

Capability context creation consumes the current `AuditOperationContext` before
projection-specific configuration runs:

```text
CorrelationId = ambient CorrelationId or new root ID
CausationId   = ambient OperationId
ParentAuditId = ambient EnclosingAuditId
```

`CapabilityExecutionContext.CorrelationId` must no longer silently create an
unrelated value before the ambient bridge can run. Projections may explicitly
override the derived context when they own a stronger protocol identity.

Dynamic API removes `CausationId = TraceIdentifier`. TraceIdentifier remains a
runtime RequestId only. Agent and MCP continue to preserve their explicit
invocation/request causation IDs.

### 13.2 Fact mapping

```text
Action.Kind       = capability.execute
Action.Name       = CapabilityId
Target.Kind       = capability
Target.Id         = CapabilityId
Target.Version    = CapabilityVersion
Descriptor        = capability id/version/structured contract hash
Runtime.Execution = middleware ExecutionId
Runtime.Source    = InvocationSource stable name
Runtime.Duration  = elapsed duration
```

`OccurredAt` is the terminal result/exception/cancellation observation time.

The pipeline retains the full structured `CanonicalHash` returned by the
descriptor stable hash builder. The existing string `CapabilityContractHash`
may remain as a compatibility projection of `.Value`, but the Accountability
descriptor reference uses structured hash metadata.

Actor means effective invocation authority, not the Handler class that executes
the method. Mapping priority is:

```text
explicit producer-provided AuditActor
    → trusted current user for direct user invocation
    → workflow instance for Workflow-triggered invocation, with original
      user/Agent preserved in InitiatedBy
    → agent for Agent-triggered invocation, with human/system authority in
      InitiatedBy or OnBehalfOf when known
    → reserved runtime actor from InvocationSource and known runtime reference
    → explicit unknown
```

CapabilityId is never used as a fake Agent, Workflow, or HumanTask ActorId.

### 13.3 Middleware/result restructuring

AuditId and ExecutionId are created before `next`. The middleware pushes a
Capability operation scope so a Workflow started by the handler links to it.

This is an execution-fact boundary, not a dispatch-attempt boundary. The current
pipeline can reject an unknown Capability descriptor before constructing
`CapabilityExecutionContext` or entering middleware. Phase 9a therefore
guarantees one fact only for resolved Capability executions that enter the
Capability pipeline. `CAPABILITY_NOT_FOUND` is explicitly outside this fact and
is covered by the enclosing HTTP/protocol/governance observation; a future
dispatch-attempt fact is deferred.

```text
allocate IDs and scope
    → execute next
    → capture returned result or thrown exception/cancellation
    → record fact with independent bounded write token
    → store accepted AuditId in execution context
    → attach AuditRecordId to returned or outer-catch result
    → return result or preserve original exception path
```

`AuditRecordId` is populated only when at least one sink returns Accepted or
Duplicate. A generated-but-unaccepted ID is not exposed as an existing record.

The outer CapabilityPipeline catch uses the execution-context AuditRecordId when
constructing failure/timeout results. Returned success/failure results use an
explicit copy/with helper. Original exception stack is preserved.

### 13.4 Outcome mapping follows observed facts

| Observed path | Outcome |
|---|---|
| successful result | succeeded |
| failed result | failed; preserve canonical ErrorCode |
| CapabilityFailureException | failed; preserve ErrorCode |
| OperationCanceledException | cancelled; do not invent timeout |
| explicit TimedOut result | failed; Code=`CAPABILITY_TIMEOUT` |
| unknown exception | failed; Code=`UNHANDLED_EXCEPTION` |

The current pipeline maps an `OperationCanceledException` to legacy
`CapabilityExecutionStatus.TimedOut`. Phase 9a records what the middleware
actually observed—cancellation—until a real timeout classifier exists.

### 13.5 Compatibility store

`CapabilityExecutionRecord` and `ICapabilityAuditStore` remain obsolete
compatibility contracts but are no longer wired into the Accountability
mainline. The current interface is append-only: it cannot look up AuditId,
return an existing structured hash, perform conditional insert, or distinguish
Duplicate from Conflict. Therefore a general
`LegacyCapabilityAuditStoreSink : IAuditSink` is forbidden.

A pure `MapCompatibility(AuditEnvelope)` function may project a sanitized
Capability fact to `CapabilityExecutionRecord` for migration/export tests. Its
output is non-authoritative, does not participate in fan-out, does not count as
an accepted record, and cannot populate `CapabilityExecutionResult.AuditRecordId`.
Only a provider that independently implements durable conditional insert by
AuditId and existing-integrity lookup may register as `IAuditSink`. A side
dictionary is never sufficient.

`NullCapabilityAuditStore` is removed from the main registration path. Existing
source files remain unwired compatibility artifacts until a later removal phase;
new tests and Procurement use `InMemoryAuditSink`.

---

## 14. Workflow Lifecycle Adapter

### 14.1 Typed observer surface

Introduce:

```csharp
public interface IWorkflowLifecycleObserver
{
    ValueTask ObserveAsync(
        WorkflowLifecycleEvent lifecycleEvent,
        CancellationToken cancellationToken = default);
}
```

The existing publisher fans out deterministically to observers. Lifecycle
notifications are post-save. Observer failure is logged/diagnosed and never
rolls back or reclassifies committed Workflow state.

Accountability registers one observer:

```text
Workflow Runtime
    → IWorkflowLifecycleEventPublisher
    → IWorkflowLifecycleObserver[]
    → AccountabilityWorkflowLifecycleObserver
    → IAuditRecorder
```

### 14.2 Typed lifecycle event

Remove/deprecate `object? Payload`. The event carries at least:

```text
EventId, AuditId, EventType, OccurredAt
WorkflowInstanceId
WorkflowDescriptorId, Version, structured ContractHash
TenantId, CorrelationId, CausationId, ParentAuditId, PreviousAuditId
WorkflowRunOperationId
FromStatus, ToStatus
StepId, HumanTaskInstanceId, ReasonCode
AuditOrigin
```

`WorkflowLifecycleEventFactory` centralizes ID allocation, descriptor hash
mapping, status transition mapping, and actor/runtime reference construction.
Engine, runner, and continuation service do not duplicate string protocols.

### 14.3 Fact mapping

Workflow lifecycle facts use one fixed target and result mapping:

```text
Action.Kind         = workflow.lifecycle
Action.Name         = lifecycle EventType

Target.Kind         = workflow-instance
Target.Id           = WorkflowInstanceId

Descriptor          = workflow descriptor id/version/structured contract hash

Runtime.ExecutionId = WorkflowRunOperationId
Runtime.References  = workflow-instance
                      + workflow-step when known
                      + human-task-instance when known
```

Lifecycle EventType persistence uses an explicit exhaustive mapping to exactly
`workflow.started`, `workflow.suspended`, `workflow.resumed`,
`workflow.completed`, and `workflow.failed`; it never uses `enum.ToString()`.

References use the stable runtime-reference Kinds from §8.4 and canonical
ordering. A HumanTask instance may be a related Runtime Reference but never
replaces the Workflow instance as Target.

Outcome describes the Workflow's current business result, not whether the
lifecycle notification or Accountability write succeeded:

| Lifecycle Action.Name | Outcome |
|---|---|
| `workflow.started` | `indeterminate` |
| `workflow.suspended` | `indeterminate` |
| `workflow.resumed` | `indeterminate` |
| `workflow.completed` | `succeeded` |
| `workflow.failed` | `failed`; Code = stable ReasonCode |

`workflow.failed` requires a non-empty stable ReasonCode. The factory preserves
a known domain/runtime code or uses `WORKFLOW_FAILED` when no narrower safe code
exists; it never persists an exception message, stack, or CLR type name as Code.

### 14.4 Persisted origin and lifecycle linkage

`WorkflowExecutionRequest` accepts an optional explicit AuditOrigin. Otherwise
the engine snapshots the ambient operation context. `WorkflowInstance` stores
that origin and copies it in `Snapshot()`.

Every `Execute` and `Continue` invocation allocates a distinct
`WorkflowRunOperationId` before it performs transition work. The operation ID
causes transitions observed during that run; a lifecycle EventId identifies the
notification about a transition and does not cause the next transition.

The run pushes an `AuditOperationContext` whose Actor is the Workflow instance,
OperationId is `WorkflowRunOperationId`, and EnclosingAuditId is copied only
from the responsibility scope that actually contains the run. Capabilities
invoked by the run therefore use the Workflow as Actor, preserve the origin in
`InitiatedBy`, use the run operation as CausationId, and leave ParentAuditId null
when no Accountability fact truly encloses the continuation.

The instance preserves the last producer-allocated lifecycle AuditId for
sequence linkage. `PreviousAuditId` carries that sequence relation;
it is never copied into `CausationId` or `ParentAuditId` merely because it came
first. Exact mapping is:

```text
workflow.started
    CausationId     = initiating Capability/HTTP/Agent operation ID, when known
    ParentAuditId   = initiating/enclosing AuditId, when known
    PreviousAuditId = null

workflow.suspended|completed|failed in the initial run
    CausationId     = current WorkflowRunOperationId
    ParentAuditId   = run's enclosing AuditId, when the run is actually nested
    PreviousAuditId = previous lifecycle AuditId

workflow.resumed
    CausationId     = HumanTask completion event/operation ID, when available
    ParentAuditId   = HumanTask completion AuditId, when one exists
    PreviousAuditId = workflow.suspended AuditId

post-resume completed|failed
    CausationId     = continuation WorkflowRunOperationId
    ParentAuditId   = continuation run's enclosing AuditId, when actually nested
    PreviousAuditId = workflow.resumed AuditId
```

The run captures `EnclosingAuditId` only from the operation scope that actually
contains that Execute/Continue call. Persisted origin is not reused as Parent
after the enclosing operation has ended merely because it is historically
related.

`HumanTaskInstanceId` is a runtime entity identity and appears only as
`AuditRuntimeReference("human-task-instance", id)`. It is never a CausationId.
Phase 9a adds a required producer-allocated `EventId` to
`HumanTaskCompletedEvent`. `WorkflowContinuationRequest` carries optional
`TriggerOperationId` and `TriggerAuditId`; the HumanTask subscriber forwards the
completion EventId as `TriggerOperationId`. The EventId is transport identity
only and does not claim complete HumanTask Accountability. Other continuation
sources that have no trustworthy completion operation/event identity leave
resumed `CausationId` null rather than inventing one. The suspended lifecycle
AuditId still links through `PreviousAuditId`.

Workflow lifecycle Actor is `workflow` with the WorkflowInstanceId. The
persisted initiating user/Agent/system is retained as `Actor.InitiatedBy`; it is
not promoted to the direct Actor of an autonomous lifecycle transition.

A persisted producer-allocated AuditId may later be missing from a sink because
Phase 9a is best-effort. This is an explicit auditable gap, not permission to
rewrite history.

### 14.5 Transition ordering

For each lifecycle transition:

```text
validate transition
    → allocate WorkflowRunOperationId and lifecycle EventId/AuditId
    → mutate instance, origin/linkage, and status
    → save successfully
    → stamp OccurredAt as committed-transition observation time
    → publish typed lifecycle event
    → observer records Accountability
```

Store failure produces no committed-transition fact. Audit failure after save
does not roll back the state.

### 14.6 Phase 9a lifecycle scope

Only:

```text
workflow.started
workflow.suspended
workflow.resumed
workflow.completed
workflow.failed
```

Step telemetry, retry, and variable-change facts are excluded.

The suspended/resumed records may reference a HumanTask instance, but Phase 9a
does not claim who approved/rejected, what they saw, or why they were authorized.
That remains an explicit HumanTask Accountability gap.

---

## 15. Composition and Startup

`AddAccountability()` registers recorder, validator, default-deny sanitizer,
canonical projector/hasher, operation accessor, clock/ID services, and startup
validation. It does not register any sink.

First-party Hosts that enable the Phase 9a producers must explicitly call it and
register at least one intended sink. Development/test hosts may opt into
`AddInMemoryAccountability()`.

`AccountabilityOptions.RequireAtLeastOneSink` validates composition at startup.
Its library default is `false`. First-party production Hosts that enable Phase
9a producers set it to `true`; development/tests explicitly register
`InMemoryAuditSink` and may choose either setting. It cannot provide delivery
guarantees or post-commit strict behavior.

AuditLogging, Capability, and Workflow producers depend on `IAuditRecorder` and
fail startup composition if the required foundation is absent. An optional
Null recorder is forbidden because it hides a broken mainline.

---

## 16. Migration State

After Phase 9a:

```text
AuditEnvelope = framework-wide Accountability source model
```

Compatibility roles:

```text
AuditContext                  = mutable HTTP observation/compatibility buffer
AuditLog                      = legacy persistence entity
CapabilityExecutionRecord    = legacy execution-summary DTO
ICapabilityAuditStore         = obsolete append-only compatibility API, unwired
```

Legacy model mappings are pure compatibility/export projections. They are not
sinks and do not participate in accepted-record closure. A legacy provider may
become an `IAuditSink` only by independently satisfying the full idempotency,
structured-integrity, conflict, and conditional-insert contract.

Unchanged governance-control roles:

```text
AgentToolGovernanceDecisionRecord
AgentToolGovernancePreDispatchRecord
AgentToolGovernanceFinalizationRecord
IAgentToolGovernanceAuditor
```

These Agent records may later project accepted outcomes into Accountability, but
their checkpoint/fencing/reconciliation protocol remains independent.

---

## 17. Case Matrix

| Category | Case | Expected |
|---|---|---|
| Happy | authenticated HTTP success | user actor, tenant, route template, success |
| Happy | anonymous HTTP | explicit anonymous actor |
| Happy | multiple audited methods | separate method facts; no overwrite |
| Happy | Capability success/failure | structured descriptor hash and source retained |
| Happy | Workflow start/suspend | Workflow instance target; indeterminate; facts only after save |
| Happy | Workflow resume/complete | resumed indeterminate; completed succeeded; origin survives |
| Boundary | host-level system fact | TenantId may be null |
| Boundary | unknown actor | explicit unknown; no display-name inference |
| Boundary | no data/payload | valid default |
| Boundary | custom semantic kind | accepted when stable grammar/limits pass |
| Boundary | presentation minimization | sanitizer may remove display name, summary, tags, or captured data |
| Boundary | same ID/same hash | Duplicate/idempotent success |
| Failure | same ID/different hash | Conflict; first snapshot retained |
| Failure | conflict plus provider exception | Conflict remains in SinkResults; exception remains in SinkFailures |
| Failure | missing correlation | Rejected; no sink called |
| Failure | sanitizer rewrites protected fact | Rejected with stable issue; no sink called |
| Failure | oversized candidate payload/artifact | Rejected before rule or sink invocation |
| Failure | self ParentAuditId/PreviousAuditId | Rejected with stable issue code |
| Failure | previous Workflow lifecycle fact | linked only by PreviousAuditId; never direct cause |
| Failure | HumanTask instance identity | runtime reference only; never CausationId |
| Failure | resume without trustworthy trigger identity | null CausationId; no invented identity |
| Failure | duplicate retry | no new global record time; provider-local first acceptance retained when known |
| Failure | unknown raw payload kind | Rejected; raw content reaches no sink |
| Failure | sanitizer throws | Rejected; no fallback |
| Failure | legacy append-only store | cannot register as IAuditSink or count as accepted |
| Failure | no sink | NoSinkConfigured |
| Failure | one sink fails | remaining sinks attempted; partial result if any accepts |
| Failure | all sinks fail/conflict | Failed |
| Failure | business token cancelled after commit | independent bounded audit attempt |
| Failure | Workflow store save fails | no transition fact |
| Failure | audit fails after Workflow save | state remains committed |
| Failure | Workflow failed | Workflow instance target; failed with stable ReasonCode |
| Composition | HTTP → Capability | shared CorrelationId; request operation causes capability |
| Composition | Capability → Workflow | capability operation/audit links workflow start |
| Composition | Workflow lifecycle | ParentAuditId containment and PreviousAuditId sequence remain distinguishable |
| Composition | resume after HumanTask | completion EventId causes resume when known; HumanTask instance is a runtime ref |
| Composition | Workflow actor | workflow is Actor; initiating principal survives as InitiatedBy |
| Composition | parallel operation scopes | immutable AsyncLocal sibling frames do not interfere |
| Composition | unresolved Capability | explicitly outside resolved execution-fact boundary |
| Composition | Agent/MCP → Capability | source and explicit protocol causation preserved |
| Composition | legacy models | pure Envelope-to-legacy mapping; no acceptance or double write |
| Composition | NativeAOT | generated JSON only; native binary prints sentinel |

---

## 18. Acceptance Tests

### 18.1 Contract and validation

```text
AuditEnvelopeContractTests
  RequiresAuditIdCorrelationActorActionTargetOutcome
  AllowsTenantlessSystemFact
  UsesImmutableCollectionsAndClonedJsonElements
  RejectsSelfParentAndDuplicateReferences
  EnforcesAllHardLimits
  PreservesUnknownStableExtensionKinds
  RejectsUnknownOutcomeStatus
  RoundTripsWithGeneratedJsonTypeInfo

AuditCausalityContractTests
  NeverSubstitutesTraceIdForCorrelationOrCausation
  RootFactAllowsNoCauseOrParent
  NestedOperationUsesOperationIdAndEnclosingAuditIdExactly
  ParentAuditIdNeverRepresentsSequence
  PreviousAuditIdLinksLifecycleSequence
  ScopeStackRejectsOutOfOrderDispose
  ParallelSiblingScopesDoNotInterfere
  ChildScopeDoesNotMutateParentExecutionContext
  NestedAwaitPreservesCurrentScope
  OutOfOrderDisposeFailsWithoutCorruptingParent

AuditDataContractTests
  DefaultsToNoCapture
  RejectsUnknownRawPayload
  RejectsDuplicateJsonProperties
  RequiresCapturePolicySanitizerPolicyAndRuleVersions
  RequiresContentHashBasis
  EnforcesPayloadAndArtifactLimitsBeforeAndAfterSanitization
  RejectsOversizedCandidateBeforeInvokingRules

AuditRuntimeSemanticMappingTests
  InvocationSourceUsesStableExplicitMapping
  InvocationSourceMappingIsExhaustive
  ProducerIncludesStableServiceAndApplicationReferencesWhenKnown

AuditSanitizationRuleRegistryTests
  RejectsDuplicatePayloadKindOwnerAtStartup
  RejectsDuplicateArtifactKindOwnerAtStartup
  HasNoWildcardOrPassThroughFallback
  RejectsUnknownKindsWithStableIssueCode
  RejectsRuleExceptionWithStableIssueCode
  RevalidatesRuleOutputAsSafeSnapshot
  DistinguishesCompositionPolicyVersionFromRuleVersion
  PayloadRuleCannotChangePayloadKindOrVersion
  ArtifactRuleCannotChangeArtifactKind

AccountabilityCompositionTests
  LibraryDefaultDoesNotRequireSink
  FirstPartyProductionHostsRequireAtLeastOneSink
  DevelopmentHostRegistersInMemorySinkExplicitly
```

### 18.2 Recorder/hash/sink

```text
DefaultAuditRecorderTests
  ValidatesBeforeSanitizing
  SanitizesBeforeAnySink
  ValidatesSanitizedSnapshot
  ComputesHashWithCanonicalHashRuntime
  ExcludesIntegrityAndAttemptMetadataFromHash
  IncludesSanitizationStampInHash
  OrdersSinksByStableIdAndAttemptsAll
  AggregatesRecordedPartialFailedNoSinkRejected
  ConflictIsPreservedInRecordResult
  ConflictIsNotReportedAsProviderFailure
  RejectedResultContainsStableIssueCodes
  AcceptedSinkIdsAreDerivedNotDuplicated
  MultiSinkHasNoFalseGlobalRecordedTime
  ProcessedAtIsAttemptMetadataNotFactMetadata
  SinkCannotReturnDifferentSinkId
  SinkCannotReturnDifferentAuditId
  SinkCannotReturnDifferentIntegrity
  SanitizerCannotRewriteProtectedFactFields
  SanitizerMayMinimizePresentationFields
  SanitizerRewriteRejectionCallsNoSink
  DoesNotMutateProducerCandidate

InMemoryAuditSinkContractTests
  AcceptsNewRecord
  SameIdAndHashReturnsDuplicate
  SameIdAndDifferentHashReturnsConflict
  StructuredExistingHashIsPreserved
  DuplicateRetryReturnsOriginalFirstAcceptedAtWhenKnown
  SinkAcceptanceTimeIsProviderLocal
  SnapshotsOnWriteAndRead
  IsThreadSafeUnderConcurrentIdenticalWrite
  HasDeterministicReadOrder
```

The abstract sink suite uses a provider-specific test read driver. Phase 9b
database sinks must reuse it.

### 18.3 HTTP/AOP

```text
HttpAccountabilityAdapterTests
  EmitsSucceededForCompletedSuccess
  NoBuiltInHttpRejectedPathWithoutTypedFirstPartyProducer
  Generic4xxIsFailedWithStableStatusCode
  FiveHundredWithoutExceptionIsFailed
  RequestAbortIsCancelled
  HttpOccurredAtIsTerminalObservationTime
  UsesRouteTemplateNotDisplayUrlOrQuery
  SeparatesRequestIdTraceIdSpanId
  DoesNotCaptureBodyHeadersIpOrUserAgentByDefault
  PreservesTenantActorAndCorrelation
  AuditFailureDoesNotReplaceHttpOutcome

AuditedMethodAccountabilityTests
  EmitsOneFactPerInvocation
  LinksToHttpOrEnclosingMethodScope
  MultipleMethodsNeverOverwrite
  StandaloneMethodCreatesRootFact
  DoesNotReflectionSerializeArgumentsOrResult
  AuditFailureDoesNotReplaceMethodResult
  AuditFailureDoesNotReplaceOriginalMethodException
  AlwaysDisposesScope
```

### 18.4 Capability

```text
CapabilityAccountabilityMiddlewareTests
  EmitsReturnedSuccessAndFailure
  EmitsCapabilityFailureCancellationAndUnhandledException
  ResolvedCapabilityEnteringPipelineAlwaysEmitsFact
  UnresolvedCapabilityIsExplicitlyOutsideExecutionFactBoundary
  CapabilityOccurredAtIsTerminalObservationTime
  CapabilityActorFollowsEffectiveInvocationAuthority
  PreservesOriginalExceptionStack
  CarriesCorrelationCausationParentAndActor
  CarriesStructuredDescriptorContractHash
  AttachesAuditRecordIdOnlyWhenAccepted
  OuterCatchResultReceivesAcceptedAuditRecordId
  RemovesDynamicApiTraceIdentifierCausationSubstitution
  AuditFailureDoesNotChangeCapabilityResult
```

### 18.5 Workflow

```text
WorkflowAccountabilityObserverTests
  RecordsStartedSuspendedResumedCompletedFailedAfterSave
  DoesNotRecordWhenStoreSaveFails
  ObserverFailureDoesNotRollbackState
  SnapshotPreservesAuditOriginAndLastLifecycleLinkage
  SuspensionResumePreservesCorrelationAndInitiatingActor
  PreviousLifecycleEventIsNotUsedAsCausation
  HumanTaskInstanceIdIsRuntimeReferenceNotCausation
  ResumeUsesCompletionEventIdWhenAvailable
  ResumeAllowsUnknownCauseWithoutInventingIdentity
  WorkflowRunOperationCausesTerminalTransition
  WorkflowLifecycleActorIsWorkflowNotInitiatingUser
  WorkflowActorPreservesInitiatedBy
  WorkflowOccurredAtIsCommittedTransitionTime
  WorkflowLifecycleTargetsWorkflowInstance
  WorkflowLifecycleReferencesWorkflowDescriptor
  WorkflowStartedSuspendedResumedAreIndeterminate
  WorkflowCompletedIsSucceeded
  WorkflowFailedIsFailedWithStableReasonCode
  IncludesDescriptorVersionAndStructuredHash
  DoesNotExposeObjectPayload
```

### 18.6 Architecture

```text
AccountabilityArchitectureTests
  AbstractionsDoNotReferenceProducerOrAspNetCoreAssemblies
  ProducersReferenceOnlyAccountabilityAbstractions
  ProducersDoNotReferenceIAuditSink
  EnvelopeContainsNoObjectOrMutableCollectionPayload
  AccountabilityUsesJsonContractBuildTasks
  AccountabilityUsesCanonicalHashRuntime
  NoAccountabilityReflectionJsonOrIL2026Suppression
  CapabilityMiddlewareDoesNotUseICapabilityAuditStore
  LegacyAppendOnlyStoreIsNotRegisteredAsAuditSink
  CompatibilityMappingDoesNotCountAsAcceptedRecord
  CapabilityAuditRecordIdComesOnlyFromContractCompliantSink
  NoSideDictionaryPretendsToProvideDurableIdempotency
  WorkflowLifecycleEventContainsNoObjectPayload
  AgentGovernanceAuditorRemainsIndependent
  NewRuntimeSpecificPrimaryAuditStoresAreForbidden
```

### 18.7 Procurement mainline and NativeAOT

Extend the existing sample/tests rather than creating another sample:

```text
POST /api/procurement/requests
    → http.request fact
    → procurement.submit capability fact
    → workflow.started fact
    → workflow.suspended fact
```

Assertions:

1. All facts share one CorrelationId.
2. Capability CausationId is the generated HTTP operation ID and differs from
   ASP.NET TraceIdentifier/Activity trace identifiers.
3. Workflow start links to the Capability ExecutionId/AuditId.
4. Workflow suspend uses the current WorkflowRunOperationId as cause and the
   prior lifecycle AuditId only as PreviousAuditId.
5. Capability and Workflow descriptor id/version/structured hashes are correct.
6. Invocation sources and actors are explicit.
7. No body, token, input DTO, exception stack, or visible HumanTask data exists.
8. Capability result AuditRecordId resolves in the in-memory sink.
9. Existing Agent, MCP, and HumanTask source assertions migrate from the legacy
   Capability store to Accountability facts where applicable.
10. Native publish-link-run prints `CRESTCREATES_ACCOUNTABILITY_OK`.

HumanTask completion → internal decision Capability → Workflow resume/complete
may be asserted for Capability/Workflow facts, but the test must not claim a
complete HumanTask decision fact.

---

## 19. Implementation Slices

### PR 1 — Contract kernel and generated JSON

- projects, immutable contracts, semantic constants, frozen v1 hard limits;
- candidate and safe-snapshot byte budgets;
- operation context and persisted origin;
- JSON Contract BuildTasks and generated context;
- contract/AOT JSON round-trip and dependency boundaries.

**Gate:** no object/mutable payload escape hatch; Phase 9b contract is
provider-neutral; generated roots are authoritative.

### PR 2 — Recorder, sanitizer, canonical hash, in-memory sink

- validator, typed sanitization rule registries, and default-deny sanitizer;
- protected-fact projector/equality guard and stable rewrite rejection;
- Canonical Hash Runtime projection;
- explicit sink write results and fan-out aggregation;
- thread-safe in-memory sink and shared sink contract suite;
- startup sink validation.

**Gate:** every sink sees only a safe immutable snapshot; replay/conflict is
unambiguous; no second SHA-256 contract exists.

### PR 3 — HTTP, AOP, and Capability adapters

- request operation scope and safe route mapping;
- one fact per audited method and removal of reflection serialization;
- AOP post-fact failure preservation for returned results and original exceptions;
- Capability ambient propagation, structured descriptor hash, result closure;
- pure legacy AuditLog and Capability mapping tools only; append-only stores stay
  unwired;
- removal of independent double writes and Null store mainline.

**Gate:** HTTP and methods are separate; Dynamic API does not misuse trace as
causation; audit failure cannot replace committed/observed business outcome.

### PR 4 — Workflow and Procurement mainline

- typed observer/event/factory;
- fixed Workflow lifecycle Action/Target/Outcome mapping;
- persisted origin and lifecycle linkage;
- five lifecycle facts after save;
- Procurement acceptance and existing NativeAOT fixture extension;
- solution, CI gate, and memory updates after implementation evidence exists.

**Gate:** suspend/resume preserves responsibility context; save-before-publish
is enforced; no HumanTask completion claim is overstated.

---

## 20. Exit Criteria

Phase 9a closes only when all are true:

1. `AuditEnvelope` is the only framework-wide Accountability source model.
2. HTTP requests and every `[AuditedMo]` invocation emit separate facts.
3. Every resolved Capability execution that enters the pipeline emits a fact for
   returned/exception/cancellation paths with accepted AuditRecordId closure;
   unresolved dispatch is explicitly outside this boundary.
4. Correlation, direct causation, enclosing ParentAuditId, sequential
   PreviousAuditId, actor, invocation source, descriptor version, and structured
   contract hash are preserved without conflating relation types.
5. Workflow started/suspended/resumed/completed/failed emit only after store save.
6. Workflow origin and lifecycle linkage survive snapshot and suspend/resume.
7. Every sink sees only a validated, sanitized, immutable snapshot.
8. Default adapters capture no raw input/output/request/response content.
9. RecordHash uses the existing Canonical Hash Runtime and v1 profile.
10. In-memory and future durable sinks share explicit Accepted/Duplicate/Conflict
    semantics and contract tests.
11. Conflict, provider failure, and recorder rejection remain distinguishable in
    `AuditRecordResult`, and all integrity values remain structured
    `CanonicalHash` values.
12. Legacy AuditLog and Capability append-only stores remain unwired obsolete
    compatibility APIs/pure mapping targets, not `IAuditSink` implementations.
13. Agent Tool Governance Auditor remains the execution-control protocol.
14. No reflection JSON fallback, object payload, or Accountability IL2026
    suppression exists.
15. Procurement proves HTTP → Capability → Workflow responsibility composition.
16. Existing linux-x64 AOT fixture publishes, links, runs the original binary,
    and prints the Accountability sentinel.
17. Phase 9b can implement `IAuditSink` without redefining core record,
    idempotency, conflict, sanitization, or hash semantics.
18. Envelope OccurredAt, recorder ProcessedAt, and provider-local FirstAcceptedAt
    remain distinct; no global RecordedAt is manufactured.
19. Payload/artifact sanitization has one active owner per Kind, deterministic
    versioned rule identity, deny-unknown behavior, and safe-output revalidation.
20. Contract v1 limits and stable rejection issue codes are frozen by this Spec.
21. Workflow run operation, enclosing parent, lifecycle sequence, HumanTask
    runtime identity, direct Actor, and initiating Actor remain distinct.
22. Workflow lifecycle facts target the Workflow instance, reference the
    descriptor, and use the fixed indeterminate/succeeded/failed outcome map.
23. The sanitizer cannot change the protected fact projection; permitted
    presentation/data minimization and rule Kind/version preservation are
    enforced before any sink call.

---

## 21. Explicit Deferred Work

```text
Phase 9a+ HumanTask Accountability
    assignment / claim / delegation / decision / visible-data evidence

Phase 9a+ Agent Governance Accountability Adapter
    project governance outcomes into post-fact Accountability
    without replacing checkpoints/finalization/reconciliation

Phase 9a+ Agent Memory Accountability
    recall / promotion / rejection / supersession / expansion / sanitization

Phase 9a+ Event Accountability
    publish / consume / retry / dead-letter

Phase 9a+ Descriptor Governance Accountability
    author / review / activate / reject / supersede

Phase 9b Durable Sink
    persistence / indexes / provider transaction / retention contract

Future Lineage
    correlation / causation / runtime-ref / evidence-ref readers

Future Tamper Evidence
    chain / signature / checkpoint / verification
```

---

## 22. Permanent Architectural Rule

```text
Runtime-specific typed payloads are allowed.

Independent runtime-specific Accountability source models are not allowed.
```

And:

```text
Accountability records what the runtime claims happened.

Durable and tamper-evident providers later establish how strongly that claim
can be retained, reconciled, and verified.
```
