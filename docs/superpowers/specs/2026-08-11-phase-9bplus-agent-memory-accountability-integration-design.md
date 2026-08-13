# Phase 9b+ — Agent Memory Accountability Integration Design Spec

**Issue:** [#56 — Phase 9b+ Agent Memory Accountability Integration](https://github.com/OrchesAdam/CrestCreates/issues/56)

**Depends on:** [#43 — Agent Memory & Context Compression Runtime](https://github.com/OrchesAdam/CrestCreates/issues/43), [#39 — Phase 9a Accountability Runtime Foundation](https://github.com/OrchesAdam/CrestCreates/issues/39), [#24 — Phase 9b Durable Persistence Foundation](https://github.com/OrchesAdam/CrestCreates/issues/24)

**Feeds into:** #55 Durable Agent Memory Store Provider

**Related but excluded:** #25 Transactional Outbox / reliable delivery, #73 Agent Tool pre-dispatch complexity consolidation

**Status:** APPROVED — frozen for Plan/TDD

**Design mode:** Case-first TDD; Red → Green → Review

**Date:** 2026-08-11

---

## 1. Decision Summary

Phase 9b+ adds one bridge from an established Agent Memory result to the
existing unified Accountability write chain:

```text
trusted Agent Memory invocation context
    + stable Memory operation identity (OperationId + OccurredAt)
    ↓
deterministic Memory operation
    ↓
known terminal result / effective visible result
    ↓
typed, bounded Memory Accountability payload
    ↓
IAgentMemoryAccountabilityProducer
    ↓
AuditEnvelope
    ↓
IAuditRecorder
    ↓
validate → sanitize → integrity → configured IAuditSink
```

The design freezes four decisions:

1. `AgentMemoryOperationIdentity` freezes `OperationId + OccurredAt` together
   for one admitted Memory operation execution and its established
   Accountability fact. Neither value is reconstructed when that fact is
   projected or republished. A fresh Capability execution is a fresh execution
   context and is not promised Duplicate semantics.
2. Recall and Source Expansion facts are produced only after the shared
   ReadCore has enforced tenant, visibility, grant, budget, and caller-visible
   result boundaries. Dedicated effective-visible content hashes describe the
   returned content without reusing provenance-aware domain hashes.
3. Curation facts are produced only for a known committed transition or a
   known typed rejection/conflict. An arbitrary provider exception is an
   unknown mutation outcome and produces no false `failed` fact.
4. Accountability is one bounded post-result projection. Its rejection,
   timeout, sink failure, Duplicate, or Conflict never changes the already
   established Memory result.

Duplicate is deliberately narrow: it means the exact same established Memory
Accountability fact, including its complete causal/runtime envelope, was
republished. Re-executing a business operation or re-entering a logical
invocation through a new Capability execution is not Duplicate semantics.

Source Expansion also closes one prerequisite safety defect: the current
ReadCore returns domain content from a property named `SanitizedContent` without
independently sanitizing it. Phase 9b+ intentionally changes that caller-visible
behavior before projecting Accountability. This is an explicit safety cutover,
not a claim that #56 is only a passive audit adapter.

The resulting ownership model is:

```text
Agent Memory Abstractions
    owns OperationId semantics, invocation context, typed payloads,
    producer interface, result summaries, and JSON roots

Agent Memory Runtime / ReadCore
    owns the operation boundary and safe fact construction

Agent Memory Accountability bridge
    owns AuditEnvelope mapping, deterministic AuditId, payload rules,
    bounded recording, diagnostics, and DI activation

Accountability Runtime
    owns validation, sanitization, integrity, recorder, and sinks

Memory Store
    owns Memory state only; it emits no Accountability fact
```

There is no Memory-specific audit store, sink, envelope, database table, or
authorization decision.

---

## 2. Repository Facts That Constrain the Design

### 2.1 Trusted invocation context already exists

`AgentMemoryInvocationContext` already carries:

```text
TenantId
ActorId / ActorKind
AgentId / SessionId
CorrelationId / CausationId
InvocationSource
DisplayName
TraceAttributes
```

`AgentMemoryOperationRequest` already embeds it for Promote, Reject,
Supersede, and Archive. Phase 9b+ reuses this contract. It does not create a
second Memory accountability context, does not add actor fields to
`AgentMemoryQuery`, and does not turn `AgentContextSourceRef` into an invocation
context carrier.

`DisplayName` and `TraceAttributes` remain non-accountability input. The v1
producer never copies either into `AuditEnvelope`, Tags, or payload data.

### 2.2 Curation has context but no semantic operation identity

`AgentMemoryOperationRequest` has a timestamp and trusted context but no
semantic operation identity. Phase 9b+ replaces the loose timestamp with the
required `AgentMemoryOperationIdentity`; `OperationId` and `OccurredAt` enter
and cross the curation boundary as one immutable pair.

The four meanings remain distinct:

```text
CorrelationId  groups related operations
CausationId    identifies the direct upstream operation
OperationId    identifies this Memory semantic operation
AuditId         identifies this versioned Accountability fact
```

### 2.3 Artifact origin identity is not Accountability operation identity

`AgentMemoryArtifactOrigin.OperationId` already identifies the logical Agent
Tool, MCP, MCP-session, or trusted-host origin used by Handle/Grant security.
Agent Tool currently uses the logical Tool InvocationId and MCP uses the MCP
InvocationId. That origin identity may span more than one Capability execution
and therefore cannot also be the Memory Accountability operation identity.

ReadCore requests carry both contracts explicitly: ArtifactOrigin continues to
bind security artifacts, while `AgentMemoryOperationIdentity` identifies one
admitted Memory execution and its fact. Phase 9b+ does not force equality
between them and never uses an origin InvocationId as an Accountability
OperationId fallback.

### 2.4 Retriever output is not the final security boundary

`DefaultAgentMemoryRetriever` already produces:

```text
ScopeFingerprint
VisibleMemorySetHash
CanonicalPackHash
WasTruncated
Diagnostics
```

However, `AgentMemoryReadCore` subsequently revalidates pack TenantId, filters
items by exact tenant and effective descriptor closure, creates opaque Handles
and Grants, and constructs `BuildAgentMemoryPackResult`. Accountability must
observe this effective result, not raw Store output or a pre-defense-in-depth
pack.

The Retriever `ScopeFingerprint` is not safe for the v1 Accountability payload:
`ComputeScopeFingerprint(AgentMemoryQuery)` includes MemoryIds, Tags,
DescriptorRefs, and VisibleDescriptorRefs, including inputs that may later be
removed by ReadCore defense-in-depth. The other existing projection-level
`AgentMemoryScopeFingerprint` is a different string contract implemented with a
different hash path. Neither is reused or renamed for Accountability.

V1 therefore records neither ScopeFingerprint nor VisibleMemorySetHash.
ReadCore computes one effective pack hash from dedicated caller-visible content
hashes, count, truncation, and the explicitly persisted safe bounded query
parameters. It uses the existing Canonical Hash Runtime and a dedicated
governed shape. Raw Retriever ScopeFingerprint, VisibleMemorySetHash, and
CanonicalPackHash never enter Accountability.

The returned domain `CanonicalContentHash` is also not an Accountability
effective-content hash. Its v2 projection includes TenantId and complete
SourceRefs; SourceRef canonicalization includes source coordinates,
DescriptorRefs, correlation/causation, and upstream hashes. Those provenance
inputs are valid for domain artifact identity but are not all caller-visible.
Phase 9b+ therefore does not feed this hash into `EffectivePackHash`.

### 2.5 Source Expansion has a false-safety naming hazard

`DefaultAgentContextSourceExpander` may place domain content directly into a
property named `SanitizedContent`. The shared `AgentMemorySourceExpandCore`
currently truncates that value, and `ExpandAgentMemorySourceResult` already has
a `CanonicalContentHash` field that the current core does not populate.

Phase 9b+ must not infer safety from the property name. After Grant validation,
ReadCore applies `IAgentMemoryContentSanitizer`, maps a rejected result to
Redacted, truncates the sanitized value to the caller budget, and computes the
canonical hash over the exact final caller-visible value. Only that hash and a
bounded redaction summary may enter Accountability. Expanded content never
does.

The current sanitizer result exposes Rejected, RedactionKinds, Diagnostics,
and a content hash, but no stable policy/rule-set identity. V1 therefore records
only bounded state and codes. It does not invent a RuleSet or RuleSetVersion.

### 2.6 Curation has typed known failures and untyped unknown failures

`AgentMemoryOperationException` already distinguishes stable domain failures,
including resource unavailable, invalid lifecycle state, state conflict,
identity conflict, and invalid trusted request fields. An arbitrary provider
exception does not prove whether a future durable write committed, failed
before commit, or committed while its response was lost.

The Accountability mapping therefore treats only explicit stable domain
outcomes as known. A general exception, provider timeout, connection loss, or
unclassified cancellation is not converted into a deterministic Memory
`failed` fact.

### 2.7 Archive is not yet on the conditional curation mainline

Promote, Reject, and Supersede use `IAgentMemoryConditionalCurationStore`.
Archive currently performs Get + `SaveMemoryAsync`, which cannot freeze the
same expectation/CAS contract for #55.

Phase 9b+ adds a conditional Archive operation using
`AgentMemoryItemExpectation`. This is a curation-mainline closure, not an
Accountability method on the Store. It preserves the existing Active or
Superseded → Archived lifecycle and lets the producer distinguish committed,
conflict, and unknown outcomes without inventing Store audit hooks.

### 2.8 #73 is a structural precedent, not an identity precedent

Issue #73 concentrates Agent Tool reconciliation into decision, settlement,
result persistence, and best-effort Accountability projection. Its ordering is
the relevant precedent:

```text
authoritative result first → Accountability projection second
```

The current reconciliation producer builds an AuditId with a timestamp. That
pattern is deliberately not reused here. Memory fact AuditId is deterministic
from the admitted Memory operation identity and payload version.

### 2.9 Accountability already freezes the unified write chain

Phase 9a requires:

```text
candidate validation
    → sanitization/minimization
    → safe-snapshot validation
    → canonicalization
    → integrity
    → sink fan-out
```

It also freezes Duplicate/Conflict by `AuditId + RecordHash`. RecordHash is the
canonical projection of the complete safe Envelope, including OccurredAt,
CorrelationId, CausationId, ParentAuditId, Actor, Action, Target, Outcome,
Runtime.ExecutionId, Runtime.References, descriptors/evidence/tags, and
Payload—not Payload alone. It also forbids reflection JSON fallback and
requires producer-owned payload DTOs to use their own source-generated
`JsonTypeInfo<T>`. Phase 9b+ changes none of those rules.

### 2.10 Capability identity is execution-scoped

When no ambient operation exists, `CapabilityPipeline` creates a new root
CorrelationId. For every invocation, `AuditMiddleware` independently allocates
a new Capability ExecutionId and Capability AuditId before entering the
handler, then exposes them through `CapabilityExecutionContext` and the ambient
`AuditOperationContext`.

Therefore a logical Agent/MCP invocation that actually re-enters Capability is
a new execution fact. Even if a Memory OperationId and OccurredAt were
incorrectly reused, the nested Memory CausationId, ParentAuditId, and usually
CorrelationId differ. Because all of those fields participate in RecordHash,
the second Memory fact is Conflict, not Duplicate. Phase 9b+ does not change or
make replayable Capability execution identity.

---

## 3. Scope

### 3.1 In scope

- Stable Memory `AgentMemoryOperationIdentity` (`OperationId + OccurredAt`) for
  Recall, Promote, Reject, Supersede, Archive, and Source Expansion.
- Existing `AgentMemoryInvocationContext` reuse and explicit stable actor/source
  mapping.
- Recall Accountability from the effective ReadCore-visible result.
- Dedicated `EffectiveVisibleContentHash` and `EffectivePackHash` canonical
  shapes that exclude domain provenance inputs.
- Curation Accountability for known committed and known rejection/conflict
  outcomes.
- Intentional Source Expansion safety closure after Grant validation: real
  sanitization, final truncation/hash, and caller-visible Redacted mapping
  before Accountability projection.
- Conditional Archive closure on the existing curation Store contract.
- Three typed, versioned producer payload families.
- Deterministic `AuditId` derivation and durable sink Duplicate/Conflict
  semantics.
- Exact protected-field and forbidden-field rules.
- Independent finite post-result recording budget.
- One real and one explicit null Memory producer.
- Agent Tool, Capability, MCP, and direct trusted-host context propagation.
- InMemory and PostgreSQL durable Audit sink composition evidence.
- Source-generated JSON and linux-x64 NativeAOT publish-link-run evidence.

### 3.2 Out of scope

- Durable Agent Memory storage, migrations, or lifecycle journal (#55).
- Atomic Memory mutation plus Accountability persistence.
- Transactional Outbox, retry worker, delivery receipt, or reliable delivery
  (#25).
- Memory-specific Audit Store, Sink, Envelope, table, or query API.
- Accountability methods on `IAgentMemoryStore` or
  `IAgentMemoryConditionalCurationStore`.
- Store-level semantic audit hooks.
- Compression or candidate-extraction Accountability.
- Recall ranking, filtering, or budget algorithm redesign.
- New Memory lifecycle states.
- Agent Tool pre-dispatch/finalization governance redesign.
- Authorization, approval, activation, retention platform, lineage UI, WORM,
  signatures, or tamper-evident chains.
- Raw prompt, Memory, expanded content, reason, explanation, exception, or
  diagnostic-message capture.
- Claiming that a Memory business operation itself is replay-safe or exactly
  once. #56 only makes replay of the same Accountability fact idempotent.
- Claiming that re-entering the same logical Agent/MCP invocation through a new
  Capability execution reproduces the prior fact or reaches Duplicate.

### 3.3 Compatibility position

Phase 9b+ makes four intentional cutovers:

1. Every formal operation carries `AgentMemoryOperationIdentity`, freezing
   OperationId and OccurredAt together for one admitted Memory execution and
   fact projection. First-party adapters allocate the pair once per Capability
   execution; direct callers must supply it. Producers never reconstruct it
   from current time, CorrelationId, Capability StartedAt, or payload data.
   `AgentMemoryArtifactOrigin.OperationId` remains a separate logical security
   origin and is no longer implicitly reused as the Accountability OperationId.
2. Every formal curation path, including standalone null-producer Memory,
   requires the selected service/store path to advertise `ConfirmedAtomic` and
   implement conditional Promote, Reject, Supersede, and Archive. Existing
   convenience overloads may remain, but they delegate to this single path.
   Providers that implement only `IAgentMemoryStore` no longer provide formal
   curation and must migrate; the legacy Get/Save and multi-write fallbacks are
   removed rather than retained as a second mainline.
3. Source Expansion now sanitizes domain content inside ReadCore before it is
   returned. Content/status may change to a sanitized value or Redacted. This is
   an intentional caller-visible safety correction required before safe
   Accountability projection.
4. Agent Tool/MCP Memory causality migrates from handler-local Agent/MCP field
   derivation to the current authoritative Capability Accountability context.
   Upstream identities validate that context; they do not form a competing
   correlation chain.

Raw `IAgentMemoryRetriever` and `IAgentContextSourceExpander` contracts remain
deterministic primitives. The breaking behavior occurs at formal ReadCore and
Curation orchestration boundaries, not through audit hooks inside those
primitives or Stores.

---

## 4. System Invariants

### INV-01 — Memory remains context, never authority

An Accountability fact describes a Memory operation. It cannot authorize a
Tool, approve a draft, activate a descriptor, grant a permission, drive a
Memory transition, or become runtime truth.

### INV-02 — One admitted Memory execution has one stable fact identity

Every formal Recall, Curation, or Expansion execution has one immutable
`AgentMemoryOperationIdentity` containing a non-empty OperationId and a
non-default OccurredAt. A trusted operation boundary allocates/freezes the pair
once before Memory domain execution, and the established fact retains it
unchanged for projection or republication. A new Capability execution does not
reuse the prior pair. CorrelationId, CausationId, artifact-origin IDs, resource
IDs, and CLR type names never substitute for it.

### INV-03 — Operation identity has no fallback

The trusted first-party operation-identity factory may allocate a new
OperationId and capture OccurredAt exactly once at Memory operation admission.
After that point, no adapter or producer may regenerate either value, copy
CorrelationId/artifact-origin ID as a fallback, use
`CapabilityExecutionContext.StartedAt`, or derive identity from payload data.
Missing identity makes the formal operation invalid before domain execution;
no best-effort producer fallback is allowed.

### INV-04 — Recall facts describe only the effective visible result

Raw Store values, raw Retriever output, hidden MemoryIds, hidden SourceRefs,
denied descriptors, unsafe query scope, and pre-filter counts never enter the
fact. A hash that includes a hidden value is also forbidden. Retriever
ScopeFingerprint, VisibleMemorySetHash, and CanonicalPackHash are specifically
excluded from v1 Accountability because they include unsafe query scope,
internal MemoryIds, or both. Domain `CanonicalContentHash` is also excluded
from effective-result hashes because its provenance-aware shape includes
SourceRefs and DescriptorRefs that are not all caller-visible.

### INV-05 — Expansion facts follow Grant and final-sanitization boundaries

No source fact is emitted before a valid Grant resolves to an authorized
`AgentContextSourceRef`. The content hash is computed over the exact sanitized,
possibly truncated value returned by ReadCore through the dedicated
Accountability visible-content shape, never raw domain content or the
sanitizer's pre-truncation/domain-provenance hash.

### INV-06 — Stores do not own Accountability semantics

Memory Store interfaces contain only Memory persistence and conditional
lifecycle transitions. Stores do not reference `IAuditRecorder`, `IAuditSink`,
`AuditEnvelope`, or `IAgentMemoryAccountabilityProducer` and do not emit facts.

### INV-07 — Retriever and Expander remain deterministic domain services

`DefaultAgentMemoryRetriever` and `DefaultAgentContextSourceExpander` do not
depend on `IAuditRecorder`, `IAuditSink`, or the Memory Accountability producer.
ReadCore owns the accountable effective-result boundary.

### INV-08 — Known curation outcome is required

A curation fact may represent only:

```text
confirmed committed
confirmed typed rejection
confirmed typed conflict
```

An unclassified provider failure is unknown and produces no deterministic
failure fact. Absence of a fact is an observable delivery gap, not evidence
that the mutation failed.

### INV-09 — Accountability never rewrites the Memory result

After a Memory result is established, recorder rejection, no sink, timeout,
sink failure, Duplicate, or Conflict cannot roll back, replace, or reclassify
it. The original result or original domain exception remains authoritative.

### INV-10 — Post-result recording ignores prior business cancellation

Once the terminal/effective result exists, an already-cancelled business token
does not suppress the recording attempt. The producer uses a new finite budget.
Cancellation before a result exists follows the underlying Memory operation and
emits no false terminal fact.

### INV-11 — Duplicate and Conflict are deterministic

For one Tenant, Memory operation kind, OperationId, and payload version:

```text
first safe fact                         → Accepted
same complete safe fact                 → Duplicate
different complete safe fact            → Conflict
```

“Complete” includes the entire sanitized `AuditEnvelope` canonical projection,
not only payload and OccurredAt. CorrelationId, CausationId, ParentAuditId,
Actor, Runtime, references, and payload must all match. Conflict never
overwrites the first fact and never rolls back Memory state.

### INV-12 — OccurredAt is identity-bound, not adapter-local

`AgentMemoryOperationIdentity.OccurredAt` becomes
`AuditEnvelope.OccurredAt`. It is frozen with OperationId at the admitted
Memory operation boundary and must be reused when the established fact is
projected or republished. Capability `StartedAt`, a producer-local current
time, sink AcceptedAt, and recorder ProcessedAt never replace it.

### INV-13 — Payload wire identity is semantic

Payload identity is the explicit stable `Kind + Version`. Action/Target/Outcome
names use explicit mappings. No persisted value depends on a CLR type name,
namespace, `enum.ToString()`, localized text, or exception message.

### INV-14 — Raw and free-form content never enters Accountability

Forbidden values include Memory content, expanded content, prompts,
`IntentText`, Tags, reason/explanation text, diagnostic messages, raw exception
text/stack, DisplayName, TraceAttributes, opaque Handles/Grants, secrets, and
arbitrary dictionaries. Exact final caller-visible content may exist only as a
transient input to the dedicated canonical hash projector inside ReadCore; the
raw value never crosses into the producer, Envelope, payload, sink, or
diagnostics.

### INV-15 — V1 payload fields are protected semantic facts

V1 contains no sanitizer-rewritable free-text summary. Identifiers, operation,
status, stable code, state, counts, budgets, canonical hashes, diagnostic codes,
redaction codes, and source coordinates are protected. The payload rule may
canonicalize ordering and remove no protected field; malformed/unknown data is
rejected.

### INV-16 — The unified write order is unchanged

Memory producers call `IAuditRecorder`, never `IAuditSink`. Accountability
retains validate → sanitize → integrity → sink ordering. No raw fallback is
allowed after sanitizer rejection.

### INV-17 — Facts remain separate and correlated

Agent Tool governance, Capability execution, and Memory domain facts remain
separate records owned by their respective runtimes. Correlation and causation
connect them; none is collapsed into another or used as the other's authority.

### INV-18 — Generated JSON is the only serialization path

Public payload roots use source-generated `JsonTypeInfo<T>`. No
`JsonSerializer.Serialize(object)`, reflection resolver, open polymorphism,
`Dictionary<string, object>`, trimming suppression, or reflection fallback is
permitted.

### INV-19 — Delivery claims remain bounded

Phase 9b+ performs one bounded best-effort attempt. A durable sink makes an
attempt durable and conflict-safe; it does not make projection delivery
reliable. Retry workers and mutation/outbox atomicity remain #25/#55 concerns.

---

## 5. Operation Contracts

### 5.1 Stable operation identity

OperationId and fact time are one Memory contract:

```csharp
public sealed record AgentMemoryOperationIdentity
{
    public required string OperationId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

public interface IAgentMemoryOperationIdentityFactory
{
    AgentMemoryOperationIdentity Create();
}
```

The first-party factory allocates the pair once at Memory operation admission;
it is not called by the producer and is not called again when the established
fact is republished. The pair is snapshot-copied into the operation request and
fact projection. Equality requires both fields. OccurredAt is the admitted
Memory execution time, not Capability StartedAt or sink/recorder time. The
identity type contains no actor, tenant, correlation, resource, origin, or
payload data and therefore does not replace `AgentMemoryInvocationContext`.

### 5.2 Curation request

The existing request becomes:

```csharp
public sealed record AgentMemoryOperationRequest
{
    public required AgentMemoryOperationIdentity Identity { get; init; }
    public required string TenantId { get; init; }
    public required AgentMemoryInvocationContext InvocationContext { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; }
        = Array.Empty<AgentContextSourceRef>();
    public string? Explanation { get; init; }
}
```

`Reason`, `Explanation`, and `SourceRefs` remain domain inputs. The producer
does not serialize them. Identity, Tenant, and InvocationContext are the
projection identity/context inputs. Promotion timestamps use
`Identity.OccurredAt`; there is no second Curation Timestamp.

Validation additionally requires:

- Identity.OperationId is non-empty and within the Accountability identifier
  limit;
- request TenantId equals InvocationContext TenantId and the trusted operation
  TenantId;
- ActorId and ActorKind are non-empty;
- CorrelationId is present on first-party accountable paths;
- Identity.OccurredAt is non-default;
- first-party ActorKind and InvocationSource use explicit stable mappings.

### 5.3 Recall ReadCore request

Replace the loose ReadCore argument list with an orchestration request or an
equivalent immutable shape:

```csharp
public sealed record AgentMemoryRecallOperationRequest
{
    public required AgentMemoryAccessPrincipal Principal { get; init; }
    public required AgentMemoryArtifactOrigin Origin { get; init; }
    public required AgentMemoryOperationIdentity Identity { get; init; }
    public required AgentMemoryInvocationContext InvocationContext { get; init; }
    public required AgentMemoryAccessScope Scope { get; init; }
    public required BuildAgentMemoryPackInput Input { get; init; }
}
```

Principal, InvocationContext, Origin, and Scope must agree on Tenant and caller
identities. Origin.OperationId remains the logical security-artifact origin;
Identity.OperationId owns only this Memory execution/fact, so equality between
them is neither required nor expected. The request is an orchestration carrier;
`AgentMemoryQuery` remains only a query/filter.

### 5.4 Source Expansion ReadCore request

```csharp
public sealed record AgentMemorySourceExpansionOperationRequest
{
    public required AgentMemoryAccessPrincipal Principal { get; init; }
    public required AgentMemoryArtifactOrigin Origin { get; init; }
    public required AgentMemoryOperationIdentity Identity { get; init; }
    public required AgentMemoryInvocationContext InvocationContext { get; init; }
    public required AgentMemoryAccessScope Scope { get; init; }
    public required ExpandAgentMemorySourceInput Input { get; init; }
}
```

No GrantId is copied into Accountability. The fact uses the authorized SourceRef
resolved inside ReadCore.

### 5.5 Conditional Archive

Add only the domain transition to the existing conditional Store contract:

```csharp
ValueTask ArchiveAsync(
    string tenantId,
    AgentMemoryItemExpectation memory,
    AgentMemoryOperationRequest operation,
    CancellationToken cancellationToken = default);
```

The canonical service resolves the current item, computes its existing state
hash through `AgentMemoryCanonicalHashProjector`, and invokes this method. The
Store atomically verifies Active/Superseded plus the expectation and writes
Archived. No Accountability type crosses the Store boundary.

Adding Archive to `IAgentMemoryConditionalCurationStore` makes complete
conditional curation a compile-time provider contract. A provider cannot claim
the formal curation mainline by implementing only the existing three
transitions.

### 5.6 First-party stable identity and causal context propagation

The first-party adapters use one explicit mapping table:

| Caller | Memory operation identity allocation | Correlation/Causation source |
|---|---|---|
| Agent Tool | call `IAgentMemoryOperationIdentityFactory` once inside the admitted Capability execution; retain the pair in the Memory request/fact snapshot | current authoritative Capability Accountability context |
| MCP | call the same factory once inside the admitted Capability execution; retain the pair in the Memory request/fact snapshot | current authoritative Capability Accountability context |
| Direct trusted host | caller-allocated stable OperationId + immutable OccurredAt | caller-supplied trusted context or a matching ambient Accountability scope |

Agent Tool InvocationId and MCP InvocationId remain upstream logical identities,
ArtifactOrigin values, and Runtime references. They do not become the Memory
Accountability OperationId. A new Capability execution calls the factory again,
even when it re-enters the same logical Agent/MCP invocation. Only republication
of an already established Memory fact reuses its snapshotted pair.

If a defective adapter reuses the same Memory OperationId under a different
Capability execution, the Memory AuditId is reused but CorrelationId,
CausationId, ParentAuditId, Capability references, or other envelope fields
change. The durable sink must return Conflict, never Duplicate. #56 does not
make Capability execution identity replayable.

MCP RequestId remains the direct cause of the Capability fact, while the
Capability execution becomes the direct cause of the nested Memory fact. Sink
acceptance and recorder processing retain their existing timestamps and are not
copied into the producer fact.

---

## 6. Project and Dependency Shape

Add one integration bridge, not a second subsystem:

```text
src/Runtime/Agent/
├── CrestCreates.Agent.Memory.Abstractions/
│   ├── operation identity, Accountability payloads, and producer interface
│   └── Json/AgentMemoryAccountabilityJsonSerializerContext.cs
├── CrestCreates.Agent.Memory/
│   ├── DefaultAgentMemoryOperationIdentityFactory
│   └── NullAgentMemoryAccountabilityProducer
├── CrestCreates.Agent.Memory.ReadCore/
│   ├── effective-visible content/pack hash projectors
│   └── effective result fact construction and producer call
└── CrestCreates.Agent.Memory.Accountability/
    ├── AgentMemoryAccountabilityProducer
    ├── deterministic AuditId projector
    ├── exact payload sanitization rules
    ├── bounded recording options/budget
    └── service registration and startup validation
```

Allowed dependencies:

```text
Agent.Memory.Abstractions
    → existing Core/Metadata/Snapshot/Prompting abstractions

Agent.Memory / Agent.Memory.ReadCore
    → Agent.Memory.Abstractions

Agent.Memory.ReadCore
    → Metadata canonical hash runtime

Agent.Memory.Accountability
    → Agent.Memory.Abstractions
    → Accountability.Abstractions
    → Metadata canonical hash runtime
```

Forbidden dependencies:

```text
Accountability(.Abstractions) → Agent Memory
Agent Memory Store            → Accountability or producer
Retriever / Expander          → Accountability or producer
Memory producer               → IAuditSink or PostgreSQL/Npgsql
Memory bridge                 → Agent Tools, MCP, ASP.NET Core, or Platform
```

`IAgentMemoryAccountabilityProducer` is Memory-owned and accepts only typed safe
Memory facts. The real implementation is the sole type that maps them into
`AuditEnvelope`. The null implementation preserves the standalone deterministic
Memory runtime; explicit bridge activation replaces it.

The canonical `DefaultAgentMemoryPromotionService` and shared ReadCore depend
on the Memory producer interface, never directly on `IAuditRecorder`. The
default service reports `ConfirmedAtomic` only when its Store implements both
`IAgentMemoryStoreCapabilities` with `ConfirmedAtomic` and the complete
`IAgentMemoryConditionalCurationStore` contract including Archive.

A custom `IAgentMemoryPromotionService` may enter formal curation only by also
implementing `IAgentMemoryCurationServiceCapabilities`, reporting
`ConfirmedAtomic`, and passing the same conditional curation shared contract
suite. Delegation through the canonical service is the shortest path; a custom
claim is an explicit provider guarantee, not an escape hatch to legacy
multi-write behavior.

---

## 7. Payload Contracts

### 7.1 Stable payload identities

```text
agent-memory.recall.result           version 1
agent-memory.curation.result         version 1
agent-memory.source-expansion.result version 1
```

The CLR record names do not define wire identity.

### 7.2 Recall payload

```csharp
public sealed record AgentMemoryRecallAccountabilityPayload
{
    public required string OperationId { get; init; }
    public required string Result { get; init; } // completed | rejected
    public string? StableFailureCode { get; init; }
    public CanonicalHash? EffectivePackHash { get; init; }
    public required int ReturnedCount { get; init; }
    public required bool WasTruncated { get; init; }
    public IReadOnlyList<string> DiagnosticCodes { get; init; } = [];
    public IReadOnlyList<string> RequestedKinds { get; init; } = [];
    public required int MaximumCount { get; init; }
    public required int CharacterBudget { get; init; }
    public required string MinimumConfidence { get; init; }
}
```

V1 deliberately excludes ScopeFingerprint, MemoryIds, Handles, SourceRefs,
Tags, IntentText, descriptor refs, eligible pre-filter counts, Memory content,
and diagnostic messages. It also excludes any set hash based on internal
MemoryIds. `EffectivePackHash` uses a dedicated Accountability shape containing
only dedicated effective-visible content hashes in final caller-visible order,
ReturnedCount, WasTruncated, RequestedKinds, MaximumCount, CharacterBudget,
and MinimumConfidence. It does not reuse any Retriever hash or any returned
domain `CanonicalContentHash`.

For a completed result EffectivePackHash is required. For a deterministic
pre-result rejection it is null, ReturnedCount is zero, WasTruncated is false,
and only a stable caller-safe failure code is allowed.

### 7.3 Curation payload

```csharp
public sealed record AgentMemoryCurationAccountabilityPayload
{
    public required string OperationId { get; init; }
    public required string Operation { get; init; }
    // promote | reject | supersede | archive

    public string? CandidateId { get; init; }
    public string? MemoryId { get; init; }
    public string? ReplacementCandidateId { get; init; }
    public string? NewMemoryId { get; init; }

    public CanonicalHash? ExpectedCandidateStateHash { get; init; }
    public CanonicalHash? ExpectedMemoryStateHash { get; init; }
    public CanonicalHash? ExpectedReplacementStateHash { get; init; }
    public CanonicalHash? ExpectedContentHash { get; init; }

    public string? PreviousState { get; init; }
    public string? ResultingState { get; init; }
    public required string Result { get; init; }
    // committed | rejected | conflict
    public string? StableFailureCode { get; init; }
    public AgentMemoryAccountabilitySanitizationSummary? Sanitization { get; init; }
}
```

Operation-specific field rules are exact:

| Operation | Required targets | Committed transition |
|---|---|---|
| Promote | CandidateId, NewMemoryId | Candidate → Active Memory |
| Reject | CandidateId | Candidate → Rejected |
| Supersede | MemoryId, ReplacementCandidateId, NewMemoryId | Active old → Superseded; Candidate → Active new |
| Archive | MemoryId | Active/Superseded → Archived |

`Result = failed` is not a v1 curation value. Unknown provider outcomes emit no
curation payload. Expected hashes are existing canonical Memory CAS facts; they
are not described as redaction, truth, or proof that source content was safe.

Raw Reason and Explanation never enter the payload. If a future compliance
requirement needs reason semantics, it must introduce a closed stable reason
code contract, not a truncated free-text field.

### 7.4 Source Expansion payload

```csharp
public sealed record AgentMemorySourceExpansionAccountabilityPayload
{
    public required string OperationId { get; init; }
    public required string SourceKind { get; init; }
    public required string SourceId { get; init; }
    public int? RangeStart { get; init; }
    public int? RangeEnd { get; init; }
    public required string Status { get; init; }
    // expanded | redacted | not-found | not-expandable |
    // external-source-not-supported
    public CanonicalHash? EffectiveVisibleContentHash { get; init; }
    public required int MaximumCharacters { get; init; }
    public required bool WasTruncated { get; init; }
    public required AgentMemoryAccountabilitySanitizationSummary Sanitization { get; init; }
    public IReadOnlyList<string> DiagnosticCodes { get; init; } = [];
}
```

SourceId is permitted only after a valid Grant resolves the exact authorized
SourceRef. An unresolved, expired, mismatched, or cross-tenant Grant produces no
source payload and the outer Capability/Tool fact remains the only failure
fact. GrantId is never logged.

`EffectiveVisibleContentHash` is required only for Expanded. It is computed by
the dedicated Accountability visible-content projector over the exact final
value after Memory sanitization and character-budget truncation. It does not
reuse `SanitizedAgentContent.CanonicalContentHash`, which describes the
pre-truncation domain artifact. Redacted and non-expanded statuses carry no
content hash. The required Sanitization summary preserves safe state/codes
without retaining match text or original content.

### 7.5 Sanitization summary

```csharp
public sealed record AgentMemoryAccountabilitySanitizationSummary
{
    public required string State { get; init; } // none | redacted | rejected
    public IReadOnlyList<string> RedactionCodes { get; init; } = [];
    public IReadOnlyList<string> DiagnosticCodes { get; init; } = [];
}
```

Arrays are ordinal-deduplicated, ordinal-sorted, and bounded before
serialization. Messages, match counts, original values, and replacement text
are forbidden. V1 deliberately records no sanitizer RuleSet/RuleSetVersion:
the current `SanitizedAgentContent` contract exposes no trusted policy identity,
and the adapter must not hard-code invented provenance. Adding policy identity
requires a future explicit sanitizer contract version.

### 7.6 Effective caller-visible hash shapes

Domain artifact identity and Accountability result evidence answer different
questions:

```text
Memory CanonicalContentHash
    = content + trusted domain provenance identity

Accountability EffectiveVisibleContentHash
    = exact final caller-visible content in one Tenant
```

ReadCore adds one governed effective-content projection:

```text
ArtifactKind          = AgentMemoryAccountabilityEffectiveVisibleContent
Purpose               = AuditEvidence
Scope                 = TenantVisible
ContractVersion       = agent-memory-accountability-effective-content-v1
CanonicalShapeVersion = agent-memory-accountability-effective-content-v1

Canonical ordered fields:
TenantId
Content
```

`Content` is the exact sanitized string returned to the caller, after any
per-operation truncation. The shape contains no MemoryId, SourceRef,
DescriptorRef, source coordinate, correlation/causation, upstream hash, domain
`CanonicalContentHash`, or sanitizer pre-truncation hash.

The existing domain CanonicalContentHash may remain in the caller DTO for
artifact/provenance verification. “Effective-visible” here means the minimized
caller-visible Content semantics projected by Accountability, not a byte hash
of every DTO property. A provenance-only domain-hash change therefore does not
change EffectiveVisibleContentHash when final Content is identical.

Recall computes one such hash for each final returned item's caller-visible
Content, preserving final result order only as an input to the pack projector.
Source Expansion uses the same shape for its exact final expanded Content.
These per-item Recall hashes are projection inputs and do not become separate
payload fields.

The governed effective-pack projection is:

```text
ArtifactKind          = AgentMemoryAccountabilityEffectivePack
Purpose               = AuditEvidence
Scope                 = TenantVisible
ContractVersion       = agent-memory-accountability-effective-pack-v1
CanonicalShapeVersion = agent-memory-accountability-effective-pack-v1

Canonical ordered fields:
TenantId
EffectiveVisibleContentHashes (final returned order)
ReturnedCount
WasTruncated
RequestedKinds (ordinal canonical order)
MaximumCount
CharacterBudget
MinimumConfidence
```

CanonicalHash inputs include their governed metadata plus value. Neither shape
wraps or aliases the existing Memory domain content/pack hashes.

---

## 8. Producer Contract and Envelope Mapping

### 8.1 One producer

```csharp
public interface IAgentMemoryAccountabilityProducer
{
    ValueTask PublishRecallAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemoryRecallAccountabilityPayload payload);

    ValueTask PublishCurationAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemoryCurationAccountabilityPayload payload);

    ValueTask PublishSourceExpansionAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemorySourceExpansionAccountabilityPayload payload);
}
```

The interface intentionally has no business CancellationToken. Each call owns
one independent bounded attempt. Both the real producer and each orchestration
caller fence exceptions so a faulty projection cannot change the Memory result.
The producer requires `identity.OperationId == payload.OperationId` and maps
`identity.OccurredAt` directly to the Envelope; it never samples a clock.

Concrete types:

```text
NullAgentMemoryAccountabilityProducer
AgentMemoryAccountabilityProducer
```

There is no separate producer per operation family.

### 8.2 Stable Action and Target mappings

| Memory operation | Action.Kind | Action.Name | Target.Kind |
|---|---|---|---|
| Recall | `agent-memory.recall` | `recall` | `agent-memory-pack` |
| Promote | `agent-memory.promote` | `promote` | `agent-memory-candidate` |
| Reject | `agent-memory.reject` | `reject` | `agent-memory-candidate` |
| Supersede | `agent-memory.supersede` | `supersede` | `agent-memory` |
| Archive | `agent-memory.archive` | `archive` | `agent-memory` |
| Source Expansion | `agent-memory.source-expand` | `source-expand` | `agent-memory-source` |

Recall Target.Id is OperationId because the pack has no durable identity.
Curation Target.Id is the primary CandidateId or MemoryId. Expansion Target.Id
is SourceId after Grant validation. Secondary targets remain in the typed
payload.

### 8.3 Outcome mapping

| Memory semantic result | AuditOutcome.Status | Code |
|---|---|---|
| Recall completed | `succeeded` | `completed` or `empty` |
| Recall deterministic rejection | `rejected` | stable safe code |
| Curation committed | `succeeded` | `committed` |
| Curation typed rejection | `rejected` | stable safe code |
| Curation state/identity conflict | `rejected` | stable conflict code |
| Expansion expanded | `succeeded` | `expanded` |
| Expansion redacted | `rejected` | `redacted` |
| Expansion not found/not expandable/external | `rejected` | exact stable status |

`AuditOutcome.SafeSummary` is null. Payload Result/Status and Outcome mapping
must agree. The producer validates the cross-object agreement before calling
`IAuditRecorder`; each payload rule separately validates the payload's internal
field matrix because the rule does not receive the surrounding Envelope.

### 8.4 Actor and runtime mapping

`AgentMemoryInvocationContext.ActorKind` uses the Accountability stable actor
grammar. First-party mappings are explicit:

```text
Agent Tool       → actor from Capability AccountabilityActor (normally agent)
MCP user scope   → user
Trusted host     → explicit user / agent / system supplied by the host
```

The producer never uses `enum.ToString()` and never promotes DisplayName to
identity. Unknown actor data maps only to explicit `unknown` when the trusted
operation contract permits it; it is not invented from UserName or HostName.

Runtime mapping is bounded:

```text
Runtime.InvocationSource = explicit stable mapping
Runtime.ExecutionId      = Memory OperationId
Runtime.References       = agent-session / agent-invocation when known
```

No TraceAttributes are copied. TraceId/SpanId are omitted in v1 unless a later
contract explicitly supplies trusted observation fields.

### 8.5 Correlation and causation composition

For every Agent Tool or MCP Memory operation, the current Capability execution
context and its matching ambient Accountability scope are authoritative after
Capability dispatch begins:

```text
Memory CorrelationId
    = CapabilityExecutionContext.CorrelationId

Memory CausationId
    = current Capability Accountability operation/execution ID
    = CapabilityExecutionContext.ExecutionId
    = matching ambient AuditOperationContext.OperationId

Memory ParentAuditId
    = matching ambient AuditOperationContext.EnclosingAuditId
    = Capability AuditId
```

The Capability middleware creates the execution ID and pushes the ambient scope
before invoking the handler. The Memory adapter consumes that established
context; it does not independently derive a second causal chain from Agent or
MCP fields. A first-party Agent Tool/MCP operation is invalid before Memory
domain execution when the Capability context or matching ambient scope is
missing, or when TenantId, CorrelationId, Actor, or Capability execution ID do
not agree.

Upstream identities remain necessary as consistency checks, security-artifact
origins, and bounded Runtime references. They do not supply the Memory
Accountability operation identity:

```text
Agent Tool InvocationId / ExecutionId
    → validate Agent binding and Capability invocation metadata
    → ArtifactOrigin / Runtime reference only

MCP InvocationId / RequestId
    → validate MCP binding and Capability invocation metadata
    → ArtifactOrigin / Runtime reference only

IAgentMemoryOperationIdentityFactory
    → called once for this admitted Memory execution
    → never called during fact projection/republication
```

This is an intentional semantic migration from the current curation helper,
which derives Memory correlation/causation directly from
`AgentExecutionContext`. The migrated rule makes the active Capability fact
the one direct parent of the nested Memory fact.

Every new Capability execution receives a new Capability ExecutionId and
preallocated Capability AuditId. A Memory fact created under it is therefore a
different complete fact even if a caller incorrectly reuses a prior Memory
OperationId and OccurredAt. Accountability must report Conflict because the
causal envelope changed; it must not flatten both executions into Duplicate.

For a direct trusted-host call without Capability dispatch, the caller supplies
CorrelationId and optional upstream CausationId. The producer may use an
ambient `ParentAuditId` only when ambient TenantId and CorrelationId match the
supplied Memory context and ambient OperationId equals the supplied
CausationId. Otherwise ParentAuditId is null; no unrelated ambient relation is
invented.

`PreviousAuditId` is null for v1 Memory facts. Memory lifecycle sequencing is
not inferred from prior sink records.

---

## 9. Deterministic Audit Identity

### 9.1 Canonical identity projection

The real producer uses the existing Canonical Hash Runtime. Add one governed
artifact/shape identity:

```text
ArtifactKind          = AgentMemoryAccountabilityIdentity
Purpose               = SourceIdentity
Scope                 = InternalFull
ContractVersion       = agent-memory-accountability-identity-v1
CanonicalShapeVersion = agent-memory-accountability-audit-id-v1
```

Canonical ordered fields:

```text
TenantId
ActionKind
OperationId
PayloadKind
PayloadVersion
```

The AuditId is:

```text
amem-v1-{full lowercase canonical hash value}
```

TenantId prevents cross-tenant collision in sinks whose AuditId key is global.
ActionKind and payload version make semantic/version namespaces explicit.
Outcome, payload data, timestamp, and RecordHash are deliberately excluded so
changed facts reuse the same AuditId and reach sink Conflict.

No SHA implementation, string-concatenation protocol, random ID, timestamp,
or CLR type name is introduced for this identity.

### 9.2 What Duplicate means

Duplicate means the exact sanitized Accountability fact was presented again.
The republished fact must reuse OperationId, OccurredAt,
CorrelationId/CausationId/ParentAuditId, Actor, Runtime execution/references,
Action, Target, Outcome, descriptors, evidence, tags, and the sanitized typed
payload exactly, so the complete RecordHash is unchanged.

Duplicate does not mean the Memory mutation was replayed, the Recall query was
not executed, the same logical Agent/MCP invocation re-entered Capability, or
the caller must receive a previous business result. Making a Memory business
operation replayable or durably reconstructing its prior result remains a #55
concern. #56 only supports republication of the already established fact.

Phase 9b+ performs no delayed retry itself. “Republication” means the same
already-constructed safe Envelope/fact snapshot is presented to Recorder/Sink
again, or the producer is invoked again with all inputs unchanged while the
same Capability ambient scope remains active. A future outbox/retry worker must
persist and replay the complete safe fact, not reconstruct causal fields from a
new ambient scope.

### 9.3 What Conflict means

Conflict means the same versioned semantic operation identity was reused for a
different complete safe fact. Examples include a changed Recall pack, changed
budget, changed Curation target/result, changed source status/hash, changed
actor/context, changed timestamp, or the same OperationId appearing beneath a
different Capability ExecutionId/AuditId. The latter is the expected guardrail
when an adapter incorrectly carries a Memory OperationId across Capability
executions.

The producer emits one structured warning/metric containing only AuditId,
OperationId, ActionKind, payload Kind/Version, and stable recorder/sink status
codes. It emits no payload data or raw identifiers beyond these already-safe
operation identifiers.

---

## 10. Operation Boundaries

### 10.1 Recall

Normative order:

```text
validate operation identity/context/budgets
    → resolve input Handles
    → build closed visibility query
    → Retriever Recall
    → validate pack TenantId
    → defense-in-depth tenant/closure filtering
    → prepare and verify Handles/Grants
    → construct effective BuildAgentMemoryPackResult
    → project effective-visible content hashes from exact returned Content
    → project EffectivePackHash from final safe caller-visible content result
    → construct safe Recall payload
    → bounded Accountability attempt
    → return the original result
```

ReadCore does not serialize returned items into the Accountability payload.
Returned MemoryIds and opaque Handles are absent from payload data and from all
Accountability hash inputs. The effective pack projector receives dedicated
effective-visible hashes computed from each exact returned Content value,
returned count, truncation, and the safe bounded query fields listed in
sections 7.2 and 7.6. It never receives the returned domain
CanonicalContentHash, original query, any raw Retriever hash, hidden identity
or provenance, or pre-filter counts.

Known ReadCore validation failures may produce a rejected Recall fact only when
the complete operation identity, actor, Tenant, Correlation, and minimized query
facts are already trusted. Handle resolution failures use only the generic stable
`resource-unavailable` code and never include the Handle or hidden target ID.
Provider exceptions and raw Retriever exceptions produce no deterministic
Recall result fact.

### 10.2 Curation

Normative order:

```text
validate AgentMemoryOperationRequest
    → load/prepare expected state
    → execute conditional Store transition
    → classify known committed or typed rejection/conflict
    → construct safe Curation payload
    → bounded Accountability attempt
    → return/rethrow the original Memory result
```

Typed request validation that lacks a valid Actor/OperationId/Correlation
cannot form a valid Accountability envelope and is not recorded. A stable
state conflict with complete trusted context is recorded.

For a general exception:

```text
catch AgentMemoryOperationException with known stable code
    → known rejection/conflict fact
    → rethrow the same exception

catch any other exception
    → no deterministic curation fact
    → rethrow the same exception
```

The producer never catches an unknown exception and labels it `failed`.

### 10.3 Source Expansion

Normative order:

```text
validate operation identity/context/budget
    → resolve and validate Grant
    → obtain authorized SourceRef
    → Expander domain result
    → sanitize produced content independently
    → map rejection/redaction
    → truncate sanitized content to caller budget
    → project EffectiveVisibleContentHash from exact final visible content
    → construct effective ExpandAgentMemorySourceResult
    → construct safe Expansion payload from authorized SourceRef + result
    → bounded Accountability attempt
    → return the original result
```

The payload preserves exact domain terminal status after authorization, while
the protocol result may continue to collapse statuses according to the existing
Tool contract. This extra precision is allowed only because the valid Grant
already proves the caller was authorized for that exact SourceRef. Before Grant
validation, no SourceId or source-status fact is emitted.

For Expanded, the existing result `CanonicalContentHash` field receives the
dedicated effective-visible hash despite its legacy generic property name. The
projector does not copy or wrap the sanitizer's pre-truncation hash. The typed
Accountability payload exposes the less ambiguous
`EffectiveVisibleContentHash` name.

---

## 11. Payload Sanitization and Validation

Register exactly three `IAuditPayloadSanitizationRule` implementations, one per
Kind. Each rule:

- requires payload Version 1;
- parses with the exact source-generated `JsonTypeInfo<T>`;
- rejects missing, duplicate, unknown, or invalid fields;
- validates operation-specific target combinations;
- validates stable code/state/value allowlists;
- validates canonical hash metadata;
- validates all identifiers and hard bounds;
- requires code arrays to be ordinal-distinct and already ordinal-sorted;
- reserializes with the same generated type info;
- preserves every protected semantic field exactly.

The rules are not pass-through wildcard rules. Unknown payload Kind or Version
is rejected by the existing Accountability sanitizer. The real producer never
falls back to the candidate payload.

Candidate and safe payloads must remain below the existing Accountability v1
hard limits. Memory-specific lower limits are:

```text
Maximum diagnostic codes       32
Maximum redaction codes        16
Maximum requested kinds         6
Maximum identifier length     256
Maximum code length           128
Maximum payload version         1
```

The v1 source-generated context uses camelCase, ignores nulls, disallows
unmapped members on deserialization, and contains exactly the three public
payload roots plus their transitive typed dependencies.

---

## 12. Post-result Recording Budget and Diagnostics

`AgentMemoryAccountabilityOptions` contains:

```csharp
public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds(5);
```

The value must be finite and positive. It is the producer's outer cap and does
not assume the concrete Recorder type or inspect Recorder-specific options.
Startup validation rejects an invalid Memory budget; the Recorder continues to
enforce its own independently configured sink budget.

For each terminal result:

```text
create independent CancellationTokenSource
    → CancelAfter(WriteTimeout)
    → call IAuditRecorder once
    → observe result/exception within the same budget
    → log/measure only safe status metadata
    → return original Memory result
```

No retry loop occurs inside the business call. A Duplicate is successful
idempotent observability. A Conflict, Rejected, Failed, NoSinkConfigured,
timeout, or exception is observable projection failure but not a Memory
failure.

Required safe diagnostics distinguish at least:

```text
recorded
duplicate
conflict
recorder-rejected
no-sink
sink-failed
timeout
producer-contract-invalid
```

No diagnostic logs payload JSON, Memory/source/candidate content, exception
message, Reason, Explanation, TraceAttributes, or diagnostic messages.

---

## 13. Registration and Startup Composition

`AddAgentMemoryRuntime()` registers the null producer and
`DefaultAgentMemoryOperationIdentityFactory` with `TryAddSingleton`. The
factory uses the Host's `TimeProvider` only at the one admitted Memory operation
allocation point. This keeps deterministic Memory usable without pulling in
Accountability.

`AddAgentMemoryReadCore()` registers the dedicated effective-visible content
and effective-pack canonical projectors. They depend on the existing Canonical
Hash Runtime, not on `IAuditRecorder` or the bridge.

`AddAgentMemoryAccountability()`:

- requires the unified Accountability runtime marker and `IAuditRecorder`;
- replaces the null producer with the one real producer;
- registers the deterministic Memory AuditId projector;
- registers the three exact payload sanitizer rules;
- registers and validates the bounded write options;
- adds a startup validator that rejects a remaining null producer;
- does not register an `IAuditSink`;
- does not silently call `AddAccountability()` for the Host.

First-party Hosts and fixtures that claim Memory Accountability explicitly call:

```text
AddAccountability()
Add intended IAuditSink provider
AddAgentMemoryRuntime()
AddAgentMemoryAccountability()
Add Agent Memory ReadCore / Tools / MCP projection
```

Registration order is validated by final service resolution rather than a
first-registration-wins assumption. The Agent Memory runtime, independent of
whether the producer is real or null, validates the selected formal
`IAgentMemoryPromotionService` through
`IAgentMemoryCurationServiceCapabilities.OutcomeGuarantee == ConfirmedAtomic`.
For the canonical service, that guarantee is available only when the selected
Store advertises `ConfirmedAtomic` and implements the complete conditional
interface containing Promote, Reject, Supersede, and Archive. Agent Tool may
repeat this as a composition assertion, but it is not the owner of the
provider contract.

A Host may compose read-only Memory primitives without enabling formal
curation. Once it registers formal curation, an `IAgentMemoryStore`-only or
partially conditional provider is a startup error even when the null producer
is selected. There is no standalone legacy curation exemption.

The Memory bridge is not activated by merely referencing the package. A Host
that enables it but has no recorder/sanitizer composition fails startup rather
than silently downgrading to the null producer.

---

## 14. Case Matrix

### 14.1 Happy cases

| ID | Case | Expected contract |
|---|---|---|
| H01 | Recall returns visible bounded pack | Dedicated effective-visible content/pack projection, count, budgets, and safe codes recorded |
| H02 | Recall returns empty pack | Succeeded empty fact; no hidden ID/count/source leakage |
| H03 | Promote commits | Candidate/new Memory IDs and committed transition recorded after commit |
| H04 | Reject commits | Candidate → Rejected fact recorded |
| H05 | Supersede commits | Old Memory, replacement Candidate, and new Memory identities preserved |
| H06 | Archive commits | Conditional Active/Superseded → Archived fact recorded |
| H07 | Source expands | Exact final sanitized/truncated content hash recorded; content absent |

### 14.2 Boundary cases

| ID | Case | Expected contract |
|---|---|---|
| B01 | Recall character budget truncates | WasTruncated and final pack hash describe returned set |
| B02 | Retriever supplies hidden item | Item absent from result and all Accountability hashes/facts |
| B03 | Same complete fact republished | Durable sink returns Duplicate |
| B04 | Existing accepted fact republished | Original sink snapshot retained; Memory result unchanged |
| B05 | Expansion Redacted | No content/hash; stable redaction state/codes only |
| B06 | Expansion NotFound/NotExpandable/external after valid Grant | Exact safe terminal status; no content |
| B07 | Business token cancels after result | Independent bounded write still attempted |
| B08 | Direct trusted-host operation | Caller-provided identity/context preserved; no invented parent |
| B09 | Archive from Superseded | Conditional commit records PreviousState Superseded |
| B10 | Domain SourceRef/DescriptorRef provenance changes while final visible content is identical | EffectiveVisibleContentHash is unchanged; provenance-aware domain hash is not reused |

### 14.3 Failure cases

| ID | Case | Expected contract |
|---|---|---|
| F01 | Recall rejected before visible result | Stable safe rejected fact only when trusted context is complete |
| F02 | Recall provider throws | Original exception; no fabricated failed Recall fact |
| F03 | Curation CAS/state conflict | Stable Conflict payload; original domain exception/result unchanged |
| F04 | Curation provider outcome unknown | No deterministic failed/committed payload |
| F05 | Recorder rejects payload | Original Memory result unchanged; safe diagnostic emitted |
| F06 | Sink unavailable/times out | Original result unchanged after finite budget |
| F07 | Same OperationId, changed semantic fact | Same AuditId reaches durable sink Conflict |
| F08 | Missing/default operation identity field | No fallback identity or time; validation fails before domain execution |
| F09 | Unresolved/expired Source Grant | No SourceId/GrantId Accountability payload |
| F10 | Conditional Archive conflict | No overwrite; stable conflict fact when context is complete |
| F11 | A complete fact is republished with same OperationId but changed OccurredAt | Same AuditId reaches durable sink Conflict; this is not Duplicate |
| F12 | Agent/MCP logical origin disagrees with Capability binding metadata | Validation fails before Memory domain execution; no identity fallback |
| F13 | Same Memory OperationId is reused under a different Capability execution | Same Memory AuditId, changed complete RecordHash, durable sink Conflict |

### 14.4 Safety cases

| ID | Case | Expected contract |
|---|---|---|
| S01 | Memory contains credentials | No raw value in payload, Tags, logs, or sink snapshot |
| S02 | Expanded source contains credentials | Sanitized caller result; only exact safe hash/redaction codes recorded |
| S03 | Hidden MemoryId/SourceRef/DescriptorRef is outside the authorized effective Recall/Expansion result | No value or hidden-derived selection/content hash reaches the effective-result fact |
| S04 | Diagnostic Message contains user content | Only allowlisted DiagnosticCode is recorded |
| S05 | Reason/Explanation contains secret | Neither field reaches producer JSON or logs |
| S06 | TraceAttributes contain secret | Entire dictionary ignored |
| S07 | Malicious unknown payload property | Exact rule rejects; no sink called |

### 14.5 Composition cases

| ID | Case | Expected contract |
|---|---|---|
| C01 | Agent Tool → Capability → Recall | Memory correlation/cause/parent come from the matching Capability context; upstream Agent identities validate it |
| C02 | MCP → Capability → Expansion | Memory correlation/cause/parent come from the matching Capability context; upstream MCP identities validate it |
| C03 | Curation + PostgreSQL Audit sink | Unified `AuditEnvelope` persists without Memory schema/table |
| C04 | Identical established fact republished to PostgreSQL sink | Duplicate with original accepted snapshot |
| C05 | Established fact republished with changed complete envelope | Conflict; first snapshot retained |
| C06 | Memory fact near activation | Fact supplies no approval/activation authority |
| C07 | Standalone Memory runtime | Null producer needs no Accountability dependency, but formal curation still requires complete ConfirmedAtomic conditional support |
| C08 | Accountable Host misconfigured | Startup failure, not silent null downgrade |
| C09 | NativeAOT mainline | Generated payload JSON and unified write chain publish/link/run |
| C10 | Same logical invocation enters a fresh Capability execution | Fresh Capability and Memory operation identities; the new fact is independently Accepted if completed, never treated as prior-fact Duplicate |

---

## 15. Acceptance Test Skeleton

Establish the named tests before implementation. Names are normative for the
Issue-local evidence ledger; minor fixture grouping may change without changing
case meaning.

### 15.1 Identity and contract

```text
AgentMemoryAccountabilityIdentityTests
├─ SameOperationSameFact_Should_BeStable
├─ SameOperationDifferentFact_Should_UseSameAuditId
├─ EstablishedFactRepublish_Should_ReuseOperationIdAndOccurredAt
├─ ChangedOccurredAtForSameOperationId_Should_Conflict
├─ CorrelationId_Should_Not_Be_OperationIdentity
├─ OccurredAt_Should_BeExcludedFromAuditIdAndIncludedInRecordHash
├─ ProducerClock_Should_Not_ReplaceOccurredAt
├─ OperationIdentityFactory_Should_AllocateExactlyOncePerMemoryExecution
├─ Tenant_Should_Isolate_AuditIdentity
├─ PayloadVersion_Should_Version_AuditIdentity
└─ AuditIdentity_Should_UseCanonicalHashRuntime

AgentMemoryAccountabilityPayloadContractTests
├─ PayloadKindsAndVersions_Should_BeFrozen
├─ Payloads_Should_RoundTrip_WithGeneratedJsonTypes
├─ PayloadContext_Should_ContainExactPublicRoots
├─ CurationOperationFieldMatrix_Should_BeExact
├─ SanitizationSummary_Should_NotInventPolicyIdentity
├─ Payloads_Should_RejectUnknownFieldsAndVersions
└─ Payloads_Should_NotExposeForbiddenRawProperties
```

### 15.2 Recall

```text
AgentMemoryRecallAccountabilityTests
├─ Recall_Should_Record_EffectiveVisibleResult
├─ EmptyRecall_Should_NotLeakHiddenResources
├─ TruncatedRecall_Should_Record_BudgetState
├─ HiddenRetrieverResult_Should_NotEnterAccountability
├─ HiddenQueryMemoryId_Should_NotEnterAnyAccountabilityHash
├─ RetrieverHashes_Should_NotBeReused
├─ InternalMemoryId_Should_NotEnterEffectivePackProjection
├─ DomainCanonicalContentHash_Should_NotEnterEffectivePackHash
├─ HiddenProvenanceChange_Should_NotChangeEffectiveVisibleContentHash
├─ VisibleContentChange_Should_ChangeEffectiveVisibleContentHash
├─ DefenseFiltering_Should_ReprojectHashesWithCanonicalRuntime
├─ Recall_Should_NotRecordRawContentIdsTagsOrIntent
├─ StableRejectedRecall_Should_RecordSafeCode
├─ ProviderFailure_Should_NotClaimDeterministicFailure
└─ RecorderFailure_Should_NotChangeRecallResult
```

### 15.3 Curation

```text
AgentMemoryCurationAccountabilityTests
├─ PromoteCommitted_Should_Record
├─ RejectCommitted_Should_Record
├─ Supersede_Should_Record_OldAndNewIdentity
├─ ArchiveCommitted_Should_Record
├─ StateConflict_Should_RecordStableConflict
├─ IdentityConflict_Should_RecordStableConflict
├─ UnknownStoreFailure_Should_NotClaimDeterministicFailure
├─ ReasonAndExplanation_Should_NotReachAuditSink
├─ RecorderFailure_Should_NotChangeCommittedResult
└─ CancelledBusinessTokenAfterCommit_Should_NotSuppressAttempt

AgentMemoryConditionalArchiveContractTests
├─ Active_Should_ArchiveAtomically
├─ Superseded_Should_ArchiveAtomically
├─ StateHashMismatch_Should_Conflict
├─ ConcurrentArchive_Should_HaveOneCommittedTransition
├─ StandaloneNullProducer_Should_RejectNonConditionalCurationProvider
├─ PartialConditionalProvider_Should_FailStartup
└─ Store_Should_NotReceiveAccountabilityTypes
```

### 15.4 Source Expansion

```text
AgentMemorySourceExpansionAccountabilityTests
├─ Expanded_Should_RecordExactVisibleContentHash
├─ Truncated_Should_HashTruncatedSanitizedContent
├─ SanitizerPreTruncationHash_Should_NotBeReused
├─ HiddenProvenanceChange_Should_NotChangeExpansionVisibleContentHash
├─ NotFound_Should_RecordTerminalStatusAfterValidGrant
├─ NotExpandable_Should_RecordTerminalStatus
├─ Redacted_Should_RecordRedactionState
├─ SanitizerRejection_Should_ChangeCallerResultToRedacted
├─ SecretContent_Should_NotReachAuditSink
├─ UnresolvedGrant_Should_NotRecordSourceIdentity
└─ RecorderFailure_Should_NotChangeExpansionResult
```

### 15.5 Architecture, composition, and NativeAOT

```text
AgentMemoryAccountabilityArchitectureTests
├─ Store_Should_NotReferenceAccountability
├─ Retriever_Should_NotReferenceAccountabilityProducerOrRecorder
├─ Expander_Should_NotReferenceAccountabilityProducerOrRecorder
├─ Producer_Should_NotReferenceIAuditSink
├─ Accountability_Should_NotReferenceAgentMemory
├─ Bridge_Should_NotReferenceAgentToolsMcpOrPostgreSql
├─ Payloads_Should_HaveGeneratedJsonContracts
└─ NoReflectionSerializationFallback_Should_Exist

AgentMemoryAccountabilityCompositionTests
├─ AgentToolCapabilityMemoryFacts_ShouldRemainDistinct
├─ AgentToolCapabilityMemoryFacts_ShouldShareExactCausality
├─ AgentToolCapabilityExecution_ShouldAllocateFreshMemoryOperationIdentity
├─ McpCapabilityMemoryFacts_ShouldShareExactCausality
├─ McpCapabilityExecution_ShouldAllocateFreshMemoryOperationIdentity
├─ UpstreamOriginMismatch_ShouldFailBeforeMemoryExecution
├─ SameOperationIdUnderDifferentCapabilityExecution_ShouldConflict
├─ DurableAuditSink_ShouldAcceptMemoryFact
├─ EstablishedFactRepublish_ShouldProduceDuplicate
├─ ChangedEstablishedFactRepublish_ShouldProduceConflict
├─ MemoryFact_ShouldNotBecomeActivationEvidence
├─ StandaloneRuntime_ShouldUseExplicitNullProducer
├─ StandaloneFormalCuration_ShouldRequireConfirmedAtomicProvider
└─ EnabledBridge_ShouldRejectNullProducerAtStartup

AgentMemoryAccountabilityAotFixtureTests
└─ NativeAot_Mainline_ShouldPublishLinkAndRunOriginalBinary
```

---

## 16. Implementation Slices

### Slice 1 — Freeze identity and payload contracts

Red:

- identity tests;
- exact Kind/Version tests;
- generated JSON root tests;
- forbidden-field tests.

Green:

- add `AgentMemoryOperationIdentity` and replace the loose curation timestamp;
- add ReadCore operation requests with the existing invocation context and
  stable identity pair;
- add three payloads, sanitization summary, producer interface, null producer,
  and source-generated context.

Review:

- confirm no query/source-ref actor expansion;
- confirm one identity-factory allocation per admitted Memory execution and no
  producer/current-time/Capability-StartedAt reconstruction;
- confirm no public open payload/dictionary.

### Slice 2 — Real producer and unified write bridge

Red:

- deterministic AuditId Accepted/Duplicate/Conflict;
- strict sanitizer rule cases;
- independent timeout/result-isolation cases;
- startup composition cases.

Green:

- add bridge project;
- canonical AuditId projector;
- one real producer;
- three exact rules;
- options/budget/diagnostics/registration.

Review:

- inspect every Envelope field and protected payload field;
- prove producer references Recorder only;
- prove no content appears in failure diagnostics.

### Slice 3 — Recall effective-result integration

Red:

- H01/H02, B01/B02, F01/F02, S01/S03/S04.

Green:

- project dedicated effective-visible content and effective-pack hashes through
  the existing canonical runtime after defense filtering;
- exclude Retriever ScopeFingerprint/VisibleMemorySetHash/CanonicalPackHash
  from all fact inputs;
- exclude provenance-aware domain CanonicalContentHash from effective-result
  hash inputs;
- publish after final ReadCore result construction;
- preserve original result on recording failure.

Review:

- inspect hidden-resource cases and all hashes;
- confirm no MemoryIds/Tags/IntentText enter payload;
- confirm raw Retriever remains producer-free.

### Slice 4 — Curation and conditional Archive

Red:

- H03–H06, F03/F04/F10, S05/S06;
- conditional Archive concurrency contract.

Green:

- move Archive to conditional Store mainline;
- remove legacy non-conditional formal curation fallbacks and validate complete
  `ConfirmedAtomic` support even with the null producer;
- publish committed and typed rejection/conflict outcomes from the canonical
  curation orchestration;
- leave unknown outcomes unclassified/unrecorded.

Review:

- prove no Store Accountability hook;
- prove no legacy non-conditional formal curation mainline;
- inspect exception filters for false failure claims.

### Slice 5 — Source Expansion safe boundary

Red:

- H07, B05/B06, F09, S02;
- exact sanitized/truncated hash cases.

Green:

- independently sanitize expansion output in ReadCore;
- populate the existing result hash with the dedicated post-truncation
  effective-visible hash, never the sanitizer/domain hash;
- map sanitizer rejection to the caller-visible Redacted result without
  inventing RuleSet/version provenance;
- publish only after Grant and final result mapping.

Review:

- verify raw expanded content never reaches producer/rule/sink/log;
- verify unresolved Grant cannot expose SourceId;
- verify hash describes exact visible bytes.

### Slice 6 — Composition, durable sink, and NativeAOT

Red:

- C01–C10;
- original native artifact execution assertion.

Green:

- allocate a fresh Memory operation identity once per Agent Tool/MCP Capability
  execution and map correlation/causation/parent only from that matching scope;
- first-party Host/AOT fixture registration;
- PostgreSQL Audit sink composition test;
- Issue-local evidence ledger and `memory.md` update after evidence is green.

Review:

- inspect Agent Tool → Capability → Memory causality values;
- run full dependency boundaries;
- publish, link, and execute original linux-x64 native binary.

---

## 17. NativeAOT and Evidence Contract

The authoritative AOT gate extends the existing Agent Memory Tool fixture or
adds one dedicated fixture only if extension would weaken isolation. It must:

1. publish with `-p:CrestCreatesPublishMode=aot` for linux-x64;
2. complete native link without reflection JSON fallback warnings;
3. execute the original native binary, not `dotnet` over managed output;
4. run Recall, Source Expansion, and at least one committed Curation operation;
5. exercise typed payload serialization, exact sanitizer rules, Recorder,
   integrity projection, and an actual configured sink;
6. verify Agent Tool, Capability, and Memory facts remain distinct/correlated;
7. print `CRESTCREATES_AGENT_MEMORY_ACCOUNTABILITY_OK` only after assertions.

The durable PostgreSQL Audit sink composition must separately prove Accepted,
Duplicate, Conflict, first-snapshot retention, and tenant isolation. It may run
in the existing Phase 9b PostgreSQL fixture. A JIT PostgreSQL test does not
replace the Memory bridge NativeAOT gate, and an InMemory native gate does not
replace durable sink composition evidence.

Required focused evidence before completion:

```text
Agent Memory Abstractions/Runtime tests
Agent Memory ReadCore tests
Agent Memory Tools + E2E tests
MCP Memory tests + E2E tests
Accountability Runtime tests
PostgreSQL Audit sink shared contracts/composition
Dependency boundary tests
Agent Memory Accountability NativeAOT publish-link-run
Canonical solution build
```

No `NativeAOT-verified` claim is made from `IsAotCompatible`, trim analysis,
source-generated JSON unit tests, or publish without running the original
binary.

---

## 18. Review Guardrails

Every implementation review answers these questions with code/test evidence:

1. Can any formal Memory operation omit OperationId/OccurredAt or generate a
   fallback for either field?
2. Can CorrelationId, CausationId, Capability StartedAt, resource ID, or CLR
   type replace the stable operation identity?
3. Can republication of an established fact change OccurredAt or any causal
   envelope field and still be called Duplicate?
4. Can any hidden MemoryId/SourceRef/DescriptorRef influence a Recall/Expansion
   effective-result hash, including through a provenance-aware domain
   CanonicalContentHash?
5. Can Retriever or Expander call Recorder/producer directly?
6. Can a Store reference or emit Accountability semantics?
7. Can Promote, Reject, Supersede, or Archive bypass the conditional curation
   mainline, including when the null producer is selected?
8. Can an arbitrary provider exception become a deterministic failed mutation?
9. Can a cancelled business token suppress recording after terminal result?
10. Can recorder/sink failure alter the Memory result or exception?
11. Can Source Expansion trust a property merely because it is named
    SanitizedContent?
12. Does the expansion hash describe the exact final visible value?
13. Can an unresolved Grant expose GrantId, SourceId, or existence?
14. Can raw content, prompt, reason, explanation, message, exception, Tag,
    DisplayName, or TraceAttribute reach payload/sink/log?
15. Can the payload sanitizer accept an unknown Kind/Version/property?
16. Can a sanitizer rewrite a protected semantic fact?
17. Is AuditId derived through the canonical hash runtime from the exact v1
    identity projection?
18. Does same ID/same fact reach Duplicate and same ID/different fact reach
    Conflict in both InMemory and PostgreSQL sinks?
19. Do Agent Tool, Capability, and Memory facts remain separate and exactly
    correlated?
20. Are Memory CorrelationId, CausationId, and ParentAuditId taken from one
    matching authoritative Capability scope rather than re-derived from
    Agent/MCP fields?
21. Does a fresh Capability execution allocate a fresh Memory operation
    identity, and does accidental cross-execution OperationId reuse reach
    Conflict rather than Duplicate?
22. Can any Accountability fact drive Tool dispatch, Memory state, approval, or
    activation?
23. Does any production JSON path use reflection or open object payloads?
24. Did the original NativeAOT binary execute every new payload/write path?

---

## 19. Exit Criteria

Phase 9b+ is complete only when:

- Recall, Promote, Reject, Supersede, Archive, and Source Expansion project
  their scoped known results through one Memory producer into `IAuditRecorder`;
- curation and read executions freeze OperationId + OccurredAt once per
  admitted Memory execution, while established fact republication preserves
  the pair unchanged;
- deterministic AuditId Duplicate/Conflict behavior passes against the durable
  Audit sink;
- ReadCore hashes describe only effective visible results and never reuse raw
  Retriever ScopeFingerprint/VisibleMemorySetHash/CanonicalPackHash or
  provenance-aware domain CanonicalContentHash;
- Source Expansion independently sanitizes and hashes exact visible content,
  with its intentional caller-visible safety cutover documented and tested;
- every formal curation provider, including standalone null-producer
  composition, is `ConfirmedAtomic` for Promote/Reject/Supersede/Archive and no
  legacy non-conditional fallback remains;
- unknown curation outcomes never become false failures;
- recording failure never changes Memory behavior or lifecycle state;
- no Memory-specific sink/store/envelope/schema exists;
- payloads contain no forbidden raw/free-form content and use exact generated
  JSON roots;
- Agent Tool, Capability, MCP, and Memory composition evidence proves the
  authoritative Capability correlation/causation/parent mapping;
- a fresh Capability execution receives a fresh Memory operation identity, and
  accidental OperationId reuse across Capability executions proves Conflict;
- NativeAOT publish/link/run prints the required sentinel;
- dependency boundaries and the canonical solution build are green;
- `memory.md` records verified evidence without overclaiming #25 or #55.

The final architecture remains:

```text
one stable operation identity
one effective result boundary
one safe semantic payload projection
one unified Accountability write path
```
