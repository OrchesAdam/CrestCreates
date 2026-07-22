# Phase 8c+ — MCP Context and Memory Tool Projection Design

**Date**: 2026-07-21
**Status**: APPROVED
**Issue**: #54
**Depends on**: Phase 7e+ Agent Memory Runtime, Phase 8e MCP Tool Projection, Phase 8d+ Governed Agent Memory Tool Projection
**Related**: #43 — Agent Memory first closure, #21 — MCP Tool Projection

## 1. Goal and positioning

Project selected Agent Memory context operations as read-only MCP tools. This phase is an MCP protocol adapter over the existing Memory runtime, not a new Memory runtime, not an MCP version of Agent Tool governance, and not a general RAG server.

The only execution mainline is:

```text
[McpToolSpec]
  → Source Generator → McpToolDescriptor + McpToolBindingContract
  → Startup composition → Immutable McpToolRuntimeSnapshot
  → McpToolDiscoveryService / McpToolInvoker
  → ICapabilityDispatcher.DispatchAsync(descriptor, InvocationSource.Mcp, typedInput)
  → Capability Pipeline (authorization → validation → audit → handler)
  → MCP Memory Capability Handler
  → IAgentMemoryReadCore / IAgentContextReadCore / IAgentMemorySourceExpandCore (shared read core)
  → IAgentMemoryRetriever / IAgentCompressedContextStore / IAgentContextSourceExpander
  → IAgentMemoryAccessScopeProvider / IAgentMemoryAccessArtifactCoordinator
```

MCP must not directly access Memory Store, CompressedContext Store, or `IAgentContextSourceExpander`. All access is mediated through the shared read core. `Mcp.Memory` has no direct project reference to, and does not use types from, `Agent.Memory.Abstractions`. Access is enforced through `ReadCore` plus dependency-boundary tests — `Projection.Abstractions` itself depends on `Agent.Memory.Abstractions`, and .NET `ProjectReference` defaults to transitive, so the true boundary is "must not directly use Store/Retriever types", not physical absence of the transitive assembly reference. An architecture test in `CrestCreates.DependencyBoundaries.Tests` additionally enforces that `CrestCreates.Mcp.Memory` must not reference namespace/types: `IAgentMemoryStore`, `IAgentMemoryCompressedContextStore`, `IAgentMemoryRetriever`, `IAgentContextSourceExpander`.

### 1.1 Non-goals

This phase does not implement:

- governed agent tool projection (that is Phase 8d+ / #53);
- production MCP Server hosting, stdio, SSE, Streamable HTTP, or MCP authentication;
- mutating memory tools (promote, reject, supersede, compress, extract);
- Agent Control Plane replacement or MCP-specific descriptor authoring;
- a second grant mechanism — if absolute zero-write is required, the existing grant system must support stateless signed credentials, not a #54-specific alternative;
- general RAG server;
- activation or governance changes.

## 2. Repository facts that constrain the design

1. `BuildAgentMemoryPackHandler` and `ExpandAgentMemorySourceHandler` depend on Agent Tool execution context, invocation binding, audit fact buffer, and output preflight receipt. MCP cannot directly reuse these handlers and must not fake Agent Tool calls.
2. Memory Tool Schema already contains nested objects, object collections, and enums. The current MCP snapshot/parity facade does not pass explicit Schema reference closure. The shared read core and MCP Memory integration must resolve and freeze trusted nested Schema reference closures before handing them to the JSON Schema projector and CLR parity validator. Encoding nested results as JSON strings, parallel arrays, or MCP-specific flat protocols is rejected.
3. `IAgentMemoryToolAccessScopeProvider` already resolves visibility boundaries, budgets, and handle/grant lifetimes. MCP reuses this provider with a projection-neutral principal.
4. `IAgentMemorySecurityArtifactCoordinator` currently only has `PrepareForAgentToolAsync` and `PrepareForHostAsync` — both accept `AgentMemoryToolPrincipal`. New projection-neutral interfaces (`IAgentMemoryAccessArtifactCoordinator`, etc.) are defined in `Agent.Memory.Projection.Abstractions` with new names (see §3.3.2, §4.7).
5. `IAgentMemoryResourceHandleResolver` currently accepts `AgentMemoryToolPrincipal`. New projection-neutral interfaces (`IAgentMemoryAccessHandleResolver`, etc.) are defined in `Agent.Memory.Projection.Abstractions` (see §3.3.2, §4.7).
6. `AgentMemoryPack.IsAuthoritative` is always false. Tool projections must preserve that invariant and must not introduce a second authority flag.
7. `McpToolSpecAttribute` has no `ReadOnlyHint` property. `ReadOnlyHint` is derived automatically from `CapabilityKind.Query` on the associated Capability descriptor. Spec declarations must not set `ReadOnlyHint` — they must ensure the Capability is `Query` kind.
8. `McpToolRuntimeSnapshotBuilder` does not use `IAgentToolJsonContextContributor`. It works through `McpJsonOptions` and `ISchemaRegistry`. A new `IMcpJsonContextContributor` is needed for CLR JSON metadata closure (see §8.2).
9. `CrestCreates.Agent.Memory.Tools.Abstractions` references `CrestCreates.Agent.Tools.Abstractions`. The new `CrestCreates.Mcp.Memory` integration must not transitively pull in Agent Tool governance types. New projection-neutral assemblies are needed (see §3.1).
10. Capability handlers implement `ICapabilityHandler<TInput, TOutput>` returning `Task<TOutput>`, not `CapabilityExecutionResult`. Handlers are registered via source-generated `GeneratedHandlerRegistry`, which currently has replace-semantics — a composable registration mode is needed before multi-module composition works (see §8.5).
11. Descriptor providers use `IDescriptorProvider<SchemaDescriptor>` / `IDescriptorProvider<CapabilityDescriptor>` via `[ModuleInitializer]` + `DescriptorProviderRegistry`, not `[CapabilityDescriptorProvider]`.
12. `CapabilityExecutionRecord` has no count/hash fields. MCP Memory audit uses only the existing generic Capability Pipeline audit (see §6.7).
13. `McpToolInvoker` places trusted MCP call identity into `CapabilityExecutionContext.Items`: `HostId`, `RequestId`, `SessionId`, `InvocationId`, `ToolDescriptorId`, `ToolDescriptorVersion`. MCP handlers must derive `AgentMemoryAccessPrincipal` from these, not from `CapabilityExecutionContext.ExecutionId`.
14. In #53, grant Principal binds to `TenantId + UserId + AgentId + Agent ExecutionId` (stable across tool calls within one execution). The single Tool `InvocationId` is recorded in `IssuingInvocationId` but is NOT used for grant access control — grants are reusable across tool calls within the same execution. MCP must follow the same pattern: grant access control must bind to a stable security context, not to a per-call InvocationId.

## 3. Assembly boundaries and dependency graph

### 3.1 New assemblies

| Assembly | Location | Owns |
|---|---|---|
| `CrestCreates.Agent.Memory.Projection.Abstractions` | `src/Runtime/Agent/` | Projection-neutral contracts: `AgentMemoryAccessPrincipal`, `AgentMemoryCallerKind`, `AgentMemoryArtifactOrigin`, `AgentMemoryArtifactOriginKind`, `AgentMemoryAccessScope`, `IAgentMemoryAccessScopeProvider`, `IAgentMemoryAccessArtifactCoordinator`, `IAgentMemoryAccessHandleResolver`, `IAgentMemoryAccessGrantResolver`, `AgentMemoryAccessResourceHandle`, `AgentMemoryAccessSourceGrant`, `AgentMemoryAccessResolvedResource`, `AgentMemoryAccessPreparedArtifacts`, `AgentMemorySecurityArtifactReceipt`, `AgentMemoryReadCoreOutcome<T>`, all read request/result types, all read-only DTOs and enums (preserving existing `AgentMemoryTool*` names via TypeForward), `IAgentMemoryContextHandleIssuer`, `AgentMemoryContextHandleIssueResult`. No Agent Tool governance types, no MCP protocol, no `ICapabilityHandler`. |
| `CrestCreates.Agent.Memory.Projection` | `src/Runtime/Agent/` | Default implementations of projection-neutral security infrastructure: `AgentMemoryAccessArtifactCoordinator`, `AgentMemoryAccessHandleResolver`, `AgentMemoryAccessGrantResolver`, `DefaultAgentMemoryContextHandleIssuer`. DI extension `AddAgentMemoryProjectionSecurity()`. |
| `CrestCreates.Agent.Memory.ReadCore` | `src/Runtime/Agent/` | `IAgentMemoryReadCore`, `IAgentContextReadCore`, `IAgentMemorySourceExpandCore`, implementations. Visibility filtering, budget enforcement, handle/grant issuance, sanitization/hash projection. No Agent Tool governance, no MCP protocol. |
| `CrestCreates.Mcp.Memory` | `src/Integrations/` | `[McpToolSpecs]` declarations, 3 MCP Memory capability handlers, DI extension, `IMcpJsonContextContributor` for MCP Memory DTOs, descriptor providers via `ModuleInitializer`, MCP result mapping. |

### 3.2 Dependency graph

```text
Agent.Memory.Projection.Abstractions
  ← Core.Abstractions
  ← Agent.Memory.Abstractions (DescriptorRef, AgentContextSourceRef, etc.)
  ← Metadata.Abstractions (CanonicalHash)

Agent.Memory.Projection
  ← Agent.Memory.Projection.Abstractions
  ← Agent.Memory.Abstractions

Agent.Memory.ReadCore
  ← Agent.Memory.Projection.Abstractions
  ← Agent.Memory.Projection (for security infrastructure implementations)
  ← Agent.Memory.Abstractions

Agent.Memory.Tools.Abstractions (existing, unchanged)
  ← Agent.Tools.Abstractions
  ← Agent.Memory.Abstractions
  ← Agent.Memory.Projection.Abstractions (type forwards for migrated DTO/enum types)

Agent.Memory.Tools (refactored)
  ← Agent.Memory.ReadCore (delegates read orchestration)
  ← Agent.Memory.Projection.Abstractions
  ← Agent.Memory.Projection (security infrastructure)
  ← Agent.Memory.Abstractions
  ← Agent.Memory.Tools.Abstractions
  ← Agent.Tools (governance: preflight, audit, invocation binding)
  ← Agent.Tools.Abstractions
  ← Capability.Abstractions

Mcp.Memory
  ← Mcp.Abstractions ([McpToolSpec], IMcpToolDiscoveryService, etc.)
  ← Metadata.Mcp.Abstractions (McpToolDescriptor)
  ← Agent.Memory.Projection.Abstractions (projection-neutral principal, DTOs, contracts)
  ← Agent.Memory.ReadCore (delegates read orchestration — sole access path to Memory stores)
  ← Capability.Abstractions (ICapabilityHandler)
  ← Metadata.Abstractions (IDescriptorProvider)
  ← Schema.Abstractions (for Schema descriptor providers)
```

**Critical constraints**:
- `Mcp.Memory` has no direct project reference to, and does not use types from, `Agent.Memory.Abstractions`, `Agent.Memory.Tools`, `Agent.Memory.Tools.Abstractions`, `Agent.Tools`, or `Agent.Tools.Abstractions`. It reaches Memory stores only through `Agent.Memory.ReadCore`. The true boundary is enforced by dependency-boundary tests (no direct use of Store/Retriever types), not by physical absence of transitive assembly references.
- `Agent.Memory.Projection.Abstractions` does NOT reference `Capability.Abstractions` — `ICapabilityHandler` is not a projection concern.
- `Agent.Memory.Projection.Abstractions` does NOT reference `Agent.Tools.Abstractions` — no Agent Tool governance types leak through.
- Architecture test in `CrestCreates.DependencyBoundaries.Tests` enforces: `CrestCreates.Mcp.Memory` must not reference `IAgentMemoryStore`, `IAgentCompressedContextStore`, `IAgentMemoryRetriever`, `IAgentContextSourceExpander`.

### 3.3 Type migration strategy — split approach

The migration uses two different strategies depending on whether the type's public API shape changes.

#### 3.3.1 Types that can be TypeForwarded (public shape unchanged)

These types have no `AgentMemoryToolPrincipal` or other Agent-Tool-specific types in their public API. They move from `Agent.Memory.Tools.Abstractions` to `Agent.Memory.Projection.Abstractions` with the same namespace and name, using .NET `TypeForwardedTo`:

- `AgentMemoryToolOperationStatus`
- `AgentMemoryToolKind`
- `AgentMemoryToolConfidence`
- `AgentMemoryToolMemoryStatus`
- `AgentMemoryToolSourceKind`
- `AgentMemoryToolDiagnosticSeverity`
- `AgentMemoryToolItemDto`
- `AgentMemoryToolBlockDto`
- `AgentMemorySourceGrantDto`
- `AgentMemoryToolCanonicalHashDto`
- `AgentMemoryToolDiagnosticDto`
- `AgentMemoryResourceKind`
- `BuildAgentMemoryPackInput`
- `BuildAgentMemoryPackResult`
- `ExpandAgentMemorySourceInput`
- `ExpandAgentMemorySourceResult`

**Enum converter base class and shared converters** must also be migrated. All enum converters share a public generic base class `AgentMemoryToolEnumConverter<T>`. If the base class stays in `Tools.Abstractions`, Projection converters would create a reverse dependency on Tools. Therefore:

Move and TypeForward:
- `AgentMemoryToolEnumConverter<T>` (public abstract base class)
- `AgentMemoryToolOperationStatusJsonConverter`
- `AgentMemoryToolKindJsonConverter`
- `AgentMemoryToolConfidenceJsonConverter`
- `AgentMemoryToolSourceKindJsonConverter`
- `AgentMemoryToolMemoryStatusJsonConverter`
- `AgentMemoryToolDiagnosticSeverityJsonConverter`

Remain in `Agent.Memory.Tools.Abstractions` (write-specific):
- `AgentMemoryToolCandidateStatus`
- `AgentMemoryToolCandidateStatusJsonConverter` (inherits the forwarded base class — old assembly adds reference to Projection.Abstractions)

**`AgentMemoryToolJsonSerializerContext`** stays in `Agent.Memory.Tools.Abstractions`. It covers both write DTOs and forwarded shared DTOs — it cannot be wholly migrated to Projection because it includes write-only types. The context is NOT TypeForwarded.

`Agent.Memory.Tools.Abstractions` adds a reference to `Agent.Memory.Projection.Abstractions` and uses `[assembly: TypeForwardedTo(typeof(...))]` for each migrated type. This preserves binary compatibility for existing consumers.

#### 3.3.2 Types that CANNOT be TypeForwarded (public shape changes)

These types have `AgentMemoryToolPrincipal`, `AgentToolInvocationBindingSnapshot`, or `AgentMemoryToolAccessScope` in their public API. Changing the Principal type changes the CLR signature of properties and method parameters, breaking binary compatibility. These types get new names in `Projection.Abstractions`:

| Old type (stays in Tools.Abstractions) | New type (in Projection.Abstractions) |
|---|---|
| `AgentMemoryResourceHandle` | `AgentMemoryAccessResourceHandle` |
| `AgentMemorySourceGrant` | `AgentMemoryAccessSourceGrant` |
| `AgentMemoryPreparedSecurityArtifacts` | `AgentMemoryAccessPreparedArtifacts` |
| `IAgentMemorySecurityArtifactCoordinator` | `IAgentMemoryAccessArtifactCoordinator` |
| `IAgentMemoryResourceHandleResolver` | `IAgentMemoryAccessHandleResolver` |
| `IAgentMemorySourceGrantResolver` | `IAgentMemoryAccessGrantResolver` |
| `IAgentMemoryResourceHandleStore` | `IAgentMemoryAccessHandleStore` |
| `IAgentMemorySourceGrantStore` | `IAgentMemoryAccessGrantStore` |
| `AgentMemorySecurityArtifactBatchKey` | `AgentMemoryAccessArtifactBatchKey` |
| `AgentMemorySecurityArtifactBatchOriginKind` | `AgentMemoryAccessArtifactBatchOriginKind` |
| `AgentMemorySecurityArtifactState` | `AgentMemoryAccessArtifactState` |
| `AgentMemorySecurityArtifactKind` | `AgentMemoryAccessArtifactKind` |
| `AgentMemoryPreparedSecurityArtifact` | `AgentMemoryAccessPreparedArtifact` |
| `IAgentMemorySecurityArtifactBatchStore` | `IAgentMemoryAccessArtifactBatchStore` |
| `AgentMemoryResolvedResourceHandle` | `AgentMemoryAccessResolvedResource` |
| `AgentMemoryResourceHandleIssueResult` | `AgentMemoryAccessHandleIssueResult` |
| `AgentMemoryGrantIssueResult` | `AgentMemoryAccessGrantIssueResult` |

The old types remain in `Agent.Memory.Tools.Abstractions` unchanged. `Agent.Memory.Tools` contains adapter implementations that convert between old and new types (e.g., `AgentMemoryResourceHandle` ↔ `AgentMemoryAccessResourceHandle`). The adapters are internal to `Agent.Memory.Tools` — no consumer sees both types simultaneously unless they reference both assemblies.

This is an explicit breaking-change boundary: the projection-neutral path uses new types, the Agent Tool path uses existing types, and `Agent.Memory.Tools` bridges them.

#### 3.3.3 Single credential store — no dual store

There is exactly one authoritative credential store. The new `IAgentMemoryAccessHandleStore`, `IAgentMemoryAccessGrantStore`, and `IAgentMemoryAccessArtifactBatchStore` (in `Projection.Abstractions`) are the canonical store interfaces. The old `IAgentMemoryResourceHandleStore`, `IAgentMemorySourceGrantStore`, and `IAgentMemorySecurityArtifactBatchStore` (in `Tools.Abstractions`) become thin adapters that delegate to the new store:

```text
IAgentMemoryResourceHandleStore
  → AgentMemoryResourceHandleStoreAdapter
  → IAgentMemoryAccessHandleStore (canonical)

IAgentMemorySourceGrantStore
  → AgentMemorySourceGrantStoreAdapter
  → IAgentMemoryAccessGrantStore (canonical)

IAgentMemorySecurityArtifactCoordinator
  → AgentMemorySecurityArtifactCoordinatorAdapter
  → IAgentMemoryAccessArtifactCoordinator (canonical)
```

`AddAgentMemoryTools()` no longer registers independent old Store implementations. It registers the canonical store (via `AddAgentMemoryProjectionSecurity()`) and then registers the adapters for backward compatibility. This ensures:

- New ReadCore-issued handles (Agent Tool caller) are visible to old Supersede/Compress/Extract handlers via adapters
- Old Compress-issued context handles are visible to new ReadCore via the same canonical store
- Revoke, expiry, and quota state is consistent across old and new interfaces

**Old Store Adapter filtering**: The old `AgentMemoryResourceHandle` type can only express `AgentMemoryToolPrincipal`, not MCP Principal. Therefore, all old Store/Resolver/Coordinator adapters are **filtered views**:

- `CallerKind == AgentTool` → convert to old types and delegate to canonical store
- `CallerKind == Mcp` → old `Get`/`Resolve` interfaces return `null` (MCP handles are invisible to old Agent Tool interfaces)

The cross-interface test is scoped accordingly: "New ReadCore-issued Handle resolvable through old Agent Tool interfaces" applies only when ReadCore was called by an Agent Tool caller. MCP-issued handles are NOT convertible to old Agent Tool types.

**Complete adapter list** (all registered by `AddAgentMemoryTools()`):

| Old Interface | Adapter | Delegates To |
|---|---|---|
| `IAgentMemoryResourceHandleStore` | `AgentMemoryResourceHandleStoreAdapter` | `IAgentMemoryAccessHandleStore` |
| `IAgentMemorySourceGrantStore` | `AgentMemorySourceGrantStoreAdapter` | `IAgentMemoryAccessGrantStore` |
| `IAgentMemorySecurityArtifactBatchStore` | `AgentMemorySecurityArtifactBatchStoreAdapter` | `IAgentMemoryAccessArtifactBatchStore` |
| `IAgentMemoryResourceHandleResolver` | `AgentMemoryResourceHandleResolverAdapter` | `IAgentMemoryAccessHandleResolver` |
| `IAgentMemorySourceGrantResolver` | `AgentMemorySourceGrantResolverAdapter` | `IAgentMemoryAccessGrantResolver` |
| `IAgentMemorySecurityArtifactCoordinator` | `AgentMemorySecurityArtifactCoordinatorAdapter` | `IAgentMemoryAccessArtifactCoordinator` |

**Adapter filtering rules**: All old Store/Resolver/Coordinator adapters are **filtered views** — they only process artifacts belonging to `CallerKind.AgentTool`. This applies to ALL operations including `Revoke`:

- `CallerKind == AgentTool` → convert to old types and delegate to canonical store/resolver
- `CallerKind == Mcp` → old `Get`/`Resolve` interfaces return `null`; old `Revoke` interfaces are no-ops

For `RevokeAsync` in particular, the adapter must check the artifact's Principal before delegating:

```csharp
public async ValueTask RevokeAsync(string handleId, CancellationToken ct = default)
{
    var artifact = await _canonical.GetAsync(handleId, ct);
    if (artifact?.Principal.CallerKind != AgentMemoryCallerKind.AgentTool)
        return; // MCP artifacts are invisible to old Agent Tool interfaces
    await _canonical.RevokeAsync(handleId, ct);
}
```

More robustly, the canonical store can provide an atomic conditional revoke:

```csharp
ValueTask RevokeAsync(string artifactId, AgentMemoryCallerKind expectedCallerKind, CancellationToken ct);
```

This ensures "old interfaces cannot see MCP artifacts" AND "old interfaces cannot mutate MCP artifacts".

**TrustedHostOperation regression**: The existing `IAgentMemoryHistoryResourceHandleIssuer` uses `PrepareForHostAsync` with `AgentMemoryHostArtifactBatchKey`. The old `PrepareForHostAsync` adapter maps to `ArtifactOriginKind = TrustedHostOperation` — NOT to `McpSessionOperation`. Regression test: old `HistoryResourceHandleIssuer` → canonical Coordinator → canonical Store → `CompressAgentHistoryHandler` can resolve the handle.

#### 3.3.4 New types added directly to Projection.Abstractions

- `AgentMemoryCallerKind` enum
- `AgentMemoryArtifactOriginKind` enum
- `AgentMemoryAccessPrincipal` record
- `AgentMemoryArtifactOrigin` record
- `AgentMemoryAccessScope` record
- `IAgentMemoryAccessScopeProvider` interface
- `AgentMemoryReadCoreOutcome<T>` record
- `AgentMemorySecurityArtifactReceipt` record
- `IAgentMemoryContextHandleIssuer` interface
- `AgentMemoryContextHandleIssueResult` record
- `AgentMemoryAccessResolvedGrant` record
- `AgentMemoryAccessHandleIssueResult` record
- `AgentMemoryAccessGrantIssueResult` record
- `AgentMemoryArtifactCompensationToken` record
- `AgentMemoryArtifactBatchReceipt` record

### 3.4 Test assemblies

| Assembly | Location | Purpose |
|---|---|---|
| `CrestCreates.Agent.Memory.Projection.Tests` | `tests/Runtime/Agent/` | Unit tests for projection-neutral security infrastructure: Coordinator, Resolver, ScopeProvider, adapter conversions |
| `CrestCreates.Agent.Memory.ReadCore.Tests` | `tests/Runtime/Agent/` | Unit tests for shared read core: visibility, budget, tenant isolation, handle/grant issuance, sanitization |
| `CrestCreates.Mcp.Memory.Tests` | `tests/Integrations/` | Unit tests for MCP handlers: principal derivation, input mapping, result mapping, protocol contracts |
| `CrestCreates.Mcp.Memory.E2E.Tests` | `tests/Integrations/` | E2E: discovery, invocation, security enforcement, zero-write proof |

## 4. Shared read core design

### 4.1 Projection-neutral principal — two identity layers

The principal has two identity layers: a **stable security context** that controls grant access, and a **per-call invocation** that is recorded in the artifact origin for audit and per-invocation quota.

```csharp
public enum AgentMemoryCallerKind
{
    Unknown = 0,
    AgentTool = 1,
    Mcp = 2
}

public sealed record AgentMemoryAccessPrincipal
{
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public required AgentMemoryCallerKind CallerKind { get; init; }

    /// <summary>AgentId (AgentTool) or MCP HostId (Mcp).</summary>
    public required string CallerId { get; init; }

    /// <summary>
    /// Stable security context ID — controls who can use issued grants.
    /// Agent Tool: Agent ExecutionId (stable across tool calls within one execution).
    /// MCP: SessionId or host-provided stable SecurityContextId.
    /// Must NOT be a per-call InvocationId — grants must survive across tool calls.
    /// </summary>
    public required string SecurityContextId { get; init; }
}
```

**MCP handlers** derive this from `CapabilityExecutionContext.Items` (placed by `McpToolInvoker`):
- `TenantId` / `UserId` from `CapabilityExecutionContext`
- `CallerKind = Mcp`
- `CallerId` = `CapabilityExecutionContext.Items["HostId"]`
- `SecurityContextId` = `CapabilityExecutionContext.Items["SessionId"]`

**Unknown rejection**: `AgentMemoryCallerKind.Unknown` and `AgentMemoryArtifactOriginKind.Unknown` are fail-closed defaults. The Coordinator and Resolver entry points reject `Unknown` values — callers must explicitly declare their kind. This is consistent with the framework's fail-closed enum pattern.

**MCP SessionId requirement**: Memory MCP Tools require a non-null `SessionId`. If `SessionId` is null/empty, the handler returns unavailable. This prevents using per-call `InvocationId` as the security context (which would break cross-call grant reuse). Hosts that do not provide session identity cannot use Memory MCP tools — this is a deliberate security constraint, not a limitation to be worked around.

**Agent Tool handlers** derive it from their existing `AgentMemoryToolPrincipal`:
- `CallerKind = AgentTool`
- `CallerId = AgentId`
- `SecurityContextId = ExecutionId` (stable across tool calls within one agent execution)

### 4.2 Artifact origin — per-call invocation identity

The artifact origin captures the per-call invocation identity for audit, batch idempotency, and per-invocation quota. It is separate from the principal's security context.

```csharp
public enum AgentMemoryArtifactOriginKind
{
    Unknown = 0,
    AgentToolInvocation = 1,
    TrustedHostOperation = 2,
    McpInvocation = 3,
    McpSessionOperation = 4
}

public sealed record AgentMemoryArtifactOrigin
{
    public required AgentMemoryArtifactOriginKind Kind { get; init; }

    /// <summary>
    /// Pre-verified canonical binding hash computed from the full invocation snapshot.
    /// Agent Tool adapter: preserves existing agent-tool-origin-binding-v3 algorithm
    /// (TenantId, UserId, AgentId, ExecutionId, InvocationId, InvocationFingerprint).
    /// MCP adapter: binds TenantId, UserId, HostId, SecurityContextId,
    /// McpInvocationId, RequestId, ToolDescriptorId, ToolDescriptorVersion,
    /// CapabilityId, CapabilityVersion.
    /// McpSessionOperation: binds TenantId, UserId, HostId, SecurityContextId,
    /// SessionOperationId (included in hash to support multiple ContextHandle
    /// issuances within the same session).
    /// </summary>
    public required CanonicalHash BindingHash { get; init; }

    /// <summary>
    /// Per-operation identity for audit, batch idempotency, and per-operation quota.
    /// AgentToolInvocation → Tool InvocationId.
    /// McpInvocation → MCP InvocationId from CapabilityExecutionContext.Items["InvocationId"].
    /// TrustedHostOperation → HostOperationId.
    /// McpSessionOperation → Host-generated SessionOperationId (NOT a per-call InvocationId;
    /// session setup occurs before any tool invocation). Must be unique per ContextHandle
    /// issuance within the session, and must participate in BindingHash to prevent
    /// batch identity collisions when issuing multiple ContextHandles per session.
    /// </summary>
    public required string OperationId { get; init; }
}
```

**Semantic separation**:
- `Principal.SecurityContextId` → determines who can **use** issued grants (cross-call reuse within same session/execution)
- `ArtifactOrigin.OperationId` → records who **issued** the grant, for audit and per-operation quota

**Grant access control**: a grant is accessible when the resolver confirms **full Principal record equality** (`grant.Principal == current.Principal`). This includes all five fields: `TenantId`, `UserId`, `CallerKind`, `CallerId`, `SecurityContextId`. Partial-field comparison (e.g., only `SecurityContextId` + `CallerId` + `TenantId`) is prohibited — it would allow cross-User collisions when a Host reuses the same SessionId, or cross-CallerKind collisions when Agent Tool and MCP identity values overlap. The handle resolver uses the same full-Principal equality rule. This matches the existing #53 behavior where the old resolver compares complete `AgentMemoryToolPrincipal` instances.

The `OperationId` is NOT compared — grants issued in operation A are usable in operation B within the same security context. This matches #53's behavior where `IssuingOperationId` is recorded but not enforced for access control.

**Per-operation quota**: the handle/grant store tracks `IssuingOperationId` (from `ArtifactOrigin.OperationId`) and enforces `MaxResourceHandlesPerOperation` / `MaxGrantsPerOperation` per `OriginBindingHash`, not per `Principal`. This prevents a long-lived session from accumulating unlimited artifacts.

### 4.3 MCP session setup origin

`IAgentMemoryContextHandleIssuer` is called during MCP session setup, before any tool invocation. It uses `ArtifactOriginKind = McpSessionOperation` with a binding hash computed from session-level identity (TenantId, UserId, HostId, SecurityContextId, **SessionOperationId**) — not from a per-call invocation that doesn't exist yet. The `SessionOperationId` is included in the hash to support multiple ContextHandle issuances within the same session without batch identity collisions.

### 4.4 Interfaces

```csharp
// Memory recall (replaces read orchestration in BuildAgentMemoryPackHandler)
public interface IAgentMemoryReadCore
{
    ValueTask<AgentMemoryReadCoreOutcome<AgentMemoryReadResult>> RecallAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryReadRequest request,
        CancellationToken ct);
}

// Compressed context recall (new — for ctx_recall)
public interface IAgentContextReadCore
{
    ValueTask<AgentMemoryReadCoreOutcome<AgentContextReadResult>> RecallContextAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentContextReadRequest request,
        CancellationToken ct);
}

// Source expansion (replaces read orchestration in ExpandAgentMemorySourceHandler)
public interface IAgentMemorySourceExpandCore
{
    ValueTask<AgentMemoryReadCoreOutcome<AgentMemorySourceExpandResult>> ExpandAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemorySourceExpandRequest request,
        CancellationToken ct);
}
```

All three interfaces accept `AgentMemoryArtifactOrigin` — the caller is responsible for constructing the origin with a verified binding hash before calling the read core.

### 4.5 ReadCore outcome — security facts returned to caller

The read core returns an outcome wrapper that includes the security facts from the single execution, avoiding TOCTOU and duplicate scope resolution:

```csharp
public sealed record AgentMemoryReadCoreOutcome<T>
{
    public required T Result { get; init; }
    public required string ScopeFingerprint { get; init; }
    public required int MaximumAuditFacts { get; init; }
    public required AgentMemorySecurityArtifactReceipt ArtifactReceipt { get; init; }

    /// <summary>
    /// Present only when this execution created security artifacts.
    /// Allows the caller (Agent Tool adapter or MCP handler) to revoke
    /// artifacts if a subsequent step (governance wrapping, result mapping)
    /// fails after ReadCore has already prepared credentials.
    /// Never serialized into protocol output.
    /// </summary>
    public AgentMemoryArtifactCompensationToken? CompensationToken { get; init; }
}

public sealed record AgentMemorySecurityArtifactReceipt
{
    public required AgentMemoryArtifactBatchReceipt? HandleBatch { get; init; }
    public required AgentMemoryArtifactBatchReceipt? GrantBatch { get; init; }
}

public sealed record AgentMemoryArtifactBatchReceipt
{
    public required string BatchHash { get; init; }
    public required int Count { get; init; }
    public required bool ReusedExisting { get; init; }
}
```

The outcome returns only opaque IDs, batch hashes, and batch-level counts — never complete `AgentMemoryAccessResourceHandle` or `AgentMemoryAccessSourceGrant` domain objects. The receipt is split into separate Handle and Grant batch receipts, correctly expressing states like "Handle batch reused, Grant batch newly created". Protocol adapters (Agent Tool or MCP) consume the receipt for audit and governance wrapping without seeing internal security details.

### 4.6 Request/response models

**AgentMemoryReadRequest** (memory recall):
- `MemoryHandles: IReadOnlyList<string>` — opaque handle IDs to resolve
- `Kinds: IReadOnlyList<AgentMemoryToolKind>` — filter by kind
- `Tags: IReadOnlyList<string>` — filter by tags
- `MaximumCount: int` — budget (must be > 0, enforced before store access)
- `CharacterBudget: int` — budget (must be > 0, enforced before store access)
- `MinimumConfidence: AgentMemoryToolConfidence`

**AgentMemoryReadResult** (memory recall):
- `OperationStatus: AgentMemoryToolOperationStatus`
- `Items: IReadOnlyList<AgentMemoryToolItemDto>` — sanitized, visibility-filtered
- `ReturnedCount: int`
- `WasTruncated: bool`
- `IsAuthoritative: bool` (always false)
- `Diagnostics: IReadOnlyList<AgentMemoryToolDiagnosticDto>`

**AgentContextReadRequest** (compressed context recall):
- `ContextHandle: string` — opaque handle to resolve
- `MaximumBlockCount: int` — budget
- `CharacterBudget: int` — total character budget across all blocks (blocks consumed in stable order until budget exhausted)
- `StartBlockIndex: int?` — optional inclusive start (must be ≥ 0)
- `EndBlockIndexExclusive: int?` — optional exclusive end (must be > StartBlockIndex)

**AgentContextReadResult** (compressed context recall):
- `OperationStatus: AgentMemoryToolOperationStatus`
- `Blocks: IReadOnlyList<AgentMemoryToolBlockDto>` — sanitized (each block carries its own SourceGrants)
- `BlockCount: int`
- `WasTruncated: bool`
- `Diagnostics: IReadOnlyList<AgentMemoryToolDiagnosticDto>`

No top-level `SourceGrants` — each `AgentMemoryToolBlockDto` already carries its own source grants, avoiding duplication and inconsistency.

**AgentMemorySourceExpandRequest/Result** — mirrors existing `ExpandAgentMemorySourceInput/Result` semantics but uses `AgentMemoryAccessPrincipal` instead of `AgentMemoryToolPrincipal`.

### 4.7 Required upgrades to existing interfaces

New projection-neutral interfaces are defined in `Agent.Memory.Projection.Abstractions` with new names (see §3.3.2). The implementations in `Agent.Memory.Projection` satisfy these new interfaces. The old interfaces in `Agent.Memory.Tools.Abstractions` remain unchanged — `Agent.Memory.Tools` contains internal adapters.

**IAgentMemoryAccessArtifactCoordinator** (new, in Projection.Abstractions):

```csharp
public interface IAgentMemoryAccessArtifactCoordinator
{
    ValueTask<AgentMemoryAccessPreparedArtifacts> PrepareAsync(
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        string purpose,
        int ordinal,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants,
        CancellationToken ct = default);

    /// <summary>
    /// Revoke all artifacts created in a PrepareAsync call.
    /// Used when ReadCore, governance wrapping, or result mapping fails
    /// after credentials have been prepared.
    /// </summary>
    ValueTask RevokeCreatedAsync(
        AgentMemoryArtifactCompensationToken token,
        CancellationToken ct = default);
}
```

`PrepareAsync` returns `AgentMemoryAccessPreparedArtifacts` which includes an `AgentMemoryArtifactCompensationToken`. The compensationToken is an opaque handle that allows the caller to revoke all artifacts created in that batch without seeing the full Handle/Grant domain objects. This preserves the Coordinator's role as the single preparation and compensation boundary.

**Failure compensation rules**:

| Failure point | Compensation |
|---|---|
| ReadCore internal exception after Prepare | Immediate `RevokeCreatedAsync` |
| Agent Tool governance/preflight failure after ReadCore returns | Immediate `RevokeCreatedAsync` |
| MCP Handler result mapping failure | Immediate `RevokeCreatedAsync` |
| MCP Invoker serialization failure after Handler succeeds | Short-lived expiry (provisional artifact policy) |

The compensationToken is NOT the same as the `ArtifactReceipt` — the receipt contains opaque IDs and counts for audit; the compensationToken is a revocation capability that is consumed by `RevokeCreatedAsync` and then invalidated.

**CompensationToken lifecycle semantics**:

- **All batches reused** → `CompensationToken = null` (nothing to revoke)
- **Partial reuse, partial newly created** → Token covers only the newly created artifacts; reused batches are not affected by revocation
- **RevokeCreatedAsync** → one-shot, idempotent: calling it multiple times does not expand the revocation scope. After the first successful call, the token is invalidated and subsequent calls are no-ops
- **Normal success** → Token is not consumed and does not form permanent state leakage. The token is a short-lived, non-forgeable reference to the batch's internal identity — it does not persist beyond the Coordinator's tracking window

**IAgentMemoryAccessHandleResolver** (new, in Projection.Abstractions):

```csharp
public interface IAgentMemoryAccessHandleResolver
{
    ValueTask<AgentMemoryAccessResolvedResource?> ResolveAsync(
        string handleId,
        AgentMemoryResourceKind expectedKind,
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        CancellationToken ct = default);
}
```

**IAgentMemoryAccessGrantResolver** (new, in Projection.Abstractions):

```csharp
public interface IAgentMemoryAccessGrantResolver
{
    ValueTask<AgentMemoryAccessResolvedGrant?> ResolveAsync(
        string grantId,
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        CancellationToken ct = default);
}
```

**IAgentMemoryAccessScopeProvider** (new, in Projection.Abstractions):

```csharp
public interface IAgentMemoryAccessScopeProvider
{
    ValueTask<AgentMemoryAccessScope> ResolveAsync(
        AgentMemoryAccessPrincipal principal,
        CancellationToken ct = default);
}
```

`AddAgentMemoryProjectionSecurity()` does NOT register a default `IAgentMemoryAccessScopeProvider` — the Host must register an authorization policy implementation. A deny-all fallback is available as `DenyAllAgentMemoryAccessScopeProvider` for testing, but it must be explicitly opted into.

**Scope provider capabilities**: To enable startup validation without implementation-type sniffing, the scope provider declares its supported caller kinds:

```csharp
public interface IAgentMemoryAccessScopeProviderCapabilities
{
    bool Supports(AgentMemoryCallerKind callerKind);
}
```

`AddMcpMemoryTools()` startup gate requires:
1. `IAgentMemoryAccessScopeProvider` is registered
2. The provider implements `IAgentMemoryAccessScopeProviderCapabilities`
3. `Supports(AgentMemoryCallerKind.Mcp)` returns `true`

If the provider does not support `CallerKind.Mcp` (e.g., it is the `LegacyAgentMemoryAccessScopeProviderAdapter` which only supports `AgentTool`), the startup fails with a clear diagnostic — MCP Memory tools cannot run under an AgentTool-only scope policy.

**Backward compatibility for `AddAgentMemoryTools()`**: existing Hosts typically register only the old `IAgentMemoryToolAccessScopeProvider`. `AddAgentMemoryTools()` registers a `LegacyAgentMemoryAccessScopeProviderAdapter` via `TryAddSingleton` that wraps the old provider, but only supports `CallerKind.AgentTool`. Using `TryAddSingleton` ensures that if the Host has already registered a MCP-capable `IAgentMemoryAccessScopeProvider`, the legacy adapter does not override it. `AddMcpMemoryTools()` adds a startup validation: the resolved `IAgentMemoryAccessScopeProvider` must support `CallerKind.Mcp` — it cannot be the AgentTool-only legacy adapter. This prevents MCP from silently running under an Agent Tool-only scope policy.

**Legacy Scope budget field mapping**: The old `AgentMemoryToolAccessScope` does not have `MaxContextRecallCharacters`. The `LegacyAgentMemoryAccessScopeProviderAdapter` maps it as:

```csharp
MaxContextRecallCharacters = old.MaxRecallCharacters
```

This is the correct mapping because `MaxRecallCharacters` is the existing total character budget for memory recall, and context recall character budget should use the same limit. The alternative of computing `MaxCompressedBlockCount * MaxCompressedBlockCharacters` would over-estimate the budget (blocks may not all be full-length).

**Scope fingerprint stability**: The current `memory-scope-v2` fingerprint shape includes only Tenant, AllowUnscoped, and VisibleDescriptorRefs. The new projection-neutral `AgentMemoryAccessScope` preserves this fingerprint shape exactly. `CallerId` and `SecurityContextId` are validated through Principal equality separately — they are NOT folded into the fingerprint. This ensures existing durable Handle/Grant entries remain valid after the refactoring.

### 4.8 Context handle issuance — through Coordinator, self-resolving Scope

```csharp
public interface IAgentMemoryContextHandleIssuer
{
    /// <summary>
    /// Issues a ContextHandle for a trusted context ID. The trustedContextId
    /// must come from Host code, never from MCP parameters.
    /// Routes through IAgentMemoryAccessArtifactCoordinator.PrepareAsync
    /// — never calls IAgentMemoryAccessHandleStore directly.
    /// Internally resolves scope via IAgentMemoryAccessScopeProvider,
    /// freezing the ScopeFingerprint into the handle.
    /// On failure, calls RevokeCreatedAsync via compensationToken.
    /// </summary>
    ValueTask<AgentMemoryContextHandleIssueResult> IssueForCallerAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        string trustedContextId,
        CancellationToken ct = default);
}

public sealed record AgentMemoryContextHandleIssueResult
{
    public required string HandleId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
```

The implementation in `Agent.Memory.Projection`:
1. Resolves scope via `IAgentMemoryAccessScopeProvider.ResolveAsync(principal, ct)` — no Host-supplied scope, avoiding TOCTOU
2. Loads the trusted context from `IAgentCompressedContextStore`
3. Validates tenant isolation
4. Computes effective descriptor refs
5. Validates closed-world scope
6. Constructs a handle plan
7. Routes through `IAgentMemoryAccessArtifactCoordinator.PrepareAsync()` — the single preparation boundary
8. On failure, calls `RevokeCreatedAsync(compensationToken, ct)` — immediate compensation
9. Returns only the opaque `HandleId` and `ExpiresAt`

The host application calls this during MCP session setup using `ArtifactOriginKind = McpSessionOperation`. MCP clients receive handles through application-specific out-of-band channels.

### 4.9 Implementation responsibilities

The `DefaultAgentMemoryReadCore` implementation:

1. Resolves scope via `IAgentMemoryAccessScopeProvider.ResolveAsync(principal, ct)` (projection-neutral)
2. Validates budget (fail-closed: count/characters ≤ scope limits, > 0)
3. Resolves opaque handles via `IAgentMemoryAccessHandleResolver.ResolveAsync(handleId, kind, principal, scope, ct)` (projection-neutral)
4. Calls `IAgentMemoryRetriever.RecallAsync` / `IAgentCompressedContextStore` / `IAgentContextSourceExpander`
5. Enforces tenant isolation (returned data must match `principal.TenantId`)
6. Enforces closed-world descriptor visibility
7. Prepares security artifacts via `IAgentMemoryAccessArtifactCoordinator.PrepareAsync(origin, principal, scope, purpose, ordinal, handles, grants, ct)` (projection-neutral). On failure, calls `RevokeCreatedAsync(compensationToken, ct)`.
8. Projects to tool-safe DTOs (`IsAuthoritative = false`, canonical hash, sanitized content)
9. Returns `AgentMemoryReadCoreOutcome<T>` with `ArtifactReceipt` containing only opaque IDs and counts

### 4.10 What the read core does NOT own

- Agent Tool preflight receipts (`IAgentToolOutputPreflightReceiptSink`)
- Agent Tool audit fact buffering (`IAgentToolInvocationFactSink`)
- Agent Tool invocation binding (`AgentToolInvocationBindingSnapshot`)
- MCP protocol result mapping
- Any write operations (promote, reject, supersede, compress, extract)
- Direct access to `IAgentMemoryAccessHandleStore` or `IAgentMemoryAccessGrantStore` — all handle/grant issuance routes through `IAgentMemoryAccessArtifactCoordinator`
- Compensation — ReadCore calls `RevokeCreatedAsync` on internal failure; Agent Tool/MCP adapters call it on governance/mapping failure

## 5. MCP Memory capabilities and tool specifications

### 5.1 Three capabilities, four MCP tools

| MCP Tool | Capability ID | Handler | Read Core Method |
|---|---|---|---|
| `ctx_recall` | `mcp-memory:agent.context.recall` | `McpRecallAgentContextHandler` | `IAgentContextReadCore.RecallContextAsync` |
| `ctx_expand` | `mcp-memory:agent.source.expand` | `McpExpandAgentSourceHandler` | `IAgentMemorySourceExpandCore.ExpandAsync` |
| `memory_recall` | `mcp-memory:agent.memory.recall` | `McpRecallAgentMemoryHandler` | `IAgentMemoryReadCore.RecallAsync` |
| `memory_source_expand` | `mcp-memory:agent.source.expand` | `McpExpandAgentSourceHandler` | `IAgentMemorySourceExpandCore.ExpandAsync` |

`ctx_expand` and `memory_source_expand` share one Capability (`mcp-memory:agent.source.expand`) and one Handler (`McpExpandAgentSourceHandler`). They differ only in MCP Tool Spec metadata (name, title, description). Since a Capability ID maps to exactly one Handler, this requires distinct `DescriptorId` values to avoid identity collision:

- `ctx_expand` → `DescriptorId = "mcp-tool:agent.context.expand-source"`
- `memory_source_expand` → `DescriptorId = "mcp-tool:agent.memory.expand-source"`

All three Capabilities are `CapabilityKind.Query`, which causes `ReadOnlyHint = true` automatically.

### 5.2 MCP Tool Spec declarations

```csharp
[McpToolSpecs]
public static partial class McpMemoryToolSpecifications
{
    [McpToolSpec(
        "mcp-memory:agent.context.recall",
        DescriptorId = "mcp-tool:agent.context-recall",
        CapabilityVersion = 1,
        InputType = typeof(RecallAgentContextInput),
        OutputType = typeof(RecallAgentContextResult),
        ToolName = "ctx_recall",
        Title = "Recall compressed agent context",
        Description = "Recalls bounded, sanitized blocks from a compressed agent context.",
        DestructiveHint = McpBooleanHint.False,
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class RecallContext;

    [McpToolSpec(
        "mcp-memory:agent.source.expand",
        DescriptorId = "mcp-tool:agent.context.expand-source",
        CapabilityVersion = 1,
        InputType = typeof(ExpandAgentMemorySourceInput),
        OutputType = typeof(ExpandAgentMemorySourceResult),
        ToolName = "ctx_expand",
        Title = "Expand agent context source",
        Description = "Expands one governed context source grant into sanitized content.",
        DestructiveHint = McpBooleanHint.False,
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class ExpandContextSource;

    [McpToolSpec(
        "mcp-memory:agent.memory.recall",
        DescriptorId = "mcp-tool:agent.memory-recall",
        CapabilityVersion = 1,
        InputType = typeof(BuildAgentMemoryPackInput),
        OutputType = typeof(BuildAgentMemoryPackResult),
        ToolName = "memory_recall",
        Title = "Recall agent memory",
        Description = "Recalls bounded, visibility-filtered, sanitized memory items.",
        DestructiveHint = McpBooleanHint.False,
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class RecallMemory;

    [McpToolSpec(
        "mcp-memory:agent.source.expand",
        DescriptorId = "mcp-tool:agent.memory.expand-source",
        CapabilityVersion = 1,
        InputType = typeof(ExpandAgentMemorySourceInput),
        OutputType = typeof(ExpandAgentMemorySourceResult),
        ToolName = "memory_source_expand",
        Title = "Expand agent memory source",
        Description = "Expands one governed memory source grant into sanitized content.",
        DestructiveHint = McpBooleanHint.False,
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class ExpandMemorySource;
}
```

No `ReadOnlyHint` — derived from `CapabilityKind.Query`. Each spec has `InputType` and `OutputType` for strong CLR binding. Two expand specs target the same Capability ID but with distinct `DescriptorId` values.

### 5.3 Handler pattern

Each MCP handler implements `ICapabilityHandler<TInput, TOutput>`:

```csharp
public Task<TOutput> ExecuteAsync(TInput input, CancellationToken ct)
```

The handler:
1. Derives `AgentMemoryAccessPrincipal` from `ICapabilityExecutionContextAccessor`:
   - `TenantId` / `UserId` from `CapabilityExecutionContext`
   - `CallerKind = Mcp`
   - `CallerId` from `CapabilityExecutionContext.Items["HostId"]`
   - `SecurityContextId` from `CapabilityExecutionContext.Items["SessionId"]`
   - **If `SessionId` is null/empty → return unavailable** (Memory MCP Tools require session identity)
2. Constructs `AgentMemoryArtifactOrigin` with `Kind = McpInvocation`:
   - `BindingHash` computed from all MCP invocation identity fields (TenantId, UserId, HostId, SecurityContextId, McpInvocationId, RequestId, ToolDescriptorId, ToolDescriptorVersion, CapabilityId, CapabilityVersion)
    - `OperationId` from `CapabilityExecutionContext.Items["InvocationId"]`
3. Maps input DTO → ReadCore request
4. Delegates to the shared read core
5. Retains `ArtifactReceipt` from the outcome for diagnostics/future audit extension; no additional audit facts are emitted in this phase
6. Maps ReadCore result → output DTO
7. Returns the output

Handlers do NOT touch `IAgentExecutionContextAccessor`, `IAgentToolOutputPreflightReceiptSink`, `IAgentToolInvocationFactSink`, or `AgentToolInvocationBindingSnapshot`.

Handler registration is via `ICapabilityHandlerModule` (see §8.5).

### 5.4 Input/Output DTOs

MCP handlers use DTOs from `Agent.Memory.Projection.Abstractions` (migrated from Tools.Abstractions — see §3.3):

- `BuildAgentMemoryPackInput` / `BuildAgentMemoryPackResult` → for `memory_recall`
- `ExpandAgentMemorySourceInput` / `ExpandAgentMemorySourceResult` → for both expand tools
- New `RecallAgentContextInput` / `RecallAgentContextResult` → for `ctx_recall` (added to `Agent.Memory.Projection.Abstractions`)

### 5.5 Credential lifecycle — output serialization failure

MCP handlers issue handles/grants inside the read core, then return. If output serialization or Schema validation fails in `McpToolInvoker` after the handler succeeds:

- The Capability has already succeeded
- Handles/grants have already been prepared
- The MCP client does not receive the credentials

**Chosen policy**: provisional artifact + short expiry. Security artifacts prepared during MCP handler execution use a short-lived expiry. The actual lifetime is `Min(scope.Lifetime, projectionPolicy.Lifetime)` where the projection policy is configured via `IAgentMemoryArtifactLifetimePolicy`:

```csharp
public interface IAgentMemoryArtifactLifetimePolicy
{
    TimeSpan GetHandleLifetime(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string purpose);

    TimeSpan GetGrantLifetime(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string purpose);
}
```

The policy is origin-aware because different origin kinds have fundamentally different lifetime requirements:

| Origin Kind | Handle Lifetime | Grant Lifetime |
|---|---|---|
| `AgentToolInvocation` | `scope.ResourceHandleLifetime` (existing behavior) | `scope.ExpansionGrantLifetime` (existing behavior) |
| `TrustedHostOperation` | `scope.ResourceHandleLifetime` (existing behavior) | N/A |
| `McpInvocation` | `Min(scope.ResourceHandleLifetime, configured provisional cap)` | `Min(scope.ExpansionGrantLifetime, configured provisional cap)` |
| `McpSessionOperation` | `Min(scope.ResourceHandleLifetime, MCP session lifetime cap)` | N/A |

The default implementation in `Agent.Memory.Projection` returns:
- AgentToolInvocation / TrustedHostOperation → scope-provided lifetimes (preserves existing behavior exactly)
- McpInvocation → 60-second provisional cap (short-lived recall credentials)
- McpSessionOperation → session-lifetime cap from configuration (ContextHandles must survive the full session, not just 60 seconds)

**Critical**: The 60-second default must NOT apply to `McpSessionOperation` ContextHandles. A Session setup handle that expires in 60 seconds would make `ctx_recall` unusable after any brief pause.

**Expired batch reprepare**: if the same `OriginBindingHash` retries after expiry, the store throws `IdentityConflict`. Clients must use a new `OperationId` (new origin binding). The store does not support expired-batch reprepare — this is by design to prevent indefinite credential accumulation.

**E2E test**: verify that artifacts prepared during a handler execution that subsequently fails serialization are not usable after their expiry window.

## 6. Security and governance boundaries

### 6.1 Parameter restrictions (MCP input must never receive)

- `TenantId` or `UserId` — derived from `CapabilityExecutionContext`
- Raw Memory/Context/Block ID — only opaque handles
- Complete `AgentContextSourceRef` — only opaque grant IDs
- Descriptor visibility boundary — resolved by `IAgentMemoryAccessScopeProvider`
- Internal permission information — handled by Capability Pipeline authorization
- `CallerId` / `SecurityContextId` / `HostId` / `SessionId` — derived from execution context, not from parameters

### 6.2 Unified unavailable response

All failure modes return the same unavailable result shape — callers cannot distinguish between:

- Cross-tenant access attempts
- Denied visibility
- Expired/revoked grants
- Forged handles
- Non-existent resources
- Foreign-security-context grants (different `SecurityContextId`)
- Foreign-host grants (different `CallerId`)
- Missing session identity (null `SessionId`)

This prevents information leakage about resource or descriptor existence.

### 6.3 Tenant isolation

The shared read core enforces tenant isolation after returning data from stores:

- `pack.TenantId` must match `principal.TenantId`
- Mismatch → unavailable (indistinguishable from any other unavailable case)

### 6.4 Closed-world descriptor visibility

The shared read core enforces closed-world visibility:

- `scope.VisibleDescriptorRefs` defines the complete visible set
- Memory items with descriptor refs not in the visible set → unavailable
- Memory items with zero descriptor refs (unscoped) → visible only if `scope.AllowUnscopedMemory` is true
- Version > 0 check: descriptor refs with version ≤ 0 → unavailable

### 6.5 Budget enforcement (fail-closed before store access)

- `MaximumCount ≤ 0` or `CharacterBudget ≤ 0` → unavailable ("budget-invalid")
- `MaximumCount > scope.MaxRecallCount` → unavailable
- `CharacterBudget > scope.MaxRecallCharacters` → unavailable
- Expansion: `MaximumCharacters ≤ 0` or `> scope.MaxExpansionCharacters` → unavailable
- Context recall: `MaximumBlockCount ≤ 0` or `> scope.MaxCompressedBlockCount` → unavailable
- Context recall: `CharacterBudget ≤ 0` or `> scope.MaxContextRecallCharacters` → unavailable
- Context recall: `StartBlockIndex < 0` → unavailable
- Context recall: `EndBlockIndexExclusive ≤ StartBlockIndex` (when both specified) → unavailable
- Context recall: `EndBlockIndexExclusive - StartBlockIndex > scope.MaxCompressedBlockCount` → unavailable

All budget checks occur before any store access.

### 6.6 Authoritative invariant

- All recalled memory has `IsAuthoritative = false` — no exceptions
- No secondary authority flag
- Expansion content is always sanitized (via `IAgentMemoryContentSanitizer`)

### 6.7 Audit

MCP capabilities are audited through the Capability Pipeline's `AuditMiddleware` — no separate MCP-Memory audit store. The current `CapabilityExecutionRecord` contains only generic fields (CapabilityId, TenantId, UserId, Source, Success, ErrorCode, Duration, Timestamp). It does not support count/hash facts.

**This phase uses only existing generic audit**. Count/hash fact audit is deferred to a future phase that adds `CapabilityAuditFacts` sidecar support to the Capability Pipeline middleware. The spec does not claim count/hash audit that does not exist.

### 6.8 Zero domain-state write

E2E must prove zero writes to:

- Memory Store (no candidate/memory writes)
- CompressedContext Store (no context/block writes)
- Conversation/Task Store (no writes)
- Descriptor Store (no writes)
- Workflow Store (no writes)

**Allowed side effects by tool type**:

| Tool | Security Artifact Store writes | Notes |
|---|---|---|
| `memory_recall` / `ctx_recall` | Handle + Grant issuance allowed | Credentials needed for expansion follow-up |
| `ctx_expand` / `memory_source_expand` | **Zero writes** | Expand only consumes existing grants, does not issue new ones. Store reads must not mutate state (see §6.8.1). |

If absolute zero-write is required for recall tools, the existing grant system must support stateless signed credentials; #54 does not create a second grant mechanism.

#### 6.8.1 Store read purification — no lazy expiry state mutation

The current Grant Store and Handle Store perform lazy state mutation on read: when `GetAsync` encounters an artifact whose `State == Active` but `ExpiresAt <= now`, it writes `State = Expired` back to the store. This creates a conflict with the expand tools' zero-write requirement: reading an expired grant during `ctx_expand` would trigger a store write.

**Chosen approach: Read purification (Method A)**. The canonical store's `GetAsync` returns an effective state view without persisting the transition:

```csharp
// Internal logic in canonical store GetAsync:
if (artifact.State == AgentMemoryAccessArtifactState.Active && artifact.ExpiresAt <= now)
    return artifact with { State = AgentMemoryAccessArtifactState.Expired };
// Return artifact as-is — no write-back to store
```

Actual expired-state persistence is handled by an independent cleanup/retention mechanism, not by read paths. This ensures:

- Expand tools achieve true zero security artifact store writes
- Recall tools' credential issuance is the only write path
- Read behavior is observationally identical (expired artifacts still appear expired)
- Durable providers are responsible for bounded retention and expiry cleanup; the development in-memory store may retain expired entries until disposal

**E2E test**: verify that reading an expired grant during expand produces zero store writes, and the grant appears as expired in the result.

### 6.9 Grant replay semantics

The current grant resolver does not consume grants on use — the same grant can be used multiple times within its scope and expiry. The spec does not require single-use grants.

**Test language**:

- Expired grant → unavailable
- Revoked grant → unavailable
- Forged grant ID → unavailable
- Foreign-host grant (different `CallerId`) → unavailable
- Foreign-security-context grant (different `SecurityContextId`) → unavailable
- Scope-stale grant (scope fingerprint mismatch) → unavailable
- Same-security-context, later-invocation grant → **allowed** (this is the normal cross-call flow)

### 6.10 ContextHandle issuance entry point

`ctx_recall` requires a `ContextHandle` but no MCP tool produces one. The `IAgentMemoryContextHandleIssuer` interface (§4.8) provides the trusted entry point. Host code calls it during MCP session setup using `ArtifactOriginKind = McpSessionOperation`, not through MCP tool invocation. The issuer self-resolves scope and routes through `IAgentMemoryAccessArtifactCoordinator.PrepareAsync` — never calls `IAgentMemoryAccessHandleStore` directly.

### 6.11 Permissions and risk levels

Each MCP Memory capability declares a fixed permission and risk level:

| Capability ID | Permission | Risk Level | Kind |
|---|---|---|---|
| `mcp-memory:agent.context.recall` | `Crest.AgentMemory.Context.Recall` | Low | Query |
| `mcp-memory:agent.memory.recall` | `Crest.AgentMemory.Memory.Recall` | Low | Query |
| `mcp-memory:agent.source.expand` | `Crest.AgentMemory.Source.Expand` | Low | Query |

### 6.12 Two visibility layers

MCP Memory tools have two distinct visibility controls:

1. **MCP Tool descriptor exposure**: controlled by `IMcpToolExposurePolicy`. When denied, the tool does not appear in discovery and invocation appears as "unknown tool". This is the same mechanism used by all MCP tools.

2. **Memory content descriptor visibility**: controlled by `AgentMemoryAccessScope.VisibleDescriptorRefs`. When denied, the result is unified unavailable — indistinguishable from any other unavailable case. This is the closed-world visibility enforced by the shared read core.

## 7. Agent Tool handler refactoring

### 7.1 BuildAgentMemoryPackHandler → delegates to IAgentMemoryReadCore

**Before**: Handler directly owns visibility filtering, budget enforcement, handle/grant issuance, sanitization/hash projection, store queries.

**After**: Handler delegates to `IAgentMemoryReadCore.RecallAsync`, then:

1. Extracts `AgentMemoryReadCoreOutcome<AgentMemoryReadResult>` from the read core
2. Uses `outcome.ScopeFingerprint` and `outcome.MaximumAuditFacts` for governance wrapping (no re-resolution of scope — avoids TOCTOU)
3. Uses `outcome.ArtifactReceipt` for audit facts (issued handle/grant counts, batch hash)
4. Adds Agent Tool governance wrapping:
   - `AddBranchInvariantFacts(scopeFingerprint, maximumAuditFacts, operation, receipt)` — new overload accepting pre-resolved security facts, not a full Scope object
   - `PublishAllowedOutcomes(...)` — preflight receipts
5. Converts projection-neutral result types back to Agent Tool types via internal adapters
6. Returns existing `BuildAgentMemoryPackResult`

Handler still requires `IAgentExecutionContextAccessor` to build `AgentMemoryAccessPrincipal` (which maps from `AgentMemoryToolPrincipal` with CallerKind = AgentTool, SecurityContextId = ExecutionId).

### 7.2 ExpandAgentMemorySourceHandler → delegates to IAgentMemorySourceExpandCore

**Before**: Handler owns scope resolution, grant resolution, expansion, budget enforcement, result mapping.

**After**: Handler delegates to `IAgentMemorySourceExpandCore.ExpandAsync`, then:

1. Uses the outcome's security facts for governance wrapping
2. Converts projection-neutral result types back to Agent Tool types via internal adapters
3. Returns existing `ExpandAgentMemorySourceResult`

### 7.3 Unchanged handlers

The five write handlers (CompressHistory, ExtractCandidates, PromoteCandidate, RejectCandidate, SupersedeItem) are NOT refactored — they do not participate in the read-only shared core.

### 7.4 AgentMemoryToolHandlerBase changes

The base class stays in `Agent.Memory.Tools`. It still owns:

- `Principal` property (builds from `ICapabilityExecutionContextAccessor` + `IAgentExecutionContextAccessor`)
- `AddBranchInvariantFacts` (new overload added — see below), `PublishAllowedOutcomes`, `PrepareOutput`
- `IsValidScope`, `IsTrustedSourceRefSubset`

Added helper method mapping `AgentMemoryToolPrincipal` → `AgentMemoryAccessPrincipal`:

```csharp
protected AgentMemoryAccessPrincipal ToAccessPrincipal() => new()
{
    TenantId = Principal.TenantId,
    UserId = Principal.UserId,
    CallerKind = AgentMemoryCallerKind.AgentTool,
    CallerId = Principal.AgentId,
    SecurityContextId = Principal.ExecutionId
};
```

Added helper method constructing `AgentMemoryArtifactOrigin` from Agent Tool binding:

```csharp
protected AgentMemoryArtifactOrigin ToArtifactOrigin()
{
    // Preserves existing agent-tool-origin-binding-v3 hash algorithm
    var bindingHash = ComputeAgentToolOriginBindingHash(InvocationBinding, Principal);
    return new AgentMemoryArtifactOrigin
    {
        Kind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
        BindingHash = bindingHash,
        OperationId = InvocationBinding.LogicalKey.InvocationId
    };
}
```

Added new `AddBranchInvariantFacts` overload:

```csharp
protected void AddBranchInvariantFacts(
    string scopeFingerprint,
    int maximumAuditFacts,
    string operation,
    AgentMemorySecurityArtifactReceipt receipt);
```

This overload accepts pre-resolved security facts from the read core outcome, avoiding re-resolution of scope. The existing overload accepting `AgentMemoryToolAccessScope` is preserved for write handlers that still resolve scope directly.

**Audit fact allowlist**: before writing new handle/grant count and batch hash facts, confirm that #53's audit contract allowlist accepts these. If not, this phase only writes the existing fact types and defers receipt-derived facts to a future phase.

### 7.5 Risk assessment

- **Limited scope**: only 2 of 7 handlers are refactored
- **Behavior preservation**: shared read core produces identical output to current handler logic; Agent Tool handlers only add governance wrapping
- **Testable**: shared read core has its own unit tests; existing Agent Tool integration tests continue to pass
- **No new dependencies**: `Agent.Memory.Tools` already references `Agent.Memory.Abstractions` and `Agent.Memory.Tools.Abstractions`; it now also references `Agent.Memory.ReadCore`, `Agent.Memory.Projection.Abstractions`, and `Agent.Memory.Projection`

## 8. DI composition, JSON contract contribution, Schema closure, and handler registration

### 8.1 DI extension methods

**Agent.Memory.Projection** (`AddAgentMemoryProjectionSecurity()`):

Registers:
- `IAgentMemoryAccessArtifactCoordinator` → `AgentMemoryAccessArtifactCoordinator`
- `IAgentMemoryAccessHandleStore` → implementation
- `IAgentMemoryAccessGrantStore` → implementation
- `IAgentMemoryAccessHandleResolver` → `AgentMemoryAccessHandleResolver`
- `IAgentMemoryAccessGrantResolver` → `AgentMemoryAccessGrantResolver`
- `IAgentMemoryContextHandleIssuer` → `DefaultAgentMemoryContextHandleIssuer`
- `IAgentMemoryArtifactLifetimePolicy` → `DefaultAgentMemoryArtifactLifetimePolicy` (60s for `McpInvocation` recall handles/grants; session-lifetime cap for `McpSessionOperation` ContextHandles; existing scope lifetimes for `AgentToolInvocation`/`TrustedHostOperation`)

Does NOT register:
- `IAgentMemoryAccessScopeProvider` — **Host must register** an authorization policy. `DenyAllAgentMemoryAccessScopeProvider` is available for explicit opt-in testing.

Depends on (caller must register):
- `IAgentMemoryStore`
- `IAgentCompressedContextStore`
- `TimeProvider`

**Agent.Memory.ReadCore** (`AddAgentMemoryReadCore()`):

Registers:
- `IAgentMemoryReadCore` → `DefaultAgentMemoryReadCore`
- `IAgentContextReadCore` → `DefaultAgentContextReadCore`
- `IAgentMemorySourceExpandCore` → `DefaultAgentMemorySourceExpandCore`

Chains internally:
- `AddAgentMemoryProjectionSecurity()`

Depends on (caller must register):
- `IAgentMemoryAccessScopeProvider` (authorization policy)
- `IAgentMemoryRetriever`
- `IAgentCompressedContextStore`
- `IAgentContextSourceExpander`
- `IAgentMemoryContentSanitizer`
- `TimeProvider`

**Mcp.Memory** (`AddMcpMemoryTools()`):

Registers:
- `ICapabilityHandlerModule` → `McpMemoryCapabilityHandlerModule` (source-generated, see §8.5)
- `IMcpJsonContextContributor` → `McpMemoryJsonContextContributor`
- Descriptor providers via `[ModuleInitializer]` (see §8.4)

Chains internally:
- `AddAgentMemoryReadCore()`

Depends on (caller must register):
- `AddCapabilityPipeline()` (with authorization, audit, etc.)
- `AddCrestMcpToolProjection()` (MCP runtime snapshot, discovery, invoker)
- `IAgentMemoryAccessScopeProvider` (authorization policy)
- Memory stores (`IAgentMemoryStore`, `IAgentCompressedContextStore`, etc.)

**Agent.Memory.Tools** (refactored `AddAgentMemoryTools()`):

Now chains `AddAgentMemoryReadCore()` internally (which chains `AddAgentMemoryProjectionSecurity()`). Still registers 7 Agent Tool handlers via `ICapabilityHandlerModule` + governance infrastructure. No breaking change to existing callers.

### 8.2 IMcpJsonContextContributor — new MCP-specific interface

`IAgentToolJsonContextContributor` is Agent Tool-specific (namespace `CrestCreates.Agent.Tools`). `McpToolRuntimeSnapshotBuilder` does not use it. A new MCP-specific contributor is needed:

```csharp
public interface IMcpJsonContextContributor
{
    string Id { get; }
    int Order { get; }
    JsonSerializerContext Create(JsonSerializerOptions options);
    IReadOnlyCollection<Type> BindingRootTypes { get; }
}
```

Startup composition rules:
- Contributor `Id` must be Ordinal-unique across all registered contributors
- Contributors are sorted by `Order` then `Id` for stable composition
- A binding root type must have exactly one owner contributor
- All `JsonSerializerContext` instances must be source-generated — `DefaultJsonTypeInfoResolver` fallback is prohibited (NativeAOT requirement)

Composition order:
```text
McpJsonOptions.SerializerOptions
  → application-owned source-generated context
  → sorted MCP contributors (by Order, then Id)
  → frozen resolver chain
```

`McpMemoryJsonContextContributor` implements this, registering all MCP Memory DTO types for CLR JSON metadata closure.

`AddCrestMcpToolProjection()` must be updated to collect and apply `IMcpJsonContextContributor` registrations during `McpToolRuntimeSnapshotBuilder` composition.

### 8.3 Schema closure resolution

`McpToolRuntimeSnapshotBuilder` currently does not resolve transitive Schema reference closure. Memory Tool Schema contains nested objects and collections that need full closure resolution for:

1. JSON Schema projection (via `McpJsonSchemaProjector`)
2. CLR parity validation (via `McpToolSchemaParityValidator` → `SchemaJsonTypeInfoParityValidator` with `referencedSchemas` overload)

**New capability**: `McpToolSchemaClosureResolver` resolves the exact transitive Schema closure from `ISchemaRegistry` for each tool's input and output schemas:

```text
McpToolSchemaClosureResolver
  → resolve exact transitive Schema closure from ISchemaRegistry
  → McpJsonSchemaProjector.Project(root, closure)
  → McpToolSchemaParityValidator.Validate(root, typeInfo, closure)
```

This is a general MCP infrastructure improvement, not specific to Memory tools. It enables correct Schema handling for any MCP tool with nested schemas.

### 8.4 Descriptor providers and Schema ownership

MCP Memory capabilities register descriptors via `[ModuleInitializer]` + `DescriptorProviderRegistry`, following the existing pattern.

**Schema ownership**: shared read DTO schemas (item, block, grant, diagnostic, canonical-hash, build-pack-input/output, expand-source-input/output) are registered by `Agent.Memory.Projection` as the unique owner. `Agent.Memory.Tools` and `Mcp.Memory` register only their respective Capability schemas and any tool-specific schemas (e.g., `ctx_recall` input/output). Both capabilities reference the same shared Schema IDs — no parallel schemas, no contract drift.

```csharp
// Agent.Memory.Projection — shared read DTO schemas
internal static class AgentMemoryProjectionSchemaProviders
{
    [ModuleInitializer]
    internal static void Register()
    {
        DescriptorProviderRegistry.Register<SchemaDescriptor>(new SharedReadSchemas());
    }
}

// Mcp.Memory — MCP capabilities + ctx_recall-specific schemas
internal static class McpMemoryDescriptorProviders
{
    [ModuleInitializer]
    internal static void Register()
    {
        DescriptorProviderRegistry.Register<SchemaDescriptor>(new McpMemorySchemas());
        DescriptorProviderRegistry.Register<CapabilityDescriptor>(new McpMemoryCapabilities());
    }
}

// Agent.Memory.Tools — Agent Tool capabilities + write-only schemas
// MIGRATION: shared read schema definitions REMOVED from this provider
// and moved to AgentMemoryProjectionSchemaProviders.
// Only write-only schemas (compress-input/output, extract-input/output,
// promote-input/output, reject-input/output, supersede-input/output) remain.
// Query Capability descriptors now reference Projection-registered shared Schema IDs.
internal static class AgentMemoryToolDescriptorProviders
{
    [ModuleInitializer]
    internal static void Register()
    {
        DescriptorProviderRegistry.Register<SchemaDescriptor>(new AgentToolWriteOnlySchemas());
        DescriptorProviderRegistry.Register<CapabilityDescriptor>(new AgentMemoryToolCapabilities());
    }
}
```

Both Schema and Capability descriptors must be provided — Schema providers are required for snapshot startup.

All three capabilities are `CapabilityKind.Query` with permissions and risk levels per §6.11.

### 8.5 Composable capability handler registration — ICapabilityHandlerModule

The current `GeneratedHandlerRegistry.Apply(services)` has replace-semantics — each module's `Apply()` calls `services.RemoveAll<CapabilityHandlerResolver>()` and creates a new resolver containing only that module's handlers. This breaks multi-module composition: the last `Apply()` wins.

**Solution**: `ICapabilityHandlerModule` — a DI-registerable module contributor that the `CapabilityHandlerResolver` factory consumes.

**Assembly location**: `ICapabilityHandlerModule` is placed in `CrestCreates.Capability.Abstractions`, not in `Agent.Memory.Projection.Abstractions`. It is a Capability composition concern, not a Memory Projection concern. `Agent.Memory.Projection.Abstractions` does not reference `Capability.Abstractions`.

```csharp
// In CrestCreates.Capability.Abstractions
public interface ICapabilityHandlerModule
{
    string Id { get; }

    /// <summary>
    /// Apply invokers into the shared resolver.
    /// Called by the CapabilityHandlerResolver factory during DI resolution.
    /// </summary>
    void Apply(CapabilityHandlerResolver resolver);
}
```

The interface does NOT include `RegisterHandlers(IServiceCollection services)` — handler service registration is handled by generated static methods, not by the module interface. This keeps `ICapabilityHandlerModule` free of `Microsoft.Extensions.DependencyInjection.Abstractions` dependency.

The CodeGenerator emits for each assembly:

```csharp
internal sealed class GeneratedCapabilityHandlerModule : ICapabilityHandlerModule
{
    internal static GeneratedCapabilityHandlerModule Instance { get; } = new();

    private GeneratedCapabilityHandlerModule() { }

    public string Id => "<AssemblyName>";

    public void Apply(CapabilityHandlerResolver resolver)
        => CapabilityHandlerResolverProvider.ApplyDefinition(Id, resolver);
}
```

The module uses a static `Instance` singleton rather than relying on DI reflection activation of an internal type. This is NativeAOT-safe and avoids the issue of internal constructors not being publicly constructible by the DI container.

Module DI extensions register the pre-built instance:

```csharp
// In AddMcpMemoryTools():
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<ICapabilityHandlerModule>(
        GeneratedCapabilityHandlerModule.Instance));
GeneratedHandlerRegistry.RegisterServices(services); // static method for handler DI registration
```

The `CapabilityHandlerResolver` is built by a DI factory that collects all modules. This factory **replaces** the existing `TryAddSingleton<CapabilityHandlerResolver>` registration in `AddCapabilityPipeline()` — it becomes the single registration authority:

```csharp
// In AddCapabilityPipeline() — replaces existing TryAddSingleton<CapabilityHandlerResolver>
services.TryAddSingleton<CapabilityHandlerResolver>(sp =>
{
    var resolver = new CapabilityHandlerResolver();

    foreach (var module in sp.GetServices<ICapabilityHandlerModule>()
             .OrderBy(x => x.Id, StringComparer.Ordinal))
    {
        module.Apply(resolver);
    }

    return resolver;
});

services.TryAddSingleton<ICapabilityHandlerResolver>(
    sp => sp.GetRequiredService<CapabilityHandlerResolver>());
```

The existing legacy compatible resolver (registered by `AddCapabilityPipeline()`) is converted to a `LegacyCapabilityHandlerModule` that participates in the same composition, rather than being a parallel registration. This ensures:

- Host creates a single resolver
- Modules compose additively in stable order
- No dependency on `Add*()` call ordering
- No `BuildServiceProvider()` needed
- Duplicate Capability IDs still fail-fast in the resolver
- The legacy resolver does not silently win via `TryAddSingleton`

**LegacyCapabilityHandlerModule implementation**: The current `CapabilityHandlerResolverProvider` has two distinct internal states:

1. **Registrations** — invokers added via `Register(capabilityId, invoker)` (legacy compatible resolver)
2. **Definitions** — module definitions added via `RegisterDefinition(providerId, apply)` (source-generated modules)

`LegacyCapabilityHandlerModule` must only copy **Registrations**, NOT Definitions. Each `GeneratedCapabilityHandlerModule` applies its own Definition via `ApplyDefinition(Id, resolver)`. If the Legacy module also iterated Definitions, the same handlers would be registered twice, triggering duplicate-capability exceptions.

To make `LegacyCapabilityHandlerModule` constructible, add a public controlled copy API on `CapabilityHandlerResolverProvider`:

```csharp
// In CapabilityHandlerResolverProvider (CrestCreates.Capability.Abstractions)
// Public API — safe to call from CrestCreates.Capability
public static void ApplyLegacyRegistrations(CapabilityHandlerResolver target)
{
    ArgumentNullException.ThrowIfNull(target);
    Resolver.CopyRegistrationsTo(target);
}
```

`CopyRegistrationsTo` remains `internal` on `CapabilityHandlerResolver` — it is only called by `ApplyLegacyRegistrations` within the same assembly. The public `ApplyLegacyRegistrations` method is the controlled entry point that exposes only the "copy legacy registrations" capability, not the full internal state.

The `LegacyCapabilityHandlerModule` uses a static `Instance` singleton (same pattern as `GeneratedCapabilityHandlerModule`) and is registered by `AddCapabilityPipeline()`:

```csharp
internal sealed class LegacyCapabilityHandlerModule : ICapabilityHandlerModule
{
    internal static LegacyCapabilityHandlerModule Instance { get; } = new();

    private LegacyCapabilityHandlerModule() { }

    public string Id => "legacy-capability-pipeline";

    public void Apply(CapabilityHandlerResolver resolver)
    {
        // Only copies legacy Register() invokers — NOT Generated Definitions
        CapabilityHandlerResolverProvider.ApplyLegacyRegistrations(resolver);
    }
}
```

Registered via static Instance (same pattern as GeneratedCapabilityHandlerModule):

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<ICapabilityHandlerModule>(
        LegacyCapabilityHandlerModule.Instance));
```

Each `GeneratedCapabilityHandlerModule` applies its own Definition independently:

```csharp
internal sealed class GeneratedCapabilityHandlerModule : ICapabilityHandlerModule
{
    public string Id => "<AssemblyName>";

    public void Apply(CapabilityHandlerResolver resolver)
    {
        CapabilityHandlerResolverProvider.ApplyDefinition(Id, resolver);
    }
}
```

This ensures no double-application: legacy registrations go through `CopyRegistrationsTo`, generated definitions go through `ApplyDefinition`, and the two paths never overlap.

## 9. Testing and exit criteria

### 9.1 Exit criteria

1. **Discovery**: Only 4 tools appear, all with `ReadOnlyHint=true` (derived from `CapabilityKind.Query`). No write operations are discoverable or invokable.
2. **Budget fail-closed**: `memory_recall` and `ctx_recall` count/character budgets fail closed before store access.
3. **Indistinguishable unavailable**: Cross-tenant, denied visibility, expired/revoked grant, forged handle, foreign-security-context grant, foreign-host grant, missing session identity — all return the same unavailable result. No information leakage.
4. **No content leakage**: Output, diagnostics, exceptions, and MCP result do not leak raw sensitive content, raw source refs, or denied descriptor identity.
5. **Shared expand path**: `ctx_expand` and `memory_source_expand` use the same Capability, Handler, grant resolver, and sanitized expander. No separate grant resolver or sanitizer.
6. **Zero domain-state write**: E2E proves Memory/Context/Descriptor/Conversation/Task/Workflow store zero writes. Expand tools also prove zero security artifact store writes. Recall tools allow controlled credential issuance only.
7. **Authoritative invariant**: All recalled memory has `IsAuthoritative = false`. No secondary authority flag.
8. **Agent Tool regression**: Existing Agent Tool Memory Tool integration tests pass after refactoring.
9. **AOT verification**: MCP Memory tools close under NativeAOT publish fixture (extend existing `CrestCreates.Mcp.AotFixture` or new fixture).
10. **Provisional credential lifecycle**: Artifacts prepared during handler execution that fail serialization expire harmlessly within the short expiry window.
11. **Composable registration**: Multiple `Add*MemoryTools()` calls compose correctly — all handlers are resolvable, no replace-semantics data loss.
12. **ContextHandle issuance**: Host code calls `IAgentMemoryContextHandleIssuer.IssueForCallerAsync` → handle is resolvable via `ctx_recall`. Issuer self-resolves scope, routes through Coordinator.
13. **Cross-call grant reuse**: Grant issued in `memory_recall` invocation A is usable in `memory_source_expand` invocation B within the same `SecurityContextId`.
14. **Architecture boundary**: `CrestCreates.DependencyBoundaries.Tests` confirms `Mcp.Memory` does not reference `IAgentMemoryStore`, `IAgentCompressedContextStore`, `IAgentMemoryRetriever`, `IAgentContextSourceExpander`.

### 9.2 Test scenarios

**Security acceptance checkpoints** (implementation-phase validation, not design expansion):

1. **Coordinator internal partial-failure compensation**: When `PrepareAsync` creates Handles successfully but Grant creation fails (before returning `CompensationToken` to the caller), the Coordinator must internally revoke the already-created artifacts. This invariant is already followed by the existing Coordinator; the new Coordinator must preserve it.

2. **Principal construction fail-closed validation**: MCP handlers must validate that `TenantId`, `UserId`, `HostId`, `SessionId`, and `InvocationId` are all non-null/non-empty before constructing `AgentMemoryAccessPrincipal` or `ArtifactOrigin.BindingHash`. Any missing identity field results in an immediate unavailable response — no empty-string fallback, no partial Principal.

**Discovery tests**:
- List tools → exactly 4, all `ReadOnlyHint=true`
- No promote/reject/supersede/compress/extract tools discoverable
- Two expand tools share one Capability ID

**Budget enforcement tests**:
- `MaximumCount = 0` → unavailable
- `CharacterBudget = -1` → unavailable
- `MaximumCount > scope.MaxRecallCount` → unavailable
- `CharacterBudget > scope.MaxContextRecallCharacters` → unavailable (ctx_recall)
- `StartBlockIndex < 0` → unavailable
- `EndBlockIndexExclusive ≤ StartBlockIndex` → unavailable
- Budget checks occur before store access (verify no store call made)

**Security tests**:
- Cross-tenant handle → unavailable (indistinguishable from missing)
- Expired grant → unavailable
- Revoked grant → unavailable
- Forged handle ID → unavailable
- Foreign-host grant (different `CallerId`) → unavailable
- Foreign-security-context grant (different `SecurityContextId`) → unavailable
- Scope-stale grant (fingerprint mismatch) → unavailable
- Denied visibility descriptor → unavailable
- Unscoped memory + `AllowUnscopedMemory=false` → unavailable
- Missing SessionId → unavailable

**Cross-call grant reuse tests**:
- Grant issued in `memory_recall` invocation A → usable in `memory_source_expand` invocation B (same `SecurityContextId`)
- Grant issued in `ctx_recall` invocation A → usable in `ctx_expand` invocation B (same `SecurityContextId`)
- Grant issued in session A → NOT usable in session B (different `SecurityContextId`)

**Zero-write tests**:
- Inspect all stores before and after `memory_recall` call → no domain-state records (security artifact issuance allowed)
- Inspect all stores before and after `ctx_recall` call → no domain-state records (security artifact issuance allowed)
- Inspect all stores before and after `memory_source_expand` call → **zero writes including security artifact stores**
- Inspect all stores before and after `ctx_expand` call → **zero writes including security artifact stores**
- Grant issuance counted via `ArtifactReceipt` for recall tools

**Shared core tests**:
- `IAgentMemoryReadCore` produces identical output to pre-refactor `BuildAgentMemoryPackHandler`
- `IAgentMemorySourceExpandCore` produces identical output to pre-refactor `ExpandAgentMemorySourceHandler`
- `IAgentContextReadCore` correctly queries compressed context store with budget enforcement
- `AgentMemoryReadCoreOutcome` returns correct `ScopeFingerprint` and `ArtifactReceipt`

**Agent Tool regression tests**:
- Existing `BuildAgentMemoryPackHandler` integration tests pass
- Existing `ExpandAgentMemorySourceHandler` integration tests pass

**Provisional credential lifecycle test**:
- Prepare artifacts during handler → force serialization failure → verify artifacts are not usable after expiry window
- Same `OriginBindingHash` retry after expiry → `IdentityConflict`

**Compensation test**:
- ReadCore internal exception after Prepare → `RevokeCreatedAsync` called → artifacts are revoked
- Agent Tool governance failure after ReadCore returns → `RevokeCreatedAsync` called → artifacts are revoked
- MCP Handler result mapping failure → `RevokeCreatedAsync` called → artifacts are revoked
- MCP Invoker serialization failure after Handler succeeds → short-lived expiry (no immediate revoke — handler already returned)

**Cross-interface store test**:
- New ReadCore-issued handle → resolvable through old Agent Tool interfaces (via adapter)
- Old Compress-issued context handle → resolvable through new ReadCore (via canonical store)
- Revoke/expiry/quota state consistent across old and new interfaces

**ContextHandle issuance test**:
- Host code calls `IAgentMemoryContextHandleIssuer.IssueForCallerAsync` → handle is resolvable via `ctx_recall`
- Issuer self-resolves scope (no Host-supplied scope)
- Issuer routes through `IAgentMemoryAccessArtifactCoordinator.PrepareAsync`, not directly through store

**Composable registration test**:
- Register Application + AgentMemory + McpMemory handler modules → all handlers resolvable
- No replace-semantics data loss when multiple modules register

**MCP Tool exposure test**:
- `IMcpToolExposurePolicy` denies Memory tools → tools not discoverable, invocation = "unknown tool"
- `IMcpToolExposurePolicy` allows Memory tools → tools discoverable and invokable

**Architecture boundary test**:
- `CrestCreates.DependencyBoundaries.Tests` confirms `Mcp.Memory` does not reference store/retriever/expander types

## 10. New DTOs for ctx_recall

Added to `CrestCreates.Agent.Memory.Projection.Abstractions`:

```csharp
public sealed record RecallAgentContextInput
{
    public required string ContextHandle { get; init; }
    public required int MaximumBlockCount { get; init; }
    public required int CharacterBudget { get; init; }
    public int? StartBlockIndex { get; init; }
    public int? EndBlockIndexExclusive { get; init; }
}

public sealed record RecallAgentContextResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public IReadOnlyList<AgentMemoryToolBlockDto> Blocks { get; init; } = Array.Empty<AgentMemoryToolBlockDto>();
    public int BlockCount { get; init; }
    public bool WasTruncated { get; init; }
    public IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; } = Array.Empty<AgentMemoryToolDiagnosticDto>();
}
```

These are in `Agent.Memory.Projection.Abstractions` rather than `Mcp.Memory` because they represent a capability contract that could be projected through any protocol (MCP, HTTP, Agent Tool), not just MCP.

`CharacterBudget` is a total character budget across all blocks, consumed in stable block order until exhausted. `StartBlockIndex` / `EndBlockIndexExclusive` replace `Range?` to be wire-safe (no negative indices, no from-end syntax).

## 11. AgentMemoryAccessScope — context recall budget field

The `AgentMemoryAccessScope` record (in `Projection.Abstractions`) must include a field for context recall character budget:

```csharp
public sealed record AgentMemoryAccessScope
{
    // ... existing fields from AgentMemoryToolAccessScope ...
    public required int MaxRecallCount { get; init; }
    public required int MaxRecallCharacters { get; init; }
    public required int MaxExpansionCharacters { get; init; }
    public required int MaxCompressedBlockCount { get; init; }

    /// <summary>
    /// Maximum total character budget for ctx_recall across all blocks.
    /// Constrained by: ctx_recall.CharacterBudget <= this value.
    /// </summary>
    public required int MaxContextRecallCharacters { get; init; }

    /// <summary>
    /// Maximum resource handles per single invocation (per OriginBindingHash).
    /// </summary>
    public required int MaxResourceHandlesPerOperation { get; init; }

    /// <summary>
    /// Maximum grants per single invocation (per OriginBindingHash).
    /// </summary>
    public required int MaxGrantsPerOperation { get; init; }

    // ... remaining fields ...
}
```

Per-invocation quotas are tracked by `OriginBindingHash` (not by `Principal.SecurityContextId`) to prevent long-lived sessions from accumulating unlimited artifacts.
