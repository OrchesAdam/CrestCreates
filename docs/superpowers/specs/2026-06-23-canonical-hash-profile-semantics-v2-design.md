# Canonical Hash Profile Semantics v2 — Design Spec

**Date**: 2026-06-23
**Status**: Draft
**Related**: #30, `docs/Feature/CanonicalHash/arch-design.md`, `docs/superpowers/specs/2026-06-23-canonical-hash-runtime-design.md`

## 1. Goal

Canonical Hash Runtime v1 closed the main hash execution path: descriptor hashes are generated from profile declarations, canonical JSON is written with `Utf8JsonWriter`, and runtime execution only computes SHA-256 over generated canonical bytes.

This v2 spec tightens two profile semantics that should be resolved before more descriptor and control-plane artifacts depend on the hash model:

1. Replace hand-written `CustomWriter` discriminated-union handling with a declaration-driven union profile model.
2. Split Schema hash semantics from directional compatibility analysis so optional schema changes are not forced into `ContractHash` only to keep governance visible.

Breaking changes are allowed. Existing hash values may change.

## 2. Current State

### 2.1 Discriminated Unions

`WorkflowStep.Target` is currently included in `WorkflowStepCanonicalHashProfile` as a `Contract` field with:

```csharp
CustomWriter = typeof(InteractionTargetCanonicalHashWriter)
```

The hand-written writer switches over `CapabilityTarget`, `HumanTaskTarget`, and `SubWorkflowTarget`, then emits `{ Kind, Id, Version }`.

This fixed the immediate ContractHash gap, but it leaves a second model beside the SG path:

- union cases are hard-coded in a manual writer;
- missing union cases are discovered only at runtime;
- `CustomWriter` is a broad escape hatch that can bypass profile diagnostics;
- canonical shape is not declared in profile form.

### 2.2 Schema Optional Fields

`SchemaDescriptorCanonicalHashProfile` includes `Fields` in `ContractHash`, and `SchemaFieldCanonicalHashProfile` classifies every `SchemaFieldDescriptor` property as `Contract`.

This means adding an optional schema field changes both `ContractHash` and `DefinitionHash`. The compatibility rule already classifies optional field addition as `Compatible`, but that rule only runs when the change set contains `ContractHashChanged` or `Updated`.

If optional fields are simply removed from `ContractHash` without changing the change-set model, optional additions can disappear from compatibility/governance analysis.

## 3. Design Principles

1. **Profile declarations remain the source of hash shape truth.** Do not keep long-term handwritten writer special cases.
2. **SG owns shape and writer generation.** It may generate union switches and field filtering, but it does not decide compatibility level or governance outcome.
3. **Hash is a fingerprint, not a compatibility engine.** Directional rules such as optional add/remove/type change belong to compatibility analysis over old/new descriptors.
4. **No compatibility shims for old hash values.** Shape-version bumps are enough.
5. **No runtime reflection or dynamic discovery.** Union exhaustiveness and profile correctness are compile-time diagnostics.

## 4. Union Profile v2

### 4.1 Attribute API

Add these attributes to `CrestCreates.Metadata.Abstractions`:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CanonicalHashUnionProfileAttribute : Attribute
{
    public required Type TargetType { get; init; }
    public required string Discriminator { get; init; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CanonicalHashUnionCaseAttribute : Attribute
{
    public CanonicalHashUnionCaseAttribute(Type caseType, string discriminatorValue)
    {
        CaseType = caseType;
        DiscriminatorValue = discriminatorValue;
    }

    public Type CaseType { get; }
    public string DiscriminatorValue { get; }
    public required Type ValueProfile { get; init; }
}
```

Example:

```csharp
[CanonicalHashUnionProfile(
    TargetType = typeof(InteractionTarget),
    Discriminator = "Kind")]
[CanonicalHashUnionCase(
    typeof(CapabilityTarget),
    "Capability",
    ValueProfile = typeof(CapabilityTargetCanonicalHashProfile))]
[CanonicalHashUnionCase(
    typeof(HumanTaskTarget),
    "HumanTask",
    ValueProfile = typeof(HumanTaskTargetCanonicalHashProfile))]
[CanonicalHashUnionCase(
    typeof(SubWorkflowTarget),
    "Workflow",
    ValueProfile = typeof(SubWorkflowTargetCanonicalHashProfile))]
internal sealed class InteractionTargetCanonicalHashProfile
{
}
```

Each case uses an ordinary sub-structure profile:

```csharp
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CapabilityTarget),
    ContractShapeVersion = "capability-target-contract-hash-v1",
    DefinitionShapeVersion = "capability-target-definition-hash-v1")]
internal sealed class CapabilityTargetCanonicalHashProfile
{
    [CanonicalHashField(
        nameof(CapabilityTarget.Capability),
        CanonicalHashFieldClassification.Contract,
        Order = 0,
        ValueProfile = typeof(VersionedDescriptorRefBaseCanonicalHashProfile))]
    private static void Fields() { }
}
```

### 4.2 Field Usage

`WorkflowStep.Target` changes from `CustomWriter` to a union `ValueProfile`:

```csharp
[CanonicalHashField(
    nameof(WorkflowStep.Target),
    CanonicalHashFieldClassification.Contract,
    Order = 10,
    ValueProfile = typeof(InteractionTargetCanonicalHashProfile))]
```

The generator must allow `ValueProfile` and `ElementProfile` to point either to a normal canonical hash profile or a union profile.

### 4.3 Generated JSON Shape

The generated union writer uses wrapper shape:

```json
{
  "Kind": "Capability",
  "Value": {
    "Capability": {
      "Id": "cap-1",
      "Version": 1
    }
  }
}
```

The discriminator belongs to the union wrapper. The case payload belongs to the case profile.

This is a breaking shape change from the v1 hand-written `{ Kind, Id, Version }` writer.

### 4.4 Generated Writer Shape

The SG generates:

```csharp
internal static class InteractionTargetCanonicalHashWriter
{
    public static void WriteContractPayload(Utf8JsonWriter w, InteractionTarget target)
    {
        switch (target)
        {
            case CapabilityTarget value:
                w.WriteStartObject();
                w.WriteString("Kind", "Capability");
                w.WritePropertyName("Value");
                CapabilityTargetCanonicalHashWriter.WriteContractPayload(w, value);
                w.WriteEndObject();
                break;

            case HumanTaskTarget value:
                // same pattern
                break;

            case SubWorkflowTarget value:
                // same pattern
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown InteractionTarget subtype: {target.GetType().Name}");
        }
    }
}
```

The runtime default branch is still present as a defensive guard, but correctness is enforced by compile-time diagnostics.

### 4.5 Diagnostics

Add diagnostics:

| Code | Severity | Condition |
|------|----------|-----------|
| CCHASH015 | Error | Union profile missing `TargetType` or `Discriminator` |
| CCHASH016 | Error | Union case type is not assignable to union target type |
| CCHASH017 | Error | Union case missing `ValueProfile` |
| CCHASH018 | Error | Duplicate discriminator value |
| CCHASH019 | Error | Duplicate case type |
| CCHASH020 | Error | Union case type is not sealed |
| CCHASH021 | Error | Known direct sealed subtype of union target is not declared as a case |
| CCHASH022 | Error | Case `ValueProfile.TargetType` does not match union case type |
| CCHASH023 | Error | `CustomWriter` is unsupported in v2 canonical hash profiles |

For exhaustiveness, the generator scans compilation symbols for sealed types whose `BaseType` chain reaches `TargetType`. v2 does not provide a non-exhaustive escape hatch.

### 4.6 Migration

1. Add union profile attributes.
2. Extend the CanonicalHash generator model to parse normal profiles and union profiles.
3. Generate union writers and allow union profiles as `ValueProfile` / `ElementProfile`.
4. Add `InteractionTargetCanonicalHashProfile`.
5. Add case profiles for `CapabilityTarget`, `HumanTaskTarget`, and `SubWorkflowTarget`.
6. Replace `WorkflowStep.Target` `CustomWriter` with `ValueProfile`.
7. Remove `InteractionTargetCanonicalHashWriter`.
8. Remove or obsolete `CanonicalHashFieldAttribute.CustomWriter`; v2 SG must report CCHASH023 when it is used.

## 5. Schema Compatibility Projection v2

### 5.1 Core Decision

Schema hash semantics are split:

```text
ContractHash
    required consumer contract surface fingerprint

DefinitionHash
    full current descriptor definition fingerprint

CompatibilityAnalyzer
    old/new direction-aware semantic diff
```

`ContractHash` no longer tries to answer whether a migration is breaking. Compatibility remains the responsibility of descriptor-specific rules that compare old and new descriptors.

### 5.2 Schema ContractHash v2 Shape

`SchemaDescriptor.Fields` should no longer include every field in `ContractHash`. ContractHash includes required fields only:

```csharp
[CanonicalHashField(
    nameof(SchemaDescriptor.Fields),
    CanonicalHashFieldClassification.Contract,
    Order = 10,
    ElementProfile = typeof(SchemaRequiredFieldCanonicalHashProfile),
    CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
    OrderByProperty = nameof(SchemaFieldDescriptor.Name),
    Filter = typeof(RequiredSchemaFieldCanonicalHashFilter))]
```

Add `Filter` to `CanonicalHashFieldAttribute`:

```csharp
public Type? Filter { get; init; }
```

Filter types provide a static include method:

```csharp
internal static class RequiredSchemaFieldCanonicalHashFilter
{
    public static bool Include(SchemaFieldDescriptor field) => field.IsRequired;
}
```

For collection fields, the SG applies the filter before ordering:

```csharp
foreach (var item in d.Fields
    .Where(RequiredSchemaFieldCanonicalHashFilter.Include)
    .OrderBy(x => x.Name, StringComparer.Ordinal))
{
    SchemaRequiredFieldCanonicalHashWriter.WriteContractPayload(w, item);
}
```

Filter support is a general profile capability, but v2 should introduce it only for collection fields. Non-collection filter support is out of scope.

Filter diagnostics:

| Code | Severity | Condition |
|------|----------|-----------|
| CCHASH024 | Error | `Filter` is set on a non-collection field |
| CCHASH025 | Error | Filter type does not expose `public` or `internal static bool Include(TElement value)` |
| CCHASH026 | Error | Filter input type does not match the collection element type |

### 5.3 Schema Required Field Profile

Add a required-field contract profile:

```csharp
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(SchemaFieldDescriptor),
    ContractShapeVersion = "schema-required-field-contract-hash-v1",
    DefinitionShapeVersion = "schema-required-field-definition-hash-v1")]
internal sealed class SchemaRequiredFieldCanonicalHashProfile
{
    [CanonicalHashField(nameof(SchemaFieldDescriptor.Name), Contract, Order = 0)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.FieldType), Contract, Order = 1)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsRequired), Contract, Order = 2)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsNullable), Contract, Order = 3)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.IsCollection), Contract, Order = 4)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.CollectionElementType), Contract, Order = 5)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MaxLength), Contract, Order = 6)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MinLength), Contract, Order = 7)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MaxValue), Contract, Order = 8)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.MinValue), Contract, Order = 9)]
    [CanonicalHashField(nameof(SchemaFieldDescriptor.Pattern), Contract, Order = 10)]
    private static void Fields() { }
}
```

`DefinitionHash` still includes all schema fields through the full `SchemaFieldCanonicalHashProfile`.

### 5.4 Shape Versions

Bump schema shape versions:

```text
schema-contract-hash-v2
schema-definition-hash-v2
schema-required-field-contract-hash-v1
schema-field-definition-hash-v2
```

Exact names may be adjusted during implementation, but the contract shape version must change because optional fields are removed from the contract payload.

## 6. ChangeSet v2

### 6.1 New Change Kind

Add `DefinitionHashChanged`:

```csharp
public enum DescriptorChangeKind
{
    Added,
    Removed,
    Deprecated,
    Activated,
    StateChanged,
    ContractHashChanged,
    DefinitionHashChanged,
    Updated
}
```

This change kind preserves visibility into definition-only changes after they stop changing ContractHash.

### 6.2 DescriptorChange Hash Metadata

Extend `DescriptorChange`:

```csharp
public string? BeforeDefinitionHash { get; init; }
public string? AfterDefinitionHash { get; init; }
```

`BeforeContractHash` / `AfterContractHash` remain.

### 6.3 Builder Rules

`DescriptorChangeSetBuilder` order:

1. State/removal/lifecycle transitions.
2. `ContractHashChanged` when `ContractHash` differs.
3. `DefinitionHashChanged` when `ContractHash` is equal but `DefinitionHash` differs.
4. `Updated` when both hashes are equal but name changed.

Priority:

```text
Removed
Deprecated
StateChanged
ContractHashChanged
DefinitionHashChanged
Updated
Added
Activated
```

The change set must populate all available before/after hash values for the emitted change.

## 7. Compatibility v2

### 7.1 Schema Rule Entry

`SchemaCompatibilityRule.CanAnalyze` must include the new change kind:

```csharp
return change.Kind is (
        DescriptorChangeKind.ContractHashChanged
        or DescriptorChangeKind.DefinitionHashChanged
        or DescriptorChangeKind.Updated)
    && (after is SchemaDescriptor || before is SchemaDescriptor);
```

The existing old/new semantic diff remains the authority for field-level outcomes.

### 7.2 Required Outcomes

| Change | Hash outcome | Change kind | Compatibility outcome |
|--------|--------------|-------------|-----------------------|
| Optional field added | Contract same, Definition changed | DefinitionHashChanged | Compatible |
| Required field added | Contract changed | ContractHashChanged | Breaking |
| Optional field removed | Definition changed | DefinitionHashChanged | Risky without affected consumers; Breaking with affected consumers |
| Optional field type changed | Definition changed | DefinitionHashChanged | Breaking |
| Required field type changed | Contract changed | ContractHashChanged | Breaking |
| Required relaxed to optional | Contract changed | ContractHashChanged | Compatible |
| Optional validation/display change | Definition changed | DefinitionHashChanged | Risky until rule categories are modeled |

### 7.3 Generic Rule

Add generic fallback behavior:

```text
DefinitionHashChanged + descriptor-specific findings
    -> use descriptor-specific findings

DefinitionHashChanged + no descriptor-specific findings
    -> Risky
```

Defaulting to `Risky` avoids silently approving definition-shape changes for descriptor kinds that have no semantic rule.

## 8. Tests

### 8.1 Union Profile Tests

Add generator tests under `tests/Tooling/CrestCreates.CodeGenerator.Tests`:

- Missing union profile target/discriminator emits `CCHASH015`.
- Non-assignable case emits `CCHASH016`.
- Missing case value profile emits `CCHASH017`.
- Duplicate discriminator emits `CCHASH018`.
- Duplicate case type emits `CCHASH019`.
- Non-sealed case emits `CCHASH020`.
- Missing direct sealed subtype emits `CCHASH021`.
- ValueProfile target mismatch emits `CCHASH022`.
- `CustomWriter` usage emits `CCHASH023`.
- Filter on non-collection field emits `CCHASH024`.
- Invalid filter signature emits `CCHASH025`.
- Filter element type mismatch emits `CCHASH026`.

Add runtime hash tests:

- Changing `WorkflowStep.Target` kind changes ContractHash.
- Changing target id/version changes ContractHash.
- Reordering workflow steps still changes ContractHash.
- Union case JSON shape is deterministic and includes discriminator before value.

### 8.2 Schema Hash Tests

- Optional field addition does not change ContractHash.
- Optional field addition changes DefinitionHash.
- Required field addition changes ContractHash.
- Optional field type change does not change ContractHash but changes DefinitionHash.
- Validation rule change changes DefinitionHash only.

### 8.3 ChangeSet Tests

- Optional field addition emits `DefinitionHashChanged`.
- Required field addition emits `ContractHashChanged`.
- Name-only change emits `Updated`.
- State change beats `DefinitionHashChanged`.
- `ContractHashChanged` beats `DefinitionHashChanged`.
- Definition hash values are populated on `DescriptorChange`.

### 8.4 Compatibility Tests

- Optional field addition with `DefinitionHashChanged` is Compatible.
- Optional field removal with affected consumers is Breaking.
- Optional field removal without affected consumers is Risky.
- Optional field type change is Breaking.
- Validation rule change is Risky until categories are modeled.
- Generic `DefinitionHashChanged` without descriptor-specific findings is Risky.

## 9. Non-goals

- Do not implement non-exhaustive union profiles.
- Do not use reflection to discover union cases at runtime.
- Do not add tenant-customizable hash policy.
- Do not migrate package/review/report hashes in this spec.
- Do not SG-generate compatibility analyzer rules.
- Do not preserve v1 hash values.

## 10. Implementation Order

1. Add union profile attributes and generator models.
2. Add union diagnostics and tests.
3. Generate union writers and migrate `InteractionTarget`.
4. Remove `CustomWriter` from active profile path.
5. Add collection filter support and diagnostics.
6. Migrate Schema contract/definition profiles to v2 shape.
7. Add `DefinitionHashChanged` and definition hash metadata to change-set model.
8. Update compatibility analyzer and generic rule.
9. Update governance/change-set consistency tests.
10. Update `docs/Feature/CanonicalHash/arch-design.md` and `usage-guide.md`.

## 11. Acceptance Criteria

- `WorkflowStep.Target` is represented by a declared union profile, not a hand-written `CustomWriter`.
- SG emits exhaustive union writer switches and compile-time diagnostics for missing cases.
- `CustomWriter` is rejected by v2 canonical hash generator diagnostics.
- Optional schema field addition does not change `ContractHash`.
- Optional schema field addition changes `DefinitionHash`.
- Optional schema field addition still appears in change-set and compatibility/governance flow as `DefinitionHashChanged`.
- Schema compatibility outcomes are produced by old/new semantic diff, not inferred from hash value alone.
- Existing canonical hash AOT guarantees remain: no `JsonSerializer`, no `JsonTypeInfo`, no runtime reflection on the hash main path.
