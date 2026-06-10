# Phase 4a — Main Chain Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate 4 remaining Registries to RegistryBase, add centralized DescriptorProviderRegistry + MetadataBootstrapper, unify Source Generator output, write E2E and cross-registry validation tests, and clean up Id/Name semantics.

**Architecture:** New layer: `DescriptorProviderRegistry` (static provider collector) → `MetadataBootstrapper.BuildAll()` (build orchestrator) → `Registry.Build(providers)`. All 6 registries now share the same lifecycle. Source Generator generates `IDescriptorProvider<T>` classes registered to the centralized store.

**Tech Stack:** .NET 10, C# 13, Roslyn Source Generator (netstandard2.0), xUnit, FluentAssertions

**Design Spec:** `docs/superpowers/specs/2026-06-10-phase-4a-main-chain-closure-design.md`

---

### Task 1: Prerequisites Check

**Files:**
- Check: `samples/LibraryManagement/LibraryManagement.Domain/LibraryManagement.Domain.csproj`

- [ ] **Step 1: Verify consumer project references CrestCreates.Metadata**

```bash
grep "CrestCreates.Metadata" samples/LibraryManagement/LibraryManagement.Domain/LibraryManagement.Domain.csproj
```
Expected: ProjectReference or transitive dependency exists. If missing, add `<ProjectReference Include="../../framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj" />`.

- [ ] **Step 2: Verify consumer project references CrestCreates.Metadata.Abstractions**

```bash
grep "CrestCreates.Metadata.Abstractions" samples/LibraryManagement/LibraryManagement.Domain/LibraryManagement.Domain.csproj
```
Expected: Already present (via transitive dependency through Domain.Shared → Metadata.Abstractions).

- [ ] **Step 3: Build consumer project to confirm current state**

Run: `dotnet build samples/LibraryManagement/LibraryManagement.Domain`
Expected: Pre-existing `SchemaRegistryProvider` error (unrelated to Phase 4a — will be fixed by Task 9).

- [ ] **Step 4: Commit prerequisites check**

```bash
GIT_MASTER=1 git add -A && GIT_MASTER=1 git commit -m "chore: verify Phase 4a prerequisites — consumer project references confirmed"
```

---

### Task 2: DescriptorProviderRegistry

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorProviderRegistry.cs`

- [ ] **Step 1: Create DescriptorProviderRegistry.cs**

```csharp
// framework/src/CrestCreates.Metadata/DescriptorProviderRegistry.cs
using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorProviderRegistry
{
    private static readonly ConcurrentBag<object> _providers = new();

    public static void Register<T>(IDescriptorProvider<T> provider) where T : class, IDescriptor
        => _providers.Add(provider);

    public static IReadOnlyList<IDescriptorProvider<T>> GetProviders<T>() where T : class, IDescriptor
        => _providers.OfType<IDescriptorProvider<T>>().ToList();
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
GIT_MASTER=1 git add framework/src/CrestCreates.Metadata/DescriptorProviderRegistry.cs
GIT_MASTER=1 git commit -m "feat(metadata): add DescriptorProviderRegistry — centralized provider collector"
```

---

### Task 3: SchemaRegistry Migration to RegistryBase

**Files:**
- Rewrite: `framework/src/CrestCreates.Schema/SchemaRegistry.cs`
- Update: `framework/test/CrestCreates.Schema.Tests/SchemaRegistryTests.cs`

- [ ] **Step 1: Update SchemaRegistryTests to use Build(providers) pattern**

Replace all `new SchemaRegistry()` + `.Register()` with `Build([provider])`:

```csharp
// framework/test/CrestCreates.Schema.Tests/SchemaRegistryTests.cs
using System.Collections.Immutable;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Schema.Tests;

public class SchemaRegistryTests
{
    private sealed class TestSchemaProvider : IDescriptorProvider<SchemaDescriptor>
    {
        private readonly List<SchemaDescriptor> _descriptors;
        public TestSchemaProvider(List<SchemaDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<SchemaDescriptor> GetDescriptors() => _descriptors;
    }

    private static SchemaRegistry CreateRegistry(params SchemaDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<SchemaDescriptor>([]);
        var registry = new SchemaRegistry(engine);
        registry.Build([new TestSchemaProvider(descriptors.ToList())]);
        return registry;
    }

    [Fact]
    public void Build_And_GetById_Returns_Descriptor()
    {
        var registry = CreateRegistry(
            new SchemaDescriptor { Id = "schema_01", Name = "CustomerInput", Version = 1 }
        );
        var result = registry.GetById("schema_01");
        result.Should().NotBeNull();
        result!.Name.Should().Be("CustomerInput");
    }

    [Fact]
    public void GetByName_Returns_Active_Version()
    {
        var registry = CreateRegistry(
            new SchemaDescriptor { Id = "schema_01", Name = "CustomerInput", Version = 1, State = DescriptorState.Active },
            new SchemaDescriptor { Id = "schema_02", Name = "CustomerInput", Version = 2, State = DescriptorState.Draft }
        );
        var result = registry.GetByName("CustomerInput");
        result.Should().NotBeNull();
        result!.Version.Should().Be(1);
    }

    [Fact]
    public void GetActiveVersion_Returns_Highest_Active()
    {
        var registry = CreateRegistry(
            new SchemaDescriptor { Id = "schema_01", Name = "CustomerInput", Version = 1, State = DescriptorState.Active },
            new SchemaDescriptor { Id = "schema_02", Name = "CustomerInput", Version = 2, State = DescriptorState.Active },
            new SchemaDescriptor { Id = "schema_03", Name = "CustomerInput", Version = 3, State = DescriptorState.Draft }
        );
        var result = registry.GetActiveVersion("CustomerInput");
        result.Should().NotBeNull();
        result!.Version.Should().Be(2);
    }

    [Fact]
    public void GetByVersion_Returns_Exact_Match()
    {
        var registry = CreateRegistry(
            new SchemaDescriptor { Id = "schema_01", Name = "CustomerInput", Version = 1 },
            new SchemaDescriptor { Id = "schema_02", Name = "CustomerInput", Version = 2 }
        );
        var result = registry.GetByVersion("schema_02", 2);
        result.Should().NotBeNull();
        result!.Version.Should().Be(2);
    }

    [Fact]
    public void GetById_Missing_Returns_Null()
    {
        var registry = CreateRegistry();
        registry.GetById("nonexistent").Should().BeNull();
    }

    [Fact]
    public void GetAll_Returns_All_Descriptors()
    {
        var registry = CreateRegistry(
            new SchemaDescriptor { Id = "schema_01", Name = "A", Version = 1 },
            new SchemaDescriptor { Id = "schema_02", Name = "B", Version = 1 }
        );
        registry.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Build_Sets_State_To_Built()
    {
        var registry = CreateRegistry(
            new SchemaDescriptor { Id = "schema_01", Name = "A", Version = 1 }
        );
        registry.State.Should().Be(RegistryState.Built);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test framework/test/CrestCreates.Schema.Tests --filter "SchemaRegistryTests"`
Expected: FAIL — `SchemaRegistry` has no constructor taking `IRegistryValidationEngine<SchemaDescriptor>`.

- [ ] **Step 3: Rewrite SchemaRegistry to extend RegistryBase**

```csharp
// framework/src/CrestCreates.Schema/SchemaRegistry.cs
using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

public sealed class SchemaRegistry : RegistryBase<SchemaDescriptor>, ISchemaRegistry
{
    protected override string RegistryNamespace => "schema";

    public SchemaRegistry(IRegistryValidationEngine<SchemaDescriptor> validationEngine)
        : base(validationEngine) { }

    protected override RegistrySnapshot<SchemaDescriptor> BuildSnapshot(
        List<SchemaDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<SchemaDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Schema.Tests --filter "SchemaRegistryTests"`
Expected: 7 passed, 0 failed.

- [ ] **Step 5: Build Schema project**

Run: `dotnet build framework/src/CrestCreates.Schema`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
GIT_MASTER=1 git add framework/src/CrestCreates.Schema/SchemaRegistry.cs framework/test/CrestCreates.Schema.Tests/SchemaRegistryTests.cs
GIT_MASTER=1 git commit -m "feat(schema): migrate SchemaRegistry to RegistryBase with Build(providers) pattern"
```

---

### Task 4: FormRegistry Migration to RegistryBase

**Files:**
- Rewrite: `framework/src/CrestCreates.Form/FormRegistry.cs`
- Update: `framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs`

- [ ] **Step 1: Update FormRegistryTests to use Build pattern**

```csharp
// framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormRegistryTests
{
    private sealed class TestFormProvider : IDescriptorProvider<FormDescriptor>
    {
        private readonly List<FormDescriptor> _descriptors;
        public TestFormProvider(List<FormDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<FormDescriptor> GetDescriptors() => _descriptors;
    }

    private static FormRegistry CreateRegistry(params FormDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<FormDescriptor>([]);
        var registry = new FormRegistry(engine);
        registry.Build([new TestFormProvider(descriptors.ToList())]);
        return registry;
    }

    private static FormDescriptor CreateForm(string id, string name, int version) => new()
    {
        Id = id, Name = name, Version = version,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
    };

    [Fact]
    public void Build_And_GetById_Works()
    {
        var registry = CreateRegistry(CreateForm("form_01", "CustomerCreateForm", 1));
        var result = registry.GetById("form_01");
        result.Should().NotBeNull();
        result!.Name.Should().Be("CustomerCreateForm");
    }

    [Fact]
    public void Multiple_Versions_Same_Name()
    {
        var registry = CreateRegistry(
            CreateForm("f1", "CustomerForm", 1),
            CreateForm("f2", "CustomerForm", 2)
        );
        registry.GetAllByName("CustomerForm").Should().HaveCount(2);
    }

    [Fact]
    public void GetAll_Returns_All_Forms()
    {
        var registry = CreateRegistry(
            CreateForm("f1", "FormA", 1),
            CreateForm("f2", "FormB", 1)
        );
        registry.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Build_Sets_State_To_Built()
    {
        var registry = CreateRegistry(CreateForm("f1", "A", 1));
        registry.State.Should().Be(RegistryState.Built);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test framework/test/CrestCreates.Form.Tests --filter "FormRegistryTests"`
Expected: FAIL.

- [ ] **Step 3: Rewrite FormRegistry to extend RegistryBase**

```csharp
// framework/src/CrestCreates.Form/FormRegistry.cs
using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Form;

public sealed class FormRegistry : RegistryBase<FormDescriptor>, IFormRegistry
{
    protected override string RegistryNamespace => "form";

    public FormRegistry(IRegistryValidationEngine<FormDescriptor> validationEngine)
        : base(validationEngine) { }

    protected override RegistrySnapshot<FormDescriptor> BuildSnapshot(
        List<FormDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<FormDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Form.Tests --filter "FormRegistryTests"`
Expected: 4 passed, 0 failed.

- [ ] **Step 5: Build Form project**

Run: `dotnet build framework/src/CrestCreates.Form`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
GIT_MASTER=1 git add framework/src/CrestCreates.Form/FormRegistry.cs framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs
GIT_MASTER=1 git commit -m "feat(form): migrate FormRegistry to RegistryBase with Build(providers) pattern"
```

---

### Task 5: HumanTaskRegistry Migration to RegistryBase

**Files:**
- Rewrite: `framework/src/CrestCreates.HumanTask/HumanTaskRegistry.cs`
- Update: `framework/test/CrestCreates.HumanTask.Tests/HumanTaskRegistryTests.cs`

- [ ] **Step 1: Update HumanTaskRegistryTests to use Build pattern**

```csharp
// framework/test/CrestCreates.HumanTask.Tests/HumanTaskRegistryTests.cs
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskRegistryTests
{
    private sealed class TestHumanTaskProvider : IDescriptorProvider<HumanTaskDescriptor>
    {
        private readonly List<HumanTaskDescriptor> _descriptors;
        public TestHumanTaskProvider(List<HumanTaskDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<HumanTaskDescriptor> GetDescriptors() => _descriptors;
    }

    private static HumanTaskRegistry CreateRegistry(params HumanTaskDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<HumanTaskDescriptor>([]);
        var registry = new HumanTaskRegistry(engine);
        registry.Build([new TestHumanTaskProvider(descriptors.ToList())]);
        return registry;
    }

    [Fact]
    public void Build_And_GetById_Returns_Descriptor()
    {
        var registry = CreateRegistry(
            new HumanTaskDescriptor { Id = "ht_01", Name = "ManagerApproval", Version = 1 }
        );
        var result = registry.GetById("ht_01");
        result.Should().NotBeNull();
        result!.Name.Should().Be("ManagerApproval");
    }

    [Fact]
    public void GetByName_Returns_Active()
    {
        var registry = CreateRegistry(
            new HumanTaskDescriptor { Id = "ht_01", Name = "Approval", Version = 1, State = DescriptorState.Active },
            new HumanTaskDescriptor { Id = "ht_02", Name = "Approval", Version = 2, State = DescriptorState.Draft }
        );
        var result = registry.GetByName("Approval");
        result.Should().NotBeNull();
        result!.Version.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test framework/test/CrestCreates.HumanTask.Tests --filter "HumanTaskRegistryTests"`
Expected: FAIL.

- [ ] **Step 3: Rewrite HumanTaskRegistry**

```csharp
// framework/src/CrestCreates.HumanTask/HumanTaskRegistry.cs
using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class HumanTaskRegistry : RegistryBase<HumanTaskDescriptor>, IHumanTaskRegistry
{
    protected override string RegistryNamespace => "humantask";

    public HumanTaskRegistry(IRegistryValidationEngine<HumanTaskDescriptor> validationEngine)
        : base(validationEngine) { }

    protected override RegistrySnapshot<HumanTaskDescriptor> BuildSnapshot(
        List<HumanTaskDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<HumanTaskDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
```

- [ ] **Step 4: Run tests + build**

Run: `dotnet test framework/test/CrestCreates.HumanTask.Tests --filter "HumanTaskRegistryTests"` && `dotnet build framework/src/CrestCreates.HumanTask`
Expected: 2 passed. Build succeeded.

- [ ] **Step 5: Commit**

```bash
GIT_MASTER=1 git add framework/src/CrestCreates.HumanTask/HumanTaskRegistry.cs framework/test/CrestCreates.HumanTask.Tests/HumanTaskRegistryTests.cs
GIT_MASTER=1 git commit -m "feat(humantask): migrate HumanTaskRegistry to RegistryBase with Build(providers) pattern"
```

---

### Task 6: WorkflowRegistry Migration to RegistryBase

**Files:**
- Rewrite: `framework/src/CrestCreates.Workflow/WorkflowRegistry.cs`
- Update: `framework/test/CrestCreates.Workflow.Tests/WorkflowRegistryTests.cs`

- [ ] **Step 1: Update WorkflowRegistryTests to use Build pattern**

```csharp
// framework/test/CrestCreates.Workflow.Tests/WorkflowRegistryTests.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowRegistryTests
{
    private sealed class TestWorkflowProvider : IDescriptorProvider<WorkflowDescriptor>
    {
        private readonly List<WorkflowDescriptor> _descriptors;
        public TestWorkflowProvider(List<WorkflowDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<WorkflowDescriptor> GetDescriptors() => _descriptors;
    }

    private static WorkflowRegistry CreateRegistry(params WorkflowDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<WorkflowDescriptor>([]);
        var registry = new WorkflowRegistry(engine);
        registry.Build([new TestWorkflowProvider(descriptors.ToList())]);
        return registry;
    }

    [Fact]
    public void Build_And_GetById_Returns_Descriptor()
    {
        var registry = CreateRegistry(
            new WorkflowDescriptor { Id = "wf_01", Name = "Onboarding", Version = 1 }
        );
        var result = registry.GetById("wf_01");
        result.Should().NotBeNull();
        result!.Name.Should().Be("Onboarding");
    }

    [Fact]
    public void GetByName_Returns_Active()
    {
        var registry = CreateRegistry(
            new WorkflowDescriptor { Id = "wf_01", Name = "Onboarding", Version = 1, State = DescriptorState.Active },
            new WorkflowDescriptor { Id = "wf_02", Name = "Onboarding", Version = 2, State = DescriptorState.Draft }
        );
        var result = registry.GetByName("Onboarding");
        result.Should().NotBeNull();
        result!.Version.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests --filter "WorkflowRegistryTests"`
Expected: FAIL.

- [ ] **Step 3: Rewrite WorkflowRegistry**

```csharp
// framework/src/CrestCreates.Workflow/WorkflowRegistry.cs
using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowRegistry : RegistryBase<WorkflowDescriptor>, IWorkflowRegistry
{
    protected override string RegistryNamespace => "workflow";

    public WorkflowRegistry(IRegistryValidationEngine<WorkflowDescriptor> validationEngine)
        : base(validationEngine) { }

    protected override RegistrySnapshot<WorkflowDescriptor> BuildSnapshot(
        List<WorkflowDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<WorkflowDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
```

- [ ] **Step 4: Run tests + build**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests --filter "WorkflowRegistryTests"` && `dotnet build framework/src/CrestCreates.Workflow`
Expected: 2 passed. Build succeeded.

- [ ] **Step 5: Commit**

```bash
GIT_MASTER=1 git add framework/src/CrestCreates.Workflow/WorkflowRegistry.cs framework/test/CrestCreates.Workflow.Tests/WorkflowRegistryTests.cs
GIT_MASTER=1 git commit -m "feat(workflow): migrate WorkflowRegistry to RegistryBase with Build(providers) pattern"
```

---

### Task 7: Delete Static Provider Files

**Files:**
- Delete: `framework/src/CrestCreates.Schema/SchemaRegistryProvider.cs`
- Delete: `framework/src/CrestCreates.Form/FormRegistryProvider.cs`
- Delete: `framework/src/CrestCreates.HumanTask/HumanTaskRegistryProvider.cs`
- Delete: `framework/src/CrestCreates.Workflow/WorkflowRegistryProvider.cs`

- [ ] **Step 1: Move files to RecycleBin then delete**

```bash
cp framework/src/CrestCreates.Schema/SchemaRegistryProvider.cs 99_RecycleBin/SchemaRegistryProvider_Schema.cs
cp framework/src/CrestCreates.Form/FormRegistryProvider.cs 99_RecycleBin/FormRegistryProvider_Form.cs
cp framework/src/CrestCreates.HumanTask/HumanTaskRegistryProvider.cs 99_RecycleBin/HumanTaskRegistryProvider_HumanTask.cs
cp framework/src/CrestCreates.Workflow/WorkflowRegistryProvider.cs 99_RecycleBin/WorkflowRegistryProvider_Workflow.cs
rm framework/src/CrestCreates.Schema/SchemaRegistryProvider.cs
rm framework/src/CrestCreates.Form/FormRegistryProvider.cs
rm framework/src/CrestCreates.HumanTask/HumanTaskRegistryProvider.cs
rm framework/src/CrestCreates.Workflow/WorkflowRegistryProvider.cs
```

- [ ] **Step 2: Verify source projects build (will fail until SG is fixed in Task 9)**

Run: `dotnet build framework/src/CrestCreates.Schema framework/src/CrestCreates.Form framework/src/CrestCreates.HumanTask framework/src/CrestCreates.Workflow`
Expected: Build succeeded (provider files were only consumed by SG-generated code in obj/).

- [ ] **Step 3: Commit**

```bash
GIT_MASTER=1 git add 99_RecycleBin/ framework/src/CrestCreates.Schema/SchemaRegistryProvider.cs framework/src/CrestCreates.Form/FormRegistryProvider.cs framework/src/CrestCreates.HumanTask/HumanTaskRegistryProvider.cs framework/src/CrestCreates.Workflow/WorkflowRegistryProvider.cs
GIT_MASTER=1 git commit -m "refactor: delete static RegistryProvider classes — replaced by DescriptorProviderRegistry"
```

---

### Task 8: MetadataBootstrapper

**Files:**
- Create: `framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs`

- [ ] **Step 1: Create MetadataBootstrapper.cs**

```csharp
// framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs
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
        IEventRegistry eventRegistry)
    {
        schemaRegistry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        formRegistry.Build(DescriptorProviderRegistry.GetProviders<FormDescriptor>());
        humanTaskRegistry.Build(DescriptorProviderRegistry.GetProviders<HumanTaskDescriptor>());
        workflowRegistry.Build(DescriptorProviderRegistry.GetProviders<WorkflowDescriptor>());
        eventRegistry.Build(DescriptorProviderRegistry.GetProviders<GeneratedEventDescriptor>());
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
GIT_MASTER=1 git add framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs
GIT_MASTER=1 git commit -m "feat(metadata): add MetadataBootstrapper.BuildAll() — unified registry build orchestrator"
```

---

### Task 9: Source Generator Update

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs`

- [ ] **Step 1: Rewrite GenerateRegistries to generate IDIscriptorProvider<T> classes**

Replace the entire `GenerateRegistries` method body (lines 277-394) with the new implementation:

```csharp
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using CrestCreates.Metadata;");
        sb.AppendLine("using CrestCreates.Metadata.Abstractions;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        if (hasSchema) sb.AppendLine("using CrestCreates.Schema.Abstractions;");
        if (hasForm) sb.AppendLine("using CrestCreates.Form.Abstractions;");
        if (hasHumanTask) sb.AppendLine("using CrestCreates.HumanTask.Abstractions;");
        if (hasWorkflow) sb.AppendLine("using CrestCreates.Workflow.Abstractions;");
        if (hasEvent) sb.AppendLine("using CrestCreates.Event.Abstractions;");
        sb.AppendLine();
        sb.AppendLine("namespace CrestCreates.Generated;");
        sb.AppendLine();

        // Schema Provider
        if (schemas.Any(s => s != null))
        {
            sb.AppendLine("internal sealed class GeneratedSchemaProvider : IDescriptorProvider<SchemaDescriptor>");
            sb.AppendLine("{");
            sb.AppendLine("    public IReadOnlyList<SchemaDescriptor> GetDescriptors() => new List<SchemaDescriptor>");
            sb.AppendLine("    {");
            foreach (var schema in schemas)
            {
                if (schema == null) continue;
                sb.AppendLine($"        new SchemaDescriptor");
                sb.AppendLine("        {");
                sb.AppendLine($"            Id = \"{schema.Id}\",");
                sb.AppendLine($"            Name = \"{schema.Name}\",");
                sb.AppendLine($"            Version = {schema.Version},");
                sb.AppendLine($"            Fields = new List<SchemaFieldDescriptor>");
                sb.AppendLine("            {");
                foreach (var field in schema.Fields)
                {
                    sb.AppendLine($"                new SchemaFieldDescriptor");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    Name = \"{field.Name}\",");
                    sb.AppendLine($"                    FieldType = \"{field.FieldType}\",");
                    sb.AppendLine($"                    IsNullable = {field.IsNullable.ToString().ToLowerInvariant()},");
                    sb.AppendLine($"                    IsRequired = {field.IsRequired.ToString().ToLowerInvariant()},");
                    sb.AppendLine($"                    IsCollection = {field.IsCollection.ToString().ToLowerInvariant()},");
                    sb.AppendLine("                },");
                }
                sb.AppendLine("            }");
                sb.AppendLine("        },");
            }
            sb.AppendLine("    };");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Form Provider (new)
        if (forms.Any(f => f != null))
        {
            sb.AppendLine("internal sealed class GeneratedFormProvider : IDescriptorProvider<FormDescriptor>");
            sb.AppendLine("{");
            sb.AppendLine("    public IReadOnlyList<FormDescriptor> GetDescriptors() => new List<FormDescriptor>");
            sb.AppendLine("    {");
            foreach (var form in forms)
            {
                if (form == null) continue;
                sb.AppendLine($"        new FormDescriptor");
                sb.AppendLine("        {");
                sb.AppendLine($"            Id = \"{form.Id}\",");
                sb.AppendLine($"            Name = \"{form.Name}\",");
                sb.AppendLine($"            Version = {form.Version},");
                sb.AppendLine($"            State = DescriptorState.Active,");
                sb.AppendLine("        },");
            }
            sb.AppendLine("    };");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // HumanTask Provider (new)
        if (humanTasks.Any(h => h != null))
        {
            sb.AppendLine("internal sealed class GeneratedHumanTaskProvider : IDescriptorProvider<HumanTaskDescriptor>");
            sb.AppendLine("{");
            sb.AppendLine("    public IReadOnlyList<HumanTaskDescriptor> GetDescriptors() => new List<HumanTaskDescriptor>");
            sb.AppendLine("    {");
            foreach (var ht in humanTasks)
            {
                if (ht == null) continue;
                sb.AppendLine($"        new HumanTaskDescriptor");
                sb.AppendLine("        {");
                sb.AppendLine($"            Id = \"{ht.Id}\",");
                sb.AppendLine($"            Name = \"{ht.Name}\",");
                sb.AppendLine($"            Version = {ht.Version},");
                sb.AppendLine($"            State = DescriptorState.Active,");
                sb.AppendLine("        },");
            }
            sb.AppendLine("    };");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Workflow Provider (new)
        if (workflows.Any(w => w != null))
        {
            sb.AppendLine("internal sealed class GeneratedWorkflowProvider : IDescriptorProvider<WorkflowDescriptor>");
            sb.AppendLine("{");
            sb.AppendLine("    public IReadOnlyList<WorkflowDescriptor> GetDescriptors() => new List<WorkflowDescriptor>");
            sb.AppendLine("    {");
            foreach (var wf in workflows)
            {
                if (wf == null) continue;
                sb.AppendLine($"        new WorkflowDescriptor");
                sb.AppendLine("        {");
                sb.AppendLine($"            Id = \"{wf.Id}\",");
                sb.AppendLine($"            Name = \"{wf.Name}\",");
                sb.AppendLine($"            Version = {wf.Version},");
                sb.AppendLine($"            State = DescriptorState.Active,");
                sb.AppendLine("        },");
            }
            sb.AppendLine("    };");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Event Provider (unified — replaces IEventDescriptorProvider)
        if (events.Any(e => e != null))
        {
            sb.AppendLine("internal sealed class GeneratedEventProvider : IDescriptorProvider<GeneratedEventDescriptor>");
            sb.AppendLine("{");
            sb.AppendLine("    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() => new List<GeneratedEventDescriptor>");
            sb.AppendLine("    {");
            foreach (var evt in events)
            {
                if (evt == null) continue;
                sb.AppendLine($"        new GeneratedEventDescriptor");
                sb.AppendLine("        {");
                sb.AppendLine($"            Id = \"{evt.Id}\",");
                sb.AppendLine($"            Name = \"{evt.Name}\",");
                sb.AppendLine($"            Version = {evt.Version},");
                sb.AppendLine($"            State = DescriptorState.Active,");
                sb.AppendLine($"            PayloadType = typeof(object),");
                sb.AppendLine($"            Scope = EventScope.Integration,");
                sb.AppendLine($"            Reliability = EventReliability.AtLeastOnce,");
                sb.AppendLine($"            Importance = EventImportance.{evt.Importance},");
                sb.AppendLine($"            ChangeKind = SchemaChangeKind.{evt.ChangeKind},");
                sb.AppendLine("        },");
            }
            sb.AppendLine("    };");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // ModuleInitializer registration
        sb.AppendLine("internal static class GeneratedDescriptorRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    internal static void Register()");
        sb.AppendLine("    {");
        if (schemas.Any(s => s != null))
            sb.AppendLine($"        DescriptorProviderRegistry.Register(new GeneratedSchemaProvider());");
        if (forms.Any(f => f != null))
            sb.AppendLine($"        DescriptorProviderRegistry.Register(new GeneratedFormProvider());");
        if (humanTasks.Any(h => h != null))
            sb.AppendLine($"        DescriptorProviderRegistry.Register(new GeneratedHumanTaskProvider());");
        if (workflows.Any(w => w != null))
            sb.AppendLine($"        DescriptorProviderRegistry.Register(new GeneratedWorkflowProvider());");
        if (events.Any(e => e != null))
            sb.AppendLine($"        DescriptorProviderRegistry.Register(new GeneratedEventProvider());");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("GeneratedDescriptorRegistry.g.cs", sb.ToString());
```

- [ ] **Step 2: Build the source generator project**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator`
Expected: Build succeeded (netstandard2.0).

- [ ] **Step 3: Build consumer project to verify generated code compiles**

⚠️ **RISK**: This is the critical verification step.

Clean and rebuild:
```bash
dotnet clean samples/LibraryManagement/LibraryManagement.Domain
dotnet build samples/LibraryManagement/LibraryManagement.Domain
```

Expected: Build succeeded. If error — check that generated code references `CrestCreates.Metadata` and `CrestCreates.Metadata.Abstractions` are available.

- [ ] **Step 4: Build full framework to check all consumers**

Run: `dotnet build framework/src/`
Expected: All projects build. Ignore pre-existing `SchemaRegistryProvider` errors from samples (they use stale generated code — `dotnet clean` first).

- [ ] **Step 5: Commit**

```bash
GIT_MASTER=1 git add framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs
GIT_MASTER=1 git commit -m "refactor(sg): generate IDescriptorProvider<T> classes instead of XxxRegistryProvider.Register() — unified Event, added Form/HumanTask/Workflow generation"
```

---

### Task 10: Metadata Reference Validation Tests

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorReferenceValidationTests.cs`
- Modify: `framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj` (add project references)

**Prerequisites:** Metadata.Tests needs explicit project references to Schema, Form, HumanTask, Workflow implementation projects (for `SchemaRegistry`, `FormRegistry`, etc. concrete types). Add to csproj:
```xml
<ProjectReference Include="..\..\src\CrestCreates.Schema\CrestCreates.Schema.csproj" />
<ProjectReference Include="..\..\src\CrestCreates.Form\CrestCreates.Form.csproj" />
<ProjectReference Include="..\..\src\CrestCreates.HumanTask\CrestCreates.HumanTask.csproj" />
<ProjectReference Include="..\..\src\CrestCreates.Workflow\CrestCreates.Workflow.csproj" />
```

> **Scope note**: Spec R2/R4/R6 ("Build 失败") tests require a cross-registry DescriptorRef validator, which is **Phase 5+** work. Task 10 covers R1/R3/R5 ("Build 成功") — verifying migrated registries accept well-formed descriptors.

- [ ] **Step 1: Create DescriptorReferenceValidationTests.cs**

```csharp
// framework/test/CrestCreates.Metadata.Tests/DescriptorReferenceValidationTests.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorReferenceValidationTests
{
    private sealed class ListProvider<T> : IDescriptorProvider<T> where T : class, IDescriptor
    {
        private readonly List<T> _descriptors;
        public ListProvider(List<T> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<T> GetDescriptors() => _descriptors;
    }

    [Fact]
    public void Form_ReferencesSchema_Existing_Ok()
    {
        var schemaRegistry = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
        var formRegistry = new FormRegistry(new RegistryValidationEngine<FormDescriptor>([]));

        var schema = new SchemaDescriptor { Id = "schema_01", Name = "Customer", Version = 1 };
        var form = new FormDescriptor { Id = "form_01", Name = "CustomerForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1) };

        schemaRegistry.Build([new ListProvider<SchemaDescriptor>([schema])]);
        formRegistry.Build([new ListProvider<FormDescriptor>([form])]);

        formRegistry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void Workflow_ReferencesCapability_Existing_Ok()
    {
        var capRegistry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
        var wfRegistry = new WorkflowRegistry(new RegistryValidationEngine<WorkflowDescriptor>([]));

        var cap = new CapabilityDescriptor { Id = "cap_01", Name = "Create Customer", Version = 1 };
        var wf = new WorkflowDescriptor { Id = "wf_01", Name = "Onboarding", Version = 1,
            Steps = new List<WorkflowStep> { new() { Id = "step_01", Name = "Create",
                Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1) } } } };

        capRegistry.Build([new ListProvider<CapabilityDescriptor>([cap])]);
        wfRegistry.Build([new ListProvider<WorkflowDescriptor>([wf])]);

        wfRegistry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void HumanTask_ReferencesForm_Existing_Ok()
    {
        var formRegistry = new FormRegistry(new RegistryValidationEngine<FormDescriptor>([]));
        var htRegistry = new HumanTaskRegistry(new RegistryValidationEngine<HumanTaskDescriptor>([]));

        var form = new FormDescriptor { Id = "form_01", Name = "ApprovalForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1) };
        var ht = new HumanTaskDescriptor { Id = "ht_01", Name = "Approval", Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1) };

        formRegistry.Build([new ListProvider<FormDescriptor>([form])]);
        htRegistry.Build([new ListProvider<HumanTaskDescriptor>([ht])]);

        htRegistry.State.Should().Be(RegistryState.Built);
    }
}
```

- [ ] **Step 2: Build and run tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests --filter "DescriptorReferenceValidationTests"`
Expected: 3 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
GIT_MASTER=1 git add framework/test/CrestCreates.Metadata.Tests/DescriptorReferenceValidationTests.cs
GIT_MASTER=1 git commit -m "test(metadata): add DescriptorReferenceValidation tests — Form→Schema, Workflow→Capability, HumanTask→Form cross-registry validation"
```

---

### Task 11: Capability End-to-End Tests

**Files:**
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityEndToEndTests.cs`

- [ ] **Step 1: Create CapabilityEndToEndTests.cs**

```csharp
// framework/test/CrestCreates.Capability.Tests/CapabilityEndToEndTests.cs
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Internal;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityEndToEndTests
{
    private sealed class TestProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly List<CapabilityDescriptor> _descriptors;
        public TestProvider(List<CapabilityDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
    }

    private static (CapabilityRegistry, ICapabilityPipeline, InMemoryCapabilityAuditStore, IServiceProvider) CreateE2EPipeline(
        params CapabilityDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestProvider(descriptors.ToList())]);

        var auditStore = new InMemoryCapabilityAuditStore();
        var resolver = new CapabilityHandlerResolver();
        var versionResolver = new DefaultCapabilityVersionResolver(registry);
        var capResolver = new DefaultCapabilityResolver(versionResolver);

        var services = new ServiceCollection();
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityHandlerResolver>(resolver);
        services.AddSingleton<ICapabilityAuditStore>(auditStore);
        services.AddSingleton(new CapabilityPipelineBuilder());
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        return (registry, sp.GetRequiredService<ICapabilityPipeline>(), auditStore, sp);
    }

    [Fact]
    public async Task E2E_Execute_ReturnsSuccess_AndAuditRecorded()
    {
        var (_, pipeline, audit, _) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "test.echo", Name = "Echo", Version = 1,
                CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active }
        );
        var resolver = new CapabilityHandlerResolver();
        resolver.Register("test.echo", new EchoInvoker());

        var result = await pipeline.ExecuteAsync("test.echo", input: "hello",
            configureContext: ctx => ctx.InvocationSource = InvocationSource.Http);

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("ECHO: hello");
        var records = audit.GetRecords();
        records.Should().HaveCount(1);
        records[0].IsSuccess.Should().BeTrue();
        records[0].Duration.Should().BePositive();
        records[0].CorrelationId.Should().NotBeNullOrEmpty();
        records[0].ExecutionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task E2E_WithTenantAndUser_PopulatesAuditContext()
    {
        var (_, pipeline, audit, sp) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "test.echo", Name = "Echo", Version = 1,
                CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active }
        );
        var resolver = new CapabilityHandlerResolver();
        resolver.Register("test.echo", new EchoInvoker());

        var tenantMock = new Mock<ITenantContext>();
        tenantMock.Setup(t => t.CurrentTenantId).Returns("tenant_42");
        var userMock = new Mock<ICurrentUser>();
        userMock.Setup(u => u.Id).Returns("user_77");

        var dispatcher = new CapabilityDispatcher(
            new DefaultCapabilityResolver(new DefaultCapabilityVersionResolver(
                sp.GetRequiredService<ICapabilityRegistry>())),
            pipeline,
            tenantMock.Object,
            userMock.Object);

        await dispatcher.DispatchAsync("test.echo", InvocationSource.Workflow, input: "test");

        var records = audit.GetRecords();
        records[0].TenantId.Should().Be("tenant_42");
        records[0].UserId.Should().Be("user_77");
    }

    [Fact]
    public async Task E2E_CapabilityNotFound_ReturnsErrorCode()
    {
        var (_, pipeline, audit, _) = CreateE2EPipeline();

        var result = await pipeline.ExecuteAsync("nonexistent");

        result.ErrorCode.Should().Be("CAPABILITY_NOT_FOUND");
        audit.GetRecords()[0].ErrorCode.Should().Be("CAPABILITY_NOT_FOUND");
    }

    [Fact]
    public async Task E2E_IdDifferentFromName_PreservesBoth()
    {
        var (_, pipeline, audit, _) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "echo.v2", Name = "Echo Command", Version = 1,
                CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active }
        );
        var resolver = new CapabilityHandlerResolver();
        resolver.Register("echo.v2", new EchoInvoker());

        var result = await pipeline.ExecuteAsync("echo.v2");

        result.IsSuccess.Should().BeTrue();
        var records = audit.GetRecords();
        records[0].CapabilityId.Should().Be("echo.v2");
        records[0].CapabilityName.Should().Be("Echo Command");
    }

    [Fact]
    public async Task E2E_MultiVersion_ResolverReturnsActive()
    {
        var (registry, pipeline, audit, _) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "echo.v1", Name = "Echo", Version = 1, CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active },
            new CapabilityDescriptor { Id = "echo.v2", Name = "Echo", Version = 2, CapabilityKind = CapabilityKind.Query, State = DescriptorState.Deprecated }
        );
        var resolver = new CapabilityHandlerResolver();
        resolver.Register("echo.v1", new EchoInvoker());
        resolver.Register("echo.v2", new FailingEchoInvoker());

        var versionResolver = new DefaultCapabilityVersionResolver(registry);
        var resolved = versionResolver.Resolve(new CapabilityRef { Id = "echo.v1" });
        resolved.Version.Should().Be(1);
        resolved.State.Should().Be(DescriptorState.Active);
    }

    [Fact]
    public async Task Legacy_NameLookup_BackwardCompatibility()
    {
        var (_, pipeline, audit, _) = CreateE2EPipeline(
            new CapabilityDescriptor { Id = "echo.v2", Name = "Echo Command", Version = 1,
                CapabilityKind = CapabilityKind.Query, State = DescriptorState.Active }
        );
        var resolver = new CapabilityHandlerResolver();
        resolver.Register("echo.v2", new EchoInvoker());

        var result = await pipeline.ExecuteAsync("Echo Command");
        result.IsSuccess.Should().BeTrue();
    }

    // Test handler invokers
    private sealed class EchoInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>($"ECHO: {input}");
    }

    private sealed class FailingEchoInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>($"WRONG: {input}");
    }
}
```

- [ ] **Step 2: Build and run tests**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests --filter "CapabilityEndToEndTests"`
Expected: 6 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
GIT_MASTER=1 git add framework/test/CrestCreates.Capability.Tests/CapabilityEndToEndTests.cs
GIT_MASTER=1 git commit -m "test(capability): add E2E tests — dispatch→pipeline→handler→audit full chain with Id/Name, multi-version, tenant/user"
```

---

### Task 12: Id/Name Semantic Cleanup

**Files:**
- Modify: `framework/src/CrestCreates.Capability.Abstractions/ICapabilityPipeline.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityPipeline.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityHandlerResolver.cs`
- Modify: `framework/test/CrestCreates.Capability.Tests/CapabilityPipelineTests.cs`
- Modify: `framework/test/CrestCreates.Capability.Tests/CapabilityDispatcherTests.cs`
- Modify: `framework/test/CrestCreates.Capability.Tests/CapabilityEndToEndTests.cs`

- [ ] **Step 1: Rename ICapabilityPipeline parameter**

```csharp
// framework/src/CrestCreates.Capability.Abstractions/ICapabilityPipeline.cs line 6:
string capabilityName → string capabilityIdOrName
```

- [ ] **Step 2: Rename CapabilityPipeline parameter and all local references**

```csharp
// framework/src/CrestCreates.Capability/CapabilityPipeline.cs:
- Parameter: string capabilityName → string capabilityIdOrName
- All local variable/string interpolation references: capabilityName → capabilityIdOrName
```

- [ ] **Step 3: Rename CapabilityHandlerResolver parameters**

```csharp
// framework/src/CrestCreates.Capability/CapabilityHandlerResolver.cs:
- Register parameter: string capabilityName → string capabilityId
- Resolve parameter: string capabilityName → string capabilityId
```

- [ ] **Step 4: Update all test files — find/replace capabilityName references**

```bash
# In test files
cd framework/test/CrestCreates.Capability.Tests/
# Pipeline tests: ExecuteAsync("test.echo") calls unchanged (string literal, not parameter name)
# Only update the mock Setup parameter references:
sed -i 's/\.ExecuteAsync(capabilityName,/.ExecuteAsync(capabilityIdOrName,/g' CapabilityPipelineTests.cs
# Dispatcher parameter rename — handlerResolver.Register("...") is a string call, unchanged
```

- [ ] **Step 5: Build and run all Capability tests**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests`
Expected: All 96 tests pass (90 existing + 6 new E2E).

- [ ] **Step 6: Commit**

```bash
GIT_MASTER=1 git add framework/src/CrestCreates.Capability.Abstractions/ICapabilityPipeline.cs framework/src/CrestCreates.Capability/CapabilityPipeline.cs framework/src/CrestCreates.Capability/CapabilityHandlerResolver.cs framework/test/CrestCreates.Capability.Tests/
GIT_MASTER=1 git commit -m "refactor(capability): rename capabilityName→capabilityIdOrName in Pipeline, capabilityName→capabilityId in HandlerResolver — Id-first semantics"
```

---

### Task 13: Documentation Update

**Files:**
- Modify: `docs/Feature/UnifiedMetadataModel/2026-06-09-unified-metadata-model-architecture-summary.md`
- Modify: `docs/Feature/UnifiedMetadataModel/usage-guide.md`

- [ ] **Step 1: Update architecture summary — Registry status and test counts**

In the architecture summary:
- Update test count: Capability.Tests 90 → 96
- Update Registry status: all 6 marked as "✅ Phase 4a" (unified under RegistryBase)
- Add DescriptorProviderRegistry and MetadataBootstrapper to Section 2 project structure

- [ ] **Step 2: Update usage guide — Build pattern for all registries**

In the usage guide Section 8:
- Add note that all 6 registries now use `Build(providers)` pattern
- Update code examples from `new XxxRegistry()` + `.Register()` to `new XxxRegistry(engine)` + `.Build(providers)`

- [ ] **Step 3: Commit**

```bash
GIT_MASTER=1 git add docs/Feature/UnifiedMetadataModel/
GIT_MASTER=1 git commit -m "docs: update Phase 4a registry status and test counts in architecture docs"
```

---

### Task 14: Final Verification

- [ ] **Step 1: Full framework build**

Run: `dotnet build framework/src/`
Expected: All source projects build (ignore pre-existing sample errors from stale generated code).

- [ ] **Step 2: Run all affected test projects**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests
dotnet test framework/test/CrestCreates.Capability.Tests
dotnet test framework/test/CrestCreates.Schema.Tests
dotnet test framework/test/CrestCreates.Form.Tests
dotnet test framework/test/CrestCreates.HumanTask.Tests
```

Expected: All pass.

- [ ] **Step 3: Verify consumer project builds**

```bash
dotnet clean samples/LibraryManagement/LibraryManagement.Domain
dotnet build samples/LibraryManagement/LibraryManagement.Domain
```
Expected: Build succeeded (SG now generates correct code).

- [ ] **Step 4: Run git status to confirm clean state**

```bash
GIT_MASTER=1 git status
```
Expected: Clean working tree.

- [ ] **Step 5: Final commit**

```bash
GIT_MASTER=1 git commit --allow-empty -m "chore: Phase 4a Main Chain Closure — all registries unified, all tests pass"
```
