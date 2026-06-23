# Canonical Hash Runtime Foundation — Design Spec

**Issue**: #30
**Date**: 2026-06-23
**Status**: Approved
**Branch**: `feature/canonical-hash-runtime-30`

---

## 1. Goal

Introduce a unified Canonical Hash Runtime for descriptor and control-plane artifacts. Replace 4 independent ad hoc hash systems with one deterministic, versioned, AoT-friendly hash runtime driven by a Source Generator for descriptor types.

## 2. Current State — 4 Independent Hash Systems

| # | System | Location | Output | Versioning |
|---|--------|----------|--------|------------|
| 1 | `DescriptorStableHashBuilder` | `CrestCreates.Metadata` | ContractHash, DefinitionHash, RuntimeHash?, BindingHash? | None |
| 2 | `DescriptorPackageHashComputer` | `CrestCreates.Metadata` | ContentHash, EvidenceHash, EnvelopeHash | FormatVersion = "1.0" |
| 3 | `SourceReviewHash` | `Agent.ControlPlane/ReportBuilder` | SourceReviewHash (SHA-256) | None |
| 4 | `ReportId` | Same as #3 | ReportId (SHA-256) | ContractVersion = "7d.v1" |

Shared infrastructure: None. SHA-256 computation copy-pasted in 4+ locations. All use hand-coded pipe-delimited string concatenation with inconsistent escaping.

## 3. Design Decisions

### 3.1 Source Generator Boundary

The source generator owns canonical hash **shape generation**, not hash execution.

**SG generates:**
- Canonical DTO types per descriptor/artifact profile
- Deterministic projection code from descriptor to canonical DTO
- Per-profile canonical JSON writer methods using `Utf8JsonWriter` (deterministic output, no reflection, AOT-safe)

**SG must not generate:**
- SHA-256 computation
- Ad hoc pipe-delimited serialization
- Visibility / authorization filtering
- Runtime reflection-based dispatch

**Runtime hash computer owns:**
- Canonical metadata construction (`CanonicalHashMetadata`)
- Algorithm / hash version metadata
- Serialization through SG-generated `WriteCanonicalJson` delegate via `Utf8JsonWriter`
- SHA-256 computation via `ArrayBufferWriter<byte>` + `Utf8JsonWriter` + `SHA256.HashData(buffer)`
- `CanonicalHash` result construction

### 3.2 Classification Source: Profile Classes

Canonical hash field classification is owned by explicit profile classes, not by the source generator.

- **Primary**: Profile classes with `[CanonicalHashProfile]` + `[CanonicalHashField]` attributes
- **Supplementary**: Per-property `[CanonicalHashExcluded]` annotation as optional local hint
- **Prohibited**: SG internal hard-coded classification table as long-term solution
- **Rule**: Profile explicit declaration > Per-property annotation > SG diagnostic

The generator must emit diagnostics when descriptor public properties are not explicitly classified by a profile or approved exclusion rule.

### 3.3 SG Scope

- **SG v1**: Descriptor hashes only (6 descriptor kinds + sub-structures)
- **Runtime v1**: All current hash systems (descriptor, package, review result, report ID)
- **Future SG**: Can expand to ReviewResult / Package / Evidence / Activation without redesigning the generator
- **Attribute model**: Reserves `ArtifactKind` dimension; SG v1 reports CCHASH010 for non-Descriptor kinds

### 3.4 Canonical DTO Shape: Per-kind Payload + CanonicalHashMetadata

SG generates per-kind strong-typed payload DTOs. Runtime provides `CanonicalHashMetadata` (7 metadata fields: ArtifactKind, DescriptorKind, Purpose, Scope, AlgorithmVersion, ContractVersion, CanonicalShapeVersion). Metadata participates in hash input via the writer delegate (written as envelope properties) for domain separation.

### 3.5 Contract/Definition Separation: Dual Payload

Each descriptor kind generates two payloads:
- **ContractHashPayload**: Only `Classification = Contract` fields
- **DefinitionHashPayload**: `Contract + DefinitionOnly` fields

Contract payload must not contain DefinitionOnly fields, even as null. Each payload has its own `CanonicalShapeVersion`. Profile declares field classification once; SG auto-derives both payloads.

### 3.6 Nested Sub-structures: Recursive Payload Generation

SG recursively generates payloads for sub-structures referenced via `ElementProfile` / `ValueProfile`. Parent and child structures both generate dual payloads. Collections require explicit ordering rules. Dictionaries canonicalize to ordered key-value payload lists. SG does not implicitly hash all public properties of sub-structures.

### 3.7 Purpose Semantics

`CanonicalHashPurpose` explicitly distinguishes Contract and Definition:

| Purpose | Use Case | Timestamp Rule |
|---------|----------|----------------|
| Contract | Descriptor ContractHash | No generated timestamps |
| Definition | Descriptor DefinitionHash | No generated timestamps |
| SourceBinding | ReviewResult SourceReviewHash, ReportId | No generated timestamps |
| Integrity | Package EnvelopeHash | May include creation metadata |
| AuditEvidence | Audit trail | May include timestamps |

### 3.8 Scope Semantics

Scope is domain-separation metadata. It does not authorize or filter input. Visibility projection must happen before calling the hash computer.

- `InternalFull`: All fields, no filtering — internal storage & governance
- `TenantVisible`: After denied-kind filtering — agent/user-facing
- `PublicCrossTenant`: Identity fields only — cross-tenant dedup (**reserved in v1; no PublicCrossTenant projection is required in this issue**)

TenantVisible hash must be computed from TenantVisible-projected artifacts. It must not be computed from InternalFull artifacts by merely changing the scope parameter.

---

## 4. Profile Declaration Model

### 4.1 Attributes

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CanonicalHashProfileAttribute : Attribute
{
    public CanonicalHashArtifactKind ArtifactKind { get; init; }
    public DescriptorKind DescriptorKind { get; init; } = DescriptorKind.Unknown; // must be explicitly set when ArtifactKind=Descriptor; all runtime switches must reject Unknown
    public Type TargetType { get; init; } = null!;
    public string ContractShapeVersion { get; init; } = string.Empty;
    public string DefinitionShapeVersion { get; init; } = string.Empty;
}

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
    public Type? CustomWriter { get; init; }
}
```

### 4.2 Enums

```csharp
public enum CanonicalHashArtifactKind
{
    Descriptor = 1,
    ReviewResult = 2,   // SG v1: reserved, CCHASH010
    Package = 3,        // SG v1: reserved
    Report = 4          // SG v1: reserved
}

public enum CanonicalHashFieldClassification
{
    Contract = 1,
    DefinitionOnly = 2,
    Excluded = 3
}

public enum CanonicalHashCollectionOrderMode
{
    None = 0,              // Error if collection + None → CCHASH003
    SourceOrder = 1,       // Preserve runtime order (ordered collections only)
    OrdinalByValue = 2,    // Sort by value (string ordinal)
    OrdinalByProperty = 3, // Sort by a specific property of elements
    OrderedKeyValue = 4    // Dictionaries: canonicalize to ordered KV list
}

public enum CanonicalHashPurpose
{
    Contract = 1,
    Definition = 2,
    SourceBinding = 3,
    Integrity = 4,
    AuditEvidence = 5
}

public enum CanonicalHashScope
{
    InternalFull = 1,
    TenantVisible = 2,
    PublicCrossTenant = 3
}
```

### 4.3 Profile Class Example

```csharp
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Schema,
    TargetType = typeof(SchemaDescriptor),
    ContractShapeVersion = "schema-contract-hash-v1",
    DefinitionShapeVersion = "schema-definition-hash-v1")]
internal sealed class SchemaDescriptorCanonicalHashProfile
{
    [CanonicalHashField(nameof(SchemaDescriptor.Id), Contract, Order = 0)]
    [CanonicalHashField(nameof(SchemaDescriptor.Version), Contract, Order = 1)]
    [CanonicalHashField(
        nameof(SchemaDescriptor.Fields), Contract, Order = 2,
        ElementProfile = typeof(SchemaFieldCanonicalHashProfile),
        CollectionOrderMode = OrdinalByProperty,
        OrderByProperty = nameof(SchemaFieldDescriptor.Name))]
    [CanonicalHashField(nameof(SchemaDescriptor.DisplayName), DefinitionOnly, Order = 100)]
    [CanonicalHashField(nameof(SchemaDescriptor.Description), DefinitionOnly, Order = 101)]
    [CanonicalHashField(nameof(SchemaDescriptor.CreatedAt), Excluded)]
    [CanonicalHashField(nameof(SchemaDescriptor.UpdatedAt), Excluded)]
    private static void Fields() { }
}
```

### 4.4 Profile Rules

1. **Single classification declaration**: Each field declared once. SG auto-derives Contract and Definition payloads.
2. **Profile hosts SG-generated writer methods**: `CanonicalHashWriterWriter` generates static `WriteContractEnvelope`/`WriteDefinitionEnvelope`/`WriteContractPayload`/`WriteDefinitionPayload` methods on the profile class. No interface, no DI, not manually instantiated at runtime.
3. **`Fields()` is `private static void`**: SG reads attributes only, does not generate implementation.
4. **Order determines JSON property order**: SG re-numbers to continuous `[JsonPropertyOrder]` per payload.
5. **Unclassified public readable properties**: Must be explicitly classified or matched by global exclusion rule. Otherwise SG reports CCHASH001.
6. **`ArtifactKind != Descriptor`**: `DescriptorKind` must be `Unknown`. SG v1 reports CCHASH010 for non-Descriptor kinds.
7. **Single field declaration block**: A profile class must contain exactly one method carrying `[CanonicalHashField]` attributes. The generator merges no runtime method bodies and does not support separate ContractFields/DefinitionFields declarations. Field classification is declared once and used to derive both payloads. SG reports CCHASH014 if multiple field-block methods exist.

**Note on `DescriptorKind.Unknown`**: The current `DescriptorKind` enum starts at `Schema = 0`. This spec requires adding `Unknown = 0` before `Schema = 1` (renumbering existing members). All runtime switch expressions on `DescriptorKind` must explicitly reject `Unknown` with a default/throw branch. This is a breaking change for any code that casts `0` to `DescriptorKind` expecting `Schema`.

---

## 5. SG Generated Artifacts

### 5.1 Per-kind Dual Payload

Namespace: `CrestCreates.Metadata.CanonicalHashing.Generated`
Type modifier: `internal sealed record`

Naming rule: `{ProfileClassNameWithout"CanonicalHashProfile"}ContractHashPayload` / `DefinitionHashPayload`

```csharp
internal sealed record SchemaContractHashPayload
{
    [JsonPropertyOrder(0)] public required string Id { get; init; }
    [JsonPropertyOrder(1)] public required string Version { get; init; }
    [JsonPropertyOrder(2)] public required IReadOnlyList<SchemaFieldContractHashPayload> Fields { get; init; }
}

internal sealed record SchemaDefinitionHashPayload
{
    [JsonPropertyOrder(0)] public required string Id { get; init; }
    [JsonPropertyOrder(1)] public required string Version { get; init; }
    [JsonPropertyOrder(2)] public required IReadOnlyList<SchemaFieldDefinitionHashPayload> Fields { get; init; }
    [JsonPropertyOrder(3)] public required string DisplayName { get; init; }
    [JsonPropertyOrder(4)] public required string? Description { get; init; }
}
```

Rules:
- Contract payload: only `Classification = Contract` fields
- Definition payload: `Contract + DefinitionOnly` fields
- Excluded fields do not appear in any payload
- Nullable types preserve nullability
- Complex types use recursive sub-structure payloads
- Collection fields use `.ToArray()` for `IReadOnlyList<T>`

### 5.2 Sub-structure Payloads

Recursive generation with parent-kind prefix to avoid naming conflicts:

```csharp
SchemaFieldContractHashPayload / SchemaFieldDefinitionHashPayload
WorkflowStepContractHashPayload / WorkflowStepDefinitionHashPayload
HumanTaskOutcomeContractHashPayload / HumanTaskOutcomeDefinitionHashPayload
```

### 5.3 Projection Code

```csharp
internal static class SchemaCanonicalHashProjection
{
    public static SchemaContractHashPayload ToContractPayload(SchemaDescriptor descriptor)
    {
        return new SchemaContractHashPayload
        {
            Id = descriptor.Id,
            Version = descriptor.Version,
            Fields = descriptor.Fields
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(SchemaFieldCanonicalHashProjection.ToContractPayload)
                .ToArray()
        };
    }

    public static SchemaDefinitionHashPayload ToDefinitionPayload(SchemaDescriptor descriptor)
    {
        return new SchemaDefinitionHashPayload
        {
            Id = descriptor.Id,
            Version = descriptor.Version,
            Fields = descriptor.Fields
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(SchemaFieldCanonicalHashProjection.ToDefinitionPayload)
                .ToArray(),
            DisplayName = descriptor.DisplayName,
            Description = descriptor.Description
        };
    }
}
```

Collection ordering rules:
- `SourceOrder` → no sorting, direct mapping
- `OrdinalByValue` → `.OrderBy(x => x, StringComparer.Ordinal)`
- `OrdinalByProperty` → `.OrderBy(x => x.{Property}, StringComparer.Ordinal)`
- `OrderedKeyValue` → `.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(...)` → `CanonicalStringKeyValuePayload`

Collection null rules:
- Non-nullable descriptor collections: projector treats as non-null, uses `.ToArray()`
- Nullable descriptor collections: preserve nullable; null and empty array produce different hashes

### 5.4 Dispatcher (returns CanonicalHashMetadata + WriteCanonicalJson)

`CanonicalHashProjectionResult` lives in `CrestCreates.Metadata.Abstractions` as a **public** type, since `ICanonicalHashComputer.ComputeFromProjection` accepts it. SG-generated dispatcher code and hand-written artifact projectors both construct it via the `Create` factory.

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record CanonicalHashProjectionResult(
    CanonicalHashMetadata Metadata,
    Action<Utf8JsonWriter> WriteCanonicalJson)
{
    public static CanonicalHashProjectionResult Create(
        CanonicalHashMetadata metadata,
        Action<Utf8JsonWriter> writeCanonicalJson)
    {
        return new CanonicalHashProjectionResult(metadata, writeCanonicalJson);
    }
}
```

`CanonicalHashMetadata` is a sealed record carrying the 7 envelope metadata fields:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record CanonicalHashMetadata
{
    public required string ArtifactKind { get; init; }
    public string? DescriptorKind { get; init; }
    public required string Purpose { get; init; }
    public required string Scope { get; init; }
    public required string AlgorithmVersion { get; init; }
    public required string ContractVersion { get; init; }
    public required string CanonicalShapeVersion { get; init; }
}
```

**SG-generated CanonicalHashDispatcherWriter**:

```csharp
internal static class CanonicalHashDispatcherWriter
{
    public static CanonicalHashProjectionResult ToContractEnvelope(
        IDescriptor descriptor,
        CanonicalHashScope scope,
        string contractVersion,
        string algorithmVersion)
    {
        return descriptor switch
        {
            SchemaDescriptor d => CanonicalHashProjectionResult.Create(
                new CanonicalHashMetadata
                {
                    ArtifactKind = CanonicalHashArtifactNames.Descriptor,
                    DescriptorKind = DescriptorKindNames.Schema,
                    Purpose = CanonicalHashPurposeNames.Contract,
                    Scope = CanonicalHashScopeNames.ToCanonicalString(scope),
                    AlgorithmVersion = algorithmVersion,
                    ContractVersion = contractVersion,
                    CanonicalShapeVersion = "schema-contract-hash-v1"
                },
                writer => SchemaCanonicalHashProfile.WriteContractEnvelope(writer, d, scope)),
            // ... per kind
            _ => throw new InvalidOperationException(
                $"No canonical hash profile for {descriptor.GetType().Name}")
        };
    }

    public static CanonicalHashProjectionResult ToDefinitionEnvelope(...) { /* same pattern */ }
}
```

**Profile writer methods** (SG-generated via `CanonicalHashWriterWriter`):

```csharp
internal static class SchemaCanonicalHashProfile
{
    public static void WriteContractEnvelope(Utf8JsonWriter writer, SchemaDescriptor d, CanonicalHashScope scope)
    {
        writer.WriteStartObject();
        // Envelope metadata fields
        writer.WriteString("ArtifactKind", CanonicalHashArtifactNames.Descriptor);
        writer.WriteString("DescriptorKind", DescriptorKindNames.Schema);
        writer.WriteString("Purpose", CanonicalHashPurposeNames.Contract);
        writer.WriteString("Scope", CanonicalHashScopeNames.ToCanonicalString(scope));
        // ... ContractVersion, AlgorithmVersion, CanonicalShapeVersion
        // Payload object
        writer.WritePropertyName("Payload");
        writer.WriteStartObject();
        writer.WriteString("Id", d.Id);
        writer.WriteString("Version", d.Version);
        // Fields: sorted by Name (OrdinalByProperty)
        writer.WritePropertyName("Fields");
        writer.WriteStartArray();
        foreach (var f in d.Fields.OrderBy(x => x.Name, StringComparer.Ordinal))
            SchemaFieldCanonicalHashProfile.WriteContractPayload(writer, f);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
```

**Critical**: Dispatcher must return `CanonicalHashProjectionResult` with `CanonicalHashMetadata` + `Action<Utf8JsonWriter> WriteCanonicalJson` paired. No `object Envelope`, no `JsonTypeInfo`, no `System.Text.Json` serialization context. The `Create` factory ensures metadata consistency.

AOT safety: `Utf8JsonWriter` is a value type with zero reflection. The entire write path is SG-generated static methods calling `writer.WriteString`/`writer.WriteStartObject`/`writer.WriteEndArray` directly — no runtime `Type`, no `JsonSerializer`, no `DefaultJsonTypeInfoResolver`.

### 5.5 CanonicalHashWriterWriter (SG-generated Utf8JsonWriter methods)

The `CanonicalHashWriterWriter` source generator produces per-profile static writer methods that write directly to `Utf8JsonWriter`. These replace the previous `JsonSerializerContext` approach entirely.

**SG generates per profile:**
- `WriteContractEnvelope(Utf8JsonWriter, TDescriptor, CanonicalHashScope)` — writes envelope metadata + Contract payload as nested JSON object
- `WriteDefinitionEnvelope(Utf8JsonWriter, TDescriptor, CanonicalHashScope)` — writes envelope metadata + Definition payload as nested JSON object
- `WriteContractPayload(Utf8JsonWriter, TSubStructure)` — for nested sub-structures
- `WriteDefinitionPayload(Utf8JsonWriter, TSubStructure)` — for nested sub-structures

**Writer rules:**
- PascalCase property names — `writer.WriteString("PropertyName", value)` directly
- Include null values explicitly — `writer.WriteString("PropertyName", value ?? null)` per field
- No `JsonSerializer`, `JsonTypeInfo`, `DefaultJsonTypeInfoResolver`, or runtime `Type` involvement
- AOT-safe by design — `Utf8JsonWriter` is a `ref struct` with zero reflection
- Envelope properties written before Payload; Payload written as a nested object with field order determined by `[CanonicalHashField(Order)]`

**CustomWriter support on `[CanonicalHashField]`**:
- The `CustomWriter` property specifies a hand-written writer class for fields requiring custom serialization
- When set, SG emits a call to the custom writer class instead of inline generation
- Example: `WorkflowStep.Target` (InteractionTarget discriminated union) → `InteractionTargetCanonicalHashWriter`

**Nullable<T> path handling in `OrderByProperty`**:
- SG generates null-safe property access for Nullable<T> intermediate segments
- Pattern: `x.Capability != null ? x.Capability.Value.Id : (string?)null`

### 5.6 Dictionary Canonicalization

```csharp
internal sealed record CanonicalStringKeyValuePayload
{
    [JsonPropertyOrder(0)] public required string Key { get; init; }
    [JsonPropertyOrder(1)] public string? Value { get; init; }
}

internal sealed record CanonicalStringKeyValuePayload<TValue>
{
    [JsonPropertyOrder(0)] public required string Key { get; init; }
    [JsonPropertyOrder(1)] public required TValue? Value { get; init; }
}
```

Dictionaries must not appear directly in canonical payloads. They must be converted to ordered key-value lists via `CollectionOrderMode = OrderedKeyValue`.

### 5.7 SG Diagnostics

| Code | Severity | Message |
|------|----------|---------|
| CCHASH001 | Warning | Descriptor public property '{Name}' is not classified by any CanonicalHashProfile. Configurable as Error via `CanonicalHashStrictProfileValidation` MSBuild property. |
| CCHASH002 | Error | CanonicalHashField references property '{Name}' that does not exist on '{TypeName}' |
| CCHASH003 | Error | Collection field '{Name}' requires explicit CollectionOrderMode |
| CCHASH004 | Error | Nested complex field '{Name}' requires ElementProfile or ValueProfile |
| CCHASH005 | Error | Contract payload cannot include DefinitionOnly fields |
| CCHASH006 | Error | Excluded field '{Name}' appears in generated payload |
| CCHASH007 | Error | CanonicalHashProfile TargetType and ContractShapeVersion/DefinitionShapeVersion are required |
| CCHASH008 | Error | Duplicate hash field order {N} in profile '{ProfileName}' |
| CCHASH009 | Error | Profile TargetType '{TypeName}' does not match DescriptorKind '{Kind}' |
| CCHASH010 | Warning | ArtifactKind '{Kind}' is reserved but not supported by SG v1 |
| CCHASH011 | Error | CollectionOrderMode.OrdinalByProperty requires OrderByProperty |
| CCHASH012 | Error | CollectionOrderMode.OrderedKeyValue can only be used on dictionary-like fields |
| CCHASH013 | Error | ElementProfile target type does not match collection element type |
| CCHASH014 | Error | Profile class must contain exactly one method carrying CanonicalHashField attributes; separate ContractFields/DefinitionFields declarations are not supported |

---

## 6. Runtime Hash Computer

### 6.1 CanonicalHash (Full Metadata)

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record CanonicalHash
{
    public required string Value { get; init; }
    public required string Algorithm { get; init; }           // "SHA-256"
    public required string AlgorithmVersion { get; init; }    // "sha256-canonical-json-v1"
    public required string ArtifactKind { get; init; }        // "Descriptor"
    public string? DescriptorKind { get; init; }              // "Schema" (null for non-descriptor)
    public required string Scope { get; init; }               // "InternalFull"
    public required string Purpose { get; init; }             // "Contract" / "Definition" / "SourceBinding" / "Integrity" / "AuditEvidence"
    public required string ContractVersion { get; init; }
    public required string CanonicalShapeVersion { get; init; } // "schema-contract-hash-v1" (string, not int)
}
```

### 6.2 CanonicalHashMetadata

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record CanonicalHashMetadata
{
    [JsonPropertyOrder(0)] public required string ArtifactKind { get; init; }
    [JsonPropertyOrder(1)] public string? DescriptorKind { get; init; }
    [JsonPropertyOrder(2)] public required string Scope { get; init; }
    [JsonPropertyOrder(3)] public required string Purpose { get; init; }
    [JsonPropertyOrder(4)] public required string ContractVersion { get; init; }
    [JsonPropertyOrder(5)] public required string CanonicalShapeVersion { get; init; }
    [JsonPropertyOrder(6)] public required string AlgorithmVersion { get; init; }
}
```

`CanonicalHashMetadata` carries the 7 metadata fields for domain separation. It replaces the previous `CanonicalHashEnvelope<TPayload>` pattern. Metadata participates in hash input via the writer delegate: the SG-generated `WriteContractEnvelope`/`WriteDefinitionEnvelope` methods write these fields as envelope properties before the nested Payload object, ensuring domain separation (same payload + different scope/purpose/shape = different hash).

### 6.3 Canonical String Helpers

```csharp
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

public static class CanonicalHashPurposeNames
{
    public const string Contract = "Contract";
    public const string Definition = "Definition";
    public const string SourceBinding = "SourceBinding";
    public const string Integrity = "Integrity";
    public const string AuditEvidence = "AuditEvidence";
    public static string ToCanonicalString(CanonicalHashPurpose purpose) => purpose switch
    {
        CanonicalHashPurpose.Contract => Contract,
        CanonicalHashPurpose.Definition => Definition,
        CanonicalHashPurpose.SourceBinding => SourceBinding,
        CanonicalHashPurpose.Integrity => Integrity,
        CanonicalHashPurpose.AuditEvidence => AuditEvidence,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };
}

public static class CanonicalHashArtifactNames
{
    public const string Descriptor = "Descriptor";
    public const string ReviewResult = "ReviewResult";
    public const string Package = "Package";
    public const string Report = "Report";
}

public static class DescriptorKindNames
{
    public const string Schema = "Schema";
    public const string Capability = "Capability";
    public const string Form = "Form";
    public const string HumanTask = "HumanTask";
    public const string Workflow = "Workflow";
    public const string Event = "Event";
    public static string ToCanonicalString(DescriptorKind kind) => kind switch
    {
        DescriptorKind.Schema => Schema,
        DescriptorKind.Capability => Capability,
        DescriptorKind.Form => Form,
        DescriptorKind.HumanTask => HumanTask,
        DescriptorKind.Workflow => Workflow,
        DescriptorKind.Event => Event,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
```

Rule: Never use `enum.ToString()` for hash input. Always use canonical string helpers. This prevents accidental hash changes from enum member renames.

### 6.4 ICanonicalHashComputer

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface ICanonicalHashComputer
{
    /// <summary>
    /// Compute ContractHash for a descriptor using SG-generated projection.
    /// </summary>
    CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope);

    /// <summary>
    /// Compute DefinitionHash for a descriptor using SG-generated projection.
    /// </summary>
    CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope);

    /// <summary>
    /// Compute a canonical hash from a pre-built projection result.
    /// For hand-written artifact projectors (ReviewResult, Package, ReportId, etc.).
    /// </summary>
    CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection);
}
```

### 6.5 DefaultCanonicalHashComputer

```csharp
namespace CrestCreates.Metadata;

public sealed class DefaultCanonicalHashComputer : ICanonicalHashComputer
{
    private const string Algorithm = "SHA-256";
    private const string AlgorithmVersion = "sha256-canonical-json-v1";

    public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
    {
        var projection = CanonicalHashDispatcherWriter.ToContractEnvelope(
            descriptor, scope, ContractVersions.DescriptorHash, AlgorithmVersion);
        return ComputeFromProjection(projection);
    }

    public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
    {
        var projection = CanonicalHashDispatcherWriter.ToDefinitionEnvelope(
            descriptor, scope, ContractVersions.DescriptorHash, AlgorithmVersion);
        return ComputeFromProjection(projection);
    }

    public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        // Write canonical JSON directly to a UTF-8 buffer via Utf8JsonWriter
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        projection.WriteCanonicalJson(writer);
        writer.Flush();
        var hashBytes = SHA256.HashData(buffer.WrittenSpan);
        var hashValue = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return new CanonicalHash
        {
            Value = hashValue,
            Algorithm = Algorithm,
            AlgorithmVersion = projection.Metadata.AlgorithmVersion,
            ArtifactKind = projection.Metadata.ArtifactKind,
            DescriptorKind = projection.Metadata.DescriptorKind,
            Scope = projection.Metadata.Scope,
            Purpose = projection.Metadata.Purpose,
            ContractVersion = projection.Metadata.ContractVersion,
            CanonicalShapeVersion = projection.Metadata.CanonicalShapeVersion
        };
    }
}
```

**AOT hard rule**: `ComputeFromProjection` must serialize using `projection.WriteCanonicalJson(writer)` with `Utf8JsonWriter` + `ArrayBufferWriter<byte>`. It must not use `JsonSerializer.Serialize`, `JsonTypeInfo`, or reflectively resolve serialization metadata at runtime. `Utf8JsonWriter` is a `ref struct` value type — zero reflection, fully AOT-compatible.

### 6.6 DescriptorStableHashBuilder (Adapter)

```csharp
public sealed class DescriptorStableHashBuilder : IDescriptorStableHashBuilder
{
    private readonly ICanonicalHashComputer _hashComputer;

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

Old pipe-delimited logic (AppendField, Esc, NullSentinel, ComputeSha256) is deleted entirely.

### 6.7 Hand-written Artifact Projections

ReviewResult, Package, and ReportId use hand-written canonical writers, but share the same `ICanonicalHashComputer.ComputeFromProjection`.

**Writer separation**:
- SG-generated descriptor profile writers + hand-written package writers (since `DescriptorPackageHashComputer` remains in Metadata)
- Hand-written ReviewResult / ReportId writers

`ComputeFromProjection` does not care which module the `WriteCanonicalJson` delegate comes from — it simply calls `projection.WriteCanonicalJson(writer)`.

**ReviewResult example**:
```csharp
internal static class ReviewResultCanonicalHashProjection
{
    public static CanonicalHashProjectionResult ToSourceBindingEnvelope(
        DescriptorDraftReviewResult reviewResult,  // must be already visibility-projected
        CanonicalHashScope scope,
        string algorithmVersion)
    {
        var metadata = new CanonicalHashMetadata
        {
            ArtifactKind = CanonicalHashArtifactNames.ReviewResult,
            Scope = CanonicalHashScopeNames.ToCanonicalString(scope),
            Purpose = CanonicalHashPurposeNames.SourceBinding,
            AlgorithmVersion = algorithmVersion,
            ContractVersion = ContractVersions.ReviewResultHash,
            CanonicalShapeVersion = "review-result-sourcebinding-v1"
        };
        return CanonicalHashProjectionResult.Create(metadata, writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("ArtifactKind", metadata.ArtifactKind);
            writer.WriteString("Purpose", metadata.Purpose);
            // ... envelope fields
            writer.WritePropertyName("Payload");
            writer.WriteStartObject();
            // ... review result fields
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }
}
```

### 6.8 DI Registration

```csharp
services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
services.AddSingleton<IDescriptorStableHashBuilder, DescriptorStableHashBuilder>();
```

---

## 7. Migration Strategy

### 7.1 Migration Overview

| # | System | Migration Method | SG | Purpose |
|---|--------|-----------------|-----|---------|
| 1 | Descriptor hashes | SG Profile/DTO/Projection/Dispatcher | ✅ | Contract / Definition |
| 2 | Package hashes | Hand-written canonical DTO + projection | ❌ | Integrity / AuditEvidence |
| 3 | SourceReviewHash | Hand-written canonical DTO + projection | ❌ | SourceBinding (TenantVisible) |
| 4 | ReportId | Hand-written canonical DTO + projection | ❌ | SourceBinding |

### 7.2 Migration Steps

**Step 1: SG Descriptor Profile Infrastructure**
- Create 6 Descriptor Profile classes + sub-structure Profiles
- SG generates dual Payloads, projection code, CanonicalHashDispatcherWriter, profile writer methods via CanonicalHashWriterWriter
- `DescriptorStableHashBuilder` degrades to `ICanonicalHashComputer` adapter
- Delete old pipe-delimited logic

**Step 2: Migrate DescriptorPackageHashComputer**
- `DescriptorPackageHashComputer` is marked `[Obsolete]` — still uses pipe-delimited format, but tagged for v2 migration
- Not deleted yet — still in production use for package hashes
- Create DescriptorPackage canonical DTO / projection
- Package hashes use `ICanonicalHashComputer.ComputeFromProjection`
- ContentHash / ManifestHash / EntryHash → Purpose=Integrity
- EvidenceHash → Purpose=AuditEvidence (default; use Integrity only if code inspection proves the hash is purely package-content integrity, not evidence binding)
- EnvelopeHash → Purpose=Integrity, may include createdAt only when createdAt is envelope contract metadata
- **Do not强行 map package ContentHash to descriptor Contract purpose** unless that hash explicitly represents descriptor contract identity
- Old DescriptorPackageHashComputer degrades to adapter or is deleted after v2 migration

**Step 3: Migrate SourceReviewHash**
- Create `ReviewResultCanonicalHashPayload` (hand-written canonical DTO with full field set per issue spec)
- InternalFull scope: complete review result
- **TenantVisible scope = SourceReviewHash** (explicit TenantVisible SourceBinding hash)
- Create `ReviewResultCanonicalHashProjection` (hand-written, producing `CanonicalHashProjectionResult` with writer delegate)
- Delete `ComputeSourceReviewHash` from `DefaultDescriptorReviewReportBuilder`, use `ComputeFromProjection`

**Step 4: Migrate ReportId**
- Create `ReportSourceBindingHashPayload` (hand-written canonical DTO: TenantId, DraftId, DraftVersion, SourceReviewHash, ContractVersion, TemplateVersion)
- No GeneratedAt
- Create `ReportSourceBindingHashProjection` (hand-written)
- Delete builder-local ReportId computation

**Step 5: Visibility Projector Update**
- Visibility projector first generates TenantVisible artifact/review result
- **TenantVisible hash must only be computed from TenantVisible-projected artifacts**
- **Must not compute TenantVisible hash from InternalFull artifacts by merely changing scope parameter**
- InternalFull hashes must not be passed to TenantVisible consumers

**Step 6: Cleanup**
- Delete all old `ComputeSha256` duplicate implementations
- Delete old pipe-delimited helper methods
- Delete `DescriptorHashComputer` (static wrapper, replaced by `ICanonicalHashComputer`)
- **Remove `IDescriptor.ContractHash` / `IDescriptor.DefinitionHash`** (hashes are computed results, not intrinsic properties)
- All consumers obtain hashes via `IDescriptorStableHashBuilder.Build(descriptor)` or `ICanonicalHashComputer`
- Delete hard-coded hash counts from report sections
- **Deleted types moved to `99_RecycleBin/`** (for reference, not compiled):
  - `CanonicalHashEnvelope` — replaced by `CanonicalHashMetadata`
  - `MetadataCanonicalHashJsonContext` — replaced by SG-generated `Utf8JsonWriter` methods
  - `CanonicalHashJsonContextWriter` — replaced by `CanonicalHashWriterWriter`
  - `DescriptorHashComputer` — replaced by `ICanonicalHashComputer`/`DefaultCanonicalHashComputer`
  - `IHasContractIdentity` — replaced by `ICanonicalHashable` → then replaced by `CanonicalHashProfile`
  - `DescriptorHashComputerTests` — removed

### 7.3 Breaking Changes

- All existing hash values become invalid (pipe-delimited → canonical JSON written via `Utf8JsonWriter`)
- `DescriptorStableHashes.ContractHash` / `DefinitionHash` type changes from `string` to `CanonicalHash`
  - Callers needing the digest string should use `ContractHash.Value` / `DefinitionHash.Value`
- `IDescriptor.ContractHash` / `DefinitionHash` properties removed
- `CanonicalHashEnvelope<TPayload>` removed — replaced by `CanonicalHashMetadata`
- `MetadataCanonicalHashJsonContext` removed — replaced by SG-generated `Utf8JsonWriter` writer methods
- `CanonicalHashJsonContextWriter` removed — replaced by `CanonicalHashWriterWriter`
- `IHasContractIdentity` removed — replaced by `CanonicalHashProfile`
- SchemaField optional field downgraded from ContractHash to DefinitionOnly
- No backward compatibility shims allowed
- Internal runtime models may carry full `CanonicalHash` metadata
- Agent-facing or report-facing DTOs must explicitly decide whether to expose full `CanonicalHash` metadata or only `CanonicalHash.Value`
- Do not accidentally leak InternalFull hash metadata into TenantVisible DTOs

---

## 8. Test Strategy

### 8.1 Determinism

- Same canonical input → same hash
- Dictionary order does not affect hash
- Unordered collection order does not affect hash (e.g., Schema.Fields sorted by Name)
- **SourceOrder collection reorder must affect hash** (e.g., Workflow.Steps order is contract semantics)

### 8.2 Policy

- Schema optional field addition → DefinitionHash only, not ContractHash
- Schema required field removal → ContractHash changes
- Workflow step reorder → ContractHash changes (SourceOrder)
- Capability permission change → ContractHash changes
- Form label change → DefinitionHash only
- Event payload schema change → ContractHash changes

### 8.3 SourceBinding

- VisibleReviewHash stable (same projected review)
- Diagnostic change → hash changes
- Package hash change → hash changes
- ReportId excludes GeneratedAt
- ReportId changes when SourceReviewHash/TemplateVersion changes
- SourceReviewHash is TenantVisible SourceBinding hash

### 8.4 Visibility

- Denied descriptor kind does not affect TenantVisible hash
- Denied descriptor kind affects InternalFull hash
- TenantVisible hash does not encode denied descriptor count
- **TenantVisible hash must be computed from projected artifact, not from InternalFull artifact with scope swap**

### 8.5 SG Diagnostics

- CCHASH001-014 trigger conditions covered

---

## 9. Non-goals

- Do not make `DescriptorReviewReportDto` the center of the hash runtime
- Do not create a ReviewReport-only hash service
- Do not implement dynamic policy loading
- Do not expose hash policy customization to tenants yet
- Do not introduce runtime plugin/reflection discovery
- Do not move visibility or access-control logic into the hash computer
- Do not refactor report rendering, fix proposal contracts, activation workflow, or package validation beyond the hash integration points
- Do not preserve old hash values through compatibility shims
- Do not implement PublicCrossTenant projection in this issue; the scope name is reserved for future use
