# Phase 8d+ — Governed Agent Memory Tool Projection Design

**Date**: 2026-07-17
**Status**: APPROVED — implementation may begin with Slice 0
**Issue**: #53
**Depends on**: Phase 7e+ Agent Memory Runtime, Phase 7g+ LLM-backed Agent Memory Adapter, Phase 8f Agent Tool Projection
**Revision**: 7 — closes the final execution-branch review by replacing the single preflight receipt with a bounded prepared-outcome set and selecting branch-specific audit facts only after the final output matches exactly one predeclared outcome

## 1. Goal and positioning

Project the seven selected Agent Memory operations as governed Agent Tools
without creating a second Memory facade, a second Agent governance runtime, or
a direct service-call escape hatch around Capability.

The only execution mainline is:

```text
Memory-owned Capability + Schema descriptors
  -> [AgentToolSpec]
  -> generated AgentCapabilityToolDescriptor + exact binding
  -> Phase 8f discovery / roles / invocation fencing
  -> Phase 8f approval / Tool budget / governance audit
  -> ICapabilityDispatcher(InvocationSource.Agent)
  -> Capability Pipeline authorization / validation / audit / idempotency
  -> Agent Memory Capability Handler
  -> existing IAgentMemory* runtime contracts
  -> tool-safe projection with IsAuthoritative = false
```

This phase is an outward adapter over the existing Memory runtime. It owns
Tool-safe contracts, Memory Capability descriptors and handlers, access-scope
composition, source-expansion grants, permission definitions, projection-safe
audit metadata, DI, and executable projection tests.

It does not move Tool, approval, budget, audit, or visibility concerns into
`CrestCreates.Agent.Memory` or `CrestCreates.Agent.Memory.Abstractions`.

## 2. Repository facts that constrain the design

The design is based on the implemented repository rather than the original
issue wording alone.

1. Phase 8f is complete. `Agent.Tools` already owns trusted invocation identity,
   role/origin filtering, exact binding, logical invocation leases, approval,
   Tool budget, governance audit, Dispatcher integration, terminal replay, and
   Indeterminate fencing. This phase must reuse those state machines unchanged.
2. `AgentToolSpec` selects an existing Capability. It does not generate the
   Capability or its Schemas. This adapter must provide stable, compile-time
   Capability and Schema descriptor providers as well as the Tool specs.
3. The current shared Tool Schema path accepts only flat object roots with
   primitive fields and primitive collections. Natural Memory outputs contain
   nested item, candidate, block, diagnostic, and expansion-grant collections.
   Encoding those as JSON strings or parallel arrays would create a string
   protocol and is rejected.
4. The current `HandlerInvokerSourceGenerator` constructs handlers with
   `new Handler()`. Memory handlers require stores, retriever, compressor,
   extractor, promotion service, trusted context, access policy, grant service,
   and `TimeProvider`. A service-locator call inside business handlers is not
   acceptable.
5. `AddCrestAgentTools()` currently captures one `AgentToolJsonOptions` instance
   and `TryAddSingleton` means independently packaged Tool modules cannot safely
   contribute their source-generated JSON contexts. Memory Tools need an
   explicit, deterministic module-contribution path.
6. `AgentMemoryQuery.VisibleDescriptorRefs` currently treats an empty list as
   unrestricted and uses any-overlap matching. It also ignores descriptor refs
   carried only by source refs. This cannot represent a closed-world empty
   visibility universe and can leak a multi-descriptor memory through one
   visible ref.
7. `VisibleDescriptorKinds` cannot be evaluated by the current retriever because
   `DescriptorRef` does not carry `DescriptorKind`; the retriever correctly
   returns an empty result. Tool arguments therefore must not expose kinds as an
   authority boundary.
8. `DefaultAgentContextSourceExpander` accepts a complete
   `AgentContextSourceRef`. Passing a model-authored ref directly would turn it
   into a tenant-scoped store probing API.
9. Compression and extraction services return values but do not persist them.
   The adapter handlers must explicitly load trusted stored input, invoke the
   registered implementation, validate the returned tenant/lifecycle shape,
   and persist through the existing stores.
10. `DefaultAgentMemoryPromotionService` is the required promotion mainline,
    but it currently reports lifecycle failures through string-bearing
    `InvalidOperationException` and performs multi-write transitions without a
    distributed atomicity claim. The adapter must not parse exception messages
    and must not claim distributed exactly-once or cross-store atomicity.
11. Phase 8f governance finalization currently carries the full
    `AgentToolInvocationOutcome` to an auditor, and the development auditor
    retains it. That is incompatible with the Memory requirement that audit
    storage contain safe metadata and outcome hashes, not recalled text.
12. `AgentMemoryPack.IsAuthoritative` is already always false. Tool projections
    must preserve that invariant and must not introduce a second authority flag
    that can disagree.
13. Conversation/TaskEvent expansion currently chains two indexed `Where`
    operations, so the second index is relative to the filtered sequence.
    Compressed-context expansion ignores Range and returns every block. A valid
    grant therefore does not currently prove range-safe expansion.
14. Deterministic and LLM Memory implementations currently let source/provider
    values become `ContextId`, `BlockId`, and `CandidateId`; promotion also uses
    CandidateId as MemoryId. In the LLM compressor, `ContextId` is currently the
    TenantId. These values are neither safe store identities nor safe model
    handles.
15. Current supersession accepts a complete replacement candidate, does not
    reload or validate its tenant/status, and does not consume it after success.
    One candidate can therefore corrupt more than one supersession chain.
16. A Host may register any compressor/extractor implementation. Tenant and
    visibility checks alone do not prove that returned source/descriptor refs
    were derived from the loaded input, so provenance closure must be enforced
    outside provider-specific parsers.
17. Capability Pipeline converts Handler exceptions to a failed
    `CapabilityExecutionResult`; Agent Tool Invoker maps that result to
    `CapabilityFailure` and completes the logical invocation. Only a thrown
    Dispatcher call, timeout/lease uncertainty, or success-output finalization
    failure becomes Indeterminate. A post-Memory-write grant/handle failure is
    therefore a Completed failure on the current mainline.
18. `DescriptorRef` contains only `Namespace`, `Id`, and nullable `Version`.
    It has no DescriptorKind, so Memory visibility/hash code cannot include or
    infer Kind without opening a registry-resolution path.
19. `memory-content-hash-v1` accepts only one source tuple. The sanitizer uses
    the first SourceRef and the `"unknown"` sentinel for an empty list, so v1
    cannot bind complete multi-source provenance.
20. Promote, Reject, and Supersede currently perform load/check/write sequences
    without one shared conditional Candidate-consumption primitive. Concurrent
    calls can observe Candidate state before another call publishes its update.
21. Phase 8f already owns the structured five-field
    `AgentToolLogicalInvocationKey` and exact InvocationFingerprint, but the
    Capability Context currently receives only individual Agent/execution ids
    and governance artifact ids. A Handler cannot recover the exact binding
    without a new trusted snapshot item.
22. The current Memory store exposes unconditional `SaveCandidateAsync` and
    `SaveMemoryAsync` with no revision/expectation, even though present
    production callers use Candidate saves only for lifecycle changes.
23. System.Text.Json source generation transitively emits nested member type
    metadata. Unique ownership for every nested CLR Type is therefore not a
    realizable multi-context rule; root ownership and nested contract parity are
    separate concerns.
24. Current Schema fields do not carry enum allowed-values. A string-enum Tool
    contract must be enforced by exact generated binding/output validation or
    require a separate Schema protocol expansion.
25. The shared Governance Outcome Hasher currently writes
    `agent-tool-outcome-v1` for every Tool and includes Message and
    StructuredOutput. A Memory-only v2 write path would create two audit
    mainlines.
26. The implemented audit projection can derive output facts only from the
    model-visible output DTO. `CapabilityExecutionResult` returns Status,
    Output, errors, issues, duration, and existing audit/event ids, but does not
    return the original Capability Context or a trusted sidecar. Authorized
    Handler-only scope/hash/count facts therefore have no path back to the
    Agent Tool Invoker.
27. The current Memory store has no declared curation outcome guarantee. A
    future durable provider could commit a lifecycle transition and then throw
    before acknowledging it; the present Capability/Invoker path would convert
    that exception into a Completed failure even though commit state is
    unknown.
28. Exact Agent Tool output serialization and OutputSchema validation currently
    happen in the Invoker only after Dispatcher returns. A mutating Handler has
    no shared generated preflight contract that can prove the exact Completed
    envelope before its first Memory write.
29. Revision 4 security-artifact batches bind origin, purpose, and ordinal but
    not the concrete resource/grant plan. The same origin key could therefore
    be presented with a different authorized resource graph without a stable
    idempotency/conflict rule.
30. `CanonicalContentHash` and diagnostic `Severity` are named in Tool DTOs but
    do not yet have exact independent wire shapes. The domain CanonicalHash
    contains internal metadata, while domain `SeverityLevel` is an open
    semantic value object with Info/Warning/Review/Error/Blocker values.
31. The Tool Handler calls `IAgentMemoryPromotionService`, and the default
    implementation is replaceable through DI. A capability declared only by
    its underlying `IAgentMemoryStore` cannot prove that the selected Service
    performs no fallible work after commit or even uses that Store instance.
32. A first output preflight currently returns only a Handler-local prepared
    value. Arrays and ordinary `IReadOnlyList<T>` graphs are not recursively
    immutable, so the Invoker cannot prove that its post-commit serialization
    matches the exact bytes validated before mutation.
33. The invocation fact buffer belongs to `Agent.Tools` runtime, but Revision 5
    incorrectly assigned its creation/context propagation registration to
    `AddAgentMemoryTools()`, whose module may depend only on Agent Tools
    abstractions.
34. Reject changes Candidate lifecycle state but was omitted from the explicit
    mandatory preflight-before-mutation operation list.
35. Expand supports character truncation while its model-visible content-hash
    rule does not distinguish a full persistent artifact from a returned slice;
    Tool Confidence also gives the fail-closed zero enum value a legal wire
    representation.
36. A curation call can legitimately return a confirmed-zero-write Conflict or
    ResourceUnavailable after the Handler preflights a Completed result. A
    single immutable receipt therefore turns a normal conditional-lifecycle
    branch into post-dispatch `output_finalization_failure`.
37. Revision 6's common internal-fact sidecar can receive success-only facts
    before the domain call. If the selected curation branch is Conflict, those
    facts could contradict the final envelope unless fact ownership is split
    into branch-invariant, actual-output, and predeclared branch-internal sets.

These facts produce a small set of mandatory shared-platform prerequisites in
section 5. They are not permission to implement Memory-specific reflection,
JSON strings, service locators, registry lookups, or governance fallbacks.

## 3. Scope

The first closure exposes exactly these tools:

```text
BuildAgentMemoryPack
ExpandAgentMemorySource
CompressAgentHistory
ExtractMemoryCandidates
PromoteMemoryCandidate
RejectMemoryCandidate
SupersedeMemoryItem
```

The adapter supports whichever `IAgentContextCompressor` and
`IAgentMemoryExtractor` implementations the Host registered. The implementation
may be deterministic or LLM-backed. This phase does not select a provider and
does not add a new LLM client.

### 3.1 Non-goals

- MCP, HTTP, Dynamic API, CLI, or provider-SDK projection.
- A planner, model loop, Agent session runtime, or general Agent Runtime.
- A new Memory facade or duplicate Memory lifecycle service.
- A durable Memory provider or distributed Memory transaction coordinator.
- Autonomous compaction, extraction, promotion, deduplication, or retention.
- A new LLM compression/extraction implementation.
- HumanTask approval orchestration or approval UI.
- Runtime Activation Gate access.
- Descriptor draft, registry, package, snapshot, or runtime-registry mutation.
- Treating recalled, compressed, extracted, or promoted Memory as verified
  Metadata, activation evidence, or business truth.
- Distributed exactly-once or cross-store atomicity claims.
- `ArchiveMemoryItem`; it remains a later, separately reviewed addition.

## 4. Project and dependency boundaries

Add the outward adapter slice:

```text
src/Runtime/Agent/
├── CrestCreates.Agent.Memory.Tools.Abstractions/
└── CrestCreates.Agent.Memory.Tools/

tests/Runtime/Agent/
├── CrestCreates.Agent.Memory.Tools.Tests/
├── CrestCreates.Agent.Memory.Tools.E2E.Tests/
├── CrestCreates.Agent.Memory.Tools.AotFixture/
└── CrestCreates.Agent.Memory.Tools.AotFixture.Tests/
```

`CrestCreates.Agent.Memory.Tools.Abstractions` owns:

- Tool-safe input/output DTOs;
- stable Tool, Capability, role, permission, status, and diagnostic names;
- Host-supplied access-scope and history-source authorization contracts;
- opaque expansion-grant store contracts;
- opaque resource-handle store and trusted history-handle issuance contracts;
- safe Memory Tool audit-fact contracts when the shared Phase 8f contract needs
  a Memory-owned projection type.

Allowed dependencies are limited to:

```text
Agent.Memory.Abstractions
Agent.Abstractions
Metadata.Abstractions
```

It does not reference Memory runtime implementations, Agent.Tools runtime,
Capability runtime, Control Plane, HumanTask, DescriptorDraft, Web, persistence
providers, or an LLM/provider SDK.

`CrestCreates.Agent.Memory.Tools` owns:

- stable Schema and Capability descriptor providers;
- `[AgentToolSpecs]` declarations;
- Capability handlers and orchestration services;
- permission-definition provider;
- access-scope enforcement and source-grant issue/resolve services;
- Tool-safe output projection and audit-fact projection;
- DI and eager prerequisite validation.

Its allowed dependencies are:

```text
Agent.Memory.Tools.Abstractions
Agent.Memory.Abstractions
Agent.Tools.Abstractions
Agent.Abstractions
Capability.Abstractions
Metadata.Abstractions / Metadata runtime bootstrap
Schema.Abstractions
Authorization.Abstractions
MultiTenancy.Abstract
Microsoft.Extensions.DependencyInjection.Abstractions
```

The runtime adapter does not reference `CrestCreates.Agent.Memory`; the Host
chooses and registers the Memory implementation. It does not reference
`CrestCreates.Agent.Memory.Llm`; LLM selection remains a Host concern.

The following references are forbidden from both new projects:

```text
Agent.ControlPlane*
IRuntimeActivationGate
DescriptorDraft*
HumanTask runtime or approval orchestration
Framework/Api/DynamicApi
ASP.NET Core / Platform
MCP
provider SDKs
concrete persistence providers
mutable AgentToolRegistry / CapabilityRegistry / SchemaRegistry lookup
direct Handler resolver or Handler invocation
runtime assembly scanning
reflection JsonSerializer fallback
Dictionary<string, object?> Tool arguments
```

Generated/module-initializer registration through
`DescriptorProviderRegistry.Register<T>()` is the one bootstrap exception. No
handler or runtime orchestration class may read a descriptor registry or mutate
an already-built runtime registry.

The existing boundaries remain mandatory:

```text
Agent.Memory.Abstractions  × Agent.Tools / ControlPlane / Web / Platform
Agent.Memory runtime       × Agent.Tools / ControlPlane / Web / Platform
Agent.Memory.Llm           × Agent.Tools / ControlPlane / Web / Platform
```

## 5. Mandatory shared-platform prerequisites

These are implementation prerequisites, not alternate Memory paths.

### 5.1 Bounded nested Schema/JSON projection

Extend the protocol-neutral Schema projection kernel used by Agent and MCP to
support closed nested DTOs and collections of closed nested DTOs.

The supported addition is deliberately narrow:

- object properties whose type is an exact referenced `SchemaDescriptor`;
- arrays/lists of an exact referenced `SchemaDescriptor`;
- recursive use of the existing primitive/primitive-collection subset inside
  those referenced objects;
- deterministic maximum depth, maximum referenced-schema count, and cycle
  rejection;
- exact source-generated `JsonTypeInfo` parity at every node;
- closed properties, duplicate rejection, required/nullability, and current
  scalar constraints at every node.

Protocol limits are fixed for v1 nested projection:

```text
root depth = 0
maximum nested depth = 4
maximum distinct referenced Schemas = 64
maximum fields across the resolved graph = 256
```

Still unsupported:

- dictionaries and additional-properties bags;
- unions, `oneOf`, `anyOf`, polymorphism, interfaces, or abstract DTOs;
- arbitrary CLR object roots, reflection discovery, or runtime type scanning;
- schema Latest/Compatible references;
- cycles or unbounded recursive shapes.

Add one explicit field-level nested-schema reference:

```csharp
public sealed class SchemaFieldDescriptor
{
    // Existing scalar/primitive-collection fields remain unchanged.
    public VersionedDescriptorRef<SchemaDescriptor>? ObjectSchema { get; init; }
}
```

The fixed interpretation is:

| `IsCollection` | `ObjectSchema` | Meaning |
| --- | --- | --- |
| false | null | existing scalar field |
| true | null | existing primitive collection |
| false | exact ref | one closed nested object |
| true | exact ref | collection of closed nested objects |

When `ObjectSchema` is non-null, the exact neutral marker is
`FieldType="object"`; `CollectionElementType` must be null. Arbitrary CLR names
are rejected. The reference must be Exact with `Version > 0`.

JSON Schema output uses one root object plus `$defs`/`$ref`; it does not inline
the same nested Schema differently at each use site. A `$defs` key is:

```text
schema-<lowercase SHA-256 of canonical [Namespace, Id, Version] JSON>
```

and `$ref` is the corresponding local JSON Pointer. The full digest is used;
truncation is forbidden. Object-collection elements are non-null in this first
shape. Field `IsNullable` controls the property value, not individual array
elements.

Existing `SchemaDescriptor.References` remains the direct-reference graph index
for compatibility and topology; it is not a transitive closure. Generated and
handwritten descriptors must derive it as the Ordinal-distinct set of the
current Schema's field-level `ObjectSchema` refs. Recursive resolution follows
each referenced Schema's own direct refs. A mismatch fails registry validation
so the two collections cannot become independent truths.

This is a formal contract revision:

```text
Schema ContractHash shape   -> schema-contract-hash-v3
Schema DefinitionHash shape -> schema-definition-hash-v3
```

The v3 field profiles include `ObjectSchema`. Old flat v2 descriptors retain
byte-identical v2 golden results when evaluated under the v2 profile; versioned
compatibility tests prove the explicit transition rather than silently changing
old hashes.

This change requires synchronized updates to Schema validation, JSON Schema
projection, `JsonTypeInfo` parity, canonical hashing, compatibility rules,
relationship extraction, generator DTO validation, MCP/Agent bindings, package
and snapshot compatibility, E2E, and both linux-x64 NativeAOT fixtures.

If those gates cannot be preserved, implementation stops. It must not fall back
to JSON-in-string fields or parallel arrays for Memory items.

### 5.2 Composable source-generated JSON contexts

Add a deterministic explicit contribution contract to
`CrestCreates.Agent.Tools.Abstractions`:

```csharp
public sealed record AgentToolJsonTypeContract
{
    public required Type ClrType { get; init; }
    public required string ContributorId { get; init; }
    public required VersionedDescriptorRef<SchemaDescriptor> SchemaRef { get; init; }
    public required CanonicalHash ContractFingerprint { get; init; }
    public required bool IsBindingRoot { get; init; }
}

public interface IAgentToolJsonContextContributor
{
    string Id { get; }
    int Order { get; }
    IReadOnlyList<AgentToolJsonTypeContract> TypeContracts { get; }
    JsonSerializerContext Create(JsonSerializerOptions sharedOptions);
}
```

`AddCrestAgentTools()` creates one shared options/settings authority and applies
Host configuration once. Before contribution it closes configuration, records
a canonical settings snapshot, and passes the same Options instance to every
contributor. The instance is not yet `MakeReadOnly()` because the framework
still owns final resolver-chain composition. Contributors may construct only
their generated context and may not mutate naming policy, number handling,
converters, enum behavior, or resolver fallback; the framework compares the
settings snapshot after every call and requires the Context's `Options` to be
that shared instance.

Runtime orders returned contexts by `Order`, then `Id` with Ordinal comparison,
validates unique IDs, composes only their generated resolvers, rejects
reflection-capable resolvers, then calls `MakeReadOnly()` on the shared Options.
It captures all registered root and nested `JsonTypeInfo` instances, performs
full directional parity, and publishes one read-only resolver-chain snapshot.

Unique ownership applies only to exact Tool binding roots. Each selected input
root Type and output root Type has exactly one `IsBindingRoot=true` contract;
another Contributor claiming or returning a root contract fails startup. The
resolver used for exact binding/serialization of that root is always its owner.

Source-generated nested metadata may legitimately appear in multiple Contexts.
Duplicate nested Type contracts are allowed only when all copies:

- use the same shared `JsonSerializerOptions` instance;
- reference the same exact SchemaDescriptor;
- have the same required/nullability and normalized JSON property contract;
- use the same converter, enum-wire, number, collection, and ignore policy;
- pass full directional parity against that Schema; and
- have the same `agent-tool-json-contract-v1` ContractFingerprint.

The fingerprint is a full structured CanonicalHash. Its canonical payload binds
stable CLR contract id, exact SchemaRef, ordered JSON property names/order,
required/nullability, scalar/collection/nested wire shape, converter semantic
id, enum wire map, number handling, and ignore conditions. It never hashes
runtime reflection output. `TypeContracts` and `typeof(T)` expressions are
generated; runtime scanning is forbidden.

A repeated binding root fails even when fingerprints match. A repeated nested
Type with a different SchemaRef/fingerprint/parity fails. Equivalent repeated
nested metadata is accepted because System.Text.Json transitively generates
member types; deterministic Contributor order is not treated as contract
override. Shared DTOs may remain shared without an impossible single-Context
constraint.

`AddAgentMemoryTools()` explicitly registers the Memory Tool generated context
as a contributor. Multiple modules can therefore contribute contexts without
registration-order mutation or `TryAddSingleton` silently discarding a later
configuration.

### 5.3 DI-safe Capability handler activation

Upgrade `HandlerInvokerSourceGenerator` to emit an
`ICapabilityContextAwareHandlerInvoker` that resolves the exact handler from
`CapabilityExecutionContext.ServiceProvider` and invokes it through its typed
`ICapabilityHandler<TInput,TOutput>` contract.

Rules:

- the generator emits one
  `IGeneratedCapabilityHandlerRegistrationProvider` per compilation, carrying
  the exact CapabilityName, Handler type, closed interface, and generated
  invoker type for every declaration plus stable `ProviderId` and `ModuleId`;
- a module initializer adds only the immutable Provider definition to the
  process bootstrap-definition registry; it does not select the Provider,
  resolve services, or mutate a Host;
- each module opt-in extension, including `AddAgentMemoryTools()`, adds its
  generated `ProviderId`/`ModuleId` selection marker to that current
  `IServiceCollection`;
- `AddCapabilityRuntime()` snapshots the markers from that
  `IServiceCollection`, resolves and applies only those selected Provider
  definitions exactly once, registers each exact Handler as Scoped and each
  generated invoker as Singleton, and creates that Host's immutable resolver
  index from the same selected entries;
- generated infrastructure may resolve the handler; handler code may not call
  `IServiceProvider`;
- no `Activator.CreateInstance`, constructor reflection, runtime scanning, or
  `new Handler()` fallback remains on the generated mainline;
- handler lifetime follows the DI registration and current Capability scope;
- missing registration fails deterministically and is covered by startup/E2E
  tests;
- existing parameterless handlers migrate to the same generated DI path rather
  than retaining two invoker implementations.

The existing direct static `CapabilityHandlerResolverProvider.Register(id,
invoker)` bootstrap is migrated into this provider/finalization path for
generated handlers; it does not remain a second formal registration truth.
Explicit delegate registration is isolated as legacy/JIT compatibility and is
not accepted for an Active generated native Capability or any Memory Tool.

Registration rules are fail-closed:

- duplicate CapabilityName inside one compilation is a generator error;
- duplicate CapabilityName across selected assemblies is an eager startup
  error; an unselected Provider contributes nothing to that Host;
- duplicate identical selection markers are idempotent; missing definitions,
  a ProviderId/ModuleId mismatch, or selection after Host finalization fails
  startup;
- one Handler must implement exactly one closed
  `ICapabilityHandler<TInput,TOutput>` for its `[CapabilityName]` declaration;
- a Handler implementing multiple closed Capability interfaces is rejected and
  must be split into explicit Handler classes;
- `AddAgentMemoryTools()` does not create a Memory-only Handler activation path;
  Memory handlers use the same generated registration provider as every native
  Capability;
- the generated invoker and service registration share one exact Handler type
  identity, so an unregistered or mismatched type fails startup.

This is reusable Capability infrastructure and closes the existing gap for any
native handler with dependencies.

The process-global definition registry is discovery metadata only. It is never
enumerated as implicit Host opt-in and never becomes the runtime Handler
resolver. Two service collections in one process may select disjoint module
sets without Handler leakage; referenced-but-unselected and test-only Provider
definitions remain inactive.

### 5.4 Closed-world Memory visibility boundary

Replace the ambiguous recall visibility inputs with one explicit Memory-owned
boundary, conceptually:

```csharp
public sealed record AgentMemoryVisibilityBoundary
{
    public required IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; }
    public required bool AllowUnscopedMemory { get; init; }
}
```

`AgentMemoryQuery` carries this boundary as a trusted caller-supplied value.
Legacy `VisibleDescriptorRefs`/`VisibleDescriptorKinds` do not remain as a
second formal mainline. Migration is hard or explicitly time-bounded with an
exit issue.

The retriever applies these rules before count and character budgets:

1. Build the effective descriptor-ref closure from
   `AgentMemoryItem.DescriptorRefs` and every nested
   `AgentContextSourceRef.DescriptorRefs`.
2. If the closure is empty, include only when `AllowUnscopedMemory` is true.
3. If the closure is non-empty, every ref must be present in the visible set.
   One visible ref cannot disclose a memory also bound to an invisible ref.
4. Every boundary ref and every Memory/source ref used on the Tool path must
   have `Version > 0`. Null, zero, Latest-like, Compatible-like, or otherwise
   unpinned refs fail closed and are treated as invisible.
5. An empty visible set is a valid closed-world scope and returns only
   explicitly allowed unscoped memory.
6. Visibility filtering occurs before `MaxCount`, character budget, eligible
   set hashing, and final pack hashing.
7. Visibility diagnostics never reveal hidden ids, kinds, counts, or hashes.

Descriptor identity and equality on this Memory path are exactly:

```text
Namespace (Ordinal)
Id        (Ordinal)
Version   (> 0)
```

DescriptorKind is not part of `DescriptorRef` and is not inferred from a
Registry. The same comparer is reused by visibility closure, grant/handle scope
checks, provenance closure, scope fingerprinting, and canonical hashing. Adding
Kind to the global ref contract would require a separate Metadata breaking
phase and is outside #53.

The Memory runtime still does not resolve descriptor visibility. A Host-facing
adapter must first resolve its visible universe to exact versions and supplies
that already-resolved boundary. The Tool adapter never performs Latest or
Compatible resolution. The Memory canonical projector
computes the recall `ScopeFingerprint` from the trusted query and boundary; a
Host or model never supplies a hash as authority.

### 5.5 Typed Memory lifecycle failures

Add stable typed Memory operation failures/results for at least:

```text
ResourceUnavailable
InvalidLifecycleState
TenantMismatch
MissingActor
MissingReason
MissingTimestamp
MissingSourceOrExplanation
```

`DefaultAgentMemoryPromotionService` uses those types. Memory Tool handlers map
only known codes to safe Tool statuses and never parse exception messages.
Unknown exceptions remain generic Capability failures.

Candidate consumption and supersession invariants belong to that domain
service, not to the Tool handler. Change the authoritative contracts to accept
version-bound transition plans rather than caller-supplied mutable snapshots:

```csharp
public sealed record AgentMemoryCandidateExpectation
{
    public required string CandidateId { get; init; }
    public required CanonicalHash ExpectedStateHash { get; init; }
}

public sealed record AgentMemoryItemExpectation
{
    public required string MemoryId { get; init; }
    public required CanonicalHash ExpectedStateHash { get; init; }
}

public sealed record AgentMemoryPromotionPlan
{
    public required AgentMemoryCandidateExpectation Candidate { get; init; }
    public required string NewMemoryId { get; init; }
    public required CanonicalHash ExpectedMemoryContentHash { get; init; }
    public required CanonicalHash ExpectedMemoryStateHash { get; init; }
    public required AgentMemoryOperationRequest Operation { get; init; }
}

public sealed record AgentMemorySupersessionPlan
{
    public required AgentMemoryItemExpectation TargetMemory { get; init; }
    public required AgentMemoryCandidateExpectation ReplacementCandidate { get; init; }
    public required string NewMemoryId { get; init; }
    public required CanonicalHash ExpectedMemoryContentHash { get; init; }
    public required CanonicalHash ExpectedMemoryStateHash { get; init; }
    public required AgentMemoryOperationRequest Operation { get; init; }
}

ValueTask<AgentMemoryItem> PromoteAsync(
    string tenantId,
    AgentMemoryPromotionPlan plan,
    CancellationToken cancellationToken = default);

ValueTask<AgentMemoryItem> SupersedeAsync(
    string tenantId,
    AgentMemorySupersessionPlan plan,
    CancellationToken cancellationToken = default);

ValueTask RejectAsync(
    string tenantId,
    AgentMemoryCandidateExpectation candidate,
    AgentMemoryOperationRequest operation,
    CancellationToken cancellationToken = default);
```

Curation certainty is declared at both the called Service boundary and its
internal Store primitive in `Agent.Memory.Abstractions`:

```csharp
public enum AgentMemoryCurationOutcomeGuarantee
{
    Unknown = 0,
    ConfirmedAtomic = 1
}

public interface IAgentMemoryStoreCapabilities
{
    AgentMemoryCurationOutcomeGuarantee CurationOutcomeGuarantee { get; }
}

public interface IAgentMemoryCurationServiceCapabilities
{
    AgentMemoryCurationOutcomeGuarantee OutcomeGuarantee { get; }
}
```

For the actual `IAgentMemoryPromotionService` call, `ConfirmedAtomic` means:

- a committed success means the complete expected-state transition is durable
  within the Service's declared curation boundary;
- a typed conflict/failure means zero writes from that transition;
- an exception escaping the Service means zero writes from that transition; a
  Service that can throw after commit cannot claim this value;
- no partial Candidate consumption, new Memory creation, or supersession-link
  update is observable for a non-success result.

The Tool Adapter resolves `IAgentMemoryPromotionService` once from the finalized
Host scope and requires that same object reference to implement
`IAgentMemoryCurationServiceCapabilities` with
`OutcomeGuarantee=ConfirmedAtomic`. It never checks a concrete runtime type and
never accepts a separately registered capability object as an attestation for
another Service. `Unknown`, a missing implementation, a different object, or a
changed value after startup fails before curation Tool discovery/invocation.
The full `AddAgentMemoryTools()` profile fails closed rather than silently
publishing an unsafe partial curation surface.

When curation Tools are enabled, the selected Promotion Service is required to
be Singleton, matching the current runtime registration. Startup captures that
exact singleton in the immutable Memory Tool runtime binding, and generated
handlers receive the same instance through DI. Scoped/transient or factory
registrations that can yield another object fail startup; no Handler performs a
service-locator lookup or substitutes a later registration.

`IAgentMemoryStoreCapabilities` remains an internal proof consumed by the
first-party Promotion Service, not the Tool Adapter's trust boundary:

```text
Memory Tool Adapter
  -> trusts the exact selected Promotion Service guarantee

Default Promotion Service
  -> trusts the exact Store transition primitive guarantee
```

The first-party Service may expose `ConfirmedAtomic` only when all of these are
true:

- it uses one expected-state Store transition primitive for the entire Promote,
  Reject, or Supersede operation;
- the exact injected Store instance implements `IAgentMemoryStoreCapabilities`
  and declares that primitive `ConfirmedAtomic`;
- the committed result object is produced by that same atomic transition;
- it performs no event publication, secondary persistence, snapshot creation,
  projection, logging callback, or other possibly failing work after commit;
- cancellation is observed only before the atomic transition starts; once the
  primitive reports commit, later cancellation returns the committed success
  rather than throwing; and
- every exception that can escape the Service occurs before any transition
  write.

The first-party InMemory Store/Service pair earns the guarantee with one
store-owned lock and fault-injection tests at every pre-commit and post-commit
boundary. A custom Promotion Service must independently satisfy and expose the
same Service capability; a Confirmed Store does not bless unsafe Service code.

A durable Store/Promotion Service pair with an acknowledgement window or
unknown commit outcome must not enable these curation Tools in this phase. A
future contract may add a typed domain `Indeterminate` result and map it through
Capability/Phase 8f, but an exception must never be used as an implicit
unknown-commit signal. The gate does not claim distributed exactly-once and
does not strengthen compression or extraction multi-item persistence.

`NewMemoryId` is generated/preassigned by trusted adapter
infrastructure before security-artifact preparation. No identity, expectation,
state hash, or plan is accepted from a model or Provider.

Candidate payload is immutable after create. TenantId, Kind, Content,
Confidence, Tags, DescriptorRefs, SourceRefs, CanonicalContentHash,
redaction/sanitization data, and prompt evidence cannot be updated in place;
only a conditional lifecycle transition is legal. Active Memory payload is
also immutable; lifecycle status and supersession/archive links change only
through typed conditional transitions. `SaveCandidateAsync` and
`SaveMemoryAsync` leave the formal mainline and are replaced by create-only and
expected-state transition operations. Legacy/test compatibility, if retained,
is isolated and cannot update an artifact used by the Tool path.

`ExpectedStateHash` uses generated canonical profiles
`memory-candidate-transition-state-v1` and
`memory-item-transition-state-v1`. They bind the complete immutable snapshot,
current lifecycle status, and lifecycle relationship fields; every included
CanonicalHash is projected with full metadata. Adding a domain field requires
a new state-shape version. Handler, service, and store use the same generated
projector—no ad-hoc hash implementation exists in the adapter. State hashes
are internal conditional-write tokens and are never returned to the model or
stored as ordinary governance audit facts.

The Handler derives the exact Completed/Conflict/conditionally-Unavailable
envelopes, their receipts/facts, Handles/Grants, Candidate expectation, target-
Memory expectation, and expected new-Memory content/state hashes from the same
loaded snapshots. The domain service reloads authoritative state and the store
atomically requires:

```text
current state hash == ExpectedStateHash
current Candidate status == Candidate
target Memory status == Active                  // Supersede only
NewMemoryId does not exist
committed Memory content hash == ExpectedMemoryContentHash
committed Memory state hash == ExpectedMemoryStateHash
```

Any mismatch writes nothing and returns typed `InvalidLifecycleState`/
`Conflict`. A domain service may not commit a resource graph different from the
prepared result. This expectation remains required even though new Candidate/
Memory payloads are immutable; it binds legacy state, concurrent lifecycle
changes, and the prepared envelope to the exact authoritative snapshot.

The service reloads both resources from the authoritative store and validates:

- the target memory belongs to `tenantId` and is Active;
- the replacement belongs to the same tenant and is still Candidate;
- the replacement has not already been consumed by Promote or Supersede;
- the new Memory receives a fresh framework-owned MemoryId rather than reusing
  CandidateId;
- the new Memory uses the create-only store operation and a collision cannot
  overwrite an existing item;
- success moves the replacement Candidate to Active/consumed state;
- `old.SupersededBy == new.MemoryId` and
  `new.Supersedes == old.MemoryId` are written consistently;
- one Candidate cannot supersede two memories, including sequential retries
  under different Tool invocation ids.

Candidate consumption is one domain transition shared by Promote, Supersede,
and Reject. Exactly one transition from Candidate can win. The first-party
InMemory provider performs conditional state check, new-Memory creation (when
applicable), target-memory link/status changes, and Candidate status transition
under the same store-owned lock. It must not expose two successful results for
concurrent operations. A durable provider later implements the same semantic
contract with CAS/transaction/reconciliation appropriate to its declared
capability.

The domain service uses a provider-owned conditional transition primitive
behind `IAgentMemoryStore`; a scoped Handler/PromotionService lock is forbidden
because it does not coordinate service instances or nodes. Conflict from a
lost condition maps to the typed `InvalidLifecycleState` result.

The Tool handler performs outer access and visibility checks, then passes the
typed plan. It does not make lifecycle state authoritative and never supplies
an `AgentMemoryCandidate` snapshot as the command. The expectation and
preassigned id come from trusted preparation in section 5.11.

This change does not add transaction semantics. The in-memory provider remains
single-process and must satisfy the locked conditional transition above. A
future durable provider owns distributed compare-and-swap, transaction, and
reconciliation capability declarations. The absence of cross-store atomicity
does not permit a normal successful call to leave an unconsumed replacement,
create two Memories from one Candidate, or produce an internally contradictory
supersession chain.

### 5.6 Governance audit minimization and safe facts

Harden the Phase 8f auditor contract before returning Memory text:

- governance decision/finalization records carry a stable outcome summary
  (`Kind`, `Code`, safe issue codes) and required `OutcomeHash`;
- they do not require or retain `Message` or `StructuredOutput`;
- the invocation gate, not the governance auditor, retains the safe completed
  outcome needed for replay;
- an optional bounded list of typed audit facts supports only safe counts,
  statuses, hashes/HMACs, flags, source kinds, and handle kinds;
- fact names, counts, lengths, hash encodings, and duplicate names are validated;
- a generated/registered exact-type projector creates facts from trusted typed
  Memory input/output; the generic auditor never parses arbitrary JSON;
- ordinary SHA-256 outcome/content hashes are integrity identifiers, not
  confidentiality controls, and no raw sensitive source is hashed before
  sanitization.

The shared contracts are conceptually:

```csharp
public sealed record AgentToolGovernanceOutcomeSummary(
    AgentToolInvocationOutcomeKind Kind,
    string Code,
    IReadOnlyList<AgentToolInvocationIssue> Issues);

public enum AgentToolAuditFactKind
{
    Unknown = 0,
    HandleKind = 1,
    Count = 2,
    Status = 3,
    CanonicalHash = 4,
    ScopeHash = 5,
    Flag = 6,
    SourceKind = 7,
    CorrelationHmac = 8
}

public sealed record AgentToolAuditFact(
    string Name,
    AgentToolAuditFactKind Kind,
    string Value);

public enum AgentToolAuditFactOwnership
{
    Unknown = 0,
    Input = 1,
    Output = 2,
    BranchInvariant = 3,
    PreparedOutcomeInternal = 4
}

public sealed record AgentToolAuditFactDefinition(
    string Name,
    AgentToolAuditFactKind Kind,
    AgentToolAuditFactOwnership Ownership);

public sealed class AgentToolAuditProjectionContract
{
    public required string ToolDescriptorId { get; init; }
    public required int ToolDescriptorVersion { get; init; }
    public required Func<object?, IReadOnlyList<AgentToolAuditFact>>
        ProjectInputFacts { get; init; }
    public required Func<object?, IReadOnlyList<AgentToolAuditFact>>
        ProjectOutputFacts { get; init; }
    public required IReadOnlyList<AgentToolAuditFactDefinition>
        FactDefinitions { get; init; }
}

public interface IAgentToolInvocationFactSink
{
    void AddBranchInvariantFacts(
        IReadOnlyList<AgentToolAuditFact> facts,
        int requestedMaximum);
}

internal interface IAgentToolInvocationFactBuffer : IAgentToolInvocationFactSink
{
    AgentToolInvocationFactSnapshot Seal();
}

public sealed record AgentToolInvocationFactSnapshot(
    IReadOnlyList<AgentToolAuditFact> BranchInvariantFacts,
    int RequestedMaximum);

public interface IAgentToolAuditCorrelationProtector
{
    string Compute(
        string purpose,
        string resourceKind,
        string trustedResourceId);
}
```

`IAgentToolInvocationFactSink`, the fact/snapshot records, and the Capability
Context item name live in `Agent.Tools.Abstractions`. The internal owner
interface and concrete buffer live in `Agent.Tools`; they are shown together
above only to freeze the call surface and are not exposed to Memory handlers.

Contracts register by Tool descriptor id/version through a frozen startup
registry. Generated bridges require the exact declared input/output runtime
types before calling the Memory-owned typed projector. Input facts are attached
to the pre-dispatch checkpoint after exact binding; output facts are attached
only after exact output-type and OutputSchema validation. Every allowlisted fact
name has one frozen ownership; the common sink accepts only `BranchInvariant`,
and outcome receipts accept only `PreparedOutcomeInternal`.

For authorized facts that deliberately do not exist in the Tool DTO, the
Invoker creates one invocation-owned `AgentToolInvocationFactBuffer` after
pre-dispatch audit and before Dispatcher execution. It keeps the only sealing
reference and places an `IAgentToolInvocationFactSink` view over the same state
in `CapabilityExecutionContext.Items` under
`AgentCapabilityContextItemNames.InvocationAuditFactSink`. Memory Handler
infrastructure requires that exact typed sink and adds branch-invariant facts
plus the trusted scope's `MaxAuditFacts`; it never receives an auditor and cannot seal,
persist, or finalize facts. This ordinary sink accepts only facts true for every
predeclared result branch, such as visibility-scope hash, authorized resource
CorrelationHmac, and trusted SourceKind. `MaxAuditFacts` is carried as the
trusted `requestedMaximum` restriction, not stored as an audit fact.

Every successfully authorized Memory Handler calls `AddBranchInvariantFacts`
at least once, even when its list is empty, so the trusted scope cap is always
bound before success. A missing scope-cap contribution is an invalid Required-
audit result and follows the post-dispatch Indeterminate path.

The buffer is bounded by the Phase 8f global maximum at construction. Every
`AddBranchInvariantFacts` call is synchronous, copy-on-add, validates a positive
`requestedMaximum`, and only lowers the effective requested maximum. It rejects
post-Seal writes, duplicate/unknown fact names, unsupported kinds/encodings,
raw ids/tokens, and values outside fixed length/cardinality limits. It cannot
hold Memory/source text, arguments, explanations, exception data, HandleId,
GrantId, or raw persistent resource ids. Internal resource correlation enters
only as an already protected `CorrelationHmac` produced after authorized load.

After Dispatcher returns successfully, the Invoker performs final output
preflight, selects exactly one predeclared outcome receipt, then seals the
common buffer. It combines only:

```text
sealed branch-invariant facts
+ generated facts projected from the actual final envelope
+ internal facts attached to the one matched prepared outcome
```

The final cap is the minimum of the global maximum and sealed
`RequestedMaximum`; the combined ordered fact set is revalidated before
governance-outcome-v2 hashing and finalization. A Capability failure discards
every output/internal candidate fact and retains input facts only. Missing,
mismatched, throwing, over-cap, or invalid projection for a Required-audit Tool
fails closed through the existing post-dispatch Indeterminate path and never
causes generic JSON inspection.

No `AsyncLocal`, static/process buffer, Capability result extension, or Handler
call to the auditor is permitted. The sink is never placed in Tool DTOs,
persistence, replay outcome, or ordinary logs and becomes unusable after Seal
or invocation disposal.

Pre-dispatch facts are intentionally weaker than post-authorization facts. They
may contain requested counts/limits, SourceKind, HandleKind, HandleCount,
`HasHandle`, and `HasExplanation`. They never contain a HandleId, GrantId,
ContextId, BlockId, CandidateId, MemoryId, SourceId, grant-bound source data,
or a plain hash of any such token/id. `ArgumentsHash` already binds the exact
input bytes. A guessed or unavailable token records only `Unavailable`, never
the submitted token or its resolved resource id.

After an authorized load succeeds, finalization facts may contain returned or
persisted counts, lifecycle statuses, visibility-scope hash, safe canonical
content/pack hashes, truncation flags, and framework resource ids only when a
trusted internal auditor requires them. OperationStatus, returned/persisted
count, MemoryStatus, CandidateStatus, truncation, and returned canonical content
hashes come from the generated projector over the actual envelope, never the
common sidecar. A model-invisible pack/set hash that varies by branch must be a
`PreparedOutcomeInternal` fact on that branch. Prompt input/output and generic
Provider-output hashes are not governance facts because they can fingerprint
sensitive text. Such an internal
cross-system correlation value uses a Host-keyed HMAC with key rotation and
purpose separation; ordinary SHA-256 of an id is forbidden. It is not returned
to the model or exposed through the governance-audit query surface.
The encoded value is `v1.<key-id>.<base64url-HMAC-SHA256>`; the key id is not a
secret, and retired keys follow the audit retention policy. If a registered
projector declares a correlation fact, the protector is a conditional startup
requirement. Most Memory facts do not require resource correlation.

The outcome hash is upgraded to the exact security shape:

```text
agent-tool-governance-outcome-v2
  Kind
  Code
  Issues[]: safe Code + safe Path only
  validated AuditFacts[]: Name + Kind + safe Value
```

Issues and facts retain their validated canonical order. The v2 hasher never
reads or incorporates `Message`, `StructuredOutput`, Memory text, expanded
text, compressed text, candidate text, explanation, exception text, or raw
arguments. V1 (`agent-tool-outcome-v1`) remains only for reading historical
records and is never written by any new Phase 8f Agent Tool invocation. The
shared Invoker/Auditor has one new-write path:

```text
AgentToolGovernanceOutcomeHasher.Compute(v2 summary, validated facts)
```

Tools without a registered custom audit-fact projector use `AuditFacts=[]`;
they do not fall back to v1 or hash full Outcome bytes. Stored records carry the
outcome shape version so auditors can verify historical v1 without making it a
write option. Per-Tool outcome-hash version selection is forbidden. The loss of
a full-output byte proof is deliberate: governance audit is not the replay
store.
Safe issue Paths are generated Schema-property paths only; dynamic map keys,
resource ids, submitted values, and exception-derived segments are rejected.
Pre-dispatch projection uses the frozen Phase 8f global fact cap because the
Memory access scope is intentionally not resolved before authorization; every
generated input projector declares a fixed maximum at startup within that cap.
After dispatch, finalization uses the smaller of that global cap and the trusted
scope's `MaxAuditFacts`. The accepted bounded list is fixed before v2 hashing or
persistence.

The Invocation Gate is therefore a sensitive replay-payload store. Its
production implementation must enforce tenant/principal access isolation,
encryption at rest, explicit TTL followed by archive or destruction, and a
separate privileged replay-read path. Governance audit query APIs must never
return the stored completed Outcome. The development Gate is explicitly
volatile and may retain the full safe Outcome only for the process lifetime.

Memory facts must not contain Memory text, compressed text, candidate text,
source text, explanations, raw Tool arguments, approval evidence, secrets, or
exception messages.

The Phase 8f invocation, approval, budget, audit, and replay state machines do
not otherwise change.

### 5.7 Source Expansion Range Integrity

An opaque grant authorizes the exact stored `AgentContextSourceRef`; it does
not authorize neighboring records. Expansion validates before reading content:

```text
RangeStart and RangeEnd are either both present or both absent
RangeStart >= 0
RangeEnd >= RangeStart
RangeEnd < original source record count
```

Conversation turns and task events are sliced by original indexes in one
operation, for example `Skip(start).Take(end - start + 1)` after checking the
original count. Chained indexed `Where` calls are forbidden because a later
operator receives a reset index. Tests freeze non-zero starts, a single item,
the final item/range, and both lower/upper out-of-range cases.

Source-kind rules are exact:

- `ConversationTurn` uses ConversationId plus an inclusive turn-index Range;
- `TaskEvent` uses TaskId plus an inclusive event-index Range;
- `TaskRecord`, `MemoryItem`, and `MemoryCandidate` reject a Range;
- `CompressedContextBlock` uses a trusted framework BlockId and rejects a
  Range; `IAgentCompressedContextStore.GetBlockAsync(tenantId, blockId)` (or an
  equivalent exact indexed lookup) returns that one block only.

`CompressedContextBlock` must never interpret ContextId as authority to return
all Blocks. Missing resource, invalid Range, Range unsupported for the kind,
and boundary overflow all return the identical `Unavailable` result without
partial expansion, count disclosure, or a fallback to the full resource.

### 5.8 Trusted Memory artifact identity and opaque resource handles

The framework, outside every compressor/extractor/provider, owns all persistent
Memory artifact identities:

```text
ContextId
BlockId
CandidateId
MemoryId
```

Persistent ids use a cryptographically strong framework generator. They must
not contain or deterministically encode TenantId, ConversationId, TaskId,
TurnId, EventId, another artifact id, content, or content fragments. A Provider
may return a response-local label only to relate objects inside that one
response. The adapter resolves such labels against the current trusted input,
then discards them. Provider labels never become a store key, SourceRef
SourceId, persistent relationship id, audit resource id, or Tool output id.

Before the first save, the framework:

1. verifies response-local label uniqueness with Ordinal comparison;
2. assigns a fresh id to every output artifact;
3. verifies all assigned ids are unique in the batch;
4. checks the authoritative store for an existing-id collision;
5. retries only identity generation, never the Provider operation, on a random
   collision; and
6. fails before persistence when uniqueness cannot be established within a
   fixed bounded attempt count.

The Memory store contracts distinguish create from lifecycle update on this
mainline. Slice 0 adds create-only operations for a compressed Context plus all
its Blocks, a Candidate, and a Memory. A create-only operation atomically
rejects an existing id for that resource and never overwrites it; the
compressed-context create rejects the whole Context/Block batch on any
collision. Updates are only the expected-state lifecycle/link transitions from
5.5; unconditional Candidate/Memory replacement is not a formal operation.
Durable providers implement conditional insert/unique constraints;
the development store performs the check and insert under one lock. A
read-then-unconditional-save sequence is not sufficient collision protection.

For sequential Candidate creates, a collision retries only the fresh id for
that validated Candidate; already-created earlier Candidates remain subject to
the explicitly documented partial-batch limit. No collision path calls the
Provider again or reuses its local label as identity.

Promotion and supersession always assign a new MemoryId independent of the
CandidateId. Existing unsafe deterministic/provider-authored ids require a
separate data migration; they are never exposed through the Tool adapter.

The model receives a second opaque layer for Context, Candidate, Memory,
ConversationHistory, and TaskHistory resources. BlockId is not a Tool selector
and is omitted entirely rather than receiving an unnecessary handle. A
resource handle is a cryptographically random capability bound to ResourceKind,
internal ResourceId, TenantId, UserId, AgentId, ExecutionId, issuing invocation,
current scope fingerprint, the artifact's complete required descriptor closure/unscoped flag,
issued-at, expires-at, and state. History handles bind their authorized
SourceKind/SourceId instead of a Memory descriptor closure. Tool inputs and
outputs use only these handles. They do not expose persistent domain ids even
after the domain migration is complete.

```csharp
public interface IAgentMemoryResourceHandleStore
{
    ValueTask<AgentMemoryResourceHandleIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        int maxActiveHandlesPerResource,
        CancellationToken ct = default);
    ValueTask<AgentMemoryResourceHandle?> GetAsync(string handleId, CancellationToken ct = default);
    ValueTask RevokeAsync(string handleId, CancellationToken ct = default);
}

public interface IAgentMemoryHistoryResourceHandleIssuer
{
    ValueTask<string> IssueAsync(
        AgentMemoryHostArtifactBatchKey hostBatchKey,
        AgentMemoryToolPrincipal principal,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        CancellationToken ct = default);
}
```

Handle resolution performs the same principal/execution/current-scope checks
as source grants and maps malformed, expired, revoked, mismatched, and unknown
handles to `Unavailable`. A changed scope must still authorize the handle's
complete stored closure; history resolution instead re-runs the Host authorizer.
Resource handles select Memory artifacts; source
grants authorize content expansion. The two capability types are not
interchangeable. The history issuer is trusted Host/Agent infrastructure, calls
the history authorizer before issuance, and is not projected as a Tool.
`TryIssueBatchAsync` detects handle-id collisions and is all-or-none; the
adapter checks `MaxResourceHandlesPerInvocation` first and the store atomically
enforces `MaxActiveResourceHandlesPerResource`. Within the same tenant/user/
Agent/Execution/scope/resource/purpose binding, an unexpired Handle is reused
rather than creating another token; reuse never extends its expiry. The stable
batch key, not random HandleId, defines idempotence. Completed replay returns
original handles without renewal. Handle issuance/revocation is a security-
artifact state change, not a Memory lifecycle mutation; issuance does not
change the Phase 8f ReadOnly classification of Build/Expand.

The issuing InvocationId is immutable trace metadata, not a requirement that a
later caller have the same InvocationId. A handle/grant is intentionally usable
by later explicit Tool invocations in the same tenant/user/Agent/Execution while
its current scope and expiry remain valid; otherwise Compress -> Extract and
Extract -> Promote could not compose. It is never usable in another Execution.
History issuance validates the Host batch key and uses the coordinator's Host
origin overload; it never fabricates or requires an Agent Tool binding.

### 5.9 Provenance closure validation

Provider-produced provenance may reference trusted input; it may not create or
modify provenance.

For compression, the adapter constructs the allowed source-ref universe from:

```text
framework-generated exact refs for the loaded Conversation/Task records
+
exact SourceRefs already stored on the loaded Turns/Events
```

Every returned Block SourceRef must be structurally equal to an entry in this
universe. Equality covers TenantId, SourceKind, SourceId, inclusive Range,
DescriptorRefs, correlation/causation relationship, and existing canonical
hash fields. A Provider cannot change a Range, add a descriptor, substitute a
SourceKind/SourceId, or manufacture a relationship. Response-local ref labels
resolve only through the adapter-created map; an unknown label is invalid.

For extraction:

- every Candidate SourceRef is a structural member of the union of SourceRefs
  on the input Context's Blocks; and
- every Candidate DescriptorRef is a member of the input Context's effective
  exact-version descriptor closure.

The extractor cannot introduce an arbitrary DescriptorRef even when that ref
would be visible to the caller. Structural comparison uses one canonical,
strongly typed comparer, never provider JSON or display strings.

All blocks/candidates, response labels, ids, source refs, descriptor refs,
tenant values, lifecycle states, and resource limits are validated as a batch
before the first save. After final sanitation, the adapter recomputes
`CanonicalContentHash` from the exact sanitized content and trusted provenance;
it never accepts a Provider-supplied hash as authoritative. Any closure failure
fails the whole operation before persistence with the safe result-contract
diagnostic.

### 5.10 Memory canonical content, scope, and pack v2

The Tool path migrates directly to versioned shapes rather than modifying v1
bytes in place:

```text
memory-content-hash-v2
memory-scope-hash-v2
memory-pack-hash-v2
```

All three use `AlgorithmVersion=sha256-canonical-json-v1` and
`ContractVersion=memory-hash-v2`; each records its distinct canonical shape
version above. A digest without matching metadata is not interchangeable.

`memory-content-hash-v2` writes:

```text
TenantId
SanitizedContent
SourceRefs[]
```

Every SourceRef canonical projection writes:

```text
SourceKind                       // numeric enum
SourceId
RangeStart                       // JSON null when absent
RangeEnd                         // JSON null when absent
DescriptorRefs[]                 // Namespace + Id + Version, exact/sorted
CorrelationId                    // JSON null when absent
CausationId                      // JSON null when absent
UpstreamCanonicalContentHash     // JSON null when absent
```

When present, `UpstreamCanonicalContentHash` is the full structured
`CanonicalHash` projection (`Algorithm`, `AlgorithmVersion`, `ArtifactKind`,
nullable `DescriptorKind`, `Scope`, `Purpose`, `ContractVersion`,
`CanonicalShapeVersion`, `Value`) in that order, not a bare digest. Its
DescriptorKind is hash metadata and does not add Kind to `DescriptorRef`.

The root TenantId is validated against every SourceRef and is not duplicated in
each element. Each SourceRef is first projected to canonical JSON; projections
are Ordinal-distinct and sorted lexicographically by their canonical UTF-8
bytes before being written. Descriptor refs use the exact tuple from 5.4.
`SourceRefs=[]` is an explicit empty array. The v1 `"unknown"` source sentinel
is forbidden.

The artifact's own `CanonicalContentHash` is never an input to its projection.
An upstream hash is allowed only for a different already-existing source
artifact; a SourceRef that identifies the artifact being hashed or creates a
hash cycle is rejected. Sanitization accepts the complete trusted SourceRef
set, then computes v2 after final sanitized bytes and provenance normalization.
It never selects only the first SourceRef.

Compressed Blocks, Candidates, and Memories created or changed by this phase
all write v2. Promotion/Supersession recomputes v2 from the intended Memory
content/source set rather than copying an unverified Candidate hash. Expansion
grant integrity, Pack v2 inputs, and governance `CanonicalHash` facts use v2.
`memory-content-hash-v1` remains historical verification only and is never
written by the new mainline.

Readers inspect `CanonicalShapeVersion`. When an otherwise visible historical
record carries v1, the retriever/adapter recomputes an effective v2 value from
its stored sanitized content and complete trusted SourceRefs before Tool
projection, Grant creation, Pack v2, or audit facts. It does not silently use a
v1 digest in a v2 Pack and does not mutate the historical record as a read side
effect. Missing/invalid provenance fails closed; durable backfill is a separate
migration.

`memory-scope-hash-v2` writes, in the listed order:

```text
TenantId
IntentText or empty              // always empty for the v1 Tool contract
MemoryIds
Kinds
Tags
DescriptorRefs
VisibilityBoundary.VisibleDescriptorRefs
VisibilityBoundary.AllowUnscopedMemory
MinimumConfidence
MaxCount
CharacterBudget
IncludeStale
IncludeSuperseded
IncludeArchived
IncludeSourceRefs
```

Set-like arrays are Ordinal-distinct and sorted; enum values are numeric and
sorted. All descriptor refs are exact and use only the implemented canonical
tuple `Namespace`, `Id`, `Version`. DescriptorKind is neither stored nor
inferred. `MemoryIds` contains only framework-owned
internal ids after handle resolution. Legacy `VisibleDescriptorRefs` and
`VisibleDescriptorKinds` are not written. `IncludeArchived` and `MemoryIds`,
which v1 omitted, are mandatory v2 fields.

`memory-pack-hash-v2` binds, in order:

```text
TenantId
ScopeFingerprint                 // memory-scope-hash-v2
VisibleMemorySetHash
ReturnedMemoryContentHashes[]    // exact returned order
ReturnedCount
WasTruncated
IsAuthoritative = false
```

`VisibleMemorySetHash` remains `memory-set-hash-v1` because its canonical
payload is still the Ordinal-sorted complete eligible internal MemoryId set;
the hash is computed only after closed-world visibility and lifecycle filters,
but before output count/character truncation. If that payload changes, it must
receive v2 independently.

Existing v1 projectors remain read/verification compatibility for persisted
historical evidence only. New Tool executions write v2. Golden vectors freeze
both old v1 bytes and new v2 bytes, and no caller infers v2 semantics from an
unversioned digest.

### 5.11 Security-artifact preparation before Memory mutation

Handler failures are ordinary Completed `CapabilityFailure` outcomes on the
implemented Phase 8f path. Therefore no Memory mutation may occur before every
Handle/Grant required by the successful result has been issued and the exact
result graph has been prepared.

The mandatory order for Compress, Extract, Promote, Reject, and Supersede is:

```text
load and authorize trusted inputs
  -> assign framework Context/Block/Candidate/new Memory ids
  -> sanitize and validate the complete output/provenance graph
  -> compute memory-content-hash-v2 values
  -> compute expected-state hashes and immutable transition plan
  -> compute the canonical security ArtifactPlanHash
  -> prepare/issue every required Handle and Grant
  -> construct and shared-preflight every allowed result envelope
  -> add bounded branch-invariant audit facts and bind the trusted scope cap
  -> publish the complete allowed-outcome receipt/fact set once
  -> perform the Memory domain create/transition
  -> select and return one already-prepared typed result
```

All seven Memory Tools use the same output preflight/outcome-set comparison.
The five operations above must publish their complete allowed set before
mutation. Build prepares its result Handles/Grants, then preflights/publishes a
single selected branch before returning and revokes CreatedByBatch artifacts on
failure. Expand creates no new security artifact and preflights/publishes its
single selected branch before returning.

The pre-write validation step is one shared generated Agent Tool contract, not
a Handler-local Schema implementation:

```csharp
public sealed record AgentToolPreparedOutput<TOutput>
{
    public required TOutput Output { get; init; }
    public required JsonElement StructuredOutput { get; init; }
    public required IReadOnlyList<AgentToolAuditFact> ProjectedOutputFacts { get; init; }
    public required AgentToolOutputPreflightReceipt Receipt { get; init; }
}

public sealed record AgentToolOutputPreflightReceipt
{
    public required string ToolDescriptorId { get; init; }
    public required int ToolDescriptorVersion { get; init; }
    public required CanonicalHash OutputContractFingerprint { get; init; }
    public required CanonicalHash StructuredOutputHash { get; init; }
}

public sealed record AgentToolPreparedOutcomeReceipt
{
    public required string OutcomeCode { get; init; }
    public required AgentToolOutputPreflightReceipt Receipt { get; init; }
    public required IReadOnlyList<AgentToolAuditFact> InternalFacts { get; init; }
}

public interface IAgentToolOutputPreflightReceiptSink
{
    void PublishAllowedOutcomes(
        IReadOnlyList<AgentToolPreparedOutcomeReceipt> outcomes);
}

public interface IAgentToolOutputPreflight<TOutput>
{
    AgentToolPreparedOutput<TOutput> Prepare(TOutput output);
}
```

The generic contract/result, outcome receipt, sink, and Capability Context item
name live in `Agent.Tools.Abstractions`; generated
implementations and final Invoker consumption live in `Agent.Tools`. Memory
handlers depend only on the closed abstraction injected by DI.

For each concrete Tool output root, the generator registers exactly one
preflight implementation alongside its binding-root owner. It uses the same
frozen `JsonTypeInfo<TOutput>`, shared Options/converters, exact OutputSchema,
duplicate/unknown-property rules, enum wire maps, and Schema validator as final
Invoker output mapping. `OutputContractFingerprint` binds Tool id/version,
output CLR contract fingerprint, exact OutputSchema identity/hash, shared JSON
settings fingerprint, and projector version. `StructuredOutputHash` uses
`agent-tool-structured-output-receipt-v1` over the exact serialized UTF-8 bytes;
neither hash contains the text as an audit value or model field.

`Prepare` materializes the generated normalized output snapshot, serializes it,
validates the exact Schema, runs the same generated typed output-fact projector,
and returns cloned JSON, bounded candidate output facts, and a receipt. Memory
handlers receive only the exact closed generic service through DI; they cannot
read an Agent Tool registry or substitute a Schema/serializer.

Before a curation domain call, the Handler constructs and preflights the finite
set of Tool-domain envelopes that the already-authorized call may return:

```text
completed
conflict
unavailable   // only when authoritative reload may observe disappearance
```

Each entry uses the stable Tool `OperationStatus` wire value as `OutcomeCode`,
the exact receipt returned by that envelope's preflight, and an optional bounded
list of model-invisible facts true only for that branch. The generated Tool
contract freezes its allowlisted outcome codes and maximum branch count; the
global maximum is five, and these curation calls permit at most the three codes
above. Duplicate codes, duplicate receipt identities, an empty set, an
unsupported code, or facts outside the safe registered shape fail before
mutation. `InternalFacts` accepts only definitions owned by
`PreparedOutcomeInternal` and cannot duplicate a common/output fact name.
`OutcomeCode` is an internal selector, not a second audit status fact; it must
equal the OperationStatus projected from that entry's exact envelope.

The Handler verifies every branch's projected output facts plus common and
branch-internal facts against the trusted/global cap, then calls
`PublishAllowedOutcomes` exactly once. After the domain result it selects and
returns the corresponding already-prepared `Output`; it does not construct,
preflight, publish, or mutate an envelope after the domain call. Completed,
Conflict, and Unavailable envelopes are distinct exact DTO values. Conflict and
Unavailable carry no success Item, Handle, Grant, content hash, or completed-
only diagnostic/fact.

The Invoker re-runs the same generated preflight over the final output, seals
the outcome sink, and requires exactly one match over OutcomeCode, Tool id/
version, `OutputContractFingerprint`, and `StructuredOutputHash`. Zero matches,
multiple matches, an unprepared OperationStatus, or any type/Schema/byte/
deterministic-contract inconsistency follows the existing
`output_finalization_failure` Indeterminate path. Multiple statically equivalent
branches are also a startup contract error. This proves pre-write/final byte
equality even if a DTO contains an array or another mutable reference, without
turning a prepared Conflict/Unavailable result into Indeterminate.

The Invoker creates and owns the receipt buffer beside the audit-fact buffer,
keeps the only sealing reference, and places only the sink view in
`AgentCapabilityContextItemNames.OutputPreflightReceiptSink`. It accepts one
bounded outcome set for the exact selected Tool contract, copies receipts and
validated branch-internal facts, and rejects a second Publish. It stores no
StructuredOutput bytes, is not itself an audit fact/replay payload, uses no
AsyncLocal/static state, and is discarded on Capability failure or invocation
disposal. Only the internal facts attached to the one matched branch are
eligible for governance finalization.

No fallible adapter projection, Handle/Grant issuance, sanitation, provenance
validation, expectation construction, lifecycle interpretation, or DTO
construction occurs after the first Memory write. Phase 8f success-output serialization still runs outside
the Handler; if that unexpected finalization fails after a successful domain
operation, the existing Invoker correctly returns `InvocationIndeterminate`.

Security artifacts are active in their stores but undisclosed while prepared.
Because their random values are neither returned nor written to governance
audit, they are not externally usable before the Handler returns. A Handle may
temporarily point to the preassigned id of a not-yet-created resource. Resolver
behavior for that interval or after rollback is the normal non-probing
`Unavailable` result.

Agent Tool Invoker first propagates the exact already-computed binding through
one new trusted Capability Context item owned by
`CrestCreates.Agent.Tools.Abstractions`:

```csharp
public sealed record AgentToolInvocationBindingSnapshot
{
    public required AgentToolLogicalInvocationKey LogicalKey { get; init; }
    public required string InvocationFingerprint { get; init; }
}
```

`AgentCapabilityContextItemNames.InvocationBinding` carries that exact object.
The Invoker does not flatten the five-part LogicalKey and the Memory Handler
does not rebuild or recompute the Invocation Fingerprint. The execution-scope
factory requires the exact type, compares LogicalKey tenant/user/Agent/
Execution/Invocation fields to its trusted contexts, and fails closed when the
item is absent or inconsistent.

The binding snapshot/context-item name live in `Agent.Tools.Abstractions`.
OriginKind, Host key, and the neutral store-facing BatchKey live in
`Agent.Memory.Tools.Abstractions`; the coordinator and Agent-Tool-specific
binding-hash projector live in `Agent.Memory.Tools`. This preserves the
dependency directions in section 4.

Security-artifact batching has two non-interchangeable origins:

```csharp
public enum AgentMemorySecurityArtifactBatchOriginKind
{
    Unknown = 0,
    AgentToolInvocation = 1,
    TrustedHostOperation = 2
}

public sealed record AgentMemoryHostArtifactBatchKey
{
    public required string HostOperationId { get; init; }
    public required CanonicalHash OperationFingerprint { get; init; }
    public required string ArtifactPurpose { get; init; }
}

public sealed record AgentMemorySecurityArtifactBatchKey
{
    public required AgentMemorySecurityArtifactBatchOriginKind OriginKind { get; init; }
    public required CanonicalHash OriginBindingHash { get; init; }
    public required string ArtifactPurpose { get; init; }
    public required int PreparationOrdinal { get; init; }
    public required CanonicalHash ArtifactPlanHash { get; init; }
}

public enum PreparedArtifactDisposition
{
    Unknown = 0,
    CreatedByBatch = 1,
    ReusedExisting = 2
}

public enum AgentMemorySecurityArtifactBatchState
{
    Unknown = 0,
    Prepared = 1,
    Committed = 2,
    Aborted = 3
}

public interface IAgentMemorySecurityArtifactCoordinator
{
    ValueTask<PreparedAgentMemorySecurityArtifacts> PrepareForAgentToolAsync(
        AgentToolInvocationBindingSnapshot binding,
        string artifactPurpose,
        int preparationOrdinal,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        AgentMemorySecurityArtifactLimits limits,
        CancellationToken cancellationToken = default);

    ValueTask<PreparedAgentMemorySecurityArtifacts> PrepareForHostAsync(
        AgentMemoryHostArtifactBatchKey hostBatchKey,
        int preparationOrdinal,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        AgentMemorySecurityArtifactLimits limits,
        CancellationToken cancellationToken = default);

    ValueTask RevokeAsync(
        PreparedAgentMemorySecurityArtifacts prepared,
        CancellationToken cancellationToken = default);
}
```

The Agent Tool overload uses the Invoker snapshot without a second Invocation
Fingerprint algorithm. It creates the store-facing `OriginBindingHash` with
shape `agent-memory-artifact-agent-tool-binding-v1` over the five structured
LogicalKey fields plus the exact propagated InvocationFingerprint. The hash is
batch-domain separation, not a recomputation of Phase 8f fingerprint semantics.

The Host overload is used by non-Tool infrastructure such as History Handle
issuance and never requires a Phase 8f invocation. Its
`OperationFingerprint` is a structured canonical hash with shape
`memory-host-artifact-operation-v1`, binding the Host operation id, trusted
principal, exact source/resource binding, scope fingerprint, and purpose; the
Host does not pass an unversioned digest. The coordinator validates that hash
and derives the neutral store-facing key without introducing Tool concepts into
`Agent.Memory.Tools.Abstractions`.

Both overloads canonicalize the complete requested Handle/Grant graph to
`agent-memory-security-artifact-plan-v1` before touching either store. Each
Ordinal-sorted plan entry binds:

```text
Artifact kind and ResourceKind
internal ResourceId or exact SourceRef binding
TenantId / UserId / AgentId / ExecutionId
scope fingerprint
exact required DescriptorRef closure and unscoped flag
purpose
requested lifetime policy id/duration
```

The plan excludes random HandleId/GrantId values, issued time, absolute expiry,
Provider/model text, and other nondeterministic response data. SourceRef
projection uses the complete v2 provenance tuple from 5.9/5.10. Resource ids in
the plan are trusted framework-preassigned ids and remain internal.
Neither a model, Provider, Handler, nor Host supplies the digest field directly;
the coordinator computes it from typed validated requests with the generated
canonical projector and passes the resulting store-facing key.

Both overloads produce `AgentMemorySecurityArtifactBatchKey` with the exact
OriginKind, versioned OriginBindingHash, allowlisted purpose, bounded framework-
owned preparation ordinal starting at zero, and the full versioned
`ArtifactPlanHash`. Repeating the same origin/purpose/ordinal and identical plan
hash returns the original artifact set; those returned records, including
their internal resource ids, are authoritative for the prepared domain graph.
The Handler must use them rather than a newly regenerated id set.

Reusing the same origin/purpose/ordinal with a different `ArtifactPlanHash`
conflicts, including when the Tool input is unchanged but recall results,
Provider results, SourceRefs, descriptor closure, scope, resource ids, or
lifetime policy differ. Another InvocationFingerprint/Host operation
fingerprint at that coordinate also conflicts. A different allowlisted purpose
is a separate batch. Randomly regenerated artifact token ids do not define
identity and cannot make a changed plan look idempotent.

`PreparationOrdinal` may increase only after a create-only identity collision
that guarantees zero domain mutation. The coordinator revokes the prior batch,
the framework generates a new artifact id, and a bounded next ordinal creates a
new idempotent batch. It is not a general retry counter and is never advanced
for Provider, authorization, lifecycle, or uncertain failures.

If either Handle or Grant preparation fails, or any envelope construction,
preflight, outcome-set publication, or bounded audit-candidate step fails after
artifact preparation and before mutation, the
coordinator idempotently revokes anything already issued and the Handler
performs zero Memory writes. For Promote/Reject/Supersede, a typed confirmed-
no-write Conflict/ResourceUnavailable revokes only CreatedByBatch artifacts and
selects the matching predeclared envelope. Another exception covered by the
frozen `ConfirmedAtomic` Promotion Service contract from 5.5 revokes those
artifacts before propagating the generic outer failure.
Revocation failure is recorded for bounded cleanup/retry by the security-
artifact store, but no undisclosed token is placed in Tool output or governance
audit. Prepared artifacts naturally expire even if cleanup is unavailable.

An unknown commit outcome is never treated as an ordinary Capability failure
and artifacts are never revoked merely because an exception was observed. Such
a Promotion Service cannot pass this phase's curation capability gate; a future
typed domain Indeterminate path must fence Phase 8f and reconcile both domain
state and artifacts before enabling it. Confirmed committed success retains
the prepared artifacts.

Every prepared entry records `CreatedByBatch` or `ReusedExisting`. Abort/
`RevokeAsync(prepared)` revokes only `CreatedByBatch`; it must never revoke or
shorten a Handle/Grant previously returned by another batch. The same rule
applies if Grant reuse is added later. Batch records move from Prepared to
Aborted on rollback. Successful domain mutation does not add a synchronous
fallible `CommitAsync` after the write; Committed is an asynchronous/advisory
reconciliation state derived from the batch purpose, referenced resource
existence, and Agent Tool Gate or trusted Host operation result-publication
state. Correct resolution
depends on artifact binding/resource existence/expiry, not on that advisory
state, so cleanup cannot break a successful result.

Build has no Memory mutation and returns after preparation. Expand issues no
new resource artifact. Reject normally reuses the submitted CandidateHandle
and issues no new Handle/Grant. Promote and Supersede preassign `newMemoryId`,
prepare the expectation-bound plan, new MemoryHandle, and any result grants,
then call the plan-accepting domain contracts from 5.5. A preparation failure
cannot consume the Candidate.

## 6. Trusted execution and access scope

Tool arguments never accept:

- TenantId, UserId, actor kind, AgentId, roles, ExecutionId, InvocationId, or
  CallOrigin;
- Capability invocation source, correlation, causation, or idempotency key;
- visible descriptor refs/kinds, unscoped-memory permission, or scope hash;
- approval, risk, budget, audit, permission, or Tool policy decisions;
- operation timestamp;
- a complete `AgentContextSourceRef`;
- lifecycle status or `IsAuthoritative`.

Handlers derive trusted identity from the same scope used by Phase 8f:

```text
ITenantContext.CurrentTenantId
ICurrentUser.Id / IsAuthenticated / TenantId
IAgentExecutionContextAccessor.Current
TimeProvider
```

The Memory `AgentMemoryInvocationContext` is constructed by adapter
infrastructure:

```text
TenantId        <- ITenantContext
ActorId         <- ICurrentUser.Id
ActorKind       <- stable adapter constant "User"
AgentId         <- AgentExecutionContext.AgentId
SessionId       <- AgentExecutionContext.ExecutionId
CorrelationId   <- AgentExecutionContext.InvocationId
CausationId     <- AgentExecutionContext.CausationId
InvocationSource <- stable adapter constant for InvocationSource.Agent
TraceAttributes <- bounded safe Tool and Agent execution identity
```

No model-authored field overrides these values. Phase 8f AttemptId, approval,
budget, and lease identities stay in the governance audit/context-item layer;
they are not copied into the Memory domain operation request.

The Host supplies:

```csharp
public interface IAgentMemoryToolAccessScopeProvider
{
    ValueTask<AgentMemoryToolAccessScope> ResolveAsync(
        AgentMemoryToolPrincipal principal,
        CancellationToken cancellationToken = default);
}

public interface IAgentMemoryHistoryAccessAuthorizer
{
    ValueTask<bool> IsAuthorizedAsync(
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        CancellationToken cancellationToken = default);
}
```

`AgentMemoryToolPrincipal` is framework-created and includes only trusted
tenant/user/Agent/execution identity. The Host-provided
`AgentMemoryToolAccessScope` contains the closed-world Memory visibility facts
and server-side limits:

```text
VisibleDescriptorRefs
AllowUnscopedMemory
MaxVisibleDescriptorRefs
MaxRecallCount
MaxRecallCharacters
MaxExpansionCharacters
MaxCompressedBlockCount / MaxCompressedCharacters
MaxCandidateCount / MaxCandidateCharacters
MaxSourceRefsPerArtifact
MaxGrantsPerResource
MaxGrantsPerInvocation
MaxResourceHandlesPerInvocation
MaxActiveResourceHandlesPerResource
MaxAuditFacts
MaxTagsPerResource
ExpansionGrantLifetime
ResourceHandleLifetime
```

Adapter infrastructure validates and normalizes those facts, then computes a
canonical visibility-scope fingerprint from trusted tenant identity, sorted
exact descriptor refs, and `AllowUnscopedMemory`. The Host does not provide the
fingerprint. Recall's broader query `ScopeFingerprint` remains independently
computed by the existing Memory canonical projection and also binds filters and
data budgets.

There is no permissive default policy. Missing, malformed, cross-tenant, or
unknown scope results fail closed. Hosts may reuse Control Plane policy as an
input when composing a scope, but the Memory adapter does not reference Control
Plane contracts or implementations.

History authorization is separate because current conversation/task records do
not carry an owner or ACL. Capability permission plus tenant equality is not
enough to prove that the current principal may probe any conversation/task id.
Missing and unauthorized sources use the same external `Unavailable` result.
The limits above are enforced before descriptor-closure construction,
projection, grant/handle issuance, audit-fact projection, or persistence can
amplify attacker-controlled cardinality. Exceeding a limit fails the operation;
it never silently drops provenance or visibility refs.

## 7. Opaque source-expansion grants

`ExpandAgentMemorySource` accepts one opaque `GrantId`, never a model-authored
`AgentContextSourceRef`.

The adapter defines a Host-backed grant store and a framework service:

```csharp
public interface IAgentMemorySourceGrantStore
{
    ValueTask<AgentMemoryGrantIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        int maxActiveGrantsPerResource,
        CancellationToken ct = default);
    ValueTask<AgentMemorySourceGrant?> GetAsync(string grantId, CancellationToken ct = default);
    ValueTask RevokeAsync(string grantId, CancellationToken ct = default);
}
```

An issued grant stores a snapshot of the trusted source ref and binds it to:

```text
cryptographically random GrantId
TenantId / UserId / AgentId / ExecutionId
issuing Tool invocation id
scope fingerprint
effective required descriptor-ref closure
whether the source is truly unscoped
issued-at / expires-at
internal memory-content-hash-v2 integrity value when present
state = Active | Revoked
```

Grant rules:

1. IDs use a cryptographically strong random source; sequential ids and raw
   source ids are forbidden.
2. Grants are reusable only by the same tenant, user, Agent, and execution
   before expiration.
3. Resolution recomputes the current access scope. A changed scope must still
   authorize the complete required descriptor-ref closure.
4. A grant issued from a descriptor-bound parent carries the parent's complete
   closure even when the nested source ref itself has no descriptor refs.
5. Cross-tenant, cross-user, cross-Agent, cross-execution, expired, revoked,
   malformed, and unknown grants all return the same `Unavailable` shape.
6. The expander is called only after successful grant resolution.
7. Output never returns TenantId, raw SourceId, correlation ids, or the stored
   `AgentContextSourceRef`.
8. Expansion uses Memory-owned source kinds only. External source kinds remain
   `NotExpandable` without querying Control Plane, activation, draft, package,
   or HumanTask stores.
9. Only sanitized stored content is returned. The adapter applies the scope's
   expansion character limit and reports truncation without exposing omitted
   length or hidden-resource counts.
10. A development in-memory grant store is explicit and named as volatile.
    Production durability, revocation distribution, and cross-node lookup are
    Host adapter concerns.
11. Grant issuance is an adapter security-artifact write, not a Memory,
    descriptor, or business-state mutation; Build/Expand remain ReadOnly
    Capabilities under the Phase 8f Query rules. `TryIssueBatchAsync` is
    all-or-none for the requested batch, uses the section 5.11 batch key to make
    an identical logical batch idempotent,
    atomically enforces the active per-resource quota in that store, and must
    not trigger Memory lifecycle events.
12. A Phase 8f Completed replay returns the originally stored grant ids and
    never renews or reissues them. If they have expired, expansion returns
    `Unavailable`; the caller needs a new explicit Tool InvocationId to build a
    fresh pack. Silent grant renewal would violate completed replay semantics.
13. `RevokeAsync` is idempotent. It changes only grant state, never the
    underlying Memory/source artifact. Expiry is derived from `ExpiresAt` and
    does not require a background state write.
14. `MaxGrantsPerInvocation` is checked before the store call;
    `MaxGrantsPerResource` is enforced by `TryIssueBatchAsync`. A quota failure
    issues none of the batch and returns a safe limit result.

Build, compression, extraction, and curation outputs issue grants only for
source refs that were reached through an authorized operation and remain inside
the current closed-world scope.

## 8. Tool-safe contracts

Public Tool contracts are independent projections. Domain request/record types
are not exposed directly as model-authored input or provider output.

Nested DTOs use the bounded shared Schema support from section 5.1. Every
string, collection, and output count has a descriptor-level maximum. Enum-like
model inputs are closed semantic strings validated by the Capability Schema or
handler; unknown values fail without fallback.

### 8.1 Shared output DTOs

```text
AgentMemoryToolItemDto
  MemoryHandle
  Kind
  Content                         // sanitized
  CanonicalContentHash            // AgentMemoryToolCanonicalHashDto
  Confidence
  MemoryStatus
  IsAuthoritative = false
  Tags
  SourceGrants[]

AgentMemoryToolCandidateDto
  CandidateHandle
  Kind
  Content                         // sanitized
  CanonicalContentHash            // AgentMemoryToolCanonicalHashDto
  Confidence
  CandidateStatus = Candidate
  IsAuthoritative = false
  SourceGrants[]

AgentMemoryToolBlockDto
  Content                         // sanitized compressed content
  CanonicalContentHash            // AgentMemoryToolCanonicalHashDto
  SourceGrants[]

AgentMemoryToolCanonicalHashDto
  Value
  AlgorithmVersion
  ContractVersion
  CanonicalShapeVersion

AgentMemorySourceGrantDto
  GrantId
  SourceKind
  ExpiresAt

AgentMemoryToolDiagnosticDto
  Code
  Severity                        // AgentMemoryToolDiagnosticSeverity
```

`AgentMemoryToolCanonicalHashDto` is the only model-visible hash projection.
For this phase `Value` is exactly 64 lowercase hexadecimal SHA-256 characters,
`AlgorithmVersion="sha256-canonical-json-v1"`,
`ContractVersion="memory-hash-v2"`, and
`CanonicalShapeVersion="memory-content-hash-v2"`. Mapping from the internal
`CanonicalHash` is an exhaustive generated projection that first verifies all
four exposed values and the expected internal purpose. It deliberately omits
internal `Algorithm`, `ArtifactKind`, `DescriptorKind`, `Scope`, and `Purpose`;
the domain type is never serialized directly into a Tool envelope.

Every Tool has a concrete, non-generic, source-generated result envelope with
one required field of the shared closed enum:

```text
AgentMemoryToolOperationStatus
  Unknown = 0
  Completed = 1
  Unavailable = 2
  Conflict = 3
  Redacted = 4
  NotExpandable = 5

AgentMemoryToolDiagnosticSeverity
  Unknown = 0
  Info = 1
  Warning = 2
  Error = 3

AgentMemoryToolConfidence
  Unknown = 0            // no wire value
  Unspecified = 1        // "unknown"
  Low = 2                // "low"
  Medium = 3             // "medium"
  High = 4               // "high"
```

All Tool-facing enums are dedicated Tool contract enums with `Unknown=0` and
explicit generated domain mapping. JSON wire values are stable lowercase
semantic strings; integer tokens, CLR `ToString()`, case-insensitive aliases,
and direct numeric/domain enum casts are forbidden.

| Tool enum | Stable wire values |
| --- | --- |
| `AgentMemoryToolOperationStatus` | `completed`, `unavailable`, `conflict`, `redacted`, `not-expandable` |
| `AgentMemoryToolMemoryStatus` | `active`, `superseded`, `archived` |
| `AgentMemoryToolCandidateStatus` | `candidate`, `active`, `rejected` |
| `AgentMemoryToolKind` | `preference`, `project-fact`, `decision`, `constraint`, `workflow-hint`, `risk` |
| `AgentMemoryToolConfidence` | `unspecified -> unknown`, `low`, `medium`, `high` |
| `AgentMemoryToolSourceKind` | `conversation-turn`, `task-record`, `task-event`, `compressed-context-block`, `memory-candidate`, `memory-item`, `metadata-context-pack`, `review-report`, `fix-proposal`, `package-preview`, `activation-request` |
| `AgentMemoryToolDiagnosticSeverity` | `info`, `warning`, `error` |

`Unknown=0` has no wire value for any Tool enum. The legal confidence string
`"unknown"` maps only to `Unspecified=1`, so a default-initialized enum never
becomes valid business data. Generated converters accept strings only and use
exhaustive switches in both directions. Output validation rejects unsupported
domain values before serialization.

The explicit domain projection is
`AgentMemoryConfidence.Unknown -> AgentMemoryToolConfidence.Unspecified`; it is
not a numeric cast.

Diagnostic severity maps explicitly from the domain value object:

```text
Info    -> info
Warning -> warning
Review  -> warning
Error   -> error
Blocker -> error
```

An empty or unsupported domain severity fails output validation. The Tool
contract never invokes `SeverityLevelJsonConverter`, never emits `Review` or
`Blocker`, and never accepts a domain semantic string as a Tool enum value.

Schema v3 currently has no enum allowed-values field. These enum fields are
projected as `FieldType="string"`; the generated exact input binder and exact
output validator enforce the closed sets above. Descriptions are not an
authority mechanism, and #53 does not expand Schema v3 solely to add enum
keywords.

`OperationStatus` always describes the Tool-domain operation. `MemoryStatus`
and `CandidateStatus` describe only artifact lifecycle. No envelope aliases one
as the other, and no public generic `AgentMemoryToolResult<T>` is introduced.
Each exact envelope independently declares its payload and `Diagnostics[]`.

| Condition | `OperationStatus` |
| --- | --- |
| Normal domain completion, including an empty authorized recall | `Completed` |
| Missing, invisible, unauthorized, wrong-principal, expired/revoked handle or grant | `Unavailable` |
| Candidate/Memory lifecycle condition lost or already consumed | `Conflict` |
| Sanitizer rejects content required for the result | `Redacted` |
| Expand receives a valid grant for a source kind the Memory runtime does not expand | `NotExpandable` |

Capability permission denial, invalid Tool/Schema input, unknown infrastructure
or Provider failure, and `InvocationIndeterminate` remain Phase 8f outer
Outcomes and never become this enum. Except for a fully populated `Completed`
payload, envelope payload objects/handles are null and collections are empty;
failure envelopes never expose a partially prepared result.

`MemoryHandle` and `CandidateHandle` are resource handles from section 5.8, not
domain ids. Context handles appear only on operation envelopes; BlockId is not
projected.
Prompt/output hashes, scope/set/pack hashes, and domain ids remain internal
facts. A per-item `CanonicalContentHash` may be returned as an integrity and
association label; it is not a confidentiality control and must not be treated
as proof that the content is authoritative.

The model-visible Grant DTO deliberately omits the source content hash. The
internal Grant record retains the v2 integrity hash, but the model receives it
only from a successful, non-truncated Expand result alongside the complete
sanitized expanded content. Truncated results omit it as specified in 8.3.
This prevents an unexpanded low-entropy source from becoming a stable
fingerprint and avoids mislabeling a slice with an artifact hash.

Diagnostics expose fixed safe codes and severity only. Domain diagnostic
messages, source refs, tenant ids, resource ids, provider failures, and raw
exception details are not returned automatically.

### 8.2 BuildAgentMemoryPack

Input:

```text
MemoryHandles[]
Kinds[]
Tags[]
MaximumCount
CharacterBudget
MinimumConfidence
```

The model cannot request stale, superseded, archived, invisible, or unscoped
content. Those decisions come from the trusted scope and fixed first-closure
policy. Requested limits are required positive values and are clamped to the
server limits before constructing `AgentMemoryQuery`.

Output:

```text
BuildAgentMemoryPackResult
OperationStatus
Items[]
ReturnedCount
WasTruncated
IsAuthoritative = false
Diagnostics[]
```

Build returns `Completed` even when the authorized result set is empty. Its
arrays/count/truncation fields are always present and never encode hidden-set
cardinality.

The handler builds the trusted `AgentMemoryVisibilityBoundary`, calls only
`IAgentMemoryRetriever.RecallAsync`, validates tenant and authority invariants,
and projects safe items/grants. It does not call stores to recreate retrieval,
ranking, hashing, or budget logic.

`ScopeFingerprint`, `VisibleMemorySetHash`, and `CanonicalPackHash` remain in
the invocation context/gate and validated finalization facts for cache,
correlation, and replay. They are never returned to the model. An empty
`MemoryHandles` list means the trusted eligible set; every supplied handle must
resolve successfully before recall so missing/invisible selection cannot be
used as an oracle.

`IntentText` is not a v1 Tool input. The current default retriever does not use
it for matching or ordering, so exposing it would imply relevance semantics
that do not exist. `AgentMemoryQuery.IntentText` may remain an optional hint for
non-Tool callers and is still included by `memory-scope-hash-v2` when present.
Future Tool exposure requires stable, versioned retrieval semantics.

### 8.3 ExpandAgentMemorySource

Input:

```text
GrantId
MaximumCharacters
```

Output:

```text
ExpandAgentMemorySourceResult
OperationStatus
SanitizedContent?
CanonicalContentHash?              // AgentMemoryToolCanonicalHashDto
WasTruncated
Diagnostics[]
```

`MaximumCharacters` is clamped to the trusted scope. All invalid or
unauthorized grant cases are `Unavailable`; the result never distinguishes
missing from forbidden. Hash semantics are exact:

```text
OperationStatus=Completed, WasTruncated=false
  -> CanonicalContentHash is required
  -> it is the Tool-safe projection of the complete sanitized source artifact's
     memory-content-hash-v2

OperationStatus=Completed, WasTruncated=true
  -> CanonicalContentHash is null
```

A truncated payload never reuses the complete artifact hash and never computes
a second `memory-content-hash-v2` from the returned prefix. The internal Grant
record retains the complete source integrity hash for authorization/validation,
but it is not projected beside truncated text. If slice integrity is needed
later, it requires a separate `memory-expanded-slice-hash-v1` binding the exact
returned sanitized bytes, upstream source integrity hash, returned character
range, and `WasTruncated`; that shape is deferred.

### 8.4 CompressAgentHistory

Input:

```text
HistorySourceHandle
```

Flow:

```text
trusted tenant/principal
  -> resolve opaque HistorySourceHandle
  -> history access authorization
  -> tenant-scoped conversation/task load
  -> IAgentContextCompressor registered by Host
  -> assign trusted context/block ids
  -> validate tenant, blocks, provenance closure, limits
  -> prepare ContextHandle and every SourceGrant
  -> prepare exact Completed envelope
  -> IAgentCompressedContextStore.CreateCompressedContextAsync (create-only)
  -> publish prepared result
```

Output:

```text
CompressAgentHistoryResult
OperationStatus
ContextHandle?
SourceKind?
Blocks[]
BlockCount
Diagnostics[]
```

For `Completed`, ContextHandle and SourceKind are required and Blocks/BlockCount
describe the exact prepared/persisted Context. Other statuses return no handle
or blocks.

The model never supplies raw history. A missing or unauthorized source returns
the same `Unavailable` shape and calls the compressor zero times.

`HistorySourceHandle` is a resource handle with kind
`ConversationHistory` or `TaskHistory`. A trusted Host integration issues it
through `IAgentMemoryHistoryResourceHandleIssuer` only after the existing
history authorizer approves the principal/source pair. Issuance is not an
Agent Tool and the raw history SourceId is never placed in model arguments or
pre-dispatch facts. The Host supplies the independent versioned
`AgentMemoryHostArtifactBatchKey`; no Phase 8f LogicalKey/Fingerprint is
required. Resolution re-runs current authorization before loading.

### 8.5 ExtractMemoryCandidates

Input:

```text
ContextHandle
```

Flow:

```text
trusted tenant
  -> resolve opaque ContextHandle
  -> tenant-scoped compressed-context load
  -> closed-world access check over every block/source ref
  -> IAgentMemoryExtractor registered by Host
  -> assign trusted candidate ids
  -> validate all candidates and provenance closure before the first save
  -> require TenantId match and Status = Candidate
  -> enforce count/content/source-ref limits
  -> prepare CandidateHandles, SourceGrants, and exact Completed envelope
  -> IAgentMemoryStore.CreateCandidateAsync for each validated candidate
  -> publish prepared result
```

Output:

```text
ExtractMemoryCandidatesResult
OperationStatus
ContextHandle?
Candidates[]
CandidateCount
Diagnostics[]
```

For `Completed`, ContextHandle is required and Candidates/CandidateCount
describe the exact prepared Candidate set. Other statuses return no Context or
Candidate handles.

Extraction never promotes. Unknown Memory kind/confidence handling remains the
registered extractor's responsibility; the adapter validates only supported
final domain enum values and Candidate lifecycle status.

The current processing store has no atomic multi-Candidate batch operation.
Validation completes before the first save, but a store failure during multiple
saves can leave partial extraction results. This phase does not claim otherwise
and the curation-only `ConfirmedAtomic` gate does not strengthen it. A future
durable provider should add atomic batch persistence or reconciliation behind
the Memory store contract.

### 8.6 PromoteMemoryCandidate

Input:

```text
CandidateHandle
Explanation
```

The handler resolves the candidate handle under the trusted principal, loads it
by trusted tenant, applies the complete descriptor/source visibility closure,
preassigns `newMemoryId`, prepares the new MemoryHandle/result grants and exact
Completed envelope plus payload-empty Conflict and, when authoritative reload
permits disappearance, Unavailable envelopes. It preflights and publishes the
whole allowed-outcome set with the Candidate expectation, expected Memory
content/state hashes, and
`AgentMemoryPromotionPlan` with trusted actor/time/source refs and stable Reason
`AgentToolPromotion`, then calls only
`IAgentMemoryPromotionService.PromoteAsync`. Preparation failure performs no
domain call; a typed expectation conflict/resource disappearance revokes only
CreatedByBatch artifacts and selects the corresponding already-prepared branch.

Output:

```text
PromoteMemoryCandidateResult
OperationStatus
Item?
Diagnostics[]
```

For `Completed`, Item is required with `MemoryStatus=Active` and
`IsAuthoritative=false`. Any other successful domain shape is an internal
contract failure owned and prevented by the Promotion Service before mutation.

### 8.7 RejectMemoryCandidate

Input:

```text
CandidateHandle
Explanation
```

The handler follows the same trusted-handle/load and visibility path, computes
the Candidate expectation, constructs and exact-preflights the Completed,
Conflict, and conditionally allowed Unavailable Reject envelopes, publishes the
whole outcome set, adds only branch-invariant common facts, uses stable Reason
`AgentToolRejection`, and only then calls
`IAgentMemoryPromotionService.RejectAsync` with that expectation. Reject creates
no new Handle/Grant, but no lifecycle transition occurs before this result
preparation sequence completes. The exact order is:

```text
load/authorize Candidate
  -> compute Candidate expectation
  -> construct + exact-preflight all allowed Reject envelopes
  -> add branch-invariant facts + publish allowed outcome set once
  -> conditional Reject transition
  -> select and return one prepared envelope
```

Output:

```text
RejectMemoryCandidateResult
OperationStatus
CandidateHandle?
CandidateStatus? = Rejected when Completed
IsAuthoritative = false
Diagnostics[]
```

It does not return rejected content.

### 8.8 SupersedeMemoryItem

Input:

```text
MemoryHandle
ReplacementCandidateHandle
Explanation
```

The handler resolves both handles under the trusted tenant, requires the
complete visibility closure of both resources, preassigns `newMemoryId`,
prepares the new MemoryHandle/result grants and exact Completed envelope plus
payload-empty Conflict and conditionally allowed Unavailable envelopes,
preflights/publishes the complete outcome set, constructs Candidate/target-
Memory expectations and an
`AgentMemorySupersessionPlan` with stable Reason `AgentToolSupersession`, and
calls only `IAgentMemoryPromotionService.SupersedeAsync`. State-hash, Active,
Candidate, and consumption checks are repeated and owned by the domain service
as specified in 5.5. Preparation failure makes no domain call; an expectation
conflict/resource disappearance revokes only CreatedByBatch artifacts and
selects the corresponding already-prepared branch.

Output:

```text
SupersedeMemoryItemResult
OperationStatus
Item?
SupersededMemoryHandle?
ActiveMemoryHandle?
Diagnostics[]
```

For `Completed`, Item and both relationship handles are required, Item has
`MemoryStatus=Active`, and all returned context is non-authoritative. Raw
old/new ids are not projected.

No adapter code calls Memory create/transition primitives directly; all
curation state changes remain behind `IAgentMemoryPromotionService` and its
expected-state store operation.

## 9. Tool, Capability, role, permission, and governance matrix

Stable names use a dotted lowercase semantic namespace. Display names in the
issue remain documentation aliases.

| Display tool | Tool / Capability id | Capability kind | Side effect | Selection | Risk floor | Approval | Roles | Permission |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BuildAgentMemoryPack | `agent.memory.build-pack` | Query | ReadOnly | AutomaticAllowed | Low | None | reader, processor, curator | `Crest.AgentMemory.Recall` |
| ExpandAgentMemorySource | `agent.memory.expand-source` | Query | ReadOnly | ExplicitOnly | Medium | PolicyDriven | reader, processor, curator | `Crest.AgentMemory.Expand` |
| CompressAgentHistory | `agent.memory.compress-history` | Command | InternalWrite | AutomaticAllowed | Medium | PolicyDriven | processor, curator | `Crest.AgentMemory.Compress` |
| ExtractMemoryCandidates | `agent.memory.extract-candidates` | Command | InternalWrite | AutomaticAllowed | Medium | PolicyDriven | processor, curator | `Crest.AgentMemory.Extract` |
| PromoteMemoryCandidate | `agent.memory.promote-candidate` | Command | InternalWrite | ExplicitOnly | Medium | PolicyDriven | curator | `Crest.AgentMemory.Promote` |
| RejectMemoryCandidate | `agent.memory.reject-candidate` | Command | InternalWrite | ExplicitOnly | Medium | PolicyDriven | curator | `Crest.AgentMemory.Reject` |
| SupersedeMemoryItem | `agent.memory.supersede-item` | Command | InternalWrite | ExplicitOnly | High | Required | curator | `Crest.AgentMemory.Supersede` |

Role constants are:

```text
memory-reader
memory-processor
memory-curator
```

The short labels in the table map to those constants. Roles control discovery
and selection only. Capability permissions are the real authenticated-user
authorization boundary and remain enforced by Capability Pipeline.

All seven Tools use:

```text
AuditMode = Required
BudgetCategory = agent-memory-recall | agent-memory-expand |
                 agent-memory-process | agent-memory-curation
CostUnits > 0
MaxCallsPerExecution > 0
```

Exact cost units and maximum call counts are stable descriptor contract fields
chosen in implementation and frozen by canonical-hash tests. Tenant policy may
raise approval/risk/call restrictions through Phase 8f Host adapters but may
never weaken these floors.

`SupersedeMemoryItem` deliberately uses `RiskFloor=High` and
`ApprovalMode=Required`. Phase 8f rejects explicit High/Critical risk with
non-Required approval through `ATP016`; the earlier Issue comment's
“higher-risk PolicyDriven” combination is not valid against the implemented
mainline.

An LLM-backed compressor/extractor may perform provider egress. The static Tool
classification remains the minimum Memory mutation classification above. A
Host that enables external provider egress must strengthen approval and budget
policy for the process category. This phase does not hide provider selection in
Tool arguments.

## 10. Descriptor and generated-artifact design

The adapter publishes stable v1 descriptors for:

- the nested input/output Schemas;
- seven distinct concrete result-envelope Schemas with required
  `OperationStatus` and separately named lifecycle fields;
- seven native `CapabilityDescriptor` values;
- seven generated `AgentCapabilityToolDescriptor` values.

Every Capability:

- is `DescriptorState.Active`, version 1, and `ProjectionKind.Native`;
- uses exact input/output Schema refs;
- declares the permission in section 9;
- declares Query/Command kind and base risk consistent with the Tool floor;
- has a stable id/name and canonical ContractHash.

`[AgentToolSpec]` uses exact `CapabilityVersion=1`, exact Tool-safe input/output
types, explicit ToolName/Title/Description, explicit side effect/risk/approval,
required budget, required audit, and the exact allowed roles.

Generated artifacts remain the Phase 8f mainline:

```text
descriptor provider
exact input binder
exact output serializer
root/nested JsonTypeInfo registration
safe audit-fact projector registration
exact output preflight registration
DI-safe Capability handler invoker
generated Capability handler registration provider
```

No generated or handwritten Memory Tool code may contain direct Dispatcher
calls, Handler resolution, Control Plane symbols, activation symbols,
reflection JSON overloads, or runtime scanning.

## 11. Handler orchestration and invariants

Handlers are intentionally thin. Shared adapter services own repeated trusted
context, scope, validation, projection, and grant logic.

```text
Handler
  -> AgentMemoryToolExecutionScopeFactory
  -> operation-specific existing Memory interface(s)
  -> AgentMemoryToolResultProjector
  -> AgentMemorySecurityArtifactCoordinator
  -> IAgentToolOutputPreflight<ExactResult>
  -> invocation-owned output-preflight receipt sink
  -> invocation-owned trusted audit-fact sink
  -> prepared exact result / domain mutation ordering
```

The execution-scope factory validates once:

- authenticated current user;
- tenant/user tenant consistency;
- trusted Agent execution context;
- exact `AgentToolInvocationBindingSnapshot` whose LogicalKey matches trusted
  identity/execution; the Handler never recomputes its fingerprint;
- exact invocation-owned audit-fact and output-preflight receipt sink views;
- Host access scope identity and server limits;
- no unknown role/source/status/enum values;
- cancellation before store or provider calls.

Every domain object loaded or returned across the adapter boundary is validated:

- `TenantId` equals the trusted tenant;
- every nested source ref tenant equals the trusted tenant;
- every descriptor/source visibility closure is authorized;
- every descriptor ref is exact with `Version > 0`;
- every Tool resource selector resolves from a principal/execution/scope-bound
  opaque handle; a raw domain id is never accepted or projected;
- framework-owned artifact ids are assigned before persistence and are unique
  within the batch and authoritative store;
- compression/extraction provenance is a structural subset of the trusted
  loaded input as defined in section 5.9;
- source expansion Range is valid against original record boundaries and never
  widens to adjacent records;
- collection and content limits are respected;
- candidate lifecycle observations in the Handler are access/projection checks
  only; Promotion Service and its conditional store primitive own the winning
  Promote/Reject/Supersede transition;
- returned memories are Active where required;
- prepared envelope/domain plans share the same Candidate/Memory state hashes
  and expected v2 content hash; a committed graph cannot differ;
- each security-artifact batch hash binds the exact prepared resource/grant
  plan and returned original artifact records supply authoritative ids;
- mutating envelopes pass the shared generated exact-output preflight before
  the first Memory write;
- the complete bounded allowed-outcome receipt set is published once before
  mutation, and the Invoker requires exactly one final match;
- curation executes only against the startup-frozen `ConfirmedAtomic`
  capability on the exact selected Promotion Service instance;
- every projected `IsAuthoritative` is false;
- common internal audit candidates are branch-invariant; branch-specific
  internal facts live only on their prepared outcome, and Handler code cannot
  seal, select for audit, or persist either set;
- source refs are replaced with opaque grants;
- every content hash uses `AgentMemoryToolCanonicalHashDto` and every diagnostic
  severity uses the explicit Tool mapping from 8.1;
- every concrete envelope has required `OperationStatus`, and artifact
  lifecycle fields use separately named `MemoryStatus`/`CandidateStatus`;
- raw domain diagnostic messages are not projected.

Reason strings are not model authority. Each operation supplies a stable
framework Reason code; model-authored `Explanation` is sanitized, length
bounded, and passed only as rationale. It is excluded from safe audit facts.

Execution is three-phase: resolve/load trusted inputs; validate/normalize/hash
the complete graph; then prepare security artifacts, branch-invariant facts,
and the exact preflighted allowed-outcome set before any Memory create/
transition. A confirmed no-write branch revokes only artifacts created by that
batch; a confirmed commit retains them. Unknown commit state is not an ordinary
failure path. After the domain result the Handler only selects and returns one
already-prepared typed result. This reduces partial writes but does not claim a
transaction across sequential Candidate creates or undeclared durable stores.

## 12. Independent budgets

Two different budgets apply and neither replaces the other:

```text
Phase 8f Agent Tool budget
  -> call/cost reservation before Dispatcher

Agent Memory data budget
  -> returned count/characters/source expansion inside the handler
```

Required behavior:

- Tool budget denial calls Dispatcher and every Memory service zero times;
- recall limits are clamped before `RecallAsync` and reflected in Memory pack
  diagnostics/hashes;
- expansion limits apply after authorized source resolution;
- compressor/extractor results exceeding Host limits fail before persistence;
- a completed Phase 8f replay does not recall, expand, compress, extract, or
  mutate Memory again;
- Tool budget settlement follows Phase 8f and does not infer Memory transaction
  atomicity.

## 13. Replay, concurrency, and uncertainty

Phase 8f owns logical invocation fencing. The adapter does not add a second
idempotency journal.

For the same logical invocation and fingerprint:

- Completed replay returns the stored safe Tool outcome;
- no access policy, grant issue, Memory read, compression, extraction,
  persistence, promotion, rejection, or supersession repeats;
- no new approval claim or Tool budget reservation occurs.

This includes expansion grants: replay returns the original Tool result. It
does not extend grant expiry or create replacement authorization artifacts.

For a different logical invocation:

- repeated promotion/rejection/supersession is a new authorized request;
- current lifecycle state decides success or safe conflict;
- this is not a claim of global operation deduplication.

Post-Dispatch uncertainty follows Phase 8f. A timeout, cancellation, lost
response, lease failure, budget-settlement uncertainty, or governance-audit
uncertainty becomes `InvocationIndeterminate` and must not be automatically
retried.

The current Memory stores and promotion service do not provide distributed
transactions. Specifically:

- extraction may persist a subset if a store fails during sequential Candidate
  creates; its entire prepared security-artifact batch is then revoked;
- the first-party InMemory store makes each Promote/Reject/Supersede conditional
  Candidate transition `ConfirmedAtomic` under its store-owned lock, but this
  is not a distributed transaction claim;
- a Store/Promotion Service pair whose post-commit acknowledgement can be lost
  cannot enable the three curation Tools until a typed domain/Capability
  Indeterminate contract exists; observing an exception alone never authorizes
  artifact revocation;
  and
- no grant/handle issuance occurs after a Memory write. All required artifacts
  and the exact output preflight are completed first; only confirmed no-write
  failures revoke CreatedByBatch artifacts as specified in 5.11.

The first closure tests deterministic in-memory behavior and states these
limits. A reported successful curation transition must be the sole winner for
its Candidate; successful supersession also satisfies candidate-consumed and
bidirectional-link invariants. A durable Store must own atomic compare-and-swap/
transaction or reconciliation, and its selected Promotion Service may declare
`ConfirmedAtomic` only when the whole Service call also eliminates
acknowledgement ambiguity; Phase 8f fencing cannot manufacture Memory-store
atomicity.

## 14. Safe results and anti-probing behavior

Every concrete result envelope uses the stable `OperationStatus` semantics from
8.1:

```text
Completed
Unavailable
Conflict
Redacted
NotExpandable
```

Mappings:

- missing, invisible, cross-tenant, unauthorized, expired-grant, revoked-grant,
  and principal-mismatch resources all map to `Unavailable`;
- invalid candidate/item lifecycle maps to `Conflict` without current hidden
  state details;
- sanitizer rejection of content required for the result maps to `Redacted`
  with no partial payload and a safe diagnostic;
- external Memory source kinds map to `NotExpandable`;
- malformed DTOs remain Phase 8f `InvalidRequest`/Capability validation;
- missing Capability permission remains generic `CapabilityFailure` without
  exposing the permission policy;
- unknown provider/store exceptions outside curation remain generic failures;
  curation exceptions are ordinary failures only under the frozen
  Promotion Service's `ConfirmedAtomic` zero-write guarantee, while a selected
  Service with unknown commit outcome cannot publish the curation Tools;
- Indeterminate is owned and returned by Phase 8f, not converted into
  `OperationStatus` or a Memory lifecycle status.

Safe outputs never expose:

- TenantId, user id, Agent roles, or internal correlation values;
- persistent ContextId, BlockId, CandidateId, MemoryId, or history record ids;
- raw SourceId or complete `AgentContextSourceRef`;
- invisible descriptor refs, kinds, ids, counts, or hashes;
- approval evidence, approver details, budget reservation ids, or lease tokens;
- exception messages, stack traces, CLR types, provider response text, SQL, or
  store details;
- raw unsanitized content;
- `ScopeFingerprint`, `VisibleMemorySetHash`, `CanonicalPackHash`, or
  prompt/output evidence hashes;
- a true or nullable authoritative flag.

## 15. Audit model

Three existing concerns remain separate:

| Layer | Responsibility |
| --- | --- |
| Phase 8f governance audit | selection, role/origin, fingerprint, lease, approval, Tool budget, dispatch, terminal outcome |
| Capability audit | Capability id/version, tenant/user, InvocationSource.Agent, success/failure, duration |
| Memory domain state | candidate/item lifecycle and source/evidence fields through existing Memory contracts |

This phase does not introduce a second governance auditor inside handlers.
Memory-specific safe facts are contributed to the Phase 8f governance
finalization through the exact typed output projector plus invocation-owned
trusted fact sidecar from section 5.6.

Pre-dispatch facts are limited to:

```text
requested counts and limits
SourceKind
HandleKind / HandleCount / HasHandle
HasExplanation
```

After authorized load, finalization fact ownership is frozen:

| Owner | Allowed facts |
| --- | --- |
| Actual output projector | OperationStatus, returned/persisted count, MemoryStatus, CandidateStatus, truncation, model-visible returned canonical content hashes |
| Branch-invariant sidecar | visibility scope hash, authorized resource CorrelationHmac, trusted SourceKind, other facts true for every prepared branch |
| Matched prepared outcome | rare model-invisible facts true only for that exact Completed/Conflict/Unavailable branch, such as internal pack/set hash |

The common sidecar never accepts persisted count, final lifecycle status,
OperationStatus, truncation, or returned canonical content hash. A
branch-specific internal fact must be published before mutation on its prepared
outcome and is ignored unless that receipt is the unique final match. Capability
failure discards all output/internal facts. After exact output validation the
Invoker selects one outcome, seals the common buffer, applies
`min(global cap, trusted Scope.MaxAuditFacts)`, combines only the three sources
above, validates that status/facts agree, and then computes/finalizes governance
v2. No internal fact is recoverable from the model-visible DTO or Capability
error text.

The finalization record separately carries the
`agent-tool-governance-outcome-v2` hash computed over the summary and already-
validated fact list; the hash is not itself an input fact and therefore cannot
be recursive.

Audit storage must prove through tests that no fact, decision, pre-dispatch
record, finalization record, or development governance-auditor snapshot
contains recalled Memory text, expanded source text, compressed block text,
candidate text, explanations, raw input JSON, or structured Tool output.
Unavailable resources never add the submitted Handle/Grant token, a resolved
id, or a hash/HMAC of the submitted token. The governance query surface also
cannot retrieve the Invocation Gate's completed replay payload.

## 16. Registration and startup

The ordinary Host path is:

```csharp
services.AddDescriptorStableHash();
services.AddAgentMemoryRuntime();                 // or Host provider
services.AddAgentMemoryTools();
services.AddCapabilityRuntime();                  // applies selected handler providers only
services.AddCrestAgentTools();                    // composes JSON contributors

services.AddSingleton<IAgentMemoryToolAccessScopeProvider, HostScopeProvider>();
services.AddSingleton<IAgentMemoryHistoryAccessAuthorizer, HostHistoryAuthorizer>();
services.AddSingleton<IAgentMemorySourceGrantStore, HostGrantStore>();
services.AddSingleton<IAgentMemoryResourceHandleStore, HostResourceHandleStore>();

// Existing Phase 8f Host adapters:
services.AddSingleton<IAgentToolInvocationGate, HostInvocationGate>();
services.AddSingleton<IAgentToolApprovalGate, HostApprovalGate>();
services.AddSingleton<IAgentToolBudgetGate, HostBudgetGate>();
services.AddSingleton<IAgentToolGovernanceAuditor, HostGovernanceAuditor>();
```

`AddCrestAgentTools()` owns and registers the shared runtime infrastructure:

- invocation audit-fact buffer factory and Invoker create/propagate/seal flow;
- prepared-outcome receipt buffer factory and Invoker create/propagate/unique-
  match flow;
- shared generated-preflight runtime over contributed exact binding contracts;
- the one global governance-outcome-v2 hashing/finalization path.

These implementations live in `Agent.Tools`; a Tool module cannot install,
replace, or statically capture them.

`AddAgentMemoryTools()` registers:

- the Memory ModuleId/ProviderId selection marker and validates that its module-
  initializer definition is present; the final `AddCapabilityRuntime()`
  application registers exact Scoped handlers and Singleton invokers for this
  selected provider only;
- Memory Tool source-generated JSON context contributor;
- permission definition provider;
- access/grant/resource-handle preparation coordinator, the trusted history-
  handle issuer, result/audit projectors, and generated exact-output preflight
  definitions;
- Memory Handler consumption of the abstract fact and prepared-outcome sinks;
- eager Memory Tool prerequisite validator.

The Memory module only opts into and validates the shared Fact Buffer/Prepared-
Outcome/governance-v2 features. It does not own their factories, Invoker
propagation, or concrete implementation registration.

It does not silently call `AddAgentMemoryRuntime()`, `AddAgentMemoryLlm()`, or
`AddCrestAgentTools()`/`AddCapabilityRuntime()`, and it does not install
permissive access, grant, handle, approval, budget, invocation, or audit
services.

Startup fails before discovery when any Active Memory Tool lacks:

- a Capability or exact Schema;
- exact generated binding or nested `JsonTypeInfo`;
- unique binding-root ownership and equivalent-fingerprint/Schema parity for
  repeated nested CLR metadata across selected JSON contributors;
- exact Tool enum converters/binders and closed wire maps;
- registered handler or required Memory interface;
- the exact selected `IAgentMemoryPromotionService` object also implements
  `IAgentMemoryCurationServiceCapabilities` with `ConfirmedAtomic`; Store
  capability alone is insufficient, and curation rejects non-Singleton or
  object-changing registrations;
- access-scope provider, history authorizer, grant store, or resource-handle
  store;
- current-user, tenant, or Agent execution context infrastructure;
- Phase 8f exact InvocationBinding propagation and Memory transition-state hash
  projectors;
- Phase 8f invocation/approval/budget/audit infrastructure;
- safe audit-fact projector/ownership definitions, shared invocation fact-
  buffer/sink feature, exact output preflight, and shared prepared-outcome sink
  feature for every concrete result root;
- Host-keyed audit correlation protector when any projector declares a
  correlation fact;
- valid closed-world limits.

All selected module extension methods run before the single final
`AddCapabilityRuntime()` call. A selection marker appearing after finalization
is a deterministic startup error, not late mutable registration. Unselected
global Provider definitions are ignored; repeated identical selections do not
re-register handlers. Integration tests freeze ordering and two-Host isolation
so the DI migration is not an implicit Host breaking change.

There is no empty snapshot, reflection fallback, direct-service fallback, or
“read-only means unrestricted” fallback.

## 17. Diagnostics

Memory Tool projection uses the `AMTP` prefix and does not reuse Phase 8f `ATP`
or Control Plane diagnostic names.

Startup/contract diagnostics include:

```text
AMTP101 missing Memory runtime service
AMTP102 missing or invalid access-scope provider
AMTP103 missing history-source authorizer
AMTP104 missing expansion-grant store
AMTP105 missing Memory Tool JSON context contribution
AMTP106 missing DI handler registration
AMTP107 invalid Memory Schema/Capability/Tool descriptor set
AMTP108 invalid server-side limits
AMTP109 missing safe audit-fact projector
AMTP110 unsupported nested Schema graph or cycle
AMTP111 missing resource-handle store
AMTP112 invalid or late generated handler provider registration
AMTP113 unpinned descriptor ref on Memory Tool path
AMTP114 invalid canonical hash shape/version wiring
AMTP115 unsupported resource/grant/audit cardinality limits
AMTP116 reflection-capable or options-divergent JSON contributor
AMTP117 missing required audit correlation protector
AMTP118 duplicate binding root or incompatible nested JSON type contract
AMTP119 missing or invalid security-artifact coordinator/batch configuration
AMTP120 missing or inconsistent Agent Tool InvocationBinding snapshot
AMTP121 invalid trusted-Host artifact batch binding
AMTP122 invalid Tool enum wire contract/converter
AMTP123 missing or invalid Memory transition-state hash profile
AMTP124 missing or invalid invocation audit-fact sidecar
AMTP125 selected Promotion Service lacks ConfirmedAtomic curation guarantee
AMTP126 missing or inconsistent exact output preflight/outcome-set feature
AMTP127 invalid or conflicting security-artifact plan hash
AMTP128 invalid Tool canonical-hash/diagnostic/confidence wire projection
AMTP129 invalid Expand truncation/content-hash combination
AMTP130 invalid, duplicate, or ambiguous prepared outcome set
AMTP131 branch-dependent fact submitted to common invocation sidecar
```

Safe runtime codes include:

```text
AGENT_MEMORY_TOOL_UNAVAILABLE
AGENT_MEMORY_TOOL_CONFLICT
AGENT_MEMORY_TOOL_REDACTED
AGENT_MEMORY_TOOL_SOURCE_NOT_EXPANDABLE
AGENT_MEMORY_TOOL_SCOPE_INVALID
AGENT_MEMORY_TOOL_RESULT_CONTRACT_INVALID
AGENT_MEMORY_TOOL_RESULT_LIMIT_EXCEEDED
```

These codes are safe classifications, not exception messages or existence
evidence.

## 18. Testing strategy

### 18.1 Shared prerequisite gates

- nested scalar object and object-collection Schema projection;
- exact `$defs`/`$ref` keys, `FieldType="object"` marker, direct References,
  root-depth-zero semantics, non-null collection elements, graph limits, and
  cycle rejection;
- nested duplicate/unknown/required/nullability validation;
- recursive `JsonTypeInfo` directional parity;
- MCP and Agent JSON bytes/canonical hash compatibility for old flat schemas;
- MCP runtime/E2E/NativeAOT regression after shared-kernel extension;
- schema contract/definition v2 historical and v3 nested golden vectors;
- deterministic multi-module JSON-context contribution with shared Options,
  frozen settings, duplicate contributor rejection, and no resolver fallback;
- `DuplicateBindingRootAcrossContributors_FailsStartup`;
- `EquivalentNestedTypeAcrossContributors_IsAllowed`;
- `DifferentNestedTypeContractAcrossContributors_FailsStartup`, including
  SchemaRef, property/nullability, converter/enum policy, and fingerprint
  differences;
- DI constructor handler generation with Scoped lifetime, provider ordering,
  duplicate Capability rejection, multi-interface rejection, and no
  `new Handler()`, reflection, or handler service locator;
- `SelectedProviders_AreIsolatedAcrossTwoHostsInSameProcess`; the two service
  collections select disjoint Provider/Module sets, and referenced-unselected
  or test-only handlers never enter the other Host;
- governance auditor records `agent-tool-governance-outcome-v2` summary/hash/
  facts but not full outcome; Message/StructuredOutput/text changes do not
  change v2 when safe shape is equal;
- Invocation Gate replay payload is absent from governance audit query APIs;
- ordinary non-Memory Agent Tools with no fact projector write v2 with an empty
  fact list; no new invocation can select/write v1;
- generated exact output preflight uses the same binding-root JsonTypeInfo,
  Options, enum converters, OutputSchema, and validator as final Invoker
  serialization; a changed contract fingerprint fails startup;
- `PreflightAndFinalization_ProduceIdenticalStructuredOutput` for every concrete
  result root through prepared-outcome matching, including mutation of an
  array/list after the first preflight;
- missing/duplicate/wrong-Tool/changed-contract/changed-structured-output
  receipts enter `output_finalization_failure` and never enter audit facts;
- `FinalOutput_MustMatchExactlyOnePreparedOutcome` rejects zero/multiple
  matches, duplicate codes/receipts, and an empty outcome set;
- `UnpreparedOutcomeStatus_BecomesOutputFinalizationFailure`;
- typed Memory lifecycle failure mapping;
- closed-world empty/all-of/source-ref visibility behavior;
- `memory-content-hash-v1`, `memory-scope-hash-v1`, and `memory-pack-hash-v1`
  historical vectors plus complete v2 vectors including full SourceRefs,
  `MemoryIds`, `IncludeArchived`, boundary, truncation, and returned order;

### 18.2 Contract and descriptor tests

- every public DTO is present in the Memory Tool source-generated JSON context;
- no domain operation request is used as Tool input;
- no Tool DTO exposes ContextId, BlockId, CandidateId, MemoryId, SourceId,
  `ScopeFingerprint`, `VisibleMemorySetHash`, or `CanonicalPackHash`;
- each of the seven Tools has a distinct exact result envelope with required
  `OperationStatus`; lifecycle uses only `MemoryStatus`/`CandidateStatus` and
  non-Completed payloads are empty;
- the five OperationStatus mappings are frozen independently from Phase 8f
  outer Outcome mappings;
- every Tool enum has Unknown=0, emits only its frozen semantic string, rejects
  integer/case/unsupported aliases, and maps through exhaustive generated
  switches;
- Tool Confidence maps domain Unknown to `Unspecified=1 -> "unknown"`; default
  Tool `Unknown=0` cannot bind or serialize;
- `AgentMemoryToolCanonicalHashDto` exposes only validated Value/version fields
  and never serializes internal CanonicalHash metadata;
- `CanonicalHashDto_OmitsArtifactKindDescriptorKindScopeAndPurpose` and rejects
  an invalid digest or version tuple;
- diagnostic severity maps Info/Warning/Review/Error/Blocker explicitly to
  `info`/`warning`/`warning`/`error`/`error`; unsupported/empty values fail;
- `DiagnosticSeverity_MapsReviewAndBlockerExplicitly` and rejects integer,
  domain-string, Unknown, and case aliases;
- Schema v3 projects Tool enums as strings while exact bind/output validation
  proves their allowed-value closure;
- model-visible `AgentMemorySourceGrantDto` has GrantId/SourceKind/ExpiresAt
  only and never exposes the unexpanded source hash;
- all seven Schema, Capability, Tool, permission, role, risk, approval, budget,
  and audit values match section 9;
- exact Capability/Schema refs and generated bindings;
- ContractHash/DefinitionHash golden vectors;
- Supersede is High + Required and compile-time authoring has no ATP016;
- all outputs have fixed non-authoritative semantics;
- every Tool descriptor ref and every generated nested ref is exact with
  `Version > 0`; null/unpinned refs fail startup or runtime visibility closed.
- DescriptorRef equality/hash vectors use Namespace/Id/Version only and prove
  no Registry lookup or synthetic DescriptorKind participates.

### 18.3 Read and anti-probing tests

- tenant comes only from trusted context;
- closed-world empty scope returns no descriptor-bound memory;
- unscoped memory requires explicit scope permission;
- all-of visibility rejects mixed visible/invisible refs, including refs present
  only on nested source refs;
- invisible and missing Memory handles have identical output;
- recall count/character budget and Tool budget are independently enforced;
- source grants contain no raw source id and are principal/execution/scope bound;
- resource/history handles contain no inferable domain id and are
  principal/execution/scope/kind bound;
- forged, guessed, expired, cross-tenant, cross-user, cross-Agent,
  cross-execution, revoked, wrong-kind, and visibility-stale grants/handles have
  identical output;
- grant/handle revocation is idempotent and Completed replay does not renew;
- a later InvocationId in the same Execution can use a valid handle/grant,
  while another Execution receives the identical `Unavailable` shape;
- visible-descriptor/source-ref/grant/handle/audit/tag cardinality limits fail
  before partial grant, handle, audit, or persistence writes;
- active resource handles are reused for the same binding without expiry
  renewal; `MaxActiveResourceHandlesPerResource` blocks cross-invocation token
  growth;
- identical security-artifact BatchKey returns the original set; changed
  fingerprint at the same logical-key/purpose/ordinal conflicts, while a
  different allowlisted purpose is a separate bounded batch;
- `SameBatchAndPlan_ReturnsOriginalArtifacts` proves the returned internal
  resource ids remain authoritative for the prepared graph;
- `SameOriginPurposeOrdinal_DifferentArtifactPlan_Conflicts`, including an
  unchanged Tool input with changed recall/provider result, SourceRef, scope,
  descriptor closure, resource id, or lifetime policy;
- artifact-plan golden vectors exclude token ids/timestamps/text and include
  every security-relevant resource/grant binding;
- `HistoryHandleIssuer_DoesNotRequireAgentToolInvocation` and validates a
  versioned Host operation binding instead;
- `ChangedBindingFingerprint_Conflicts` for both Agent Tool and Host origins;
- abort revokes only `CreatedByBatch`; `ReusedExisting` artifacts remain active
  with unchanged expiry and usable by their earlier recipient;
- Prepared/Committed/Aborted bookkeeping never adds a synchronous post-Memory
  commit call or changes resolver authorization semantics;
- PreparationOrdinal advances only after a zero-write identity collision and
  prior artifacts are revoked before the new id/batch is prepared;
- unauthorized history source calls compressor zero times;
- external source expansion does not query Control Plane or activation stores;
- stored sanitized content is the only text returned.
- `FullExpand_RequiresCanonicalContentHash` and returns the complete artifact's
  v2 Tool-safe projection;
- `TruncatedExpand_OmitsCanonicalContentHash` for every character limit and
  never hashes the prefix as `memory-content-hash-v2`;
- historical v1 content hashes are recomputed as effective v2 for Tool/Grant/
  Pack/Audit without mutating the stored record; invalid provenance fails
  closed.

### 18.4 Processing and curation tests

- compression loads stored history instead of accepting raw model text;
- deterministic and LLM-backed registered implementations both use the same
  handler path;
- compression validates all returned tenant/source/limit invariants before
  save;
- conversation/task expansion covers non-zero start, singleton, tail, negative,
  reversed, and out-of-range intervals against original indexes;
- invalid-range and nonexistent-resource expansion are byte-equivalent
  `Unavailable` outcomes;
- compressed-context expansion resolves exactly one BlockId and never returns
  sibling blocks or treats ContextId as a block grant;
- deterministic and LLM provider labels never become ContextId, BlockId,
  CandidateId, MemoryId, SourceId, Tool output, or audit resource identity;
- framework ids are opaque, batch-unique, store-collision-checked, and do not
  encode tenant/history/content identifiers;
- compression rejects a changed Range/SourceKind/SourceId/DescriptorRef or an
  unknown provider ref label before the first save;
- extraction loads stored compressed context and saves Candidate-only results;
- extraction validates every result before the first save;
- extraction rejects SourceRefs outside input Blocks and DescriptorRefs outside
  the input context's effective closure;
- final `CanonicalContentHash` changes with adapter-sanitized content and is
  recomputed rather than trusted from Provider output;
- `memory-content-hash-v2` is order-independent for an equivalent multi-source
  set and changes when a source, Range, DescriptorRef, relationship id, or
  upstream hash is added/removed/changed;
- empty SourceRefs writes an empty array, never `"unknown"`; self-source/hash
  cycles are rejected; Block/Candidate/Memory/Grant/Pack/Audit use v2;
- `HandleIssueFailure_PerformsNoMemoryWrite`;
- `GrantIssueFailure_PerformsNoMemoryWrite`;
- `ConfirmedNoWriteMemoryFailure_RevokesPreparedHandles` and prepared Grants;
- `PromoteHandlePreparationFailure_DoesNotConsumeCandidate`;
- `OutputPreflightFailure_RevokesCreatedArtifactsBeforeMutation` and leaves
  reused artifacts active;
- `Reject_PreflightsAndPublishesAllowedOutcomesBeforeConditionalTransition` and
  issues no new Handle/Grant;
- strict call-order tests prove all security artifacts and every allowed result
  envelope preflight/outcome-set publication are complete before the first
  Memory write and no fallible adapter work occurs after a successful mutation;
- promote/reject/supersede build actor, tenant, time, reason, explanation, and
  source refs from the correct trusted/domain sources;
- Candidate/Memory payload cannot be updated after create; only typed expected-
  state lifecycle transitions exist on the production mainline;
- a Candidate state-hash mismatch between preparation and service transition
  returns Conflict, writes nothing, and revokes only artifacts created by the
  batch;
- `PromoteConflict_MatchesPreparedConflictReceipt`;
- `RejectConflict_MatchesPreparedConflictReceipt`;
- `SupersedeConflict_MatchesPreparedConflictReceipt`;
- `ResourceDisappeared_MatchesPreparedUnavailableReceipt` when that branch is
  declared by the authoritative service contract;
- `CurationConflict_IsNotInvocationIndeterminate`;
- Supersede binds both replacement Candidate and target Memory expectations;
  either mismatch performs zero writes;
- `PreparedEnvelope_EqualsCommittedMemoryGraph` covers id, kind, content v2
  hash, confidence, tags, descriptor/source refs, promoted timestamp,
  lifecycle/authority, and relationship fields;
- state-hash golden vectors cover every immutable/lifecycle field and require a
  new shape when the domain contract gains a field;
- only `IAgentMemoryPromotionService` performs lifecycle changes;
- Promote and Supersede outputs are Active and non-authoritative;
- `SameCandidate_CannotSupersedeTwoMemories`;
- `CrossTenantReplacement_IsRejected`;
- `NonCandidateReplacement_IsRejected`;
- `SuccessfulSupersede_ConsumesReplacementCandidate` and writes matching
  forward/back links with a new independent MemoryId;
- `SameCandidate_CannotBePromotedTwiceConcurrently`;
- `PromoteAndSupersedeSameCandidate_OnlyOneWins`;
- `RejectAndPromoteSameCandidate_OnlyOneWins`;
- `CurationTools_RequireConfirmedAtomicPromotionService`; missing/Unknown or a
  capability object different from the selected Promotion Service instance
  fails startup before discovery;
- `ConfirmedStore_CustomUnsafePromotionService_FailsStartup`;
- `PromotionService_PostCommitException_CannotClaimConfirmedAtomic`;
- `PromotionService_UsesDifferentStore_FailsCapabilityValidation`;
- `CancellationAfterCommit_ReturnsCommittedSuccess` without post-commit event,
  snapshot, projection, or cleanup work on the call path;
- InMemory fault injection proves every non-success/exception point performs
  zero curation writes and every success commits the complete transition;
- `ConfirmedNoWriteFailure_RevokesOnlyCreatedByBatchArtifacts`;
- `UnknownCommitOutcome_IsNotMappedToOrdinaryFailureOrRevocation` is a contract/
  startup test for a Promotion Service that cannot claim `ConfirmedAtomic`;
- Reject returns no candidate content;
- lifecycle conflict is safe and does not expose hidden state;
- Memory/store exception messages never reach Tool output.

### 18.5 Phase 8f governance integration

- all seven Tools are `AuditMode.Required`;
- read/write classification matches the matrix;
- AutomaticAllowed never bypasses PolicyDriven approval;
- Supersede requires claimed approval evidence;
- Capability permissions execute through AuthorizationMiddleware;
- `InvocationSource.Agent`, tenant/user, `InputJson`, idempotency key, and context
  item constants reach the Capability mainline;
- `AgentToolInvoker_PropagatesExactInvocationBinding` with the same structured
  LogicalKey and fingerprint used by Gate/Governance/Budget;
- `MemoryHandler_DoesNotRecomputeInvocationFingerprint`; missing/wrong-type/
  identity-inconsistent binding snapshots fail before artifact preparation;
- `AgentToolInvoker_PropagatesInvocationFactSink` and retains the only sealing
  owner without AsyncLocal/static state;
- `AddCrestAgentTools_OwnsFactAndOutcomeBufferFactories`; two Hosts receive
  isolated factories/state;
- `AddAgentMemoryTools_DoesNotRegisterAgentToolsRuntimeBuffers` and only
  validates/consumes their abstraction features;
- `InvocationFactBuffer_SealsExactlyOnce`, rejects post-Seal writes and safely
  disposes abandoned invocation state;
- `AgentToolInvoker_PropagatesAndMatchesPreparedOutcomeSet`; the Handler receives
  only the sink view and the Invoker owns publication sealing/unique matching;
- `CapabilityFailure_DiscardsOutputAndInternalFacts` while retaining validated
  input facts;
- `ScopeMaxAuditFacts_CapsCombinedOutputAndInternalFacts` using the smaller
  trusted/global limit;
- `EveryPreparedBranch_FitsAuditFactCapBeforeMutation`, while finalization
  includes facts only from the uniquely selected branch;
- sidecar validation rejects Memory text, raw ids, HandleId/GrantId, unsupported
  fact kinds/encodings, duplicates, and over-cap writes;
- common sidecar rejects OperationStatus, persisted/returned count, lifecycle
  status, truncation, and returned content-hash facts as branch-dependent;
- `Conflict_DoesNotRetainCompletedOnlyFacts`;
- `Completed_SelectsCompletedInternalFacts`;
- `Unavailable_SelectsUnavailableInternalFacts`;
- `OutputFactsAndInternalFacts_AgreeOnOperationStatus` and a mismatch follows
  `output_finalization_failure`;
- completed replay executes no Memory service or mutation twice;
- same logical invocation with changed arguments/origin/roles conflicts;
- post-dispatch Indeterminate is fenced and never automatically retried;
- pre-dispatch audit stores only counts/limits/source kind/handle kind and
  count/presence flags/explanation flag; it never stores HandleId or GrantId,
  and guessed unavailable tokens never produce a raw id fact;
- finalization audit stores safe facts and Host-keyed correlation HMAC only
  after authorized load, with no raw Memory text or domain ids;
- outcome v2 excludes Message, StructuredOutput, explanations, and all Memory
  text while the Invocation Gate retains the separately protected replay
  Outcome.

### 18.6 Dependency-boundary tests

Freeze:

```text
Agent.Memory / Agent.Memory.Abstractions / Agent.Memory.Llm
  × Agent.Tools / ControlPlane / Web / Platform

Agent.Memory.Tools.Abstractions
  × Agent.Tools runtime / Capability runtime / ControlPlane / HumanTask /
    DescriptorDraft / Web / MCP / providers

Agent.Memory.Tools handlers/orchestration
  × RuntimeActivationGate / ControlPlane / HumanTask / DescriptorDraft /
    mutable descriptor registries / direct Handler invocation / reflection
```

Generated descriptor bootstrap registration is inspected separately and must be
the only `DescriptorProviderRegistry` reference in the adapter assembly.

### 18.7 E2E and NativeAOT

The generator-backed E2E Host executes at least:

```text
seed sanitized active memory
  -> BuildAgentMemoryPack
  -> issue grant
  -> ExpandAgentMemorySource

seed stored conversation/task + issue trusted history handle
  -> CompressAgentHistory
  -> ExtractMemoryCandidates
  -> PromoteMemoryCandidate
  -> BuildAgentMemoryPack

seed active item + replacement candidate
  -> approved SupersedeMemoryItem
  -> replacement candidate consumed and links consistent
```

It also proves permission denial, PolicyDriven approval denial, Tool budget
denial, visibility denial, anti-probing, completed replay, and Indeterminate
fencing. The read path expands a non-zero singleton turn range and one exact
compressed block, proving no adjacent turn/block is returned. Every successful
envelope asserts `OperationStatus=Completed`; unavailable/lifecycle/sanitizer/
unsupported-source cases assert the exact inner status while outer permission,
schema, unknown-failure, and Indeterminate cases never synthesize one.

The formal linux-x64 NativeAOT fixture completes native linking and runs the
native binary through at least:

```text
generated nested Schema/Capability/Tool descriptors
  -> at least two module JSON contexts over shared frozen Options
  -> ConfirmedAtomic InMemory Promotion Service/Store binding validation
  -> discovery
  -> BuildAgentMemoryPack
  -> exact nested output prepared-outcome/final unique match
  -> branch-correct internal audit-fact finalization
  -> PromoteMemoryCandidate
  -> Memory mutation once
  -> completed replay without second mutation
```

The fixture uses the deterministic Memory implementation. LLM/provider AOT
capability remains separately declared. Analyzer-only or PublishTrimmed evidence
is not NativeAOT verification.

## 19. Delivery slices

0. **Shared mainline prerequisites**: bounded nested Schema support, composable
   Agent Tool JSON contexts with root/nested contract rules, DI-safe handler
   generation, exact InvocationBinding propagation, closed-world Memory
   visibility, typed lifecycle failures, immutable/expected-state Candidate
   transitions, Promotion-Service-owned confirmed-atomic curation gating,
   domain-owned supersession validation,
   source Range integrity, trusted artifact identity, provenance closure,
   artifact-plan-bound dual-origin security preparation, shared exact-output
   bounded preflight outcome sets, Agent-Tools-owned invocation fact/outcome
   sidecars, branch-correct audit facts, global governance-audit v2, Tool
   enum/hash/diagnostic wire contracts,
   full-vs-truncated Expand hash semantics, and Memory content/scope/pack v2
   shapes.
1. **Contracts and descriptors**: projects, DTOs, access/grant/handle contracts,
   concrete OperationStatus envelopes, permissions/roles/names, Schemas,
   Capabilities, Tool specs, canonical hashes, and boundary tests.
2. **Read path**: trusted scope factory, Build pack, opaque resource handles and
   grants, exact-range expansion, safe projection, read budgets, and
   anti-probing tests.
3. **Processing path**: history authorization, compression persistence,
   extraction persistence, limits, provider-neutral implementation selection,
   and safe facts.
4. **Curation path**: trusted operation requests, promote/reject/supersede,
   lifecycle mapping, approval/risk floors, and replay tests.
5. **Executable closure**: full E2E, linux-x64 NativeAOT publish-link-run,
   Runtime/All solution inclusion, usage documentation, `memory.md`, and Issue
   #53 acceptance evidence.

Slice 0 is a stop gate. Later slices must not work around a failed prerequisite
with reflection, service location, direct Memory store writes, flat string
protocols, permissive visibility, provider-owned ids/provenance, widened source
ranges, post-Memory security-artifact issuance, handler-only lifecycle checks,
raw audit tokens, or full-output audit retention.

## 20. Exit criteria

This phase is complete only when:

1. all seven operations are source-generated Agent Tools over native
   Capabilities and exact Schemas;
2. `IAgentToolInvoker -> ICapabilityDispatcher -> Capability Pipeline -> Memory
   Handler -> existing Memory runtime` is the only execution path;
3. Memory core and LLM adapter remain unaware of Agent Tools, Control Plane,
   provider Tool SDKs, and activation;
4. natural nested Tool DTOs use the bounded `$defs`/`$ref` Schema v3 mainline
   with no JSON string, dictionary, parallel-array, or reflection fallback;
5. independently packaged JSON contexts use one frozen shared Options contract,
   compose deterministically, give each binding root one owner, and accept
   repeated nested metadata only with identical Schema/parity/fingerprint;
6. every Capability handler is Scoped through one generated Provider path,
   only explicitly selected Module/Provider ids apply to each Host, and two
   Hosts in one process cannot leak handlers into each other;
7. every visibility/grant/nested Schema DescriptorRef is exact with
   `Version > 0`, and equality/hash/scope use Namespace/Id/Version only without
   DescriptorKind or Registry inference;
8. trusted tenant/user/Agent/execution/time/governance values never come from
   model arguments;
9. Capability permissions, Agent roles, selection policy, approval, Tool
   budget, governance audit, and Capability audit all remain independently
   enforced;
10. closed-world visibility handles an empty universe, unscoped memory, mixed
    descriptor refs, and nested source refs without leakage;
11. all seven concrete result envelopes have required `OperationStatus`, while
    `MemoryStatus`/`CandidateStatus` exclusively represent lifecycle, and all
    Tool enums use frozen string-only wire mappings with fail-closed Unknown=0;
    confidence `"unknown"` maps to Unspecified=1, while content hashes and
    diagnostic severity use only the dedicated safe projections;
12. model contracts use principal/execution/scope-bound opaque handles/grants
    and expose no persistent Context/Block/Candidate/Memory/history/source ids
    or an unexpanded source content hash;
13. Agent Tool batches consume the exact propagated structured LogicalKey/
    InvocationFingerprint, Host History batches use an independent versioned
    Host origin, `ArtifactPlanHash` binds the complete security graph, active
    Handles are resource-capped/reused without renewal, and an identical batch/
    plan retry returns the original authoritative artifact set;
14. expansion validates original-index ranges, resolves one compressed Block,
    and never widens a grant to adjacent content;
15. framework-owned ContextId/BlockId/CandidateId/MemoryId values are opaque,
    batch unique, collision checked, and never accepted from a Provider;
16. compression/extraction output provenance is a structural subset of trusted
    loaded input and `memory-content-hash-v2` binds the complete canonical
    SourceRef set after sanitation;
17. Build and Expand return only sanitized, budgeted, non-authoritative context,
    internal scope/set/pack hashes are not returned to the model, and truncated
    Expand output has no CanonicalContentHash while full output requires it;
18. every required Handle/Grant and every allowed Completed/Conflict/
    conditionally-Unavailable envelope is exact-preflighted and published as
    one bounded outcome set before Memory mutation—including Reject; the final
    Invoker result matches exactly one prepared code/receipt, normal curation
    Conflict is not Indeterminate, immutable/expected-state plans forbid
    committing another graph, preparation/preflight failure writes no Memory,
    and only a confirmed no-write result revokes artifacts created by that
    batch;
19. Compress resolves a trusted history handle, loads stored history, and saves
    only fully validated compressed context through create-only persistence;
20. Extract resolves a trusted Context handle and saves only fully validated
    Candidate results without promotion;
21. Promote/Reject/Supersede use only `IAgentMemoryPromotionService`; one
    store-owned conditional Candidate transition permits exactly one concurrent
    winner, general payload Save is not a mainline API, and Promote/Supersede
    accept expectation-bound plans with framework-preassigned new MemoryId;
    these Tools cannot start unless the exact selected Promotion Service itself
    exposes a frozen `ConfirmedAtomic` capability, and unknown commit state is
    never mapped to ordinary failure; confirmed Conflict/ResourceUnavailable
    selects a prepared payload-empty branch;
22. Supersede is High risk with Required approval and all seven Tools require
    governance audit;
23. completed replay never repeats a provider call, persistence/lifecycle
    mutation, grant/handle issuance, or grant/handle renewal;
24. Indeterminate remains fenced and no test or documentation claims
    distributed exactly-once or undeclared durable Memory-store atomicity;
25. every new Phase 8f governance record uses
    `agent-tool-governance-outcome-v2` (empty facts when no projector), excludes
    all text/output payloads, and governance queries cannot read Gate replay
    data; the Invoker-owned common sidecar accepts only branch-invariant facts,
    actual status/count/lifecycle/hash facts come from the final output
    projector, and branch-specific internal facts come only from the uniquely
    matched prepared outcome; Capability failure discards them all and the
    smaller trusted/global cap applies; `AddCrestAgentTools()` alone owns Fact/
    Outcome buffer factories and Invoker propagation, while Memory only
    consumes their abstractions; v1 is historical read-only;
26. pre-dispatch facts contain only safe limits/counts/kinds/presence flags;
    raw HandleId/GrantId are never audited, and keyed resource correlation is
    allowed only after authorized load where explicitly required;
27. invisible, missing, cross-tenant, unauthorized, expired, revoked, wrong-kind,
    and forged handles/grants share non-probing result shapes;
28. descriptor/source/grant/handle/audit/tag cardinality limits fail closed
    before partial security-artifact or Memory writes, with final Invoker
    revalidation remaining authoritative after dispatch;
29. v1 Tool input omits `IntentText`; no description promises relevance
    semantics that the retriever does not implement;
30. Schema v2/v3 and Memory content/scope/pack v1/v2 shapes have versioned
    golden vectors, Candidate/Memory transition-state shapes are frozen, and
    vectors include complete multi-source provenance, artifact plans, output-
    preflight contract/structured-output receipt hashes, prepared outcome codes/
    fact ownership, and every behavior-affecting scope field;
31. boundary tests prove handlers cannot reach Runtime Activation Gate,
    Control Plane, HumanTask approval, DescriptorDraft, mutable registries, or
    direct runtime handlers;
32. existing Agent Tool, MCP, Memory, Capability, Metadata, Control Plane, and
    dependency-boundary suites remain green;
33. the first-party Memory Tool path passes a real linux-x64 NativeAOT
    publish-link-run fixture covering Build and Promote plus completed replay;
34. documentation explicitly preserves `IsAuthoritative=false`, Gate replay
    data sensitivity, security-artifact ordering, and declared durability/
    atomicity limitations; and
35. no compatibility fallback or legacy path becomes a second formal mainline.

## 21. Deferred work

- `ArchiveMemoryItem` Tool projection.
- Durable Memory stores and transaction/reconciliation protocols.
- Durable distributed expansion-grant/resource-handle store implementations.
- Provider-specific Tool adapters or Agent planner/runtime loops.
- HumanTask-backed Memory approval workflow.
- Dynamic provider-cost accounting beyond the Phase 8f Tool budget contract.
- Autonomous retention, compaction, deduplication, candidate ranking, or
  promotion policy.
- Cross-execution source-grant delegation.
- Broader Schema features beyond the bounded closed nested-object subset.
- Truncated expansion integrity through a future
  `memory-expanded-slice-hash-v1` contract.

## 22. Review finding closure map

| Finding | Frozen resolution |
| --- | --- |
| P0-1 Source range integrity | 5.7, 8.3, 18.4, Slice 0 |
| P0-2 Trusted/opaque artifact identity | 5.8, all Tool DTOs in 8, 18.4, Slice 0 |
| P0-3 Supersede lifecycle | 5.5, 8.8, 13, 18.4, Slice 0 |
| P0-4 Provenance closure | 5.9, 8.4-8.5, 11, 18.4, Slice 0 |
| P1-1 OutcomeHash v2 / sensitive replay Gate | 5.6, 15, 18.1/18.5 |
| P1-2 Pre-dispatch resource facts | 5.6, 15, 18.5 |
| P1-3 DI handler registration | 5.3, 16, 18.1 |
| P1-4 Shared Options JSON contributors | 5.2, 16, 18.1 |
| P1-5 Exact Descriptor versions | 5.4, 18.2, Exit 7 |
| P1-6 Internal pack/scope/set hashes | 8.1-8.2, 14, 18.2 |
| P1-7 Nested and Memory hash shapes | 5.1, 5.10, 18.1 |
| P2-1 Grant revocation contract | 7, 18.3 |
| P2-2 Cardinality limits | 6-7, 11-12, 18.3 |
| P2-3 IntentText semantics | 5.10, 8.2, Exit 29 |

Revision 3 closure:

| Finding | Frozen resolution |
| --- | --- |
| P0-1 Security artifact before mutation | 5.11, 8.4-8.8, 11, 13, 18.4, Slice 0 |
| P0-2 DescriptorRef has no Kind | 5.4, 5.10, 18.2, Exit 7/30 |
| P0-3 Exact OperationStatus envelopes | 8.1-8.8, 10, 14, 18.2/18.7, Exit 11 |
| P0-4 Complete provenance content hash v2 | 5.9-5.10, 7-8, 18.1/18.4, Exit 16/30 |
| P1-1 Per-Host selected Handler Providers | 5.3, 16, 18.1, Exit 6 |
| P1-2 JsonContext duplicate ownership | Refined by Revision 4 root/nested rule in 5.2 |
| P1-3 Resource Handle quota and BatchKey | 5.8, 5.11, 6-7, 18.3, Exit 13 |
| P1-4 No raw Token in governance audit | 5.6, 15, 18.5, Exit 26 |
| P1-5 No pre-expansion source hash | 7, 8.1/8.3, 18.2, Exit 12 |
| P1-6 Shared concurrent Candidate consumption | 5.5, 8.6-8.8, 13, 18.4, Exit 21 |

Revision 4 closure:

| Finding | Frozen resolution |
| --- | --- |
| P0-1 BatchKey trusted origin | 5.11, 6, 11, 18.3/18.5, Exit 13 |
| P0-2 Prepared/domain TOCTOU | 5.5, 5.11, 8.6-8.8, 11/13, 18.4, Exit 18/21/30 |
| P0-3 Root vs nested JsonContext contracts | 5.2, 16, 18.1, Exit 5 |
| P1-1 Created vs reused rollback | 5.8/5.11, 7, 18.3, Exit 18 |
| P1-2 Tool enum wire contracts | 8.1, 14, 18.2/18.7, Exit 11 |
| P1-3 Global governance outcome v2 | 5.6, 15, 18.1/18.5, Exit 25 |

Revision 5 closure:

| Finding | Frozen resolution |
| --- | --- |
| P0-1 Handler-to-Invoker internal Audit Facts | 5.6, 11, 15-16, 18.5, Exit 25/28 |
| P0-2 Confirmed no-write curation outcome | 5.5, 5.11, 11/13/16, 18.4, Exit 18/21/24 |
| P1-1 Shared exact output preflight | 5.11, 10-11/16, 18.1/18.4, Exit 18/30 |
| P1-2 Artifact-plan-bound BatchKey | 5.11, 11, 18.3, Exit 13/30 |
| P1-3 Tool-safe hash/severity shapes | 8.1/8.3, 14, 18.2/18.7, Exit 11 |

Revision 6 closure:

| Finding | Frozen resolution |
| --- | --- |
| P0 Promotion-Service-owned ConfirmedAtomic proof | 5.5, 5.11, 11/13/16, 18.4, Exit 21/24 |
| P1-1 Preflight receipt returned to Invoker | 5.11, 11/16, 18.1/18.5, Exit 18/30 |
| P1-2 Fact/Receipt buffer registration ownership | 5.6, 5.11, 16, 18.5, Exit 25/31 |
| P1-3 Reject explicit preflight order | 5.11, 8.7, 11, 18.4, Exit 18/21 |
| P1-4 Truncated Expand hash semantics | 8.3, 18.3, Exit 17, Deferred |
| Minor Confidence zero-value semantics | 8.1, 18.2, Exit 11 |

Revision 7 closure:

| Finding | Frozen resolution |
| --- | --- |
| P0 Single receipt rejects legal curation branch | 5.5/5.11, 8.6-8.8, 11/13, 18.1/18.4, Exit 18/21 |
| P1 Branch-specific facts pollute Conflict | 5.6/5.11, 15-16, 18.5, Exit 25/30 |
