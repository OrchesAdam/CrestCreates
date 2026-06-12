# Form Descriptor Kernel — Architecture Summary

> **Date:** 2026-06-12 | **Status:** Complete | **Phase 5g: Form Descriptor Kernel**

---

## 1. Design Goals

Establish the minimum Form Descriptor Kernel enabling Form to participate as a validatable, dependency-aware descriptor node in the Descriptor-first architecture.

The kernel answers 4 questions:

| Question | Mechanism |
|----------|-----------|
| Is this Form structurally valid? | `FormDescriptorValidator` (during `FormRegistry.Build()`) |
| Do all Form fields bind to real Schema fields? | `FormSchemaBindingValidator` (post-build callback) |
| What does this Form depend on? | `FormDescriptorDependencyExtractor` (Form → Schema edge) |
| Does the Form interaction contract change? | `DescriptorHashComputer` (contract hash includes interaction fields) |

---

## 2. Project Structure

Two projects following the existing descriptor conventions:

```
framework/src/CrestCreates.Form.Abstractions/   # FormDescriptor, FormFieldDescriptor, IFormRegistry, IFormDescriptorProvider
framework/src/CrestCreates.Form/                  # FormRegistry, validators, dependency extractor, DI extensions
framework/test/CrestCreates.Form.Tests/           # 32 tests
```

**Dependencies**: Form.Abstractions depends on `Metadata.Abstractions` + `Schema.Abstractions`. Form depends on Abstractions + `Metadata` + `Schema.Abstractions`. Form does NOT depend on HumanTask, Workflow, Capability, or ASP.NET Core.

---

## 3. Core Models (Abstractions)

### 3.1 FormDescriptor

```csharp
public sealed class FormDescriptor : IInteractionDescriptor
{
    // Identity
    string Id { get; init; }
    string Name { get; init; }
    int Version { get; init; }
    DescriptorState State { get; init; }
    string? SupersededById { get; init; }

    // Hash (computed by DescriptorHashComputer)
    string ContractHash { get; init; }
    string DefinitionHash { get; init; }

    // Core: Schema reference + field list
    VersionedDescriptorRef<SchemaDescriptor> Schema { get; init; }
    IReadOnlyList<FormFieldDescriptor> Fields { get; init; }

    // Layout hint
    string? LayoutColumns { get; init; }
}
```

Implements `IInteractionDescriptor : IVersionedDescriptor`. Registered via `IFormRegistry : IVersionedDescriptorRegistry<FormDescriptor>`.

### 3.2 FormFieldDescriptor

```csharp
public sealed class FormFieldDescriptor
{
    // Binding
    string SchemaFieldName { get; init; }  // maps to SchemaFieldDescriptor.Name

    // UI metadata (cosmetic — excluded from contract hash)
    string? Label { get; init; }
    string? Placeholder { get; init; }
    string? HelpText { get; init; }
    string? FormatHint { get; init; }
    string? ValidationMessage { get; init; }

    // Interaction contract (changes affect contract hash)
    int Order { get; init; }
    string? Group { get; init; }
    bool IsReadOnly { get; init; }
    string? ControlType { get; init; }        // "text" | "select" | "date" | "file" | ...
    bool? IsRequiredOverride { get; init; }    // UI required, does NOT alter Schema
    string? OptionsSource { get; init; }       // "static:us_states" | "lookup:departments"

    // Opaque metadata (stored as strings, never executed)
    string? VisibilityCondition { get; init; }
    string? DefaultValueExpression { get; init; }  // "today()"
    IReadOnlyDictionary<string, string> Metadata { get; init; }  // extension bag
}
```

---

## 4. Validation Pipeline

### 4.1 FormDescriptorValidator

Runs during `FormRegistry.Build()` via `IRegistryValidator<FormDescriptor>` (Order=10).

| # | Rule | Severity |
|---|---|---|
| 1 | `Id` null/whitespace | Error |
| 2 | `Name` null/whitespace | Error |
| 3 | `Version <= 0` | Error |
| 4 | `Schema.Id` empty or `Schema.Version <= 0` | Error |
| 5 | `Fields` is null | Error |
| 6 | `SchemaFieldName` null/whitespace | Error |
| 7 | Duplicate `SchemaFieldName` within same Form | Error |
| 8 | `ControlType` whitespace-only | Error |
| 9 | Duplicate `Order` | (none) — allowed |

**Not validated**: Field→Schema binding (delegated to FormSchemaBindingValidator), Schema version resolution, expression syntax.

### 4.2 FormSchemaBindingValidator

Standalone service — NOT an `IRegistryValidator`. Invoked via `onFormBuilt` callback in `MetadataBootstrapper.BuildAll()` after both Schema and Form registries are built.

```
Build SchemaRegistry
Build FormRegistry  (FormDescriptorValidator runs here)
  │
  ▼
onFormBuilt(forms, schemaRegistry)
  │
  ▼
FormSchemaBindingValidator.Validate(forms, schemaRegistry)
```

| # | Rule | Severity |
|---|---|---|
| 1 | Schema not found via `GetByVersion(form.Schema.Id, form.Schema.Version)` | Error |
| 2 | Schema version exists for Id but requested version doesn't | Error |
| 3 | `SchemaFieldName` not in requested version's `Fields[].Name` | Error |
| 4 | Schema `IsRequired=true` field not in Form | Warning |

**Critical**: Uses `schemaRegistry.GetByVersion(id, version)` — NOT `GetById()` (which returns latest). Form requesting Schema v1 validates against v1's fields, even if v2 has different fields.

---

## 5. Dependency Extraction

```csharp
public static class FormDescriptorDependencyExtractor
{
    public static IReadOnlyList<DependencyEdge> Extract(FormDescriptor descriptor);
}
```

Produces a single edge: `Form → Schema` with `Kind = Uses`. Feeds into `DependencyGraphProvider.RegisterEdge()`.

Resulting graph:
```
Schema ←──[Uses]── Form ←──[References]── HumanTask
```

HumanTask's reference to Form via `IInteractionDescriptor` is managed by HumanTask's own extraction path — not duplicated here.

---

## 6. Contract Hash Classification

| Field | Contract Hash | Definition Hash | Rationale |
|---|---|---|---|
| `Schema.Id` / `Schema.Version` | ✅ | ✅ | Core reference |
| `SchemaFieldName` | ✅ | ✅ | Core binding |
| `IsReadOnly` | ✅ | ✅ | Affects interaction |
| `Order` | ✅ | ✅ | Affects interaction |
| `Group` | ✅ | ✅ | Affects interaction |
| `ControlType` | ✅ | ✅ | Changing widget type alters interaction |
| `IsRequiredOverride` | ✅ | ✅ | Changing mandatory status alters interaction |
| `OptionsSource` | ✅ | ✅ | Changing option source alters interaction |
| `Label` | ❌ | ✅ | Cosmetic |
| `Placeholder` | ❌ | ✅ | Cosmetic |
| `HelpText` | ❌ | ✅ | Cosmetic |
| `FormatHint` | ❌ | ✅ | Cosmetic |
| `ValidationMessage` | ❌ | ✅ | Cosmetic |
| `VisibilityCondition` | ❌ | ✅ | Deferred — not executed in Phase 5g |
| `DefaultValueExpression` | ❌ | ✅ | Deferred — not executed in Phase 5g |
| `Metadata` | ❌ | ✅ | Extension bag |

---

## 7. DI Registration

```csharp
services.AddFormKernel();
// Registers (all Singleton, all TryAdd*):
//   IFormRegistry                          → FormRegistry
//   IRegistryValidationEngine<FormDescriptor> → RegistryValidationEngine<FormDescriptor>
//   IRegistryValidator<FormDescriptor>       → FormDescriptorValidator
//   FormSchemaBindingValidator               → FormSchemaBindingValidator
```

All Singleton — no captive dependencies. `TryAdd*` never overrides consumer registrations.

---

## 8. CodeGenerator Status

The CodeGenerator's SchemaCapabilitySourceGenerator previously emitted auto-generated `FormDescriptor` instances from `IFormDescriptorProvider` type signatures. These were invalid (`Version=0`, empty `Schema.Id`) and would fail the new `FormDescriptorValidator`.

**Phase 5g removal**: The entire Form auto-generation branch is removed:
- `GetFormProviderInfo()` method deleted
- `GenerateRegistries` Form emission block deleted
- `hasForm` reference check deleted
- `GeneratedFormProvider` is never emitted

The only path for Form descriptors into the registry is hand-written `IFormDescriptorProvider` implementations via `DescriptorProviderRegistry.GetProviders<FormDescriptor>()`.

---

## 9. Tests (32 tests, 0 failures)

| Test File | Tests | Coverage |
|-----------|-------|----------|
| `FormDescriptorTests` | 8 | Descriptor construction, field metadata defaults, Metadata hash exclusion |
| `FormDescriptorValidatorTests` | 8 | All 9 validation rules (EmptyId, EmptyName, NonPositiveVersion, EmptySchemaRef, EmptySchemaFieldName, DuplicateSchemaFieldName, PartialSchemaCoverage, DuplicateOrder) |
| `FormSchemaBindingValidatorTests` | 7 | All 5 binding rules + GetByVersion correctness + version-specific field validation |
| `FormRegistryTests` | 7 | Build, GetById, multi-version, GetAll, Build-with-validator, Build-fails-on-error, real-registries integration |
| `FormDescriptorDependencyExtractorTests` | 2 | Uses edge, no HumanTask dependency |

---

## 10. Design Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | `FormSchemaBindingValidator` is standalone, not `IRegistryValidator` | Schema registry must be built before Form field validation. Post-build callback avoids circular build-order dependency. |
| 2 | Uses `GetByVersion`, not `GetById` | `GetById` returns latest. Form requesting v1 must validate against v1's fields, not v2's. |
| 3 | All DI registrations are Singleton | Components are stateless. Scoped registry with Singleton engine would be a captive dependency. |
| 4 | `Metadata` is `IReadOnlyDictionary<string,string>` with `init` | Immutable interface, mutable backing via `Dictionary`. Phase 5g does not provide deep immutability; providers must not mutate after build. |
| 5 | Required Schema field absent from Form → Warning (not Error) | System fields, hidden fields, and computed fields are legitimately omitted. |
| 6 | Generator Form branch removed entirely | Branch produced invalid descriptors. No valid path existed. Making it instantiate providers would add coupling. |
| 7 | `ControlType` is a string, not an enum | Avoids binding Form to a specific UI component library. |

---

## 11. Explicit Non-Goals

No renderer, form designer, low-code builder UI, frontend component library binding, FormSubmission runtime, draft/autosave, HumanTask form binding runtime, HumanTask claim/delegate/escalation, Workflow step Form target, Capability Authorization changes, DataPermission integration, API/Controller/AppService, database persistence, EF/SqlSugar/Dapper/Mongo/Redis provider, expression execution, visibility evaluation, JavaScript/C# script engine, localization resources, file upload storage, full Descriptor Topology engine.

No dependency on HumanTask, Workflow, Capability, Organization, or ASP.NET Core.

---

## 12. References

- Design spec: `docs/superpowers/specs/2026-06-12-phase-5g-form-descriptor-kernel-design.md`
- Implementation plan: `docs/superpowers/plans/2026-06-12-phase-5g-form-descriptor-kernel.md`
- Unified Metadata Model (architecture): `docs/Feature/UnifiedMetadataModel/arch-design.md`
- Form source: `framework/src/CrestCreates.Form.Abstractions/` and `framework/src/CrestCreates.Form/`
- Form tests: `framework/test/CrestCreates.Form.Tests/`
