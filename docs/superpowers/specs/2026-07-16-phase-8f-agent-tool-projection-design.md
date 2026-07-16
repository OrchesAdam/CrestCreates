# Phase 8f — Agent Tool Projection Design

**Date**: 2026-07-16
**Status**: Approved
**Issue**: #60
**Related debt**: #61 — Generated CRUD Trimming-Safe JSON Contracts
**Depends on**: Phase 8a Capability Endpoint Projection, Phase 8c Legacy Dynamic API Boundary, Phase 8d AppService Compatibility Projection, Phase 8e MCP Tool Projection

## 1. Goal and scope

Phase 8f projects explicitly selected `CapabilityDescriptor` definitions into
discoverable, governed, exactly bound Agent Tools. It is an Agent-facing
governance projection over the Capability mainline, not a second business
runtime and not an adapter over MCP, Dynamic API, AppService compatibility, or
Agent Control Plane execution.

```text
[AgentToolSpec]
      ↓ Source Generator
AgentCapabilityToolDescriptor + exact AgentToolBindingContract
      ↓ Startup composition and validation
Immutable AgentToolRuntimeSnapshot
      ↓ Discovery / invocation
Trusted Agent execution context
      ↓
Selection policy / role exposure
      ↓
Argument validation / exact binding / canonical fingerprint
      ↓
Logical invocation lease + fencing
      ↓
Approval evidence / budget reservation / governance pre-audit
      ↓
Atomic DispatchStarted transition
      ↓
ICapabilityDispatcher.DispatchAsync(
    capturedCapabilityDescriptor,
    InvocationSource.Agent,
    exactTypedInput)
      ↓
CapabilityPipeline → Handler
      ↓
Exact output serialization / OutputSchema validation
      ↓
Budget settlement / governance finalization / invocation terminal state
```

The Agent Tool layer owns explicit exposure, model-facing discovery metadata,
selection policy, Agent-role constraints, side-effect classification, risk
escalation, logical invocation integrity, approval evidence, budget reservation,
and governance audit. Capability remains authoritative for business schemas,
permissions, data permissions, base risk, validation, rate limiting, business
idempotency, business audit, tenant/user propagation, events, handlers, and
actual execution.

Phase 8f ends at a provider-neutral Tool catalog and invocation facade. Future
OpenAI, Microsoft Agent Framework, or other provider adapters may translate
their provider contracts into this facade, but they never receive a Dispatcher,
Handler resolver, or mutable runtime registry.

### 1.1 Non-goals

Phase 8f does not implement:

- a planner, autonomous loop, model client, prompt runtime, session runtime, or
  general-purpose Agent Runtime;
- provider-specific Tool contracts or SDK adapters;
- a durable approval workflow, approval UI, or HumanTask orchestration;
- a production distributed invocation journal, budget ledger, approval store,
  or governance audit sink;
- cross-node exactly-once execution claims;
- automatic exposure of every Capability;
- hot reload or runtime registry mutation;
- dynamic CLR DTO generation, runtime DTO discovery, or reflection JSON
  serialization fallback;
- nested object, dictionary, union, or arbitrary provider JSON Schema expansion;
- MCP, HTTP, Dynamic API, or AppService execution bridges;
- Agent Draft, Authoring, or Control Plane support for creating or activating
  Agent Tool descriptors;
- Generated CRUD JSON contract debt tracked by issue #61.

## 2. Repository facts and final architecture decisions

The design is grounded in these current repository facts:

1. `InvocationSource.Agent` already exists in
   `CrestCreates.Capability.Abstractions`; Phase 8f does not add a second
   Capability invocation source.
2. `ICapabilityDispatcher` already exposes the captured
   `CapabilityDescriptor` overload. Agent invocation must use that overload.
3. Phase 8e already proves the generated-binding, application-owned
   `JsonTypeInfo`, immutable-snapshot, `InputJson`, output-validation, E2E, and
   NativeAOT patterns.
4. `CrestCreates.Agent.ControlPlane.Abstractions.AgentToolDescriptor` is the
   fixed Phase 7c Control Plane manifest contract. Phase 8f must not reuse,
   extend, rename, or replace it.
5. `CrestCreates.Agent.Abstractions` is a dependency anchor and
   `CrestCreates.Agent.Runtime` is a future composition root. The Phase 8f
   implementation is an independent `Agent.Tools` vertical slice; it does not
   turn either aggregate project into a full Agent Runtime.
6. `CapabilityDescriptor.RiskLevel` is the base-risk authority. Agent Tool may
   only raise effective risk through a risk floor.
7. `CapabilityProfile.RequireApproval` currently has a model and static
   resolver but is not part of the Capability execution mainline. Phase 8f does
   not treat it as an already enforced approval system. A future approval-gate
   adapter may consume it without creating another execution path.
8. The current Capability `IdempotencyMiddleware` performs non-atomic
   `GetResultAsync → Execute → success-only StoreResultAsync`. It does not
   serialize concurrent misses, cache failures, or detect one InvocationId with
   different arguments. Agent logical invocation integrity therefore requires
   its own pre-dispatch gate.
9. `DescriptorPackage` and `DescriptorSnapshot` persist descriptor references,
   kinds, lifecycle state, relationships, and stable hashes. They do not persist
   concrete `McpToolDescriptor` payloads.

Final decisions:

- the metadata descriptor is named `AgentCapabilityToolDescriptor`;
- descriptor lifecycle uses only `DescriptorState`; no
  `InvocationMode.Disabled` exists;
- selection permission and actual call origin are separate concepts;
- every governance enum uses a safe zero-value sentinel unless zero is itself a
  provably non-permissive semantic;
- Agent Tool and MCP remain independent runtimes;
- only protocol-neutral Schema/JSON implementation is extracted from MCP;
- logical invocation, execution attempt, and budget reservation are distinct
  identities and state machines;
- a call may become Invocation `Indeterminate` while Budget is already
  `Committed`;
- `Title` is model-facing Agent behavior and participates in Agent Tool
  ContractHash;
- no further Agent Runtime features are added to Phase 8f.

## 3. Projects and dependency boundaries

Add one metadata contract project and one independent Agent Tool vertical slice:

```text
src/Metadata/
└── CrestCreates.Metadata.AgentTool.Abstractions/
    ├── AgentCapabilityToolDescriptor.cs
    └── AgentToolMetadataContracts.cs

src/Runtime/Agent/
├── CrestCreates.Agent.Tools.Abstractions/
└── CrestCreates.Agent.Tools/

src/Tooling/CrestCreates.CodeGenerator/
└── AgentToolGenerator/

tests/Runtime/Agent/
├── CrestCreates.Agent.Tools.Tests/
├── CrestCreates.Agent.Tools.E2E.Tests/
├── CrestCreates.Agent.Tools.AotFixture/
└── CrestCreates.Agent.Tools.AotFixture.Tests/
```

`CrestCreates.Metadata.AgentTool.Abstractions` contains only metadata required
by registry, canonical hashing, topology, impact, snapshot, and package
governance. Its only project dependency is `Metadata.Abstractions`. It does not
reference Metadata runtime, Runtime/Agent, Capability runtime, MCP, DynamicApi,
ASP.NET Core, or a provider SDK.

`CrestCreates.Agent.Tools.Abstractions` contains:

- authoring attributes;
- provider-neutral catalog, invocation, outcome, and governance contracts;
- the generated binding contract;
- the generated binding and JSON-contract registration surfaces;
- logical invocation, approval, budget, and governance-audit interfaces.

Its allowed dependencies are:

```text
Agent.Abstractions
Metadata.AgentTool.Abstractions
Metadata.Abstractions
Schema.Abstractions
Capability.Abstractions
```

`CrestCreates.Agent.Tools` contains:

- Agent Tool registry and descriptor validation;
- Capability and Schema resolution;
- JSON configuration validation and `JsonTypeInfo` capture;
- immutable runtime snapshot composition;
- role-aware discovery;
- invocation fingerprinting;
- invocation-gate orchestration and the explicit in-memory opt-in adapter;
- approval, budget, and governance-audit orchestration;
- Dispatcher integration, safe result mapping, and output validation;
- relationship extraction, DI, and eager startup validation.

Its allowed dependencies include Agent.Tools.Abstractions, Metadata runtime,
Schema runtime, Capability.Abstractions, Authorization.Abstractions, and
MultiTenancy.Abstract.

The following references are forbidden from Agent.Tools.Abstractions,
Agent.Tools runtime, and generated Agent Tool output unless a narrower row says
otherwise:

```text
CrestCreates.Mcp*
Framework/Api/DynamicApi
ASP.NET Core
AppService compatibility execution
Agent.ControlPlane execution
Agent Draft / Activation services
OpenAI / Microsoft Agent Framework / provider SDKs
direct Handler resolver or Handler invocation
runtime assembly scanning
reflection JSON fallback
Dictionary<string, object?> argument fallback
```

`CrestCreates.Agent.Runtime` may compose `AddCrestAgentTools()` in a future
phase. Phase 8f does not add that composition.

## 4. Protocol-neutral projection kernel and MCP migration boundary

The second Tool projection makes these Phase 8e implementation concerns proven
shared platform capability rather than MCP-owned behavior:

- closed Schema → JSON Schema projection for the supported subset;
- Schema ↔ `JsonTypeInfo` directional parity;
- strict primitive and primitive-collection token handling;
- duplicate/unknown property validation helpers;
- CodeGenerator root-DTO validation and exact binder/serializer emission.

Phase 8f Slice 0 performs a narrow extraction:

- move the protocol-neutral core of `McpJsonSchemaProjector` to Schema runtime,
  for example `SchemaJsonContractProjector`;
- move the protocol-neutral core of `McpToolSchemaParityValidator` to Schema
  runtime, for example `SchemaJsonTypeInfoParityValidator`;
- keep MCP facades, MCP error mapping, MCP JSON options, MCP bindings, MCP
  snapshot, and MCP result contracts in the MCP projects;
- extract only CodeGenerator-internal DTO validation and binding-emission
  helpers; MCP and Agent generated runtime types remain distinct;
- preserve all MCP discovery bytes, canonical hashes, E2E behavior, and
  NativeAOT behavior.

The shared metadata-owned reference is:

```csharp
public readonly record struct CapabilityProjectionReference(
    string Id,
    int Version,
    VersionSelectionMode SelectionMode = VersionSelectionMode.Exact,
    string? ExpectedContractHash = null);
```

It lives under `Metadata.Abstractions.DescriptorCapability`. Its purpose is to
let metadata-owned projections reference Runtime-owned Capability identity
without reversing the dependency direction. It does not replace every existing
`VersionedDescriptorRef<CapabilityDescriptor>` in the repository.

### 4.1 `McpCapabilityReference` compatibility gate

Changing `McpToolDescriptor.Capability` to the shared type changes its public
CLR signature even when field names are identical. The migration is accepted
only with these rules:

1. MCP generated code emits `CapabilityProjectionReference` as the only
   mainline type.
2. A time-bounded `[Obsolete] McpCapabilityReference` value wrapper provides
   implicit conversion to and from the shared type for common source migration.
3. The wrapper is not used by runtime resolution, registry, canonical profiles,
   or new generated output. It is not a second execution path.
4. The wrapper does not preserve already compiled binary property signatures;
   release notes must state this explicitly.
5. A follow-up removal issue/version is created; the wrapper is not permanent.

Required compatibility gates:

- pre-migration MCP canonical payload golden bytes are byte-identical after the
  migration;
- pre-migration MCP ContractHash and DefinitionHash vectors are identical;
- descriptors constructed through the obsolete wrapper and shared type hash
  identically;
- MCP generated source compiles and produces identical descriptor semantics;
- existing DescriptorPackage JSON bytes, round trips, refs, kinds, and hashes
  are unchanged;
- no test claims that DescriptorPackage round-trips a concrete MCP descriptor,
  because that is not the current package contract;
- MCP runtime, E2E, and linux-x64 NativeAOT publish-and-run remain green with no
  behavior change.

If any gate cannot be preserved, Slice 0 stops the MCP reference migration.
Agent Tool temporarily owns an equivalent metadata-only reference and the shared
reference migration becomes a separate versioned change. Phase 8f must not
destabilize the closed Phase 8e mainline.

MCP continues to reject non-null ExpectedContractHash in Phase 8e. A shared CLR
value shape does not silently expand MCP runtime semantics.

## 5. Descriptor kind and metadata contract

Add:

```text
DescriptorKind.AgentTool = 9
DescriptorKindNames.AgentTool = "AgentTool"
Descriptor Namespace = "agent-tool"
```

The descriptor is:

```csharp
public sealed class AgentCapabilityToolDescriptor
    : IDescriptor, IVersionedDescriptor
{
    public string Namespace => "agent-tool";
    public DescriptorKind Kind => DescriptorKind.AgentTool;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }

    public required CapabilityProjectionReference Capability { get; init; }

    public string ToolName { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string Description { get; init; } = string.Empty;

    public AgentToolSelectionPolicy SelectionPolicy { get; init; }
        = AgentToolSelectionPolicy.ExplicitOnly;

    public AgentToolSideEffectKind SideEffectKind { get; init; }
        = AgentToolSideEffectKind.Unknown;

    public CapabilityRiskLevel? RiskFloor { get; init; }

    public AgentToolApprovalMode ApprovalMode { get; init; }
        = AgentToolApprovalMode.PolicyDriven;

    public required AgentToolBudgetRequirement Budget { get; init; }

    public AgentToolAuditMode AuditMode { get; init; }
        = AgentToolAuditMode.Required;

    public IReadOnlyList<string> AllowedAgentRoles { get; init; }
        = Array.Empty<string>();
}
```

Safe enums are:

```csharp
public enum AgentToolSelectionPolicy
{
    Unknown = 0,
    ExplicitOnly = 1,
    AutomaticAllowed = 2
}

public enum AgentToolSideEffectKind
{
    Unknown = 0,
    ReadOnly = 1,
    InternalWrite = 2,
    ExternalWrite = 3,
    Destructive = 4
}

public enum AgentToolApprovalMode
{
    Unknown = 0,
    PolicyDriven = 1,
    Required = 2,
    None = 3
}

public enum AgentToolAuditMode
{
    Unknown = 0,
    Required = 1,
    BestEffort = 2
}
```

`Unknown` and out-of-range values always fail closed. The unusual numeric value
for `None` is intentional: a zero-initialized approval enum must not waive an
approval requirement.

The budget contract is:

```csharp
public sealed record AgentToolBudgetRequirement
{
    public required string Category { get; init; }
    public long CostUnits { get; init; } = 1;
    public int? MaxCallsPerExecution { get; init; }
}
```

Descriptor validation rules:

- Id, Name, ToolName, Description, and Capability Id are non-empty;
- Id contains no whitespace and Version is greater than zero;
- ToolName satisfies `^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$`;
- Capability selection supports Exact with Version `> 0` and Latest with
  Version `0`; Compatible and unknown modes fail;
- Agent supports non-null ExpectedContractHash and compares it with the resolved
  Capability ContractHash during snapshot build;
- AllowedAgentRoles is non-empty, contains non-empty Ordinal-unique values, and
  forbids `*`;
- Budget is non-null, Category is non-empty, CostUnits is greater than zero, and
  MaxCallsPerExecution is null or greater than zero;
- every enum is a known supported value;
- Query resolves to ReadOnly; Query plus any write classification fails;
- Query plus Unknown is safely derived to ReadOnly in the runtime snapshot;
- Command cannot be ReadOnly and Command plus Unknown fails; command authors
  must classify the side effect;
- EffectiveRisk is `max(Capability.RiskLevel, RiskFloor)`;
- a RiskFloor numerically below Capability risk is rejected as misleading rather
  than silently appearing to lower risk;
- High/Critical effective risk and ExternalWrite/Destructive side effects force
  effective approval Required regardless of descriptor ApprovalMode;
- High/Critical and ExternalWrite/Destructive Tools require AuditMode.Required;
- BestEffort audit is allowed only for Low/Medium ReadOnly Tools;
- AutomaticAllowed permits planner selection but never bypasses approval,
  budget, audit, invocation-gate, or Capability governance.

DescriptorState is the only publish/disable lifecycle. There is no separate
Disabled selection or invocation mode.

## 6. Authoring API and developer path

Phase 8f provides one explicit authoring level:

```csharp
[AgentToolSpecs]
public static partial class OrderAgentTools
{
    [AgentToolSpec(
        "orders.create",
        InputType = typeof(CreateOrderInput),
        OutputType = typeof(OrderDto),
        ToolName = "orders.create",
        Title = "Create order",
        Description = "Creates one validated order.",
        SelectionPolicy = AgentToolSelectionPolicy.ExplicitOnly,
        SideEffectKind = AgentToolSideEffectKind.ExternalWrite,
        RiskFloor = AgentToolRiskFloor.High,
        ApprovalMode = AgentToolApprovalMode.Required,
        BudgetCategory = "order-write",
        CostUnits = 5,
        MaxCallsPerExecution = 1,
        AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "sales-agent" })]
    public sealed class Create;
}
```

The authoring contract contains:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgentToolSpecsAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgentToolSpecAttribute : Attribute
{
    public AgentToolSpecAttribute(string capabilityId)
        => CapabilityId = capabilityId;

    public string CapabilityId { get; }
    public string? DescriptorId { get; set; }
    public int DescriptorVersion { get; set; } = 1;
    public int CapabilityVersion { get; set; }
    public string? ExpectedCapabilityContractHash { get; set; }

    public Type? InputType { get; set; }
    public Type? OutputType { get; set; }

    public string? ToolName { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }

    public AgentToolSelectionPolicy SelectionPolicy { get; set; }
        = AgentToolSelectionPolicy.ExplicitOnly;
    public AgentToolSideEffectKind SideEffectKind { get; set; }
        = AgentToolSideEffectKind.Unknown;
    public AgentToolRiskFloor RiskFloor { get; set; }
        = AgentToolRiskFloor.Inherit;
    public AgentToolApprovalMode ApprovalMode { get; set; }
        = AgentToolApprovalMode.PolicyDriven;
    public string? BudgetCategory { get; set; }
    public long CostUnits { get; set; } = 1;
    public int MaxCallsPerExecution { get; set; }
    public AgentToolAuditMode AuditMode { get; set; }
        = AgentToolAuditMode.Required;
    public string[] AllowedAgentRoles { get; set; } = Array.Empty<string>();
}
```

`AgentToolRiskFloor` is an Attribute-friendly sentinel enum:

```text
Inherit = 0, Low = 1, Medium = 2, High = 3, Critical = 4
```

The generator maps these values explicitly to `CapabilityRiskLevel`; it must
not cast between enums because their numeric layouts differ. Inherit maps to
null. `MaxCallsPerExecution == 0` maps to null; negative values are errors.
CapabilityVersion `0` means Latest and a positive value means Exact. Negative
values are errors.

Container rules match the proven Phase 8e shape:

- the `[AgentToolSpecs]` container is top-level, non-generic, static, and
  partial;
- each `[AgentToolSpec]` target is a direct nested non-generic class;
- one Error diagnostic suppresses every Provider and Binding output for that
  container;
- input/output roots must be explicit supported DTOs;
- interfaces, abstract types, open generics, dynamic dictionaries, primitive
  roots, and multiple-argument assembly are rejected;
- Description, BudgetCategory, and AllowedAgentRoles are required;
- ToolName and descriptor identity are unique inside the generation container.

## 7. Generated artifacts and exact binding

Each valid container generates:

1. an `IDescriptorProvider<AgentCapabilityToolDescriptor>` registered through
   `DescriptorProviderRegistry`;
2. exact input and output binding methods registered through
   `AgentToolBindingRegistry`;
3. input/output CLR type registrations for startup `JsonTypeInfo` validation.

The generated binding contract is:

```csharp
public sealed class AgentToolBindingContract
{
    public required string ToolDescriptorId { get; init; }
    public int ToolDescriptorVersion { get; init; }
    public Type? InputType { get; init; }
    public Type? OutputType { get; init; }

    public required Func<
        JsonElement,
        JsonTypeInfo?,
        CancellationToken,
        ValueTask<object?>> BindInputAsync { get; init; }

    public required Func<
        object?,
        JsonTypeInfo?,
        CancellationToken,
        ValueTask<JsonElement?>> SerializeOutputAsync { get; init; }
}
```

Registry identity is Descriptor Id + Descriptor Version, not ToolName.
ToolName may change without rebinding CLR delegates, while a descriptor-version
change receives its own binding identity.

Generated binders require `JsonTypeInfo<TInput>` and deserialize to exact
`TInput`. Generated serializers require output runtime
`GetType() == typeof(TOutput)`, require `JsonTypeInfo<TOutput>`, and serialize to
`JsonElement`. Void output accepts only null.

Generated code must not contain:

```text
Handler resolution or invocation
ICapabilityDispatcher invocation
Dictionary<string, object?>
DefaultJsonTypeInfoResolver
reflection JsonSerializer overloads
MCP symbols
DynamicApi / ASP.NET symbols
Agent Control Plane symbols
provider SDK symbols
```

## 8. JSON contracts, Schema authority, and NativeAOT binding

Agent Tools own JSON configuration independently from ASP.NET and MCP:

```csharp
public sealed class AgentToolJsonOptions
{
    public JsonSerializerOptions SerializerOptions { get; } = new();
}
```

Applications register their source-generated context explicitly:

```csharp
services.AddCrestAgentTools(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(
        0,
        ApplicationJsonContext.Default);
});
```

Startup copies the configured options and requires either one
`JsonSerializerContext` or a resolver chain containing only generated contexts.
`DefaultJsonTypeInfoResolver`, reflection-capable resolvers, and fallback
options fail startup. Startup resolves every registered input/output Type,
captures its `JsonTypeInfo`, and calls `MakeReadOnly()` before publishing the
runtime snapshot.

`RespectNullableAnnotations` and `RespectRequiredConstructorParameters` remain
disabled. Schema validation owns input presence and nullability; STJ must not
reject an input before the captured Capability reaches Dispatcher and the
Capability Pipeline.

Schema is the sole discovery and business-shape authority:

| Capability Schema | generated CLR Type | Result |
| --- | --- | --- |
| absent | absent | valid no-input/no-output contract |
| present | present | validate parity and capture |
| present | absent | startup failure |
| absent | present | startup failure |

Phase 8f uses exactly the Phase 8e verified object-root subset:

- string, bool, int, long, decimal, double;
- Guid/UUID, DateOnly/date, DateTime/DateTimeOffset/date-time;
- collections of those primitive shapes;
- required, nullable, string-length, and numeric-range constraints.

Nested objects, dictionaries, unions, enum sets, arbitrary references,
unsupported validation rules, and portable-pattern debt fail startup. Agent Tool
does not widen the supported subset independently from MCP. Future shared
expansion belongs in the protocol-neutral Schema projection kernel and requires
both consumers' parity, executable-validation, E2E, and NativeAOT gates.

Arguments normalize to a JSON object. Absent arguments become `{}`. Duplicate
properties, unknown properties, non-object roots, non-empty input for a no-input
Tool, and captured InputSchema violations fail before approval, budget, or
Dispatcher execution.

The invoker retains a clone of the normalized arguments and configures:

```csharp
executionContext.InputJson = normalizedArguments.Clone();
```

Capability Validation Middleware therefore validates canonical JSON and never
reflection-serializes the exact typed input back into JSON.

Successful output rules are fail-closed:

- absent OutputSchema/OutputType plus non-null output is unexpected output;
- present OutputSchema/OutputType plus null output is missing output;
- derived or otherwise non-exact output runtime type is a type mismatch;
- serialized output must pass the captured OutputSchema;
- invalid structured output is never returned.

A deterministic exact-type or OutputSchema violation is a completed internal
contract failure after the business call; it is not automatically
`Indeterminate`. Cancellation, process failure, or an inability to determine
serialization/finalization state is `Indeterminate`.

## 9. Trusted Agent execution context

Phase 8f adds the minimum neutral execution identity to
`CrestCreates.Agent.Abstractions`:

```csharp
public enum AgentToolCallOrigin
{
    Unknown = 0,
    ExplicitRequest = 1,
    AutomaticSelection = 2
}

public sealed record AgentExecutionContext
{
    public required string ExecutionId { get; init; }
    public required string InvocationId { get; init; }
    public required string AgentId { get; init; }
    public required IReadOnlySet<string> AgentRoles { get; init; }
    public required AgentToolCallOrigin CallOrigin { get; init; }
    public string? CausationId { get; init; }
}

public interface IAgentExecutionContextAccessor
{
    AgentExecutionContext? Current { get; }
}
```

The Host or future Agent orchestrator creates this scope from trusted runtime
state. Tool arguments, model output, and provider adapters do not control these
values.

TenantId and UserId come from existing `ITenantContext` and `ICurrentUser` in
the same DI scope used by `CapabilityDispatcher`. The Agent Tool invocation
request carries only ToolName, arguments, and approval evidence. It does not
accept TenantId, UserId, permissions, roles, risk, ExecutionId, InvocationId,
AgentId, CallOrigin, or Capability InvocationSource.

The invocation fails before lookup or dispatch when:

- the trusted Agent execution context is absent;
- ExecutionId, InvocationId, or AgentId is blank;
- AgentRoles is null, empty, contains blank values, or contains duplicates;
- CallOrigin is Unknown or an unsupported value;
- current tenant/user identity required by the Host is absent or inconsistent.

The runtime never copies TenantId/UserId from Tool arguments into the Capability
context. `CapabilityDispatcher` continues to establish its authoritative
ambient tenant/user values.

## 10. Discovery and selection semantics

Selection policy and actual call origin are separate:

```csharp
public enum AgentToolSelectionPolicy
{
    Unknown = 0,
    ExplicitOnly = 1,
    AutomaticAllowed = 2
}
```

The fixed matrix is:

| SelectionPolicy | CallOrigin | Runtime result |
| --- | --- | --- |
| AutomaticAllowed | AutomaticSelection | continue governance |
| AutomaticAllowed | ExplicitRequest | continue governance |
| ExplicitOnly | ExplicitRequest | continue governance |
| ExplicitOnly | AutomaticSelection | deny before dispatch |
| Unknown/unsupported | any | fail closed |
| any | Unknown/unsupported | fail closed |

`IAgentToolCatalog.ListAsync()` reads the trusted current context and returns
only Active Tools visible to the current Agent roles and intended CallOrigin.
Automatic discovery excludes ExplicitOnly Tools. Explicit discovery may include
both policies. Results are sorted by ToolName using `StringComparer.Ordinal`.

The provider-neutral discovery contract contains:

- ToolName, Title, and Description;
- input and optional output JSON Schema;
- SelectionPolicy and resolved SideEffectKind;
- EffectiveRisk;
- effective approval, budget, and audit summary;
- descriptor and resolved Capability contract identity needed by a trusted
  adapter, without exposing Handler or mutable registry objects.

Invocation repeats every role and selection check. A discovery result is never
an authorization token. A role- or selection-denied Tool is classified like an
unknown Tool to avoid a Schema or existence oracle.

AllowedAgentRoles constrains Agent identity, not the authenticated user's
Capability permissions. Both layers apply:

```text
Agent roles / selection policy → Agent Tool pre-dispatch exposure
Current user permissions / data permissions → Capability Pipeline
```

## 11. Registry and immutable runtime snapshot

The startup chain is:

```text
SchemaRegistry.Build
  → CapabilityRegistry.Build
  → AgentToolRegistry.Build
  → validate all descriptor identity/lifecycle/reference shapes
  → select Active candidates
  → resolve and capture exact/latest Capability
  → verify ExpectedContractHash when present
  → resolve and capture exact input/output Schema
  → lookup generated binding
  → resolve/freeze application JsonTypeInfo
  → validate Schema/CLR directional parity
  → derive side effect / EffectiveRisk / approval and audit floor
  → build discovery contract and stable hashes
  → publish FrozenDictionary<string, AgentToolRuntimeEntry>
```

The runtime entry is conceptually:

```csharp
public sealed record AgentToolRuntimeEntry(
    AgentCapabilityToolDescriptor Descriptor,
    CapabilityDescriptor Capability,
    SchemaDescriptor? InputSchema,
    SchemaDescriptor? OutputSchema,
    AgentToolRuntimeBinding Binding,
    AgentToolDiscoveryContract DiscoveryContract,
    CapabilityRiskLevel EffectiveRisk,
    AgentToolSideEffectKind EffectiveSideEffectKind,
    AgentToolEffectiveGovernance Governance,
    string ToolContractHash,
    string CapabilityContractHash,
    string? InputSchemaContractHash,
    string? OutputSchemaContractHash);
```

`AgentToolRegistry` retains all versions and lifecycle states for Metadata
governance. `AgentToolRuntimeSnapshot` contains only Active, fully validated,
runtime-ready entries and indexes ToolName through a
`FrozenDictionary<string, AgentToolRuntimeEntry>` with
`StringComparer.Ordinal`.

Lifecycle-aware validation follows Phase 8e:

- every state receives identity, version, reference syntax, enum, role, budget,
  and canonical-hash validation;
- only Active candidates require Capability/Schema resolution, generated
  binding, JsonTypeInfo, global ToolName uniqueness, Schema parity, and
  executable governance services;
- historical Removed/Deprecated descriptors do not block startup because an
  obsolete binding or application JsonTypeInfo was removed.

Latest Capability resolution occurs once at startup and is captured. Invocation
does not re-resolve Latest. Exact Schema versions are required. Schema Latest or
Compatible references in a resolved Capability remain unsupported for the Tool
projection and fail startup.

`AddCrestAgentTools()` registers an `IHostedService` that eagerly builds the
dependency registries and publishes the snapshot before the Host reports
started. Discovery and invocation never lazily create an empty or partial
snapshot.

Active Tools require configured invocation gate, approval gate, budget gate,
and governance audit sink. Missing required governance infrastructure fails
startup rather than allowing an unrestricted runtime.

## 12. Canonical invocation fingerprint

The framework owns an AOT-safe, versioned canonical fingerprint builder. No
application or Handler assembles separator-delimited strings.

The `agent-tool-invocation-v1` payload binds, in fixed order:

```text
shapeVersion
Tool descriptor Id / Version / Tool ContractHash
resolved Capability Id / Version / Capability ContractHash
input Schema ContractHash or explicit null
output Schema ContractHash or explicit null
canonical ArgumentsHash
TenantId / UserId
AgentId / ordinal AgentRoles hash
ExecutionId / InvocationId
CallOrigin
```

SelectionPolicy is already bound through Tool ContractHash and is not duplicated
as a second field. CallOrigin is a per-call trusted fact and is written directly.
Changing one InvocationId from AutomaticSelection to ExplicitRequest therefore
changes fingerprint and produces an invocation conflict rather than replay.

For accepted input, ArgumentsHash is computed only after duplicate/unknown-
property and captured Schema validation. Its canonical writer is Schema-aware
and reflection-free:

- object properties are sorted by JSON property name with Ordinal comparison;
- arrays preserve element order;
- absence and explicit null remain distinct;
- string/bool/null are written as native JSON values;
- integers are written as their validated integral value;
- decimal/double numbers are written through the deterministic writer for the
  captured Schema numeric category;
- supported date/date-time/UUID values retain their already validated canonical
  lexical form;
- unsupported shapes never reach the writer.

For a rejected payload, the decision audit uses a schema-neutral canonical raw
JSON hash that sorts object properties but never reads values according to the
business Schema. This keeps invalid values distinguishable without allowing a
fingerprint exception to escape. Role/Selection denials occur before argument
evaluation and record `ArgumentsEvaluated=false` with a null ArgumentsHash.

The payload is written with `Utf8JsonWriter`, hashed with SHA-256, and encoded as
a stable lowercase hexadecimal or Base64Url value chosen once by the concrete
contract. The encoding choice is part of the v1 shape and cannot vary per Host.

The same fingerprint binds logical invocation identity, approval evidence,
budget reservation requests, and governance audit correlation.

## 13. Logical invocation gate, attempts, leases, and fencing

Capability idempotency remains active but is insufficient for Agent logical-call
integrity. Phase 8f adds `IAgentToolInvocationGate` before approval, budget, and
Dispatcher execution.

Three identities are distinct:

```text
TenantId + UserId + AgentId + ExecutionId + InvocationId = LogicalInvocationKey
AttemptId + LeaseId + FencingToken                        = one execution attempt
ReservationId                                            = belongs to AttemptId
```

The logical invocation permanently binds its first accepted fingerprint. A
different fingerprint for the same key is Conflict even if every prior attempt
was blocked before Dispatcher.

The public contracts are conceptually:

```csharp
public readonly record struct AgentToolLogicalInvocationKey(
    string? TenantId,
    string UserId,
    string AgentId,
    string ExecutionId,
    string InvocationId);

public sealed record AgentToolInvocationAcquireRequest(
    AgentToolLogicalInvocationKey Key,
    string InvocationFingerprint);

public enum AgentToolInvocationAcquireStatus
{
    Unknown = 0,
    Acquired = 1,
    InProgress = 2,
    Completed = 3,
    Indeterminate = 4,
    Conflict = 5
}

public sealed record AgentToolInvocationLease
{
    public required string AttemptId { get; init; }
    public required string LeaseId { get; init; }
    public required long FencingToken { get; init; }
    public required DateTimeOffset AcquiredAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public interface IAgentToolInvocationGate
{
    ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(
        AgentToolInvocationAcquireRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolInvocationLease> RenewAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryMarkDispatchStartedAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);

    ValueTask PrepareCompletionAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationOutcome outcome,
        CancellationToken cancellationToken = default);

    ValueTask PublishCompletionAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);

    [Obsolete("Use PrepareCompletionAsync followed by PublishCompletionAsync.")]
    ValueTask CompleteAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationOutcome outcome,
        CancellationToken cancellationToken = default);

    ValueTask MarkIndeterminateAsync(
        AgentToolInvocationLease lease,
        string reasonCode,
        CancellationToken cancellationToken = default);

    ValueTask ReleaseLeaseAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);
}
```

Acquire behavior:

| Existing logical state | Fingerprint | Result |
| --- | --- | --- |
| absent | new | Acquired with first attempt |
| available, no active attempt | same | Acquired with new attempt |
| active attempt | same | InProgress |
| CompletionPending | same | InProgress |
| Completed | same | Completed with stored safe outcome |
| Indeterminate | same | Indeterminate |
| any bound state | different | Conflict |

`PrepareCompletionAsync` persists a fenced `CompletionPending` state containing
the terminal outcome. Acquire returns `InProgress` while that state is pending;
it never exposes a replay before publication. `PublishCompletionAsync` is called
only after Required governance finalization succeeds and makes the outcome
visible as `Completed`. If preparation, audit finalization, or publication is
uncertain, the gate transitions the attempt to `Indeterminate` and no Completed
replay is exposed. `Completed` returns the stored provider-neutral Agent outcome. It does not
re-run approval, reserve budget, dispatch, serialize output, or finalize audit.
Both success and deterministic business/contract failure are completed terminal
outcomes. `Indeterminate` never auto-dispatches and requires explicit
reconciliation.

### 13.1 Lease and fencing rules

- FencingToken is monotonic for one LogicalInvocationKey and increases for each
  new attempt ownership;
- Renew, TryMarkDispatchStarted, Complete, MarkIndeterminate, and ReleaseLease
  validate both LeaseId and FencingToken;
- every transition is idempotent for the same valid lease and rejects a stale
  or mismatched lease;
- an expired or fenced Worker cannot complete, release, or overwrite newer
  state;
- the invoker calls `TryMarkDispatchStartedAsync` as the final operation before
  Dispatcher and may dispatch only when it returns true;
- this transition atomically records that the attempt may have entered business
  execution; at most one attempt for a logical invocation can record it;
- expiry before DispatchStarted permits a new attempt because a stale Worker's
  later TryMarkDispatchStarted will fail fencing;
- expiry after DispatchStarted, or uncertainty about whether DispatchStarted was
  durably recorded, transitions the logical invocation to Indeterminate rather
  than reacquiring and dispatching;
- the active owner renews a long-running lease; renewal returns the updated
  expiry while retaining attempt identity and fencing ownership;
- after acquisition, the invoker owns one renewal loop until Release,
  Complete, or MarkIndeterminate finishes; application and Handler code never
  renew the lease directly;
- renewal failure before DispatchStarted blocks dispatch; renewal failure after
  DispatchStarted is Indeterminate unless the durable adapter can prove a
  terminal outcome; cancellation of the renewal loop is cleanup and is not
  proof that Handler execution stopped.

The state is stored separately:

```text
Logical Invocation: Registered / Running / Completed / Indeterminate
Attempt:            Acquired / DispatchStarted / Released / Completed / Indeterminate
Budget Reservation: Reserved / Released / Committed / Indeterminate
```

A logical invocation may have multiple Released pre-dispatch attempts, but at
most one attempt reaches DispatchStarted.

### 13.2 In-memory and distributed claims

Phase 8f includes an AOT-safe concurrent in-memory gate only as:

- test/dev infrastructure; or
- an explicit single-node opt-in that accepts process-restart replay risk.

It is not the default production-safe implementation and must not claim durable
or cross-node exactly-once behavior. Active Tool startup fails if no invocation
gate is registered.

A distributed adapter is responsible for durable compare-and-swap,
monotonic-fencing allocation, lease recovery, stale-worker rejection, bounded
InProgress waiting, and reconciliation. Its implementation is outside Phase 8f.

## 14. Approval policy, evidence binding, and replay

`IAgentToolApprovalGate` evaluates the resolved runtime entry, EffectiveRisk,
side effect, descriptor ApprovalMode, trusted identity, canonical fingerprint,
and optional evidence. The runtime invokes it only after acquiring the logical
invocation lease and before budget reservation.

The provider-neutral decision is explicit and safe by default:

```csharp
public enum AgentToolApprovalDecision
{
    Unknown = 0,
    Denied = 1,
    NotRequired = 2,
    Approved = 3
}

public interface IAgentToolApprovalGate
{
    ValueTask<AgentToolApprovalResult> EvaluateAndClaimAsync(
        AgentToolApprovalRequest request,
        CancellationToken cancellationToken = default);
}
```

The request includes the logical key, AttemptId, fingerprint, trusted identity,
resolved governance facts, and opaque Host evidence. The result returns a
known decision and only safe evidence/approver references. Unknown decisions,
exceptions, malformed results, and a Required decision without a verified
claim all deny dispatch.

Effective approval cannot be lowered:

```text
Descriptor Required                         → Required
EffectiveRisk High or Critical              → Required
SideEffect ExternalWrite or Destructive     → Required
Descriptor PolicyDriven                     → approval gate policy
Descriptor None without a forced floor      → no descriptor-level approval
```

The default fail-closed behavior may allow only calls whose effective decision
is provably no-approval. A required or PolicyDriven call without a reliable Host
verifier is denied. There is no `Approved=true` Boolean shortcut.

Evidence binds the canonical invocation fingerprint and includes at least:

```text
EvidenceId
InvocationFingerprint
Approver identity
IssuedAt
ExpiresAt
authenticity/signature material owned by the Host verifier
```

Replay rules are fixed:

> Evidence may be reused only for the same logical invocation and identical
> fingerprint. A different InvocationId or fingerprint requires a different
> approval decision.

- fingerprint already binds Tool, Capability, Schemas, arguments, tenant, user,
  Agent, roles, ExecutionId, InvocationId, and CallOrigin;
- EvidenceId claim is atomic and stores at least
  `EvidenceId → InvocationFingerprint`;
- the same EvidenceId + same fingerprint claim is idempotent;
- the same EvidenceId + different fingerprint is denied;
- a Completed replay does not claim evidence again because it does not execute;
- a new attempt after a Released pre-dispatch attempt may reuse the same
  EvidenceId claim when fingerprint is identical;
- before dispatch, the new attempt re-evaluates expiry and revocation; expired
  evidence cannot authorize a call that has not entered Dispatcher;
- approver separation, authenticity, expiry, revocation, cross-node atomic
  claim, and durable replay protection belong to the Host verifier/durable
  adapter.

Phase 8f defines the contracts and fail-closed validation. It does not implement
approval persistence, UI, approval workflows, or HumanTask integration.

Governance audit records EvidenceId, claim result, approver-safe reference, and
fingerprint. It does not store the full sensitive evidence payload by default.

## 15. Budget reservation and settlement

Every Active Tool has an explicit Budget requirement. The budget gate receives:

- logical invocation key and AttemptId;
- Tool/Capability contract identity and invocation fingerprint;
- Budget Category, CostUnits, and MaxCallsPerExecution;
- tenant, user, Agent, EffectiveRisk, and side effect.

The budget state machine is:

```text
Reserved
  ├─ Released
  ├─ Committed
  └─ Indeterminate
```

Reserved enters exactly one terminal state. Finalization is idempotent by
ReservationId and terminal states cannot transform into one another.

The contract makes reservation and settlement separate operations:

```csharp
public enum AgentToolBudgetReservationState
{
    Unknown = 0,
    Reserved = 1,
    Released = 2,
    Committed = 3,
    Indeterminate = 4
}

public interface IAgentToolBudgetGate
{
    ValueTask<AgentToolBudgetReserveResult> ReserveAsync(
        AgentToolBudgetReserveRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolBudgetReservation> FinalizeAsync(
        AgentToolBudgetFinalizeRequest request,
        CancellationToken cancellationToken = default);
}
```

Reserve returns either a known denial or a Reserved value containing
ReservationId, AttemptId, fingerprint, units, and provisional capacity facts.
Unknown/malformed results fail closed. Finalize includes ReservationId,
AttemptId, fingerprint, requested terminal state, and reason code; the adapter
must return the persisted terminal state and reject an identity mismatch. A
known denial is exactly `Status=Denied`, a null Reservation, and a non-empty
ReasonCode; a Denied result carrying a reservation or lacking its reason is
Indeterminate and must retain the invocation lease for reconciliation.

Reservation belongs to an attempt, not the logical invocation:

```text
Logical Invocation L
  ├─ Attempt A / Reservation R1 → Released
  ├─ Attempt B / Reservation R2 → Released
  └─ Attempt C / Reservation R3 → Committed or Indeterminate
```

Rules:

- approval or budget denial before a successful reservation creates no fake
  Released reservation;
- one attempt reuses its existing Reserved ReservationId;
- a pre-audit, lease, fencing, or other pre-dispatch failure after reservation
  releases that reservation;
- Released is terminal for that ReservationId, but a later attempt with the
  same fingerprint receives a new ReservationId;
- a logical invocation may have multiple Released attempts;
- Completed replay never reserves again;
- once one attempt is Committed, the logical invocation creates no new
  reservation;
- Indeterminate blocks further attempts until reconciliation;
- Reserved provisionally occupies MaxCallsPerExecution capacity;
- Committed and Indeterminate consume final capacity;
- Released returns capacity;
- the logical Capability IdempotencyKey is stable across attempts;
  AttemptId/ReservationId do not enter business idempotency identity.

Settlement rules:

- before Dispatcher, a held reservation is Released;
- after DispatchStarted, success and deterministic business, authorization, or
  validation failure default to Committed and do not receive automatic refunds;
- only verifiable proof that business execution never began may release after a
  dispatch transition; string ErrorCode inference is not proof;
- cancellation, timeout, lost connection, process failure, or unknown execution
  state is Budget Indeterminate;
- the current CapabilityPipeline converts OperationCanceledException to
  TimedOut without proving that Handler work did not happen or stopped, so the
  default Agent settlement is Indeterminate;
- finalization failures are recorded and never represented as “business did not
  execute.”
- Reserve/Finalize responses that are unknown because the adapter may have
  durably changed state are not released or retried automatically; the logical
  invocation is marked Indeterminate and remains available for reconciliation.

No allow-unlimited default budget gate is registered. Tests use a concurrent
in-memory ledger. A production distributed ledger is outside Phase 8f.

## 16. Governance audit and independent terminal states

Two audit layers remain distinct:

| Layer | Responsibility |
| --- | --- |
| Agent Tool Governance Audit | selection, roles, fingerprint, lease/fencing, approval, budget, blocks, dispatch transition, finalization |
| Capability Audit | actual Capability execution through existing AuditMiddleware |

Agent audit never replaces Capability audit. Capability audit does not prove
Agent selection, approval, budget, or replay decisions.

The governance sink has two idempotent checkpoints tied by one AuditId, plus a
decision record for denials that occur before approval/budget reservation:

```csharp
public interface IAgentToolGovernanceAuditor
{
    ValueTask RecordDecisionAsync(
        AgentToolGovernanceDecisionRecord record,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolGovernanceAuditHandle> RecordPreDispatchAsync(
        AgentToolGovernancePreDispatchRecord record,
        CancellationToken cancellationToken = default);

    ValueTask FinalizeAsync(
        AgentToolGovernanceFinalizationRecord record,
        CancellationToken cancellationToken = default);
}
```

`RecordDecisionAsync` records Role/SelectionPolicy, schema/argument,
approval, and known budget denials without fabricating an Approval, Lease, or
Budget reservation. An uncertain approval/budget result is recorded as an
Indeterminate decision and fences the logical invocation. If budget settlement
cannot be confirmed, `FinalizeAsync` uses `BudgetReservationState.Unknown` with
`InvocationState=Indeterminate`; it must still close or durably mark the audit
checkpoint for reconciliation. Decision records are idempotent only when the
same AttemptId and full decision content match; a conflicting decision is an
adapter error. `AuditMode.Required` applies to Decision Audit too: if its
record cannot be accepted, the facade returns a stable audit-failure outcome
and never dispatches.

Pre-dispatch succeeds only after its record is durably accepted according to
the adapter's advertised guarantee. Finalize uses AuditId plus the logical and
attempt identities, records whether DispatchStarted was obtained, the Attempt
final state, and the optional logical Invocation terminal state, and is
idempotent. A Released pre-dispatch Attempt has no logical Invocation terminal
state; it must not be mislabeled Completed or Indeterminate merely to close the
audit checkpoint. A Required sink cannot acknowledge success while silently
buffering into volatile process memory unless the Host explicitly selected a
documented single-node development adapter.

Governance audit records metadata by default:

- Tool descriptor identity and contract hash;
- resolved Capability identity and contract hash;
- tenant/user/Agent/execution/invocation correlation;
- CallOrigin and Agent roles hash;
- ArgumentsHash, not full arguments; decision records may carry
  `ArgumentsEvaluated=false` and a null hash for pre-evaluation masking;
- AttemptId, LeaseId-safe reference, and FencingToken;
- EvidenceId and claim result;
- ReservationId, budget category/units, and settlement;
- optional read-only `ObservedReservation` on a malformed budget decision so a
  reconciliation adapter can recover a returned ReservationId;
- block/failure code, DispatchStarted, and terminal state.

Full arguments/output are not recorded by default. Audit implementations own
redaction, retention, and persistence.

Required pre-dispatch audit failure releases a held reservation and lease and
does not call Dispatcher. BestEffort audit failure records a diagnostic and may
continue only where descriptor validation allows BestEffort. Role/Selection
denials always retain the external `UnknownTool` mask, even when Required
Decision Audit itself is unavailable.

Budget and Invocation terminal state are independent. Invocation is Completed
only when all of these are determined:

```text
Capability outcome is known
AND output contract has a deterministic conclusion
AND budget settlement is known
AND required governance finalization succeeded
AND the invocation terminal outcome was persisted
```

The completion protocol is a three-phase fence:

```text
settle Budget
→ PrepareCompletion (Invocation = CompletionPending)
→ Finalize Required governance audit
→ PublishCompletion (Completed replay becomes visible)
```

If preparation fails after the Dispatcher, the audit checkpoint is finalized as
`Budget=Committed`, `Attempt=Indeterminate`, `Invocation=Indeterminate` when
possible. If Required audit finalization fails, the same Indeterminate
finalization is attempted and publication is skipped. If publication is
uncertain, the gate remains fenced and reconciliation is authoritative; no
concurrent request may observe a Completed replay during the pending window.

Invocation is Indeterminate when any critical result becomes unknown after
DispatchStarted.

This combination is valid and required:

```text
Budget = Committed
Invocation = Indeterminate
Reason = PostDispatchAuditFailure
```

A fencing transition whose durable result is unknown before this Worker calls
Dispatcher is also represented independently:

```text
Budget = Released
Invocation = Indeterminate
DispatchStarted obtained = false/unknown to this Worker
Reason = DispatchFenceUncertain
```

The Worker can prove it did not enter business execution and may release budget,
but it cannot safely reacquire the logical invocation while the durable gate may
have recorded the transition.

A deterministic exact-output-type or OutputSchema violation can finish as:

```text
Budget = Committed
Invocation = Completed
Outcome = InternalContractFailure
```

when required governance finalization succeeds. It cannot automatically repeat
the business call. Only an inability to determine output/finalization state is
Indeterminate. BestEffort audit failure does not by itself prevent Completed;
Required audit failure does.

## 17. Invocation order and Dispatcher integration

The fixed invocation order is:

1. validate outer request and trusted Agent/tenant/user context;
2. look up the immutable snapshot entry by ToolName;
3. check Agent roles and SelectionPolicy × CallOrigin; denied entries behave as
   unknown Tools;
4. normalize arguments and validate object root, duplicates, closed properties,
   and captured InputSchema;
5. generated binder materializes exact TInput;
6. build canonical ArgumentsHash and invocation fingerprint including
   CallOrigin;
7. acquire logical invocation lease;
8. return Completed replay, InProgress, Indeterminate, or Conflict without
   entering later gates when applicable;
9. validate/claim approval evidence;
10. reserve budget for the current AttemptId;
11. write required governance pre-audit;
12. atomically call `TryMarkDispatchStartedAsync(LeaseId, FencingToken)`;
13. only on success, dispatch the captured CapabilityDescriptor with
    `InvocationSource.Agent`;
14. map deterministic Capability failure or serialize exact output and validate
    captured OutputSchema;
15. settle budget;
16. persist `CompletionPending` through the valid fenced lease;
17. finalize required governance audit as Completed (or Indeterminate on any
    known post-dispatch failure);
18. publish Completed replay visibility through the same fenced lease.

Every role, selection, input, acquisition, approval, budget, pre-audit, expired
lease, and fencing rejection proves Dispatcher call count zero.

Dispatcher use is fixed:

```csharp
await dispatcher.DispatchAsync(
    entry.Capability,
    InvocationSource.Agent,
    input,
    ctx =>
    {
        ctx.CausationId = execution.CausationId;
        ctx.IdempotencyKey = idempotencyKeyBuilder.Build(entry, execution);
        ctx.InputJson = normalizedArguments.Clone();

        ctx.Items[AgentCapabilityContextItemNames.ToolDescriptorId]
            = entry.Descriptor.Id;
        ctx.Items[AgentCapabilityContextItemNames.ToolDescriptorVersion]
            = entry.Descriptor.Version;
        ctx.Items[AgentCapabilityContextItemNames.ToolName]
            = entry.Descriptor.ToolName;
        ctx.Items[AgentCapabilityContextItemNames.AgentId]
            = execution.AgentId;
        ctx.Items[AgentCapabilityContextItemNames.ExecutionId]
            = execution.ExecutionId;
        ctx.Items[AgentCapabilityContextItemNames.InvocationId]
            = execution.InvocationId;
        ctx.Items[AgentCapabilityContextItemNames.CallOrigin]
            = execution.CallOrigin;
        ctx.Items[AgentCapabilityContextItemNames.AttemptId]
            = lease.AttemptId;
        ctx.Items[AgentCapabilityContextItemNames.ApprovalEvidenceId]
            = approval.EvidenceId;
        ctx.Items[AgentCapabilityContextItemNames.BudgetReservationId]
            = reservation.ReservationId;
    },
    cancellationToken);
```

The Item names are typed constants owned by Agent.Tools runtime. Handler code
does not parse Tool protocols or governance string keys.

The Agent Capability IdempotencyKey uses versioned canonical SHA-256 over Tool,
Capability, and Schema hashes plus trusted logical ExecutionId and InvocationId.
It does not include AttemptId or ReservationId. A changed arguments or CallOrigin
for the same logical key is stopped by the invocation fingerprint conflict
before Capability idempotency can replay it.

## 18. Outcomes and safe error mapping

The provider-neutral outcome has stable classifications:

```text
UnknownTool
InvalidRequest
GovernanceDenied
InProgress
InvocationConflict
InvocationIndeterminate
CapabilityFailure
InternalContractFailure
InternalServer
```

The concrete contract carries success/error classification, safe content,
optional structured output, and stable internal code. It does not expose raw
Capability ErrorMessage, stack traces, SQL, authorization policy details, CLR
types, inner exceptions, or unsanitized audit data.

Safe field guidance may use `CapabilityExecutionResult.Issues` code and field
path only when `ErrorCode == ValidationFailed` and the issue code is in the
fixed Schema validation allowlist (`FIELD_REQUIRED`, `TYPE_MISMATCH`, and the
other built-in shape/range/property codes). Authorization, rate limit, timeout,
unknown business failures, and arbitrary Handler/Middleware issue codes receive
stable generic model-facing messages.

Unknown or role/selection-denied Tool names share UnknownTool. Malformed outer
request or non-object arguments are InvalidRequest. Approval/budget/audit blocks
are GovernanceDenied without exposing sensitive policy internals. A fingerprint
mismatch is InvocationConflict. A previously or newly uncertain dispatch is
InvocationIndeterminate and must tell an adapter not to auto-retry.

Successful typed output becomes provider-neutral structured JSON. Successful
void output carries stable completion text and no structured output. Errors do
not place arbitrary error objects into a declared business OutputSchema.

## 19. Metadata, topology, canonical hashing, and Control Plane boundary

Add `AgentToolRelationshipExtractor`:

```text
AgentCapabilityToolDescriptor
  -- References / Strong / Role=Capability -->
CapabilityDescriptor
```

Schema impact continues through the existing Capability-to-Schema edges. The
Agent Tool descriptor does not duplicate input/output Schema references and
does not introduce parallel compatibility truth.

Synchronize every explicit generic Metadata mapping for
`DescriptorKind.AgentTool`: descriptor names, registry routing, stable-hash
dispatch, topology/impact traversal, snapshot/package refs, and diagnostic
rendering. The following allowlists remain intentionally unchanged:

```text
Agent Draft supported kinds
Agent Authoring supported kinds
Agent Control Plane activation/mutation kinds
```

Those governance surfaces may inspect generic AgentTool metadata through
topology or package summaries, but Phase 8f does not let them create, approve,
activate, mutate, or invoke Agent Tool descriptors. The existing Control Plane
`AgentToolDescriptor` remains the Phase 7c manifest contract and is not a
projection descriptor.

Agent Tool ContractHash includes every field that can change model selection,
exposure, governance, binding, or execution:

- Descriptor Id, Name, Version, State, and SupersededById, following existing
  descriptor conventions;
- Capability Id, Version, SelectionMode, and ExpectedContractHash;
- ToolName, Title, and Description;
- SelectionPolicy, SideEffectKind, and RiskFloor;
- ApprovalMode, AuditMode, Budget Category, CostUnits, and
  MaxCallsPerExecution;
- AllowedAgentRoles after Ordinal validation and deterministic Ordinal sorting.

`Title` is deliberately in ContractHash. It is sent to the model-facing
discovery contract and can change selection behavior; treating it as UI-only
DefinitionHash metadata would make an invocation fingerprint replay across a
behavioral Tool change.

DefinitionHash follows the repository's descriptor-definition profile and also
contains the complete Agent Tool definition. Runtime-derived EffectiveRisk,
effective approval, resolved Latest Capability version, generated CLR types,
delegates, JsonTypeInfo, and service implementation identities are not written
back into either descriptor hash. Resolved Capability and Schema hashes are
bound separately in the runtime entry and invocation fingerprint.

The Agent Tool canonical profile owns a dedicated
`CapabilityProjectionReference` writer. Exact and Latest references, and
references that differ only by ExpectedContractHash, must not hash identically.
Invalid handwritten descriptors remain hashable for diagnostics before runtime
validation rejects them.

DescriptorPackage continues to serialize refs, kinds, relationships, and hash
entries rather than concrete polymorphic descriptor payloads. Package coverage
therefore proves AgentTool ref/kind/hash/relationship round trips and does not
invent a new concrete `AgentCapabilityToolDescriptor` JSON contract. Snapshot
and package readers must accept existing artifacts created before Kind 9 was
known, while rejecting malformed new Kind 9 entries through the normal
validation path.

## 20. Diagnostics and observability

Agent Tool Projection uses the `ATP` diagnostic prefix to avoid collision with
the existing Agent Control Plane `AgentToolDiagnosticCodes` surface.

### 20.1 Generator diagnostics

| Code | Meaning |
|---|---|
| ATP001 | invalid or empty CapabilityId |
| ATP002 | invalid ToolName |
| ATP003 | duplicate ToolName in one generation container |
| ATP004 | empty Description or BudgetCategory |
| ATP005 | non-positive DescriptorVersion |
| ATP006 | unsupported InputType root |
| ATP007 | unsupported OutputType root |
| ATP008 | invalid or duplicate DescriptorId in one container |
| ATP009 | unknown selection, side-effect, approval, audit, or risk-floor enum |
| ATP010 | invalid `[AgentToolSpecs]` container declaration |
| ATP011 | invalid `[AgentToolSpec]` nested declaration |
| ATP012 | negative CapabilityVersion |
| ATP013 | empty, duplicate, or wildcard AllowedAgentRoles entry |
| ATP014 | invalid CostUnits or MaxCallsPerExecution |
| ATP015 | contradictory Query/Command side-effect classification detectable at compile time |
| ATP016 | unsafe approval/audit combination detectable at compile time |

ATP016 is intentionally limited to statically provable unsafe combinations:
ExternalWrite/Destructive with BestEffort audit, or explicit High/Critical risk
without Required approval and audit. Unknown side-effect semantics are resolved
at startup after the captured Capability kind is available.

One Error in a container suppresses all Agent Tool provider, binding, and type
registration output for that container. The generator must not emit a partial
runtime catalog that happens to omit the invalid Tool.

### 20.2 Startup validation issues

| Code | Meaning |
|---|---|
| ATP101 | global Active ToolName conflict |
| ATP102 | descriptor identity/version conflict |
| ATP103 | invalid handwritten or generated descriptor contract |
| ATP104 | unsupported Capability selection/version combination |
| ATP105 | Capability resolution failure |
| ATP106 | ExpectedContractHash mismatch |
| ATP107 | Capability Schema reference is not exact and versioned |
| ATP108 | input Schema/CLR type presence mismatch |
| ATP109 | output Schema/CLR type presence mismatch |
| ATP110 | missing generated binding |
| ATP111 | binding identity/type mismatch |
| ATP112 | missing input/output JsonTypeInfo |
| ATP113 | reflection-capable or invalid Agent Tool JSON configuration |
| ATP114 | JsonTypeInfo root kind is not Object |
| ATP115 | Schema/JsonTypeInfo directional parity failure |
| ATP116 | unsupported Schema root, field, collection, or constraint shape |
| ATP117 | invalid Query/Command side-effect classification |
| ATP118 | invalid risk floor or risk reduction attempt |
| ATP119 | unsafe effective approval/audit combination |
| ATP120 | missing invocation gate |
| ATP121 | missing approval gate/verifier required by an Active Tool |
| ATP122 | missing budget gate/ledger |
| ATP123 | missing governance audit sink |
| ATP124 | invalid lifecycle or SupersededBy relationship |
| ATP125 | immutable snapshot publication failure |

Startup issues are not Roslyn diagnostics. Any Error prevents snapshot
publication, places the build state in Failed, and requires a corrected restart.
No discovery or invocation path may observe an empty fallback snapshot.

Stable runtime codes used for audit, telemetry, and safe provider mapping
include:

```text
AGENT_TOOL_UNKNOWN
AGENT_TOOL_INVALID_REQUEST
AGENT_TOOL_SELECTION_DENIED
AGENT_TOOL_APPROVAL_DENIED
AGENT_TOOL_BUDGET_DENIED
AGENT_TOOL_PRE_AUDIT_FAILED
AGENT_TOOL_INVOCATION_IN_PROGRESS
AGENT_TOOL_INVOCATION_CONFLICT
AGENT_TOOL_INVOCATION_INDETERMINATE
AGENT_TOOL_LEASE_EXPIRED
AGENT_TOOL_FENCING_REJECTED
AGENT_TOOL_OUTPUT_TYPE_MISMATCH
AGENT_TOOL_OUTPUT_SCHEMA_VIOLATION
AGENT_TOOL_UNEXPECTED_OUTPUT
AGENT_TOOL_MISSING_OUTPUT
AGENT_TOOL_POST_DISPATCH_FINALIZATION_FAILED
```

Runtime codes are not exception messages. Adapters map them to the stable safe
outcomes in section 18 and may use a stricter redaction policy. Metrics should
separate discovery denial, pre-dispatch governance denial, DispatchStarted,
Completed, and Indeterminate; a single generic failure counter would hide the
most important operational distinction.

## 21. Registration and implementation ergonomics

The ordinary application path stays short:

1. declare a Capability and its Schema-backed input/output DTOs;
2. declare one `[AgentToolSpec]` selecting that Capability;
3. add the application source-generated JSON context;
4. register `AddCrestAgentTools()` and Host-owned governance adapters;
5. establish a trusted `AgentExecutionContext` scope before catalog or
   invocation calls.

Applications do not implement binders, canonical hashes, Schema projection,
lease orchestration, Dispatcher calls, or audit sequencing.

The DI surface should separate framework composition from policy adapters:

```text
AddCrestAgentTools(configureJson)
IAgentToolInvocationGate
IAgentToolApprovalGate
IAgentToolBudgetGate
IAgentToolGovernanceAuditor
IAgentExecutionContextAccessor
```

Framework registration does not silently install permissive production
implementations. Explicit test/development helpers may install in-memory
adapters, but their names and documentation must state their durability and
restart limitations. In-memory registration must not occur merely because the
Host forgot a service.

`CapabilityProfile.RequireApproval`, if a Host chooses to consume it through an
approval adapter, may only raise the effective requirement. Phase 8f neither
modifies Capability profile resolution nor claims it is an enforced Capability
middleware. There is one execution mainline: the Agent Tool facade performs
Agent governance and then calls the existing Capability Dispatcher.

Provider adapters are consumers of `IAgentToolCatalog` and
`IAgentToolInvoker`. They may translate provider request/response shapes and
establish a trusted call scope, but cannot:

- enumerate a mutable descriptor registry;
- choose a Handler or Capability version at call time;
- weaken roles, SelectionPolicy, approval, budget, or audit decisions;
- synthesize ExecutionId, InvocationId, AgentId, roles, or CallOrigin from model
  arguments;
- reinterpret `InvocationIndeterminate` as a safe automatic retry;
- serialize business CLR output with their own reflection options.

## 22. Testing strategy

### 22.1 Generator tests

Cover valid provider/binding/type-registration output, no-input, void-output,
exact typed DTOs, explicit risk-floor mapping, safe enum defaults, zero/latest
and positive/exact CapabilityVersion semantics, deterministic role ordering,
all ATP001-ATP016 diagnostics, container-level output suppression, Unknown
side-effect + BestEffort authoring, and rejected
interface/abstract/open-generic/dynamic-dictionary/primitive roots.

Generated-source guards reject MCP types, provider SDKs, DynamicApi, ASP.NET,
Agent Control Plane execution, Handler resolution, `Dictionary<string,
object?>`, `DefaultJsonTypeInfoResolver`, reflection JsonSerializer overloads,
and direct Dispatcher calls.

### 22.2 Shared projection and MCP regression tests

Freeze byte-identical JSON Schema output and parity results before and after the
protocol-neutral extraction. Run the existing MCP runtime, E2E, canonical hash,
package/snapshot, generator, boundary, and NativeAOT suites unchanged.

For `CapabilityProjectionReference`, add golden vectors covering Exact, Latest,
ExpectedContractHash null/non-null, obsolete-wrapper conversion, generated
source, and old package/snapshot JSON. If the public CLR migration gates in
section 4.1 fail, test and implement the specified stop path instead of
weakening expectations.

### 22.3 Registry, discovery, and binding tests

Cover:

- handwritten-provider validation and every lifecycle state;
- Exact/Latest Capability resolution and one-time Latest capture;
- ExpectedContractHash match/mismatch;
- exact Schema capture, supported subset, parity, and frozen source-generated
  JsonTypeInfo-only options;
- missing binding, type registration, governance adapter, or JsonTypeInfo;
- global ToolName and descriptor identity conflicts;
- ContractHash/DefinitionHash vectors, including Title, sorted roles, budget,
  policy, and Capability reference fields;
- Active-only immutable snapshot and deterministic Ordinal discovery;
- all SelectionPolicy × CallOrigin combinations and Unknown fail-closed cases;
- role filtering, no existence oracle, and invocation-time recheck;
- Title and governance summaries in provider-neutral discovery;
- exact input binding, absent `{}` normalization, duplicate/unknown properties,
  strict primitives/collections, and exact output serialization/validation.

### 22.4 Invocation, concurrency, approval, budget, and audit tests

Use deterministic barriers and fake clocks rather than timing-sensitive sleeps.
At minimum freeze these cases:

- same logical key + same fingerprint concurrent acquisition permits exactly
  one DispatchStarted transition;
- same logical key + different arguments, CallOrigin, role set, Tool hash, or
  Capability hash returns Conflict and calls Dispatcher zero times;
- Completed success and deterministic failure replay the stored safe outcome
  without approval, reservation, dispatch, or finalization repetition;
- expired pre-dispatch lease permits a fenced new attempt and rejects stale
  completion/release/dispatch;
- expired or uncertain post-DispatchStarted lease becomes Indeterminate and
  never auto-dispatches;
- renewal preserves AttemptId/LeaseId/FencingToken ownership and extends only
  ExpiresAt;
- evidence same fingerprint retry is idempotent, different InvocationId or
  fingerprint is denied, expiry/revocation is re-evaluated before dispatch, and
  cross-node claims are never attributed to the in-memory adapter;
- Released reservation permits a later Attempt with a new ReservationId;
- Reserved is reused within one Attempt, Completed replay reserves zero times,
  Committed/Indeterminate consume capacity, and Released returns capacity;
- every pre-dispatch rejection releases held reservation/lease and proves
  Dispatcher call count zero;
- every deterministic post-DispatchStarted Capability failure commits budget;
- cancellation/timeout with unknown execution becomes Budget and Invocation
  Indeterminate;
- output contract failure can be Budget Committed + Invocation Completed with
  InternalContractFailure;
- required post-dispatch audit failure can be Budget Committed + Invocation
  Indeterminate;
- stale fencing tokens cannot overwrite either Completed or Indeterminate;
- terminal budget finalization and invocation transitions are idempotent by
  their own identities.

### 22.5 Capability mainline, E2E, and NativeAOT tests

Capability integration tests prove that Agent dispatch uses the captured
descriptor overload, `InvocationSource.Agent`, stable logical idempotency key,
canonical `InputJson`, ambient TenantId/UserId, context item constants, and the
unchanged authorization, validation, rate-limit, idempotency, audit, event, and
Handler pipeline.

The generator-backed E2E Host covers ReadOnly Query, approved external-write
Command, no-input Query, void Command, AutomaticAllowed and ExplicitOnly,
authorization/validation failure, approval denial, budget denial, pre-audit
failure, concurrent same/different fingerprint calls, Completed replay,
Released-attempt retry, Indeterminate blocking, exact output failures, and
deterministic discovery.

The formal NativeAOT gate performs a real linux-x64 `PublishAot`, completes
native linking, and executes the native binary through:

```text
generated descriptor/binding registration
→ snapshot and discovery JSON Schema
→ source-generated input deserialization
→ invocation/approval/budget/audit gates
→ Dispatcher/Pipeline/Handler
→ exact output serialization and Schema validation
→ terminal replay
```

It must produce no Agent Tool path IL2026/IL3050 warnings and must not rely on
Generated CRUD DTOs tracked by issue #61. PublishTrimmed, analyzer-only, or
source-generation-only evidence is insufficient. Other RIDs skip rather than
claim portable NativeAOT verification.

### 22.6 Dependency-boundary tests

Freeze these boundaries:

```text
Metadata.AgentTool.Abstractions
  → Metadata.Abstractions only

Agent.Tools.Abstractions / Agent.Tools / generated Agent Tool output
  × CrestCreates.Mcp*
  × DynamicApi / ASP.NET / AppService compatibility execution
  × Agent Control Plane execution / Draft / Activation
  × provider SDKs
  × direct Handler invocation
  × runtime assembly scanning
  × reflection JSON fallback
  × Dictionary<string, object?> argument fallback
```

Also prove that adding `DescriptorKind.AgentTool` does not add it to Agent
Draft/Authoring/Control Plane mutation allowlists and that the Phase 7c
Control Plane `AgentToolDescriptor` CLR contract is unchanged.

## 23. Delivery slices

0. **Shared-kernel compatibility gate**: extract protocol-neutral Schema/JSON
   code, introduce `CapabilityProjectionReference`, run every MCP golden/E2E/AOT
   gate, and stop the reference migration if compatibility cannot be retained.
1. **Metadata and contracts**: DescriptorKind/name, Agent Tool metadata project,
   authoring/runtime abstractions, canonical profiles, relationship extraction,
   snapshot/package support, and dependency boundaries.
2. **Generator and exact binding**: attributes, semantic model, diagnostics,
   descriptor provider, exact binder/serializer, type registration, and
   generated-source guards.
3. **Snapshot and discovery**: registry, Capability/Schema capture,
   ExpectedContractHash, JSON options, parity, effective governance derivation,
   immutable snapshot, and role/origin-aware catalog.
4. **Invocation integrity and governance**: fingerprint, logical invocation
   gate, lease/fencing, approval evidence, budget state machine, governance
   audit, Dispatcher integration, result mapping, and reconciliation-safe
   terminal handling.
5. **Executable closure**: runtime/concurrency/Capability/E2E tests, linux-x64
   NativeAOT publish-and-run, usage documentation, memory update, and Issue #60
   acceptance evidence.

These are implementation-plan slices, not a requirement for six pull requests.
Slice 0 is deliberately first: Phase 8f cannot build on a shared extraction that
silently regresses the already closed MCP mainline.

## 24. Exit criteria

Phase 8f is complete only when:

1. an explicitly selected Capability generates an independent
   `AgentCapabilityToolDescriptor` and exact binding;
2. `Agent.Tools` is the only Phase 8f runtime slice and neither
   `Agent.Abstractions` nor `Agent.Runtime` becomes a parallel runtime;
3. Agent and MCP share only protocol-neutral Schema/JSON infrastructure and
   have no runtime dependency on each other;
4. the Phase 8e MCP compatibility, canonical golden, E2E, and NativeAOT gates
   remain green;
5. every governance enum has safe zero semantics and unknown values fail
   closed;
6. SelectionPolicy and CallOrigin remain separate, and CallOrigin participates
   in the canonical invocation fingerprint;
7. discovery is immutable, deterministic, role-aware, origin-aware, and not an
   authorization token;
8. invocation uses trusted Host context, `InvocationSource.Agent`, and only the
   captured `CapabilityDescriptor` Dispatcher overload;
9. generated input/output binding uses exact types and application-owned
   source-generated JsonTypeInfo with no reflection or dictionary fallback;
10. Schema remains the input/output authority and actual serialized output is
    validated before return;
11. Title and every model-selection/governance field participate in Agent Tool
    ContractHash;
12. one logical invocation permanently binds one fingerprint and at most one
    attempt reaches DispatchStarted;
13. lease expiry, renewal, atomic DispatchStarted, and fencing reject stale
    Workers without claiming cross-node exactly-once from the in-memory gate;
14. approval evidence can replay only for the same logical invocation and
    fingerprint, and production cross-node claims belong to a durable Host
    adapter;
15. Released reservations permit a new attempt/reservation, while Completed
    replay never reserves again and Indeterminate blocks automatic retry;
16. Budget and Invocation retain independent terminal states, including Budget
    Committed + Invocation Indeterminate;
17. every pre-dispatch block proves Dispatcher call count zero and releases
    held attempt resources consistently;
18. every post-DispatchStarted unknown result becomes Indeterminate rather than
    a false “not executed” result;
19. Capability authorization, validation, rate limiting, idempotency, audit,
    events, tenant/user propagation, and Handler execution remain authoritative;
20. governance audit covers selection, identity, fingerprint, lease/fencing,
    evidence, reservation, DispatchStarted, settlement, and terminal state
    without storing sensitive payloads by default;
21. Agent Draft, Authoring, Activation, and Control Plane cannot create,
    approve, activate, mutate, or invoke Phase 8f Tools;
22. missing binding, JsonTypeInfo, Capability, Schema, or required governance
    adapter fails startup without an empty/permissive fallback;
23. concurrency tests deterministically prove single DispatchStarted ownership,
    stale-worker rejection, Completed replay, and Indeterminate blocking;
24. the first-party Agent Tool path passes a real linux-x64 NativeAOT
    publish-link-run fixture with no path-specific IL2026/IL3050 warnings;
25. provider runtimes, durable distributed stores, approval workflows, planner
    loops, hot reload, and issue #61 remain explicitly outside Phase 8f.

## 25. Deferred work

The following require separate issues or phases and must not be hidden inside
the Phase 8f implementation:

- OpenAI, Microsoft Agent Framework, or other provider adapters;
- a general Agent Runtime/planner/session loop;
- durable distributed invocation journal and reconciliation service;
- durable approval workflow, approver UI, or HumanTask integration;
- production distributed budget ledger and governance audit store;
- provider-specific streaming, progress, cancellation, and Tool-result
  protocol semantics;
- hot reload or runtime activation of Agent Tool descriptors;
- expansion of the shared Schema subset beyond primitive object fields and
  primitive collections;
- Generated CRUD trimming-safe JSON contracts tracked by issue #61;
- removal of an obsolete `McpCapabilityReference` wrapper after its declared
  migration window.
