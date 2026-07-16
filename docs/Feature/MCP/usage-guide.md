# MCP Tool Projection — Usage Guide

This guide shows how to expose an explicitly selected Capability as an MCP Tool. It assumes the application already has a Capability descriptor, Schema descriptors, a generated Handler, and an application-owned `JsonSerializerContext`.

## 1. Register the runtime

Register the Capability runtime and MCP projection in the application composition root:

```csharp
builder.Services.AddCapabilityRuntime();

builder.Services.AddCrestMcpToolProjection(options =>
{
    options.SerializerOptions.TypeInfoResolver = ApplicationJsonContext.Default;
});
```

`AddCrestMcpToolProjection()` registers the descriptor registry, snapshot bootstrapper, discovery service, scoped invoker, schema projector, result mapper, and exposure policy defaults. The MCP invoker is scoped so the Capability Dispatcher retains request tenant/user and pipeline scope isolation.

Host startup builds Schema, Capability, and MCP registries, validates all Active projected tools, and publishes one immutable snapshot. Missing Capability, exact Schema, generated binding, or JsonTypeInfo fails startup.

## 2. Author a Tool

The container must be a top-level, non-generic `static partial class`. Each Tool is a direct nested class:

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
    public sealed class Create { }

    [McpToolSpec(
        "orders.get",
        CapabilityVersion = 1,
        InputType = typeof(GetOrderInput),
        OutputType = typeof(OrderDto),
        ToolName = "orders.get",
        Title = "Get order",
        Description = "Returns one order by identifier.")]
    public sealed class Get { }

    [McpToolSpec(
        "orders.refresh-cache",
        ToolName = "orders.refresh-cache",
        Title = "Refresh order cache",
        Description = "Refreshes the server-side order cache.")]
    public sealed class RefreshCache { }
}
```

Use `CapabilityVersion = 0` for Latest Active or a positive number for Exact. Compatible selection is not supported. `ReadOnlyHint`, `DestructiveHint`, `IdempotentHint`, and `OpenWorldHint` are protocol hints only and never replace Capability permissions or risk policy.

No-input Tools omit `InputType`; void Tools omit `OutputType`. Input and output must be concrete object-root DTOs. Do not use interfaces, abstract/open-generic types, collections as the root DTO, dictionaries, runtime DTO generation, or nested `[McpToolSpecs]` containers.

## 3. Define DTOs and JSON metadata

The application owns JSON metadata for every projected DTO:

```csharp
public sealed class CreateOrderInput
{
    [JsonPropertyName("customerId")]
    public Guid CustomerId { get; set; }

    [JsonPropertyName("items")]
    public List<CreateOrderItem> Items { get; set; } = [];
}

public sealed class OrderDto
{
    [JsonPropertyName("orderId")]
    public Guid OrderId { get; init; }
}

[JsonSerializable(typeof(CreateOrderInput))]
[JsonSerializable(typeof(CreateOrderItem))]
[JsonSerializable(typeof(OrderDto))]
public partial class ApplicationJsonContext : JsonSerializerContext;
```

Configure the context through `McpJsonOptions`. Do not enable reflection fallback, `RespectNullableAnnotations`, or `RespectRequiredConstructorParameters`. Do not put `[JsonRequired]` on input properties; the referenced Capability Schema is the input presence authority and the Capability Pipeline must receive missing-field requests.

## 4. Schema requirements

The Capability referenced by the Tool supplies InputSchema and OutputSchema. MCP requires each referenced Schema to use an Exact positive version and a null `ExpectedContractHash`.

Supported field tokens are:

```text
string, bool, int, long, decimal, double, guid, date, datetime
```

Primitive collections use `CollectionElementType`; the legacy container-facing `FieldType` is not rewritten. The MCP snapshot rejects unknown types, non-empty Pattern, unsupported References, ValidationRules, and invalid/inapplicable constraints.

All projected schemas are object-root and closed:

```json
{
  "type": "object",
  "properties": {},
  "additionalProperties": false
}
```

## 5. Discover Tools

The future MCP Server Adapter supplies a trusted Host context:

```csharp
var host = new McpToolHostContext(
    HostId: "operations",
    EnvironmentName: "Production",
    ProfileName: "read-write");

var contracts = await discovery.ListAsync(
    new McpToolDiscoveryContext(host),
    cancellationToken);
```

Discovery returns protocol-neutral `McpToolContract` values. It does not expose internal permissions, risk details, Capability descriptors, or registry objects. Tool order is deterministic using `StringComparer.Ordinal`.

An `IMcpToolExposurePolicy` may hide Tools by Host, environment, profile, risk, or other trusted governance context. A denied Tool is omitted; a policy exception is an InternalServer protocol failure. The policy does not grant Capability permissions.

## 6. Invoke a Tool

The Server Adapter creates a trusted call context. `InvocationId` must be stable for redelivery of the same logical call; `RequestId` is used for causation only:

```csharp
var outcome = await invoker.InvokeAsync(
    "orders.create",
    arguments, // JsonElement?; null means {}
    new McpToolCallContext(
        host,
        InvocationId: logicalInvocationId,
        RequestId: canonicalRequestId,
        SessionId: sessionId),
    cancellationToken);
```

The invocation sequence is:

1. validate outer context and ToolName;
2. find the immutable snapshot entry;
3. apply Host exposure policy;
4. normalize absent arguments to `{}`;
5. reject non-object root, duplicate properties, unknown properties, and invalid no-input shape;
6. run the generated exact binder;
7. call `ICapabilityDispatcher` with the captured Capability and `InvocationSource.Mcp`;
8. let Capability Pipeline execute audit, rate limit, tenant, authorization, validation, idempotency, metrics, events, and Handler;
9. serialize exact output with cached JsonTypeInfo;
10. validate serialized output against OutputSchema and map the protocol-neutral result.

MCP arguments cannot set TenantId, UserId, permissions, risk, InvocationSource, or idempotency identity. Those values come from trusted Host context and Capability Runtime.

## 7. Handle results and errors

Successful output contains text content and `structuredContent`:

```json
{
  "isError": false,
  "content": [{ "type": "text", "text": "{\"orderId\":\"...\"}" }],
  "structuredContent": { "orderId": "..." }
}
```

Successful void Tools return a safe completion message. Capability validation failures, authorization failures, rate limits, timeouts, and business failures return `isError: true` without stack traces, SQL, internal service names, permissions, or raw policy messages.

Only authoritative `CAPABILITY_VALIDATION_FAILED` issues may produce safe field hints such as:

```text
Field 'customerId': required.
```

Unknown Tool, invalid arguments root, null context, and blank ToolName are protocol InvalidRequest/UnknownTool classifications. Binder failures are Tool input errors. Output type mismatch or OutputSchema violation is an internal server contract failure.

## 8. Testing and NativeAOT verification

For a first-party MCP runtime change, run at least:

```bash
dotnet test tests/Integrations/CrestCreates.Mcp.Tests/CrestCreates.Mcp.Tests.csproj
dotnet test tests/Integrations/CrestCreates.Mcp.E2E.Tests/CrestCreates.Mcp.E2E.Tests.csproj
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~McpToolGeneratorTests"
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CrestCreates.DependencyBoundaries.Tests.csproj
dotnet test tests/Integrations/CrestCreates.Mcp.AotFixture.Tests/CrestCreates.Mcp.AotFixture.Tests.csproj
```

The NativeAOT fixture is the formal `linux-x64` gate. It performs a real `PublishAot`, native link, and native-binary execution. A trimming-only result is not sufficient to claim the first-party MCP runtime is NativeAOT-verified.

## 9. Current non-goals

Phase 8e does not provide MCP server transport hosting, stdio/SSE/Streamable HTTP, MCP authentication/session protocol, Tasks, progress, resources, prompts, sampling, automatic Capability exposure, approval workflows, hot reload, dynamic CLR DTO generation, reflection schema discovery, or a legacy Dynamic API bridge.

Issue #61 — Generated CRUD Trimming-Safe JSON Contracts — remains a separate technical-debt track. Projected DTOs must already be visible to the application's source-generated JSON context.
