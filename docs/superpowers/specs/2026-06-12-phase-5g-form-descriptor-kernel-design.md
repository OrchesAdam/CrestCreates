# Phase 5g — Form Descriptor Kernel Design Spec

**Date**: 2026-06-12
**Status**: Approved
**Implementation Order**: 1. FormFieldDescriptor → 2. FormDescriptorValidator → 3. AddFormKernel → 4. FormSchemaBindingValidator → 5. FormDescriptorDependencyExtractor → 6. DescriptorHashComputer → 7. CodeGenerator removal → 8. Tests & regression
**Parent Issue**: [#3 — Phase 5g: Form Descriptor Kernel](https://github.com/OrchesAdam/CrestCreates/issues/3)

---

## 1. Overview

Phase 5g elevates the existing `FormDescriptor` from "a POCO that can be registered" to a **fully validatable, dependency-aware descriptor node** in the Descriptor-first architecture. It does NOT introduce UI rendering, form submission runtime, or database persistence.

### The Kernel Loop After Phase 5g

```
SchemaRegistry (built first)
     │
     ▼
FormDescriptorValidator (during FormRegistry.Build)
     │
     ▼
FormRegistry (built)
     │
     ▼
FormSchemaBindingValidator (onFormBuilt callback, validates field→Schema parity)
     │
     ▼
FormDescriptorDependencyExtractor (Form→Schema dependency edge)
     │
     ▼
DescriptorHashComputer (updated contract hash)
     │
     ▼
HumanTaskDescriptor.Interaction → Form (already works, unchanged)
```

### Design Principles

1. **Form is UI metadata, not business action** — answers "how should this Schema be displayed/collected?", not "who executes, where does data go?"
2. **Form does not depend on HumanTask** — dependency direction: `Schema.Abstractions ← Form.Abstractions ← HumanTask.Abstractions`
3. **Schema is the data contract truth** — Form field metadata (required, control type, format) is presentation-only; never alters `SchemaFieldDescriptor`
4. **AoT-friendly** — zero runtime reflection, zero script engine, zero dynamic expression execution, zero JSON clone
5. **Descriptor-only** — all new metadata is stored as immutable init-only properties; no runtime behavior or side effects

---

## 2. Current State vs. Target

### Already Exists (pre-5g)

| Component | Location | Status |
|---|---|---|
| `FormDescriptor : IInteractionDescriptor` | `CrestCreates.Form.Abstractions` | ✅ 8 fields + Schema ref + Fields list + LayoutColumns |
| `FormFieldDescriptor` | `CrestCreates.Form.Abstractions` | ✅ 9 fields (SchemaFieldName, Label, Placeholder, HelpText, FormatHint, Order, Group, IsReadOnly, VisibilityCondition) |
| `IFormRegistry : IVersionedDescriptorRegistry<FormDescriptor>` | `CrestCreates.Form.Abstractions` | ✅ |
| `IFormDescriptorProvider` | `CrestCreates.Form.Abstractions` | ✅ Single method `GetFormDescriptor()` |
| `FormRegistry : RegistryBase<FormDescriptor>` | `CrestCreates.Form` | ✅ Build/GetById/GetByName/GetActiveVersion/GetLatestVersion |
| `MetadataBootstrapper.BuildAll()` | `CrestCreates.Metadata` | ✅ Builds `IFormRegistry` (no validators) |
| `DescriptorRefValidator` | `CrestCreates.Metadata` | ✅ Validates `FormDescriptor.Schema` ref exists |
| `DescriptorHashComputer` | `CrestCreates.Metadata` | ✅ Contract hash excludes Label/Placeholder/HelpText/VisibilityCondition/FormatHint |
| CodeGenerator `IFormDescriptorProvider` discovery | `CrestCreates.CodeGenerator` | ✅ Discovers providers and generates wrapper — but ID uses `Guid.NewGuid()` |

### Gaps to Fill

| Gap | Phase 5g Action |
|---|---|
| No Form-specific validation | ➕ `FormDescriptorValidator : IRegistryValidator<FormDescriptor>` |
| No field→Schema binding validation | ➕ `FormSchemaBindingValidator` (standalone, post-build callback) |
| No Form→Schema dependency edge | ➕ `FormDescriptorDependencyExtractor` |
| FormFieldDescriptor lacks interaction metadata | ➕ 6 new properties (ControlType, IsRequiredOverride, ValidationMessage, DefaultValueExpression, OptionsSource, Metadata) |
| Unstable code-generated Form IDs | 🔧 Replace `Guid.NewGuid()` with deterministic hash |
| Contract hash doesn't cover new interaction fields | 🔧 Add ControlType/IsRequiredOverride/OptionsSource to contract hash |

---

## 3. FormFieldDescriptor Extension

**File**: `framework/src/CrestCreates.Form.Abstractions/FormFieldDescriptor.cs`

### New Properties

```csharp
public sealed class FormFieldDescriptor
{
    // === Existing (unchanged) ===
    public string SchemaFieldName { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? Placeholder { get; init; }
    public string? HelpText { get; init; }
    public string? FormatHint { get; init; }
    public int Order { get; init; }
    public string? Group { get; init; }
    public bool IsReadOnly { get; init; }
    public string? VisibilityCondition { get; init; }

    // === New ===
    public string? ControlType { get; init; }
    public bool? IsRequiredOverride { get; init; }
    public string? ValidationMessage { get; init; }
    public string? DefaultValueExpression { get; init; }
    public string? OptionsSource { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
```

### Property Semantics

| Property | Type | Purpose | Phase 5g Behavior |
|---|---|---|---|
| `ControlType` | `string?` | UI widget hint (text, select, date, file, textarea, number, checkbox…) | Stored as hint string. No enum, no component registry, no binding. |
| `IsRequiredOverride` | `bool?` | Form-layer required marker, independent of Schema | Does NOT alter `SchemaFieldDescriptor.IsRequired`. Purely presentation-level. |
| `ValidationMessage` | `string?` | Custom error display text for this field | Display metadata only. Does not execute or replace Schema validation. |
| `DefaultValueExpression` | `string?` | Expression for initial value (e.g. `"today()"`) | Stored as opaque string. **Never executed** in Phase 5g. |
| `OptionsSource` | `string?` | Option source identifier (e.g. `"static:us_states"`, `"lookup:departments"`) | Stored as opaque string. **Never resolved** in Phase 5g. |
| `Metadata` | `IReadOnlyDictionary<string, string>` | Extension bag for future metadata | Keys and values must be strings. No object graph. No JSON parsing. No reflection. |

### Metadata Hash Ordering Rules

`IReadOnlyDictionary<string, string>` is purely an interface — it does NOT prevent the caller from retaining a mutable `Dictionary<string, string>` reference and mutating it after the descriptor is constructed. Phase 5g does **not** provide descriptor deep immutability. The requirements are:

1. **Descriptor construction**: The `init` setter accepts any `IReadOnlyDictionary<string, string>`. The descriptor is init-only metadata — once constructed, the reference is assumed stable.
2. **Hash computation**: `DescriptorHashComputer` reads `Metadata` as a point-in-time snapshot — it sorts entries by key before serialization so that insertion order does not affect the hash.
3. **Validator**: `FormDescriptorValidator` treats `Metadata` as read-only opaque data — no validation of keys or values beyond null checks.
4. **Provider contract**: Providers MUST NOT mutate descriptors (or their `Metadata` dictionaries) after returning them from `GetFormDescriptor()` / `GetDescriptors()`. The registry does not clone descriptors and will not detect post-build mutation.

**Important**: This is NOT a "snapshot contract" — it is hash ordering discipline. Phase 5g does NOT provide registry-level defense against mutable dictionary mutation. Should a future phase require defense-in-depth, switch the default to `ImmutableDictionary<string, string>.Empty` and add a clone step in `BuildSnapshot()`.

**Test requirement**: `Metadata_InsertionOrder_DoesNotAffect_ContractHash` — two descriptors with identical key-value pairs in different insertion order produce the same contract hash.

### What Stays the Same

- All 9 existing properties unchanged
- `Label`/`Placeholder`/`HelpText` remain cosmetic (excluded from contract hash)
- `VisibilityCondition`/`DefaultValueExpression` remain opaque strings (no parsing, no evaluation)
- No executable delegates, expression trees, scripts, or frontend component type dependencies

---

## 4. FormDescriptorValidator

**File**: `framework/src/CrestCreates.Form/FormDescriptorValidator.cs` (new)

### Interface

```csharp
public sealed class FormDescriptorValidator : IRegistryValidator<FormDescriptor>
{
    public int Order => 10;
    public ValidationReport Validate(IReadOnlyList<FormDescriptor> descriptors);
}
```

### Validation Rules

| # | Rule | Severity | Detail |
|---|---|---|---|
| 1 | `Id` is null or whitespace | Error | Every descriptor must have a stable identity |
| 2 | `Name` is null or whitespace | Error | Required for registry lookup |
| 3 | `Version <= 0` | Error | Must be positive integer |
| 4 | `Schema.Id` null/whitespace, or `Schema.Version <= 0` | Error | Must reference valid Schema by id+version |
| 5 | `Fields` is null | Error | Must use `Array.Empty<FormFieldDescriptor>()`, not null |
| 6 | Any field's `SchemaFieldName` is null or whitespace | Error | Every field must bind to a Schema field name |
| 7 | Duplicate `SchemaFieldName` within same Form | Error | Ambiguous binding — two fields can't target the same Schema field |
| 8 | `ControlType` is whitespace-only (non-empty whitespace) | Error | If provided, must have content; null is OK |
| 9 | Duplicate `Order` values | (none) | Allowed. Stable sort uses `Order` then `SchemaFieldName`. |

### What It Does NOT Validate

- Whether `SchemaFieldName` exists in the referenced Schema → delegated to `FormSchemaBindingValidator`
- Whether Schema required fields appear in Form → delegated to `FormSchemaBindingValidator`
- Schema version resolution against Schema registry → delegated to post-build validation
- `VisibilityCondition`/`DefaultValueExpression` syntax → stored as opaque strings
- `OptionsSource` format → stored as opaque string

### Registration

A new `FormServiceCollectionExtensions.AddFormKernel()` method consolidates all Form DI registrations into one entry point:

**File**: `framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs` (new)

```csharp
public static class FormServiceCollectionExtensions
{
    public static IServiceCollection AddFormKernel(this IServiceCollection services)
    {
        // Registry (singleton — holds built snapshot)
        services.TryAddSingleton<IFormRegistry, FormRegistry>();

        // Validation engine (singleton — consumed by singleton FormRegistry)
        // MUST be singleton to avoid captive dependency: FormRegistry (singleton)
        // cannot depend on scoped services.
        services.TryAddSingleton<IRegistryValidationEngine<FormDescriptor>,
            RegistryValidationEngine<FormDescriptor>>();

        // Validators (singleton — stateless, consumed by singleton engine)
        services.TryAddSingleton<IRegistryValidator<FormDescriptor>,
            FormDescriptorValidator>();

        // Schema binding validator (singleton — stateless, used via onFormBuilt callback)
        services.TryAddSingleton<FormSchemaBindingValidator>();

        return services;
    }
}
```

Key decisions:
- **Everything is Singleton** — all components are stateless. `FormRegistry` captures the validation engine at construction time and never changes. Making validators Scoped while the registry is Singleton creates a captive dependency (scoped instance captured by singleton) which fails DI scope validation.
- `TryAdd*` — never overrides consumer registrations
- `FormSchemaBindingValidator` as Singleton — stateless, resolved once for the `onFormBuilt` callback

Consumer usage:
```csharp
services.AddFormKernel();

// In bootstrap:
var schemaBindingValidator = serviceProvider.GetRequiredService<FormSchemaBindingValidator>();
MetadataBootstrapper.BuildAll(
    schemaRegistry, formRegistry, humanTaskRegistry, workflowRegistry, eventRegistry,
    onFormBuilt: (forms, schema) =>
    {
        var report = schemaBindingValidator.Validate(forms, schema);
        if (report.HasErrors) throw new InvalidOperationException(...);
    });
```

---

## 5. FormSchemaBindingValidator

**File**: `framework/src/CrestCreates.Form/FormSchemaBindingValidator.cs` (new)

### Why Not IRegistryValidator

`FormRegistry.Build()` runs validators during construction. At that point, `ISchemaRegistry` may not yet be stable. To avoid circular build-order dependencies, `FormSchemaBindingValidator` is a **standalone service** invoked as a post-build callback after both registries are built.

### Interface

```csharp
public sealed class FormSchemaBindingValidator
{
    public ValidationReport Validate(
        IReadOnlyList<FormDescriptor> forms,
        ISchemaRegistry schemaRegistry);
}
```

### Validation Rules

| # | Rule | Severity | Detail |
|---|---|---|---|
| 1 | Referenced Schema does not exist in `ISchemaRegistry` | Error | `schemaRegistry.GetByVersion(form.Schema.Id, form.Schema.Version)` returns null |
| 2 | Schema version not found, but a different version exists for the same Id | Error | `GetByVersion` returns null but `GetById` returns a descriptor — the requested version is unavailable |
| 3 | `SchemaFieldName` not found in the **requested version** of Schema's `Fields[].Name` | Error | Form declares a field the requested Schema version does not have |
| 4 | Schema field with `IsRequired = true` not present in Form | Warning | Might be intentional (system field, hidden field, computed field) |

**Critical: Use `GetByVersion`, NOT `GetById`**. `GetById()` returns the latest version of a descriptor, which may differ from the version the Form requests. Example: Schema v1 has fields `[A, B]`; Schema v2 removes `B` and adds `C`. Form requesting Schema v1 should validate against v1's fields `[A, B]`. If we used `GetById` (which returns v2 as latest), we'd wrongly reject `B` as "not in Schema".

Validation logic:
```csharp
var schema = schemaRegistry.GetByVersion(form.Schema.Id, form.Schema.Version);
if (schema == null)
{
    // Check if ANY version exists for this Id
    var latest = schemaRegistry.GetById(form.Schema.Id);
    if (latest != null)
        return Error("Schema v{form.Schema.Version} not found; latest is v{latest.Version}");
    return Error("Schema '{form.Schema.Id}' not found in registry");
}
// Validate fields against the specific requested version
foreach (var field in form.Fields)
{
    if (!schema.Fields.Any(sf => sf.Name == field.SchemaFieldName))
        return Error($"Field '{field.SchemaFieldName}' not found in Schema v{schema.Version}");
}
```

**Why rule 4 is a Warning, not an Error**: Many Schema fields marked `IsRequired` should never appear on a Form — system-generated IDs, audit fields (`CreatedBy`, `CreationTime`), computed fields, or fields that are auto-populated by backend logic.

### Activation in MetadataBootstrapper

**File**: `framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs`

```csharp
public static void BuildAll(
    ISchemaRegistry schemaRegistry,
    IFormRegistry formRegistry,
    IHumanTaskRegistry humanTaskRegistry,
    IWorkflowRegistry workflowRegistry,
    IEventRegistry eventRegistry,
    Action<IReadOnlyList<FormDescriptor>, ISchemaRegistry>? onFormBuilt = null,
    Action<IReadOnlyList<WorkflowDescriptor>>? onWorkflowBuilt = null)
{
    schemaRegistry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
    formRegistry.Build(DescriptorProviderRegistry.GetProviders<FormDescriptor>());

    // Post-build: Form→Schema binding validation
    onFormBuilt?.Invoke(formRegistry.GetAll(), schemaRegistry);

    humanTaskRegistry.Build(DescriptorProviderRegistry.GetProviders<HumanTaskDescriptor>());
    workflowRegistry.Build(DescriptorProviderRegistry.GetProviders<WorkflowDescriptor>());
    eventRegistry.Build(DescriptorProviderRegistry.GetProviders<GeneratedEventDescriptor>());

    onWorkflowBuilt?.Invoke(workflowRegistry.GetAll());
}
```

Consumer wires it:
```csharp
var schemaBindingValidator = new FormSchemaBindingValidator();

MetadataBootstrapper.BuildAll(
    schemaRegistry, formRegistry, humanTaskRegistry, workflowRegistry, eventRegistry,
    onFormBuilt: (forms, schema) =>
    {
        var report = schemaBindingValidator.Validate(forms, schema);
        if (report.HasErrors)
            throw new InvalidOperationException(
                "Form→Schema binding validation failed: " +
                string.Join("; ", report.Issues.Where(i => i.Severity == ValidationSeverity.Error)
                    .Select(i => i.Message)));
    });
```

---

## 6. FormDescriptorDependencyExtractor

**File**: `framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs` (new)

### Interface

```csharp
public static class FormDescriptorDependencyExtractor
{
    public static IReadOnlyList<DependencyEdge> Extract(FormDescriptor descriptor)
    {
        var edges = new List<DependencyEdge>
        {
            new()
            {
                SourceId = descriptor.Id,
                TargetId = descriptor.Schema.Id,
                Kind = DescriptorDependencyKind.Uses
            }
        };
        return edges;
    }
}
```

### Design

- **Single edge per Form**: `Form → Schema` with `Kind = Uses`
- Output feeds into `DependencyGraphProvider.RegisterEdge()` (already exists in `CrestCreates.Metadata`)
- No full topology engine, no cycle analysis, no graph persistence — deferred to future phases
- **Known limitation**: `DependencyEdge.SourceId`/`TargetId` are simplified descriptor IDs without namespace prefix (e.g., `"form_01"`, not `"form:form_01"`). Two descriptors from different namespaces with the same local ID could collide in the graph. Namespace-aware graph identity is deferred to the Descriptor Topology phase.
- Consumer iterates all Forms and registers edges during bootstrap:

```csharp
foreach (var form in formRegistry.GetAll())
{
    var edges = FormDescriptorDependencyExtractor.Extract(form);
    foreach (var edge in edges)
        DependencyGraphProvider.RegisterEdge(edge.SourceId, edge.TargetId, edge.Kind);
}
```

### Resulting Graph

```
Schema ←──[Uses]── Form ←──[References]── HumanTask
```

HumanTask's reference to Form via `IInteractionDescriptor` is NOT duplicated here — that edge is managed by HumanTask's own extraction path.

---

## 7. CodeGenerator — Disable Form Auto-Generation Branch

### Problem: Generated Form Descriptors Are Invalid

**File**: `framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs`

The current `GetFormProviderInfo()` (line 178) discovers types implementing `IFormDescriptorProvider` and captures only the type name. `GenerateRegistries` then emits a `GeneratedFormProvider : IDescriptorProvider<FormDescriptor>` that constructs `new FormDescriptor` **directly** — it does NOT call the provider's `GetFormDescriptor()`:

```csharp
// What the generator ACTUALLY emits today:
new FormDescriptor
{
    Id = $"form_{Guid.NewGuid():N}",                         // unstable
    Name = "SomeNamespace.SomeProvider",                     // qualified type name
    Version = 0,                                             // INVALID — would fail validator
    Schema = new VersionedDescriptorRef<SchemaDescriptor>("", 0),  // INVALID
    // Fields = empty (no field data captured)
}
```

This produces invalid descriptors with `Version = 0`, empty `Schema.Id`, and zero fields. With Phase 5g's new `FormDescriptorValidator`, these would be **rejected at build time** — the generated provider path is fundamentally incompatible with validated descriptors.

### Decision: Disable the Form Auto-Generation Branch

**Phase 5g MUST disable this branch.** The generator was written under the assumption that it could fabricate descriptor content from type metadata, but Form descriptors require Schema references and field metadata that cannot be extracted from a provider type's signature alone.

**What to do in `SchemaCapabilitySourceGenerator.cs`**:

1. **Remove `GetFormProviderInfo()`** — the `SyntaxProvider.CreateSyntaxProvider` predicate that matches `IFormDescriptorProvider` types no longer captures Form descriptors.
2. **Remove `forms` from `GenerateRegistries()`** — the method's `ImmutableArray<FormDescriptorInfo?> forms` parameter and the entire `if (hasForm && forms.Any(...))` block (lines 331–367) must be removed.
3. **Remove `using CrestCreates.Form.Abstractions;`** — the conditional `using` at line 284–285 is no longer needed.

**What stays**:
- `IFormDescriptorProvider` interface in `CrestCreates.Form.Abstractions` — hand-written providers remain fully supported
- `DescriptorProviderRegistry.GetProviders<FormDescriptor>()` — still discovers providers via DI
- `MetadataBootstrapper.BuildAll()` — still builds `IFormRegistry` from providers
- Other descriptor types in the same source generator (Schema, Capability, HumanTask, Workflow, Event) are **untouched**

### Why This Is the Right Call

1. **No valid path today**: Even with a deterministic ID, the generated descriptor has `Version=0` and empty `Schema` — it would fail the `FormDescriptorValidator`.
2. **Generator doesn't call `GetFormDescriptor()`**: Making it instantiate and call the provider would require parameterless construction (new constraint), adding coupling.
3. **Form content belongs to providers**: Form field metadata (ControlType, Label, Order, SchemaFieldName bindings) must be hand-authored. No amount of code generation can fabricate meaningful Form content from type signatures.
4. **Aligns with Schema precedent**: Schema descriptors are NOT auto-generated from provider types either — they come from hand-written providers. This keeps the generator's role consistent across descriptor types.

### Result

After this change, the only path for Form descriptors into the registry is:

```
Hand-written IFormDescriptorProvider
  → DescriptorProviderRegistry.GetProviders<FormDescriptor>()
    → FormRegistry.Build() with FormDescriptorValidator
      → onFormBuilt → FormSchemaBindingValidator
```

No auto-generated wrapper. No invalid descriptors slipping through.

---

## 8. DescriptorHashComputer Update

**File**: `framework/src/CrestCreates.Metadata/DescriptorHashComputer.cs`, lines 90–105

### Contract Hash — Current (pre-5g)

```csharp
FormDescriptor f => new
{
    f.Id, f.Name, f.Version, f.State, f.SupersededById,
    Schema = new { f.Schema.Id, f.Schema.Version },
    Fields = f.Fields.Select(fd => new
    {
        fd.SchemaFieldName,
        fd.IsReadOnly,
        fd.Order,
        fd.Group
    }).OrderBy(fd => fd.SchemaFieldName).ToArray()
},
```

### Contract Hash — Updated (post-5g)

```csharp
FormDescriptor f => new
{
    f.Id, f.Name, f.Version, f.State, f.SupersededById,
    Schema = new { f.Schema.Id, f.Schema.Version },
    Fields = f.Fields.Select(fd => new
    {
        fd.SchemaFieldName,
        fd.IsReadOnly,
        fd.Order,
        fd.Group,
        fd.ControlType,        // NEW — changes interaction contract
        fd.IsRequiredOverride,  // NEW — changes interaction contract
        fd.OptionsSource        // NEW — changes interaction contract
    }).OrderBy(fd => fd.SchemaFieldName).ToArray()
},
```

### Contract vs. Definition Hash Classification

| Field | Contract Hash | Definition Hash | Rationale |
|---|---|---|---|
| `Schema.Id` / `Schema.Version` | ✅ | ✅ | Core reference |
| `SchemaFieldName` | ✅ | ✅ | Core binding |
| `IsReadOnly` | ✅ | ✅ | Affects interaction |
| `Order` | ✅ | ✅ | Affects interaction |
| `Group` | ✅ | ✅ | Affects interaction |
| `ControlType` | ✅ **NEW** | ✅ | Changing widget type alters interaction |
| `IsRequiredOverride` | ✅ **NEW** | ✅ | Changing mandatory status alters interaction |
| `OptionsSource` | ✅ **NEW** | ✅ | Changing option source alters interaction |
| `Label` | ❌ | ✅ | Cosmetic |
| `Placeholder` | ❌ | ✅ | Cosmetic |
| `HelpText` | ❌ | ✅ | Cosmetic |
| `FormatHint` | ❌ | ✅ | Cosmetic |
| `ValidationMessage` | ❌ | ✅ | Cosmetic display text |
| `VisibilityCondition` | ❌ | ✅ | Deferred — not executed in Phase 5g |
| `DefaultValueExpression` | ❌ | ✅ | Deferred — not executed in Phase 5g |
| `Metadata` | ❌ | ✅ | Extension bag |

### Definition Hash

No code change needed — `ComputeDefinitionHash()` serializes the entire descriptor via `JsonSerializer.Serialize(descriptor, descriptor.GetType(), ...)`, so all new properties are automatically included.

---

## 9. Testing

### Test File Map

| Test File | Type | What It Covers |
|---|---|---|
| `FormDescriptorTests.cs` | Extended | New field defaults, Metadata snapshot hash |
| `FormDescriptorValidatorTests.cs` | New | 9 validation rules (Section 4) |
| `FormSchemaBindingValidatorTests.cs` | New | 7 binding rules (Section 5) |
| `FormRegistryTests.cs` | Extended | Build-with-validator, validation failure on Build |
| `FormDescriptorDependencyExtractorTests.cs` | New | Edge extraction (Section 6) |
| `DescriptorHashComputerTests.cs` *(Metadata.Tests)* | Extended | Contract hash for new fields |

### Detailed Test Cases

#### A. FormFieldDescriptor (2 tests, extend existing)
- `FormFieldDescriptor_Defaults_Metadata_To_Empty` — new `Metadata` dict defaults to empty
- `FormFieldDescriptor_Allows_Control_Metadata_Without_Runtime_Behavior` — all 6 new fields are settable, no side effects

#### B. FormDescriptorValidator (8 tests, new file)
- `Rejects_EmptyId` — `Id = ""` → Error
- `Rejects_EmptyName` — `Name = ""` → Error
- `Rejects_NonPositiveVersion` — `Version = 0` → Error
- `Rejects_EmptySchemaRef` — `Schema.Id = ""` → Error
- `Rejects_EmptySchemaFieldName` — field with `SchemaFieldName = ""` → Error
- `Rejects_DuplicateSchemaFieldName` — two fields with same `SchemaFieldName` → Error
- `Allows_PartialSchemaCoverage` — Form covers subset of Schema fields, no error
- `Allows_DuplicateOrder` — two fields with same `Order`, no error from validator

#### C. FormSchemaBindingValidator (7 tests, new file)
- `Passes_When_AllFieldsExistInSchema` — happy path
- `Fails_When_FormFieldMissingInSchema` — field name not in Schema.Fields → Error
- `Fails_When_SchemaRefMissing` — Schema.Id not in ISchemaRegistry → Error
- `Fails_When_SchemaVersionNotFound` — Id exists but requested version is not in registry → Error
- `Warns_When_RequiredSchemaFieldNotInForm` — Schema `IsRequired=true` field absent from Form → Warning (not Error)
- `Uses_VersionedSchemaRef` — validates against the requested version, NOT latest. If Schema v2 removes a field that v1 had, Form requesting v1 should still pass.
- `Uses_GetByVersion_Not_GetById` — verifies that `ISchemaRegistry.GetByVersion()` is called, not `GetById()`

#### D. Registry/Bootstrap (3 tests, extend existing)
- `Build_Runs_FormValidator` — validation fires during `FormRegistry.Build()`
- `Build_Fails_On_ValidationError` — `Build()` throws `RegistryValidationException`
- `FormSchemaBindingValidator_With_Real_Registries` — integration: build SchemaRegistry + FormRegistry + callback

#### E. Dependency Graph (2 tests, new file)
- `FormDependencyExtractor_Adds_UsesEdge_ToSchema` — single `DependencyEdge` with `Kind = Uses`
- `Form_DoesNot_Depend_On_HumanTask` — no reverse dependency

#### F. Hash Computer (3 tests, in Metadata.Tests)
- `FormContractHash_Changes_When_ControlTypeChanges`
- `FormContractHash_Changes_When_IsRequiredOverrideChanges`
- `FormContractHash_DoesNotChange_When_ValidationMessageChanges`

#### G. Generator (1 test, in CodeGenerator.Tests)
- `GeneratedFormProvider_Is_Not_Emitted` — the generator output must NOT contain any `GeneratedFormProvider` or `new FormDescriptor` instantiation. Use a compile-time verification: provide an `IFormDescriptorProvider` implementation, run the generator, assert no Form-related code appears in the generated output.

#### H. Metadata Snapshot (1 test, in Form.Tests)
- `Metadata_InsertionOrder_DoesNotAffect_ContractHash` — two descriptors with same Metadata key-value pairs in different insertion order produce identical contract hash

### Regression Baseline

All existing tests must pass:
```bash
dotnet test framework/test/CrestCreates.Form.Tests
dotnet test framework/test/CrestCreates.Metadata.Tests
dotnet test framework/test/CrestCreates.HumanTask.Tests
dotnet test framework/test/CrestCreates.Workflow.Tests
```

---

## 10. File Scope

### New Files (7)

```
framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs
framework/src/CrestCreates.Form/FormDescriptorValidator.cs
framework/src/CrestCreates.Form/FormSchemaBindingValidator.cs
framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs
framework/test/CrestCreates.Form.Tests/FormDescriptorValidatorTests.cs
framework/test/CrestCreates.Form.Tests/FormSchemaBindingValidatorTests.cs
framework/test/CrestCreates.Form.Tests/FormDescriptorDependencyExtractorTests.cs
```

### Modified Files (7)

```
framework/src/CrestCreates.Form.Abstractions/FormFieldDescriptor.cs        # 6 new properties
framework/src/CrestCreates.Metadata/DescriptorHashComputer.cs              # 3 new contract fields
framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs                # +onFormBuilt parameter
framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/
    SchemaCapabilitySourceGenerator.cs                                     # Remove Form auto-generation branch
framework/test/CrestCreates.Form.Tests/FormDescriptorTests.cs              # 2 new tests + Metadata hash test
framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs                # 3 new tests
framework/test/CrestCreates.Metadata.Tests/DescriptorHashComputerTests.cs  # 3 new tests
```

### Never Modified

```
CrestCreates.Workflow.*
CrestCreates.HumanTask.* (runtime)
CrestCreates.Capability.*
CrestCreates.Organization.*
CrestCreates.DataPermission.*
CrestCreates.Application.*
CrestCreates.Web.*
```

---

## 11. Acceptance Criteria

Phase 5g is complete when:

- [ ] `FormDescriptor` is a validatable descriptor node — not just a POCO + registry
- [ ] `FormFieldDescriptor` carries interaction metadata (ControlType, IsRequiredOverride, etc.) without runtime behavior
- [ ] `FormDescriptorValidator` catches malformed descriptors during `FormRegistry.Build()`
- [ ] `FormSchemaBindingValidator` catches field→Schema binding errors as a post-build callback
- [ ] Form→Schema relationship is expressible as a `DependencyEdge`
- [ ] `DescriptorHashComputer` includes new contract-relevant fields
- [ ] CodeGenerator no longer emits a Form auto-generation branch (no `GeneratedFormProvider`, no `new FormDescriptor` in generated code)
- [ ] HumanTask still references Form through `IInteractionDescriptor` — no runtime binding changes
- [ ] Form does not introduce UI renderer, submission runtime, or persistence provider
- [ ] All logic is AoT-friendly: zero runtime reflection, zero script engine, zero dynamic expression execution
- [ ] All tests pass (new + existing regression suites)

### Regression Command

```bash
dotnet test framework/test/CrestCreates.Form.Tests
dotnet test framework/test/CrestCreates.Metadata.Tests
dotnet test framework/test/CrestCreates.HumanTask.Tests
dotnet test framework/test/CrestCreates.Workflow.Tests
```

---

## 12. Out of Scope (Explicitly Not Done)

- Form renderer / Form designer / low-code builder UI
- Frontend component library binding
- FormSubmission runtime
- Draft / autosave / validation state persistence
- HumanTask form binding runtime
- HumanTask claim/delegate/escalation
- Workflow step direct Form target (Workflow → Capability/HumanTask/SubWorkflow only)
- Capability Authorization changes
- DataPermission integration
- API / Controller / AppService
- Database persistence / migrations
- EF / SqlSugar / Dapper / Mongo / Redis provider
- Expression execution (`DefaultValueExpression` not evaluated)
- Dynamic visibility evaluation (`VisibilityCondition` not parsed)
- JavaScript/C# script engine
- Localization resources for labels/help text
- File upload storage handling
- Full Descriptor Topology engine
- FreeSql/SqlSugar Form persistence
