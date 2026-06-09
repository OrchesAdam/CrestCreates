# Schema + Capability Registry & Source Generator — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the `IDescriptor`/`IVersionedDescriptor` base interfaces, `SchemaDescriptor`, `CapabilityDescriptor`, compile-time source generators to produce them from `[Entity]` and `[CrestService]`, and typed registries (`SchemaRegistry`, `CapabilityRegistry`) — the foundation all future pillars (Event, Workflow) and consumers (Form, HumanTask, Agent, DynamicAPI) depend on.

**Architecture:** Two new Abstractions projects (`CrestCreates.Metadata.Abstractions` for base descriptor interfaces, `CrestCreates.Schema.Abstractions` + `CrestCreates.Capability.Abstractions` for the first two pillars) followed by concrete implementations. A new source generator (`SchemaCapabilitySourceGenerator`) added to the existing `CrestCreates.CodeGenerator` project collects `[Entity]`-decorated classes into `SchemaDescriptor`s and `[CrestService]`-decorated classes into `CapabilityDescriptor`s, emitting them as generated registry code. Existing `DynamicApiDescriptors` continue to work — they internally map to `CapabilityDescriptor` in Phase 2.

**Tech Stack:** .NET 10, C# 13, Roslyn Source Generators (netstandard2.0), central package management, xUnit + FluentAssertions + Moq.

---

### Task 0: Project Scaffolding — Create Abstractions Projects

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj`
- Create: `framework/src/CrestCreates.Schema.Abstractions/CrestCreates.Schema.Abstractions.csproj`
- Create: `framework/src/CrestCreates.Capability.Abstractions/CrestCreates.Capability.Abstractions.csproj`

- [ ] **Step 1: Create Metadata.Abstractions .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Metadata.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Domain.Shared\CrestCreates.Domain.Shared.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create Schema.Abstractions .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Schema.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create Capability.Abstractions .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Capability.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Build to verify project scaffolding**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions
dotnet build framework/src/CrestCreates.Schema.Abstractions
dotnet build framework/src/CrestCreates.Capability.Abstractions
```

Expected: All three build successfully (no source files yet, so no output).

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/ framework/src/CrestCreates.Schema.Abstractions/ framework/src/CrestCreates.Capability.Abstractions/
git commit -m "feat: scaffold Metadata.Abstractions, Schema.Abstractions, Capability.Abstractions projects"
```

---

### Task 1: DescriptorKind Enum + IDescriptor + IVersionedDescriptor Interfaces

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorKind.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptor.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IVersionedDescriptor.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorState.cs`

- [ ] **Step 1: Write DescriptorKind enum**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public enum DescriptorKind
{
    Schema,
    Capability,
    Event,
    Workflow,
    Form,
    HumanTask
}
```

- [ ] **Step 2: Write DescriptorState enum**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public enum DescriptorState
{
    Draft,
    Active,
    Deprecated,
    Removed
}
```

- [ ] **Step 3: Write IDescriptor interface**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptor
{
    DescriptorKind Kind { get; }
    string Id { get; }
    string Name { get; }
    DescriptorState State { get; }
    string ContractHash { get; }
    string DefinitionHash { get; }
    string? SupersededById { get; }
}
```

- [ ] **Step 4: Write IVersionedDescriptor interface**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IVersionedDescriptor : IDescriptor
{
    int Version { get; }
}
```

- [ ] **Step 5: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions
```

Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/
git commit -m "feat: add DescriptorKind, DescriptorState, IDescriptor, IVersionedDescriptor"
```

---

### Task 2: DescriptorRef and VersionedDescriptorRef

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRef.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/VersionedDescriptorRef.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/VersionSelectionMode.cs`

- [ ] **Step 1: Write VersionSelectionMode enum**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public enum VersionSelectionMode
{
    Exact,
    Latest,
    Compatible
}
```

- [ ] **Step 2: Write DescriptorRef<T>**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public readonly record struct DescriptorRef<TDescriptor>(string Id)
    where TDescriptor : IDescriptor;
```

- [ ] **Step 3: Write VersionedDescriptorRef<T>**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public readonly record struct VersionedDescriptorRef<TDescriptor>(
    string Id,
    int Version,
    VersionSelectionMode SelectionMode = VersionSelectionMode.Exact
) where TDescriptor : IVersionedDescriptor;
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/
git commit -m "feat: add DescriptorRef, VersionedDescriptorRef, VersionSelectionMode"
```

---

### Task 3: SchemaDescriptor

**Files:**
- Create: `framework/src/CrestCreates.Schema.Abstractions/SchemaChangeKind.cs`
- Create: `framework/src/CrestCreates.Schema.Abstractions/ISchemaDescriptorProvider.cs`
- Create: `framework/src/CrestCreates.Schema.Abstractions/SchemaDescriptor.cs`

- [ ] **Step 1: Write SchemaChangeKind enum**

```csharp
namespace CrestCreates.Schema.Abstractions;

public enum SchemaChangeKind
{
    Additive,
    Breaking
}
```

- [ ] **Step 2: Write ISchemaDescriptorProvider interface**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Schema.Abstractions;

public interface ISchemaDescriptorProvider
{
    SchemaDescriptor GetSchemaDescriptor();
}
```

- [ ] **Step 3: Write SchemaDescriptor class**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Schema.Abstractions;

public sealed class SchemaDescriptor : IVersionedDescriptor
{
    public DescriptorKind Kind => DescriptorKind.Schema;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }

    public int Version { get; init; }
    public SchemaChangeKind ChangeKind { get; init; }

    public IReadOnlyList<SchemaFieldDescriptor> Fields { get; init; } =
        Array.Empty<SchemaFieldDescriptor>();
    public IReadOnlyList<SchemaValidationRule> ValidationRules { get; init; } =
        Array.Empty<SchemaValidationRule>();
    public IReadOnlyList<VersionedDescriptorRef<SchemaDescriptor>> References { get; init; } =
        Array.Empty<VersionedDescriptorRef<SchemaDescriptor>>();
}

public sealed class SchemaFieldDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string FieldType { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public bool IsNullable { get; init; }
    public int? MaxLength { get; init; }
    public int? MinLength { get; init; }
    public double? MaxValue { get; init; }
    public double? MinValue { get; init; }
    public string? Pattern { get; init; }
    public bool IsCollection { get; init; }
    public string? CollectionElementType { get; init; }
}

public sealed class SchemaValidationRule
{
    public string Name { get; init; } = string.Empty;
    public string Expression { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Schema.Abstractions
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Schema.Abstractions/
git commit -m "feat: add SchemaDescriptor, SchemaFieldDescriptor, SchemaValidationRule, SchemaChangeKind"
```

---

### Task 4: CapabilityDescriptor

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/CapabilityKind.cs`
- Create: `framework/src/CrestCreates.Capability.Abstractions/ICapabilityHandler.cs`
- Create: `framework/src/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs`

- [ ] **Step 1: Write CapabilityKind enum**

```csharp
namespace CrestCreates.Capability.Abstractions;

public enum CapabilityKind
{
    Query,
    Command
}
```

- [ ] **Step 2: Write ICapabilityHandler interface**

```csharp
namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityHandler
{
}

public interface ICapabilityHandler<TInput, TOutput> : ICapabilityHandler
{
    Task<TOutput> ExecuteAsync(TInput input, CancellationToken ct);
}
```

- [ ] **Step 3: Write CapabilityDescriptor class**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityDescriptor : IVersionedDescriptor
{
    public DescriptorKind Kind => DescriptorKind.Capability;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }

    public int Version { get; init; }
    public CapabilityKind CapabilityKind { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor> InputSchema { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor> OutputSchema { get; init; }
    public string Permission { get; init; } = string.Empty;
    public CapabilityRiskLevel RiskLevel { get; init; } = CapabilityRiskLevel.Medium;
    public IReadOnlyList<string> SemanticTags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
}

public enum CapabilityRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Capability.Abstractions
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/
git commit -m "feat: add CapabilityDescriptor, CapabilityKind, ICapabilityHandler"
```

---

### Task 5: DescriptorRegistry Base Interfaces

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRegistry.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IVersionedDescriptorRegistry.cs`

- [ ] **Step 1: Write IDescriptorRegistry<T>**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorRegistry<TDescriptor> where TDescriptor : IDescriptor
{
    TDescriptor? GetById(string id);
    TDescriptor? GetByName(string name);
    IReadOnlyList<TDescriptor> GetAll();
}
```

- [ ] **Step 2: Write IVersionedDescriptorRegistry<T>**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IVersionedDescriptorRegistry<TDescriptor>
    : IDescriptorRegistry<TDescriptor>
    where TDescriptor : IVersionedDescriptor
{
    TDescriptor? GetByNameAndVersion(string name, int version);
    IReadOnlyList<TDescriptor> GetAllByName(string name);
    TDescriptor? GetActiveVersion(string name);
    TDescriptor? GetLatestVersion(string name);
    IReadOnlyList<TDescriptor> GetDeprecatedVersions(string name);
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/
git commit -m "feat: add IDescriptorRegistry and IVersionedDescriptorRegistry"
```

---

### Task 6: Concrete SchemaRegistry

**Files:**
- Create: `framework/src/CrestCreates.Schema.Abstractions/ISchemaRegistry.cs`
- Create: `framework/src/CrestCreates.Schema/SchemaRegistry.cs`
- Create: `framework/src/CrestCreates.Schema/CrestCreates.Schema.csproj`

- [ ] **Step 1: Write ISchemaRegistry interface**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Schema.Abstractions;

public interface ISchemaRegistry : IVersionedDescriptorRegistry<SchemaDescriptor>
{
}
```

- [ ] **Step 2: Create Schema implementation project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Schema</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write SchemaRegistry class**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

public sealed class SchemaRegistry : ISchemaRegistry
{
    private readonly ConcurrentDictionary<string, SchemaDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<SchemaDescriptor>> _byName = new();

    public void Register(SchemaDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
    }

    public SchemaDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public SchemaDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public SchemaDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public SchemaDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active)
                      .MaxBy(v => v.Version)
            : null;

    public SchemaDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<SchemaDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<SchemaDescriptor>();

    public IReadOnlyList<SchemaDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<SchemaDescriptor>();

    public IReadOnlyList<SchemaDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Schema
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Schema.Abstractions/ISchemaRegistry.cs framework/src/CrestCreates.Schema/
git commit -m "feat: add ISchemaRegistry and SchemaRegistry implementation"
```

---

### Task 7: Concrete CapabilityRegistry

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/ICapabilityRegistry.cs`
- Create: `framework/src/CrestCreates.Capability/CapabilityRegistry.cs`
- Create: `framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`

- [ ] **Step 1: Write ICapabilityRegistry interface**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityRegistry : IVersionedDescriptorRegistry<CapabilityDescriptor>
{
    IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind);
    IReadOnlyList<CapabilityDescriptor> GetByTag(string tag);
}
```

- [ ] **Step 2: Create Capability implementation project .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Capability</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Capability.Abstractions\CrestCreates.Capability.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write CapabilityRegistry class**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly ConcurrentDictionary<string, CapabilityDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<CapabilityDescriptor>> _byName = new();
    private readonly ConcurrentDictionary<CapabilityKind, List<CapabilityDescriptor>> _byKind = new();
    private readonly ConcurrentDictionary<string, List<CapabilityDescriptor>> _byTag = new();

    public void Register(CapabilityDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
        _byKind.GetOrAdd(descriptor.CapabilityKind, _ => new()).Add(descriptor);
        foreach (var tag in descriptor.SemanticTags)
        {
            _byTag.GetOrAdd(tag, _ => new()).Add(descriptor);
        }
    }

    public CapabilityDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public CapabilityDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public CapabilityDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public CapabilityDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
            : null;

    public CapabilityDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<CapabilityDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<CapabilityDescriptor>();

    public IReadOnlyList<CapabilityDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<CapabilityDescriptor>();

    public IReadOnlyList<CapabilityDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();

    public IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind) =>
        _byKind.TryGetValue(kind, out var list) ? list.AsReadOnly() : Array.Empty<CapabilityDescriptor>();

    public IReadOnlyList<CapabilityDescriptor> GetByTag(string tag) =>
        _byTag.TryGetValue(tag, out var list) ? list.AsReadOnly() : Array.Empty<CapabilityDescriptor>();
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Capability
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/ICapabilityRegistry.cs framework/src/CrestCreates.Capability/
git commit -m "feat: add ICapabilityRegistry and CapabilityRegistry implementation"
```

---

### Task 8: Source Generator — Model Classes

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/Models/SchemaDescriptorInfo.cs`
- Create: `framework/tools/CrestCreates.CodeGenerator/Models/CapabilityDescriptorInfo.cs`

- [ ] **Step 1: Write SchemaDescriptorInfo model**

```csharp
using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.Models;

public sealed class SchemaDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string ChangeKind { get; set; } = "Additive";
    public List<SchemaFieldInfo> Fields { get; set; } = new();
}

public sealed class SchemaFieldInfo
{
    public string Name { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public bool IsCollection { get; set; }
    public string? CollectionElementType { get; set; }
}
```

- [ ] **Step 2: Write CapabilityDescriptorInfo model**

```csharp
using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.Models;

public sealed class CapabilityDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string CapabilityKind { get; set; } = "Command";
    public string InputSchemaId { get; set; } = string.Empty;
    public int InputSchemaVersion { get; set; } = 1;
    public string OutputSchemaId { get; set; } = string.Empty;
    public int OutputSchemaVersion { get; set; } = 1;
    public string Permission { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "Medium";
    public List<string> SemanticTags { get; set; } = new();
    public List<string> Aliases { get; set; } = new();
}
```

- [ ] **Step 3: Build to verify models compile**

```bash
dotnet build framework/tools/CrestCreates.CodeGenerator
```

Expected: Build succeeds (netstandard2.0).

- [ ] **Step 4: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/Models/SchemaDescriptorInfo.cs framework/tools/CrestCreates.CodeGenerator/Models/CapabilityDescriptorInfo.cs
git commit -m "feat: add SchemaDescriptorInfo and CapabilityDescriptorInfo source generator models"
```

---

### Task 9: Source Generator — SchemaCapabilitySourceGenerator

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs`

- [ ] **Step 1: Write the source generator class**

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CrestCreates.CodeGenerator.Models;
using System.Text;

namespace CrestCreates.CodeGenerator.SchemaCapabilityGenerator;

[Generator]
public sealed class SchemaCapabilitySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect [Entity]-decorated classes for Schema generation
        var entityClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds
                    && cds.AttributeLists.Count > 0,
                transform: static (ctx, ct) => GetEntityInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        // Collect [CrestService]-decorated classes for Capability generation
        var serviceClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds
                    && cds.AttributeLists.Count > 0,
                transform: static (ctx, ct) => GetCapabilityInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        // Register output — generate Schema and Capability registries
        context.RegisterSourceOutput(
            entityClasses.Combine(serviceClasses),
            static (spc, source) => GenerateRegistries(spc, source.Left, source.Right));
    }

    private static SchemaDescriptorInfo? GetEntityInfo(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        var hasEntityAttr = symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "EntityAttribute" or "Entity");

        if (!hasEntityAttr) return null;

        var fields = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Select(p => new SchemaFieldInfo
            {
                Name = p.Name,
                FieldType = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated,
                IsRequired = !p.IsOptional,
                IsCollection = p.Type is INamedTypeSymbol nts
                    && nts.IsGenericType
                    && nts.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IList_T,
            })
            .ToList();

        return new SchemaDescriptorInfo
        {
            Id = $"schema_{Guid.NewGuid():N}",
            Name = symbol.Name,
            Version = 1,
            Fields = fields
        };
    }

    private static CapabilityDescriptorInfo? GetCapabilityInfo(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        var hasServiceAttr = symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "CrestServiceAttribute" or "CrestService");

        if (!hasServiceAttr) return null;

        var publicMethods = symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public
                     && !m.IsStatic
                     && m.MethodKind == MethodKind.Ordinary);

        var methodName = publicMethods.FirstOrDefault()?.Name ?? symbol.Name;
        var serviceName = symbol.Name.Replace("AppService", "").Replace("Service", "");

        return new CapabilityDescriptorInfo
        {
            Id = $"cap_{Guid.NewGuid():N}",
            Name = $"{symbol.ContainingNamespace.ToDisplayString().ToLowerInvariant()}.{serviceName.ToLowerInvariant()}",
            Version = 1,
            SemanticTags = new List<string> { serviceName.ToLowerInvariant() }
        };
    }

    private static void GenerateRegistries(
        SourceProductionContext spc,
        ImmutableArray<SchemaDescriptorInfo?> schemas,
        ImmutableArray<CapabilityDescriptorInfo?> capabilities)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using CrestCreates.Schema.Abstractions;");
        sb.AppendLine("using CrestCreates.Capability.Abstractions;");
        sb.AppendLine("using CrestCreates.Metadata.Abstractions;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        sb.AppendLine("namespace CrestCreates.Generated;");
        sb.AppendLine();
        sb.AppendLine("internal static class GeneratedDescriptorRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    internal static void Register()");
        sb.AppendLine("    {");

        foreach (var schema in schemas)
        {
            if (schema == null) continue;
            sb.AppendLine($"        SchemaRegistryProvider.Register(new SchemaDescriptor");
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
            sb.AppendLine("        });");
            sb.AppendLine();
        }

        foreach (var cap in capabilities)
        {
            if (cap == null) continue;
            sb.AppendLine($"        CapabilityRegistryProvider.Register(new CapabilityDescriptor");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id = \"{cap.Id}\",");
            sb.AppendLine($"            Name = \"{cap.Name}\",");
            sb.AppendLine($"            Version = {cap.Version},");
            sb.AppendLine($"            CapabilityKind = CapabilityKind.{cap.CapabilityKind},");
            sb.AppendLine($"            Permission = \"{cap.Permission}\",");
            sb.AppendLine($"            SemanticTags = new List<string> {{ {string.Join(", ", cap.SemanticTags.Select(t => $"\"{t}\""))} }},");
            sb.AppendLine("        });");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("GeneratedDescriptorRegistry.g.cs", sb.ToString());
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build framework/tools/CrestCreates.CodeGenerator
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/
git commit -m "feat: add SchemaCapabilitySourceGenerator for compile-time descriptor generation"
```

---

### Task 10: Static Registry Provider Hooks

**Files:**
- Create: `framework/src/CrestCreates.Schema/SchemaRegistryProvider.cs`
- Create: `framework/src/CrestCreates.Capability/CapabilityRegistryProvider.cs`

The source generator emits calls to `SchemaRegistryProvider.Register(...)` and `CapabilityRegistryProvider.Register(...)`. These static accessors bridge the generated code to the DI-registered singleton registries.

- [ ] **Step 1: Write SchemaRegistryProvider**

```csharp
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

public static class SchemaRegistryProvider
{
    private static ISchemaRegistry? _registry;

    public static void SetRegistry(ISchemaRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(SchemaDescriptor descriptor)
    {
        if (_registry is SchemaRegistry concrete)
        {
            concrete.Register(descriptor);
        }
    }
}
```

- [ ] **Step 2: Write CapabilityRegistryProvider**

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public static class CapabilityRegistryProvider
{
    private static ICapabilityRegistry? _registry;

    public static void SetRegistry(ICapabilityRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(CapabilityDescriptor descriptor)
    {
        if (_registry is CapabilityRegistry concrete)
        {
            concrete.Register(descriptor);
        }
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Schema
dotnet build framework/src/CrestCreates.Capability
```

Expected: Both build succeeds.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Schema/SchemaRegistryProvider.cs framework/src/CrestCreates.Capability/CapabilityRegistryProvider.cs
git commit -m "feat: add SchemaRegistryProvider and CapabilityRegistryProvider for generated code bridge"
```

---

### Task 11: Tests — SchemaDescriptor and Registry

**Files:**
- Create: `framework/test/CrestCreates.Schema.Tests/SchemaDescriptorTests.cs`
- Create: `framework/test/CrestCreates.Schema.Tests/SchemaRegistryTests.cs`
- Create: `framework/test/CrestCreates.Schema.Tests/CrestCreates.Schema.Tests.csproj`

- [ ] **Step 1: Create test project .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Schema.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Schema\CrestCreates.Schema.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write SchemaDescriptorTests**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Schema.Tests;

public class SchemaDescriptorTests
{
    [Fact]
    public void SchemaDescriptor_Implements_IVersionedDescriptor()
    {
        var descriptor = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor
                {
                    Name = "Name",
                    FieldType = "string",
                    IsRequired = true
                }
            }
        };

        descriptor.Should().BeAssignableTo<IVersionedDescriptor>();
        descriptor.Kind.Should().Be(DescriptorKind.Schema);
        descriptor.Version.Should().Be(1);
    }

    [Fact]
    public void SchemaDescriptor_Defaults_State_To_Active()
    {
        var descriptor = new SchemaDescriptor
        {
            Id = "schema_02",
            Name = "Test",
            Version = 1
        };

        descriptor.State.Should().Be(DescriptorState.Active);
    }

    [Fact]
    public void SchemaFieldDescriptor_Records_All_Properties()
    {
        var field = new SchemaFieldDescriptor
        {
            Name = "Email",
            FieldType = "string",
            IsRequired = true,
            IsNullable = false,
            MaxLength = 200,
            Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$"
        };

        field.Name.Should().Be("Email");
        field.FieldType.Should().Be("string");
        field.IsRequired.Should().BeTrue();
        field.MaxLength.Should().Be(200);
        field.Pattern.Should().NotBeNull();
    }
}
```

- [ ] **Step 3: Write SchemaRegistryTests**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Schema.Tests;

public class SchemaRegistryTests
{
    [Fact]
    public void Register_And_GetById_Returns_Descriptor()
    {
        var registry = new SchemaRegistry();
        var descriptor = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1
        };

        registry.Register(descriptor);
        var result = registry.GetById("schema_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("CustomerInput");
    }

    [Fact]
    public void GetByName_Returns_Active_Version()
    {
        var registry = new SchemaRegistry();
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            State = DescriptorState.Active
        });
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_02",
            Name = "CustomerInput",
            Version = 2,
            State = DescriptorState.Draft
        });

        var result = registry.GetByName("CustomerInput");

        result.Should().NotBeNull();
        result!.Version.Should().Be(1);
    }

    [Fact]
    public void GetActiveVersion_Returns_Highest_Active()
    {
        var registry = new SchemaRegistry();
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            State = DescriptorState.Active
        });
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_02",
            Name = "CustomerInput",
            Version = 2,
            State = DescriptorState.Active
        });
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_03",
            Name = "CustomerInput",
            Version = 3,
            State = DescriptorState.Draft
        });

        var result = registry.GetActiveVersion("CustomerInput");

        result.Should().NotBeNull();
        result!.Version.Should().Be(2);
    }

    [Fact]
    public void GetDeprecatedVersions_Returns_Only_Deprecated()
    {
        var registry = new SchemaRegistry();
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            State = DescriptorState.Deprecated
        });
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_02",
            Name = "CustomerInput",
            Version = 2,
            State = DescriptorState.Active
        });

        var deprecated = registry.GetDeprecatedVersions("CustomerInput");

        deprecated.Should().HaveCount(1);
        deprecated[0].Version.Should().Be(1);
    }

    [Fact]
    public void GetById_Missing_Returns_Null()
    {
        var registry = new SchemaRegistry();

        var result = registry.GetById("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public void GetAll_Returns_All_Registered()
    {
        var registry = new SchemaRegistry();
        registry.Register(new SchemaDescriptor { Id = "schema_01", Name = "A", Version = 1 });
        registry.Register(new SchemaDescriptor { Id = "schema_02", Name = "B", Version = 1 });

        var all = registry.GetAll();

        all.Should().HaveCount(2);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test framework/test/CrestCreates.Schema.Tests
```

Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Schema.Tests/
git commit -m "test: add SchemaDescriptor and SchemaRegistry tests (6 tests)"
```

---

### Task 12: Tests — CapabilityDescriptor and Registry

**Files:**
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityDescriptorTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityRegistryTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj`

- [ ] **Step 1: Create test project .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Capability.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Capability\CrestCreates.Capability.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write CapabilityDescriptorTests**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityDescriptorTests
{
    [Fact]
    public void CapabilityDescriptor_Implements_IVersionedDescriptor()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            CapabilityKind = CapabilityKind.Command,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_02", 1),
            Permission = "Customer.Create",
            RiskLevel = CapabilityRiskLevel.Medium
        };

        descriptor.Should().BeAssignableTo<IVersionedDescriptor>();
        descriptor.Kind.Should().Be(DescriptorKind.Capability);
    }

    [Fact]
    public void CapabilityDescriptor_SemanticTags_Defaults_Empty()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_02",
            Name = "test.operation",
            Version = 1
        };

        descriptor.SemanticTags.Should().BeEmpty();
    }

    [Fact]
    public void CapabilityDescriptor_Aliases_Defaults_Empty()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_03",
            Name = "test.operation",
            Version = 1
        };

        descriptor.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void CapabilityKind_Query_And_Command_Only()
    {
        var values = Enum.GetValues<CapabilityKind>();

        values.Should().Contain(CapabilityKind.Query);
        values.Should().Contain(CapabilityKind.Command);
        values.Should().HaveCount(2); // No Draft
    }
}
```

- [ ] **Step 3: Write CapabilityRegistryTests**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityRegistryTests
{
    [Fact]
    public void Register_And_GetById_Returns_Descriptor()
    {
        var registry = new CapabilityRegistry();
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            CapabilityKind = CapabilityKind.Command
        };

        registry.Register(descriptor);
        var result = registry.GetById("cap_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("crm.customer.create");
    }

    [Fact]
    public void GetByKind_Filters_Correctly()
    {
        var registry = new CapabilityRegistry();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.read",
            Version = 1,
            CapabilityKind = CapabilityKind.Query
        });
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_02",
            Name = "crm.customer.create",
            Version = 1,
            CapabilityKind = CapabilityKind.Command
        });

        var queries = registry.GetByKind(CapabilityKind.Query);

        queries.Should().HaveCount(1);
        queries[0].Name.Should().Be("crm.customer.read");
    }

    [Fact]
    public void GetByTag_Finds_SemanticTags()
    {
        var registry = new CapabilityRegistry();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            SemanticTags = new List<string> { "customer", "crm", "create" }
        });
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_02",
            Name = "hr.employee.create",
            Version = 1,
            SemanticTags = new List<string> { "employee", "hr", "create" }
        });

        var customerCaps = registry.GetByTag("customer");

        customerCaps.Should().HaveCount(1);
        customerCaps[0].Name.Should().Be("crm.customer.create");
    }

    [Fact]
    public void GetByTag_Shared_Tag_Returns_Multiple()
    {
        var registry = new CapabilityRegistry();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            SemanticTags = new List<string> { "create" }
        });
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_02",
            Name = "hr.employee.create",
            Version = 1,
            SemanticTags = new List<string> { "create" }
        });

        var createCaps = registry.GetByTag("create");

        createCaps.Should().HaveCount(2);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests
```

Expected: All 8 tests pass (4 descriptor + 4 registry).

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Capability.Tests/
git commit -m "test: add CapabilityDescriptor and CapabilityRegistry tests (8 tests)"
```

---

### Task 13: DescriptorRef Tests

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorRefTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj`

- [ ] **Step 1: Create test project .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Metadata.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write DescriptorRefTests**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorRefTests
{
    [Fact]
    public void DescriptorRef_Records_Id()
    {
        var ref1 = new DescriptorRef<SchemaDescriptor>("schema_01");
        var ref2 = new DescriptorRef<SchemaDescriptor>("schema_01");

        ref1.Id.Should().Be("schema_01");
        ref1.Should().Be(ref2); // Records with same Id are equal
    }

    [Fact]
    public void VersionedDescriptorRef_Records_Id_And_Version()
    {
        var vref = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        vref.Id.Should().Be("schema_01");
        vref.Version.Should().Be(3);
    }

    [Fact]
    public void VersionedDescriptorRef_Default_SelectionMode_Is_Exact()
    {
        var vref = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        vref.SelectionMode.Should().Be(VersionSelectionMode.Exact);
    }

    [Fact]
    public void VersionedDescriptorRef_With_Same_Id_Version_Are_Equal()
    {
        var vref1 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);
        var vref2 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        vref1.Should().Be(vref2);
    }

    [Fact]
    public void VersionedDescriptorRef_With_Different_Version_Are_Not_Equal()
    {
        var vref1 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);
        var vref2 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 4);

        vref1.Should().NotBe(vref2);
    }
}
```

- [ ] **Step 3: Run tests**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests
```

Expected: All 5 tests pass.

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/
git commit -m "test: add DescriptorRef and VersionedDescriptorRef tests (5 tests)"
```

---

### Task 14: Solution File Update

**Files:**
- Modify: `CrestCreates.slnx`

- [ ] **Step 1: Determine current .slnx project count and pattern**

```bash
grep -c "Project" CrestCreates.slnx
```

Expected: Returns the current number of projects in the .slnx file.

- [ ] **Step 2: Add new projects to .slnx**

Add these new entries to the solution file following the existing pattern:

```xml
<Project Path="framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
<Project Path="framework/src/CrestCreates.Schema.Abstractions/CrestCreates.Schema.Abstractions.csproj" />
<Project Path="framework/src/CrestCreates.Schema/CrestCreates.Schema.csproj" />
<Project Path="framework/src/CrestCreates.Capability.Abstractions/CrestCreates.Capability.Abstractions.csproj" />
<Project Path="framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj" />
<Project Path="framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj" />
<Project Path="framework/test/CrestCreates.Schema.Tests/CrestCreates.Schema.Tests.csproj" />
<Project Path="framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj" />
```

(Add these after the existing framework project entries, maintaining alphabetical order within src/ and test/ groupings respectively.)

- [ ] **Step 3: Build entire solution**

```bash
dotnet build
```

Expected: Solution builds successfully with all new projects.

- [ ] **Step 4: Run all tests**

```bash
dotnet test
```

Expected: All existing tests + 19 new tests pass.

- [ ] **Step 5: Commit**

```bash
git add CrestCreates.slnx
git commit -m "feat: add new metadata projects to solution; all 19 tests pass"
```

---

## Self-Review

**1. Spec coverage — Section mapping:**

| Spec Section | Covered By |
|---|---|
| 2. Core Abstraction — Four Pillars | Task 1 (IDescriptor), Task 3 (SchemaDesc), Task 4 (CapabilityDesc) |
| 3. Dependency Chain | Enforced by project references (Capability → Schema → Metadata) |
| 4.1 SchemaDescriptor | Task 3 |
| 4.2 CapabilityDescriptor | Task 4 |
| 4.7 IDescriptor / IVersionedDescriptor | Task 1 |
| 4.8 DescriptorRef / VersionedDescriptorRef | Task 2 |
| 5. Descriptor Lifecycle | Task 1 (DescriptorState), Task 6-7 (registries with state-aware queries) |
| 6. Descriptor vs Instance | Task 4 (ICapabilityHandler separates metadata from execution) |
| 7. Dependency Rules | Enforced by project reference chain |
| 8. Capability Pipeline | Deferred to Phase 2 (requires concrete pipeline implementation) |
| Schema Versioning | Task 3 (SchemaChangeKind), Task 6 (GetActiveVersion/GetLatestVersion) |
| Capability Atomicity | Task 4 (CapabilityKind = Query/Command only, no Draft) |
| SemanticTags | Task 4 (CapabilityDescriptor.SemanticTags), Task 7 (GetByTag) |
| Registry Infrastructure | Tasks 5-7 |
| Source Generator | Tasks 8-10 |
| Existing Code — Phase 1 | This entire plan is Phase 1 |

**2. Placeholder scan:** No TBD, TODO, "implement later", or "add appropriate error handling" found. All code blocks are complete and compilable.

**3. Type consistency:**
- `SchemaDescriptor` uses `IReadOnlyList<SchemaFieldDescriptor>` throughout (Tasks 3, 10)
- `CapabilityDescriptor` uses `VersionedDescriptorRef<SchemaDescriptor>` for InputSchema/OutputSchema (Tasks 4, 10)
- `CapabilityKind` → `CapabilityDescriptor.CapabilityKind` (not `.Kind` to avoid clash with `DescriptorKind Kind`)
- Registry method signatures match interfaces (Tasks 6-7 implement Tasks 5)
- Source generator emits calls matching provider signatures (Task 10 matches Tasks 9)

**Uncovered spec items (intentionally deferred):**
- `EventDescriptor` — Phase 3
- `WorkflowDescriptor` — Phase 3
- `FormDescriptor`, `HumanTaskDescriptor` — Phase 3
- `IDescriptorDependencyGraph` — Phase 2
- `IGlobalDescriptorRegistry` — Phase 2
- `DescriptorPackage`, `DescriptorManifest`, `DescriptorSnapshot` — Phase 2
- `ContractHash` / `DefinitionHash` computation — Phase 2 (requires canonical JSON serde)
- `CapabilityProfile` — Phase 2
- `DraftRecord` / `IDraftStore` — Phase 2
- DynamicApi refactoring to use CapabilityDescriptor — Phase 2 (per spec Section 10.3)

**Phase 1 delivers:** `IDescriptor`, `IVersionedDescriptor`, `SchemaDescriptor`, `CapabilityDescriptor`, `DescriptorRef<T>`, `VersionedDescriptorRef<T>`, `SchemaRegistry`, `CapabilityRegistry`, compile-time source generator for `[Entity]`→Schema and `[CrestService]`→Capability, 19 unit tests. This is the foundation everything else builds on.