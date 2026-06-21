# Phase 7c Adapter Readiness — Tool DTO & Source-Generated JSON Contract

**Date**: 2026-06-21
**Issue**: #41
**Status**: Design approved
**Depends on**: Phase 7c tool surface (#39), Visibility closure (#40)

---

## Objective

Prepare the Phase 7c Agent Control Plane Tool Surface for future MCP / HTTP / CLI / TUI adapters by introducing a source-generated JSON serialization contract and hardening DTO boundary constraints.

This is adapter readiness only. It does not implement MCP / HTTP / CLI / TUI adapters.

---

## 1. DTO Identity and Boundary

Existing public sealed records under `CrestCreates.Agent.ControlPlane.Abstractions` are promoted to the official **Phase 7c v1 Tool Contract DTOs**.

They are no longer treated as purely internal service DTOs. They are adapter-ready contracts for future MCP / HTTP / CLI / TUI adapters. However, adapters must still call `IAgentControlPlaneToolService`; DTOs do not grant runtime authority, governance authority, or activation authority.

### Boundary rules

Tool Contract DTOs must not expose:

- `IDescriptor` or other descriptor interfaces;
- `IServiceProvider`;
- registry / store / runtime handler instances;
- Capability / Workflow / HumanTask runtime execution objects;
- `object`, `dynamic`, or `JsonElement` escape-hatch properties.

Tool Contract DTOs may reference stable upstream contract primitives:

- `DescriptorRef`;
- `DescriptorKind`;
- descriptor / version / status enums;
- value objects used as metadata identifiers.

Complex upstream aggregates may remain only if:

- they are from Abstractions projects;
- they are covered by source-generated JSON metadata;
- boundary tests prove they do not expose runtime object graphs or serialization escape hatches;
- #40 visibility closure is already applied before they are returned.

If an upstream aggregate fails these rules, introduce a targeted Agent projection DTO rather than creating a parallel DTO layer for everything.

### Immediate projection candidates

**P0** (must fix in #41):

- `DraftComparisonResult.CurrentActiveDescriptor` must not expose `IDescriptor?`. Replace with `DescriptorSummaryDto` (see Section 3).
- `ReviewResultListResult.Results` and all tools returning `DescriptorDraftReviewResult` — this type contains `IReadOnlyList<IDescriptor>`, `DescriptorTopologySnapshot`, and `DescriptorDraftMaterializationResult` (which itself contains `IReadOnlyList<IDescriptor>`). This categorically violates the DTO boundary rules and cannot be test-gated. Introduce `AgentReviewResultDto` (see Section 3).
- `DescriptorDraft` — `DescriptorDraft.Payload` is declared as abstract `DescriptorDraftPayload` with 6 concrete subtypes (`CapabilityDescriptorDraftPayload`, `WorkflowDescriptorDraftPayload`, `HumanTaskDescriptorDraftPayload`, `FormDescriptorDraftPayload`, `EventDescriptorDraftPayload`, `SchemaDescriptorDraftPayload`), and `DescriptorDraftPayload.GetDescriptor()` returns `IDescriptor`. There is no `JsonPolymorphic` / `JsonDerivedType` configuration, so source-generated round-trip (especially deserialization) cannot be completed reliably. Introduce `AgentDescriptorDraftDto` with typed payload DTOs (see Section 3).
- `CreateDescriptorDraftRequest.Payload` is `DescriptorDraftPayload` — the same abstract base with `GetDescriptor() -> IDescriptor`. Replace with `AgentDraftPayloadDto` (see Section 3).
- `UpdateDescriptorDraftRequest.Payload` is `DescriptorDraftPayload?` — same violation, nullable. Replace with `AgentDraftPayloadDto?` (see Section 3).

Adapter readiness is a bidirectional contract. Request DTOs must satisfy the same boundary rules as result DTOs.

**P1** (test-gated, may remain temporarily):

- `DescriptorDraftListResult.Drafts` : `AgentDescriptorDraftDto` (after P0 draft projection, this is automatically adapter-safe)
- `PackageEvidencePreview.PackagePreview` : `DescriptorPackagePreview`

These may remain only if:

- source-generated `JsonTypeInfo` exists;
- JSON round-trip works without reflection fallback;
- boundary tests prove they do not expose runtime graphs or escape-hatch types;
- visibility projection is applied before they are returned.

If any condition fails, introduce targeted Agent projection DTOs.

### Physical layout

Move tool contract DTO files into `ToolDtos/` while keeping namespace unchanged:

```
CrestCreates.Agent.ControlPlane.Abstractions

ControlPlane.Abstractions/
  ToolDtos/          ← existing DTO files moved here (flat, no Requests/Results/Common split)
  Json/              ← new JsonSerializerContext + Options factory
  (interfaces stay at root)
```

Do not create a `ToolDtos` namespace or a parallel DTO project in Phase 7c.
Do not split `Requests/Results/Common` unless file count grows enough to justify it.
Do not move enum files to a separate `Enums/` directory in Phase 7c.

---

## 2. Source-Generated JSON Contract

### Contract ownership

Phase 7c uses hybrid JSON contract ownership.

`AgentControlPlaneToolJsonSerializerContext` owns all Agent Control Plane tool root contracts:

- every tool request DTO;
- every tool result DTO;
- `AgentToolResult<TResult>` for every tool result type;
- shared Agent tool contract primitives.

Stable upstream contract primitives may be registered directly:

- `DescriptorRef`
- `DescriptorKind`
- `DescriptorState`
- `RelationshipKind`
- `DescriptorDraftStatus`
- `DescriptorDraftOperation`
- `DescriptorStableHashes`

`DescriptorRelationship` may be registered directly only if it remains a stable value object and does not expose descriptor interfaces, `object`/`dynamic`/`JsonElement`, or runtime graph references. Current inspection confirms it is a `sealed record` with `DescriptorRef`, `RelationshipKind`, and primitive properties — safe to register.

Complex upstream aggregates should be owned by their upstream projects. Since those projects do not currently expose `JsonSerializerContext` types, `AgentControlPlaneToolJsonSerializerContext` may temporarily register required aggregate types that pass the boundary rules:

- `MetadataContextPack`
- `MetadataContextPackRequest`
- `DescriptorDraftValidationResult`
- `DescriptorPackagePreview`
- `DescriptorPackageEvidence`

**Not registered** (P0 projected to Agent DTOs instead):

- `DescriptorDraft` — abstract `DescriptorDraftPayload` with polymorphic subtypes and `IDescriptor` method; replaced by `AgentDescriptorDraftDto` (see Section 3)
- `DescriptorDraftPayload` — abstract base with `IDescriptor` return; replaced by typed payload DTOs
- `DescriptorDraftReviewResult` — contains `IReadOnlyList<IDescriptor>`, `DescriptorTopologySnapshot`, `DescriptorDraftMaterializationResult`; replaced by `AgentReviewResultDto` (see Section 3)

Temporary registration does not make `Agent.ControlPlane` the long-term owner of these aggregate JSON contracts. Once upstream contexts exist, Agent should remove duplicate aggregate registrations and compose resolvers.

Types that are not adapter-safe must use targeted Agent projection DTOs instead of registering the original upstream type.

### Serializer context

```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentDescriptorDraftDto))]
[JsonSerializable(typeof(AgentReviewResultDto))]
[JsonSerializable(typeof(DescriptorSummaryDto))]
[JsonSerializable(typeof(BuildMetadataContextPackRequest))]
[JsonSerializable(typeof(AgentToolResult<MetadataContextPack>))]
// ... every tool request DTO, result DTO, and AgentToolResult<TResult>
[JsonSerializable(typeof(DescriptorRef))]
[JsonSerializable(typeof(DescriptorKind))]
// ... stable upstream contract primitives
[JsonSerializable(typeof(MetadataContextPack))]      // Temporary ownership
// ... temporarily-owned upstream aggregates (only those passing boundary rules)
public sealed partial class AgentControlPlaneToolJsonSerializerContext
    : JsonSerializerContext
{
}
```

The context must include `JsonSerializable` entries for:

- every tool request DTO;
- every tool result DTO;
- `AgentToolResult<TResult>` for every tool result type;
- shared contract primitives;
- temporarily-owned upstream aggregates.

### Serializer options

Adapters should not construct ad-hoc reflection-based `JsonSerializerOptions`.

Phase 7c provides a single options factory:

```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

public static class AgentControlPlaneToolJsonSerializerOptions
{
    public static JsonSerializerOptions CreateDefault()
    {
        return new JsonSerializerOptions
        {
            TypeInfoResolver = AgentControlPlaneToolJsonSerializerContext.Default,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
```

Initially it uses `AgentControlPlaneToolJsonSerializerContext.Default` as its resolver. When upstream `JsonContext`s become available, this factory composes resolvers with `JsonTypeInfoResolver.Combine(...)`.

Adapters must use this factory or an equivalent explicit source-generated resolver composition. Adapters should not silently fall back to reflection-based serialization.

### Contract version

Phase 7c introduces a machine-readable contract version:

```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

public static class AgentControlPlaneContractVersion
{
    public const string Current = "7c.v1";
}
```

This version is referenced by:
- `AgentToolDescriptor` (each tool manifest entry carries the contract version it was built against);
- `AgentControlPlaneToolJsonSerializerContext` (the context is versioned, not individual DTOs);
- future adapter handshake / capability negotiation.

The version is a single constant, not duplicated per DTO. When the contract evolves (e.g., to `7c.v2` or `8.v1`), the constant changes and adapters can detect incompatibility.

---

## 3. Mapper / Projection Strategy

Phase 7c uses the C+ strategy.

There is no general-purpose `IAgentControlPlaneToolDtoMapper`. Existing sealed records are the 7c.v1 Tool Contract DTOs and are passed directly through `IAgentControlPlaneToolService`.

Projection is introduced only at real boundaries:

1. domain / manifest model → JSON-safe contract DTO;
2. unsafe upstream aggregate → adapter-safe projection;
3. future `AgentToolResult<T>` → protocol adapter envelope;
4. future protocol-specific adapter shapes (MCP / HTTP / CLI).

Projection is not part of the normal tool invocation path. It is only used to remove unsafe domain/runtime shapes from contract DTOs.

Protocol-specific mapping is out of scope for #41.

### P0 projection

Three upstream types must be projected because they violate DTO boundary rules and cannot be reliably source-generated for JSON round-trip.

#### P0.1 `IDescriptor?` → `DescriptorSummaryDto`

`DraftComparisonResult.CurrentActiveDescriptor` must not expose `IDescriptor?`.

```csharp
public sealed record DescriptorSummaryDto
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? LifecycleState { get; init; }
}
```

`DraftComparisonResult` becomes:

```csharp
public sealed record DraftComparisonResult
{
    public required AgentDescriptorDraftDto Draft { get; init; }
    public DescriptorSummaryDto? CurrentActiveDescriptor { get; init; }  // was IDescriptor?
    public required IReadOnlyList<DraftDifference> Differences { get; init; }
}
```

#### P0.2 `DescriptorDraft` → `AgentDescriptorDraftDto`

`DescriptorDraft.Payload` is declared as abstract `DescriptorDraftPayload` with 6 concrete subtypes. `DescriptorDraftPayload.GetDescriptor()` returns `IDescriptor`. No `JsonPolymorphic` / `JsonDerivedType` configuration exists, so source-generated round-trip (especially deserialization) cannot be completed reliably.

Introduce `AgentDescriptorDraftDto` with explicit typed payload DTOs:

```csharp
public sealed record AgentDescriptorDraftDto
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required DescriptorKind DescriptorKind { get; init; }
    public required string DescriptorId { get; init; }
    public required DescriptorDraftOperation Operation { get; init; }
    public required DescriptorDraftAuthorKind AuthorKind { get; init; }
    public required string AuthorId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required AgentDraftPayloadDto Payload { get; init; }

    public string? BaseVersion { get; init; }
    public string? ProposedVersion { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public string? CorrelationId { get; init; }
    public string? Source { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public DescriptorDraftStatus Status { get; init; } = DescriptorDraftStatus.Created;
}
```

Payload DTOs use a nested one-of shape — one optional sub-record per `DescriptorKind`, with a `Discriminator` property for explicit source-gen-friendly deserialization:

```csharp
public sealed record AgentDraftPayloadDto
{
    public required DescriptorKind Discriminator { get; init; }
    public AgentCapabilityDraftPayloadDto? Capability { get; init; }
    public AgentWorkflowDraftPayloadDto? Workflow { get; init; }
    public AgentHumanTaskDraftPayloadDto? HumanTask { get; init; }
    public AgentFormDraftPayloadDto? Form { get; init; }
    public AgentEventDraftPayloadDto? Event { get; init; }
    public AgentSchemaDraftPayloadDto? Schema { get; init; }
}

public sealed record AgentCapabilityDraftPayloadDto
{
    public DescriptorRef? DescriptorRef { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? InputSchema { get; init; }
    public string? OutputSchema { get; init; }
    public string? CapabilityKind { get; init; }
    public string[]? Categories { get; init; }
    public DescriptorRef[]? Produces { get; init; }
    public DescriptorRef[]? Consumes { get; init; }
    public string[]? SemanticTags { get; init; }
    public string[]? Permissions { get; init; }
    public string? RiskLevel { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public string? Version { get; init; }
}

public sealed record AgentWorkflowDraftPayloadDto
{
    public DescriptorRef? DescriptorRef { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? WorkflowKind { get; init; }
    public string? TriggerType { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public string? Version { get; init; }
}

public sealed record AgentHumanTaskDraftPayloadDto
{
    public DescriptorRef? DescriptorRef { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? HumanTaskKind { get; init; }
    public string? AssignmentStrategy { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public string? Version { get; init; }
}

public sealed record AgentFormDraftPayloadDto
{
    public DescriptorRef? DescriptorRef { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? FormKind { get; init; }
    public string? FormSchema { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public string? Version { get; init; }
}

public sealed record AgentEventDraftPayloadDto
{
    public DescriptorRef? DescriptorRef { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? EventKind { get; init; }
    public string? EventType { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public string? Version { get; init; }
}

public sealed record AgentSchemaDraftPayloadDto
{
    public DescriptorRef? DescriptorRef { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? SchemaKind { get; init; }
    public string? JsonSchema { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public string? Version { get; init; }
}
```

**Design rationale**: A nested one-of shape with `Discriminator` + optional sub-records per kind avoids the polymorphic serialization problem entirely. Each kind populates exactly one sub-record. This is AOT-safe, source-gen friendly, avoids `JsonPolymorphic` / `JsonDerivedType`, and is more type-safe than a wide union with flat optional fields — it prevents illegal cross-kind field combinations (e.g., `Discriminator = Capability` with `WorkflowKind` populated).

**Invariant**: Only the sub-record matching the `Discriminator` should be populated. Projection helpers must not populate sub-records from other descriptor kinds. Boundary tests should reject or flag mixed-kind payloads where practical.

**Request-side usage**: `CreateDescriptorDraftRequest.Payload` and `UpdateDescriptorDraftRequest.Payload` also use `AgentDraftPayloadDto` instead of `DescriptorDraftPayload`. The same invariant applies — adapter sends one sub-record matching the `Discriminator`, and the service reconstructs the domain `DescriptorDraftPayload` from it.

The projection helper:

```csharp
internal static class AgentDescriptorDraftDtoProjection
{
    public static AgentDescriptorDraftDto FromDraft(DescriptorDraft draft)
    {
        return new AgentDescriptorDraftDto
        {
            TenantId = draft.TenantId,
            DraftId = draft.DraftId,
            DescriptorKind = draft.DescriptorKind,
            DescriptorId = draft.DescriptorId,
            Operation = draft.Operation,
            AuthorKind = draft.AuthorKind,
            AuthorId = draft.AuthorId,
            CreatedAt = draft.CreatedAt,
            Payload = MapPayload(draft.Payload),
            BaseVersion = draft.BaseVersion,
            ProposedVersion = draft.ProposedVersion,
            Intent = draft.Intent,
            Rationale = draft.Rationale,
            CorrelationId = draft.CorrelationId,
            Source = draft.Source,
            Metadata = draft.Metadata,
            Status = draft.Status
        };
    }

    private static AgentDraftPayloadDto MapPayload(DescriptorDraftPayload payload) =>
        payload switch
        {
            CapabilityDescriptorDraftPayload cp => new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Capability,
                Capability = MapCapabilityPayload(cp)
            },
            WorkflowDescriptorDraftPayload wp => new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Workflow,
                Workflow = MapWorkflowPayload(wp)
            },
            // ... other payload kinds, each populating only its sub-record
            _ => new AgentDraftPayloadDto { Discriminator = payload.DescriptorKind }
        };

    // Reverse: AgentDraftPayloadDto -> DescriptorDraftPayload (for request-side)
    public static DescriptorDraftPayload ToDomainPayload(AgentDraftPayloadDto dto)
    {
        // STRICT validation: Discriminator must match exactly one non-null sub-record.
        // All other sub-records must be null.
        // If mismatched, throw a control-plane internal exception or return
        // a validation diagnostic — never silently pick the first non-null sub-record.
        ValidateDiscriminatorConsistency(dto);
        // ... reconstruct domain payload based on Discriminator
    }

    private static void ValidateDiscriminatorConsistency(AgentDraftPayloadDto dto)
    {
        // e.g. if Discriminator = Capability, only dto.Capability may be non-null
        // all other sub-records must be null
    }
}
```

This helper lives in `CrestCreates.Agent.ControlPlane` (not Abstractions) because it depends on `DescriptorDraftPayload` and its concrete subtypes.

#### P0.3 `DescriptorDraftReviewResult` → `AgentReviewResultDto`

`DescriptorDraftReviewResult` contains:
- `IReadOnlyList<IDescriptor>? ProposedInventory` — `IDescriptor` is a forbidden type
- `DescriptorTopologySnapshot?` — topology snapshot internals must not be exposed directly
- `DescriptorDraftMaterializationResult?` — contains `IReadOnlyList<IDescriptor>`
- `DescriptorImpactAnalysisReport?`, `DescriptorCompatibilityReport?`, `DescriptorLifecycleGovernanceReport?` — domain analysis types not verified as adapter-safe

Introduce:

```csharp
public sealed record AgentReviewResultDto
{
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required AgentValidationResultDto ValidationResult { get; init; }
    public AgentMaterializationResultDto? MaterializationResult { get; init; }
    public IReadOnlyList<DescriptorSummaryDto>? ProposedInventorySummary { get; init; }
    public AgentTopologySummaryDto? TopologySummary { get; init; }
    public AgentImpactAnalysisDto? ImpactAnalysis { get; init; }
    public AgentCompatibilityReportDto? CompatibilityResult { get; init; }
    public AgentGovernanceDecisionDto? GovernanceDecision { get; init; }
    public DescriptorStableHashes? StableHashes { get; init; }
    public AgentPackagePreviewSummaryDto? PackagePreviewSummary { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }
    public required bool IsActivationEligible { get; init; }  // Agent-facing readiness signal, NOT governance authority
}

public sealed record AgentMaterializationResultDto
{
    public required bool IsMaterialized { get; init; }
    public required IReadOnlyList<DescriptorSummaryDto> ProposedInventorySummary { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }
}

public sealed record AgentValidationResultDto
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }
}

public sealed record AgentTopologySummaryDto
{
    public required int TotalNodeCount { get; init; }
    public required int TotalEdgeCount { get; init; }
    public required IReadOnlyDictionary<DescriptorKind, int> NodeCountsByKind { get; init; }
    public required IReadOnlyDictionary<RelationshipKind, int> EdgeCountsByKind { get; init; }
}

public sealed record AgentImpactAnalysisDto
{
    public required IReadOnlyList<DescriptorRef> AffectedDescriptors { get; init; }
    public required string Summary { get; init; }
}

public sealed record AgentCompatibilityReportDto
{
    public required bool IsCompatible { get; init; }
    public required IReadOnlyList<DescriptorRef> IncompatibleDescriptors { get; init; }
}

public sealed record AgentGovernanceDecisionDto
{
    public required bool IsApproved { get; init; }
    public required string Decision { get; init; }
    public string? Rationale { get; init; }
}

public sealed record AgentPackagePreviewSummaryDto
{
    public required string PreviewId { get; init; }
    public required DescriptorRef DescriptorRef { get; init; }
}
```

**Design rationale**: Each sub-DTO replaces a domain/infrastructure type with an adapter-safe projection. `ProposedInventory` becomes `ProposedInventorySummary` (list of `DescriptorSummaryDto`). `TopologySnapshot` becomes `AgentTopologySummaryDto` (flat counts, no graph). Analysis reports are projected to their essential adapter-relevant fields.

The projection helper:

```csharp
internal static class AgentReviewResultDtoProjection
{
    public static AgentReviewResultDto FromReviewResult(DescriptorDraftReviewResult result)
    {
        return new AgentReviewResultDto
        {
            DraftId = result.DraftId,
            TenantId = result.TenantId,
            ValidationResult = MapValidation(result.ValidationResult),
            MaterializationResult = MapMaterialization(result.MaterializationResult),
            ProposedInventorySummary = MapInventory(result.ProposedInventory),
            TopologySummary = MapTopology(result.TopologySnapshot),
            ImpactAnalysis = MapImpact(result.ImpactAnalysisResult),
            CompatibilityResult = MapCompatibility(result.CompatibilityResult),
            GovernanceDecision = MapGovernance(result.GovernanceDecision),
            StableHashes = result.StableHashes,
            PackagePreviewSummary = MapPackagePreview(result.PackagePreview),
            Diagnostics = result.Diagnostics,
            IsActivationEligible = result.IsActivationEligible
        };
    }
    // ... individual mapping methods
}
```

This helper lives in `CrestCreates.Agent.ControlPlane` (not Abstractions) because it depends on `DescriptorDraftReviewResult` and its domain sub-types.

**Visibility closure contract**: `AgentReviewResultDtoProjection` is not a new safety boundary. It must only project results that have already been processed through #40 visibility closure. It must not re-derive values from the full `ProposedInventory` / full `TopologySnapshot` that would expose hidden refs. Specifically: denied descriptor kinds must not appear in `ProposedInventorySummary`, `TopologySummary.NodeCountsByKind`, or `ImpactAnalysis.AffectedDescriptors`. A regression test must verify this (see Section 4).

**Semantic clarification on `IsActivationEligible`**: This property in `AgentReviewResultDto` is an agent-facing readiness signal derived after #40 visibility projection. It is not an activation approval, not a governance decision, and not an execution authorization. The Safe Activation Gate / governance authority remains outside #41. Future adapters must not interpret `IsActivationEligible = true` as permission to activate.

#### Shared: DescriptorSummaryDto projection helper

The `DescriptorSummaryDto` projection helper lives in `CrestCreates.Agent.ControlPlane`, not in Abstractions:

```csharp
internal static class DescriptorSummaryDtoProjection
{
    public static DescriptorSummaryDto? FromDescriptor(IDescriptor? descriptor)
    {
        if (descriptor is null) return null;

        return new DescriptorSummaryDto
        {
            Ref = descriptor.Ref,
            Kind = descriptor.Kind,
            Name = descriptor.Name,
            DisplayName = descriptor.DisplayName,
            LifecycleState = descriptor.LifecycleState?.ToString()
        };
    }

    public static IReadOnlyList<DescriptorSummaryDto> FromDescriptors(IReadOnlyList<IDescriptor>? descriptors)
        => descriptors?.Select(FromDescriptor).Where(d => d is not null).Cast<DescriptorSummaryDto>().ToList()
           ?? Array.Empty<DescriptorSummaryDto>();
}
```

All projection DTOs (`DescriptorSummaryDto`, `AgentDescriptorDraftDto`, `AgentDraftPayloadDto`, `AgentReviewResultDto`, and their sub-DTOs) live in `Abstractions/ToolDtos` because they are part of the contract. All projection helpers live in the implementation project because they depend on `IDescriptor`, `DescriptorDraftPayload`, `DescriptorDraftReviewResult`, and their domain sub-types.

### P1 test-gated aggregates

`DescriptorPackagePreview` remains temporarily. It is a pure hash-and-IDs record (`string` and `IReadOnlyList<string>` properties only) with no references to `DescriptorDraft`, `DescriptorDraftReviewResult`, `IDescriptor`, or `DescriptorDraftMaterializationResult`. `DescriptorDraft` and `DescriptorDraftReviewResult` are already handled by P0 projections.

**Hard gate**: If `DescriptorPackagePreview` contains or later introduces `DescriptorDraft`, `DescriptorDraftReviewResult`, `IDescriptor`, topology snapshot internals, `object`/`dynamic`/`JsonElement`, or `DescriptorDraftMaterializationResult`, it must be projected in #41 instead of deferred. Current inspection confirms it is safe.

They are allowed only if:

- they have source-generated `JsonTypeInfo`;
- JSON round-trip works without reflection fallback;
- boundary tests prove they do not expose runtime graphs or escape-hatch types;
- visibility projection is applied before they are returned.

If any condition fails, introduce targeted Agent projection DTOs.

### Non-goals

- no `IAgentControlPlaneToolDtoMapper`;
- no DI mapper;
- no global mapper class;
- no request/result mapper for every tool;
- no adapter response envelope in #41.

---

## 4. Test Strategy

Tests stay in `CrestCreates.Agent.ControlPlane.Tests`. They are grouped under `ToolContracts/` to distinguish contract tests from service implementation tests.

Recommended files:

- `ToolDtoJsonContractTests.cs`
- `ToolDtoBoundaryTests.cs`
- `ToolDtoSemanticPreservationTests.cs`
- `ToolDtoProjectionTests.cs`

### 4.1 JSON contract coverage

- `EveryManifestTool_Has_Request_Result_And_ResultEnvelope_JsonTypeInfo`
- `AllPublicToolContractDtos_Have_JsonTypeInfo`
- `ManifestToolNames_Equal_JsonContractRegistrations_Equal_JsonTypeInfoSet`
- `ToolDtos_RoundTrip_With_SourceGeneratedJson`
- `SerializerOptions_Use_SourceGeneratedResolver`

Coverage must include:

- every tool request DTO;
- every tool result DTO;
- `AgentToolResult<TResult>` for every tool result type;
- shared contract primitives;
- temporary upstream aggregate roots.

**Collection equality gate**: The coverage test must verify that three sets are equal:
1. tool names from the static manifest (`IAgentToolManifestProvider.GetAllTools()`);
2. request/result contract DTO registrations in `AgentControlPlaneToolJsonSerializerContext`;
3. `JsonTypeInfo` entries that resolve successfully from the context.

Tests must not assert a hardcoded tool count. They must dynamically read the manifest and verify every tool has complete contract coverage. This ensures new tools added to the manifest are automatically caught as missing coverage.

**Coverage rules by tool kind**:

Facade tools (Waves 1–6):
- request parameter type has `JsonTypeInfo`;
- result DTO has `JsonTypeInfo`;
- `AgentToolResult<TResult>` has `JsonTypeInfo`.

Manifest query tools (Wave 7):
- request parameter type has `JsonTypeInfo`, if any;
- result DTO has `JsonTypeInfo`;
- no `AgentToolResult<TResult>` required — these are manifest queries, not facade invocations.

If manifest tools later become facade invocations, they must be wrapped in `AgentToolResult<T>` at that time.

### 4.2 DTO boundary constraints

- `ToolContractGraph_Does_Not_Contain_Forbidden_Types`
- `ToolContractGraph_Does_Not_Contain_Object_Dynamic_JsonElement`
- `ToolContractGraph_Does_Not_Contain_Service_Registry_Runtime_Types`
- `DraftComparisonResult_Does_Not_Expose_IDescriptor`
- `AgentReviewResultDto_Does_Not_Expose_IDescriptor_Or_TopologySnapshot`
- `AgentDescriptorDraftDto_Does_Not_Expose_IDescriptor_Or_AbstractPayload`
- `CreateDescriptorDraftRequest_Does_Not_Expose_DescriptorDraftPayload`
- `UpdateDescriptorDraftRequest_Does_Not_Expose_DescriptorDraftPayload`

Boundary checks inspect property type graphs recursively (expanding `IReadOnlyList<T>`, `IReadOnlyDictionary<TKey,TValue>`, `Nullable<T>`, nullable underlying types, generic type arguments, nested record properties) and do not depend on runtime instances. The recursion must not stop at the first property level — it must traverse the full graph, including through P1 types like `DescriptorPackagePreview` and `PackageEvidencePreview`, so that nested paths (e.g., `PackageEvidencePreview.PackagePreview`) are fully checked.

Forbidden types include:

- `IDescriptor` and descriptor interfaces;
- `IServiceProvider`;
- registry / store / runtime handler instances;
- `object` / `dynamic` / `JsonElement` escape hatches;
- implementation project types that should not be part of the contract.

### 4.3 Semantic round-trip preservation

These tests verify serialization preservation, not service algorithms:

- `ContextPack_RoundTrip_Preserves_CanonicalRefs`
- `ContextPack_RoundTrip_Preserves_RelationshipEntries`
- `AgentReviewResultDto_RoundTrip_Preserves_Diagnostics`
- `AgentDescriptorDraftDto_RoundTrip_Preserves_PayloadDiscriminator`
- `AgentDraftPayloadDto_Discriminator_Allows_Only_KindSpecific_SubRecord`
- `AgentDraftPayloadDto_RoundTrip_DoesNot_Lose_KindSpecific_Fields`
- `FixProposal_RoundTrip_Preserves_RiskAndApprovalFields`
- `ActivationRequest_RoundTrip_DoesNotIntroduceExecutionSemantics`
- `ReviewEligibility_DoesNotGrantActivationAuthority`
- `ReviewResultProjection_DoesNot_Expose_DeniedDescriptorKinds_In_SummaryFields`

### 4.4 Temporary upstream aggregate tests

- `TemporaryUpstreamAggregateTypes_Are_Explicitly_Allowlisted`
- `TemporaryUpstreamAggregateTypes_HaveJsonTypeInfo`
- `TemporaryUpstreamAggregateTypes_DoNotExposeRuntimeGraph`

### 4.5 P0 projection tests

- `DescriptorSummaryDto_RoundTrip_With_SourceGeneratedJson`
- `DescriptorSummaryDtoProjection_FromDescriptor_PreservesKeyFields`
- `DraftComparisonResult_RoundTrip_Preserves_CurrentActiveDescriptorSummary`
- `AgentDescriptorDraftDto_RoundTrip_With_SourceGeneratedJson`
- `AgentDescriptorDraftDtoProjection_FromDraft_PreservesAllFields`
- `AgentDraftPayloadDto_RoundTrip_Preserves_DiscriminatorAndKindSpecific_SubRecord`
- `AgentDraftPayloadDto_Discriminator_OnlyPopulates_MatchingSubRecord`
- `AgentReviewResultDto_RoundTrip_With_SourceGeneratedJson`
- `AgentReviewResultDtoProjection_FromReviewResult_PreservesAllFields`
- `CreateDescriptorDraftRequest_Uses_AgentDraftPayloadDto_Not_DescriptorDraftPayload`
- `UpdateDescriptorDraftRequest_Uses_AgentDraftPayloadDto_Not_DescriptorDraftPayload`
- `DescriptorPackagePreview_Does_Not_Reintroduce_ProjectedUnsafeTypes`
- `PackageEvidencePreview_Does_Not_Reintroduce_DescriptorDraft_Or_ReviewResult`

### Test data

Prefer explicit representative builders over AutoFixture for contract roots. AutoFixture may be used only for primitive filler fields. Core descriptor refs, topology edges, diagnostics, risk fields, and activation semantics should be hand-built.

---

## 5. Tool Coverage

The JSON contract must cover all current manifest Phase 7c tools across 7 waves:

### Wave 1 — Context / Read

| Tool | Request DTO | Result DTO |
|------|-------------|------------|
| BuildMetadataContextPack | `MetadataContextPackRequest` | `MetadataContextPack` |
| BuildRuntimeScenarioContextPack | `MetadataContextPackRequest` | `MetadataContextPack` |
| GetDescriptorByRef | `DescriptorRef` | `DescriptorInfo` |
| SearchDescriptors | `DescriptorSearchRequest` | `DescriptorSearchResult` |
| ListDescriptorRelationships | `DescriptorRef` | `DescriptorRelationshipsResult` |
| GetTopologySummary | (none) | `TopologySummaryResult` |

### Wave 2 — Draft

| Tool | Request DTO | Result DTO |
|------|-------------|------------|
| CreateDescriptorDraft | `CreateDescriptorDraftRequest` (payload: `AgentDraftPayloadDto`) | `AgentDescriptorDraftDto` |
| UpdateDescriptorDraft | `UpdateDescriptorDraftRequest` (payload: `AgentDraftPayloadDto?`) | `AgentDescriptorDraftDto` |
| GetDescriptorDraft | `string` (draftId) | `AgentDescriptorDraftDto` |
| ListDescriptorDrafts | `DraftQuery?` | `DescriptorDraftListResult` |
| CancelDescriptorDraft | `string` (draftId) | `AgentDescriptorDraftDto` |
| CompareDescriptorDraft | `string` (draftId) | `DraftComparisonResult` |

### Wave 3 — Review

| Tool | Request DTO | Result DTO |
|------|-------------|------------|
| ValidateDescriptorDraft | `string` (draftId) | `DescriptorDraftValidationResult` |
| ReviewDescriptorDraft | `string` (draftId) | `AgentReviewResultDto` |
| GetDraftReviewResult | `string` (reviewResultId) | `AgentReviewResultDto` |
| ListDraftReviewResults | `string?` (draftId) | `ReviewResultListResult` |
| ExplainDiagnostics | `ExplainDiagnosticsRequest` | `DiagnosticExplanation` |

### Wave 4 — Fix Proposal

| Tool | Request DTO | Result DTO |
|------|-------------|------------|
| SuggestDescriptorDraftFixes | `string` (draftId) | `FixProposalListResult` |
| GetFixProposal | `string` (proposalId) | `FixProposal` |
| ListFixProposals | `string?` (draftId) | `FixProposalListResult` |
| ApplyFixProposalToDraft | `ApplyFixProposalRequest` | `AgentDescriptorDraftDto` |

### Wave 5 — Package Preview

| Tool | Request DTO | Result DTO |
|------|-------------|------------|
| PreviewDescriptorPackage | `string` (draftId) | `DescriptorPackagePreview` |
| GetPackagePreview | `string` (previewId) | `DescriptorPackagePreview` |
| BuildPackageEvidencePreview | `string` (draftId) | `PackageEvidencePreview` |
| BuildActivationReadinessPreview | `string` (draftId) | `ActivationReadinessPreview` |

### Wave 6 — Activation Handoff

| Tool | Request DTO | Result DTO |
|------|-------------|------------|
| SubmitActivationRequest | `SubmitActivationRequestRequest` | `ActivationRequest` |
| GetActivationRequestStatus | `string` (requestId) | `ActivationRequest` |
| CancelActivationRequest | `string` (requestId) | `ActivationRequest` |

### Wave 7 — Manifest

| Tool | Request DTO | Result DTO |
|------|-------------|------------|
| ListAgentTools | (none) | `IReadOnlyList<AgentToolDescriptor>` |
| GetAgentToolDescriptor | `string` (toolName) | `AgentToolDescriptor?` |

These tools are defined in `StaticAgentToolManifestProvider` and are not part of `IAgentControlPlaneToolService`. They are read-only manifest queries. Their request types (`string`, none) are already source-gen friendly. `AgentToolDescriptor` is an existing sealed record in Abstractions and must be registered in `AgentControlPlaneToolJsonSerializerContext`.

### Notes

- Tools that take `string` or `DescriptorRef` as direct parameters (not wrapped in a request DTO) do not need a dedicated request DTO. The parameter type itself must have `JsonTypeInfo`.
- All result types must be wrapped in `AgentToolResult<TResult>` and that generic instantiation must have `JsonTypeInfo`.
- `DraftComparisonResult` uses `DescriptorSummaryDto?` instead of `IDescriptor?` after P0 projection.
- `DescriptorDraft` is replaced by `AgentDescriptorDraftDto` in all tool results (Wave 2, Wave 4).
- `DescriptorDraftReviewResult` is replaced by `AgentReviewResultDto` in all tool results (Wave 3).
- Manifest tools (Wave 7) return `AgentToolDescriptor` directly (not wrapped in `AgentToolResult<T>`), since they are manifest queries, not facade invocations.

---

## 6. Out of Scope

- MCP adapter implementation
- HTTP endpoint implementation
- CLI command implementation
- TUI / GUI implementation
- LLM provider integration
- Prompt template rendering
- CapabilityDescriptor → MCP executable tool projection
- CapabilityDescriptor → runtime agent tool projection
- Runtime handler execution
- Runtime registry activation
- General-purpose `IAgentControlPlaneToolDtoMapper`
- Adapter response envelope types
- Performance optimization of JSON generation mode

---

## 7. Acceptance Criteria

### DTO contract

- [ ] All 7c tool request/result DTOs are explicitly modeled (bidirectional — both request and result sides).
- [ ] DTOs do not expose `IDescriptor`.
- [ ] DTOs do not expose `IServiceProvider` or runtime handler types.
- [ ] DTOs do not expose registry or topology snapshot internals directly.
- [ ] DTOs do not expose abstract `DescriptorDraftPayload` or its polymorphic subtypes (including in request DTOs).
- [ ] `CreateDescriptorDraftRequest.Payload` uses `AgentDraftPayloadDto` instead of `DescriptorDraftPayload`.
- [ ] `UpdateDescriptorDraftRequest.Payload` uses `AgentDraftPayloadDto?` instead of `DescriptorDraftPayload?`.
- [ ] DTOs preserve canonical descriptor refs where applicable.
- [ ] DTOs preserve diagnostics, status, audit refs, and correlation fields.
- [ ] Activation request DTO remains handoff-only and has no approve/execute/activate semantics.
- [ ] `AgentReviewResultDto.IsActivationEligible` is an agent-facing readiness signal, not governance authority.

### JSON source generation

- [ ] All DTOs are included in `AgentControlPlaneToolJsonSerializerContext` or equivalent.
- [ ] Every facade tool has request DTO, result DTO, and `AgentToolResult<TResult>` `JsonTypeInfo`.
- [ ] Every manifest query tool has result DTO `JsonTypeInfo` (no `AgentToolResult<T>` required).
- [ ] Coverage test asserts set equality between manifest tool names and JSON contract registrations (no hardcoded count).
- [ ] Round-trip serialization tests use source-generated metadata, not reflection fallback.
- [ ] `AgentControlPlaneToolJsonSerializerOptions.CreateDefault()` provides a source-generated resolver.
- [ ] `AgentControlPlaneContractVersion.Current` equals `"7c.v1"`.
- [ ] Tests pass under trimming / AOT-sensitive settings where available.

### Mapping

- [ ] `DraftComparisonResult.CurrentActiveDescriptor` uses `DescriptorSummaryDto?` instead of `IDescriptor?`.
- [ ] `AgentDescriptorDraftDto` replaces `DescriptorDraft` in all tool results.
- [ ] `AgentReviewResultDto` replaces `DescriptorDraftReviewResult` in all tool results.
- [ ] `AgentDraftPayloadDto` replaces `DescriptorDraftPayload` in all request and result DTOs.
- [ ] `AgentDraftPayloadDto` uses nested one-of shape (only the sub-record matching `Discriminator` is populated).
- [ ] `DescriptorSummaryDtoProjection.FromDescriptor` correctly maps key fields.
- [ ] `AgentDescriptorDraftDtoProjection.FromDraft` correctly maps all fields including payload sub-records.
- [ ] `AgentDescriptorDraftDtoProjection.ToDomainPayload` correctly reconstructs domain `DescriptorDraftPayload` from DTO.
- [ ] `AgentReviewResultDtoProjection.FromReviewResult` correctly maps all fields including sub-projections.
- [ ] `DescriptorPackagePreview` does not reintroduce `DescriptorDraft`, `DescriptorDraftReviewResult`, `IDescriptor`, or `DescriptorDraftMaterializationResult`.
- [ ] P1 upstream aggregates are test-gated (JSON + boundary + visibility).

### Tests

- [ ] JSON contract coverage tests pass (manifest + assembly, set equality, tool-kind-aware).
- [ ] DTO boundary constraint tests pass (recursive graph check, all P0 projections verified, request-side included).
- [ ] Semantic round-trip preservation tests pass (including `AgentDescriptorDraftDto` payload discriminator, `AgentReviewResultDto` diagnostics, and `ReviewEligibility_DoesNotGrantActivationAuthority`).
- [ ] `AgentDraftPayloadDto` invariant tests pass (discriminator matches populated sub-record only).
- [ ] Temporary upstream aggregate tests pass (allowlist + JsonTypeInfo + no runtime graph).
- [ ] P0 projection tests pass (all three P0 projections: `DescriptorSummaryDto`, `AgentDescriptorDraftDto`, `AgentReviewResultDto`, plus request-side closure).
