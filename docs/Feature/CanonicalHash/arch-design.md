# Canonical Hash Runtime — Architecture Design

> **Date:** 2026-06-23 | **Status:** Complete (v1) | **Issue:** #30

---

## 1. Design Goals

Replace 4 independent ad hoc hash systems with one deterministic, versioned, AoT-friendly hash runtime driven by a Source Generator for descriptor types.

The target question:

```
Given artifact X, what is its deterministic, domain-separated identity hash that survives enum renames, field reorder, and collection ordering ambiguity?
```

### Design Principles

1. **SG owns shape generation, Runtime owns execution** — The source generator generates canonical DTOs, projections, and Utf8JsonWriter methods. The runtime applies SHA-256.
2. **No reflection, no runtime Type** — Utf8JsonWriter is a ref struct with zero reflection. No JsonSerializer, no JsonTypeInfo, no DefaultJsonTypeInfoResolver.
3. **Profile declaration is compile-time only** — Profile classes carry attributes; they are never instantiated, never registered in DI.
4. **Domain separation via metadata** — CanonicalHashMetadata (7 fields) participates in hash input. Same payload + different scope/purpose/shape = different hash.
5. **Canonical string helpers for all enums** — Never use enum.ToString(). Always use *Names.ToCanonicalString() methods.

---

## 2. Architecture Overview

```
Profile Class (compile-time declaration)
    ↓ [CanonicalHashProfile] + [CanonicalHashField] attributes
CanonicalHashSourceGenerator
    ├─→ Dual Payload DTOs (Contract + Definition per descriptor)
    ├─→ Projection Code (descriptor → payload mapping)
    ├─→ CanonicalHashProjectionDispatcher (switch-based, per concrete type)
    └─→ CanonicalHashWriterWriter (Utf8JsonWriter methods per profile)
           ├─ WriteContractEnvelope (envelope metadata + contract payload)
           ├─ WriteDefinitionEnvelope (envelope metadata + definition payload)
           ├─ WriteContractPayload (sub-structures, payload fields only)
           └─ WriteDefinitionPayload (sub-structures, payload fields only)

Runtime
    ↓ ICanonicalHashComputer
DefaultCanonicalHashComputer
    ↓ ArrayBufferWriter<byte> + Utf8JsonWriter + SHA256.HashData
CanonicalHash (9-field result)
```

---

## 3. Core Types

### 3.1 CanonicalHash

```csharp
public sealed record CanonicalHash
{
    public required string Value { get; init; }               // lowercase hex digest
    public required string Algorithm { get; init; }           // "SHA-256"
    public required string AlgorithmVersion { get; init; }    // "sha256-canonical-json-v1"
    public required string ArtifactKind { get; init; }        // "Descriptor"
    public string? DescriptorKind { get; init; }              // "Schema" (null for non-descriptor)
    public required string Scope { get; init; }               // "InternalFull"
    public required string Purpose { get; init; }             // "Contract" / "Definition"
    public required string ContractVersion { get; init; }
    public required string CanonicalShapeVersion { get; init; } // "schema-contract-hash-v1"
}
```

### 3.2 CanonicalHashMetadata

```csharp
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

### 3.3 CanonicalHashProjectionResult

```csharp
public sealed record CanonicalHashProjectionResult(
    CanonicalHashMetadata Metadata,
    Action<Utf8JsonWriter> WriteCanonicalJson)
{
    public static CanonicalHashProjectionResult Create(
        CanonicalHashMetadata metadata,
        Action<Utf8JsonWriter> writeCanonicalJson);
}
```

### 3.4 ICanonicalHashComputer

```csharp
public interface ICanonicalHashComputer
{
    CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope);
    CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope);
    CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection);
}
```

---

## 4. Profile Declaration Model

### 4.1 Profile Attributes

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[CanonicalHashProfile]` | Class | Declares ArtifactKind, DescriptorKind, TargetType, ShapeVersions |
| `[CanonicalHashField]` | Method | Declares PropertyName, Classification, Order, ElementProfile, CollectionOrderMode, CustomWriter |

### 4.2 Field Classification

| Classification | Contract Payload | Definition Payload |
|---------------|------------------|-------------------|
| Contract | ✅ | ✅ |
| DefinitionOnly | ❌ | ✅ |
| Excluded | ❌ | ❌ |

### 4.3 Collection Ordering

| Mode | Behavior | Example |
|------|----------|---------|
| None | Error — CCHASH003 | — |
| SourceOrder | Preserve runtime order | Workflow.Steps |
| OrdinalByValue | Sort by value (StringComparer.Ordinal) | String collections |
| OrdinalByProperty | Sort by a specific property | Schema.Fields by Name |
| OrderedKeyValue | Dictionary → sorted key-value list | Metadata dictionaries |

---

## 5. SG Pipeline

### 5.1 CanonicalHashSourceGenerator

Incremental source generator registered in `CrestCreates.CodeGenerator`. Pipeline:

1. **SyntaxProvider** — finds classes with `[CanonicalHashProfile]` attribute
2. **ModelBuilder** — reads attributes, builds `CanonicalHashModel` per profile with field classifications
3. **PayloadWriter** — generates `internal sealed record {Name}ContractHashPayload` / `DefinitionHashPayload`
4. **ProjectionWriter** — generates `ToContractPayload` / `ToDefinitionPayload` mapping methods
5. **DispatcherWriter** — generates `CanonicalHashProjectionDispatcher` with type-pattern switch returning `CanonicalHashProjectionResult`
6. **WriterWriter** — generates `WriteContractEnvelope` / `WriteDefinitionEnvelope` / `WriteContractPayload` / `WriteDefinitionPayload` Utf8JsonWriter methods

### 5.2 Canonical JSON Writer (WriterWriter)

The writer generates static methods that write directly to `Utf8JsonWriter`:

- **Envelope methods** (`WriteContractEnvelope`): Write 7 metadata fields + nested `"Payload"` object with field values
- **Payload methods** (`WriteContractPayload`): Write only field values (for sub-structures nested inside parent payloads)
- **Enum handling**: Generates inline switch expressions using canonical string constants — never `enum.ToString()`
- **Nullable<T> paths**: Generates null-safe access (`x.Capability != null ? x.Capability.Value.Id : (string?)null`)
- **CustomWriter**: `[CanonicalHashField]` with `CustomWriter = typeof(...)` delegates to hand-written writer class

### 5.3 Sub-structure Semantics

Sub-structures (e.g., SchemaFieldDescriptor inside SchemaDescriptor) generate payload-only writers (`WriteContractPayload`/`WriteDefinitionPayload`). When a parent writes a collection of sub-structures, it calls `WritePayload` — no envelope metadata is repeated for each element.

---

## 6. Runtime Hash Computer

### 6.1 DefaultCanonicalHashComputer

```
1. Obtain CanonicalHashProjectionResult (via dispatcher or hand-written projection)
2. Create ArrayBufferWriter<byte>
3. Create Utf8JsonWriter (PascalCase, IncludeNulls)
4. Call projection.WriteCanonicalJson(writer)
5. SHA256.HashData(buffer.WrittenSpan)
6. Convert to lowercase hex string
7. Construct CanonicalHash from digest + metadata
```

### 6.2 DescriptorStableHashBuilder (Adapter)

Wraps `ICanonicalHashComputer` to implement `IDescriptorStableHashBuilder`. Returns `DescriptorStableHashes` with `ContractHash`/`DefinitionHash` as `CanonicalHash` (not bare string).

---

## 7. Discriminated Union Support

InteractionTarget (abstract record with 3 subtypes: CapabilityTarget, HumanTaskTarget, SubWorkflowTarget) uses a hand-written `InteractionTargetCanonicalHashWriter`:

```csharp
w.WriteString("Kind", target switch
{
    CapabilityTarget => "Capability",
    HumanTaskTarget => "HumanTask",
    SubWorkflowTarget => "Workflow",
    _ => "Unknown"
});
w.WriteString("Id", target.Id);
w.WriteNumber("Version", target.Version);
```

Registered via `[CanonicalHashField(..., CustomWriter = typeof(InteractionTargetCanonicalHashWriter))]`.

---

## 8. Profile Classes (6 Descriptor Types)

| Profile | DescriptorKind | Contract Fields | Definition-Only Fields |
|---------|---------------|-----------------|----------------------|
| SchemaDescriptorCanonicalHashProfile | Schema | Id, Version, Fields, References, ValidationRules | DisplayName, Description |
| SchemaFieldCanonicalHashProfile | (sub-structure) | Name, FieldType, IsRequired, DefaultValue | DisplayName, Description |
| FormDescriptorCanonicalHashProfile | Form | Id, Name, Version, Schema, Fields | DisplayName, Description |
| CapabilityDescriptorCanonicalHashProfile | Capability | Id, Name, Version, Permission, Inputs, Outputs, Produces, Consumes | DisplayName, Description |
| HumanTaskDescriptorCanonicalHashProfile | HumanTask | Id, Name, Version, AssigneeStrategy, Outcomes | DisplayName, Description |
| WorkflowDescriptorCanonicalHashProfile | Workflow | Id, Name, Version, Steps | DisplayName, Description |
| EventDescriptorCanonicalHashProfile | Event | Id, Name, Version, State, PayloadSchema, Category, Semantic, ChangeKind | Importance |

Sub-structure profiles: SchemaFieldCanonicalHashProfile, VersionedSchemaRefCanonicalHashProfile, FormFieldCanonicalHashProfile, WorkflowStepCanonicalHashProfile, CompletionOutcomeCanonicalHashProfile, EventRefCanonicalHashProfile, VersionedDescriptorRefCanonicalHashProfile.

---

## 9. SG Diagnostics

| Code | Severity | Condition |
|------|----------|-----------|
| CCHASH001 | Warning | Public property not classified by any profile |
| CCHASH002 | Error | Referenced property does not exist on target type |
| CCHASH003 | Error | Collection field missing explicit CollectionOrderMode |
| CCHASH004 | Error | Complex field missing ElementProfile or ValueProfile |
| CCHASH007 | Error | Profile missing TargetType or ShapeVersions |
| CCHASH008 | Error | Duplicate field order |
| CCHASH009 | Error | TargetType/DescriptorKind mismatch |
| CCHASH010 | Warning | ArtifactKind not supported by SG v1 |
| CCHASH011 | Error | OrdinalByProperty without OrderByProperty |
| CCHASH012 | Error | OrderedKeyValue on non-dictionary field |
| CCHASH013 | Error | ElementProfile type mismatch |
| CCHASH014 | Error | Multiple field-block methods in profile |

---

## 10. Breaking Changes

- All existing hash values invalidated (pipe-delimited → canonical JSON via Utf8JsonWriter)
- `DescriptorStableHashes.ContractHash`/`DefinitionHash` type: `string` → `CanonicalHash`
- `IDescriptor.ContractHash`/`DefinitionHash` properties removed
- `IHasContractIdentity` removed
- `DescriptorHashComputer` removed
- `CanonicalHashEnvelope<TPayload>` removed — replaced by `CanonicalHashMetadata`

---

## 11. Deferred to v2

- DescriptorPackageHashComputer migration to canonical JSON
- SourceReviewHash / ReportId migration via hand-written projections
- PublicCrossTenant scope projection
- CCHASH diagnostic trigger tests (requires SG test infrastructure)
- Visibility projector integration tests
