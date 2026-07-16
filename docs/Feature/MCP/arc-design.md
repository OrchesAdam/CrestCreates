# MCP Tool Projection — Architecture Design

> Phase 8e architecture reference for projecting explicitly selected Capabilities into MCP tool contracts.
> Status: Approved. MCP transport hosting remains a future adapter concern.

## 1. Positioning

Phase 8e makes MCP a protocol projection over the Capability mainline. It does not create a second business runtime, wrap the legacy Dynamic API, or let an MCP adapter call a Handler directly.

```text
[McpToolSpec]
      ↓ Source Generator
McpToolDescriptor + exact typed binding
      ↓ startup validation
Active immutable McpToolRuntimeSnapshot
      ↓ discovery / invocation
McpToolDiscoveryService / McpToolInvoker
      ↓
ICapabilityDispatcher.DispatchAsync(
    captured CapabilityDescriptor,
    InvocationSource.Mcp,
    exact typed input)
      ↓
CapabilityPipeline → generated Handler
      ↓
exact output serialization → OutputSchema validation
```

MCP owns protocol metadata, JSON binding, discovery projection, Host exposure policy integration, and protocol-neutral result mapping. Capability owns schemas, permissions, risk, validation, rate limiting, idempotency, audit, tenant/user propagation, events, and business execution.

The protocol baseline is MCP `2025-06-18`. Phase 8e intentionally excludes MCP transport hosting, authentication protocol, sessions, Tasks, progress, resources, prompts, sampling, and official MCP SDK references.

## 2. Project boundaries

```text
Metadata.Abstractions
        ↑
Metadata.Mcp.Abstractions  ←  Metadata
        ↑                         ↑
Capability.Abstractions     Metadata.Mcp
        ↑                         ↑
Mcp.Abstractions          Mcp Runtime
                                  ↑
                         CodeGenerator output
```

The formal projects are:

| Project | Responsibility | Forbidden dependencies |
|---|---|---|
| `CrestCreates.Metadata.Mcp.Abstractions` | Metadata-owned `McpToolDescriptor`, `McpCapabilityReference`, relationship/hash contracts | Runtime Capability implementation, ASP.NET Core, Dynamic API |
| `CrestCreates.Mcp.Abstractions` | Protocol-neutral Tool, Host, call, result, policy and invoker contracts | ASP.NET Core, Dynamic API, AppService, Agent Control Plane, official MCP SDK |
| `CrestCreates.Mcp` | Registry, snapshot, schema projector/validator, JsonTypeInfo validation, invoker, result mapper, DI | Direct Handler invocation, runtime DTO/schema reflection, official MCP SDK |
| `CrestCreates.CodeGenerator` | `[McpToolSpecs]` authoring normalization and generated provider/binding output | Runtime scanning, reflection serializer fallback, Dynamic API symbols |

Metadata references the Metadata-layer MCP descriptor contract only. It never references `CrestCreates.Mcp.Abstractions` or the Integrations layer. The protocol abstraction references Metadata abstractions as a lower-level contract, not the reverse.

## 3. Descriptor and authoring model

`McpToolDescriptor` is projection metadata. It references a Capability; it does not copy or own Capability permissions, risk, schemas, or handlers.

```csharp
public sealed class McpToolDescriptor : IDescriptor, IVersionedDescriptor
{
    public string Namespace => "mcp-tool";
    public DescriptorKind Kind => DescriptorKind.McpTool;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public DescriptorState State { get; init; } = DescriptorState.Active;

    public required McpCapabilityReference Capability { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public McpToolAnnotationOverrides AnnotationOverrides { get; init; } = new();
}
```

The default identity convention is:

```text
Descriptor Id: mcp-tool:{capabilityId}
Tool Name:    capabilityId
Descriptor Version: 1
```

Descriptor identity and protocol ToolName remain separate. Registry and binding lookup use `(DescriptorId, DescriptorVersion)`. ToolName is an Ordinal, globally unique discovery key and may be explicitly overridden.

The authoring container must be a **top-level**, non-generic `static partial class`. Each spec is a direct, non-generic nested class. The generator rejects any Error diagnostic for the whole container and emits neither provider nor binding output.

```csharp
[McpToolSpecs]
public static partial class OrderMcpTools
{
    [McpToolSpec(
        "orders.create",
        CapabilityVersion = 0, // Latest Active
        InputType = typeof(CreateOrderInput),
        OutputType = typeof(OrderDto),
        ToolName = "orders.create",
        Title = "Create order",
        Description = "Creates one validated order.",
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class Create { }
}
```

`CapabilityVersion = 0` means Latest Active; a positive version means Exact. Compatible selection is not supported. Input and output are single object-root DTOs. No-input and void tools omit the corresponding type and schema.

Annotation values are hints, not authorization. Query/Command semantics and permissions remain authoritative in the Capability descriptor and pipeline. MCP input DTOs must not use `JsonRequired`; Schema owns input presence validation so missing members can reach the Capability Pipeline.

## 4. Generated artifacts

For every valid container, the generator emits:

1. A descriptor provider registered through a module initializer.
2. A binding registration keyed by Descriptor Id and Version.
3. Exact input materialization using the application-supplied `JsonTypeInfo<TInput>`.
4. Exact output serialization using the application-supplied `JsonTypeInfo<TOutput>`.
5. Input/output type registration metadata for startup parity checks.

Generated code must not contain:

- `Dictionary<string, object?>` to DTO fallback;
- runtime `GetProperties()` or DTO scanning;
- reflection JSON serializer overloads;
- direct Handler invocation;
- Dynamic API, ASP.NET Core, AppService, Agent Control Plane, or official MCP SDK symbols.

Generated output performs a strict runtime type check for results:

```csharp
if (output is null || output.GetType() != typeof(TOutput))
    throw new McpToolContractViolationException(...);
```

## 5. Startup closure and immutable snapshot

Host startup builds and validates registries in this order:

```text
Schema Registry
    ↓
Capability Registry
    ↓
MCP Tool Registry
    ↓
Capability/Schema reference resolution
    ↓
Generated binding lookup
    ↓
Application JsonTypeInfo validation
    ↓
Schema/JSON parity
    ↓
Immutable Active runtime snapshot publication
```

`McpToolRuntimeSnapshot` contains only Active, fully validated, runtime-ready entries. Each entry captures the descriptor, resolved Capability, exact Schema versions, generated binding, cached JsonTypeInfo, discovery contract, and contract hashes. Discovery and invocation never re-resolve mutable registries during a call.

Snapshot publication is explicit and one-time. A snapshot cannot be eagerly cached as an empty result before registry bootstrap. `IRegistryState` is opt-in: a registry implementing it must report `Built`; a custom registry without it is trusted after the startup builder returns successfully. `TenantScopedRegistry` does not manufacture state for an inner registry.

All descriptor identities and reference syntax are validated, including positive versions, exact/Latest selection rules, and unsupported `ExpectedContractHash`. Active candidates additionally require binding, JsonTypeInfo, schema parity, discovery projection, and unique ToolName. Historical or inactive descriptors do not block an otherwise valid Active snapshot merely because runtime binding is absent.

## 6. JSON and Schema contract

The application owns its source-generated JSON context:

```csharp
[JsonSerializable(typeof(CreateOrderInput))]
[JsonSerializable(typeof(OrderDto))]
public partial class ApplicationJsonContext : JsonSerializerContext;

services.AddCrestMcpToolProjection(options =>
{
    options.SerializerOptions.TypeInfoResolver = ApplicationJsonContext.Default;
});
```

MCP copies and freezes the options during snapshot construction. Reflection fallback and `DefaultJsonTypeInfoResolver` are rejected. `RespectNullableAnnotations` and `RespectRequiredConstructorParameters` must remain disabled; otherwise STJ could reject input before Dispatcher/Pipeline.

Capability Schema is the only discovery schema authority. The supported subset is object-root primitive fields and primitive collections:

| Schema token | JSON Schema |
|---|---|
| `string` | `string` |
| `bool` | `boolean` |
| `int`, `long` | `integer` with CLR bounds |
| `decimal`, `double` | `number` |
| `guid` | `string`, `uuid` |
| `date` | `string`, `date` |
| `datetime` | `string`, `date-time` |

For collections, `FieldType` remains legacy container/display metadata and `CollectionElementType` controls the JSON element shape. Existing Schema type strings and their contract hashes are not silently rewritten.

MCP rejects unknown or inapplicable constraints, non-empty `Pattern`, non-empty `ValidationRules`, and unsupported `References` at snapshot construction. The generic SchemaValidator retains its existing .NET Regex Pattern behavior for other invocation sources.

Parity checks use the actual JSON property names and directional `JsonTypeInfo` metadata. Input properties must be settable/constructible, must not be JSON-required, and use set-side nullability. Output properties must be serializable, use get-side nullability, and match Schema requiredness. Runtime OutputSchema validation remains authoritative for the serialized value.

## 7. Discovery, invocation, and governance

Discovery returns a protocol-neutral `McpToolContract`, never an internal Descriptor. Host context is trusted input from the future Server Adapter:

```csharp
public sealed record McpToolHostContext(
    string HostId,
    string EnvironmentName,
    string? ProfileName = null);
```

Exposure policy runs during both discovery and invocation. A denied Tool is hidden as if unknown. A policy exception is an InternalServer protocol failure and stops before binding or dispatch. Exposure policy cannot grant permissions or lower Capability risk.

Invocation accepts nullable `arguments`. It normalizes absence to `{}` and performs only MCP structural checks before binding:

1. arguments root is an object;
2. duplicate properties are rejected;
3. unknown properties are rejected with Ordinal comparison;
4. no-input tools reject non-empty objects.

Binder failures become safe Tool input errors. Requiredness, nullability, type, range, length, date, UUID, and collection validation are performed by the Capability Validation Middleware after Dispatcher is called.

```csharp
public sealed record McpToolCallContext(
    McpToolHostContext Host,
    string InvocationId,
    string RequestId,
    string? SessionId = null);
```

`RequestId` maps to `CausationId`. `InvocationId` is stable for repeated delivery of the same logical call and is used by the canonical idempotency key builder. TenantId, UserId, permissions, risk, invocation source, and idempotency identity cannot be supplied through tool arguments.

Null outer contexts and blank ToolName are classified as InvalidRequest protocol failures rather than leaking ordinary null or dictionary exceptions.

Result mapping distinguishes protocol errors from Tool execution errors. Business, authorization, validation, rate-limit, timeout, and handler failures use `isError=true` without stack traces, SQL, internal types, permissions, or raw policy messages. Safe field-level validation hints are emitted only for `CAPABILITY_VALIDATION_FAILED` issues.

## 8. NativeAOT and verification

MCP is a first-party `NativeAOT-verified` runtime. The formal fixture is pinned to `linux-x64` and performs:

1. real `PublishAot`;
2. native linking with clang;
3. native executable execution;
4. generated input binding;
5. real Dispatcher/Pipeline/generated Handler execution;
6. output serialization and OutputSchema validation.

The fixture is not a claim of cross-platform NativeAOT support. EF Core, external SDKs, ORM providers, and legacy compatibility paths retain separately declared capability levels.

## 9. Non-goals and related debt

Phase 8e does not implement MCP server hosting, transport, authentication protocol, sessions, Tasks, progress, resources, prompts, sampling, automatic Capability exposure, approval workflows, hot reload, dynamic CLR DTO generation, reflection-based schema discovery, or a legacy Dynamic API bridge.

Issue #61 — Generated CRUD Trimming-Safe JSON Contracts — remains independent. Phase 8e requires projected DTOs to already be visible to the application's source-generated JSON context and does not claim CRUD DTO closure.

## 10. Verification baseline

The implementation currently verifies:

- MCP Runtime: 60 tests;
- MCP E2E: generated Handler and real Capability Pipeline;
- Capability Runtime: 139 tests;
- MCP Source Generator: 9 tests;
- dependency boundaries: 40 tests;
- NativeAOT linux-x64 publish-and-run fixture.
