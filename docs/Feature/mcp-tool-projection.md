# MCP Tool Projection

Phase 8e projects explicitly selected `CapabilityDescriptor` instances as MCP 2025-06-18 tool contracts. The first-party MCP runtime is NativeAOT-verified by a publish-and-run fixture. It does not host an MCP server: a future transport adapter translates official SDK requests to the protocol-neutral discovery and invocation APIs described here.

## Authoring a tool

Declare tools in a non-generic `static partial class`. Each tool spec is a direct, non-generic nested class:

```csharp
using CrestCreates.Mcp;

[McpToolSpecs]
public static partial class OrderMcpTools
{
    [McpToolSpec(
        "orders.create",
        CapabilityVersion = 0,
        InputType = typeof(CreateOrderInput),
        OutputType = typeof(OrderDto),
        ToolName = "orders.create",
        Title = "Create order",
        Description = "Creates one validated order.",
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class Create
    {
    }
}
```

`CapabilityVersion = 0` selects Latest Active during snapshot construction; a positive value selects that exact version. Negative versions and Compatible selection are unsupported. The generated provider and binding are registered through module initializers. Binding identity is `(DescriptorId, DescriptorVersion)`, never `ToolName`.

Input and output must be concrete object-root DTOs. Omit `InputType` for a no-input tool and omit `OutputType` for a void tool. Interfaces, abstract types, open generics, multiple CLR parameters, runtime DTO discovery, and `Dictionary<string, object?>` fallback are not supported.

## Application-owned JSON metadata

The application owns source-generated JSON metadata for every projected DTO:

```csharp
[JsonSerializable(typeof(CreateOrderInput))]
[JsonSerializable(typeof(OrderDto))]
public partial class ApplicationJsonContext : JsonSerializerContext;

services.AddCrestMcpToolProjection(options =>
{
    options.SerializerOptions.TypeInfoResolver = ApplicationJsonContext.Default;
});
```

Snapshot construction resolves and caches each `JsonTypeInfo`. A reflection fallback, missing metadata, a non-object root, or schema/type parity failure prevents snapshot publication.

The MCP Descriptor stores a Metadata-owned `McpCapabilityReference`; it does not
make Metadata depend on the Runtime Capability assembly. Runtime snapshot
construction resolves that value to the captured `CapabilityDescriptor`. A hosted
startup validator idempotently builds Schema, Capability, and MCP Tool registries
in dependency order, then eagerly publishes the singleton snapshot before the Host
reports started.

## Discovery and invocation

Trusted server adapters construct Host and call identities; these values never come from tool arguments:

```csharp
var host = new McpToolHostContext("operations", "Production", "read-write");

var tools = await discovery.ListAsync(new McpToolDiscoveryContext(host), cancellationToken);

var result = await invoker.InvokeAsync(
    "orders.create",
    arguments,
    new McpToolCallContext(
        host,
        InvocationId: logicalInvocationId,
        RequestId: jsonRpcRequestId,
        SessionId: sessionId),
    cancellationToken);
```

`arguments` may be absent and is normalized to `{}`. Every schema is a closed object (`additionalProperties: false`); unknown and duplicate JSON properties are rejected with Ordinal name comparison. Startup parity is bidirectional and validates property names, direction, requiredness, nullability, scalar/collection shape, and collection element category. `InvocationId` must remain stable for redelivery of the same logical call and differ for distinct calls in the Host idempotency domain.

The invoker always calls the descriptor overload of `ICapabilityDispatcher` with `InvocationSource.Mcp`. Authorization, validation, rate limiting, idempotency, audit, and events remain in the Capability Pipeline. MCP arguments cannot set tenant, user, permissions, risk, source, or idempotency identity.

## Exposure policy

`IMcpToolExposurePolicy` receives the trusted Host, captured tool and Capability, and either Discovery or Invocation phase. It is separate from Capability authorization. A denied tool is omitted from discovery and is indistinguishable from an unknown tool during invocation. A policy exception fails the complete discovery or invocation request as an internal server protocol failure; discovery never returns a partial list and dispatch is not attempted.

The default policy allows every explicitly projected, runtime-ready tool. Replace it in DI when a Host needs profile, environment, risk, or permission-based visibility rules.

## Supported Schema contract

The root is always an object. Scalar `FieldType`, and `CollectionElementType` for collections, support:

| Schema token | JSON Schema |
| --- | --- |
| `string` | `string` |
| `bool` | `boolean` |
| `int`, `long` | `integer` |
| `decimal`, `double` | `number` |
| `guid`, legacy `Guid` | `string`, `uuid` |
| `date`, legacy `DateOnly` | `string`, `date` |
| `datetime`, legacy `DateTime` / `DateTimeOffset` | `string`, `date-time` |

For `IsCollection = true`, `FieldType` remains legacy container/display metadata and the element shape comes only from `CollectionElementType`. Phase 8e deliberately does not rewrite existing Schema type strings or their contract hashes.

Requiredness and nullability are independent. Properties and `required` entries are sorted with `StringComparer.Ordinal`. Integer projection includes CLR bounds. UUID accepts canonical hyphenated D form case-insensitively; date is `yyyy-MM-dd`; date-time is RFC 3339 with `Z` or an offset.

An active MCP snapshot fails closed for unknown types, invalid or inapplicable constraints, non-empty `Pattern`, `ValidationRules`, or unsupported `References`. Pattern remains supported by the shared Schema validator for other invocation sources; it is rejected only at the MCP projection boundary because .NET Regex and JSON Schema pattern semantics are not interchangeable.

Capability InputSchema and OutputSchema references must be Exact, positive-version references without `ExpectedContractHash`. A declared output is serialized with the captured `JsonTypeInfo`, must have the exact configured runtime CLR type, and is validated against OutputSchema before `structuredContent` is returned.

## Boundaries and non-goals

MCP projection does not depend on Dynamic API, AppService execution, ASP.NET Core, Agent Control Plane, or the official MCP Server SDK. Agent Control Plane does not author `DescriptorKind.McpTool`.

Phase 8e does not include server transport hosting, authentication protocol, sessions, Tasks, progress, resources, prompts, sampling, automatic Capability exposure, approval workflows, hot reload, or reflection-based schema discovery.

GitHub issue #61 (Generated CRUD trimming-safe JSON contracts) remains separate. Projected DTOs must already be visible to the application's `JsonSerializerContext`; Phase 8e does not claim generated CRUD DTO closure.
