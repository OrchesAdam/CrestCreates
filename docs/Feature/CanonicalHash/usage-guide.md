# Canonical Hash Runtime — Usage Guide

> This document is for CrestCreates module developers who need to compute, consume, or extend canonical hashes.
> *v1 (2026-06-23): SG-based descriptor hash generation, DefaultCanonicalHashComputer, DescriptorStableHashBuilder adapter*

---

## 1. Quick Start

### 1.1 Register the Hash Computer

```csharp
using CrestCreates.Metadata;

var builder = WebApplication.CreateBuilder(args);

// Register the canonical hash computer + stable hash builder adapter
builder.Services.AddCanonicalHashRuntime();

// Or register individually:
builder.Services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
builder.Services.AddSingleton<IDescriptorStableHashBuilder, DescriptorStableHashBuilder>();
```

### 1.2 Compute Descriptor Hashes

```csharp
using CrestCreates.Metadata.Abstractions;

public class MyService
{
    private readonly ICanonicalHashComputer _hashComputer;

    public MyService(ICanonicalHashComputer hashComputer)
    {
        _hashComputer = hashComputer;
    }

    public void AnalyzeSchema(SchemaDescriptor schema)
    {
        // Compute ContractHash — changes when contract-shaping fields change
        var contractHash = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.InternalFull);

        // Compute DefinitionHash — changes when any non-excluded field changes
        var definitionHash = _hashComputer.ComputeDefinitionHash(schema, CanonicalHashScope.InternalFull);

        Console.WriteLine($"Contract: {contractHash.Value}");        // lowercase hex
        Console.WriteLine($"Definition: {definitionHash.Value}");
        Console.WriteLine($"Shape: {contractHash.CanonicalShapeVersion}"); // "schema-contract-hash-v1"
    }
}
```

### 1.3 Use the Stable Hash Builder (Legacy Adapter)

```csharp
using CrestCreates.Metadata.Abstractions;

public class MyLegacyService
{
    private readonly IDescriptorStableHashBuilder _hashBuilder;

    public MyLegacyService(IDescriptorStableHashBuilder hashBuilder)
    {
        _hashBuilder = hashBuilder;
    }

    public void CompareDescriptors(IDescriptor d1, IDescriptor d2)
    {
        var hashes1 = _hashBuilder.Build(d1);
        var hashes2 = _hashBuilder.Build(d2);

        // ContractHash and DefinitionHash are CanonicalHash records, not strings
        if (hashes1.ContractHash != hashes2.ContractHash)
        {
            Console.WriteLine("Contract changed!");
        }

        // Access the digest string via .Value
        var contractDigest = hashes1.ContractHash.Value;
    }
}
```

---

## 2. Profile Declaration

### 2.1 Define a Profile for a New Descriptor Type

```csharp
using CrestCreates.Metadata.Abstractions;
using static CrestCreates.Metadata.Abstractions.CanonicalHashFieldClassification;

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
    [CanonicalHashField(nameof(SchemaDescriptor.Fields), Contract, Order = 2,
        ElementProfile = typeof(SchemaFieldCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = "Name")]
    [CanonicalHashField(nameof(SchemaDescriptor.DisplayName), DefinitionOnly, Order = 100)]
    [CanonicalHashField(nameof(SchemaDescriptor.Description), DefinitionOnly, Order = 101)]
    [CanonicalHashField(nameof(SchemaDescriptor.CreatedAt), Excluded, Order = 200)]
    [CanonicalHashField(nameof(SchemaDescriptor.UpdatedAt), Excluded, Order = 201)]
    private static void Fields() { }
}
```

### 2.2 Profile Rules

- **One classification per field**: Declare each field once. SG auto-derives Contract and Definition payloads.
- **`Fields()` must be `private static void`**: SG reads attributes only; no method body is needed.
- **Order determines JSON property sequence**: SG re-sequences to continuous `[JsonPropertyOrder]`.
- **Excluded is explicit**: Unclassified public properties trigger CCHASH001 warning.

### 2.3 Collection Ordering Modes

```csharp
// SourceOrder — runtime order matters (e.g., Workflow steps)
[CanonicalHashField(nameof(Workflow.Steps), Contract, Order = 2,
    ElementProfile = typeof(WorkflowStepCanonicalHashProfile),
    CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder)]

// OrdinalByProperty — sort by a specific element property
[CanonicalHashField(nameof(Schema.Fields), Contract, Order = 2,
    ElementProfile = typeof(SchemaFieldCanonicalHashProfile),
    CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
    OrderByProperty = "Name")]

// OrdinalByValue — sort by value (for string collections)
[CanonicalHashField(nameof(Descriptor.Tags), Contract, Order = 5,
    CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]

// OrderedKeyValue — dictionaries: canonicalize to sorted KV list
[CanonicalHashField(nameof(Schema.Metadata), Contract, Order = 10,
    CollectionOrderMode = CanonicalHashCollectionOrderMode.OrderedKeyValue)]
```

### 2.4 Custom Writers for Discriminated Unions

```csharp
// In the profile — reference a hand-written writer class
[CanonicalHashField(nameof(WorkflowStep.Target), Contract, Order = 5,
    CustomWriter = typeof(InteractionTargetCanonicalHashWriter))]

// The hand-written writer class
internal static class InteractionTargetCanonicalHashWriter
{
    public static void WriteContractEnvelope(Utf8JsonWriter w, InteractionTarget target)
    {
        w.WriteStartObject();
        w.WriteString("Kind", target switch
        {
            CapabilityTarget => "Capability",
            HumanTaskTarget => "HumanTask",
            SubWorkflowTarget => "Workflow",
            _ => "Unknown"
        });
        w.WriteString("Id", target.Id);
        w.WriteNumber("Version", target.Version);
        w.WriteEndObject();
    }

    public static void WriteDefinitionEnvelope(Utf8JsonWriter w, InteractionTarget target)
        => WriteContractEnvelope(w, target); // same shape for both
}
```

---

## 3. Hand-written Artifact Projections

For artifacts not covered by SG profiles (ReviewResult, Package, Report), write canonical projections manually:

```csharp
internal static class ReviewResultCanonicalHashProjection
{
    public static CanonicalHashProjectionResult ToSourceBindingEnvelope(
        DescriptorDraftReviewResult reviewResult,
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
            writer.WriteString("Scope", metadata.Scope);
            writer.WriteString("Purpose", metadata.Purpose);
            writer.WriteString("AlgorithmVersion", metadata.AlgorithmVersion);
            writer.WriteString("ContractVersion", metadata.ContractVersion);
            writer.WriteString("CanonicalShapeVersion", metadata.CanonicalShapeVersion);
            writer.WritePropertyName("Payload");
            writer.WriteStartObject();
            // ... write review result fields
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }
}

// Usage:
var projection = ReviewResultCanonicalHashProjection.ToSourceBindingEnvelope(
    reviewResult, CanonicalHashScope.TenantVisible, "sha256-canonical-json-v1");
var hash = _hashComputer.ComputeFromProjection(projection);
```

---

## 4. Canonical String Helpers

Never use `enum.ToString()` for hash input. Always use the canonical string helpers:

```csharp
// Scope
string scope = CanonicalHashScopeNames.ToCanonicalString(CanonicalHashScope.InternalFull);
// → "InternalFull"

// Purpose
string purpose = CanonicalHashPurposeNames.ToCanonicalString(CanonicalHashPurpose.Contract);
// → "Contract"

// Artifact kind
string kind = CanonicalHashArtifactNames.Descriptor;
// → "Descriptor"

// Descriptor kind
string dk = DescriptorKindNames.ToCanonicalString(DescriptorKind.Schema);
// → "Schema"
```

---

## 5. CanonicalHash Record

`CanonicalHash` is a 9-field record. Key properties:

| Property | Example | Purpose |
|----------|---------|---------|
| `Value` | `"a1b2c3..."` | Lowercase hex SHA-256 digest |
| `Algorithm` | `"SHA-256"` | Algorithm name |
| `AlgorithmVersion` | `"sha256-canonical-json-v1"` | Pipeline version |
| `ArtifactKind` | `"Descriptor"` | What was hashed |
| `DescriptorKind` | `"Schema"` | Specific descriptor kind (null for non-descriptor) |
| `Scope` | `"InternalFull"` | Visibility boundary |
| `Purpose` | `"Contract"` | Why the hash was computed |
| `ContractVersion` | `"canonical-hash-v1"` | Hash contract version |
| `CanonicalShapeVersion` | `"schema-contract-hash-v1"` | Field set + ordering version |

```csharp
var hash = _hashComputer.ComputeContractHash(descriptor, CanonicalHashScope.InternalFull);

// Record equality — two CanonicalHash with same 9 fields are equal
hash1 == hash2; // structural equality

// Access the digest
var digest = hash.Value;
```

---

## 6. SG Diagnostics Reference

| Code | Severity | When |
|------|----------|------|
| CCHASH001 | Warning | Public property not classified by any profile |
| CCHASH002 | Error | Property does not exist on target type |
| CCHASH003 | Error | Collection without explicit CollectionOrderMode |
| CCHASH004 | Error | Complex field without ElementProfile/ValueProfile |
| CCHASH007 | Error | Missing TargetType or ShapeVersions |
| CCHASH008 | Error | Duplicate field order |
| CCHASH009 | Error | TargetType/DescriptorKind mismatch |
| CCHASH010 | Warning | ArtifactKind not supported by SG v1 |
| CCHASH011 | Error | OrdinalByProperty without OrderByProperty |
| CCHASH012 | Error | OrderedKeyValue on non-dictionary |
| CCHASH013 | Error | ElementProfile type mismatch |
| CCHASH014 | Error | Multiple field-block methods in profile |

---

## 7. Migration from Old Hash Systems

### 7.1 From IDescriptor.ContractHash/DefinitionHash

```csharp
// Old — hash was a string property on IDescriptor
var contractHash = descriptor.ContractHash;

// New — hash is computed via ICanonicalHashComputer or IDescriptorStableHashBuilder
var hashes = _hashBuilder.Build(descriptor);
var contractHash = hashes.ContractHash;  // CanonicalHash record
var digest = contractHash.Value;         // string digest
```

### 7.2 From pipe-delimited hash logic

```csharp
// Old — hand-coded pipe-delimited string concatenation
var hash = ComputeSha256($"{field1}|{field2}|{field3}");

// New — canonical JSON via SG-generated Utf8JsonWriter + SHA-256
var hash = _hashComputer.ComputeContractHash(descriptor, CanonicalHashScope.InternalFull);
```

### 7.3 Breaking changes

- All existing hash values are invalidated (different serialization format)
- `DescriptorStableHashes.ContractHash`/`DefinitionHash` is now `CanonicalHash`, not `string`
- `IDescriptor.ContractHash`/`DefinitionHash` properties removed
- `IHasContractIdentity` removed
- `DescriptorHashComputer` removed

---

## 8. Testing Patterns

### 8.1 Determinism

```csharp
[Fact]
public void Same_Descriptor_Produces_Same_Hash()
{
    var schema = CreateTestSchema();
    var hash1 = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.InternalFull);
    var hash2 = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.InternalFull);
    hash1.Value.Should().Be(hash2.Value);
}
```

### 8.2 Domain Separation

```csharp
[Fact]
public void Different_Scope_Produces_Different_Hash()
{
    var schema = CreateTestSchema();
    var internalHash = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.InternalFull);
    var tenantHash = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.TenantVisible);
    internalHash.Value.Should().NotBe(tenantHash.Value);
}
```

### 8.3 Contract vs Definition

```csharp
[Fact]
public void Definition_Change_Does_Not_Affect_ContractHash()
{
    var schema1 = CreateTestSchema(displayName: "v1");
    var schema2 = CreateTestSchema(displayName: "v2");
    var c1 = _hashComputer.ComputeContractHash(schema1, CanonicalHashScope.InternalFull);
    var c2 = _hashComputer.ComputeContractHash(schema2, CanonicalHashScope.InternalFull);
    c1.Value.Should().Be(c2.Value, "DisplayName is DefinitionOnly");
}
```

---

## 9. Project Locations

| Component | Project | Path |
|-----------|---------|------|
| CanonicalHash, CanonicalHashMetadata, CanonicalHashProjectionResult | Metadata.Abstractions | `src/Metadata/CrestCreates.Metadata.Abstractions/` |
| ICanonicalHashComputer | Metadata.Abstractions | `src/Metadata/CrestCreates.Metadata.Abstractions/` |
| DefaultCanonicalHashComputer, DescriptorStableHashBuilder | Metadata | `src/Metadata/CrestCreates.Metadata/` |
| Profile classes | Metadata | `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/` |
| CanonicalHashSourceGenerator | CodeGenerator | `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/` |
| Canonical string helpers | Metadata.Abstractions | `src/Metadata/CrestCreates.Metadata.Abstractions/` |
| Design spec | docs | `docs/superpowers/specs/2026-06-23-canonical-hash-runtime-design.md` |
