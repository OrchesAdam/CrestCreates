# Phase 5g — Form Descriptor Kernel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the existing Form descriptor into a fully validatable, dependency-aware kernel node with field-level Schema binding validation.

**Architecture:** Extend `FormFieldDescriptor` with 6 interaction-metadata properties. Add `FormDescriptorValidator` (runs during `FormRegistry.Build()`) and `FormSchemaBindingValidator` (post-build callback, validates field→Schema parity using `GetByVersion`). Add dependency extraction and update contract hash. Disable the invalid CodeGenerator Form auto-generation branch.

**Tech Stack:** C# 13, .NET 10, Xunit + FluentAssertions, Roslyn Source Generator

---

## Task 1: Extend FormFieldDescriptor

**Files:**
- Modify: `framework/src/CrestCreates.Form.Abstractions/FormFieldDescriptor.cs`
- Extend: `framework/test/CrestCreates.Form.Tests/FormDescriptorTests.cs`

- [ ] **Step 1: Add 6 new properties to FormFieldDescriptor**

Replace the entire file:

```csharp
namespace CrestCreates.Form.Abstractions;

public sealed class FormFieldDescriptor
{
    // Existing (unchanged)
    public string SchemaFieldName { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? Placeholder { get; init; }
    public string? HelpText { get; init; }
    public string? FormatHint { get; init; }
    public int Order { get; init; }
    public string? Group { get; init; }
    public bool IsReadOnly { get; init; }
    public string? VisibilityCondition { get; init; }

    // New — Phase 5g interaction metadata
    public string? ControlType { get; init; }
    public bool? IsRequiredOverride { get; init; }
    public string? ValidationMessage { get; init; }
    public string? DefaultValueExpression { get; init; }
    public string? OptionsSource { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
```

- [ ] **Step 2: Build to verify no compilation errors**

Run: `dotnet build framework/src/CrestCreates.Form.Abstractions`
Expected: Build succeeded. 0 Error(s)

- [ ] **Step 3: Add test — Metadata defaults to empty dictionary**

Append to `framework/test/CrestCreates.Form.Tests/FormDescriptorTests.cs`:

```csharp
[Fact]
public void FormFieldDescriptor_Defaults_Metadata_To_Empty()
{
    var field = new FormFieldDescriptor
    {
        SchemaFieldName = "Name"
    };

    field.Metadata.Should().NotBeNull();
    field.Metadata.Should().BeEmpty();
}
```

- [ ] **Step 4: Add test — new fields are settable without runtime behavior**

```csharp
[Fact]
public void FormFieldDescriptor_Allows_Control_Metadata_Without_Runtime_Behavior()
{
    var field = new FormFieldDescriptor
    {
        SchemaFieldName = "Email",
        ControlType = "email",
        IsRequiredOverride = true,
        ValidationMessage = "Please enter a valid email",
        DefaultValueExpression = "\"user@example.com\"",
        OptionsSource = "static:domains",
        Metadata = new Dictionary<string, string>
        {
            ["minWidth"] = "200px",
            ["maxWidth"] = "400px"
        }
    };

    field.ControlType.Should().Be("email");
    field.IsRequiredOverride.Should().BeTrue();
    field.ValidationMessage.Should().Be("Please enter a valid email");
    field.DefaultValueExpression.Should().Be("\"user@example.com\"");
    field.OptionsSource.Should().Be("static:domains");
    field.Metadata["minWidth"].Should().Be("200px");
    field.Metadata["maxWidth"].Should().Be("400px");
}
```

- [ ] **Step 5: Run existing Form tests to verify no regressions**

Run: `dotnet test framework/test/CrestCreates.Form.Tests --filter "FullyQualifiedName~FormDescriptorTests"`
Expected: All tests pass (5 existing + 2 new = 7 pass)

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Form.Abstractions/FormFieldDescriptor.cs framework/test/CrestCreates.Form.Tests/FormDescriptorTests.cs
git commit -m "feat(Phase5g): extend FormFieldDescriptor with 6 interaction-metadata properties"
```

---

## Task 2: Add FormDescriptorValidator

**Files:**
- Create: `framework/src/CrestCreates.Form/FormDescriptorValidator.cs`
- Create: `framework/test/CrestCreates.Form.Tests/FormDescriptorValidatorTests.cs`

- [ ] **Step 1: Create FormDescriptorValidator**

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Form;

public sealed class FormDescriptorValidator : IRegistryValidator<FormDescriptor>
{
    public int Order => 10;

    public ValidationReport Validate(IReadOnlyList<FormDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        foreach (var descriptor in descriptors)
        {
            ValidateDescriptor(descriptor, issues);
        }

        return new ValidationReport(issues);
    }

    private static void ValidateDescriptor(FormDescriptor d, List<ValidationIssue> issues)
    {
        string ctx = $"Form '{d.Name}' (Id={d.Id}, v{d.Version})";

        // Rule 1: Id non-whitespace
        if (string.IsNullOrWhiteSpace(d.Id))
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"{ctx}: Id must not be null or whitespace."));

        // Rule 2: Name non-whitespace
        if (string.IsNullOrWhiteSpace(d.Name))
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"{ctx}: Name must not be null or whitespace."));

        // Rule 3: Version > 0
        if (d.Version <= 0)
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"{ctx}: Version must be positive (was {d.Version})."));

        // Rule 4: Schema ref valid
        if (string.IsNullOrWhiteSpace(d.Schema.Id))
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"{ctx}: Schema.Id must not be null or whitespace."));
        if (d.Schema.Version <= 0)
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"{ctx}: Schema.Version must be positive (was {d.Schema.Version})."));

        // Rule 5: Fields not null
        if (d.Fields == null)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"{ctx}: Fields must not be null. Use Array.Empty<FormFieldDescriptor>()."));
            return;
        }

        var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in d.Fields)
        {
            string fctx = $"{ctx}.Field '{field.SchemaFieldName}'";

            // Rule 6: SchemaFieldName non-whitespace
            if (string.IsNullOrWhiteSpace(field.SchemaFieldName))
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"{ctx}: Field has null or whitespace SchemaFieldName."));

            // Rule 7: Duplicate SchemaFieldName
            if (!string.IsNullOrWhiteSpace(field.SchemaFieldName) &&
                !seenFieldNames.Add(field.SchemaFieldName))
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"{ctx}: Duplicate SchemaFieldName '{field.SchemaFieldName}'."));

            // Rule 8: ControlType not whitespace-only (null is OK, whitespace is not)
            if (field.ControlType != null && string.IsNullOrWhiteSpace(field.ControlType))
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"{fctx}: ControlType is whitespace-only. Set null or non-empty value."));
        }

        // Rule 9: Duplicate Order is allowed — no validation.
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Form`
Expected: Build succeeded. 0 Error(s)

- [ ] **Step 3: Create test file with all 8 validator tests**

`framework/test/CrestCreates.Form.Tests/FormDescriptorValidatorTests.cs`:

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormDescriptorValidatorTests
{
    private readonly FormDescriptorValidator _validator = new();

    private static FormDescriptor CreateValidForm(
        string id = "form_01",
        string name = "TestForm",
        int version = 1,
        string schemaId = "schema_01",
        int schemaVersion = 1,
        FormFieldDescriptor[]? fields = null)
    {
        return new FormDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>(schemaId, schemaVersion),
            Fields = fields ?? Array.Empty<FormFieldDescriptor>()
        };
    }

    private static FormFieldDescriptor CreateField(string schemaFieldName, int order = 0)
    {
        return new FormFieldDescriptor
        {
            SchemaFieldName = schemaFieldName,
            Order = order
        };
    }

    [Fact]
    public void Rejects_EmptyId()
    {
        var form = CreateValidForm(id: "");
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Severity == ValidationSeverity.Error
            && i.Message.Contains("Id must not be null or whitespace"));
    }

    [Fact]
    public void Rejects_EmptyName()
    {
        var form = CreateValidForm(name: "");
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Name must not be null or whitespace"));
    }

    [Fact]
    public void Rejects_NonPositiveVersion()
    {
        var form = CreateValidForm(version: 0);
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("positive"));
    }

    [Fact]
    public void Rejects_EmptySchemaRef()
    {
        var form = CreateValidForm(schemaId: "");
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Schema.Id"));
    }

    [Fact]
    public void Rejects_EmptySchemaFieldName()
    {
        var form = CreateValidForm(fields: new[]
        {
            new FormFieldDescriptor { SchemaFieldName = "" }
        });
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("SchemaFieldName"));
    }

    [Fact]
    public void Rejects_DuplicateSchemaFieldName()
    {
        var form = CreateValidForm(fields: new[]
        {
            CreateField("Name", 0),
            CreateField("Name", 1)
        });
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Duplicate SchemaFieldName"));
    }

    [Fact]
    public void Allows_PartialSchemaCoverage()
    {
        var form = CreateValidForm(fields: new[]
        {
            CreateField("Name", 0),
            CreateField("Email", 1)
        });
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Allows_DuplicateOrder()
    {
        var form = CreateValidForm(fields: new[]
        {
            CreateField("Name", 0),
            CreateField("Email", 0) // same Order — allowed
        });
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeFalse();
    }
}
```

- [ ] **Step 4: Run validator tests**

Run: `dotnet test framework/test/CrestCreates.Form.Tests --filter "FullyQualifiedName~FormDescriptorValidatorTests"`
Expected: 8 tests pass

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Form/FormDescriptorValidator.cs framework/test/CrestCreates.Form.Tests/FormDescriptorValidatorTests.cs
git commit -m "feat(Phase5g): add FormDescriptorValidator with 9 validation rules"
```

---

## Task 3: Add FormServiceCollectionExtensions (AddFormKernel)

**Files:**
- Create: `framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs`
- Extend: `framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs`

- [ ] **Step 1: Create FormServiceCollectionExtensions**

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Form;

public static class FormServiceCollectionExtensions
{
    public static IServiceCollection AddFormKernel(this IServiceCollection services)
    {
        // Registry (singleton — holds built snapshot)
        services.TryAddSingleton<IFormRegistry, FormRegistry>();

        // Validation engine (singleton — consumed by singleton FormRegistry)
        // MUST be singleton to avoid captive dependency.
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

Note: `FormSchemaBindingValidator` is referenced here but created in Task 4. This file is created now with the forward reference — it will compile once Task 4 completes.

- [ ] **Step 2: Add registry test — Build runs FormValidator**

Append to `framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs`:

```csharp
[Fact]
public void Build_Runs_FormValidator()
{
    var engine = new RegistryValidationEngine<FormDescriptor>(
        [new FormDescriptorValidator()]);
    var registry = new FormRegistry(engine);
    var validForm = CreateForm("f1", "ValidForm", 1);
    var provider = new TestFormProvider([validForm]);

    registry.Build([provider]);

    registry.State.Should().Be(RegistryState.Built);
}

[Fact]
public void Build_Fails_On_ValidationError()
{
    var engine = new RegistryValidationEngine<FormDescriptor>(
        [new FormDescriptorValidator()]);
    var registry = new FormRegistry(engine);
    var invalidForm = CreateForm("", "NoIdForm", 0); // Id="" , Version=0
    var provider = new TestFormProvider([invalidForm]);

    var act = () => registry.Build([provider]);

    act.Should().Throw<RegistryValidationException>();
    registry.State.Should().Be(RegistryState.Failed);
}
```

- [ ] **Step 3: Run registry tests**

Run: `dotnet test framework/test/CrestCreates.Form.Tests --filter "FullyQualifiedName~FormRegistryTests"`
Expected: 4 existing + 2 new = 6 pass. Note: `AddFormKernel` DI method is NOT tested here (Singleton lifetime requires full DI container; tested in Task 4's integration test).

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs
git commit -m "feat(Phase5g): add FormServiceCollectionExtensions.AddFormKernel with all-Singleton DI"
```

---

## Task 4: Add FormSchemaBindingValidator

**Files:**
- Create: `framework/src/CrestCreates.Form/FormSchemaBindingValidator.cs`
- Create: `framework/test/CrestCreates.Form.Tests/FormSchemaBindingValidatorTests.cs`
- Modify: `framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs`

- [ ] **Step 1: Add ISchemaRegistry project reference to CrestCreates.Form**

The `CrestCreates.Form.csproj` currently references `CrestCreates.Form.Abstractions` and `CrestCreates.Metadata`. It needs `CrestCreates.Schema.Abstractions` for `ISchemaRegistry`.

Edit `framework/src/CrestCreates.Form/CrestCreates.Form.csproj` — add the reference:
```xml
<ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
```

(The existing `CrestCreates.Form.Abstractions` already references `CrestCreates.Schema.Abstractions`, but `CrestCreates.Form` is a separate project that needs the reference directly for `ISchemaRegistry`.)

- [ ] **Step 2: Create FormSchemaBindingValidator**

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Form;

public sealed class FormSchemaBindingValidator
{
    public ValidationReport Validate(
        IReadOnlyList<FormDescriptor> forms,
        ISchemaRegistry schemaRegistry)
    {
        var issues = new List<ValidationIssue>();

        foreach (var form in forms)
        {
            ValidateForm(form, schemaRegistry, issues);
        }

        return new ValidationReport(issues);
    }

    private static void ValidateForm(
        FormDescriptor form,
        ISchemaRegistry schemaRegistry,
        List<ValidationIssue> issues)
    {
        string ctx = $"Form '{form.Name}' (Id={form.Id}, v{form.Version})";

        // Use GetByVersion to validate against the requested version, NOT latest.
        var schema = schemaRegistry.GetByVersion(form.Schema.Id, form.Schema.Version);

        if (schema == null)
        {
            // Check if ANY version exists for this Id
            var latest = schemaRegistry.GetById(form.Schema.Id);
            if (latest != null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"{ctx}: Schema '{form.Schema.Id}' v{form.Schema.Version} not found. " +
                    $"Latest version is v{latest.Version}."));
            }
            else
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"{ctx}: Schema '{form.Schema.Id}' not found in registry."));
            }
            return;
        }

        var schemaFieldNames = new HashSet<string>(
            schema.Fields.Select(f => f.Name), StringComparer.Ordinal);

        foreach (var field in form.Fields)
        {
            if (!schemaFieldNames.Contains(field.SchemaFieldName))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"{ctx}: Field '{field.SchemaFieldName}' not found in " +
                    $"Schema '{schema.Name}' v{schema.Version} Fields."));
            }
        }

        // Warn on Schema required fields not covered by Form
        foreach (var schemaField in schema.Fields.Where(f => f.IsRequired))
        {
            if (!form.Fields.Any(ff =>
                string.Equals(ff.SchemaFieldName, schemaField.Name, StringComparison.Ordinal)))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning,
                    $"{ctx}: Schema required field '{schemaField.Name}' is not present " +
                    $"in Form fields."));
            }
        }
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Form`
Expected: Build succeeded. 0 Error(s)

- [ ] **Step 4: Create FormSchemaBindingValidator tests**

`framework/test/CrestCreates.Form.Tests/FormSchemaBindingValidatorTests.cs`:

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormSchemaBindingValidatorTests
{
    private readonly FormSchemaBindingValidator _validator = new();

    private static SchemaRegistry CreateSchemaRegistry(params SchemaDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<SchemaDescriptor>(
            Array.Empty<IRegistryValidator<SchemaDescriptor>>());
        var registry = new SchemaRegistry(engine);
        registry.Build([new TestSchemaProvider(descriptors.ToList())]);
        return registry;
    }

    private class TestSchemaProvider : IDescriptorProvider<SchemaDescriptor>
    {
        private readonly List<SchemaDescriptor> _descriptors;
        public TestSchemaProvider(List<SchemaDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<SchemaDescriptor> GetDescriptors() => _descriptors;
    }

    private static SchemaDescriptor CreateSchema(
        string id, string name, int version,
        params (string name, bool isRequired)[] fields)
    {
        return new SchemaDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Fields = fields.Select(f => new SchemaFieldDescriptor
            {
                Name = f.name,
                FieldType = "string",
                IsRequired = f.isRequired
            }).ToList()
        };
    }

    private static FormDescriptor CreateForm(
        string id, string name, int version,
        string schemaId, int schemaVersion,
        params string[] schemaFieldNames)
    {
        return new FormDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>(schemaId, schemaVersion),
            Fields = schemaFieldNames.Select(fn => new FormFieldDescriptor
            {
                SchemaFieldName = fn
            }).ToList()
        };
    }

    [Fact]
    public void Passes_When_AllFieldsExistInSchema()
    {
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true), ("Email", false)));
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 1, "Name", "Email");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Fails_When_FormFieldMissingInSchema()
    {
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true)));
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 1, "Name", "Phone");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Severity == ValidationSeverity.Error
            && i.Message.Contains("Phone"));
    }

    [Fact]
    public void Fails_When_SchemaRefMissing()
    {
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true)));
        var form = CreateForm("f1", "CustomerForm", 1, "s2", 1, "Name");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("not found"));
    }

    [Fact]
    public void Fails_When_SchemaVersionNotFound()
    {
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true)));
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 99, "Name");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("v99") &&
            i.Message.Contains("v1"));
    }

    [Fact]
    public void Warns_When_RequiredSchemaFieldNotInForm()
    {
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true), ("InternalId", true)));
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 1, "Name");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeFalse();
        report.HasWarnings.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Severity == ValidationSeverity.Warning
            && i.Message.Contains("InternalId"));
    }

    [Fact]
    public void Uses_VersionedSchemaRef()
    {
        // Schema v1 has [Name, Email]; Schema v2 adds [Phone] and removes [Email]
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true), ("Email", false)),
            CreateSchema("s1", "CustomerSchema", 2, ("Name", true), ("Phone", false)));

        // Form requests v1 — Email should be valid
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 1, "Name", "Email");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Uses_GetByVersion_Not_GetById()
    {
        // Schema v1 has [Name]; Schema v2 (latest via GetById) adds [Phone]
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true)),
            CreateSchema("s1", "CustomerSchema", 2, ("Name", true), ("Phone", false)));

        // Form requests v1 — "Phone" should fail because v1 doesn't have it
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 1, "Name", "Phone");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Phone") &&
            i.Message.Contains("v1"));
    }
}
```

- [ ] **Step 5: Run binding validator tests**

Run: `dotnet test framework/test/CrestCreates.Form.Tests --filter "FullyQualifiedName~FormSchemaBindingValidatorTests"`
Expected: 7 tests pass

- [ ] **Step 6: Update MetadataBootstrapper with onFormBuilt parameter**

Replace `framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs`:

```csharp
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata;

public static class MetadataBootstrapper
{
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

        // Post-build: Form→Schema binding validation (Phase 5g)
        onFormBuilt?.Invoke(formRegistry.GetAll(), schemaRegistry);

        humanTaskRegistry.Build(DescriptorProviderRegistry.GetProviders<HumanTaskDescriptor>());
        workflowRegistry.Build(DescriptorProviderRegistry.GetProviders<WorkflowDescriptor>());
        eventRegistry.Build(DescriptorProviderRegistry.GetProviders<GeneratedEventDescriptor>());

        // Post-build: workflow compatibility validation (Phase 4b)
        onWorkflowBuilt?.Invoke(workflowRegistry.GetAll());
    }
}
```

- [ ] **Step 7: Build entire solution to confirm no downstream breakage**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s) (the new `onFormBuilt` parameter has a default value, so existing callers are unaffected).

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.Form/CrestCreates.Form.csproj framework/src/CrestCreates.Form/FormSchemaBindingValidator.cs framework/test/CrestCreates.Form.Tests/FormSchemaBindingValidatorTests.cs framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs
git commit -m "feat(Phase5g): add FormSchemaBindingValidator with GetByVersion field validation"
```

---

## Task 5: Add FormDescriptorDependencyExtractor

**Files:**
- Create: `framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs`
- Create: `framework/test/CrestCreates.Form.Tests/FormDescriptorDependencyExtractorTests.cs`

- [ ] **Step 1: Create FormDescriptorDependencyExtractor**

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Form;

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

- [ ] **Step 2: Create tests**

`framework/test/CrestCreates.Form.Tests/FormDescriptorDependencyExtractorTests.cs`:

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormDescriptorDependencyExtractorTests
{
    [Fact]
    public void FormDependencyExtractor_Adds_UsesEdge_ToSchema()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 2)
        };

        var edges = FormDescriptorDependencyExtractor.Extract(form);

        edges.Should().HaveCount(1);
        edges[0].SourceId.Should().Be("form_01");
        edges[0].TargetId.Should().Be("schema_01");
        edges[0].Kind.Should().Be(DescriptorDependencyKind.Uses);
    }

    [Fact]
    public void Form_DoesNot_Depend_On_HumanTask()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };

        var edges = FormDescriptorDependencyExtractor.Extract(form);

        // No edge should reference HumanTask descriptors
        edges.Should().OnlyContain(e => e.Kind == DescriptorDependencyKind.Uses);
        // Form only knows about Schema, not HumanTask
    }
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build framework/src/CrestCreates.Form`
Expected: Build succeeded.

Run: `dotnet test framework/test/CrestCreates.Form.Tests --filter "FullyQualifiedName~FormDescriptorDependencyExtractorTests"`
Expected: 2 tests pass

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs framework/test/CrestCreates.Form.Tests/FormDescriptorDependencyExtractorTests.cs
git commit -m "feat(Phase5g): add FormDescriptorDependencyExtractor with Form→Schema Uses edge"
```

---

## Task 6: Update DescriptorHashComputer

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/DescriptorHashComputer.cs` (lines 90–105)
- Extend: `framework/test/CrestCreates.Metadata.Tests/DescriptorHashComputerTests.cs`

- [ ] **Step 1: Add 3 new fields to Form contract hash**

Edit `DescriptorHashComputer.cs`, replace the `FormDescriptor f =>` block (lines 90–105):

```csharp
            FormDescriptor f => new
            {
                f.Id,
                f.Name,
                f.Version,
                f.State,
                f.SupersededById,
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

- [ ] **Step 2: Find existing Form hash tests to understand the test pattern**

Run: `grep -n "FormContractHash\|FormDefinitionHash\|ComputeContractHash\|ComputeDefinitionHash" framework/test/CrestCreates.Metadata.Tests/DescriptorHashComputerTests.cs`
Read the relevant test file to match the existing test style.

- [ ] **Step 3: Add hash tests**

Append to `framework/test/CrestCreates.Metadata.Tests/DescriptorHashComputerTests.cs`:

```csharp
[Fact]
public void FormContractHash_Changes_When_ControlTypeChanges()
{
    var form1 = new FormDescriptor
    {
        Id = "f1", Name = "Test", Version = 1,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
        Fields = new[] { new FormFieldDescriptor { SchemaFieldName = "Name", ControlType = "text" } }
    };
    var form2 = new FormDescriptor
    {
        Id = "f1", Name = "Test", Version = 1,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
        Fields = new[] { new FormFieldDescriptor { SchemaFieldName = "Name", ControlType = "select" } }
    };

    var hash1 = DescriptorHashComputer.ComputeContractHash(form1);
    var hash2 = DescriptorHashComputer.ComputeContractHash(form2);

    hash1.Should().NotBe(hash2);
}

[Fact]
public void FormContractHash_Changes_When_IsRequiredOverrideChanges()
{
    var form1 = new FormDescriptor
    {
        Id = "f1", Name = "Test", Version = 1,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
        Fields = new[] { new FormFieldDescriptor { SchemaFieldName = "Name", IsRequiredOverride = true } }
    };
    var form2 = new FormDescriptor
    {
        Id = "f1", Name = "Test", Version = 1,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
        Fields = new[] { new FormFieldDescriptor { SchemaFieldName = "Name", IsRequiredOverride = false } }
    };

    var hash1 = DescriptorHashComputer.ComputeContractHash(form1);
    var hash2 = DescriptorHashComputer.ComputeContractHash(form2);

    hash1.Should().NotBe(hash2);
}

[Fact]
public void FormContractHash_DoesNotChange_When_ValidationMessageChanges()
{
    var form1 = new FormDescriptor
    {
        Id = "f1", Name = "Test", Version = 1,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
        Fields = new[] { new FormFieldDescriptor { SchemaFieldName = "Name", ValidationMessage = "Msg A" } }
    };
    var form2 = new FormDescriptor
    {
        Id = "f1", Name = "Test", Version = 1,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
        Fields = new[] { new FormFieldDescriptor { SchemaFieldName = "Name", ValidationMessage = "Msg B" } }
    };

    var hash1 = DescriptorHashComputer.ComputeContractHash(form1);
    var hash2 = DescriptorHashComputer.ComputeContractHash(form2);

    hash1.Should().Be(hash2);
}
```

- [ ] **Step 4: Run hash tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~FormContractHash"`
Expected: All Form contract hash tests pass (existing + 3 new)

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorHashComputer.cs framework/test/CrestCreates.Metadata.Tests/DescriptorHashComputerTests.cs
git commit -m "feat(Phase5g): add ControlType/IsRequiredOverride/OptionsSource to Form contract hash"
```

---

## Task 7: Remove CodeGenerator Form Auto-Generation Branch

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs`

- [ ] **Step 1: Remove GetFormProviderInfo method**

Delete lines 178–197 (the entire `GetFormProviderInfo` static method). The method spans from:
```csharp
    private static FormDescriptorInfo? GetFormProviderInfo(GeneratorSyntaxContext ctx)
```
through the closing `}` at line 197.

- [ ] **Step 2: Remove formProviders SyntaxProvider from Initialize**

In the `Initialize` method, delete lines 40–45:
```csharp
        var formProviders = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetFormProviderInfo(ctx))
            .Where(static x => x is not null)
            .Collect();
```

- [ ] **Step 3: Remove formProviders from the Combine chain**

In the `RegisterSourceOutput` call (lines 63–80), remove `.Combine(formProviders)` from the chain and update the tuple destructuring. Before:
```csharp
        context.RegisterSourceOutput(
            entityClasses.Combine(serviceClasses)
                .Combine(eventProviders)
                .Combine(formProviders)
                .Combine(humanTaskProviders)
                .Combine(workflowProviders)
                .Combine(compilationProvider),
            static (spc, source) =>
            {
                var compilation = source.Right;
                var workflowList = source.Left.Right;
                var humanTaskList = source.Left.Left.Right;
                var formList = source.Left.Left.Left.Right;
                var eventList = source.Left.Left.Left.Left.Right;
                var entityAndCapability = source.Left.Left.Left.Left.Left;
                GenerateRegistries(spc, entityAndCapability.Left, entityAndCapability.Right,
                    eventList, formList, humanTaskList, workflowList, compilation);
            });
```

After:
```csharp
        context.RegisterSourceOutput(
            entityClasses.Combine(serviceClasses)
                .Combine(eventProviders)
                .Combine(humanTaskProviders)
                .Combine(workflowProviders)
                .Combine(compilationProvider),
            static (spc, source) =>
            {
                var compilation = source.Right;
                var workflowList = source.Left.Right;
                var humanTaskList = source.Left.Left.Right;
                var eventList = source.Left.Left.Left.Right;
                var entityAndCapability = source.Left.Left.Left.Left;
                GenerateRegistries(spc, entityAndCapability.Left, entityAndCapability.Right,
                    eventList, ImmutableArray<FormDescriptorInfo?>.Empty, humanTaskList, workflowList, compilation);
            });
```

- [ ] **Step 4: Remove the Form auto-generation block from GenerateRegistries**

In `GenerateRegistries`, remove the `hasForm` declaration (lines 270–271):
```csharp
        var hasForm = compilation.ReferencedAssemblyNames
            .Any(a => a.Name == "CrestCreates.Form.Abstractions");
```

Remove the conditional `using CrestCreates.Form.Abstractions;` (lines 284–285):
```csharp
        if (hasForm)
            sb.AppendLine("using CrestCreates.Form.Abstractions;");
```

Remove the entire Form generation block (lines 331–367) — the `if (hasForm && forms.Any(f => f != null))` block.

Remove `forms` from the `hasAny` check (lines 256–261) — remove line 259:
```csharp
            || forms.Any(f => f != null)
```

- [ ] **Step 5: Remove using for ImmutableArray if no longer needed**

The `ImmutableArray<FormDescriptorInfo?>` is still used for the `forms` parameter. Since we now pass `ImmutableArray<FormDescriptorInfo?>.Empty`, the `FormDescriptorInfo` type is still referenced. Check if `using CrestCreates.CodeGenerator.Models;` still pulls in `FormDescriptorInfo` — if so, leave it. 

But actually, we removed the method that creates `FormDescriptorInfo`. The `GenerateRegistries` still accepts `ImmutableArray<FormDescriptorInfo?>` but it's always empty. The `FormDescriptorInfo` type and `FormFieldInfo` are still defined in `Models/` and may be used elsewhere (unlikely, but don't delete the model files — just stop generating Form output from them).

- [ ] **Step 6: Build the CodeGenerator project**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator`
Expected: Build succeeded. 0 Error(s), 0 Warning(s)

- [ ] **Step 7: Build full solution to verify no downstream breakage**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s)

- [ ] **Step 8: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs
git commit -m "feat(Phase5g): remove CodeGenerator Form auto-generation branch (invalid descriptors)"
```

---

## Task 8: Final Tests, Metadata Snapshot Test, and Regression

**Files:**
- Extend: `framework/test/CrestCreates.Form.Tests/FormDescriptorTests.cs` (1 test)
- Extend: `framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs` (1 integration test)

- [ ] **Step 1: Add Metadata insertion-order hash test**

Append to `framework/test/CrestCreates.Form.Tests/FormDescriptorTests.cs`:

```csharp
[Fact]
public void Metadata_InsertionOrder_DoesNotAffect_ContractHash()
{
    var form1 = new FormDescriptor
    {
        Id = "f1", Name = "TestForm", Version = 1,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
        Fields = new[]
        {
            new FormFieldDescriptor
            {
                SchemaFieldName = "Name",
                Metadata = new Dictionary<string, string>
                {
                    ["A"] = "1",
                    ["B"] = "2"
                }
            }
        }
    };
    var form2 = new FormDescriptor
    {
        Id = "f1", Name = "TestForm", Version = 1,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
        Fields = new[]
        {
            new FormFieldDescriptor
            {
                SchemaFieldName = "Name",
                Metadata = new Dictionary<string, string>
                {
                    ["B"] = "2",
                    ["A"] = "1"
                }
            }
        }
    };

    var hash1 = DescriptorHashComputer.ComputeContractHash(form1);
    var hash2 = DescriptorHashComputer.ComputeContractHash(form2);

    hash1.Should().Be(hash2);
}
```

Note: This test requires `using CrestCreates.Metadata;` — already present via the existing test file.

- [ ] **Step 2: Add integration test — FormSchemaBindingValidator with real registries**

Append to `framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs`:

```csharp
[Fact]
public void FormSchemaBindingValidator_With_Real_Registries()
{
    // Build a real SchemaRegistry
    var schemaEngine = new RegistryValidationEngine<SchemaDescriptor>(
        Array.Empty<IRegistryValidator<SchemaDescriptor>>());
    var schemaRegistry = new SchemaRegistry(schemaEngine);
    var schemaProvider = new TestSchemaProviderForRegistry([
        new SchemaDescriptor
        {
            Id = "s1", Name = "CustomerSchema", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = true },
                new() { Name = "Email", FieldType = "string", IsRequired = false }
            }
        }
    ]);
    schemaRegistry.Build([schemaProvider]);

    // Build FormRegistry with validator
    var formEngine = new RegistryValidationEngine<FormDescriptor>(
        [new FormDescriptorValidator()]);
    var formRegistry = new FormRegistry(formEngine);
    var formProvider = new TestFormProvider([
        new FormDescriptor
        {
            Id = "f1", Name = "CustomerForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new List<FormFieldDescriptor>
            {
                new() { SchemaFieldName = "Name" },
                new() { SchemaFieldName = "Email" }
            }
        }
    ]);
    formRegistry.Build([formProvider]);

    // Run schema binding validator
    var bindingValidator = new FormSchemaBindingValidator();
    var report = bindingValidator.Validate(formRegistry.GetAll(), schemaRegistry);

    report.HasErrors.Should().BeFalse();
    report.HasWarnings.Should().BeFalse();
}

private class TestSchemaProviderForRegistry : IDescriptorProvider<SchemaDescriptor>
{
    private readonly List<SchemaDescriptor> _descriptors;
    public TestSchemaProviderForRegistry(List<SchemaDescriptor> descriptors) => _descriptors = descriptors;
    public IReadOnlyList<SchemaDescriptor> GetDescriptors() => _descriptors;
}
```

Note: Add `using CrestCreates.Schema;` and `using CrestCreates.Schema.Abstractions;` to the test file if not already present.

- [ ] **Step 3: Run full Form test suite**

Run: `dotnet test framework/test/CrestCreates.Form.Tests`
Expected: All tests pass:
- FormDescriptorTests: 5 existing + 2 new (Task 1) + 1 new (metadata hash) = 8
- FormRegistryTests: 4 existing + 2 new (Task 3) + 1 new (integration) = 7
- FormDescriptorValidatorTests: 8 (Task 2)
- FormSchemaBindingValidatorTests: 7 (Task 4)
- FormDescriptorDependencyExtractorTests: 2 (Task 5)
- **TOTAL: 32 tests pass**

- [ ] **Step 4: Run Metadata test suite**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests`
Expected: All tests pass (existing + 3 new hash tests from Task 6)

- [ ] **Step 5: Run full regression**

```bash
dotnet test framework/test/CrestCreates.Form.Tests
dotnet test framework/test/CrestCreates.Metadata.Tests
dotnet test framework/test/CrestCreates.HumanTask.Tests
dotnet test framework/test/CrestCreates.Workflow.Tests
```

Expected: All four test suites pass with zero regressions.

- [ ] **Step 6: Commit**

```bash
git add framework/test/
git commit -m "test(Phase5g): add Metadata hash ordering test and real-registries integration test"
```

---

## Acceptance Checklist

After all 8 tasks complete:

- [ ] `FormFieldDescriptor` has 6 new interaction-metadata properties
- [ ] `FormDescriptorValidator` catches all 9 malformation cases during `FormRegistry.Build()`
- [ ] `FormSchemaBindingValidator` validates field→Schema parity using `GetByVersion`, not `GetById`
- [ ] `MetadataBootstrapper.BuildAll()` has `onFormBuilt` callback parameter
- [ ] `FormServiceCollectionExtensions.AddFormKernel()` registers all components as Singleton
- [ ] `FormDescriptorDependencyExtractor` produces `Form → Schema` edge with `Kind = Uses`
- [ ] `DescriptorHashComputer` includes `ControlType`, `IsRequiredOverride`, `OptionsSource` in contract hash
- [ ] CodeGenerator no longer emits `GeneratedFormProvider` or `new FormDescriptor` in generated output
- [ ] 32 new tests pass + zero regressions in HumanTask (44), Workflow (57)
- [ ] Full solution builds with 0 errors
