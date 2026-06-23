# Canonical Hash Runtime Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all 4 hash systems with a unified canonical hash runtime powered by SG-generated profiles, DTOs, projections, and a deterministic JSON→SHA-256 pipeline.

**Architecture:** Source Generator reads `[CanonicalHashProfile]`/`[CanonicalHashField]` attributes to generate per-kind Contract/Definition hash payloads, typed projections, a switch-based dispatcher returning envelope+JsonTypeInfo, and a JsonContext. Runtime `ICanonicalHashComputer` serializes envelopes via source-generated JsonTypeInfo and applies SHA-256. Hand-written projectors for non-descriptor artifacts (ReviewResult, Package, ReportId) use the same `ComputeFromProjection` entry point.

**Tech Stack:** .NET 10, Roslyn Source Generator (netstandard2.0), System.Text.Json source generation, SHA-256

## Global Constraints

- All SG code targets `netstandard2.0` (cannot reference DescriptorKind enum directly — use string constants)
- Profile classes are compile-time declaration containers only: no interfaces, no DI, no instantiation
- Single field classification declaration per property; SG auto-derives Contract and Definition payloads
- CanonicalHashProjectionResult is public, lives in `CrestCreates.Metadata.Abstractions`
- All hash serialization must use source-generated JsonTypeInfo — no reflection-based overloads, no runtime Type resolution
- Scope is domain-separation metadata only — does not authorize or filter input
- TenantVisible hash must be computed from TenantVisible-projected artifact, not by substituting scope on InternalFull artifact
- PublicCrossTenant scope is reserved in v1 — no projection required
- Package canonical payloads belong in MetadataCanonicalHashJsonContext (same layer as DescriptorPackageHashComputer)
- `DescriptorKind.Unknown = 0` must be added; all runtime switches must reject it
- Hash-value-breaking change is accepted; no compatibility shims
- File deletion: move to `./99_RecycleBin/`, never delete directly

---

## File Structure

### New files — Abstractions (CrestCreates.Metadata.Abstractions)

```
src/Metadata/CrestCreates.Metadata.Abstractions/
  CanonicalHash.cs                          — MODIFY: expand to 9 fields, Scope/Purpose → string
  CanonicalHashScope.cs                     — MODIFY: keep enum, add canonical string helpers
  CanonicalHashPurpose.cs                   — MODIFY: replace Identity with Contract/Definition/SourceBinding/Integrity/AuditEvidence/CacheKey
  CanonicalHashArtifactKind.cs              — NEW: enum
  CanonicalHashFieldClassification.cs       — NEW: enum (replace HashFieldClassification)
  CanonicalHashCollectionOrderMode.cs       — NEW: enum
  CanonicalHashProfileAttribute.cs          — NEW: attribute
  CanonicalHashFieldAttribute.cs            — NEW: attribute
  CanonicalHashEnvelope.cs                  — NEW: generic envelope record
  CanonicalHashProjectionResult.cs          — NEW: public projection result with Create factory
  CanonicalHashScopeNames.cs                — NEW: canonical string constants + ToCanonicalString
  CanonicalHashPurposeNames.cs              — NEW: canonical string constants + ToCanonicalString
  CanonicalHashArtifactNames.cs             — NEW: canonical string constants
  DescriptorKindNames.cs                    — NEW: canonical string constants + ToCanonicalString
  CanonicalHashAlgorithms.cs                — NEW: algorithm constants
  CanonicalStringKeyValuePayload.cs         — NEW: dictionary canonicalization DTOs
  DescriptorKind.cs                         — MODIFY: add Unknown = 0, renumber
  ICanonicalHashComputer.cs                 — MODIFY: ComputeContractHash/ComputeDefinitionHash/ComputeFromProjection
  ICanonicalHashable.cs                     — DELETE → RecycleBin (replaced by SG projection)
  HashFieldPolicy.cs                        — DELETE → RecycleBin (replaced by SG profile)
  DescriptorHashInclusionPolicy.cs          — DELETE → RecycleBin (replaced by SG profile)
  HashFieldClassification.cs                — DELETE → RecycleBin (replaced by CanonicalHashFieldClassification)
  CanonicalHashUtility.cs                   — MODIFY: remove ComputeSha256 methods, keep constants
```

### New files — SG (CrestCreates.CodeGenerator)

```
src/Tooling/CrestCreates.CodeGenerator/
  CanonicalHashGenerator/
    CanonicalHashSourceGenerator.cs         — NEW: main generator entry
    ProfileModel.cs                         — NEW: ROS model from attributes
    PayloadWriter.cs                        — NEW: generates Contract/Definition hash payload records
    ProjectionWriter.cs                     — NEW: generates typed projection methods
    DispatcherWriter.cs                     — NEW: generates CanonicalHashProjectionDispatcher
    JsonContextWriter.cs                    — NEW: generates MetadataCanonicalHashJsonContext
    DiagnosticDescriptors.cs                — NEW: CCHASH001-014 descriptors
```

### New files — Profile declarations (per descriptor project)

```
src/Metadata/CrestCreates.Schema.Abstractions/
  CanonicalHashProfiles/SchemaDescriptorCanonicalHashProfile.cs     — NEW
  CanonicalHashProfiles/SchemaFieldCanonicalHashProfile.cs          — NEW
  CanonicalHashProfiles/SchemaValidationRuleCanonicalHashProfile.cs — NEW

src/Runtime/Capability/CrestCreates.Capability.Abstractions/
  CanonicalHashProfiles/CapabilityDescriptorCanonicalHashProfile.cs — NEW
  CanonicalHashProfiles/EventRefCanonicalHashProfile.cs             — NEW

src/Runtime/Eventing/CrestCreates.Event.Abstractions/
  CanonicalHashProfiles/EventDescriptorCanonicalHashProfile.cs      — NEW

src/Framework/Modules/CrestCreates.Form.Abstractions/
  CanonicalHashProfiles/FormDescriptorCanonicalHashProfile.cs       — NEW
  CanonicalHashProfiles/FormFieldCanonicalHashProfile.cs            — NEW

src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/
  CanonicalHashProfiles/HumanTaskDescriptorCanonicalHashProfile.cs  — NEW
  CanonicalHashProfiles/CompletionOutcomeCanonicalHashProfile.cs    — NEW

src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/
  CanonicalHashProfiles/WorkflowDescriptorCanonicalHashProfile.cs   — NEW
  CanonicalHashProfiles/WorkflowStepCanonicalHashProfile.cs         — NEW
  CanonicalHashProfiles/InteractionTargetCanonicalHashProfile.cs    — NEW
```

### New files — Runtime (CrestCreates.Metadata)

```
src/Metadata/CrestCreates.Metadata/
  DefaultCanonicalHashComputer.cs            — NEW: ICanonicalHashComputer implementation
  ContractVersions.cs                        — NEW: version constants
  DescriptorStableHashBuilder.cs             — MODIFY: degrade to adapter
```

### New files — Hand-written projectors (Agent ControlPlane)

```
src/Runtime/Agent/CrestCreates.Agent.ControlPlane/
  CanonicalHashing/
    ReviewResultCanonicalHashPayload.cs      — NEW
    ReviewResultCanonicalHashProjection.cs   — NEW
    ReportSourceBindingHashPayload.cs        — NEW
    ReportSourceBindingHashProjection.cs     — NEW
    AgentControlPlaneCanonicalHashJsonContext.cs — NEW: partial JsonContext
```

### New files — Package projector (CrestCreates.Metadata)

```
src/Metadata/CrestCreates.Metadata/
  CanonicalHashing/
    DescriptorPackageCanonicalHashPayload.cs     — NEW
    DescriptorPackageCanonicalHashProjection.cs  — NEW
```

### Modified files — Consumers

```
src/Metadata/CrestCreates.Metadata/DescriptorPackageHashComputer.cs  — MODIFY or DELETE → RecycleBin
src/Metadata/CrestCreates.Metadata/CrestCreatesMetadataJsonContext.cs — MODIFY
src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewReportBuilder.cs — MODIFY
src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDraftArtifactVisibilityProjector.cs — MODIFY
src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Projections/AgentReviewResultDtoProjection.cs — MODIFY
samples/CrestCreates.Samples.DescriptorControlPlane/  — MODIFY: update all hash usage
tests/  — MODIFY: update all hash assertions
```

---

### Task 1: Update Abstractions — CanonicalHash + CanonicalHashPurpose + DescriptorKind

**Files:**
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHash.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashPurpose.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKind.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashUtility.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/ICanonicalHashComputer.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorStableHashes.cs`
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/`

**Interfaces:**
- Consumes: existing Phase 1 types (CanonicalHash, CanonicalHashPurpose, DescriptorKind, ICanonicalHashComputer, CanonicalHashUtility)
- Produces: updated CanonicalHash (9 fields, string Scope/Purpose), CanonicalHashPurpose (6 values), DescriptorKind (Unknown=0), ICanonicalHashComputer (3 methods), simplified CanonicalHashUtility

- [ ] **Step 1: Update DescriptorKind — add Unknown = 0**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKind.cs
namespace CrestCreates.Metadata.Abstractions;

public enum DescriptorKind
{
    Unknown = 0,
    Schema = 1,
    Capability = 2,
    Event = 3,
    Workflow = 4,
    Form = 5,
    HumanTask = 6
}
```

- [ ] **Step 2: Update CanonicalHashPurpose — replace Identity with Contract/Definition + add CacheKey**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashPurpose.cs
namespace CrestCreates.Metadata.Abstractions;

public enum CanonicalHashPurpose
{
    Contract = 1,
    Definition = 2,
    SourceBinding = 3,
    Integrity = 4,
    AuditEvidence = 5,
    CacheKey = 6
}
```

- [ ] **Step 3: Update CanonicalHash — expand to 9 fields, Scope/Purpose → string, CanonicalShapeVersion → string**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHash.cs
namespace CrestCreates.Metadata.Abstractions;

public sealed record CanonicalHash
{
    public required string Value { get; init; }
    public required string Algorithm { get; init; }
    public required string AlgorithmVersion { get; init; }
    public required string ArtifactKind { get; init; }
    public string? DescriptorKind { get; init; }
    public required string Scope { get; init; }
    public required string Purpose { get; init; }
    public required string ContractVersion { get; init; }
    public required string CanonicalShapeVersion { get; init; }
}
```

- [ ] **Step 4: Update ICanonicalHashComputer — 3 methods**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/ICanonicalHashComputer.cs
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Metadata.Abstractions;

public interface ICanonicalHashComputer
{
    CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope);
    CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope);
    CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection);
}
```

- [ ] **Step 5: Update DescriptorStableHashes — ContractHash/DefinitionHash already CanonicalHash (no change needed, but verify)**

Read current file to confirm — it already uses `CanonicalHash` type from Phase 2a. No change needed.

- [ ] **Step 6: Simplify CanonicalHashUtility — remove ComputeSha256, keep Algorithm constant**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashUtility.cs
namespace CrestCreates.Metadata.Abstractions;

public static class CanonicalHashUtility
{
    public const string Algorithm = "SHA-256";
}
```

- [ ] **Step 7: Fix all build errors from CanonicalHash field changes**

The `CanonicalHash` record changed from 4 fields to 9 fields with different types. All existing consumers that construct `CanonicalHash` objects must be updated. Search for all `new CanonicalHash` usages and update them. Key consumers:

1. `DescriptorStableHashBuilder.cs` — update all `new CanonicalHash { ... }` constructions
2. `DescriptorPackageHashComputer.cs` — update all `new CanonicalHash { ... }` constructions
3. `DefaultDescriptorReviewReportBuilder.cs` — update SourceReviewHash construction
4. All test files that construct `CanonicalHash`

For each construction site, provide the new fields:
- `Algorithm = CanonicalHashUtility.Algorithm`
- `AlgorithmVersion = "sha256-pipe-delimited-v0"` (temporary for old pipe-delimited implementation)
- `ArtifactKind = "Descriptor"`
- `DescriptorKind = "<kind>"` or `null`
- `Scope = CanonicalHashScopeNames.ToCanonicalString(scope)` (need to add this helper first — see Step 8)
- `Purpose = CanonicalHashPurposeNames.Contract` or similar
- `ContractVersion = "0"` (temporary)
- `CanonicalShapeVersion = "1"` (temporary, matching old CurrentShapeVersion)

- [ ] **Step 8: Build and verify all existing tests pass**

Run: `dotnet build && dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests/`

Expected: All tests pass. Hash values will change (expected for this task — snapshot updates needed).

- [ ] **Step 9: Update test assertions for new hash values**

All tests that assert specific hash string values will need updated expected values. Run the test suite, collect failures, update expected values.

- [ ] **Step 10: Commit**

```bash
git add -A && git commit -m "feat(canonical-hash): update CanonicalHash to 9-field model, add Contract/Definition purpose, add DescriptorKind.Unknown"
```

---

### Task 2: Add Canonical String Helpers + New Enums + Attributes + Envelope + ProjectionResult

**Files:**
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashScopeNames.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashPurposeNames.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashArtifactNames.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKindNames.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashAlgorithms.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashArtifactKind.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashFieldClassification.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashCollectionOrderMode.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashProfileAttribute.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashFieldAttribute.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashEnvelope.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashProjectionResult.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalStringKeyValuePayload.cs`
- Delete → RecycleBin: `src/Metadata/CrestCreates.Metadata.Abstractions/HashFieldClassification.cs`
- Delete → RecycleBin: `src/Metadata/CrestCreates.Metadata.Abstractions/ICanonicalHashable.cs`
- Delete → RecycleBin: `src/Metadata/CrestCreates.Metadata.Abstractions/HashFieldPolicy.cs`
- Delete → RecycleBin: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorHashInclusionPolicy.cs`
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/`

**Interfaces:**
- Consumes: CanonicalHash (from Task 1), CanonicalHashScope, CanonicalHashPurpose, DescriptorKind
- Produces: All new types listed above

- [ ] **Step 1: Create CanonicalHashArtifactKind enum**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashArtifactKind.cs
namespace CrestCreates.Metadata.Abstractions;

public enum CanonicalHashArtifactKind
{
    Descriptor = 1,
    ReviewResult = 2,
    Package = 3,
    Report = 4
}
```

- [ ] **Step 2: Create CanonicalHashFieldClassification enum**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashFieldClassification.cs
namespace CrestCreates.Metadata.Abstractions;

public enum CanonicalHashFieldClassification
{
    Contract = 1,
    DefinitionOnly = 2,
    Excluded = 3
}
```

- [ ] **Step 3: Create CanonicalHashCollectionOrderMode enum**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashCollectionOrderMode.cs
namespace CrestCreates.Metadata.Abstractions;

public enum CanonicalHashCollectionOrderMode
{
    None = 0,
    SourceOrder = 1,
    OrdinalByValue = 2,
    OrdinalByProperty = 3,
    OrderedKeyValue = 4
}
```

- [ ] **Step 4: Create CanonicalHashProfileAttribute**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashProfileAttribute.cs
using System;

namespace CrestCreates.Metadata.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CanonicalHashProfileAttribute : Attribute
{
    public CanonicalHashArtifactKind ArtifactKind { get; init; }

    public DescriptorKind DescriptorKind { get; init; } = DescriptorKind.Unknown;

    public Type TargetType { get; init; } = null!;

    public string ContractShapeVersion { get; init; } = string.Empty;

    public string DefinitionShapeVersion { get; init; } = string.Empty;
}
```

- [ ] **Step 5: Create CanonicalHashFieldAttribute**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashFieldAttribute.cs
using System;

namespace CrestCreates.Metadata.Abstractions;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class CanonicalHashFieldAttribute : Attribute
{
    public CanonicalHashFieldAttribute(
        string propertyName,
        CanonicalHashFieldClassification classification)
    {
        PropertyName = propertyName;
        Classification = classification;
    }

    public string PropertyName { get; }
    public CanonicalHashFieldClassification Classification { get; }
    public int Order { get; init; }
    public Type? ElementProfile { get; init; }
    public CanonicalHashCollectionOrderMode CollectionOrderMode { get; init; }
    public string? OrderByProperty { get; init; }
    public Type? ValueProfile { get; init; }
}
```

- [ ] **Step 6: Create CanonicalHashEnvelope<TPayload>**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashEnvelope.cs
using System.Text.Json.Serialization;

namespace CrestCreates.Metadata.Abstractions;

public sealed record CanonicalHashEnvelope<TPayload>
{
    [JsonPropertyOrder(0)] public required string ArtifactKind { get; init; }
    [JsonPropertyOrder(1)] public string? DescriptorKind { get; init; }
    [JsonPropertyOrder(2)] public required string Scope { get; init; }
    [JsonPropertyOrder(3)] public required string Purpose { get; init; }
    [JsonPropertyOrder(4)] public required string ContractVersion { get; init; }
    [JsonPropertyOrder(5)] public required string CanonicalShapeVersion { get; init; }
    [JsonPropertyOrder(6)] public required string AlgorithmVersion { get; init; }
    [JsonPropertyOrder(100)] public required TPayload Payload { get; init; }
}
```

- [ ] **Step 7: Create CanonicalHashProjectionResult with Create factory**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashProjectionResult.cs
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Metadata.Abstractions;

public sealed record CanonicalHashProjectionResult(
    object Envelope,
    JsonTypeInfo EnvelopeJsonTypeInfo,
    string ArtifactKind,
    string? DescriptorKind,
    string Scope,
    string Purpose,
    string ContractVersion,
    string AlgorithmVersion,
    string CanonicalShapeVersion)
{
    public static CanonicalHashProjectionResult Create<TPayload>(
        CanonicalHashEnvelope<TPayload> envelope,
        JsonTypeInfo<CanonicalHashEnvelope<TPayload>> jsonTypeInfo)
    {
        return new CanonicalHashProjectionResult(
            envelope,
            jsonTypeInfo,
            envelope.ArtifactKind,
            envelope.DescriptorKind,
            envelope.Scope,
            envelope.Purpose,
            envelope.ContractVersion,
            envelope.AlgorithmVersion,
            envelope.CanonicalShapeVersion);
    }
}
```

- [ ] **Step 8: Create CanonicalStringKeyValuePayload types**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalStringKeyValuePayload.cs
using System.Text.Json.Serialization;

namespace CrestCreates.Metadata.Abstractions;

public sealed record CanonicalStringKeyValuePayload
{
    [JsonPropertyOrder(0)] public required string Key { get; init; }
    [JsonPropertyOrder(1)] public string? Value { get; init; }
}

public sealed record CanonicalStringKeyValuePayload<TValue>
{
    [JsonPropertyOrder(0)] public required string Key { get; init; }
    [JsonPropertyOrder(1)] public required TValue? Value { get; init; }
}
```

- [ ] **Step 9: Create canonical string helper classes**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashScopeNames.cs
using System;

namespace CrestCreates.Metadata.Abstractions;

public static class CanonicalHashScopeNames
{
    public const string InternalFull = "InternalFull";
    public const string TenantVisible = "TenantVisible";
    public const string PublicCrossTenant = "PublicCrossTenant";

    public static string ToCanonicalString(CanonicalHashScope scope) => scope switch
    {
        CanonicalHashScope.InternalFull => InternalFull,
        CanonicalHashScope.TenantVisible => TenantVisible,
        CanonicalHashScope.PublicCrossTenant => PublicCrossTenant,
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };
}
```

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashPurposeNames.cs
using System;

namespace CrestCreates.Metadata.Abstractions;

public static class CanonicalHashPurposeNames
{
    public const string Contract = "Contract";
    public const string Definition = "Definition";
    public const string SourceBinding = "SourceBinding";
    public const string Integrity = "Integrity";
    public const string AuditEvidence = "AuditEvidence";
    public const string CacheKey = "CacheKey";

    public static string ToCanonicalString(CanonicalHashPurpose purpose) => purpose switch
    {
        CanonicalHashPurpose.Contract => Contract,
        CanonicalHashPurpose.Definition => Definition,
        CanonicalHashPurpose.SourceBinding => SourceBinding,
        CanonicalHashPurpose.Integrity => Integrity,
        CanonicalHashPurpose.AuditEvidence => AuditEvidence,
        CanonicalHashPurpose.CacheKey => CacheKey,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };
}
```

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashArtifactNames.cs
namespace CrestCreates.Metadata.Abstractions;

public static class CanonicalHashArtifactNames
{
    public const string Descriptor = "Descriptor";
    public const string ReviewResult = "ReviewResult";
    public const string Package = "Package";
    public const string Report = "Report";
}
```

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKindNames.cs
using System;

namespace CrestCreates.Metadata.Abstractions;

public static class DescriptorKindNames
{
    public const string Schema = "Schema";
    public const string Capability = "Capability";
    public const string Event = "Event";
    public const string Workflow = "Workflow";
    public const string Form = "Form";
    public const string HumanTask = "HumanTask";

    public static string ToCanonicalString(DescriptorKind kind) => kind switch
    {
        DescriptorKind.Schema => Schema,
        DescriptorKind.Capability => Capability,
        DescriptorKind.Event => Event,
        DescriptorKind.Workflow => Workflow,
        DescriptorKind.Form => Form,
        DescriptorKind.HumanTask => HumanTask,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
```

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashAlgorithms.cs
namespace CrestCreates.Metadata.Abstractions;

public static class CanonicalHashAlgorithms
{
    public const string Sha256 = "SHA-256";
    public const string AlgorithmVersion = "sha256-canonical-json-v1";
}
```

- [ ] **Step 10: Move old types to RecycleBin**

Move (not delete) these files to `./99_RecycleBin/`:
- `src/Metadata/CrestCreates.Metadata.Abstractions/HashFieldClassification.cs`
- `src/Metadata/CrestCreates.Metadata.Abstractions/ICanonicalHashable.cs`
- `src/Metadata/CrestCreates.Metadata.Abstractions/HashFieldPolicy.cs`
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorHashInclusionPolicy.cs`

Fix any remaining references to these types across the codebase. Key consumers:
- `DescriptorStableHashBuilder.cs` — uses `DescriptorHashInclusionPolicy`, `HashFieldPolicy`, `HashFieldClassification`
- `DescriptorStableHashCoverageTests.cs` — uses `HashFieldClassification`
- All descriptor implementations that implement `ICanonicalHashable`

For `ICanonicalHashable` implementations: remove the interface and `CanonicalDescriptorKind` property from all 6 descriptor types. They no longer need to implement it.

For `DescriptorHashInclusionPolicy`/`HashFieldPolicy`/`HashFieldClassification` usages: replace with `CanonicalHashFieldClassification` where needed, or remove if only used by old hash builder code.

- [ ] **Step 11: Build and fix errors**

Run: `dotnet build`

Expected: Build succeeds. May need to update references in:
- `DescriptorStableHashBuilder.cs` (temporary — will be rewritten in Task 6)
- All test files referencing old types

- [ ] **Step 12: Run tests and fix assertions**

Run: `dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests/`

- [ ] **Step 13: Commit**

```bash
git add -A && git commit -m "feat(canonical-hash): add Profile attributes, Envelope, ProjectionResult, canonical string helpers, remove old policy types"
```

---

### Task 3: Write Profile Classes for All 6 Descriptor Types

**Files:**
- Create: `src/Metadata/CrestCreates.Schema.Abstractions/CanonicalHashProfiles/SchemaDescriptorCanonicalHashProfile.cs`
- Create: `src/Metadata/CrestCreates.Schema.Abstractions/CanonicalHashProfiles/SchemaFieldCanonicalHashProfile.cs`
- Create: `src/Metadata/CrestCreates.Schema.Abstractions/CanonicalHashProfiles/SchemaValidationRuleCanonicalHashProfile.cs`
- Create: `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CanonicalHashProfiles/CapabilityDescriptorCanonicalHashProfile.cs`
- Create: `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CanonicalHashProfiles/EventRefCanonicalHashProfile.cs`
- Create: `src/Runtime/Eventing/CrestCreates.Event.Abstractions/CanonicalHashProfiles/EventDescriptorCanonicalHashProfile.cs`
- Create: `src/Framework/Modules/CrestCreates.Form.Abstractions/CanonicalHashProfiles/FormDescriptorCanonicalHashProfile.cs`
- Create: `src/Framework/Modules/CrestCreates.Form.Abstractions/CanonicalHashProfiles/FormFieldCanonicalHashProfile.cs`
- Create: `src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/CanonicalHashProfiles/HumanTaskDescriptorCanonicalHashProfile.cs`
- Create: `src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/CanonicalHashProfiles/CompletionOutcomeCanonicalHashProfile.cs`
- Create: `src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/CanonicalHashProfiles/WorkflowDescriptorCanonicalHashProfile.cs`
- Create: `src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/CanonicalHashProfiles/WorkflowStepCanonicalHashProfile.cs`
- Create: `src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/CanonicalHashProfiles/InteractionTargetCanonicalHashProfile.cs`

**Interfaces:**
- Consumes: CanonicalHashProfileAttribute, CanonicalHashFieldAttribute, CanonicalHashFieldClassification, CanonicalHashCollectionOrderMode, descriptor types
- Produces: 13 profile classes that SG will read

- [ ] **Step 1: Create SchemaDescriptor profile + sub-structures**

```csharp
// SchemaDescriptorCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Schema.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Schema,
    TargetType = typeof(SchemaDescriptor),
    ContractShapeVersion = "schema-contract-hash-v1",
    DefinitionShapeVersion = "schema-definition-hash-v1")]
internal sealed class SchemaDescriptorCanonicalHashProfile
{
    [CanonicalHashField(nameof(SchemaDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(SchemaDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(SchemaDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(SchemaDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(SchemaDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(SchemaDescriptor.ChangeKind), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(SchemaDescriptor.Fields), CanonicalHashFieldClassification.Contract, Order = 6,
        ElementProfile = typeof(SchemaFieldCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = nameof(SchemaFieldDescriptor.Name))]
    [CanonicalHashField(nameof(SchemaDescriptor.References), CanonicalHashFieldClassification.Contract, Order = 7,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder)]
    [CanonicalHashField(nameof(SchemaDescriptor.ValidationRules), CanonicalHashFieldClassification.DefinitionOnly, Order = 100,
        ElementProfile = typeof(SchemaValidationRuleCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder)]
    [CanonicalHashField(nameof(SchemaDescriptor.Namespace), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(SchemaDescriptor.Kind), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(SchemaDescriptor.FullId), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(SchemaDescriptor.ContractHash), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(SchemaDescriptor.DefinitionHash), CanonicalHashFieldClassification.Excluded)]
    private static void Fields() { }
}
```

```csharp
// SchemaFieldCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Schema.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown, // sub-structure, not a top-level descriptor
    TargetType = typeof(SchemaFieldDescriptor),
    ContractShapeVersion = "schema-field-contract-hash-v1",
    DefinitionShapeVersion = "schema-field-definition-hash-v1")]
internal sealed class SchemaFieldCanonicalHashProfile
{
    [CanonicalHashField(nameof(SchemaFieldDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.FieldType), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsRequired), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsNullable), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MaxLength), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MinLength), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MaxValue), CanonicalHashFieldClassification.Contract, Order = 6)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MinValue), CanonicalHashFieldClassification.Contract, Order = 7)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.Pattern), CanonicalHashFieldClassification.Contract, Order = 8)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsCollection), CanonicalHashFieldClassification.Contract, Order = 9)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.CollectionElementType), CanonicalHashFieldClassification.Contract, Order = 10)]
    private static void Fields() { }
}
```

```csharp
// SchemaValidationRuleCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Schema.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(SchemaValidationRule),
    ContractShapeVersion = "schema-validation-rule-contract-hash-v1",
    DefinitionShapeVersion = "schema-validation-rule-definition-hash-v1")]
internal sealed class SchemaValidationRuleCanonicalHashProfile
{
    [CanonicalHashField(nameof(SchemaValidationRule.Name), CanonicalHashFieldClassification.DefinitionOnly, Order = 0)]
    [CanonicalHashField(nameof(SchemaValidationRule.Expression), CanonicalHashFieldClassification.DefinitionOnly, Order = 1)]
    [CanonicalHashField(nameof(SchemaValidationRule.ErrorMessage), CanonicalHashFieldClassification.DefinitionOnly, Order = 2)]
    private static void Fields() { }
}
```

- [ ] **Step 2: Create CapabilityDescriptor profile + EventRef**

```csharp
// CapabilityDescriptorCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Capability,
    TargetType = typeof(CapabilityDescriptor),
    ContractShapeVersion = "capability-contract-hash-v1",
    DefinitionShapeVersion = "capability-definition-hash-v1")]
internal sealed class CapabilityDescriptorCanonicalHashProfile
{
    [CanonicalHashField(nameof(CapabilityDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(CapabilityDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(CapabilityDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(CapabilityDescriptor.CapabilityKind), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(CapabilityDescriptor.InputSchema), CanonicalHashFieldClassification.Contract, Order = 6)]
    [CanonicalHashField(nameof(CapabilityDescriptor.OutputSchema), CanonicalHashFieldClassification.Contract, Order = 7)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Permissions), CanonicalHashFieldClassification.Contract, Order = 8,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]
    [CanonicalHashField(nameof(CapabilityDescriptor.RiskLevel), CanonicalHashFieldClassification.Contract, Order = 9)]
    [CanonicalHashField(nameof(CapabilityDescriptor.SemanticTags), CanonicalHashFieldClassification.Contract, Order = 10,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Categories), CanonicalHashFieldClassification.DefinitionOnly, Order = 100,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Produces), CanonicalHashFieldClassification.DefinitionOnly, Order = 101,
        ElementProfile = typeof(EventRefCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Consumes), CanonicalHashFieldClassification.DefinitionOnly, Order = 102,
        ElementProfile = typeof(EventRefCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Namespace), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(CapabilityDescriptor.Kind), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(CapabilityDescriptor.FullId), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(CapabilityDescriptor.ContractHash), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(CapabilityDescriptor.DefinitionHash), CanonicalHashFieldClassification.Excluded)]
    private static void Fields() { }
}
```

```csharp
// EventRefCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(EventRef),
    ContractShapeVersion = "event-ref-contract-hash-v1",
    DefinitionShapeVersion = "event-ref-definition-hash-v1")]
internal sealed class EventRefCanonicalHashProfile
{
    [CanonicalHashField(nameof(EventRef.Namespace), CanonicalHashFieldClassification.DefinitionOnly, Order = 0)]
    [CanonicalHashField(nameof(EventRef.Id), CanonicalHashFieldClassification.DefinitionOnly, Order = 1)]
    [CanonicalHashField(nameof(EventRef.Version), CanonicalHashFieldClassification.DefinitionOnly, Order = 2)]
    private static void Fields() { }
}
```

- [ ] **Step 3: Create EventDescriptor profile**

```csharp
// EventDescriptorCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Event.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Event,
    TargetType = typeof(EventDescriptor),
    ContractShapeVersion = "event-contract-hash-v1",
    DefinitionShapeVersion = "event-definition-hash-v1")]
internal sealed class EventDescriptorCanonicalHashProfile
{
    [CanonicalHashField(nameof(EventDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(EventDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(EventDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(EventDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(EventDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(EventDescriptor.PayloadSchema), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(EventDescriptor.Category), CanonicalHashFieldClassification.Contract, Order = 6)]
    [CanonicalHashField(nameof(EventDescriptor.Semantic), CanonicalHashFieldClassification.Contract, Order = 7)]
    [CanonicalHashField(nameof(EventDescriptor.Importance), CanonicalHashFieldClassification.Contract, Order = 8)]
    [CanonicalHashField(nameof(EventDescriptor.ChangeKind), CanonicalHashFieldClassification.Contract, Order = 9)]
    [CanonicalHashField(nameof(EventDescriptor.Namespace), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(EventDescriptor.Kind), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(EventDescriptor.FullId), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(EventDescriptor.ContractHash), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(EventDescriptor.DefinitionHash), CanonicalHashFieldClassification.Excluded)]
    private static void Fields() { }
}
```

- [ ] **Step 4: Create FormDescriptor profile + FormField**

```csharp
// FormDescriptorCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Form.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Form,
    TargetType = typeof(FormDescriptor),
    ContractShapeVersion = "form-contract-hash-v1",
    DefinitionShapeVersion = "form-definition-hash-v1")]
internal sealed class FormDescriptorCanonicalHashProfile
{
    [CanonicalHashField(nameof(FormDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(FormDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(FormDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(FormDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(FormDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(FormDescriptor.Schema), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(FormDescriptor.Fields), CanonicalHashFieldClassification.Contract, Order = 6,
        ElementProfile = typeof(FormFieldCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = nameof(FormFieldDescriptor.Order))]
    [CanonicalHashField(nameof(FormDescriptor.LayoutColumns), CanonicalHashFieldClassification.DefinitionOnly, Order = 100)]
    [CanonicalHashField(nameof(FormDescriptor.Namespace), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(FormDescriptor.Kind), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(FormDescriptor.FullId), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(FormDescriptor.ContractHash), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(FormDescriptor.DefinitionHash), CanonicalHashFieldClassification.Excluded)]
    private static void Fields() { }
}
```

```csharp
// FormFieldCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Form.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(FormFieldDescriptor),
    ContractShapeVersion = "form-field-contract-hash-v1",
    DefinitionShapeVersion = "form-field-definition-hash-v1")]
internal sealed class FormFieldCanonicalHashProfile
{
    [CanonicalHashField(nameof(FormFieldDescriptor.SchemaFieldName), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(FormFieldDescriptor.Order), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(FormFieldDescriptor.Group), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(FormFieldDescriptor.IsReadOnly), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(FormFieldDescriptor.ControlType), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(FormFieldDescriptor.IsRequiredOverride), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(FormFieldDescriptor.OptionsSource), CanonicalHashFieldClassification.Contract, Order = 6)]
    [CanonicalHashField(nameof(FormFieldDescriptor.Label), CanonicalHashFieldClassification.DefinitionOnly, Order = 100)]
    [CanonicalHashField(nameof(FormFieldDescriptor.Placeholder), CanonicalHashFieldClassification.DefinitionOnly, Order = 101)]
    [CanonicalHashField(nameof(FormFieldDescriptor.HelpText), CanonicalHashFieldClassification.DefinitionOnly, Order = 102)]
    [CanonicalHashField(nameof(FormFieldDescriptor.FormatHint), CanonicalHashFieldClassification.DefinitionOnly, Order = 103)]
    [CanonicalHashField(nameof(FormFieldDescriptor.VisibilityCondition), CanonicalHashFieldClassification.DefinitionOnly, Order = 104)]
    [CanonicalHashField(nameof(FormFieldDescriptor.ValidationMessage), CanonicalHashFieldClassification.DefinitionOnly, Order = 105)]
    [CanonicalHashField(nameof(FormFieldDescriptor.DefaultValueExpression), CanonicalHashFieldClassification.DefinitionOnly, Order = 106)]
    [CanonicalHashField(nameof(FormFieldDescriptor.Metadata), CanonicalHashFieldClassification.DefinitionOnly, Order = 107,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrderedKeyValue)]
    private static void Fields() { }
}
```

- [ ] **Step 5: Create HumanTaskDescriptor profile + CompletionOutcome**

```csharp
// HumanTaskDescriptorCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.HumanTask,
    TargetType = typeof(HumanTaskDescriptor),
    ContractShapeVersion = "humantask-contract-hash-v1",
    DefinitionShapeVersion = "humantask-definition-hash-v1")]
internal sealed class HumanTaskDescriptorCanonicalHashProfile
{
    [CanonicalHashField(nameof(HumanTaskDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Interaction), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.InputSchema), CanonicalHashFieldClassification.Contract, Order = 6)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.OutputSchema), CanonicalHashFieldClassification.Contract, Order = 7)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.AssigneeStrategy), CanonicalHashFieldClassification.Contract, Order = 8)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Permissions), CanonicalHashFieldClassification.Contract, Order = 9)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Outcomes), CanonicalHashFieldClassification.Contract, Order = 10,
        ElementProfile = typeof(CompletionOutcomeCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Timeout), CanonicalHashFieldClassification.DefinitionOnly, Order = 100)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Namespace), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.Kind), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.FullId), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.ContractHash), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(HumanTaskDescriptor.DefinitionHash), CanonicalHashFieldClassification.Excluded)]
    private static void Fields() { }
}
```

```csharp
// CompletionOutcomeCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CompletionOutcome),
    ContractShapeVersion = "completion-outcome-contract-hash-v1",
    DefinitionShapeVersion = "completion-outcome-definition-hash-v1")]
internal sealed class CompletionOutcomeCanonicalHashProfile
{
    [CanonicalHashField(nameof(CompletionOutcome.Condition), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CompletionOutcome.Capability), CanonicalHashFieldClassification.Contract, Order = 1)]
    private static void Fields() { }
}
```

- [ ] **Step 6: Create WorkflowDescriptor profile + WorkflowStep + InteractionTarget**

```csharp
// WorkflowDescriptorCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Workflow.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Workflow,
    TargetType = typeof(WorkflowDescriptor),
    ContractShapeVersion = "workflow-contract-hash-v1",
    DefinitionShapeVersion = "workflow-definition-hash-v1")]
internal sealed class WorkflowDescriptorCanonicalHashProfile
{
    [CanonicalHashField(nameof(WorkflowDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(WorkflowDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(WorkflowDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(WorkflowDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(WorkflowDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(WorkflowDescriptor.VariableSchema), CanonicalHashFieldClassification.Contract, Order = 5)]
    [CanonicalHashField(nameof(WorkflowDescriptor.Steps), CanonicalHashFieldClassification.Contract, Order = 6,
        ElementProfile = typeof(WorkflowStepCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder)]
    [CanonicalHashField(nameof(WorkflowDescriptor.DefaultVariableScope), CanonicalHashFieldClassification.Contract, Order = 7)]
    [CanonicalHashField(nameof(WorkflowDescriptor.Namespace), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(WorkflowDescriptor.Kind), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(WorkflowDescriptor.FullId), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(WorkflowDescriptor.ContractHash), CanonicalHashFieldClassification.Excluded)]
    [CanonicalHashField(nameof(WorkflowDescriptor.DefinitionHash), CanonicalHashFieldClassification.Excluded)]
    private static void Fields() { }
}
```

```csharp
// WorkflowStepCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Workflow.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(WorkflowStep),
    ContractShapeVersion = "workflow-step-contract-hash-v1",
    DefinitionShapeVersion = "workflow-step-definition-hash-v1")]
internal sealed class WorkflowStepCanonicalHashProfile
{
    [CanonicalHashField(nameof(WorkflowStep.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(WorkflowStep.Target), CanonicalHashFieldClassification.Contract, Order = 1,
        ElementProfile = typeof(InteractionTargetCanonicalHashProfile))]
    [CanonicalHashField(nameof(WorkflowStep.Condition), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(WorkflowStep.Transitions), CanonicalHashFieldClassification.Contract, Order = 3,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder)]
    [CanonicalHashField(nameof(WorkflowStep.OnError), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(WorkflowStep.Name), CanonicalHashFieldClassification.DefinitionOnly, Order = 100)]
    [CanonicalHashField(nameof(WorkflowStep.InputMapping), CanonicalHashFieldClassification.DefinitionOnly, Order = 101)]
    [CanonicalHashField(nameof(WorkflowStep.OutputMapping), CanonicalHashFieldClassification.DefinitionOnly, Order = 102)]
    private static void Fields() { }
}
```

```csharp
// InteractionTargetCanonicalHashProfile.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Workflow.Abstractions.CanonicalHashProfiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(InteractionTarget),
    ContractShapeVersion = "interaction-target-contract-hash-v1",
    DefinitionShapeVersion = "interaction-target-definition-hash-v1")]
internal sealed class InteractionTargetCanonicalHashProfile
{
    // InteractionTarget is abstract with 3 subtypes.
    // SG must handle this as a discriminated union.
    // For v1, the profile declares the common shape (none) + per-subtype fields.
    // This needs special handling in the SG — see Task 4 design notes.
    private static void Fields() { }
}
```

**Note on InteractionTarget**: This is an abstract record with 3 concrete subtypes (CapabilityTarget, HumanTaskTarget, SubWorkflowTarget). The SG needs special handling for discriminated unions. The profile may need a `[CanonicalHashDiscriminator]` attribute or the SG must detect the inheritance hierarchy. This will be refined in Task 4 when implementing the SG. For now, the profile exists as a placeholder.

- [ ] **Step 7: Build to verify profiles compile**

Run: `dotnet build`

Expected: All profile classes compile. No SG processing yet — that's Task 4.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat(canonical-hash): add Profile classes for all 6 descriptor types and sub-structures"
```

---

### Task 4: Implement CanonicalHashSourceGenerator — Core Infrastructure

**Files:**
- Create: `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/CanonicalHashSourceGenerator.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/ProfileModel.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/DiagnosticDescriptors.cs`
- Modify: `src/Tooling/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj` — add reference to Metadata.Abstractions for attribute types (or use string constants since SG targets netstandard2.0)
- Test: `tests/Tooling/CrestCreates.CodeGenerator.Tests/` (new test project or add to existing)

**Interfaces:**
- Consumes: Profile attribute classes, descriptor types (via Roslyn symbols)
- Produces: ProfileModel (intermediate representation), DiagnosticDescriptors

**Critical constraint**: The SG targets `netstandard2.0`. It cannot reference `CrestCreates.Metadata.Abstractions` directly (which targets `net10.0`). All attribute types and enum values must be matched by full qualified name string, not by type reference.

- [ ] **Step 1: Create DiagnosticDescriptors.cs — CCHASH001-014**

```csharp
// src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/DiagnosticDescriptors.cs
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.CanonicalHashGenerator;

internal static class CanonicalHashDiagnostics
{
    private const string Category = "CanonicalHash";

    public static readonly DiagnosticDescriptor CCHASH001 = new(
        "CCHASH001",
        "Descriptor property not classified",
        "Descriptor public property '{0}' on '{1}' is not classified by any CanonicalHashProfile",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor CCHASH002 = new(
        "CCHASH002",
        "Property reference does not exist",
        "CanonicalHashField references property '{0}' that does not exist on '{1}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH003 = new(
        "CCHASH003",
        "Collection field requires ordering rule",
        "Collection field '{0}' requires explicit CollectionOrderMode",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH004 = new(
        "CCHASH004",
        "Nested complex field requires ElementProfile or ValueProfile",
        "Nested complex field '{0}' requires ElementProfile or ValueProfile",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH005 = new(
        "CCHASH005",
        "Contract payload cannot include DefinitionOnly fields",
        "Contract payload cannot include DefinitionOnly fields (internal consistency check)",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH006 = new(
        "CCHASH006",
        "Excluded field appears in generated payload",
        "Excluded field '{0}' appears in generated payload",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH007 = new(
        "CCHASH007",
        "Profile required fields missing",
        "CanonicalHashProfile on '{0}' requires TargetType and ContractShapeVersion/DefinitionShapeVersion",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH008 = new(
        "CCHASH008",
        "Duplicate hash field order",
        "Duplicate hash field order {0} in profile '{1}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH009 = new(
        "CCHASH009",
        "TargetType does not match DescriptorKind",
        "Profile TargetType '{0}' does not match DescriptorKind '{1}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH010 = new(
        "CCHASH010",
        "ArtifactKind not supported by SG v1",
        "ArtifactKind '{0}' is reserved but not supported by SG v1",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor CCHASH011 = new(
        "CCHASH011",
        "OrdinalByProperty requires OrderByProperty",
        "CollectionOrderMode.OrdinalByProperty requires OrderByProperty on field '{0}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH012 = new(
        "CCHASH012",
        "OrderedKeyValue requires dictionary-like field",
        "CollectionOrderMode.OrderedKeyValue can only be used on dictionary-like fields, not on '{0}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH013 = new(
        "CCHASH013",
        "ElementProfile type mismatch",
        "ElementProfile target type does not match collection element type for field '{0}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CCHASH014 = new(
        "CCHASH014",
        "Multiple field declaration blocks",
        "Profile class '{0}' must contain exactly one method with CanonicalHashField attributes; found {1} methods",
        Category,
        DiagnosticSeverity.Error,
        true);
}
```

- [ ] **Step 2: Create ProfileModel.cs — intermediate representation**

The ProfileModel captures all information the SG extracts from the Profile attributes, without depending on any CrestCreates types. It uses primitive types only.

```csharp
// src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/ProfileModel.cs
namespace CrestCreates.CodeGenerator.CanonicalHashGenerator;

internal sealed class ProfileFieldModel
{
    public string PropertyName { get; init; } = "";
    public int Classification { get; init; } // 1=Contract, 2=DefinitionOnly, 3=Excluded
    public int Order { get; init; }
    public string? ElementProfileTypeName { get; init; }
    public int CollectionOrderMode { get; init; } // 0=None, 1=SourceOrder, 2=OrdinalByValue, 3=OrdinalByProperty, 4=OrderedKeyValue
    public string? OrderByProperty { get; init; }
    public string? ValueProfileTypeName { get; init; }
}

internal sealed class ProfileModel
{
    public string ProfileClassName { get; init; } = "";
    public string ProfileNamespace { get; init; } = "";
    public int ArtifactKind { get; init; } // 1=Descriptor, 2=ReviewResult, 3=Package, 4=Report
    public int DescriptorKind { get; init; } // 0=Unknown, 1=Schema, etc.
    public string TargetTypeFullName { get; init; } = "";
    public string TargetTypeName { get; init; } = "";
    public string ContractShapeVersion { get; init; } = "";
    public string DefinitionShapeVersion { get; init; } = "";
    public List<ProfileFieldModel> Fields { get; init; } = new();
    public string TargetTypeNamespace { get; init; } = "";
    public bool IsSubStructure => DescriptorKind == 0; // Unknown = sub-structure
}
```

- [ ] **Step 3: Create CanonicalHashSourceGenerator.cs — main entry with SyntaxProviders**

The generator uses two SyntaxProviders:
1. `ForAttributeWithMetadataName` targeting `CanonicalHashProfileAttribute` — finds profile classes
2. Within each profile class, find the method with `CanonicalHashFieldAttribute` and extract all field declarations

Since the SG targets netstandard2.0, it must use string-based attribute matching rather than type references.

```csharp
// Pseudocode for the main generator structure
[Generator]
public sealed class CanonicalHashSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. SyntaxProvider: find all classes with [CanonicalHashProfile] attribute
        var profiles = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "CrestCreates.Metadata.Abstractions.CanonicalHashProfileAttribute",
                IsProfileClassCandidate,
                TransformProfile)
            .Where(p => p != null)
            .Collect();

        // 2. Generate outputs
        context.RegisterSourceOutput(profiles, GenerateAll);
    }

    private static bool IsProfileClassCandidate(SyntaxNode node, CancellationToken ct)
        => node is ClassDeclarationSyntax;

    private static ProfileModel? TransformProfile(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        // Extract CanonicalHashProfile attribute data
        // Find method with CanonicalHashField attributes
        // Build ProfileModel
        // Validate and report diagnostics
        // Return null if critical errors
    }

    private static void GenerateAll(SourceProductionContext context, ImmutableArray<ProfileModel> profiles)
    {
        // 1. Validate all profiles (cross-profile checks)
        // 2. Generate payload records
        // 3. Generate projection methods
        // 4. Generate dispatcher
        // 5. Generate JsonContext
    }
}
```

- [ ] **Step 4: Implement TransformProfile — extract profile data from attributes**

This is the core extraction logic. For each profile class:
1. Read `[CanonicalHashProfile]` attribute: ArtifactKind, DescriptorKind, TargetType, ContractShapeVersion, DefinitionShapeVersion
2. Find the method with `[CanonicalHashField]` attributes (validate exactly one — CCHASH014)
3. For each `[CanonicalHashField]`: PropertyName, Classification, Order, ElementProfile, CollectionOrderMode, OrderByProperty, ValueProfile
4. Resolve TargetType symbol to get property types for later type mapping
5. Validate: CCHASH002 (property exists), CCHASH007 (required fields), CCHASH008 (duplicate order)

- [ ] **Step 5: Build to verify SG compiles**

Run: `dotnet build src/Tooling/CrestCreates.CodeGenerator/`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(canonical-hash): add CanonicalHashSourceGenerator core — model, diagnostics, syntax providers"
```

---

### Task 5: Implement SG — Payload, Projection, Dispatcher, JsonContext Writers

**Files:**
- Create: `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/PayloadWriter.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/ProjectionWriter.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/DispatcherWriter.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/JsonContextWriter.cs`
- Modify: `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/CanonicalHashSourceGenerator.cs` — wire up writers

**Interfaces:**
- Consumes: ProfileModel from Task 4
- Produces: Generated source files for payloads, projections, dispatcher, JsonContext

- [ ] **Step 1: Implement PayloadWriter — generate Contract/Definition hash payload records**

For each profile, generate two `internal sealed record` types:
- `{ProfileNameWithoutSuffix}ContractHashPayload` — only Contract fields
- `{ProfileNameWithoutSuffix}DefinitionHashPayload` — Contract + DefinitionOnly fields

Each field gets `[JsonPropertyOrder]` with renumbered sequential values. Type mapping:
- Primitive (string, int, bool, double, enum) → same type
- Nullable primitive → same nullable type
- `IReadOnlyList<T>` → `IReadOnlyList<TPayload>` where TPayload is the ElementProfile's payload type
- `IReadOnlyDictionary<string, T>` → `IReadOnlyList<CanonicalStringKeyValuePayload<TPayload>>`
- `VersionedDescriptorRef<T>` → `VersionedDescriptorRefPayload` (shared struct with Id, Version, SelectionMode, ExpectedContractHash)
- Complex sub-structure → ElementProfile's payload type
- Abstract type (InteractionTarget) → generate per-subtype discriminated payload

- [ ] **Step 2: Implement ProjectionWriter — generate typed projection methods**

For each profile, generate `internal static class {Name}CanonicalHashProjection` with:
- `ToContractPayload(TargetType source)` — extracts Contract fields, applies collection ordering, recurses into sub-projections
- `ToDefinitionPayload(TargetType source)` — extracts Contract + DefinitionOnly fields

- [ ] **Step 3: Implement DispatcherWriter — generate CanonicalHashProjectionDispatcher**

```csharp
internal static class CanonicalHashProjectionDispatcher
{
    public static CanonicalHashProjectionResult ToContractEnvelope(
        IDescriptor descriptor, CanonicalHashScope scope, string contractVersion, string algorithmVersion)
    {
        return descriptor switch
        {
            SchemaDescriptor d => CanonicalHashProjectionResult.Create(
                new CanonicalHashEnvelope<SchemaContractHashPayload> { ... },
                MetadataCanonicalHashJsonContext.Default.CanonicalHashEnvelopeSchemaContractHashPayload),
            // ... per kind
        };
    }

    public static CanonicalHashProjectionResult ToDefinitionEnvelope(
        IDescriptor descriptor, CanonicalHashScope scope, string contractVersion, string algorithmVersion)
    { ... }
}
```

- [ ] **Step 4: Implement JsonContextWriter — generate MetadataCanonicalHashJsonContext**

```csharp
[JsonSourceGenerationOptions(PropertyNamingPolicy = null, DefaultIgnoreCondition = JsonIgnoreCondition.Never, WriteIndented = false)]
[JsonSerializable(typeof(CanonicalHashEnvelope<SchemaContractHashPayload>))]
[JsonSerializable(typeof(CanonicalHashEnvelope<SchemaDefinitionHashPayload>))]
[JsonSerializable(typeof(SchemaContractHashPayload))]
[JsonSerializable(typeof(SchemaDefinitionHashPayload))]
// ... per kind
[JsonSerializable(typeof(CanonicalStringKeyValuePayload))]
[JsonSerializable(typeof(CanonicalStringKeyValuePayload<string>))]
// ... per closed generic
internal partial class MetadataCanonicalHashJsonContext : JsonSerializerContext { }
```

- [ ] **Step 5: Wire up all writers in CanonicalHashSourceGenerator.GenerateAll**

- [ ] **Step 6: Register SG in Directory.Build.Aot.props**

Add the CanonicalHashSourceGenerator to the global analyzer injection alongside existing generators.

- [ ] **Step 7: Build full solution and verify SG output**

Run: `dotnet build`

Inspect generated files in `obj/{config}/{tfm}/source-generators/` to verify correct output.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat(canonical-hash): implement SG payload/projection/dispatcher/JsonContext writers"
```

---

### Task 6: Implement DefaultCanonicalHashComputer + DescriptorStableHashBuilder Adapter

**Files:**
- Create: `src/Metadata/CrestCreates.Metadata/DefaultCanonicalHashComputer.cs`
- Create: `src/Metadata/CrestCreates.Metadata/ContractVersions.cs`
- Modify: `src/Metadata/CrestCreates.Metadata/DescriptorStableHashBuilder.cs` — degrade to adapter
- Modify: `src/Metadata/CrestCreates.Metadata/CrestCreatesMetadataJsonContext.cs` — may need updates
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/`

**Interfaces:**
- Consumes: ICanonicalHashComputer, CanonicalHashProjectionDispatcher (from SG), CanonicalHashProjectionResult
- Produces: Working hash computation pipeline

- [ ] **Step 1: Create ContractVersions.cs**

```csharp
// src/Metadata/CrestCreates.Metadata/ContractVersions.cs
namespace CrestCreates.Metadata;

internal static class ContractVersions
{
    public const string DescriptorHash = "canonical-hash-v1";
}
```

- [ ] **Step 2: Create DefaultCanonicalHashComputer.cs**

```csharp
// src/Metadata/CrestCreates.Metadata/DefaultCanonicalHashComputer.cs
using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing.Generated;

namespace CrestCreates.Metadata;

public sealed class DefaultCanonicalHashComputer : ICanonicalHashComputer
{
    public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
    {
        var projection = CanonicalHashProjectionDispatcher.ToContractEnvelope(
            descriptor, scope, ContractVersions.DescriptorHash, CanonicalHashAlgorithms.AlgorithmVersion);
        return ComputeFromProjection(projection);
    }

    public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
    {
        var projection = CanonicalHashProjectionDispatcher.ToDefinitionEnvelope(
            descriptor, scope, ContractVersions.DescriptorHash, CanonicalHashAlgorithms.AlgorithmVersion);
        return ComputeFromProjection(projection);
    }

    public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var json = JsonSerializer.SerializeToUtf8Bytes(projection.Envelope, projection.EnvelopeJsonTypeInfo);
        var hashBytes = SHA256.HashData(json);
        var hashValue = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return new CanonicalHash
        {
            Value = hashValue,
            Algorithm = CanonicalHashAlgorithms.Sha256,
            AlgorithmVersion = projection.AlgorithmVersion,
            ArtifactKind = projection.ArtifactKind,
            DescriptorKind = projection.DescriptorKind,
            Scope = projection.Scope,
            Purpose = projection.Purpose,
            ContractVersion = projection.ContractVersion,
            CanonicalShapeVersion = projection.CanonicalShapeVersion
        };
    }
}
```

- [ ] **Step 3: Rewrite DescriptorStableHashBuilder as adapter**

```csharp
// src/Metadata/CrestCreates.Metadata/DescriptorStableHashBuilder.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DescriptorStableHashBuilder : IDescriptorStableHashBuilder
{
    private readonly ICanonicalHashComputer _hashComputer;

    public DescriptorStableHashBuilder(ICanonicalHashComputer hashComputer)
    {
        _hashComputer = hashComputer;
    }

    public DescriptorStableHashes Build(IDescriptor descriptor)
    {
        return new DescriptorStableHashes
        {
            ContractHash = _hashComputer.ComputeContractHash(descriptor, CanonicalHashScope.InternalFull),
            DefinitionHash = _hashComputer.ComputeDefinitionHash(descriptor, CanonicalHashScope.InternalFull)
        };
    }
}
```

Delete all old pipe-delimited logic (AppendField, Esc, NullSentinel, ComputeSha256, etc.).

- [ ] **Step 4: Update DI registration**

Add `ICanonicalHashComputer` registration before `IDescriptorStableHashBuilder`:

```csharp
services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
services.AddSingleton<IDescriptorStableHashBuilder, DescriptorStableHashBuilder>();
```

- [ ] **Step 5: Build and run tests**

Run: `dotnet build && dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests/`

Expected: Build succeeds. Many test hash values will have changed — update assertions.

- [ ] **Step 6: Update all test hash value assertions**

Run tests, collect failures, update expected hash values. The hash format changed from pipe-delimited to canonical JSON, so all hash values will be different.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(canonical-hash): implement DefaultCanonicalHashComputer, DescriptorStableHashBuilder adapter"
```

---

### Task 7: Remove IDescriptor.ContractHash/DefinitionHash + Update All Consumers

**Files:**
- Modify: All 6 descriptor types (remove ContractHash/DefinitionHash properties)
- Modify: All consumers that access descriptor.ContractHash / descriptor.DefinitionHash
- Modify: `IDescriptor` interface (remove properties)
- Modify: `IHasContractIdentity` interface (remove or delete)
- Test: All affected test files

**Interfaces:**
- Consumes: ICanonicalHashComputer, IDescriptorStableHashBuilder
- Produces: Clean descriptor types without hash output properties

- [ ] **Step 1: Search for all usages of descriptor.ContractHash and descriptor.DefinitionHash**

```bash
rg "\.ContractHash" --include="*.cs" | grep -v "obj/" | grep -v "bin/"
rg "\.DefinitionHash" --include="*.cs" | grep -v "obj/" | grep -v "bin/"
```

- [ ] **Step 2: For each usage site, replace with ICanonicalHashComputer or IDescriptorStableHashBuilder call**

Replace:
```csharp
descriptor.ContractHash  →  _hashComputer.ComputeContractHash(descriptor, scope).Value
descriptor.DefinitionHash  →  _hashComputer.ComputeDefinitionHash(descriptor, scope).Value
```

Or use `IDescriptorStableHashBuilder.Build(descriptor)` if both hashes are needed.

- [ ] **Step 3: Remove ContractHash/DefinitionHash from IDescriptor and IHasContractIdentity**

- [ ] **Step 4: Remove properties from all 6 descriptor implementations**

- [ ] **Step 5: Remove IHasContractIdentity interface (move to RecycleBin)**

- [ ] **Step 6: Build and fix all remaining errors**

Run: `dotnet build`

- [ ] **Step 7: Run all tests**

Run: `dotnet test`

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat(canonical-hash): remove IDescriptor.ContractHash/DefinitionHash, update all consumers"
```

---

### Task 8: Migrate DescriptorPackageHashComputer

**Files:**
- Create: `src/Metadata/CrestCreates.Metadata/CanonicalHashing/DescriptorPackageCanonicalHashPayload.cs`
- Create: `src/Metadata/CrestCreates.Metadata/CanonicalHashing/DescriptorPackageCanonicalHashProjection.cs`
- Modify: `src/Metadata/CrestCreates.Metadata/DescriptorPackageHashComputer.cs` — degrade to adapter or delete → RecycleBin
- Modify: `src/Metadata/CrestCreates.Metadata/CrestCreatesMetadataJsonContext.cs` — register package payload types
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/`

**Interfaces:**
- Consumes: ICanonicalHashComputer.ComputeFromProjection, CanonicalHashProjectionResult.Create
- Produces: Canonical package hashes via unified pipeline

- [ ] **Step 1: Create DescriptorPackageCanonicalHashPayload**

Define the canonical DTO for package hashing. ContentHash → Purpose=Integrity, EvidenceHash → Purpose=AuditEvidence, EnvelopeHash → Purpose=Integrity.

- [ ] **Step 2: Create DescriptorPackageCanonicalHashProjection**

Hand-written projection that creates envelopes with proper Purpose/Scope metadata.

- [ ] **Step 3: Update DescriptorPackageHashComputer to use ComputeFromProjection**

- [ ] **Step 4: Register package payload types in MetadataCanonicalHashJsonContext (hand-written partial)**

- [ ] **Step 5: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(canonical-hash): migrate DescriptorPackageHashComputer to unified pipeline"
```

---

### Task 9: Migrate SourceReviewHash + ReportId

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/CanonicalHashing/ReviewResultCanonicalHashPayload.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/CanonicalHashing/ReviewResultCanonicalHashProjection.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/CanonicalHashing/ReportSourceBindingHashPayload.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/CanonicalHashing/ReportSourceBindingHashProjection.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/CanonicalHashing/AgentControlPlaneCanonicalHashJsonContext.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewReportBuilder.cs` — use ComputeFromProjection
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/`

**Interfaces:**
- Consumes: ICanonicalHashComputer.ComputeFromProjection, CanonicalHashProjectionResult.Create
- Produces: SourceReviewHash (TenantVisible SourceBinding), ReportId via unified pipeline

- [ ] **Step 1: Create ReviewResultCanonicalHashPayload**

Define canonical DTO with all fields that participate in the SourceReviewHash. Purpose=SourceBinding.

- [ ] **Step 2: Create ReviewResultCanonicalHashProjection**

Hand-written projection. SourceReviewHash is TenantVisible SourceBinding hash.

- [ ] **Step 3: Create ReportSourceBindingHashPayload**

Fields: TenantId, DraftId, DraftVersion, SourceReviewHash, ContractVersion, TemplateVersion. No GeneratedAt.

- [ ] **Step 4: Create ReportSourceBindingHashProjection**

- [ ] **Step 5: Create AgentControlPlaneCanonicalHashJsonContext**

Register ReviewResult and Report payloads + their envelopes.

- [ ] **Step 6: Update DefaultDescriptorReviewReportBuilder**

Replace old `ComputeSourceReviewHash` and ReportId calculation with `ICanonicalHashComputer.ComputeFromProjection`.

- [ ] **Step 7: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat(canonical-hash): migrate SourceReviewHash and ReportId to unified pipeline"
```

---

### Task 10: Update Visibility Projectors

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDraftArtifactVisibilityProjector.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Projections/AgentReviewResultDtoProjection.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/`

**Interfaces:**
- Consumes: ICanonicalHashComputer with TenantVisible scope
- Produces: TenantVisible hashes computed from projected artifacts

- [ ] **Step 1: Update AgentDraftArtifactVisibilityProjector**

When computing TenantVisible StableHashes:
- First project the descriptor to TenantVisible form (strip denied descriptor kinds)
- Then compute hash with `CanonicalHashScope.TenantVisible` on the projected artifact
- Do NOT reuse InternalFull hashes with scope substitution

- [ ] **Step 2: Update AgentReviewResultDtoProjection**

Ensure TenantVisible scope hashes are used in DTOs, not InternalFull hashes.

- [ ] **Step 3: Verify InternalFull hashes are not leaked to TenantVisible consumers**

Audit all DTO projections that carry hash values.

- [ ] **Step 4: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(canonical-hash): update visibility projectors for TenantVisible hash computation"
```

---

### Task 11: Comprehensive Test Coverage

**Files:**
- Modify: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorStableHashBuilderTests.cs`
- Modify: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorStableHashCoverageTests.cs`
- Create: `tests/Tooling/CrestCreates.CodeGenerator.Tests/CanonicalHashSourceGeneratorTests.cs`
- Create: `tests/Metadata/Core/CrestCreates.Metadata.Tests/CanonicalHashDeterminismTests.cs`
- Create: `tests/Metadata/Core/CrestCreates.Metadata.Tests/CanonicalHashPolicyTests.cs`

**Interfaces:**
- Consumes: All canonical hash infrastructure
- Produces: Test coverage for determinism, policy, SG diagnostics, visibility

- [ ] **Step 1: Write determinism tests**

- same canonical input → same hash
- dictionary order doesn't affect hash
- unordered collection order doesn't affect hash (e.g., Schema.Fields sorted by Name)
- SourceOrder collection reorder must affect hash (e.g., Workflow.Steps)

- [ ] **Step 2: Write policy tests**

- Schema optional field addition → DefinitionHash only, not ContractHash
- Schema required field removal → ContractHash changes
- Workflow step reorder → ContractHash changes (SourceOrder)
- Capability permission change → ContractHash changes
- Form label change → DefinitionHash only
- Event payload schema change → ContractHash changes

- [ ] **Step 3: Write SG diagnostic tests**

- CCHASH001: unclassified property → Warning
- CCHASH002: nonexistent property → Error
- CCHASH003: collection without ordering → Error
- CCHASH004: complex without ElementProfile → Error
- CCHASH007: missing required fields → Error
- CCHASH008: duplicate order → Error
- CCHASH011: OrdinalByProperty without OrderByProperty → Error
- CCHASH014: multiple field blocks → Error

- [ ] **Step 4: Write SourceBinding tests**

- VisibleReviewHash stable (same projected review)
- Diagnostic change → hash changes
- Package hash change → hash changes
- ReportId excludes GeneratedAt
- ReportId changes when SourceReviewHash/TemplateVersion changes
- SourceReviewHash is TenantVisible SourceBinding hash

- [ ] **Step 5: Write visibility tests**

- Denied descriptor kind doesn't affect TenantVisible hash
- Denied descriptor kind affects InternalFull hash
- TenantVisible hash doesn't encode denied descriptor count
- TenantVisible hash computed from projected artifact, not scope substitution

- [ ] **Step 6: Update DescriptorStableHashCoverageTests**

Replace old policy-based coverage with Profile-based coverage. Verify every public property of every descriptor type is classified in its Profile.

- [ ] **Step 7: Run all tests**

Run: `dotnet test`

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "test(canonical-hash): comprehensive test coverage — determinism, policy, SG diagnostics, visibility"
```

---

### Task 12: Final Cleanup + Spec Compliance Verification

**Files:**
- Move to RecycleBin: any remaining old hash implementation files
- Modify: `memory.md` — update platform status
- Verify: all spec requirements are met

- [ ] **Step 1: Verify all old ComputeSha256 implementations are removed**

Search for any remaining `ComputeSha256`, `AppendField`, `NullSentinel`, `Esc(` patterns.

- [ ] **Step 2: Verify all spec requirements are implemented**

Check each requirement from the spec against the codebase.

- [ ] **Step 3: Run full solution build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 4: Run dependency boundary tests**

Run: `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/`

- [ ] **Step 5: Update memory.md**

- [ ] **Step 6: Final commit**

```bash
git add -A && git commit -m "feat(canonical-hash): final cleanup and spec compliance verification"
```
