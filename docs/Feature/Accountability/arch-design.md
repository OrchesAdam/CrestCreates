# Accountability Runtime Foundation — Architecture Design

> **Status:** Implemented and merged
> **Phase:** 9a, Issue #39, PR #67
> **Last updated:** 2026-07-31

---

## 1. Purpose

Accountability records an immutable, post-fact responsibility claim:

```text
who or what bore effective responsibility
    + what action was observed
    + which target was affected
    + what terminal outcome was observed
    + which operation directly caused it
    + which enclosing fact or lifecycle predecessor relates to it
    + which descriptor contract and runtime identities governed it
```

It is not a technical log, trace span, business event, authorization decision,
or pre-dispatch governance checkpoint. An Accountability fact observes what
already happened; it must not drive, approve, reject, or roll back business
execution.

Phase 9a establishes the framework contract and first-party HTTP, method,
Capability, and Workflow producers. Durable providers, query APIs, retention,
Outbox delivery, signatures, and tamper-evident storage belong to later phases.

---

## 2. Unique Mainline

`AuditEnvelope` is the only framework-wide Accountability source model:

```text
HTTP / method / resolved Capability / committed Workflow transition
    → producer creates AuditEnvelope candidate
    → structural and semantic validation
    → sanitization
    → protected-fact comparison
    → safe-snapshot validation
    → canonical projection + Canonical Hash Runtime
    → concurrent IAuditSink fan-out
    → AuditRecordResult
```

The implementation is split into:

| Project | Responsibility |
|---|---|
| `CrestCreates.Accountability.Abstractions` | Immutable contracts, semantic names, operation context, recorder/sink/sanitizer interfaces |
| `CrestCreates.Accountability` | Validation, sanitization, canonical projection, recorder, identity generation, in-memory sink, startup validation |
| `CrestCreates.AuditLogging.Abstractions` | Declarative method-accountability bridge and `[AuditedMo]` |
| `CrestCreates.AuditLogging` | HTTP terminal observation, authenticated operation scope, method fact runtime |
| `CrestCreates.Capability` | Resolved Capability execution producer |
| `CrestCreates.Workflow` | Post-save Workflow lifecycle observer |

Legacy `AuditLog`, `AuditContext`, `CapabilityExecutionRecord`, and
`ICapabilityAuditStore` are compatibility models only. Append-only stores do not
provide the conditional insert and existing-integrity lookup required by
`IAuditSink`, so they are not wired into the mainline.

---

## 3. AuditEnvelope v1

The root contract contains:

| Area | Fields |
|---|---|
| Identity/time | `ContractVersion`, `AuditId`, `OccurredAt`, `TenantId` |
| Relationships | `CorrelationId`, `CausationId`, `ParentAuditId`, `PreviousAuditId` |
| Responsibility | `Actor`, `Action`, `Target`, `Outcome` |
| Execution | `Runtime`, `Descriptors` |
| Optional safe data | `DataSnapshot`, `Evidence`, `Payload`, ordinal `Tags` |
| Recorder-owned output | `Sanitization`, structured `Integrity` |

`OccurredAt` is the terminal outcome observation time. There is no global
`RecordedAt`, because multiple sinks can first accept the same fact at different
times. Attempt completion is reported by `AuditRecordResult.ProcessedAt`; a sink
may report its provider-local `FirstAcceptedAt`.

### 3.1 Relationship semantics

The three relationship fields are deliberately separate:

| Field | Meaning |
|---|---|
| `CausationId` | Direct operation, event, or decision identity that caused this fact |
| `ParentAuditId` | Enclosing Accountability fact |
| `PreviousAuditId` | Previous fact in the same subject or lifecycle sequence |

Containment is not sequence, and sequence is not causation. A previous Workflow
lifecycle fact must never be used as the direct cause of the next transition.
A HumanTask instance ID is a runtime entity reference, not a causation identity.

### 3.2 Actor semantics

`AuditActor` represents the effective authority principal, not the CLR component
that happened to execute code:

| Producer | Actor |
|---|---|
| Authenticated HTTP / user-triggered Capability | trusted user |
| Anonymous HTTP | anonymous |
| Workflow transition | Workflow instance |
| Workflow-triggered Capability | Workflow instance, with initiating actor retained |
| Agent-triggered Capability | trusted Agent when explicitly supplied; otherwise unknown |
| MCP-triggered Capability | trusted MCP client identity when available; otherwise unknown |

Technical execution identities belong in `AuditRuntimeContext`.

### 3.3 Stable semantics

First-party action kinds are:

```text
http.request
method.invoke
capability.execute
workflow.lifecycle
```

Known outcome statuses are:

```text
succeeded
failed
rejected
cancelled
skipped
indeterminate
```

Extension Kinds use lowercase stable segments separated by `.` or `-`.
Persisted semantic values must not depend on `enum.ToString()`, current culture,
or type names.

---

## 4. Validation and Sanitization Boundary

The recorder treats producer candidates and sanitizer output as untrusted:

```text
non-enumerating structural preflight
    → candidate validation
    → immutable snapshot
    → sanitizer
    → sanitizer-output validation
    → protected projection equality
    → safe-snapshot validation
```

Malformed candidates are returned as `Rejected` with stable
`AuditRecordIssue.Code` values. They do not become recorder internal failures,
and no sanitizer or sink is called after a blocking validation failure.

### 4.1 Protected facts

Sanitization cannot rewrite responsibility meaning, including:

- IDs, timestamps, tenant, correlation/causation/parent/previous relationships;
- Actor authority and delegation fields;
- Action, Target, and Outcome status/code;
- Runtime identities and references;
- Descriptor and evidence references.

The recorder compares a protected projection before and after sanitization.
Illegal rewrites are rejected with
`AUDIT_SANITIZER_REWROTE_PROTECTED_FACT`.

### 4.2 Sanitizable presentation/data

The sanitizer may minimize or transform:

- `Actor.DisplayName`;
- `Outcome.SafeSummary`;
- `Tags`;
- `DataSnapshot`;
- `Payload`.

Payload and artifact data require one explicitly registered typed rule per
stable Kind. There is no wildcard pass-through. Unknown Kinds, duplicate rule
ownership, rule exceptions, null rule output, Kind/version rewrites, or invalid
safe output are rejected.

The default sanitizer therefore acts as default-deny for non-empty Payload or
DataSnapshot content without a matching rule.

### 4.3 Frozen v1 limits

Important limits include:

| Limit | Value |
|---|---:|
| Identifier | 256 characters |
| Semantic Kind | 128 characters |
| Action name | 512 characters |
| Safe summary | 1,024 characters |
| Tags | 32 |
| Descriptor references | 32 |
| Runtime references | 64 |
| Evidence references | 64 |
| Data artifacts | 16 |
| Single artifact / Payload | 64 KiB |
| Candidate / safe Envelope | 256 KiB |

The safe Envelope byte limit and integrity hash use the same authoritative
canonical projection writer.

---

## 5. Integrity and Sink Contract

Accountability reuses the existing Canonical Hash Runtime. `CanonicalHash`
preserves algorithm, purpose, scope, contract version, canonical shape version,
and digest value; it is never reduced to a plain hash string.

The hash is:

- a deterministic content identity for idempotency and conflict detection;
- not sanitization;
- not authenticity proof;
- not a signature, hash chain, WORM guarantee, or tamper-proof claim.

### 5.1 IAuditSink result

Every sink returns one of:

| Status | Meaning |
|---|---|
| `Accepted` | First conditional acceptance of the AuditId |
| `Duplicate` | Same AuditId and identical structured integrity |
| `Conflict` | Same AuditId but different structured integrity |

`ExistingIntegrity` is present only for Conflict. Provider exceptions, timeout,
or unavailability are represented separately as `AuditSinkFailure`.

`AuditRecordResult.IsAccepted` is derived only from at least one Accepted or
Duplicate sink result. `Status = Recorded` alone cannot claim that a record
exists.

### 5.2 Fan-out and cancellation

The recorder:

1. orders sinks by ordinal `Id`;
2. invokes every sink before awaiting completion;
3. gives all sinks one shared total `WriteTimeout`;
4. returns results in deterministic sink order;
5. records unfinished sinks as timeout failures.

Caller cancellation cancels the recorder attempt and is propagated. Producers
whose business result or state transition is already committed call the recorder
with `CancellationToken.None`; the recorder then applies its own bounded write
budget.

`IAuditSink.WriteAsync` implementations must return their `ValueTask` promptly.
The timeout bounds asynchronous completion; it cannot isolate a provider that
blocks synchronously before returning.

---

## 6. First-party Producers

### 6.1 HTTP: terminal observer plus operation scope

HTTP has two different responsibilities and therefore two middleware:

```text
RequestLogging
AccountabilityHttpTerminalObserver
ExceptionHandling
Routing
MultiTenancy
Authentication
AccountabilityHttpOperationScope
TenantBoundary
Authorization
Endpoint / Capability / method / Workflow
```

The outer terminal observer allocates the HTTP AuditId, OperationId, and
CorrelationId before global exception handling, then waits until exception
conversion and response writing complete before materializing the HTTP fact.

The inner operation scope runs after tenant resolution and authentication. It
enriches the same request-local state with trusted Actor/Tenant data and pushes
the operation context used by Capability, method, and Workflow children.

Tenant-resolution or authentication failures are still converted by the global
error contract. Since the trusted scope was never established, their HTTP fact
uses unknown Actor, null TenantId, and no child scope.

HTTP facts use `METHOD + normalized route template`. Raw path, route values,
query strings, headers, IP, request/response bodies, exception messages, and
stacks are not captured by default.

### 6.2 Method invocation

`[AuditedMo]` emits a distinct `method.invoke` fact. It does not mutate the HTTP
fact and does not pass arguments, return values, or Exception objects to the
Accountability contract.

The declaration lives in `CrestCreates.AuditLogging.Abstractions`; the concrete
runtime owns fact materialization. The old assembly provides a type forward for
binary compatibility.

### 6.3 Capability

Every resolved Capability execution that enters the Capability Pipeline emits a
`capability.execute` fact for returned success/failure, cancellation, or
exception paths.

Descriptor resolution failure occurs before the resolved execution pipeline and
is deliberately outside this fact boundary. It remains visible through the
enclosing HTTP, MCP, Agent governance, or future dispatch-attempt observation.

`CapabilityExecutionResult.AuditRecordId` is populated only when at least one
sink Accepted or returned Duplicate.

### 6.4 Workflow

Workflow emits post-save lifecycle facts:

```text
workflow.started    → indeterminate
workflow.suspended  → indeterminate
workflow.resumed    → indeterminate
workflow.completed  → succeeded
workflow.failed     → failed + stable ReasonCode
```

The Target is always the Workflow instance. Descriptor identity/version/hash and
Workflow/HumanTask runtime references remain separate.

Each Execute/Continue call owns a distinct Workflow run operation. Lifecycle
sequence uses `PreviousAuditId`; transition cause uses the run or trusted
completion event operation. Observer failure never rolls back committed Workflow
state.

---

## 7. Composition and Startup Guarantees

`AddAccountability()` registers the Foundation and startup validator.

Library defaults are:

```text
WriteTimeout = 5 seconds
RequireAtLeastOneSink = false
```

First-party production hosts should set `RequireAtLeastOneSink = true`.
Development and tests should register an explicit `InMemoryAuditSink`.

AuditLogging, Capability, and Workflow register producer-owned startup
validators. A host that enables one of these producers without the
Accountability Foundation fails during startup instead of waiting for the first
request or execution.

Startup also rejects:

- zero, negative, infinite, or unsupported `WriteTimeout`;
- duplicate/invalid sink IDs;
- a required-sink configuration with no sink.

---

## 8. AOT and Contract Ownership

- JSON roots are derived from `[JsonContractSurface]` by JSON Contract
  BuildTasks; there is no handwritten transitive root ledger.
- Canonical hashing uses an explicit projection writer and the existing
  `ICanonicalHashComputer`; it does not reflection-serialize an object graph.
- Tags and all persisted ordering use ordinal comparison.
- The Procurement golden path verifies real HTTP → Capability → Workflow facts,
  real Application-assembly Rougamo weaving, and linux-x64 NativeAOT
  publish/link/run.

---

## 9. Phase 9b Provider Boundary

A durable Phase 9b provider implements `IAuditSink` and must independently
provide:

- conditional insert by AuditId;
- existing structured-integrity lookup;
- exact Accepted/Duplicate/Conflict semantics;
- provider-local first-acceptance metadata;
- immutable safe snapshots.

Provider tests reuse the runner-free
`tests/Shared/CrestCreates.Accountability.Testing` contract cases. A provider
must not reference the concrete Accountability runtime tests or the in-memory
sink.

Phase 9b may add persistence metadata such as `PersistedAt`, `InsertedAt`, or
`ReceivedAt`. Those values are provider metadata, not responsibility-fact fields
and not part of the v1 integrity projection.

---

## 10. Non-goals and Security Claims

Phase 9a does not provide:

- reliable delivery, Outbox, cross-sink atomicity, or distributed exactly-once;
- global ordering or a distributed clock;
- retention, cleanup, query, export, or lineage APIs;
- signatures, hash chains, tamper evidence, or legal-evidence guarantees;
- full HumanTask decision responsibility;
- event publish/consume/retry/dead-letter facts;
- replacement for Agent Tool Governance auditing.

Design specification:
`docs/superpowers/specs/2026-07-28-phase-9a-accountability-runtime-foundation-design.md`.
