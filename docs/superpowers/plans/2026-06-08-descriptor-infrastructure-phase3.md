# Phase 3: Event, Form, HumanTask, Workflow Descriptors — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement all remaining descriptor types (Event, Form, HumanTask, Workflow) with registries, provider interfaces, source generator expansion, and tests — completing the four-pillar metadata model and the instance infrastructure layer.

**Architecture:** Follow the established Abstractions + Implementation + Tests pattern from Phase 1 and 2. Each descriptor type gets two projects (`.Abstractions` and implementation), a typed registry implementing `IVersionedDescriptorRegistry<T>`, a static `RegistryProvider` hook, and an `IDescriptorProvider` interface for discovery. Event and Workflow are the last two pillars. Form and HumanTask are instance infrastructure descriptors. InteractionTarget is a sealed-class hierarchy in Workflow.Abstractions since it's the polymorphic binding for WorkflowStep.

**Tech Stack:** .NET 10, C# 13, System.Text.Json, xUnit + FluentAssertions, ConcurrentDictionary registries, compile-time source generation via Roslyn IIncrementalGenerator

**Dependency Order (critical):**
```
Event.Abstractions  (→ Metadata.Abstractions + Schema.Abstractions)
Form.Abstractions   (→ Metadata.Abstractions + Schema.Abstractions)
HumanTask.Abstractions (→ Metadata.Abstractions + Schema.Abstractions + Form.Abstractions + Capability.Abstractions)
Workflow.Abstractions  (→ Metadata.Abstractions + Schema.Abstractions + Capability.Abstractions + HumanTask.Abstractions)
```

Tasks must execute in this order because HumanTask depends on Form, and Workflow depends on HumanTask.

---

### Task 0: Event.Abstractions — Enums + EventDescriptor + IEventRegistry

**Files:**
- Create: `framework/src/CrestCreates.Event.Abstractions/CrestCreates.Event.Abstractions.csproj`
- Create: `framework/src/CrestCreates.Event.Abstractions/EventCategory.cs`
- Create: `framework/src/CrestCreates.Event.Abstractions/EventSemantic.cs`
- Create: `framework/src/CrestCreates.Event.Abstractions/EventImportance.cs`
- Create: `framework/src/CrestCreates.Event.Abstractions/EventDescriptor.cs`
- Create: `framework/src/CrestCreates.Event.Abstractions/IEventRegistry.cs`
- Create: `framework/src/CrestCreates.Event.Abstractions/IEventDescriptorProvider.cs`

- [ ] **Step 1: Create CrestCreates.Event.Abstractions.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Event.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write EventCategory.cs**

```csharp
namespace CrestCreates.Event.Abstractions;

public enum EventCategory
{
    Capability,
    Domain,
    Integration
}
```

- [ ] **Step 3: Write EventSemantic.cs**

```csharp
namespace CrestCreates.Event.Abstractions;

public enum EventSemantic
{
    Fact,
    StateTransition,
    Notification
}
```

- [ ] **Step 4: Write EventImportance.cs**

```csharp
namespace CrestCreates.Event.Abstractions;

public enum EventImportance
{
    Critical,
    Business,
    Operational,
    Ephemeral
}
```

- [ ] **Step 5: Write EventDescriptor.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Event.Abstractions;

public sealed class EventDescriptor : IVersionedDescriptor
{
    public DescriptorKind Kind => DescriptorKind.Event;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public int Version { get; init; }

    public VersionedDescriptorRef<SchemaDescriptor> PayloadSchema { get; init; }
    public EventCategory Category { get; init; }
    public EventSemantic Semantic { get; init; }
    public EventImportance Importance { get; init; }
    public SchemaChangeKind ChangeKind { get; init; }
}
```

- [ ] **Step 6: Write IEventRegistry.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Event.Abstractions;

public interface IEventRegistry : IVersionedDescriptorRegistry<EventDescriptor>
{
    IReadOnlyList<EventDescriptor> GetByCategory(EventCategory category);
    IReadOnlyList<EventDescriptor> GetBySemantic(EventSemantic semantic);
    IReadOnlyList<EventDescriptor> GetByImportance(EventImportance importance);
}
```

- [ ] **Step 7: Write IEventDescriptorProvider.cs**

```csharp
namespace CrestCreates.Event.Abstractions;

public interface IEventDescriptorProvider
{
    EventDescriptor GetEventDescriptor();
}
```

- [ ] **Step 8: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Event.Abstractions/CrestCreates.Event.Abstractions.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add framework/src/CrestCreates.Event.Abstractions/
git commit -m "feat: add Event.Abstractions — EventDescriptor, enums, IEventRegistry, IEventDescriptorProvider"
```

---

### Task 1: Event — EventRegistry + EventRegistryProvider

**Files:**
- Create: `framework/src/CrestCreates.Event/CrestCreates.Event.csproj`
- Create: `framework/src/CrestCreates.Event/EventRegistry.cs`
- Create: `framework/src/CrestCreates.Event/EventRegistryProvider.cs`

- [ ] **Step 1: Create CrestCreates.Event.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Event</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Event.Abstractions\CrestCreates.Event.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write EventRegistry.cs**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Event.Abstractions;

namespace CrestCreates.Event;

public sealed class EventRegistry : IEventRegistry
{
    private readonly ConcurrentDictionary<string, EventDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<EventDescriptor>> _byName = new();
    private readonly ConcurrentDictionary<EventCategory, List<EventDescriptor>> _byCategory = new();
    private readonly ConcurrentDictionary<EventSemantic, List<EventDescriptor>> _bySemantic = new();
    private readonly ConcurrentDictionary<EventImportance, List<EventDescriptor>> _byImportance = new();

    public void Register(EventDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
        _byCategory.GetOrAdd(descriptor.Category, _ => new()).Add(descriptor);
        _bySemantic.GetOrAdd(descriptor.Semantic, _ => new()).Add(descriptor);
        _byImportance.GetOrAdd(descriptor.Importance, _ => new()).Add(descriptor);
    }

    public EventDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public EventDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public EventDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public EventDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
            : null;

    public EventDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<EventDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<EventDescriptor>();

    public IReadOnlyList<EventDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<EventDescriptor>();

    public IReadOnlyList<EventDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();

    public IReadOnlyList<EventDescriptor> GetByCategory(EventCategory category) =>
        _byCategory.TryGetValue(category, out var list) ? list.AsReadOnly() : Array.Empty<EventDescriptor>();

    public IReadOnlyList<EventDescriptor> GetBySemantic(EventSemantic semantic) =>
        _bySemantic.TryGetValue(semantic, out var list) ? list.AsReadOnly() : Array.Empty<EventDescriptor>();

    public IReadOnlyList<EventDescriptor> GetByImportance(EventImportance importance) =>
        _byImportance.TryGetValue(importance, out var list) ? list.AsReadOnly() : Array.Empty<EventDescriptor>();
}
```

- [ ] **Step 3: Write EventRegistryProvider.cs**

```csharp
using CrestCreates.Event.Abstractions;

namespace CrestCreates.Event;

public static class EventRegistryProvider
{
    private static EventRegistry? _registry;

    public static void SetRegistry(EventRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(EventDescriptor descriptor)
    {
        _registry?.Register(descriptor);
    }
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Event/CrestCreates.Event.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Event/
git commit -m "feat: add Event — EventRegistry, EventRegistryProvider"
```

---

### Task 2: Event Tests

**Files:**
- Create: `framework/test/CrestCreates.Event.Tests/CrestCreates.Event.Tests.csproj`
- Create: `framework/test/CrestCreates.Event.Tests/EventDescriptorTests.cs`
- Create: `framework/test/CrestCreates.Event.Tests/EventRegistryTests.cs`

- [ ] **Step 1: Create CrestCreates.Event.Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Event.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Event.Tests</AssemblyName>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Event\CrestCreates.Event.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write EventDescriptorTests.cs**

```csharp
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Event.Tests;

public class EventDescriptorTests
{
    [Fact]
    public void EventDescriptor_Kind_Is_Event()
    {
        var evt = new EventDescriptor
        {
            Id = "evt_01",
            Name = "crm.customer.created",
            Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Category = EventCategory.Domain,
            Semantic = EventSemantic.Fact,
            Importance = EventImportance.Critical
        };

        evt.Kind.Should().Be(DescriptorKind.Event);
    }

    [Fact]
    public void EventDescriptor_Implements_IVersionedDescriptor()
    {
        var evt = new EventDescriptor
        {
            Id = "evt_01",
            Name = "crm.customer.created",
            Version = 3
        };

        IVersionedDescriptor vd = evt;
        vd.Version.Should().Be(3);
    }

    [Fact]
    public void EventDescriptor_Defaults_State_To_Active()
    {
        var evt = new EventDescriptor
        {
            Id = "evt_01",
            Name = "crm.customer.created",
            Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };

        evt.State.Should().Be(DescriptorState.Active);
    }

    [Fact]
    public void EventDescriptor_Classification_Is_Preserved()
    {
        var evt = new EventDescriptor
        {
            Id = "evt_01",
            Name = "crm.customer.created",
            Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Category = EventCategory.Domain,
            Semantic = EventSemantic.StateTransition,
            Importance = EventImportance.Business
        };

        evt.Category.Should().Be(EventCategory.Domain);
        evt.Semantic.Should().Be(EventSemantic.StateTransition);
        evt.Importance.Should().Be(EventImportance.Business);
    }

    [Fact]
    public void EventDescriptor_ChangeKind_Is_Declared()
    {
        var evt = new EventDescriptor
        {
            Id = "evt_01",
            Name = "crm.customer.created",
            Version = 2,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 2),
            ChangeKind = SchemaChangeKind.Additive
        };

        evt.ChangeKind.Should().Be(SchemaChangeKind.Additive);
    }
}
```

- [ ] **Step 3: Write EventRegistryTests.cs**

```csharp
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Event.Tests;

public class EventRegistryTests
{
    private static EventDescriptor CreateEvent(string id, string name, int version,
        EventCategory category = EventCategory.Domain,
        EventSemantic semantic = EventSemantic.Fact,
        EventImportance importance = EventImportance.Business)
    {
        return new EventDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Category = category,
            Semantic = semantic,
            Importance = importance
        };
    }

    [Fact]
    public void GetById_Returns_Correct_Event()
    {
        var registry = new EventRegistry();
        var evt = CreateEvent("evt_01", "crm.customer.created", 1);
        registry.Register(evt);

        var result = registry.GetById("evt_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("crm.customer.created");
    }

    [Fact]
    public void GetByCategory_Filters_Correctly()
    {
        var registry = new EventRegistry();
        registry.Register(CreateEvent("e1", "evt.domain", 1, EventCategory.Domain));
        registry.Register(CreateEvent("e2", "evt.integration", 1, EventCategory.Integration));
        registry.Register(CreateEvent("e3", "evt.capability", 1, EventCategory.Capability));

        var domain = registry.GetByCategory(EventCategory.Domain);
        domain.Should().HaveCount(1);
        domain[0].Id.Should().Be("e1");
    }

    [Fact]
    public void GetBySemantic_Filters_Correctly()
    {
        var registry = new EventRegistry();
        registry.Register(CreateEvent("e1", "evt.fact", 1, semantic: EventSemantic.Fact));
        registry.Register(CreateEvent("e2", "evt.transition", 1, semantic: EventSemantic.StateTransition));

        var facts = registry.GetBySemantic(EventSemantic.Fact);
        facts.Should().HaveCount(1);
    }

    [Fact]
    public void GetByImportance_Filters_Correctly()
    {
        var registry = new EventRegistry();
        registry.Register(CreateEvent("e1", "evt.critical", 1, importance: EventImportance.Critical));
        registry.Register(CreateEvent("e2", "evt.ephemeral", 1, importance: EventImportance.Ephemeral));

        var critical = registry.GetByImportance(EventImportance.Critical);
        critical.Should().HaveCount(1);
    }

    [Fact]
    public void GetActiveVersion_Returns_Highest_Active_Version()
    {
        var registry = new EventRegistry();
        registry.Register(CreateEvent("e1", "evt.test", 1));
        registry.Register(CreateEvent("e2", "evt.test", 2));
        registry.Register(new EventDescriptor
        {
            Id = "e3", Name = "evt.test", Version = 3, State = DescriptorState.Deprecated,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        });

        var active = registry.GetActiveVersion("evt.test");
        active.Should().NotBeNull();
        active!.Version.Should().Be(2);
    }

    [Fact]
    public void GetLatestVersion_Returns_Highest_Version_Regardless_Of_State()
    {
        var registry = new EventRegistry();
        registry.Register(CreateEvent("e1", "evt.test", 1));
        registry.Register(new EventDescriptor
        {
            Id = "e3", Name = "evt.test", Version = 5, State = DescriptorState.Deprecated,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        });

        var latest = registry.GetLatestVersion("evt.test");
        latest.Should().NotBeNull();
        latest!.Version.Should().Be(5);
    }
}
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet test framework/test/CrestCreates.Event.Tests/CrestCreates.Event.Tests.csproj`
Expected: Build succeeded, 11 tests passed.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Event.Tests/
git commit -m "feat: add Event.Tests — 11 tests for EventDescriptor and EventRegistry"
```

---

### Task 3: Form.Abstractions — FormFieldDescriptor + FormDescriptor + IFormRegistry

**Files:**
- Create: `framework/src/CrestCreates.Form.Abstractions/CrestCreates.Form.Abstractions.csproj`
- Create: `framework/src/CrestCreates.Form.Abstractions/FormFieldDescriptor.cs`
- Create: `framework/src/CrestCreates.Form.Abstractions/FormDescriptor.cs`
- Create: `framework/src/CrestCreates.Form.Abstractions/IFormRegistry.cs`
- Create: `framework/src/CrestCreates.Form.Abstractions/IFormDescriptorProvider.cs`

- [ ] **Step 1: Create CrestCreates.Form.Abstractions.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Form.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write FormFieldDescriptor.cs**

```csharp
namespace CrestCreates.Form.Abstractions;

public sealed class FormFieldDescriptor
{
    public string SchemaFieldName { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? Placeholder { get; init; }
    public string? HelpText { get; init; }
    public string? FormatHint { get; init; }
    public int Order { get; init; }
    public string? Group { get; init; }
    public bool IsReadOnly { get; init; }
    public string? VisibilityCondition { get; init; }
}
```

- [ ] **Step 3: Write FormDescriptor.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Form.Abstractions;

public sealed class FormDescriptor : IVersionedDescriptor
{
    public DescriptorKind Kind => DescriptorKind.Form;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public int Version { get; init; }

    public VersionedDescriptorRef<SchemaDescriptor> Schema { get; init; }
    public IReadOnlyList<FormFieldDescriptor> Fields { get; init; } = Array.Empty<FormFieldDescriptor>();
    public string? LayoutColumns { get; init; }
}
```

- [ ] **Step 4: Write IFormRegistry.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Form.Abstractions;

public interface IFormRegistry : IVersionedDescriptorRegistry<FormDescriptor>
{
}
```

- [ ] **Step 5: Write IFormDescriptorProvider.cs**

```csharp
namespace CrestCreates.Form.Abstractions;

public interface IFormDescriptorProvider
{
    FormDescriptor GetFormDescriptor();
}
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Form.Abstractions/CrestCreates.Form.Abstractions.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Form.Abstractions/
git commit -m "feat: add Form.Abstractions — FormDescriptor, FormFieldDescriptor, IFormRegistry, IFormDescriptorProvider"
```

---

### Task 4: Form — FormRegistry + FormRegistryProvider

**Files:**
- Create: `framework/src/CrestCreates.Form/CrestCreates.Form.csproj`
- Create: `framework/src/CrestCreates.Form/FormRegistry.cs`
- Create: `framework/src/CrestCreates.Form/FormRegistryProvider.cs`

- [ ] **Step 1: Create CrestCreates.Form.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Form</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Form.Abstractions\CrestCreates.Form.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write FormRegistry.cs**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Form.Abstractions;

namespace CrestCreates.Form;

public sealed class FormRegistry : IFormRegistry
{
    private readonly ConcurrentDictionary<string, FormDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<FormDescriptor>> _byName = new();

    public void Register(FormDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
    }

    public FormDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public FormDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public FormDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public FormDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
            : null;

    public FormDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<FormDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<FormDescriptor>();

    public IReadOnlyList<FormDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<FormDescriptor>();

    public IReadOnlyList<FormDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();
}
```

- [ ] **Step 3: Write FormRegistryProvider.cs**

```csharp
using CrestCreates.Form.Abstractions;

namespace CrestCreates.Form;

public static class FormRegistryProvider
{
    private static FormRegistry? _registry;

    public static void SetRegistry(FormRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(FormDescriptor descriptor)
    {
        _registry?.Register(descriptor);
    }
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Form/CrestCreates.Form.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Form/
git commit -m "feat: add Form — FormRegistry, FormRegistryProvider"
```

---

### Task 5: Form Tests

**Files:**
- Create: `framework/test/CrestCreates.Form.Tests/CrestCreates.Form.Tests.csproj`
- Create: `framework/test/CrestCreates.Form.Tests/FormDescriptorTests.cs`
- Create: `framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs`

- [ ] **Step 1: Create CrestCreates.Form.Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Form.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Form.Tests</AssemblyName>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Form\CrestCreates.Form.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write FormDescriptorTests.cs**

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormDescriptorTests
{
    [Fact]
    public void FormDescriptor_Kind_Is_Form()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 2)
        };

        form.Kind.Should().Be(DescriptorKind.Form);
    }

    [Fact]
    public void FormDescriptor_References_Schema_By_VersionedRef()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3)
        };

        form.Schema.Id.Should().Be("schema_01");
        form.Schema.Version.Should().Be(3);
    }

    [Fact]
    public void FormDescriptor_Fields_Contain_UI_Metadata()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Fields = new[]
            {
                new FormFieldDescriptor
                {
                    SchemaFieldName = "Name",
                    Label = "Full Name",
                    Placeholder = "Enter your name",
                    Order = 0,
                    IsReadOnly = false
                },
                new FormFieldDescriptor
                {
                    SchemaFieldName = "Email",
                    Label = "Email Address",
                    Order = 1
                }
            }
        };

        form.Fields.Should().HaveCount(2);
        form.Fields[0].Label.Should().Be("Full Name");
        form.Fields[1].SchemaFieldName.Should().Be("Email");
    }

    [Fact]
    public void FormDescriptor_Defaults_Fields_To_Empty()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "MinimalForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };

        form.Fields.Should().BeEmpty();
    }

    [Fact]
    public void FormFieldDescriptor_VisibilityCondition_Is_Optional()
    {
        var field = new FormFieldDescriptor
        {
            SchemaFieldName = "ApprovalNotes",
            VisibilityCondition = "Role == 'Manager'"
        };

        field.VisibilityCondition.Should().Be("Role == 'Manager'");
    }
}
```

- [ ] **Step 3: Write FormRegistryTests.cs**

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormRegistryTests
{
    private static FormDescriptor CreateForm(string id, string name, int version)
    {
        return new FormDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };
    }

    [Fact]
    public void Register_And_GetById_Works()
    {
        var registry = new FormRegistry();
        var form = CreateForm("form_01", "CustomerCreateForm", 1);
        registry.Register(form);

        var result = registry.GetById("form_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("CustomerCreateForm");
    }

    [Fact]
    public void Multiple_Versions_Same_Name()
    {
        var registry = new FormRegistry();
        registry.Register(CreateForm("f1", "CustomerForm", 1));
        registry.Register(CreateForm("f2", "CustomerForm", 2));

        var all = registry.GetAllByName("CustomerForm");
        all.Should().HaveCount(2);
    }

    [Fact]
    public void GetAll_Returns_All_Forms()
    {
        var registry = new FormRegistry();
        registry.Register(CreateForm("f1", "FormA", 1));
        registry.Register(CreateForm("f2", "FormB", 1));

        var all = registry.GetAll();
        all.Should().HaveCount(2);
    }
}
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet test framework/test/CrestCreates.Form.Tests/CrestCreates.Form.Tests.csproj`
Expected: Build succeeded, 8 tests passed.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Form.Tests/
git commit -m "feat: add Form.Tests — 8 tests for FormDescriptor and FormRegistry"
```

---

### Task 6: HumanTask.Abstractions — HumanTaskDescriptor + IHumanTaskRegistry

**Files:**
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj`
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/AssigneeStrategy.cs`
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/CompletionCondition.cs`
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/CompletionOutcome.cs`
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskDescriptor.cs`
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskRegistry.cs`
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskDescriptorProvider.cs`

- [ ] **Step 1: Create CrestCreates.HumanTask.Abstractions.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.HumanTask.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Form.Abstractions\CrestCreates.Form.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Capability.Abstractions\CrestCreates.Capability.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write AssigneeStrategy.cs**

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public enum AssigneeStrategy
{
    SingleUser,
    CandidateGroup,
    RoundRobin,
    LeastLoaded
}
```

- [ ] **Step 3: Write CompletionCondition.cs**

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public enum CompletionCondition
{
    Approve,
    Reject,
    AnyInput,
    CustomExpression
}
```

- [ ] **Step 4: Write CompletionOutcome.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class CompletionOutcome
{
    public CompletionCondition Condition { get; init; }
    public VersionedDescriptorRef<CapabilityDescriptor>? Capability { get; init; }
}
```

- [ ] **Step 5: Write HumanTaskDescriptor.cs**

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskDescriptor : IVersionedDescriptor
{
    public DescriptorKind Kind => DescriptorKind.HumanTask;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public int Version { get; init; }

    public VersionedDescriptorRef<FormDescriptor> Form { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor>? InputSchema { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor>? OutputSchema { get; init; }
    public AssigneeStrategy AssigneeStrategy { get; init; }
    public TimeSpan? Timeout { get; init; }
    public string? Permissions { get; init; }
    public IReadOnlyList<CompletionOutcome> Outcomes { get; init; } = Array.Empty<CompletionOutcome>();
}
```

- [ ] **Step 6: Write IHumanTaskRegistry.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskRegistry : IVersionedDescriptorRegistry<HumanTaskDescriptor>
{
}
```

- [ ] **Step 7: Write IHumanTaskDescriptorProvider.cs**

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskDescriptorProvider
{
    HumanTaskDescriptor GetHumanTaskDescriptor();
}
```

- [ ] **Step 8: Build and verify**

Run: `dotnet build framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/
git commit -m "feat: add HumanTask.Abstractions — HumanTaskDescriptor, enums, CompletionOutcome, IHumanTaskRegistry"
```

---

### Task 7: HumanTask — HumanTaskRegistry + HumanTaskRegistryProvider

**Files:**
- Create: `framework/src/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj`
- Create: `framework/src/CrestCreates.HumanTask/HumanTaskRegistry.cs`
- Create: `framework/src/CrestCreates.HumanTask/HumanTaskRegistryProvider.cs`

- [ ] **Step 1: Create CrestCreates.HumanTask.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.HumanTask</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.HumanTask.Abstractions\CrestCreates.HumanTask.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write HumanTaskRegistry.cs**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class HumanTaskRegistry : IHumanTaskRegistry
{
    private readonly ConcurrentDictionary<string, HumanTaskDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<HumanTaskDescriptor>> _byName = new();

    public void Register(HumanTaskDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
    }

    public HumanTaskDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public HumanTaskDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public HumanTaskDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public HumanTaskDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
            : null;

    public HumanTaskDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<HumanTaskDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<HumanTaskDescriptor>();

    public IReadOnlyList<HumanTaskDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<HumanTaskDescriptor>();

    public IReadOnlyList<HumanTaskDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();
}
```

- [ ] **Step 3: Write HumanTaskRegistryProvider.cs**

```csharp
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

public static class HumanTaskRegistryProvider
{
    private static HumanTaskRegistry? _registry;

    public static void SetRegistry(HumanTaskRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(HumanTaskDescriptor descriptor)
    {
        _registry?.Register(descriptor);
    }
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build framework/src/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/
git commit -m "feat: add HumanTask — HumanTaskRegistry, HumanTaskRegistryProvider"
```

---

### Task 8: HumanTask Tests

**Files:**
- Create: `framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj`
- Create: `framework/test/CrestCreates.HumanTask.Tests/HumanTaskDescriptorTests.cs`
- Create: `framework/test/CrestCreates.HumanTask.Tests/HumanTaskRegistryTests.cs`

- [ ] **Step 1: Create CrestCreates.HumanTask.Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.HumanTask.Tests</RootNamespace>
    <AssemblyName>CrestCreates.HumanTask.Tests</AssemblyName>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.HumanTask\CrestCreates.HumanTask.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write HumanTaskDescriptorTests.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskDescriptorTests
{
    [Fact]
    public void HumanTaskDescriptor_Kind_Is_HumanTask()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "manager.approval",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1)
        };

        task.Kind.Should().Be(DescriptorKind.HumanTask);
    }

    [Fact]
    public void HumanTaskDescriptor_References_Form_By_VersionedRef()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "manager.approval",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 3)
        };

        task.Form.Id.Should().Be("form_01");
        task.Form.Version.Should().Be(3);
    }

    [Fact]
    public void HumanTaskDescriptor_InputSchema_Is_Optional()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "simple.task",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1)
        };

        task.InputSchema.Should().BeNull();
    }

    [Fact]
    public void HumanTaskDescriptor_Outcomes_Reference_Capability()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "manager.approval",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1),
            Outcomes = new[]
            {
                new CompletionOutcome
                {
                    Condition = CompletionCondition.Approve,
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 2)
                },
                new CompletionOutcome
                {
                    Condition = CompletionCondition.Reject,
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_02", 1)
                }
            }
        };

        task.Outcomes.Should().HaveCount(2);
        task.Outcomes[0].Condition.Should().Be(CompletionCondition.Approve);
        task.Outcomes[0].Capability!.Value.Id.Should().Be("cap_01");
    }

    [Fact]
    public void HumanTaskDescriptor_AssigneeStrategy_Defaults_Correctly()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "simple.task",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1),
            AssigneeStrategy = AssigneeStrategy.CandidateGroup
        };

        task.AssigneeStrategy.Should().Be(AssigneeStrategy.CandidateGroup);
    }

    [Fact]
    public void HumanTaskDescriptor_Timeout_Is_Optional()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "urgent.task",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1),
            Timeout = TimeSpan.FromHours(24)
        };

        task.Timeout.Should().Be(TimeSpan.FromHours(24));
    }
}
```

- [ ] **Step 3: Write HumanTaskRegistryTests.cs**

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskRegistryTests
{
    private static HumanTaskDescriptor CreateTask(string id, string name, int version)
    {
        return new HumanTaskDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1)
        };
    }

    [Fact]
    public void Register_And_GetById_Works()
    {
        var registry = new HumanTaskRegistry();
        var task = CreateTask("ht_01", "manager.approval", 1);
        registry.Register(task);

        var result = registry.GetById("ht_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("manager.approval");
    }

    [Fact]
    public void GetAll_Returns_All_Tasks()
    {
        var registry = new HumanTaskRegistry();
        registry.Register(CreateTask("ht_01", "task.a", 1));
        registry.Register(CreateTask("ht_02", "task.b", 1));

        var all = registry.GetAll();
        all.Should().HaveCount(2);
    }
}
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet test framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj`
Expected: Build succeeded, 8 tests passed.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.HumanTask.Tests/
git commit -m "feat: add HumanTask.Tests — 8 tests for HumanTaskDescriptor and HumanTaskRegistry"
```

---

### Task 9: Workflow.Abstractions — InteractionTarget + WorkflowStep + WorkflowDescriptor

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowVariableScope.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/StepErrorBehavior.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/InteractionTarget.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowStep.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowDescriptor.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowDraftPolicy.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowRegistry.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowDescriptorProvider.cs`

- [ ] **Step 1: Create CrestCreates.Workflow.Abstractions.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Workflow.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Capability.Abstractions\CrestCreates.Capability.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.HumanTask.Abstractions\CrestCreates.HumanTask.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write WorkflowVariableScope.cs**

```csharp
namespace CrestCreates.Workflow.Abstractions;

public enum WorkflowVariableScope
{
    Global,
    Workflow,
    SubWorkflow,
    Step
}
```

- [ ] **Step 3: Write StepErrorBehavior.cs**

```csharp
namespace CrestCreates.Workflow.Abstractions;

public enum StepErrorBehavior
{
    Retry,
    Compensate,
    Fail,
    Skip
}
```

- [ ] **Step 4: Write InteractionTarget.cs** (sealed class hierarchy — all in one file)

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Workflow.Abstractions;

public abstract record InteractionTarget
{
    private protected InteractionTarget() { }
}

public sealed record CapabilityTarget : InteractionTarget
{
    public VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }
}

public sealed record HumanTaskTarget : InteractionTarget
{
    public VersionedDescriptorRef<HumanTaskDescriptor> HumanTask { get; init; }
}

public sealed record SubWorkflowTarget : InteractionTarget
{
    public VersionedDescriptorRef<WorkflowDescriptor> SubWorkflow { get; init; }
}
```

- [ ] **Step 5: Write WorkflowStep.cs**

```csharp
namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowStep
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public InteractionTarget Target { get; init; } = null!;
    public string? Condition { get; init; }
    public IReadOnlyList<string> Transitions { get; init; } = Array.Empty<string>();
    public string? InputMapping { get; init; }
    public string? OutputMapping { get; init; }
    public StepErrorBehavior OnError { get; init; } = StepErrorBehavior.Fail;
}
```

- [ ] **Step 6: Write WorkflowDescriptor.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowDescriptor : IVersionedDescriptor
{
    public DescriptorKind Kind => DescriptorKind.Workflow;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public int Version { get; init; }

    public VersionedDescriptorRef<SchemaDescriptor>? VariableSchema { get; init; }
    public IReadOnlyList<WorkflowStep> Steps { get; init; } = Array.Empty<WorkflowStep>();
    public WorkflowVariableScope DefaultVariableScope { get; init; } = WorkflowVariableScope.Workflow;
}
```

- [ ] **Step 7: Write WorkflowDraftPolicy.cs**

```csharp
namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowDraftPolicy
{
    public bool EnableCheckpointing { get; init; }
    public TimeSpan SaveInterval { get; init; } = TimeSpan.FromMinutes(5);
    public bool SaveBeforeHumanTask { get; init; } = true;
    public bool SaveBeforeSubWorkflow { get; init; } = true;
}
```

- [ ] **Step 8: Write IWorkflowRegistry.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowRegistry : IVersionedDescriptorRegistry<WorkflowDescriptor>
{
}
```

- [ ] **Step 9: Write IWorkflowDescriptorProvider.cs**

```csharp
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowDescriptorProvider
{
    WorkflowDescriptor GetWorkflowDescriptor();
}
```

- [ ] **Step 10: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 11: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/
git commit -m "feat: add Workflow.Abstractions — InteractionTarget, WorkflowStep, WorkflowDescriptor, WorkflowDraftPolicy, IWorkflowRegistry"
```

---

### Task 10: Workflow — WorkflowRegistry + WorkflowRegistryProvider

**Files:**
- Create: `framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
- Create: `framework/src/CrestCreates.Workflow/WorkflowRegistry.cs`
- Create: `framework/src/CrestCreates.Workflow/WorkflowRegistryProvider.cs`

- [ ] **Step 1: Create CrestCreates.Workflow.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Workflow</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Workflow.Abstractions\CrestCreates.Workflow.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write WorkflowRegistry.cs**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowRegistry : IWorkflowRegistry
{
    private readonly ConcurrentDictionary<string, WorkflowDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<WorkflowDescriptor>> _byName = new();

    public void Register(WorkflowDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
    }

    public WorkflowDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public WorkflowDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public WorkflowDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public WorkflowDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
            : null;

    public WorkflowDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<WorkflowDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<WorkflowDescriptor>();

    public IReadOnlyList<WorkflowDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<WorkflowDescriptor>();

    public IReadOnlyList<WorkflowDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();
}
```

- [ ] **Step 3: Write WorkflowRegistryProvider.cs**

```csharp
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public static class WorkflowRegistryProvider
{
    private static WorkflowRegistry? _registry;

    public static void SetRegistry(WorkflowRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(WorkflowDescriptor descriptor)
    {
        _registry?.Register(descriptor);
    }
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Workflow/
git commit -m "feat: add Workflow — WorkflowRegistry, WorkflowRegistryProvider"
```

---

### Task 11: Workflow Tests

**Files:**
- Create: `framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
- Create: `framework/test/CrestCreates.Workflow.Tests/WorkflowDescriptorTests.cs`
- Create: `framework/test/CrestCreates.Workflow.Tests/WorkflowRegistryTests.cs`
- Create: `framework/test/CrestCreates.Workflow.Tests/InteractionTargetTests.cs`

- [ ] **Step 1: Create CrestCreates.Workflow.Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Workflow.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Workflow.Tests</AssemblyName>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Workflow\CrestCreates.Workflow.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write InteractionTargetTests.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class InteractionTargetTests
{
    [Fact]
    public void CapabilityTarget_References_CapabilityDescriptor()
    {
        var target = new CapabilityTarget
        {
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 3)
        };

        target.Capability.Id.Should().Be("cap_01");
        target.Capability.Version.Should().Be(3);
    }

    [Fact]
    public void HumanTaskTarget_References_HumanTaskDescriptor()
    {
        var target = new HumanTaskTarget
        {
            HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 2)
        };

        target.HumanTask.Id.Should().Be("ht_01");
        target.HumanTask.Version.Should().Be(2);
    }

    [Fact]
    public void SubWorkflowTarget_References_WorkflowDescriptor()
    {
        var target = new SubWorkflowTarget
        {
            SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_01", 1)
        };

        target.SubWorkflow.Id.Should().Be("wf_01");
        target.SubWorkflow.Version.Should().Be(1);
    }

    [Fact]
    public void All_Targets_Are_InteractionTarget()
    {
        var cap = new CapabilityTarget { Capability = new VersionedDescriptorRef<CapabilityDescriptor>("c", 1) };
        var ht = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("h", 1) };
        var sw = new SubWorkflowTarget { SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("w", 1) };

        (cap is InteractionTarget).Should().BeTrue();
        (ht is InteractionTarget).Should().BeTrue();
        (sw is InteractionTarget).Should().BeTrue();
    }
}
```

- [ ] **Step 3: Write WorkflowDescriptorTests.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowDescriptorTests
{
    [Fact]
    public void WorkflowDescriptor_Kind_Is_Workflow()
    {
        var wf = new WorkflowDescriptor
        {
            Id = "wf_01",
            Name = "employee.onboarding",
            Version = 1
        };

        wf.Kind.Should().Be(DescriptorKind.Workflow);
    }

    [Fact]
    public void WorkflowDescriptor_Steps_Contain_Targets()
    {
        var wf = new WorkflowDescriptor
        {
            Id = "wf_01",
            Name = "employee.onboarding",
            Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_01",
                    Name = "Create Customer",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
                    },
                    OnError = StepErrorBehavior.Compensate
                },
                new WorkflowStep
                {
                    Id = "step_02",
                    Name = "Manager Approval",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    },
                    Transitions = new[] { "step_03" }
                }
            }
        };

        wf.Steps.Should().HaveCount(2);
        wf.Steps[0].Target.Should().BeOfType<CapabilityTarget>();
        wf.Steps[0].OnError.Should().Be(StepErrorBehavior.Compensate);
        wf.Steps[1].Transitions.Should().Contain("step_03");
    }

    [Fact]
    public void WorkflowDescriptor_VariableSchema_Is_Optional()
    {
        var wf = new WorkflowDescriptor
        {
            Id = "wf_01",
            Name = "simple.wf",
            Version = 1
        };

        wf.VariableSchema.Should().BeNull();
    }

    [Fact]
    public void WorkflowDescriptor_VariableSchema_Can_Be_Set()
    {
        var wf = new WorkflowDescriptor
        {
            Id = "wf_01",
            Name = "employee.onboarding",
            Version = 1,
            VariableSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 2)
        };

        wf.VariableSchema!.Value.Id.Should().Be("schema_01");
        wf.VariableSchema!.Value.Version.Should().Be(2);
    }

    [Fact]
    public void WorkflowDraftPolicy_Defaults()
    {
        var policy = new WorkflowDraftPolicy
        {
            EnableCheckpointing = true
        };

        policy.EnableCheckpointing.Should().BeTrue();
        policy.SaveInterval.Should().Be(TimeSpan.FromMinutes(5));
        policy.SaveBeforeHumanTask.Should().BeTrue();
        policy.SaveBeforeSubWorkflow.Should().BeTrue();
    }

    [Fact]
    public void WorkflowStep_Id_Survives_Reordering()
    {
        var stepId = "step_01JMXZ8K";

        var step = new WorkflowStep
        {
            Id = stepId,
            Name = "Some Step",
            Target = new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
            }
        };

        step.Id.Should().Be(stepId);
    }
}
```

- [ ] **Step 4: Write WorkflowRegistryTests.cs**

```csharp
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowRegistryTests
{
    private static WorkflowDescriptor CreateWorkflow(string id, string name, int version)
    {
        return new WorkflowDescriptor
        {
            Id = id,
            Name = name,
            Version = version
        };
    }

    [Fact]
    public void Register_And_GetById_Works()
    {
        var registry = new WorkflowRegistry();
        var wf = CreateWorkflow("wf_01", "employee.onboarding", 1);
        registry.Register(wf);

        var result = registry.GetById("wf_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("employee.onboarding");
    }

    [Fact]
    public void GetActiveVersion_Returns_Highest_Active_Version()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("w1", "employee.onboarding", 1));
        registry.Register(CreateWorkflow("w2", "employee.onboarding", 2));

        var active = registry.GetActiveVersion("employee.onboarding");
        active.Should().NotBeNull();
        active!.Version.Should().Be(2);
    }

    [Fact]
    public void GetAll_Returns_All_Workflows()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("w1", "wf.a", 1));
        registry.Register(CreateWorkflow("w2", "wf.b", 1));

        var all = registry.GetAll();
        all.Should().HaveCount(2);
    }
}
```

- [ ] **Step 5: Build and run tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: Build succeeded, 13 tests passed.

- [ ] **Step 6: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/
git commit -m "feat: add Workflow.Tests — 13 tests for InteractionTarget, WorkflowDescriptor, WorkflowRegistry"
```

---

### Task 12: DescriptorHashComputer Expansion

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/DescriptorHashComputer.cs`
- Test: `framework/test/CrestCreates.Metadata.Tests/DescriptorHashComputerTests.cs` (add tests)

The `DescriptorHashComputer.ExtractContractFields()` currently handles `SchemaDescriptor` and `CapabilityDescriptor` with explicit `switch` cases. Add cases for `EventDescriptor`, `FormDescriptor`, `HumanTaskDescriptor`, and `WorkflowDescriptor`.

- [ ] **Step 1: Expand ExtractContractFields in DescriptorHashComputer.cs**

Read the existing file to understand the pattern, then add:

```csharp
DescriptorKind.Event => new
{
    descriptor.Id,
    descriptor.Name,
    descriptor.Version,
    PayloadSchema = new { descriptor.PayloadSchema.Id, descriptor.PayloadSchema.Version },
    descriptor.Category,
    descriptor.Semantic,
    descriptor.Importance,
    descriptor.ChangeKind
},
DescriptorKind.Form => new
{
    descriptor.Id,
    descriptor.Name,
    descriptor.Version,
    Schema = new { descriptor.Schema.Id, descriptor.Schema.Version },
    Fields = descriptor.Fields
        .OrderBy(f => f.SchemaFieldName)
        .Select(f => new { f.SchemaFieldName, f.IsReadOnly, f.Order })
        .ToList()
},
DescriptorKind.HumanTask => new
{
    descriptor.Id,
    descriptor.Name,
    descriptor.Version,
    Form = new { descriptor.Form.Id, descriptor.Form.Version },
    InputSchema = descriptor.InputSchema == null ? null :
        new { descriptor.InputSchema.Value.Id, descriptor.InputSchema.Value.Version },
    OutputSchema = descriptor.OutputSchema == null ? null :
        new { descriptor.OutputSchema.Value.Id, descriptor.OutputSchema.Value.Version },
    descriptor.AssigneeStrategy,
    Outcomes = descriptor.Outcomes
        .OrderBy(o => o.Condition.ToString())
        .Select(o => new
        {
            o.Condition,
            Capability = o.Capability == null ? null :
                new { o.Capability.Value.Id, o.Capability.Value.Version }
        })
        .ToList()
},
DescriptorKind.Workflow => new
{
    descriptor.Id,
    descriptor.Name,
    descriptor.Version,
    VariableSchema = descriptor.VariableSchema == null ? null :
        new { descriptor.VariableSchema.Value.Id, descriptor.VariableSchema.Value.Version },
    Steps = descriptor.Steps
        .OrderBy(s => s.Id)
        .Select(s => new
        {
            s.Id,
            s.Name,
            TargetKind = s.Target.GetType().Name,
            s.OnError,
            Transitions = s.Transitions.OrderBy(t => t).ToList()
        })
        .ToList()
},
```

Note: This step requires reading the actual DescriptorHashComputer.cs file to match the exact pattern. The extract shown above shows the canonical fields to include. The actual edit must match the switch expression's existing structure.

- [ ] **Step 2: Add hash tests for new descriptor types to DescriptorHashComputerTests.cs**

Add these test methods:

```csharp
[Fact]
public void EventDescriptor_Same_Content_Produces_Same_ContractHash()
{
    var evt1 = new Event.Abstractions.EventDescriptor
    {
        Id = "evt_01", Name = "crm.customer.created", Version = 1,
        PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
        Category = Event.Abstractions.EventCategory.Domain,
        Semantic = Event.Abstractions.EventSemantic.Fact,
        Importance = Event.Abstractions.EventImportance.Critical
    };
    var evt2 = new Event.Abstractions.EventDescriptor
    {
        Id = "evt_01", Name = "crm.customer.created", Version = 1,
        PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
        Category = Event.Abstractions.EventCategory.Domain,
        Semantic = Event.Abstractions.EventSemantic.Fact,
        Importance = Event.Abstractions.EventImportance.Critical
    };

    var h1 = DescriptorHashComputer.ComputeContractHash(evt1);
    var h2 = DescriptorHashComputer.ComputeContractHash(evt2);

    h1.Should().Be(h2);
}

[Fact]
public void WorkflowStep_ContractHash_Includes_Step_Id_Not_Name()
{
    var wf1 = new Workflow.Abstractions.WorkflowDescriptor
    {
        Id = "wf_01", Name = "test.wf", Version = 1,
        Steps = new[]
        {
            new Workflow.Abstractions.WorkflowStep
            {
                Id = "step_01", Name = "Step A",
                Target = new Workflow.Abstractions.CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<Capability.Abstractions.CapabilityDescriptor>("cap_01", 1)
                }
            }
        }
    };
    var wf2 = new Workflow.Abstractions.WorkflowDescriptor
    {
        Id = "wf_01", Name = "test.wf", Version = 1,
        Steps = new[]
        {
            new Workflow.Abstractions.WorkflowStep
            {
                Id = "step_01", Name = "Renamed Step",
                Target = new Workflow.Abstractions.CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<Capability.Abstractions.CapabilityDescriptor>("cap_01", 1)
                }
            }
        }
    };

    var h1 = DescriptorHashComputer.ComputeContractHash(wf1);
    var h2 = DescriptorHashComputer.ComputeContractHash(wf2);

    h1.Should().Be(h2); // Step Name change does NOT affect ContractHash
}
```

- [ ] **Step 3: Add project references to Metadata.Tests.csproj**

Add to `framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj`:
```xml
<ProjectReference Include="..\..\src\CrestCreates.Event.Abstractions\CrestCreates.Event.Abstractions.csproj" />
<ProjectReference Include="..\..\src\CrestCreates.Form.Abstractions\CrestCreates.Form.Abstractions.csproj" />
<ProjectReference Include="..\..\src\CrestCreates.HumanTask.Abstractions\CrestCreates.HumanTask.Abstractions.csproj" />
<ProjectReference Include="..\..\src\CrestCreates.Workflow.Abstractions\CrestCreates.Workflow.Abstractions.csproj" />
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj`
Expected: Build succeeded, all existing + new tests pass (23+ tests total).

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorHashComputer.cs framework/test/CrestCreates.Metadata.Tests/
git commit -m "feat: expand DescriptorHashComputer for Event, Form, HumanTask, Workflow descriptors"
```

---

### Task 13: Source Generator Expansion

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs`
- Create: `framework/tools/CrestCreates.CodeGenerator/Models/EventDescriptorInfo.cs`
- Create: `framework/tools/CrestCreates.CodeGenerator/Models/FormDescriptorInfo.cs`
- Create: `framework/tools/CrestCreates.CodeGenerator/Models/HumanTaskDescriptorInfo.cs`
- Create: `framework/tools/CrestCreates.CodeGenerator/Models/WorkflowDescriptorInfo.cs`

Expand the source generator to discover `IEventDescriptorProvider`, `IFormDescriptorProvider`, `IHumanTaskDescriptorProvider`, and `IWorkflowDescriptorProvider` implementations and generate registration code.

- [ ] **Step 1: Write EventDescriptorInfo.cs**

```csharp
namespace CrestCreates.CodeGenerator.Models;

internal sealed class EventDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public string PayloadSchemaId { get; set; } = string.Empty;
    public int PayloadSchemaVersion { get; set; }
    public string Category { get; set; } = "Domain";
    public string Semantic { get; set; } = "Fact";
    public string Importance { get; set; } = "Business";
    public string ChangeKind { get; set; } = "Additive";
}
```

- [ ] **Step 2: Write FormDescriptorInfo.cs**

```csharp
namespace CrestCreates.CodeGenerator.Models;

internal sealed class FormDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public string SchemaId { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public List<FormFieldInfo> Fields { get; set; } = new();
}

internal sealed class FormFieldInfo
{
    public string SchemaFieldName { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int Order { get; set; }
}
```

- [ ] **Step 3: Write HumanTaskDescriptorInfo.cs**

```csharp
namespace CrestCreates.CodeGenerator.Models;

internal sealed class HumanTaskDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public string FormId { get; set; } = string.Empty;
    public int FormVersion { get; set; }
    public string? InputSchemaId { get; set; }
    public int? InputSchemaVersion { get; set; }
    public string? OutputSchemaId { get; set; }
    public int? OutputSchemaVersion { get; set; }
    public string AssigneeStrategy { get; set; } = "SingleUser";
}
```

- [ ] **Step 4: Write WorkflowDescriptorInfo.cs**

```csharp
namespace CrestCreates.CodeGenerator.Models;

internal sealed class WorkflowDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public string? VariableSchemaId { get; set; }
    public int? VariableSchemaVersion { get; set; }
    public List<WorkflowStepInfo> Steps { get; set; } = new();
}

internal sealed class WorkflowStepInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? CapabilityId { get; set; }
    public string? HumanTaskId { get; set; }
    public string? SubWorkflowId { get; set; }
    public string OnError { get; set; } = "Fail";
}
```

- [ ] **Step 5: Expand SchemaCapabilitySourceGenerator.cs**

Add discovery of `IEventDescriptorProvider`, `IFormDescriptorProvider`, `IHumanTaskDescriptorProvider`, and `IWorkflowDescriptorProvider` in the generator. The expansion:

1. Add a compilation reference check for each new abstractions package (only generate if the project references the package).
2. Find types implementing each provider interface.
3. Generate `EventRegistryProvider.Register(...)`, `FormRegistryProvider.Register(...)`, etc. calls in the module initializer.

The exact modification must read the existing generator and follow its pattern. The key additions:

- Check `compilation.ReferencedAssemblyNames.Any(a => a.Name == "CrestCreates.Event.Abstractions")` (and similar for Form, HumanTask, Workflow)
- Use `compilation.GetTypeByMetadataName("CrestCreates.Event.Abstractions.IEventDescriptorProvider")` to discover providers
- Generate registration code in the same `Register()` method

- [ ] **Step 6: Build the generator and verify**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/
git commit -m "feat: expand source generator for Event, Form, HumanTask, Workflow descriptors"
```

---

### Task 14: Solution Updates + Full Build + Final Commit

**Files:**
- Modify: `CrestCreates.slnx`

Add all new projects to the solution file in `/src/core/` and `/src/test/` folders.

- [ ] **Step 1: Add source projects to CrestCreates.slnx**

Add in `/src/core/` (alphabetically):
```xml
<Project Path="framework/src/CrestCreates.Event.Abstractions/CrestCreates.Event.Abstractions.csproj" />
<Project Path="framework/src/CrestCreates.Event/CrestCreates.Event.csproj" />
<Project Path="framework/src/CrestCreates.Form.Abstractions/CrestCreates.Form.Abstractions.csproj" />
<Project Path="framework/src/CrestCreates.Form/CrestCreates.Form.csproj" />
<Project Path="framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj" />
<Project Path="framework/src/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj" />
<Project Path="framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj" />
<Project Path="framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj" />
```

- [ ] **Step 2: Add test projects to CrestCreates.slnx**

Add in `/src/test/` (alphabetically):
```xml
<Project Path="framework/test/CrestCreates.Event.Tests/CrestCreates.Event.Tests.csproj" />
<Project Path="framework/test/CrestCreates.Form.Tests/CrestCreates.Form.Tests.csproj" />
<Project Path="framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj" />
<Project Path="framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj" />
```

- [ ] **Step 3: Full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run all Phase 3 tests**

Run: `dotnet test CrestCreates.slnx --filter "FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~Kafka&FullyQualifiedName!~RabbitMQ"`
Expected: All unit tests pass (~84 total: 44 previous + 40 new).

- [ ] **Step 5: Final commit**

```bash
git add CrestCreates.slnx
git commit -m "feat: complete Phase 3 — Event, Form, HumanTask, Workflow descriptors with registries, tests, and solution updates

- 8 new source projects: Event(.Abstractions), Form(.Abstractions), HumanTask(.Abstractions), Workflow(.Abstractions)
- 4 new test projects: Event.Tests, Form.Tests, HumanTask.Tests, Workflow.Tests
- 40 new tests: 11 Event + 8 Form + 8 HumanTask + 13 Workflow
- Expanded DescriptorHashComputer for all 6 descriptor types
- Expanded source generator for provider-based discovery
- Updated CrestCreates.slnx with all 12 new projects
- Total: ~84 tests passing across all phases"
```

---

## Phase 3 Summary

| Category | Count |
|----------|-------|
| New source projects | 8 |
| New test projects | 4 |
| New tests | ~40 |
| Descriptor types completed | Event, Form, HumanTask, Workflow |
| All 6 descriptor types from spec | ✅ Done |
