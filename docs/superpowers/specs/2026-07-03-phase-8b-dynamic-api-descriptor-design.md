# Phase 8b - Dynamic API Descriptor Design

**Date**: 2026-07-03
**Status**: Draft
**Parent Issue**: #20 - Phase 8b: Dynamic API Descriptor

---

## 1. Overview

Phase 8b introduces endpoint descriptors as projection metadata over existing
`CapabilityDescriptor` definitions.

The new `CapabilityEndpointDescriptor` describes how a capability may be
exposed through HTTP / Dynamic API metadata. It does not describe business
execution, handler dispatch, schema authority, or permission authority.

Target relationship:

```text
CapabilityDescriptor
    -> referenced by
CapabilityEndpointDescriptor
    -> relationship edge
Descriptor Topology / Graph
    -> future runtime binding phase
Endpoint Runtime Projection
```

Non-target relationship:

```text
CapabilityEndpointDescriptor
    -> executes business logic
```

Also non-target:

```text
CapabilityEndpointDescriptor
    -> duplicates CapabilityDescriptor input / output / permission / handler logic
```

## 2. Existing Code Facts

Current relevant facts:

- `CapabilityDescriptor` already lives in the metadata descriptor model and
  implements `IDescriptor` and `IVersionedDescriptor`.
- `CapabilityDescriptor` is the authority for capability kind, input schema,
  output schema, permissions, risk level, produced events, and consumed events.
- Descriptor relationships are extracted through
  `IDescriptorRelationshipExtractor` and consumed by
  `IDescriptorRelationshipProvider`.
- `DescriptorTopologyBuilder` builds graph nodes and edges from caller-provided
  descriptors and extracted relationships.
- Dynamic API currently has `DynamicApiEndpointDescriptor` as generated runtime
  API metadata for service/action endpoints.
- Dynamic API currently has a small `CapabilityEndpointDescriptor`, but it is
  not a graph descriptor, has no registry/provider path, and does not implement
  `IDescriptor` / `IVersionedDescriptor`.

Design consequence:

`DynamicApiEndpointDescriptor` remains generated Dynamic API runtime metadata.
`CapabilityEndpointDescriptor` becomes the metadata graph descriptor for
capability HTTP exposure.

## 3. Design Principles

1. `CapabilityDescriptor` remains authoritative for business capability
   semantics.
2. `CapabilityEndpointDescriptor` is only an exposure projection.
3. Endpoint metadata must reference a capability; it must not copy capability
   logic.
4. Relationship coverage must use the existing descriptor relationship
   extraction mainline.
5. The descriptor must be topology-compatible and stable-hash-compatible.
6. No endpoint execution, route mapping, controller generation, or handler
   dispatch is added in this phase.
7. No runtime reflection scanner or fallback path is introduced.

## 4. Descriptor Kind and Namespace

Add a descriptor kind:

```csharp
public enum DescriptorKind
{
    Unknown = 0,
    Schema = 1,
    Capability = 2,
    Event = 3,
    Workflow = 4,
    Form = 5,
    HumanTask = 6,
    DynamicApiEndpoint = 7
}
```

Add canonical name:

```csharp
public const string DynamicApiEndpoint = "DynamicApiEndpoint";
```

The endpoint descriptor namespace is:

```csharp
public string Namespace => "dynamic-api-endpoint";
```

Rationale:

- It is explicit and mirrors future projection descriptors such as `mcp-tool`
  and `agent-tool`.
- It avoids making `"dynamic-api"` sound like a broad subsystem namespace.
- It makes topology edges readable:
  `dynamic-api-endpoint.create-book -> capability.create-book`.

## 5. Core Descriptor Model

Refine `CapabilityEndpointDescriptor`:

```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DynamicApi;

public sealed class CapabilityEndpointDescriptor : IDescriptor, IVersionedDescriptor
{
    public string Namespace => "dynamic-api-endpoint";
    public DescriptorKind Kind => DescriptorKind.DynamicApiEndpoint;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }

    public required VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }

    public CapabilityEndpointHttpMethod HttpMethod { get; init; }
    public string RoutePattern { get; init; } = string.Empty;
    public CapabilityEndpointAuthorizationMode AuthorizationMode { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;

    public IReadOnlyList<CapabilityEndpointInputBinding> InputBindings { get; init; }
        = Array.Empty<CapabilityEndpointInputBinding>();

    public CapabilityEndpointOutputMapping OutputMapping { get; init; } = new();

    public CapabilityEndpointProjectionMetadata Projection { get; init; } = new();
}
```

Important constraints:

- `Capability` is required where the project language level allows `required`.
  If a target context cannot use `required`, validation must reject the default
  `VersionedDescriptorRef<CapabilityDescriptor>` value by checking for empty
  `Id` and a non-positive `Version`.
- No endpoint-level `InputSchema`.
- No endpoint-level `OutputSchema`.
- No endpoint-level `Permissions`.
- No handler, invoker, service type, method info, endpoint delegate, or runtime
  execution reference.

## 6. HTTP and Projection Types

Use trim-friendly metadata enums rather than `System.Net.Http.HttpMethod`:

```csharp
public enum CapabilityEndpointHttpMethod
{
    Get,
    Post,
    Put,
    Patch,
    Delete
}
```

Authorization mode:

```csharp
public enum CapabilityEndpointAuthorizationMode
{
    InheritCapability,
    RequireAuthenticated,
    AllowAnonymous
}
```

`AllowAnonymous` rule:

> AllowAnonymous is a projection request, not an authority override.
> Validation must reject it when it weakens the referenced Capability.

At minimum, `AllowAnonymous` must fail when the referenced capability has
permissions or high-risk semantics.

Parameter source:

```csharp
public enum CapabilityEndpointParameterSource
{
    Route,
    Query,
    Header,
    Body
}
```

Input binding:

```csharp
public sealed record CapabilityEndpointInputBinding
{
    public string Name { get; init; } = string.Empty;
    public CapabilityEndpointParameterSource Source { get; init; }
    public string? CapabilityInputPath { get; init; }
    public bool Required { get; init; } = true;
}
```

Output mapping:

```csharp
public sealed record CapabilityEndpointOutputMapping
{
    public int SuccessStatusCode { get; init; } = 200;
    public string? ContentType { get; init; }
}
```

Projection metadata:

```csharp
public sealed record CapabilityEndpointProjectionMetadata
{
    public string? OperationId { get; init; }
    public string? GroupName { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
    public CapabilityEndpointVisibility Visibility { get; init; }
        = CapabilityEndpointVisibility.Public;
}
```

Visibility:

```csharp
public enum CapabilityEndpointVisibility
{
    Public,
    Internal,
    Hidden
}
```

`Projection` must not become a catch-all bag. Core contract fields stay on the
descriptor top level:

- `HttpMethod`
- `RoutePattern`
- `AuthorizationMode`
- `InputBindings`
- `OutputMapping`

Route convention decision:

- `RoutePattern` is a normalized external HTTP path and must start with `/`.
- This is intentionally stricter than existing generated Dynamic API internals
  such as `RoutePrefix` / `RelativeRoute`, which may use `"api"`-style
  fragments.
- Future runtime binding may translate descriptor route patterns into the
  runtime route registration shape, but Phase 8b stores the normalized metadata
  form only.

## 7. Provider and Registry

Add provider contract:

```csharp
public interface ICapabilityEndpointDescriptorProvider
    : IDescriptorProvider<CapabilityEndpointDescriptor>
{
}
```

Add a registry as the default Phase 8b integration path:

```csharp
public interface ICapabilityEndpointRegistry
    : IVersionedDescriptorRegistry<CapabilityEndpointDescriptor>
{
    IReadOnlyList<CapabilityEndpointDescriptor> GetByCapability(
        string capabilityId,
        int? capabilityVersion = null);
}
```

Registry behavior:

- Build from `ICapabilityEndpointDescriptorProvider`.
- Inherit the existing `RegistryBase<TDescriptor>` pattern.
- Keep read model behavior only.
- Do not map endpoints.
- Do not execute capabilities.
- Do not query service methods.
- Provide capability-based lookup for diagnostics, tooling, and future exposure
  projection phases.

Provider-only integration is acceptable only if implementing
`CapabilityEndpointRegistry` expands this phase beyond the existing
`RegistryBase<TDescriptor>` pattern. Such a deviation must be explicit in the
implementation plan.

## 8. Relationship Coverage

Add a relationship extractor:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;

namespace CrestCreates.DynamicApi;

public sealed class CapabilityEndpointRelationshipExtractor
    : DescriptorRelationshipExtractorBase<CapabilityEndpointDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.DynamicApiEndpoint;

    protected override IReadOnlyList<DescriptorRelationship> Extract(
        CapabilityEndpointDescriptor descriptor)
    {
        return
        [
            new DescriptorRelationship(
                From: new DescriptorRef(
                    descriptor.Namespace,
                    descriptor.Id,
                    descriptor.Version),
                To: new DescriptorRef(
                    "capability",
                    descriptor.Capability.Id,
                    descriptor.Capability.Version),
                Kind: RelationshipKind.References,
                Role: "Capability",
                SourcePath: nameof(CapabilityEndpointDescriptor.Capability),
                Strength: RelationshipStrength.Strong,
                IsRuntimeBinding: false)
        ];
    }
}
```

Rationale:

- The endpoint descriptor strongly references the capability.
- The edge is part of descriptor metadata topology.
- `IsRuntimeBinding = false` because Phase 8b does not bind runtime endpoints.
- `RelationshipKind.References` is enough; this is a projection reference, not
  a consumes/produces/handler execution edge.

Add DI registration in Dynamic API service registration:

```csharp
services.AddSingleton<IDescriptorRelationshipExtractor,
    CapabilityEndpointRelationshipExtractor>();
```

This must be additive to the existing relationship mainline.

## 9. Validation

Add validator coverage through the existing registry validation pattern if a
registry is added. If the registry is deferred, add a standalone validator that
is later plugged into the registry.

Validation rules:

1. `Id`, `Name`, and `RoutePattern` must be non-empty.
2. `Version` must be greater than zero.
3. `Capability` must not be the default
   `VersionedDescriptorRef<CapabilityDescriptor>` value. `Capability.Id` must
   be non-empty and `Capability.Version` must be greater than zero.
4. The referenced `CapabilityDescriptor` must exist when a capability registry
   is available to the validator.
5. `AllowAnonymous` must be rejected when it weakens the referenced capability.
   Reject it when the referenced capability has one or more permissions or when
   the referenced capability has high-risk semantics, such as
   `CapabilityRiskLevel.High` or stronger.
6. `RoutePattern` must start with `/`.
7. Route tokens in `RoutePattern` must have matching route input bindings.
8. Route input bindings must refer to route tokens present in `RoutePattern`.
9. Body input binding count must not exceed one.
10. `OutputMapping.SuccessStatusCode` must be a valid success status code
    between 200 and 299.
11. `Projection.OperationId`, when present, must be stable and non-empty.
12. The endpoint descriptor must not introduce endpoint-owned schema or
    permission authority.

Validation must not call capability handlers, dispatch pipelines, route
builders, MVC, or generated endpoint runtime.

## 10. Canonical Hash Coverage

Because `DescriptorTopologyBuilder` computes stable hashes for every node,
`CapabilityEndpointDescriptor` needs a canonical hash profile before it can be
used as a topology node.

Contract hash fields:

- `Id`
- `Name`
- `Version`
- `State`
- `SupersededById`
- `Capability`
- `HttpMethod`
- `RoutePattern`
- `AuthorizationMode`
- `InputBindings`
- `OutputMapping`
- `Projection.OperationId`

Definition-only fields:

- `Projection.GroupName`
- `Projection.Tags`
- `Projection.Summary`
- `Projection.Description`
- `Projection.Deprecated`
- `Projection.Visibility`

Excluded fields:

- `Namespace`
- `Kind`

Shape versions:

```text
dynamic-api-endpoint-contract-hash-v1
dynamic-api-endpoint-definition-hash-v1
```

`Projection.OperationId` is treated as contract hash material because it is a
stable external operation identity. Changing it can break generated clients,
OpenAPI consumers, audit correlation, and external automation.

The canonical hash profile must use explicit value profiles for nested record
types if the generator requires them. The `CapabilityEndpointProjectionMetadata`
value profile must classify `OperationId` as contract and classify the remaining
projection display/governance fields as definition-only. Do not rely on
reflection-based JSON serialization.

## 11. Control Plane and Descriptor Kind Impact

Adding `DescriptorKind.DynamicApiEndpoint` requires updating code that treats
descriptor kinds as a closed range.

Known required update:

- `AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind` currently validates
  a fixed range ending at `HumanTask`. Extend it to include
  `DynamicApiEndpoint`.

Policy behavior:

- Existing closed-world authorization mode still denies the new descriptor kind
  unless explicitly allowed.
- Development allow-all mode may show the new descriptor kind after it is valid.
- Deny rules must still win.

Do not broaden visibility as a side effect of adding the new descriptor kind.

## 12. Dynamic API Runtime Boundary

This phase does not change the Dynamic API generated runtime mainline.

Existing `DynamicApiEndpointDescriptor` remains generated service/action runtime
metadata. It may be linked in a future phase, but Phase 8b does not make it a
descriptor graph node.

No changes in this phase:

- No `IEndpointRouteBuilder` mapping.
- No Minimal API binding.
- No MVC controller generation.
- No Swagger UI or API management UI.
- No gateway.
- No `CapabilityDispatcher` integration.
- No `CapabilityPipeline` integration.
- No handler invocation.
- No runtime reflection fallback.

## 13. Testing Strategy

Add focused tests for metadata behavior only.

Descriptor tests:

- `CapabilityEndpointDescriptor_Implements_VersionedDescriptor`
- `CapabilityEndpointDescriptor_Uses_DynamicApiEndpoint_Namespace`
- `CapabilityEndpointDescriptor_DoesNotExpose_SchemaOrPermissionAuthority`

Relationship tests:

- Extractor returns one strong `References` edge to the capability.
- Edge `From` uses `dynamic-api-endpoint` namespace.
- Edge `To` uses `capability` namespace and exact capability version.
- Edge role is `Capability`.
- Edge source path is `Capability`.
- `IsRuntimeBinding` is false.

Topology tests:

- Topology includes endpoint and capability nodes.
- Endpoint has direct dependency on capability.
- Capability has endpoint as direct dependent.
- Missing referenced capability reports `MISSING_TARGET`.

Validation tests:

- Missing route fails.
- Missing capability ref fails.
- Default `VersionedDescriptorRef<CapabilityDescriptor>` fails.
- `AllowAnonymous` with capability permissions fails.
- `AllowAnonymous` with high-risk capability fails.
- One body binding is accepted.
- Two body bindings fail.
- Route token without matching binding fails.
- Route binding without route token fails.
- Route pattern without leading `/` fails.

Hash tests:

- Changing route pattern changes contract hash.
- Changing referenced capability version changes contract hash.
- Changing projection operation id changes contract hash.
- Changing projection summary changes definition hash but not contract hash.

Control-plane kind tests:

- `DescriptorKind.DynamicApiEndpoint` is a valid descriptor kind.
- Closed-world visibility denies it unless explicitly allowed.
- Deny rule overrides allow rule.

Regression tests:

- No generated endpoint mapping is added by this phase.
- No tests call `CapabilityDispatcher`.
- No tests require MVC controller generation.

## 14. Acceptance Criteria

Phase 8b is complete when:

1. `CapabilityEndpointDescriptor` is a real versioned descriptor.
2. It references `CapabilityDescriptor` through a typed versioned descriptor
   reference.
3. It models HTTP method, route pattern, authorization behavior, input binding,
   output mapping, and projection metadata.
4. It does not duplicate capability schemas, permissions, handlers, or business
   execution logic.
5. Relationship extraction emits endpoint-to-capability coverage.
6. Descriptor topology can include endpoint descriptors and traverse to their
   referenced capabilities.
7. Stable hash computation supports endpoint descriptors.
8. Validation prevents endpoint metadata from weakening capability authority.
9. `CapabilityEndpointRegistry` follows the existing `RegistryBase<TDescriptor>`
   pattern unless explicitly deferred in the implementation plan due to phase
   expansion.
10. Dynamic API runtime execution and endpoint binding remain untouched.

## 15. Out of Scope

Explicitly out of scope:

1. Endpoint runtime execution.
2. Minimal API / `EndpointRouteBuilder` binding.
3. MVC controller generation.
4. API UI / Swagger UI / API management UI.
5. Gateway behavior.
6. MCP tool projection.
7. Agent tool projection.
8. Capability handler execution.
9. New business logic.
10. Endpoint descriptor as a new authority for business capability semantics.
11. Runtime service scanning.
12. Reflection-based fallback.

## 16. Implementation Notes

Recommended implementation order:

1. Add descriptor kind and canonical kind name.
2. Refine `CapabilityEndpointDescriptor` and supporting metadata types.
3. Add `ICapabilityEndpointDescriptorProvider` and `CapabilityEndpointRegistry`.
4. Add relationship extractor and tests.
5. Add canonical hash profiles and hash tests.
6. Add validation.
7. Add topology tests.
8. Update control-plane descriptor kind validation.
9. Add DI registration for the registry and relationship extractor.

Do not start from route binding or generated endpoint code. This phase is a
metadata projection phase.
