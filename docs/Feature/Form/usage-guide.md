# Form Descriptor Kernel — Usage Guide

> This document is for CrestCreates module developers who need to define and validate Forms as descriptor-first UI metadata layers on top of Schemas.
> *Phase 5g (2026-06-12): Form Descriptor Kernel — 6 interaction-metadata properties, 2 validators, dependency extraction, 32 tests*

---

## 1. Quick Start

### 1.1 Register the Kernel

```csharp
using CrestCreates.Form;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFormKernel();
```

This registers `IFormRegistry`, `FormDescriptorValidator`, `FormSchemaBindingValidator`, and the validation engine — all as Singleton with `TryAdd*` semantics.

### 1.2 Define a Form Provider

Create a class implementing `IFormDescriptorProvider`:

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

public class CustomerFormProvider : IFormDescriptorProvider
{
    public FormDescriptor GetFormDescriptor() => new()
    {
        Id = "form_customer_create",
        Name = "CustomerCreateForm",
        Version = 1,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer", 1),
        LayoutColumns = "2",
        Fields = new List<FormFieldDescriptor>
        {
            new()
            {
                SchemaFieldName = "Name",
                Label = "Full Name",
                Placeholder = "Enter customer name",
                Order = 0,
                ControlType = "text",
                IsRequiredOverride = true
            },
            new()
            {
                SchemaFieldName = "Email",
                Label = "Email Address",
                Order = 1,
                ControlType = "email",
                ValidationMessage = "Please enter a valid email address"
            },
            new()
            {
                SchemaFieldName = "Plan",
                Label = "Subscription Plan",
                Order = 2,
                ControlType = "select",
                OptionsSource = "static:basic,premium,enterprise",
                Metadata = new Dictionary<string, string>
                {
                    ["minWidth"] = "200px"
                }
            }
        }
    };
}
```

**IMPORTANT**: Form descriptors are hand-authored. The CodeGenerator does NOT auto-generate Form descriptors — the previous auto-generation branch was removed in Phase 5g because it produced invalid descriptors.

### 1.3 Register and Build

```csharp
// Register your provider
builder.Services.AddSingleton<IDescriptorProvider<FormDescriptor>, CustomerFormProvider>();

// Build all registries
var app = builder.Build();
var schemaRegistry = app.Services.GetRequiredService<ISchemaRegistry>();
var formRegistry = app.Services.GetRequiredService<IFormRegistry>();
// ... other registries ...

MetadataBootstrapper.BuildAll(
    schemaRegistry, formRegistry,
    humanTaskRegistry, workflowRegistry, eventRegistry,
    onFormBuilt: (forms, schema) =>
    {
        var validator = app.Services.GetRequiredService<FormSchemaBindingValidator>();
        var report = validator.Validate(forms, schema);
        if (report.HasErrors)
            throw new InvalidOperationException(
                $"Form→Schema binding validation failed: {report.Issues}");
    });
```

---

## 2. FormFieldDescriptor Properties

### 2.1 Binding

| Property | Required | Description |
|----------|----------|-------------|
| `SchemaFieldName` | ✅ | Must match a `SchemaFieldDescriptor.Name` in the referenced Schema version |

### 2.2 UI Metadata (Cosmetic)

| Property | Type | Contract Hash | Description |
|----------|------|---------------|-------------|
| `Label` | `string?` | ❌ | Display label for the field |
| `Placeholder` | `string?` | ❌ | Input placeholder text |
| `HelpText` | `string?` | ❌ | Help/tooltip text |
| `FormatHint` | `string?` | ❌ | Format hint (e.g. "YYYY-MM-DD") |
| `ValidationMessage` | `string?` | ❌ | Custom error message for this field |

### 2.3 Interaction Contract

| Property | Type | Contract Hash | Description |
|----------|------|---------------|-------------|
| `Order` | `int` | ✅ | Display order. Duplicates allowed (sorted by Order then SchemaFieldName). |
| `Group` | `string?` | ✅ | Visual group/layout section |
| `IsReadOnly` | `bool` | ✅ | Whether the field is read-only |
| `ControlType` | `string?` | ✅ | UI widget hint: `"text"`, `"select"`, `"date"`, `"file"`, `"textarea"`, `"number"`, `"checkbox"`... No enum — not bound to any component library. |
| `IsRequiredOverride` | `bool?` | ✅ | Form-layer required marker. Does NOT alter `SchemaFieldDescriptor.IsRequired`. |
| `OptionsSource` | `string?` | ✅ | Option source identifier. Format: `"static:value1,value2"` or `"lookup:source_name"`. Not resolved or executed in Phase 5g. |

### 2.4 Opaque Strings (Never Executed)

| Property | Type | Description |
|----------|------|-------------|
| `VisibilityCondition` | `string?` | Visibility condition expression (e.g. `"Role == 'Manager'"`). Stored as string, not parsed. |
| `DefaultValueExpression` | `string?` | Default value expression (e.g. `"today()"`). Stored as string, not executed. |

### 2.5 Extension Bag

| Property | Type | Description |
|----------|------|-------------|
| `Metadata` | `IReadOnlyDictionary<string, string>` | Key-value extension bag. Keys and values must be strings. Excluded from contract hash. Insertion order does not affect hash (sorted by key). |

---

## 3. Validation

### 3.1 Build-Time Validation (FormDescriptorValidator)

Runs automatically during `FormRegistry.Build()`. Rejects:

- Empty `Id` or `Name`
- `Version <= 0`
- Empty `Schema.Id` or `Schema.Version <= 0`
- Null `Fields`
- Empty `SchemaFieldName` in any field
- Duplicate `SchemaFieldName` within the same Form
- Whitespace-only `ControlType` (null is OK)

Duplicate `Order` values are **allowed** and do not trigger validation errors.

### 3.2 Post-Build Validation (FormSchemaBindingValidator)

Called via `onFormBuilt` callback after both Schema and Form registries are built. Validates:

- Referenced Schema exists at the requested version (`GetByVersion`)
- Every `SchemaFieldName` exists in the referenced Schema version's fields
- Schema `IsRequired=true` fields not in Form → **Warning** (not Error)

**Version-specific validation**: Form requesting Schema v1 is validated against v1's fields. If v2 adds/removes fields, v1-requesting Forms are unaffected.

```csharp
// Schema v1: { Name, Email }
// Schema v2: { Name, Phone }  (removed Email, added Phone)

// Form requesting v1 with field "Email" → ✅ passes (Email exists in v1)
// Form requesting v1 with field "Phone" → ❌ fails (Phone doesn't exist in v1)
```

---

## 4. Dependency Graph

Forms participate in the descriptor dependency graph:

```csharp
foreach (var form in formRegistry.GetAll())
{
    var edges = FormDescriptorDependencyExtractor.Extract(form);
    foreach (var edge in edges)
        DependencyGraphProvider.RegisterEdge(edge.SourceId, edge.TargetId, edge.Kind);
}
```

Result: `Schema ←──[Uses]── Form`

Each Form produces exactly one edge pointing to its referenced Schema.

---

## 5. Contract Hash

The contract hash captures fields that change how a user **interacts** with the Form:

```csharp
// Fields included in contract hash:
Schema.Id, Schema.Version
SchemaFieldName, IsReadOnly, Order, Group
ControlType, IsRequiredOverride, OptionsSource

// Fields excluded (cosmetic):
Label, Placeholder, HelpText, FormatHint, ValidationMessage
VisibilityCondition, DefaultValueExpression, Metadata
```

Two Forms with different `Label` values but identical interaction fields have the same contract hash. Two Forms with different `ControlType` values have different contract hashes.

```csharp
var hash = DescriptorHashComputer.ComputeContractHash(form);
```

---

## 6. Common Patterns

### 6.1 Partial Schema Coverage

A Form does not need to cover every Schema field:

```csharp
// Schema has: Id, Name, Email, CreatedBy, CreatedAt
// Form only shows: Name, Email
// → System fields (Id, CreatedBy, CreatedAt) are intentionally omitted
```

The `FormSchemaBindingValidator` issues a **Warning** for required Schema fields not in the Form — never an Error.

### 6.2 Form-Only Required Override

Make a field required in the UI even if the Schema doesn't require it:

```csharp
new FormFieldDescriptor
{
    SchemaFieldName = "Notes",
    IsRequiredOverride = true  // UI shows required, Schema says optional
}
```

This does NOT alter `SchemaFieldDescriptor.IsRequired`. Validation at the Schema layer is unaffected.

### 6.3 Control Type Hints

```csharp
// Text input
new() { SchemaFieldName = "Name", ControlType = "text" }

// Dropdown with static options
new() { SchemaFieldName = "Plan", ControlType = "select", OptionsSource = "static:basic,premium" }

// Date picker
new() { SchemaFieldName = "BirthDate", ControlType = "date" }

// File upload
new() { SchemaFieldName = "Avatar", ControlType = "file" }

// Multi-line text
new() { SchemaFieldName = "Bio", ControlType = "textarea" }
```

`ControlType` is just a hint string. No enum — no dependency on any UI component library.

---

## 7. Requirements for Providers

### 7.1 Identity Stability

Every `FormDescriptor` must have a stable, unique `Id`. The `Id` is the primary key in the registry and is referenced by `HumanTaskDescriptor.Interaction`. Do NOT use random GUIDs — use meaningful, stable identifiers.

```csharp
// ✅ Good
Id = "form_customer_create"

// ❌ Bad
Id = $"form_{Guid.NewGuid():N}"
```

### 7.2 Schema Reference

`Schema.Id` and `Schema.Version` must reference a real Schema that exists in the `ISchemaRegistry`. The `FormSchemaBindingValidator` will catch missing references at build time.

### 7.3 Immutability After Build

Providers MUST NOT mutate descriptors (or their `Metadata` dictionaries) after returning them from `GetFormDescriptor()` or `GetDescriptors()`. The registry does not clone descriptors and will not detect post-build mutation.

---

## 8. Testing Your Form

### 8.1 Unit Testing a Form Descriptor

```csharp
[Fact]
public void MyForm_Passes_Validation()
{
    var form = new MyFormProvider().GetFormDescriptor();
    var validator = new FormDescriptorValidator();

    var report = validator.Validate([form]);

    report.HasErrors.Should().BeFalse();
}
```

### 8.2 Integration Testing Against Schema

```csharp
[Fact]
public void MyForm_Binds_To_Schema()
{
    // Build Schema registry
    var schemaRegistry = new SchemaRegistry(/* ... */);
    schemaRegistry.Build([/* providers */]);

    // Build Form registry with validator
    var formEngine = new RegistryValidationEngine<FormDescriptor>(
        [new FormDescriptorValidator()]);
    var formRegistry = new FormRegistry(formEngine);
    formRegistry.Build([new MyFormProvider()]);

    // Run schema binding validation
    var bindingValidator = new FormSchemaBindingValidator();
    var report = bindingValidator.Validate(formRegistry.GetAll(), schemaRegistry);

    report.HasErrors.Should().BeFalse();
}
```

---

## 9. References

- Architecture summary: `docs/Feature/Form/arch-design.md`
- Design spec: `docs/superpowers/specs/2026-06-12-phase-5g-form-descriptor-kernel-design.md`
- Implementation plan: `docs/superpowers/plans/2026-06-12-phase-5g-form-descriptor-kernel.md`
- Unified Metadata Model: `docs/Feature/UnifiedMetadataModel/usage-guide.md` (Section 4: Form & HumanTask)
- Form source: `framework/src/CrestCreates.Form.Abstractions/` and `framework/src/CrestCreates.Form/`
