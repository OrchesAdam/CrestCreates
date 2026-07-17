# Phase 8e — MCP Tool Projection Design

**Date**: 2026-07-15
**Status**: Approved
**Issue**: #59
**Related debt**: #61 — Generated CRUD Trimming-Safe JSON Contracts
**Depends on**: Phase 8a Capability Endpoint Projection, Phase 8c Legacy Dynamic API Boundary, Phase 8d AppService Compatibility Projection

## 1. Goal and scope

Phase 8e projects explicitly selected `CapabilityDescriptor` definitions into discoverable and invokable MCP tools. It is a protocol projection over the Capability mainline, not a business runtime and not an adapter over legacy Dynamic API.

```text
[McpToolSpec]
      ↓ Source Generator
McpToolDescriptor + McpToolBindingContract
      ↓ Startup composition and validation
Immutable McpToolRuntimeSnapshot
      ↓ Discovery / invocation
McpToolDiscoveryService / McpToolInvoker
      ↓
ICapabilityDispatcher.DispatchAsync(
    capturedCapabilityDescriptor,
    InvocationSource.Mcp,
    exactTypedInput)
      ↓
CapabilityPipeline → Handler
```

MCP owns protocol metadata, JSON binding, discovery projection, exposure policy integration, and protocol-neutral result mapping. Capability owns schemas, permissions, risk, execution, validation, rate limiting, idempotency, audit, tenant/user propagation, and events.

Phase 8e targets MCP protocol revision `2025-06-18`. It does not model Tasks or `execution.taskSupport`.

### 1.1 Non-goals

Phase 8e does not implement:

- production MCP Server hosting or an official MCP SDK adapter;
- stdio, SSE, Streamable HTTP, MCP authentication, or session storage;
- Tasks, progress, cancellation recovery, elicitation, sampling, resources, or prompts;
- automatic Capability exposure;
- approval, budget, token, or autonomous Agent policies;
- Agent Control Plane replacement or MCP-specific descriptor authoring through that plane;
- hot reload or runtime registry mutation;
- dynamic CLR DTO generation or reflection-based schema discovery;
- legacy Dynamic API or AppService execution bridges;
- Generated CRUD JSON contract debt tracked by issue #61.
- nondeterministic Schema IDs currently emitted by SchemaCapabilitySourceGenerator;
  this is pre-existing identity debt and is not changed by MCP projection.

## 2. Architecture and assembly boundaries

Add a metadata-owned MCP Descriptor contract plus an independent MCP projection vertical slice:

```text
src/Metadata/
└── CrestCreates.Metadata.Mcp.Abstractions/

src/Integrations/
├── CrestCreates.Mcp.Abstractions/
└── CrestCreates.Mcp/

tests/Integrations/
├── CrestCreates.Mcp.Tests/
├── CrestCreates.Mcp.E2E.Tests/
└── CrestCreates.Mcp.AotFixture/
```

`CrestCreates.Metadata.Mcp.Abstractions` contains:

- `McpToolDescriptor`;
- `McpToolAnnotationOverrides` and stored MCP projection metadata;
- only the metadata contracts required by canonical hashing, topology, registry,
  package, and snapshot governance.

Its only project dependency is `Metadata.Abstractions`. It owns the protocol-neutral
`McpCapabilityReference` value contract rather than closing a generic reference over
the Runtime-owned `CapabilityDescriptor`. It does not reference `CrestCreates.Metadata`, any
Integrations project, DynamicApi, ASP.NET Core, or an MCP SDK.

`CrestCreates.Mcp.Abstractions` contains:

- authoring attributes;
- protocol-neutral discovery, call, result, and exposure contracts;
- generated binding contract and registry-facing interfaces.

Its invocation surface contains `McpToolCallContext`, `McpToolHostContext`,
`McpToolInvocationOutcome`, `McpToolProtocolFailureKind`,
`McpToolProtocolException`, and `IMcpToolInvoker`. It does not contain runtime
snapshot or idempotency-builder types.

Its allowed project dependencies are:

```text
Metadata.Mcp.Abstractions
Metadata.Abstractions
Schema.Abstractions
Capability.Abstractions
```

`CapabilityDescriptor` uses the `CrestCreates.Metadata` namespace but belongs to the `CrestCreates.Capability.Abstractions` assembly. Therefore the abstractions project does not need the Metadata runtime assembly.

`CrestCreates.Mcp` contains:

- descriptor registry and validation;
- Capability and Schema resolution;
- immutable runtime snapshot composition;
- JSON Schema projection;
- JsonTypeInfo resolution and validation;
- discovery service;
- exposure policy execution;
- invoker and safe result mapper;
- relationship extractor;
- DI and startup integration.

`McpToolRuntimeEntry`, `McpToolRuntimeBinding`, `McpToolRuntimeSnapshot`,
`IMcpIdempotencyKeyBuilder`, and `DefaultMcpIdempotencyKeyBuilder` belong to this
runtime assembly. The builder is a runtime extension point because its input is
the resolved runtime entry; moving it to Abstractions would create an illegal
reverse dependency or require a duplicate idempotency DTO.

Its allowed dependencies include `Mcp.Abstractions`,
`Metadata.Mcp.Abstractions`, `Metadata`, `Metadata.Abstractions`,
`Capability.Abstractions`, and `Schema.Abstractions`. It must not reference
DynamicApi, ASP.NET Core, AppService, Agent Control Plane, an official MCP SDK,
or a provider-specific MCP SDK.

The current canonical hash dispatcher is generated inside `CrestCreates.Metadata`
as a closed compile-time type switch. `CrestCreates.Metadata` therefore references
the metadata-layer sibling `CrestCreates.Metadata.Mcp.Abstractions` to compile the
McpTool canonical hash profile. It must not reference
`CrestCreates.Mcp.Abstractions` or any other `src/Integrations` project. This
keeps the closed dispatcher without reversing the Metadata-to-Integrations
dependency direction. Reworking canonical hashing into a cross-assembly
contributor system is broader than Phase 8e.

The DynamicApi precedent is not treated as an architectural analogy:
`DynamicApi.Abstractions` resides under Framework/Api, whereas MCP protocol
contracts reside under Integrations. A boundary test freezes that no project
under `src/Metadata` directly references a project under `src/Integrations`.

Future transport projects such as `CrestCreates.Mcp.Server` or `CrestCreates.Mcp.AspNetCore` translate between an official SDK and the protocol-neutral 8e contracts.

## 3. Authority and security boundaries

| Concern | Authority |
|---|---|
| Business execution | `CapabilityDescriptor` + Handler |
| Input/output business schema | Capability's Schema references |
| Permissions | `CapabilityDescriptor.Permissions` |
| Risk | `CapabilityDescriptor.RiskLevel` |
| Query/Command semantics | `CapabilityDescriptor.CapabilityKind` |
| MCP name, description, title, annotation overrides | `McpToolDescriptor` |
| CLR input/output materialization | generated `McpToolBindingContract` |
| Execution authorization and audit | `CapabilityPipeline` |
| Host-level visibility | `IMcpToolExposurePolicy` |

`McpToolDescriptor` must not copy and own permissions, risk, or full schemas. MCP arguments cannot set TenantId, UserId, permissions, risk, or invocation source. The dispatcher obtains TenantId and UserId from authenticated ambient host context.

Agent Control Plane remains a privileged governance business surface. `DescriptorKind.McpTool` participates in generic Metadata registry, hashing, topology, impact, and snapshot capabilities, but is not added to Agent Draft, Authoring, or Control Plane supported-kind allowlists.

## 4. Descriptor and authoring model

Add `DescriptorKind.McpTool = 8`, `DescriptorKindNames.McpTool = "McpTool"`, and descriptor Namespace `mcp-tool`.

```csharp
public sealed class McpToolDescriptor : IDescriptor, IVersionedDescriptor
{
    public string Namespace => "mcp-tool";
    public DescriptorKind Kind => DescriptorKind.McpTool;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }

    public required McpCapabilityReference Capability { get; init; }

    public string ToolName { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public McpToolAnnotationOverrides AnnotationOverrides { get; init; } = new();
}

public readonly record struct McpCapabilityReference(
    string Id,
    int Version,
    VersionSelectionMode SelectionMode = VersionSelectionMode.Exact,
    string? ExpectedContractHash = null);
```

The MCP Runtime resolves this metadata-owned value into the captured
`CapabilityDescriptor`. This avoids both `Metadata → Integrations` and
`Metadata → Runtime` dependencies while preserving the same Exact/Latest semantics.

Default identity rules are:

```text
Descriptor Id      = mcp-tool:{capabilityId}
Descriptor Version = 1
Descriptor Name    = annotated nested spec class name
ToolName           = capabilityId
Capability Version = 0 (latest active)
```

Descriptor identity, ToolName, and Capability identity remain independent. Binding registration uses Descriptor Id and Version. Renaming ToolName does not change the binding key.

### 4.1 Annotation model

ReadOnly is not author-overridable. It is derived from Capability semantics during snapshot composition:

- Query produces `ReadOnlyHint=true`;
- Command produces `ReadOnlyHint=false`.

The other three hints are nullable overrides:

```csharp
public sealed record McpToolAnnotationOverrides
{
    public bool? DestructiveHint { get; init; }
    public bool? IdempotentHint { get; init; }
    public bool? OpenWorldHint { get; init; }
}

public sealed record McpToolAnnotations
{
    public bool ReadOnlyHint { get; init; }
    public bool? DestructiveHint { get; init; }
    public bool? IdempotentHint { get; init; }
    public bool? OpenWorldHint { get; init; }
}
```

Attribute named arguments cannot reliably represent an unspecified nullable Boolean, so the authoring API uses a tri-state enum:

```csharp
public enum McpBooleanHint
{
    Unspecified = 0,
    False = 1,
    True = 2
}
```

`DestructiveHint`, `IdempotentHint`, and `OpenWorldHint` remain absent unless explicitly overridden. Query/Command semantics do not prove these facts. The protocol-neutral contract retains null to mean "omit this property"; an Adapter must omit nullable fields rather than serialize JSON null.

MCP 2025-06-18 defines client defaults for omitted hints: ReadOnly=false, Destructive=true, Idempotent=false, and OpenWorld=true. Because CrestCreates always emits derived ReadOnly, only the other three protocol defaults apply to omitted fields. Annotations are hints and never alter permissions, risk, approval, or execution.

### 4.2 Authoring API

Phase 8e provides one explicit authoring level:

```csharp
[McpToolSpecs]
public static partial class OrderMcpTools
{
    [McpToolSpec(
        "orders.create",
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

The complete authoring contract is:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class McpToolSpecsAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class McpToolSpecAttribute : Attribute
{
    public McpToolSpecAttribute(string capabilityId)
        => CapabilityId = capabilityId;

    public string CapabilityId { get; }
    public string? DescriptorId { get; set; }
    public int DescriptorVersion { get; set; } = 1;
    public int CapabilityVersion { get; set; }
    public Type? InputType { get; set; }
    public Type? OutputType { get; set; }
    public string? ToolName { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public McpBooleanHint DestructiveHint { get; set; }
    public McpBooleanHint IdempotentHint { get; set; }
    public McpBooleanHint OpenWorldHint { get; set; }
}
```

`CapabilityVersion > 0` generates an Exact reference. `CapabilityVersion == 0` generates:

```csharp
new McpCapabilityReference(
    capabilityId,
    0,
    VersionSelectionMode.Latest)
```

Phase 8e authoring does not expose `VersionSelectionMode.Compatible`.

Rules:

- the `[McpToolSpecs]` container must be a top-level, non-generic `static partial class`;
- each `[McpToolSpec]` target must be a direct nested, non-generic class in that container;
- spec classes need not be partial because the generator does not add members to them;
- no input means both InputType and Capability InputSchema are absent;
- no output means both OutputType and Capability OutputSchema are absent;
- input and output must be supported object-root DTOs;
- interface, abstract, open-generic, and dynamic dictionary contracts are rejected;
- multiple CLR arguments are not assembled;
- DTOs are not discovered by runtime scanning;
- CLR DTOs are not generated from SchemaDescriptor;
- Description is required.
- ToolName must satisfy the CrestCreates-specific constraint `^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$`; MCP 2025-06-18 itself only declares the field as a string.
- CapabilityVersion less than zero is a generator error; zero means Latest and a positive value means Exact.

The generator only emits explicit annotation overrides. It cannot inspect runtime CapabilityKind.

## 5. Generated artifacts

Each valid `[McpToolSpecs]` container generates:

1. a descriptor provider registered through `DescriptorProviderRegistry`;
2. binding methods and `McpToolBindingRegistry` registrations;
3. input/output type registrations for startup JsonTypeInfo validation.

```csharp
public sealed class McpToolBindingContract
{
    public required string DescriptorId { get; init; }
    public required int DescriptorVersion { get; init; }
    public Type? InputType { get; init; }
    public Type? OutputType { get; init; }
    public required Func<JsonElement, JsonTypeInfo?, CancellationToken,
        ValueTask<object?>> BindInputAsync { get; init; }
    public required Func<object?, JsonTypeInfo?, CancellationToken,
        ValueTask<JsonElement?>> SerializeOutputAsync { get; init; }
}
```

The binding contract deliberately has reference identity. Delegate equality is
not a registry or validation concept. Registration and lookup use only
`DescriptorId + DescriptorVersion`; startup separately validates registered
types and binding identity against the Descriptor.

The generated registration contract is not the per-call executable object. Startup resolves and freezes JSON metadata into:

```csharp
public sealed record McpToolRuntimeBinding(
    McpToolBindingContract Contract,
    JsonTypeInfo? InputTypeInfo,
    JsonTypeInfo? OutputTypeInfo);
```

Generated delegates validate that supplied metadata is the expected `JsonTypeInfo<T>`, deserialize arguments to exact `TInput`, check output is exact `TOutput`, and serialize output to `JsonElement`. Per-call code does not resolve metadata from IServiceProvider. Generated code must not emit reflection serializer fallback, direct Handler invocation, `Dictionary<string, object?>` conversion, DynamicApi symbols, ASP.NET symbols, or official MCP SDK symbols.

Any Error diagnostic in a container suppresses all Provider and Binding output for that container.

## 6. JSON contracts and trimming safety

MCP owns independent JSON options and does not depend on ASP.NET `JsonOptions`:

```csharp
public sealed class McpJsonOptions
{
    public JsonSerializerOptions SerializerOptions { get; } = new();
}
```

Applications register their own source-generated context:

```csharp
services.Configure<McpJsonOptions>(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(
        0,
        ApplicationJsonContext.Default);
});
```

All projected input and output types require `JsonTypeInfo`. MCP provides no `DefaultJsonTypeInfoResolver` or reflection fallback. Because schema/type parity reads `JsonTypeInfo.Properties`, the application context must provide Metadata generation for projected types; a serialization-only fast path is insufficient.

The configured JSON options must leave `RespectNullableAnnotations` and
`RespectRequiredConstructorParameters` disabled. If either option is enabled,
snapshot construction fails with MCP114 because STJ could reject nullable or
missing input members before the captured Capability reaches Dispatcher and the
Capability Pipeline.

During startup, MCP copies the configured JsonSerializerOptions, requires either one application-owned `JsonSerializerContext` or a resolver chain whose entries are all source-generated contexts, rejects `DefaultJsonTypeInfoResolver`, resolves and caches every required JsonTypeInfo, and calls `MakeReadOnly()` before publishing the snapshot. The framework-created composite resolver facade for a valid chain is accepted. Runtime bindings hold the resolved JsonTypeInfo objects and do not consult mutable options or IServiceProvider again. Application-provided custom converters remain the application's trimming responsibility and are exercised by the publish fixture.

Issue #61 remains independent. MCP specs may reference only DTOs visible to the application's STJ generator and explicitly included in its `JsonSerializerContext`. Phase 8e does not claim that same-round Generated CRUD DTOs are trimming-safe.

### 6.1 Capability validation input

The current object-based `SchemaValidator` serializes payloads without JsonTypeInfo. That would reintroduce reflection into the MCP path. Add a protocol-neutral canonical JSON input to Capability execution:

```csharp
public sealed class CapabilityExecutionContext
{
    public object? Input { get; set; }
    public JsonElement? InputJson { get; set; }
}
```

Add `ISchemaValidator.Validate(SchemaDescriptor, JsonElement)`. MCP retains a clone of validated arguments, materializes exact `TInput` for the Handler, and assigns the JSON clone to `InputJson`. Validation Middleware prefers `InputJson` and never serializes the MCP typed input back to JSON.

The precedence is executable contract, not convention:

```csharp
var validationResult = context.InputJson.HasValue
    ? _schemaValidator.Validate(schema, context.InputJson.Value)
    : _schemaValidator.Validate(schema, context.Input);
```

When both properties are populated, `InputJson` is authoritative. Tests use an
object payload whose fallback serialization would fail and prove that the object
overload is never executed while `InputJson.HasValue` is true.

This is a reusable Capability contract, not an MCP-specific `Items` string key. Other invocation sources may adopt it later. Their existing object fallback is outside Phase 8e.

## 7. Schema projection and parity

Capability Schema is the only source for MCP discovery schemas. JsonTypeInfo is used for CLR alignment and serialization, not as a second schema authority.

Schema and CLR types are strictly paired:

| Schema | CLR type | Result |
|---|---|---|
| absent | absent | valid no-input/no-output contract |
| present | present | validate parity |
| present | absent | startup error |
| absent | present | startup error |

Phase 8e supports object roots whose fields are:

- string, Boolean, integer, or number;
- explicitly supported date, date-time, and UUID string formats;
- collections of those primitive shapes;
- required, nullable, length, and numeric range constraints.

Nested objects, dictionaries, enum sets, unions, field-level schema references, unknown FieldType values, and unsupported collection shapes fail startup. They do not degrade to `{}`.

MCP accepts the following closed, case-sensitive Schema token vocabulary. The
CLR-display tokens are existing `SchemaCapabilitySourceGenerator` output; the
lowercase format tokens support existing handwritten Schemas. Each listed token
has one fixed meaning. MCP performs no arbitrary case normalization and accepts
no unlisted aliases such as `GUID`, `System.Guid`, or `uuid`:

| Accepted Schema token | JSON Schema |
|---|---|
| `string` | `type: string` |
| `bool` | `type: boolean` |
| `int`, `long` | `type: integer` |
| `decimal`, `double` | `type: number` |
| `date`, `DateOnly` | `type: string, format: date` |
| `datetime`, `DateTime`, `DateTimeOffset` | `type: string, format: date-time` |
| `guid`, `Guid` | `type: string, format: uuid` |

The validation rules are split by shape:

- when IsCollection=false, FieldType must come from the primitive vocabulary and CollectionElementType must be null or empty;
- when IsCollection=true, FieldType is legacy/container display metadata and is not vocabulary-validated or used to determine JSON element shape; CollectionElementType is authoritative and must come from the primitive vocabulary.

Therefore existing generated values such as `FieldType="IList<string>"`, `IsCollection=true`, and `CollectionElementType="string"` remain valid.

For a collection, type/format and string/numeric constraints apply to each element and are emitted under `items`; the current Schema model has no array-cardinality constraints. Null collection elements are unsupported because SchemaFieldDescriptor has no element-nullability contract. Field-level IsNullable applies to the collection value itself.

Phase 8e rejects every non-empty Pattern at startup. The current validator uses .NET Regex while JSON Schema pattern uses ECMA-262 semantics; passing arbitrary expressions through would make discovery and execution disagree. A future portable-pattern profile can add an explicitly validated subset.

This rejection belongs only to MCP Active-candidate snapshot validation. The
generic SchemaValidator continues to execute existing non-empty Pattern values
with its current .NET Regex semantics for other invocation sources. MCP runtime
never receives a Pattern-bearing Schema because snapshot publication rejects it.

Active MCP Tools reject any referenced Schema with non-empty `ValidationRules` or non-empty `References`. Phase 8e cannot faithfully project those semantics, so discovery must not publish a weaker contract.

Schema field names must be non-empty and unique under StringComparer.Ordinal. Duplicate Schema field names fail startup before canonical projection.

The existing SchemaCapability Source Generator continues to emit its established
CLR display names such as `Guid`, `DateTime`, `DateOnly`, and `IList<Guid>`.
Phase 8e must not rewrite `FieldType` or `CollectionElementType`: both participate
in Schema DefinitionHash, and required fields also participate in Schema
ContractHash. Renaming them would propagate into compatibility analysis,
DescriptorPackage hashes, snapshots, and persisted expected hashes.

MCP projection and executable validation translate the closed token vocabulary
locally without mutating the Schema Descriptor. Generator regression tests freeze
the existing emitted strings. Any future global Schema vocabulary normalization
is a separate migration event requiring canonical-hash shape versioning,
compatibility rules, and DescriptorPackage republication; it is not part of 8e.

Parity validation compares Schema fields with actual JSON property names in `JsonTypeInfo.Properties` in both directions. Input parity requires a deserializable setter/constructor binding and uses JsonPropertyInfo.IsSetNullable. Input `JsonPropertyInfo.IsRequired` must always be false: Schema owns input presence validation, and JSON-required metadata would make the generated binder reject a missing property before Dispatcher/Pipeline. Output parity requires a serializable getter, uses JsonPropertyInfo.IsGetNullable, and requires JsonPropertyInfo.IsRequired to match Schema `IsRequired`. It also rejects properties that are unconditionally excluded from serialization. Any directionally participating JSON property absent from Schema fails startup. Conditional omission from ignore conditions or ShouldSerialize cannot be proven statically, so runtime OutputSchema validation remains authoritative for actual required-property presence. Both directions check exact primitive category, scalar/collection shape, and collection element category; the implementation must not choose one nullable Boolean for both read and write contracts.

Every projected input and output schema sets `additionalProperties=false`. A no-input contract is exactly:

```json
{
  "type": "object",
  "properties": {},
  "additionalProperties": false
}
```

The shared `JsonElement` Schema validator enforces the same closed-object rule
with `StringComparer.Ordinal`; unknown and duplicate properties are validation
failures for both input and actual serialized output.

Canonical projection rules are:

- IsRequired controls membership in the root required array;
- IsNullable independently controls whether the property's type includes null;
- nullable scalars use `"type":["<scalar>","null"]`;
- nullable collections use `"type":["array","null"]`, while items remain non-nullable;
- properties are written by JSON property name using StringComparer.Ordinal;
- required names are sorted using StringComparer.Ordinal;
- ToolName uniqueness and lookup use StringComparer.Ordinal;
- root keyword order is type, properties, required when non-empty, additionalProperties;
- property keyword order is type, format, items, minLength, maxLength, minimum, maximum, omitting inapplicable keys.

Requiredness and nullability remain independent:

| IsRequired | IsNullable | Meaning |
|---:|---:|---|
| false | false | may be absent; non-null when present |
| false | true | may be absent; may be null when present |
| true | false | must be present and non-null |
| true | true | must be present and may be null |

Integer projection merges CLR bounds with explicit Schema bounds. Int always emits a minimum no lower than Int32.MinValue and a maximum no higher than Int32.MaxValue. Long does the same with Int64 bounds. The projector writes inherent bounds with the `Utf8JsonWriter` Int32/Int64 overloads, never by converting them to double. Explicit MinValue/MaxValue may narrow but never widen those ranges; contradictory ranges fail startup.

Because existing Schema constraint storage is `double?`, an explicit bound on an
`int` or `long` field must be finite, mathematically integral, convertible to
`decimal` in a checked operation, and within the corresponding CLR range. Merge
and comparison use decimal values; the inherent Int64 bounds remain exact integer
literals. A rounded double representing a value outside Int64 range is rejected
rather than clamped to the nearest long value. Changing Schema constraint storage
away from `double?` is outside Phase 8e.

Active-candidate Schema constraint metadata is validated before projection:

| Shape | Allowed constraints |
|---|---|
| string, date, datetime, guid | MinLength, MaxLength |
| int, long, decimal, double | MinValue, MaxValue |
| bool | none |
| collection | the constraints allowed by CollectionElementType, applied to each element |
| any | Pattern is rejected for MCP |

MinLength and MaxLength must be non-negative and MinLength must not exceed
MaxLength. MinValue and MaxValue must be finite and MinValue must not exceed
MaxValue. A constraint on an inapplicable shape, such as bool plus MaxLength,
string plus MinValue, or int plus MaxLength, is MCP121 and fails startup rather
than being ignored.

Lexical validation is fixed and shared by input and output validation:

- date is exactly `yyyy-MM-dd`;
- datetime is RFC 3339 date-time and requires Z or an explicit numeric offset;
- guid is canonical hyphenated D form and accepts hexadecimal digits
  case-insensitively, equivalent to `Guid.TryParseExact(value, "D", out _)`;
  braces and the compact 32-digit form are rejected.

The invoker enumerates the normalized arguments object before binding and rejects duplicate JSON property names using StringComparer.Ordinal. The shared SchemaValidator core also rejects duplicates for both input and serialized output, covering custom output converters. This prevents Schema validation and STJ binding from observing different first/last-value semantics.

Arguments must normalize to a JSON object. Unknown properties are rejected before deserialization, even if application serializer options normally ignore them. A no-input Tool accepts only an empty object. These binding errors are Tool execution errors so a model can repair its call.

The Capability validation closure expands the existing SchemaValidator so projection and execution recognize the same subset. The JsonElement path must validate object root, scalar types, integer versus number, arrays, collection element types, strict date/date-time/UUID lexical formats, required/nullability, and all applicable scalar or collection constraints. Unknown FieldType and unsupported shapes fail closed rather than validate successfully.

SchemaValidator refactors these rules into one shared JsonElement validation core. The new JsonElement overload calls it directly; the legacy object overload may retain its compatibility serialization step and then call the same core. This prevents the MCP path and other invocation sources from implementing different constraint semantics.

## 8. Registry and immutable runtime snapshot

Runtime validation is authoritative and does not trust that descriptors came from the Source Generator. Handwritten providers are allowed by the abstractions contract and receive the same checks. `McpToolDescriptorValidator` revalidates:

- non-empty Descriptor Id satisfying the shared Descriptor Id constraint;
- non-empty Descriptor Name;
- Descriptor Version greater than zero;
- non-null AnnotationOverrides;
- ToolName satisfying the MCP002 CrestCreates constraint;
- non-empty Description;
- non-empty Capability Id.

Capability reference combinations are exact:

| SelectionMode | Version | Runtime result |
|---|---:|---|
| Exact | `> 0` | supported |
| Latest | `0` | supported and captured once at snapshot build |
| Exact | `<= 0` | startup error |
| Latest | any value other than `0` | startup error |
| Compatible | any | startup error |
| unknown enum | any | startup error |

Phase 8e does not support ExpectedContractHash. A non-null value fails startup. Capability InputSchema and OutputSchema references must both use SelectionMode.Exact, Version greater than zero, and null ExpectedContractHash. MCP discovery always captures a deterministic Schema version; Latest and Compatible Schema references fail startup.
An Active MCP Tool must resolve an Active Capability for both Exact and Latest
references; Draft, Removed, and Deprecated Capabilities are never projected
implicitly.

Startup composition is:

```text
Descriptor providers
  → McpToolRegistry
  → identity/state validation
  → exact/latest-active Capability resolution
  → exact Schema version resolution
  → generated binding lookup
  → JsonTypeInfo validation
  → schema/type parity
  → effective annotations
  → immutable McpToolRuntimeSnapshot
```

```csharp
public sealed record McpToolRuntimeEntry(
    McpToolDescriptor Descriptor,
    CapabilityDescriptor Capability,
    SchemaDescriptor? InputSchema,
    SchemaDescriptor? OutputSchema,
    McpToolRuntimeBinding Binding,
    McpToolContract DiscoveryContract,
    CanonicalHash ToolContractHash,
    CanonicalHash CapabilityContractHash,
    CanonicalHash? InputSchemaContractHash,
    CanonicalHash? OutputSchemaContractHash);
```

The entry does not duplicate Capability governance fields. Exposure contexts project the required read-only values directly from the captured CapabilityDescriptor.

McpToolRegistry retains all descriptor versions and lifecycle states for Metadata governance. McpToolRuntimeSnapshot contains only Active, fully validated, runtime-ready entries and indexes ToolName with `FrozenDictionary<string, McpToolRuntimeEntry>` using `StringComparer.Ordinal`. A successful lookup already proves lifecycle readiness; invocation does not recheck immutable State.

Validation is lifecycle-aware. Every Descriptor state receives identity,
version, reference-syntax, annotation-shape, and canonical-hash validation.
Only Active runtime candidates perform Capability/Schema resolution, global
ToolName uniqueness, generated-binding lookup, JsonTypeInfo validation, schema
parity, and discovery-contract construction. Historical Superseded or Deprecated
Descriptors do not block Active snapshot publication merely because their old
runtime binding or JsonTypeInfo is no longer present.

`AddCrestMcpToolProjection()` registers `McpToolProjectionStartupValidator` as
an `IHostedService`. `StartAsync` idempotently builds the authoritative Schema
and Capability registries first, then builds `McpToolRegistry` from generated
Descriptor providers and resolves the singleton snapshot, so Host startup
eagerly validates the complete projection and propagates configuration
failures before the Host reports started. Discovery and invocation never trigger
first-time snapshot composition.

Calls do not re-resolve Tool, Capability, Schema, or Binding. Registries remain immutable after build; activation and version changes require restart.

DescriptorKind integration is atomic: adding `DescriptorKind.McpTool = 8` also
adds `DescriptorKindNames.McpTool` and the `ToCanonicalString` switch arm in the
same change. Agent-specific `AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind`
remains false for McpTool because it is effectively the Agent Draft/Authoring/
Control Plane allowlist, not the global canonical-name mapping.

The current Capability Validation Middleware resolves the Capability by exact version but resolves its InputSchema with `GetById()`. Phase 8e includes a targeted mainline correction: resolve `InputSchema.Id + InputSchema.Version` exactly. This keeps runtime validation aligned with the snapshot and referenced contract version.

## 9. Discovery and exposure policy

Internal descriptors are not returned directly to transport adapters:

```csharp
public sealed record McpToolContract(
    string Name,
    string? Title,
    string Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    McpToolAnnotations Annotations);
```

```csharp
public sealed record McpToolHostContext(
    string HostId,
    string EnvironmentName,
    string? ProfileName = null);

public sealed record McpToolDiscoveryContext(
    McpToolHostContext Host);

public interface IMcpToolDiscoveryService
{
    ValueTask<IReadOnlyList<McpToolContract>> ListAsync(
        McpToolDiscoveryContext context,
        CancellationToken cancellationToken = default);
}
```

HostId and EnvironmentName are required, non-empty stable values. A trusted Server Adapter or Host DI scope creates this context; Tool arguments and model-controlled content cannot populate or override it.

Discovery reads the immutable snapshot, applies exposure policy, and sorts ToolName using `StringComparer.Ordinal`. It does not expose permissions, risk, Capability identity, or internal governance metadata by default.

```csharp
public interface IMcpToolExposurePolicy
{
    ValueTask<McpToolExposureDecision> EvaluateAsync(
        McpToolExposureContext context,
        CancellationToken cancellationToken = default);
}
```

```csharp
public enum McpToolExposurePhase
{
    Discovery,
    Invocation
}

public sealed record McpToolExposureContext(
    McpToolHostContext Host,
    McpToolExposurePhase Phase,
    string DescriptorId,
    int DescriptorVersion,
    string ToolName,
    string CapabilityId,
    int CapabilityVersion,
    CapabilityKind CapabilityKind,
    CapabilityRiskLevel RiskLevel,
    IReadOnlyList<string> RequiredPermissions);

public sealed record McpToolExposureDecision(
    bool IsAllowed,
    string? InternalReasonCode = null);
```

The default policy permits every Active, explicitly projected Tool that entered the snapshot successfully.

The same policy runs during discovery and invocation. A hidden Tool cannot be invoked by guessing its name. Exposure policy is a Host visibility boundary, not a replacement for Capability authorization.

Policy exceptions fail closed. Discovery omits the affected Tool and records an internal diagnostic; it never defaults to visible. Invocation stops before binding or dispatch and records `MCP_TOOL_EXPOSURE_POLICY_FAILURE`.

External mapping distinguishes decisions from faults:

- nonexistent Tool and policy-denied Tool both map to the same UnknownTool protocol classification;
- policy exception maps to a generic InternalServer protocol classification and never enters binder or dispatcher;
- external messages do not reveal policy internals.

## 10. Invocation

```csharp
public sealed record McpToolCallContext(
    McpToolHostContext Host,
    string InvocationId,
    string RequestId,
    string? SessionId = null);

public interface IMcpToolInvoker
{
    ValueTask<McpToolInvocationOutcome> InvokeAsync(
        string toolName,
        JsonElement? arguments,
        McpToolCallContext context,
        CancellationToken cancellationToken = default);
}

public interface IMcpIdempotencyKeyBuilder
{
    string Build(
        McpToolRuntimeEntry entry,
        McpToolCallContext context);
}
```

`IMcpIdempotencyKeyBuilder` and its default implementation are declared in
`CrestCreates.Mcp`, alongside `McpToolRuntimeEntry`. They are not protocol
contracts and are not declared in `CrestCreates.Mcp.Abstractions`.

HostId, EnvironmentName, InvocationId, and RequestId are required and non-empty. InvocationId is generated by the trusted Server Adapter or Host, not by Tool arguments. It is stable for repeated delivery of the same logical MCP call and distinct for different logical calls within the Host idempotency domain. `ActorId`, TenantId, UserId, permissions, risk, public IdempotencyKey, and invocation source are not accepted from the call contract or arguments.

MCP RequestId may be a string or number. The Adapter converts it to a canonical string before constructing McpToolCallContext: string IDs use `s:{value}` and numeric IDs use `n:{invariant-value}`. RequestId is used for causation only, never idempotency.

MCP 2025-06-18 makes `arguments` optional. The invoker normalizes absent arguments to an empty object before validation:

- no-input Tool plus absent arguments is valid;
- no-input Tool plus `{}` is valid;
- no-input Tool plus a non-empty object is a Tool input error;
- input Tool plus absent arguments is normalized to `{}` and evaluated against required fields.

Invocation order is:

1. look up the snapshot entry by ToolName;
2. apply exposure policy;
3. normalize absent arguments, then validate object root and closed property set;
4. generated binder materializes exact `TInput`;
5. dispatch the captured CapabilityDescriptor with `InvocationSource.Mcp`;
6. execute the Capability Pipeline;
7. generated serializer validates and serializes exact `TOutput`, then runtime validates the value against OutputSchema;
8. map the result through the safe MCP result mapper.

The protocol boundary classifies malformed outer inputs before dereferencing them. A
null `McpToolCallContext` is `MCP_INVALID_CALL_CONTEXT`, a null
`McpToolDiscoveryContext` is `MCP_INVALID_DISCOVERY_CONTEXT`, and a null or
blank ToolName is `MCP_INVALID_TOOL_NAME`; all use the InvalidRequest protocol
classification rather than leaking `NullReferenceException` or dictionary
argument exceptions.

Context propagation is:

```csharp
public static class McpCapabilityContextItemNames
{
    public const string ToolDescriptorId = "McpToolDescriptorId";
    public const string ToolDescriptorVersion = "McpToolDescriptorVersion";
    public const string ToolName = "McpToolName";
    public const string RequestId = "McpRequestId";
    public const string SessionId = "McpSessionId";
    public const string HostId = "McpHostId";
    public const string InvocationId = "McpInvocationId";
}

ctx.CausationId = callContext.RequestId;
ctx.IdempotencyKey = idempotencyKeyBuilder.Build(entry, callContext);

ctx.Items[McpCapabilityContextItemNames.ToolDescriptorId] = entry.Descriptor.Id;
ctx.Items[McpCapabilityContextItemNames.ToolDescriptorVersion] = entry.Descriptor.Version;
ctx.Items[McpCapabilityContextItemNames.ToolName] = entry.Descriptor.ToolName;
ctx.Items[McpCapabilityContextItemNames.RequestId] = callContext.RequestId;
ctx.Items[McpCapabilityContextItemNames.SessionId] = callContext.SessionId;
ctx.Items[McpCapabilityContextItemNames.HostId] = callContext.Host.HostId;
ctx.Items[McpCapabilityContextItemNames.InvocationId] = callContext.InvocationId;
```

Using the existing `Items` dictionary is intentional; occasional value-type
boxing is accepted in exchange for the established extensibility boundary. The
dispatcher accepts `object?` by Capability design, but the exact runtime input
type produced by the binder is preserved through that reference. Output is still
checked with `output.GetType() == typeof(TOutput)` before serialization.

DefaultMcpIdempotencyKeyBuilder writes one unambiguous canonical payload in this fixed order: shapeVersion=`mcp-idempotency-v1`, hostId, toolContractHash, capabilityContractHash, inputSchemaContractHash (explicit null when absent), outputSchemaContractHash (explicit null when absent), invocationId. It hashes the canonical UTF-8 bytes with SHA-256, Base64Url-encodes the digest without padding, and returns `mcp:v1:{digest}`. No separator-delimited business values enter the final key. Binding all resolved contract hashes prevents replay across Tool changes, Descriptor version changes, Latest Capability resolution changes, Schema changes, or same-version contract drift.

The API does not include an ineffective CorrelationId property. MCP RequestId maps to CausationId. Capability Runtime continues to generate its authoritative CorrelationId using the existing pipeline behavior. Phase 8e does not introduce an MCP-controlled or Host-provided CorrelationId.

Protocol exceptions carry stable classifications rather than relying on message parsing:

```csharp
public enum McpToolProtocolFailureKind
{
    UnknownTool,
    InvalidRequest,
    InternalServer
}

public class McpToolProtocolException : Exception
{
    protected McpToolProtocolException(
        McpToolProtocolFailureKind failureKind,
        string internalCode,
        string safeMessage,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        FailureKind = failureKind;
        InternalCode = internalCode;
    }

    public McpToolProtocolFailureKind FailureKind { get; }
    public string InternalCode { get; }
}

internal sealed class McpToolContractViolationException
    : McpToolProtocolException
{
    internal McpToolContractViolationException(
        string internalCode,
        string safeMessage,
        Exception? innerException = null)
        : base(
            McpToolProtocolFailureKind.InternalServer,
            internalCode,
            safeMessage,
            innerException)
    {
    }
}
```

Unknown or policy-denied Tool names use UnknownTool; malformed non-object roots use InvalidRequest; policy faults and broken runtime contracts use InternalServer. Deserialization, unknown or duplicate fields, Capability validation, authorization, rate limiting, timeout, and business failures return Tool outcomes with `IsError=true`.

## 11. Result and error mapping

The protocol-neutral result types are:

```csharp
public abstract record McpToolContent;

public sealed record McpToolTextContent(string Text)
    : McpToolContent;

public sealed record McpToolInvocationOutcome(
    bool IsError,
    IReadOnlyList<McpToolContent> Content,
    JsonElement? StructuredContent,
    string? InternalErrorCode = null);
```

InternalErrorCode is for adapter-side classification, telemetry, and audit correlation. An Adapter must not emit it as a non-standard MCP Tool Result field.

The success path is:

```text
CapabilityPipeline success
  → validate output presence
  → validate exact TOutput
  → serialize once to JsonElement
  → validate JsonElement against captured OutputSchema
  → produce StructuredContent and TextContent
```

Successful typed output becomes StructuredContent, and its raw JSON becomes text content for clients without structured-output support. Successful void output contains a stable completion text and no StructuredContent.

Runtime output rules are fail-closed:

- absent OutputSchema and OutputType plus non-null Handler output is `MCP_TOOL_UNEXPECTED_OUTPUT`;
- present OutputSchema plus null output is `MCP_TOOL_MISSING_OUTPUT`;
- output whose runtime `GetType()` is not exactly `typeof(TOutput)` is `MCP_TOOL_OUTPUT_TYPE_MISMATCH`; derived instances are rejected;
- serialized output that fails the captured OutputSchema is `MCP_TOOL_OUTPUT_SCHEMA_VIOLATION`;
- invalid StructuredContent is never returned.

These cases throw `McpToolContractViolationException`. They are internal server/contract failures, not Tool execution errors that a model can repair by changing arguments. External messages do not expose CLR type names, Schema internals, or raw exceptions.

Error objects must not be placed in StructuredContent when the Tool declares a business output schema; they generally do not satisfy that schema. Tool execution error outcomes contain safe text and no StructuredContent.

Current `CapabilityExecutionResult` exposes only a concatenated ErrorMessage. Add a reusable structured issue contract:

```csharp
public sealed record CapabilityExecutionIssue(
    string Code,
    string? FieldPath);
```

`CapabilityExecutionResult.Issues` defaults to an empty list. Its factory becomes:

```csharp
public static CapabilityExecutionResult Failure(
    string errorCode,
    string errorMessage,
    TimeSpan duration,
    IReadOnlyList<CapabilityExecutionIssue>? issues = null);
```

Validation Middleware maps SchemaValidationError code and field name without copying its raw message. The MCP mapper uses an allowlist to produce safe, repairable field guidance.

Authorization, rate limit, timeout, and unknown business failures receive stable generic messages. Raw ErrorMessage, stack traces, inner exceptions, SQL, authorization policy details, internal service types, and unsanitized audit data are never returned.

Phase 8e does not return an audit reference. The current AuditMiddleware creates its own execution identity and attempts persistence without copying that identity into CapabilityExecutionResult. MCP invocation still traverses the existing AuditMiddleware; exposing an audit identifier is future work.

## 12. Metadata, topology, and canonical hashing

Add `McpToolRelationshipExtractor`:

```text
McpToolDescriptor
  -- References / Strong / Role=Capability -->
CapabilityDescriptor
```

Synchronize every explicit DescriptorKind mapping or allowlist. Generic Metadata services support McpTool registry, canonical hash, topology, impact, and snapshot/package traversal. Agent Draft, Authoring, and Control Plane supported-kind lists continue to reject McpTool.

ContractHash includes:

- Descriptor Id, Name, Version, State, and SupersededById, matching existing descriptor hash conventions;
- ToolName;
- Capability reference Id, Version, SelectionMode, and ExpectedContractHash;
- Description;
- three annotation overrides.

DefinitionHash additionally includes Title. It does not duplicate permissions, risk, or Schema content. It hashes authoritative annotation overrides rather than effective derived ReadOnlyHint. Protocol revision is fixed at the phase boundary and is not a per-Tool hash field.

The existing shared Capability-ref canonical profile excludes SelectionMode and ExpectedContractHash, so the McpTool profile must use a dedicated value profile that writes Id, Version, SelectionMode, and ExpectedContractHash. Exact and Latest references must not hash identically when their Id and numeric Version happen to match, and invalid handwritten descriptors that differ only by ExpectedContractHash must remain hash-distinct before runtime validation rejects them.

Schema changes propagate through the existing Capability-to-Schema relationship and impact graph.

DescriptorPackage currently serializes manifest, snapshot, relationships, and
hash entries, not polymorphic concrete Descriptor payloads. Package coverage for
McpTool therefore round-trips its Ref, Kind, ContractHash, and DefinitionHash.
It does not introduce a new McpToolDescriptor deserialization contract merely to
exercise the `required init` Capability property; generated providers construct
that Descriptor directly. A future payload serializer must own a source-generated
JsonTypeInfo contract and its own round-trip tests.

## 13. Diagnostics

### 13.1 Generator diagnostics

| Code | Meaning |
|---|---|
| MCP001 | invalid CapabilityId |
| MCP002 | ToolName violates the CrestCreates name constraint |
| MCP003 | duplicate ToolName in one generation container |
| MCP004 | empty Description |
| MCP005 | non-positive Descriptor version |
| MCP006 | unsupported Input type |
| MCP007 | unsupported Output type |
| MCP008 | invalid or duplicate Descriptor Id in one container |
| MCP009 | annotation override value is outside the defined tri-state enum |
| MCP010 | invalid `[McpToolSpecs]` container declaration |
| MCP011 | invalid `[McpToolSpec]` nested declaration |
| MCP012 | negative CapabilityVersion |

### 13.2 Startup validation issues

| Code | Meaning |
|---|---|
| MCP101 | global Active ToolName conflict |
| MCP102 | Descriptor identity conflict |
| MCP103 | Capability resolution failure |
| MCP104 | Input Schema/CLR type mismatch in presence |
| MCP105 | Output Schema/CLR type mismatch in presence |
| MCP106 | missing generated binding |
| MCP107 | missing input/output JsonTypeInfo |
| MCP108 | Schema/JsonTypeInfo parity failure |
| MCP109 | unsupported scalar FieldType or collection CollectionElementType vocabulary |
| MCP110 | unsupported Schema root or shape |
| MCP111 | unsupported non-empty Schema ValidationRules |
| MCP112 | unsupported non-empty Schema References |
| MCP113 | Binding identity/type mismatch |
| MCP114 | invalid or reflection-capable MCP JSON serializer configuration |
| MCP115 | JsonTypeInfo root kind is not Object |
| MCP116 | invalid handwritten or generated McpTool descriptor contract |
| MCP117 | unsupported Capability reference selection/version combination |
| MCP118 | Capability Schema reference is not exact and versioned |
| MCP119 | unsupported non-null ExpectedContractHash |
| MCP120 | unsupported non-empty Pattern |
| MCP121 | invalid or inapplicable Schema constraint metadata |

Startup issues are not Roslyn diagnostics. Any Error prevents snapshot publication, places the registry in Failed state, and requires restart.

Stable internal runtime codes used for telemetry, audit classification, and safe adapter mapping are:

```text
MCP_TOOL_OUTPUT_TYPE_MISMATCH
MCP_TOOL_OUTPUT_SCHEMA_VIOLATION
MCP_TOOL_UNEXPECTED_OUTPUT
MCP_TOOL_MISSING_OUTPUT
MCP_TOOL_EXPOSURE_POLICY_FAILURE
```

They are neither Roslyn diagnostics nor startup validation codes.

## 14. Testing strategy

### 14.1 Generator tests

Cover provider, binding, type registration, typed input/output, no-input, void-output, DTO fields containing nullable primitives and primitive collections, legal container/spec declarations, MCP010/MCP011 declaration failures, negative/zero/positive CapabilityVersion semantics, preservation of existing Schema generator tokens for Guid/DateTime/DateOnly and collections, identity separation, tri-state annotations, container-level fail-closed behavior, and rejected interface/abstract/open-generic/dynamic-dictionary root types. Generated-source guards must reject reflection serialization, Handler invocation, DynamicApi, ASP.NET, and official MCP SDK symbols.

### 14.2 Runtime tests

Cover registry/snapshot build, handwritten-provider runtime validation, every Capability reference selection/version combination, non-exact Schema references, ExpectedContractHash rejection and hashing, exact/latest-active Capability resolution, exact Schema version resolution, lifecycle-aware validation and Active-only FrozenDictionary snapshot semantics, identity and ToolName conflicts, binding class reference identity and keyed lookup, missing binding/JsonTypeInfo, frozen reflection-free serializer configuration, byte-stable canonical JSON Schema projection, all required/nullable combinations, exact Int32/Int64 inherent bounds and checked decimal merging of explicit double constraints, duplicate JSON properties, MCP-only Pattern rejection, invalid/inapplicable constraint rejection, strict lexical formats including upper- and lowercase UUID D forms, directional input/output parity, absent arguments normalization, unknown fields, InputJson-over-Input precedence without object fallback execution, exact input/output types, base TOutput plus derived runtime instance rejection, missing/unexpected/type-mismatched/schema-invalid outputs, denied versus faulted exposure mapping, multi-Host policy isolation, ordinal discovery ordering, safe error mapping, canonical idempotency collision cases and contract-version isolation, MCP context item constants, ambient TenantId/UserId, and `InvocationSource.Mcp`.

### 14.3 Capability mainline tests

Freeze exact InputSchema version resolution, JsonElement validation without object reserialization, exact typed Handler input, object-root validation, integer/number distinction, collection element validation, strict date/date-time/UUID validation, unknown FieldType failure in the shared validator, preservation of existing .NET Regex Pattern validation for non-MCP callers, output-schema validation, structured execution issues, and parity between canonical JSON Schema projection and executable Schema validation. MCP snapshot tests separately freeze rejection of every non-empty Pattern.

### 14.4 Boundary tests

Freeze these boundaries:

```text
Mcp.Abstractions
  → Metadata.Mcp.Abstractions
  × DynamicApi / ASP.NET / AppService / Agent.ControlPlane / official MCP SDK

Metadata
  × Integrations

Mcp Runtime
  × DynamicApi implementation / legacy generators / direct Handler invocation

Mcp generated output
  × Dictionary<string, object?> fallback
  × reflection JSON fallback
  × Results.Json(object)
```

Also verify that Agent supported-kind manifests do not accidentally add McpTool.

### 14.5 E2E and NativeAOT fixture

Use a source-generator-backed host without MCP transport. Cover Query and Command input/output, absent arguments, no-input Query, void Command, unauthorized and validation failures, exposure denial and policy failure, two distinct Host profiles, high-risk metadata supplied to policy, missing/unexpected/type-mismatched/schema-invalid output, missing JsonTypeInfo startup failure, and deterministic discovery.

The NativeAOT fixture performs a real `PublishAot`, completes native linking, and executes the resulting native binary through input deserialization, `InputJson` schema validation, Dispatcher/Pipeline/Handler, output serialization, and discovery schema generation. The formal gate is pinned to `linux-x64`; other operating systems and architectures skip this fixture rather than claiming a portable NativeAOT result. It must produce no MCP-path IL2026/IL3050 warnings and must not rely on Generated CRUD DTOs from issue #61. A PublishTrimmed-only result is not sufficient for the first-party MCP runtime.

## 15. Delivery slices

1. **Descriptor kernel**: projects, DescriptorKind/name, contracts, registry, validator, relationship extractor, canonical hash, DI foundation, and boundaries.
2. **Generator and binding**: attributes, model/normalizer, provider/binding emitters, type registration, diagnostics, and generator tests.
3. **Schema and snapshot**: JSON Schema projector, JsonTypeInfo validation, parity, exact resolution, exposure policy, immutable snapshot, and discovery.
4. **Capability validation closure**: `InputJson`, full supported-subset JsonElement validation, exact Schema version resolution, structured execution issues, runtime OutputSchema validation, and mainline tests.
5. **Invocation closure**: invoker, context propagation, dispatcher integration, result mapping, runtime/E2E tests, NativeAOT fixture, docs, memory update, and issue acceptance checklist.

These are implementation-plan slices, not a commitment to five separate pull requests.

## 16. Exit criteria

Phase 8e is complete only when:

1. an explicitly selected Capability generates an independent McpToolDescriptor;
2. discovery is valid, stable, deterministic, and aligned with MCP 2025-06-18;
3. input/output schemas come only from referenced Capability schemas;
4. generated binding materializes exact TInput through application-owned JsonTypeInfo;
5. schema validation uses canonical InputJson without reflection serialization;
6. generated output serialization validates exact TOutput and produces StructuredContent;
7. invocation uses `InvocationSource.Mcp` and the captured CapabilityDescriptor dispatcher overload;
8. Capability authorization, validation, rate limiting, idempotency, audit, and events remain effective;
9. exposure policy limits discovery and invocation without replacing authorization;
10. annotations do not affect permissions or risk;
11. MCP has no DynamicApi, AppService, Agent Control Plane execution, ASP.NET, or official SDK dependency;
12. no dynamic dictionary, runtime DTO scan, CLR schema reflection, or reflection JSON fallback exists on the MCP path;
13. missing Capability, Schema, Binding, or JsonTypeInfo fails startup;
14. input, validation, and output paths pass the NativeAOT publish-and-run fixture;
15. production Server hosting and experimental Tasks remain future adapter work;
16. issue #61 remains explicitly tracked and is not incorrectly claimed as resolved by 8e;
17. absent arguments, Host identity, InvocationId idempotency, and policy failures have deterministic fail-closed semantics;
18. every successful StructuredContent value passes the captured OutputSchema at runtime.

## 17. Protocol references

- [MCP 2025-06-18 Schema Reference](https://modelcontextprotocol.io/specification/2025-06-18/schema)
- [MCP 2025-06-18 Tools](https://modelcontextprotocol.io/specification/2025-06-18/server/tools)
