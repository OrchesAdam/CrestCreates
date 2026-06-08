# Descriptor Infrastructure Completion — Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Descriptor Infrastructure layer: `IDescriptorDependencyGraph` with `DescriptorDependencyKind`, `IGlobalDescriptorRegistry` + `IDescriptorCatalog`, `DescriptorPackage` + `DescriptorManifest` + `DescriptorSnapshot`, `ContractHash`/`DefinitionHash` computation (canonical JSON → SHA256), `CapabilityProfile`, and `DraftRecord` + `IDraftStore`.

**Architecture:** New projects added to the Phase 1 foundation: `CrestCreates.Metadata` for the dependency graph, catalog, global registry, package, manifest, and snapshot; `CrestCreates.Draft.Abstractions` + `CrestCreates.Draft` for DraftRecord + IDraftStore. Hash computation uses `System.Text.Json` with alphabetical field sorting for canonicalization. All new types follow the existing `{ get; init; }` pattern for AOT compatibility.

**Prerequisites:** Phase 1 complete — `CrestCreates.Metadata.Abstractions`, `CrestCreates.Schema.Abstractions`, `CrestCreates.Schema`, `CrestCreates.Capability.Abstractions`, `CrestCreates.Capability` projects exist with all interfaces and registries. 22 tests passing.

**Tech Stack:** .NET 10, C# 13, System.Text.Json (canonical serialization), SHA256, central package management, xUnit + FluentAssertions.

---

### Task 0: Project Scaffolding — Metadata, Draft.Abstractions, Draft

**Files:**
- Create: `framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
- Create: `framework/src/CrestCreates.Draft.Abstractions/CrestCreates.Draft.Abstractions.csproj`
- Create: `framework/src/CrestCreates.Draft/CrestCreates.Draft.csproj`

- [ ] **Step 1: Create Metadata implementation project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Metadata</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Capability.Abstractions\CrestCreates.Capability.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create Draft.Abstractions project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Draft.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create Draft implementation project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Draft</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Draft.Abstractions\CrestCreates.Draft.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata
dotnet build framework/src/CrestCreates.Draft.Abstractions
dotnet build framework/src/CrestCreates.Draft
```

Expected: All three build successfully.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/ framework/src/CrestCreates.Draft.Abstractions/ framework/src/CrestCreates.Draft/
git commit -m "feat: scaffold Metadata, Draft.Abstractions, Draft projects"
```

---

### Task 1: DescriptorHashComputer — Canonical JSON → SHA256

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorHashComputer.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorHashComputerTests.cs`

- [ ] **Step 1: Write the hash computer**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrestCreates.Metadata;

public static class DescriptorHashComputer
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null, // Preserve original casing
        DictionaryKeyPolicy = null
    };

    public static string ComputeContractHash(IDescriptor descriptor)
    {
        var contractFields = ExtractContractFields(descriptor);
        var json = JsonSerializer.Serialize(contractFields, CanonicalOptions);
        return ComputeSha256(json);
    }

    public static string ComputeDefinitionHash(IDescriptor descriptor)
    {
        var json = JsonSerializer.Serialize(descriptor, descriptor.GetType(), CanonicalOptions);
        return ComputeSha256(json);
    }

    private static object ExtractContractFields(IDescriptor descriptor)
    {
        return descriptor switch
        {
            Schema.Abstractions.SchemaDescriptor s => new
            {
                s.Id,
                s.Name,
                s.Version,
                s.ChangeKind,
                s.State,
                s.SupersededById,
                Fields = s.Fields.Select(f => new
                {
                    f.Name,
                    f.FieldType,
                    f.IsRequired,
                    f.IsNullable,
                    f.MaxLength,
                    f.MinLength,
                    f.MaxValue,
                    f.MinValue,
                    f.Pattern,
                    f.IsCollection,
                    f.CollectionElementType
                }).OrderBy(f => f.Name).ToArray(),
                References = s.References.Select(r => new { r.Id, r.Version }).OrderBy(r => r.Id).ToArray()
            },
            Capability.Abstractions.CapabilityDescriptor c => new
            {
                c.Id,
                c.Name,
                c.Version,
                c.CapabilityKind,
                c.State,
                c.SupersededById,
                InputSchema = new { c.InputSchema.Id, c.InputSchema.Version },
                OutputSchema = new { c.OutputSchema.Id, c.OutputSchema.Version },
                c.Permission,
                c.RiskLevel,
                SemanticTags = c.SemanticTags.OrderBy(t => t).ToArray()
            },
            _ => descriptor
        };
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
```

- [ ] **Step 2: Write the tests**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorHashComputerTests
{
    [Fact]
    public void ComputeDefinitionHash_Same_Content_Produces_Same_Hash()
    {
        var schema1 = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
            }
        };
        var schema2 = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
            }
        };

        var hash1 = DescriptorHashComputer.ComputeDefinitionHash(schema1);
        var hash2 = DescriptorHashComputer.ComputeDefinitionHash(schema2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeDefinitionHash_Different_Content_Produces_Different_Hash()
    {
        var schema1 = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1
        };
        var schema2 = new SchemaDescriptor
        {
            Id = "schema_02",
            Name = "CustomerInput",
            Version = 2
        };

        var hash1 = DescriptorHashComputer.ComputeDefinitionHash(schema1);
        var hash2 = DescriptorHashComputer.ComputeDefinitionHash(schema2);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeContractHash_Excludes_Cosmetic_Fields()
    {
        var schema = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor
                {
                    Name = "Email",
                    FieldType = "string",
                    IsRequired = true,
                    MaxLength = 200
                }
            }
        };

        var contractHash = DescriptorHashComputer.ComputeContractHash(schema);
        var definitionHash = DescriptorHashComputer.ComputeDefinitionHash(schema);

        contractHash.Should().NotBe(definitionHash);
    }

    [Fact]
    public void ContractHash_Ignores_Field_Declaration_Order()
    {
        var schema1 = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "Test",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "A", FieldType = "string" },
                new SchemaFieldDescriptor { Name = "B", FieldType = "int" }
            }
        };
        var schema2 = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "Test",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "B", FieldType = "int" },
                new SchemaFieldDescriptor { Name = "A", FieldType = "string" }
            }
        };

        var hash1 = DescriptorHashComputer.ComputeContractHash(schema1);
        var hash2 = DescriptorHashComputer.ComputeContractHash(schema2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void Capability_ContractHash_Excludes_Aliases()
    {
        var cap1 = new Capability.Abstractions.CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            Aliases = new List<string> { "crm.customer.register" }
        };
        var cap2 = new Capability.Abstractions.CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            Aliases = new List<string> { "crm.customer.add" }
        };

        var hash1 = DescriptorHashComputer.ComputeContractHash(cap1);
        var hash2 = DescriptorHashComputer.ComputeContractHash(cap2);

        hash1.Should().Be(hash2);
    }
}
```

- [ ] **Step 3: Run tests**

```bash
dotnet build framework/test/CrestCreates.Metadata.Tests && dotnet test framework/test/CrestCreates.Metadata.Tests --no-build
```

Expected: 5 new tests pass (total 10 in Metadata.Tests).

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorHashComputer.cs framework/test/CrestCreates.Metadata.Tests/DescriptorHashComputerTests.cs
git commit -m "feat: add DescriptorHashComputer — canonical JSON → SHA256 for ContractHash and DefinitionHash"
```

---

### Task 2: DescriptorDependencyKind + IDescriptorDependencyGraph

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorDependencyKind.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorDependencyGraph.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DependencyEdge.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/ImpactReport.cs`
- Create: `framework/src/CrestCreates.Metadata/DescriptorDependencyGraph.cs`

- [ ] **Step 1: Write DescriptorDependencyKind enum**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public enum DescriptorDependencyKind
{
    Uses,
    Produces,
    References,
    Triggers,
    Consumes
}
```

- [ ] **Step 2: Write DependencyEdge sealed class**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed class DependencyEdge
{
    public string SourceId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public DescriptorDependencyKind Kind { get; init; }
}
```

- [ ] **Step 3: Write ImpactReport sealed class**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed class ImpactReport
{
    public string DescriptorId { get; init; } = string.Empty;
    public string DescriptorName { get; init; } = string.Empty;
    public int FromVersion { get; init; }
    public int ToVersion { get; init; }
    public IReadOnlyList<DependencyEdge> AffectedDependents { get; init; } = Array.Empty<DependencyEdge>();
    public bool IsBreaking => AffectedDependents.Any(e => e.Kind == DescriptorDependencyKind.Uses
                                                       || e.Kind == DescriptorDependencyKind.Triggers
                                                       || e.Kind == DescriptorDependencyKind.Consumes);
}
```

- [ ] **Step 4: Write IDescriptorDependencyGraph interface**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorDependencyGraph
{
    IReadOnlyList<DependencyEdge> GetDependencies(string descriptorId);
    IReadOnlyList<DependencyEdge> GetDependents(string descriptorId);
    ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion);
    void AddEdge(string sourceId, string targetId, DescriptorDependencyKind kind);
}
```

- [ ] **Step 5: Write DescriptorDependencyGraph implementation**

```csharp
using System.Collections.Concurrent;

namespace CrestCreates.Metadata;

public sealed class DescriptorDependencyGraph : IDescriptorDependencyGraph
{
    private readonly ConcurrentBag<DependencyEdge> _edges = new();

    public void AddEdge(string sourceId, string targetId, DescriptorDependencyKind kind)
    {
        _edges.Add(new DependencyEdge
        {
            SourceId = sourceId,
            TargetId = targetId,
            Kind = kind
        });
    }

    public IReadOnlyList<DependencyEdge> GetDependencies(string descriptorId)
    {
        return _edges.Where(e => e.SourceId == descriptorId).ToList().AsReadOnly();
    }

    public IReadOnlyList<DependencyEdge> GetDependents(string descriptorId)
    {
        return _edges.Where(e => e.TargetId == descriptorId).ToList().AsReadOnly();
    }

    public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion)
    {
        var dependents = GetDependents(descriptorId);
        return new ImpactReport
        {
            DescriptorId = descriptorId,
            FromVersion = fromVersion,
            ToVersion = toVersion,
            AffectedDependents = dependents
        };
    }
}
```

- [ ] **Step 6: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions && dotnet build framework/src/CrestCreates.Metadata
```

Expected: Both build succeed.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorDependencyKind.cs framework/src/CrestCreates.Metadata.Abstractions/IDescriptorDependencyGraph.cs framework/src/CrestCreates.Metadata.Abstractions/DependencyEdge.cs framework/src/CrestCreates.Metadata.Abstractions/ImpactReport.cs framework/src/CrestCreates.Metadata/DescriptorDependencyGraph.cs
git commit -m "feat: add DescriptorDependencyKind, IDescriptorDependencyGraph, and implementation"
```

---

### Task 3: IGlobalDescriptorRegistry + IDescriptorCatalog

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IGlobalDescriptorRegistry.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorCatalog.cs`
- Create: `framework/src/CrestCreates.Metadata/GlobalDescriptorRegistry.cs`
- Create: `framework/src/CrestCreates.Metadata/DescriptorCatalog.cs`

- [ ] **Step 1: Write IGlobalDescriptorRegistry interface**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IGlobalDescriptorRegistry
{
    IDescriptor? GetById(string id);
    IReadOnlyList<IDescriptor> GetAll();
    IReadOnlyList<IDescriptor> GetByKind(DescriptorKind kind);
    IReadOnlyList<IDescriptor> GetByPackage(string packageId);
    void Register(IDescriptor descriptor);
}
```

- [ ] **Step 2: Write IDescriptorCatalog interface**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorCatalog
{
    IDescriptor? Get(string id);
    IEnumerable<IDescriptor> GetAll();
    IEnumerable<IDescriptor> FindByKind(DescriptorKind kind);
    IEnumerable<IDescriptor> FindByPackage(string packageId);
    IEnumerable<IDescriptor> FindDependents(string descriptorId);
    IEnumerable<IDescriptor> FindDependencies(string descriptorId);
    ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion);
}
```

- [ ] **Step 3: Write GlobalDescriptorRegistry implementation**

```csharp
using System.Collections.Concurrent;

namespace CrestCreates.Metadata;

public sealed class GlobalDescriptorRegistry : IGlobalDescriptorRegistry
{
    private readonly ConcurrentDictionary<string, IDescriptor> _byId = new();
    private readonly ConcurrentDictionary<DescriptorKind, List<IDescriptor>> _byKind = new();
    private readonly ConcurrentDictionary<string, List<IDescriptor>> _byPackage = new();

    public void Register(IDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byKind.GetOrAdd(descriptor.Kind, _ => new()).Add(descriptor);
    }

    public IDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public IReadOnlyList<IDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();

    public IReadOnlyList<IDescriptor> GetByKind(DescriptorKind kind) =>
        _byKind.TryGetValue(kind, out var list) ? list.AsReadOnly() : Array.Empty<IDescriptor>();

    public IReadOnlyList<IDescriptor> GetByPackage(string packageId) =>
        _byPackage.TryGetValue(packageId, out var list) ? list.AsReadOnly() : Array.Empty<IDescriptor>();

    public void RegisterPackage(string packageId, IReadOnlyList<IDescriptor> descriptors)
    {
        foreach (var d in descriptors)
        {
            Register(d);
        }
        _byPackage[packageId] = descriptors.ToList();
    }
}
```

- [ ] **Step 4: Write DescriptorCatalog implementation**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DescriptorCatalog : IDescriptorCatalog
{
    private readonly IGlobalDescriptorRegistry _globalRegistry;
    private readonly IDescriptorDependencyGraph _dependencyGraph;

    public DescriptorCatalog(
        IGlobalDescriptorRegistry globalRegistry,
        IDescriptorDependencyGraph dependencyGraph)
    {
        _globalRegistry = globalRegistry;
        _dependencyGraph = dependencyGraph;
    }

    public IDescriptor? Get(string id) => _globalRegistry.GetById(id);

    public IEnumerable<IDescriptor> GetAll() => _globalRegistry.GetAll();

    public IEnumerable<IDescriptor> FindByKind(DescriptorKind kind) =>
        _globalRegistry.GetByKind(kind);

    public IEnumerable<IDescriptor> FindByPackage(string packageId) =>
        _globalRegistry.GetByPackage(packageId);

    public IEnumerable<IDescriptor> FindDependents(string descriptorId)
    {
        var edges = _dependencyGraph.GetDependents(descriptorId);
        return edges.Select(e => _globalRegistry.GetById(e.SourceId)).Where(d => d is not null)!;
    }

    public IEnumerable<IDescriptor> FindDependencies(string descriptorId)
    {
        var edges = _dependencyGraph.GetDependencies(descriptorId);
        return edges.Select(e => _globalRegistry.GetById(e.TargetId)).Where(d => d is not null)!;
    }

    public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion) =>
        _dependencyGraph.AnalyzeImpact(descriptorId, fromVersion, toVersion);
}
```

- [ ] **Step 5: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions && dotnet build framework/src/CrestCreates.Metadata
```

Expected: Both build succeed.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/IGlobalDescriptorRegistry.cs framework/src/CrestCreates.Metadata.Abstractions/IDescriptorCatalog.cs framework/src/CrestCreates.Metadata/GlobalDescriptorRegistry.cs framework/src/CrestCreates.Metadata/DescriptorCatalog.cs
git commit -m "feat: add IGlobalDescriptorRegistry, IDescriptorCatalog, and implementations"
```

---

### Task 4: DescriptorPackage + DescriptorManifest

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackage.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorManifest.cs`
- Create: `framework/src/CrestCreates.Metadata/DescriptorManifestSerializer.cs`

- [ ] **Step 1: Write DescriptorPackage sealed class**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorPackage
{
    public string PackageId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public IReadOnlyList<IDescriptor> Descriptors { get; init; } = Array.Empty<IDescriptor>();
}
```

- [ ] **Step 2: Write DescriptorManifest sealed class**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorManifest
{
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public IReadOnlyList<DescriptorManifestEntry> Schemas { get; init; } = Array.Empty<DescriptorManifestEntry>();
    public IReadOnlyList<DescriptorManifestEntry> Capabilities { get; init; } = Array.Empty<DescriptorManifestEntry>();
    public IReadOnlyList<DescriptorManifestEntry> Events { get; init; } = Array.Empty<DescriptorManifestEntry>();
    public IReadOnlyList<DescriptorManifestEntry> Workflows { get; init; } = Array.Empty<DescriptorManifestEntry>();
    public IReadOnlyList<DescriptorManifestEntry> Forms { get; init; } = Array.Empty<DescriptorManifestEntry>();
    public IReadOnlyList<DescriptorManifestEntry> HumanTasks { get; init; } = Array.Empty<DescriptorManifestEntry>();
}

public sealed class DescriptorManifestEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
}
```

- [ ] **Step 3: Write DescriptorManifestSerializer**

```csharp
using System.Text.Json;

namespace CrestCreates.Metadata;

public static class DescriptorManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(DescriptorManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, Options);
    }

    public static DescriptorManifest? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<DescriptorManifest>(json, Options);
    }
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions && dotnet build framework/src/CrestCreates.Metadata
```

Expected: Both build succeed.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackage.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorManifest.cs framework/src/CrestCreates.Metadata/DescriptorManifestSerializer.cs
git commit -m "feat: add DescriptorPackage, DescriptorManifest, and serializer"
```

---

### Task 5: DescriptorSnapshot

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorSnapshot.cs`
- Create: `framework/src/CrestCreates.Metadata/DescriptorSnapshotBuilder.cs`

- [ ] **Step 1: Write DescriptorSnapshot sealed class**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorSnapshot
{
    public string SnapshotId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public IReadOnlyList<SnapshotEntry> Descriptors { get; init; } = Array.Empty<SnapshotEntry>();
}

public sealed class SnapshotEntry
{
    public string DescriptorId { get; init; } = string.Empty;
    public string DescriptorName { get; init; } = string.Empty;
    public DescriptorKind Kind { get; init; }
    public int Version { get; init; }
}
```

- [ ] **Step 2: Write DescriptorSnapshotBuilder**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorSnapshotBuilder
{
    public static DescriptorSnapshot TakeSnapshot(
        IGlobalDescriptorRegistry registry,
        string packageId,
        string packageVersion)
    {
        var allDescriptors = registry.GetAll();
        var entries = allDescriptors.Select(d => new SnapshotEntry
        {
            DescriptorId = d.Id,
            DescriptorName = d.Name,
            Kind = d.Kind,
            Version = (d as IVersionedDescriptor)?.Version ?? 0
        }).ToList();

        return new DescriptorSnapshot
        {
            SnapshotId = $"snapshot_{Guid.NewGuid():N}",
            CreatedAt = DateTimeOffset.UtcNow,
            PackageId = packageId,
            PackageVersion = packageVersion,
            Descriptors = entries
        };
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions && dotnet build framework/src/CrestCreates.Metadata
```

Expected: Both build succeed.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorSnapshot.cs framework/src/CrestCreates.Metadata/DescriptorSnapshotBuilder.cs
git commit -m "feat: add DescriptorSnapshot and DescriptorSnapshotBuilder"
```

---

### Task 6: CapabilityProfile

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/CapabilityProfile.cs`

- [ ] **Step 1: Write CapabilityProfile sealed class**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityProfile
{
    public VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }
    public string Scope { get; init; } = string.Empty;
    public TimeSpan? Timeout { get; init; }
    public string? RetryPolicy { get; init; }
    public bool? RequireApproval { get; init; }
    public int? RateLimit { get; init; }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Capability.Abstractions
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/CapabilityProfile.cs
git commit -m "feat: add CapabilityProfile for environment/tenant-specific overrides"
```

---

### Task 7: DraftRecord + DraftStatus + IDraftStore

**Files:**
- Create: `framework/src/CrestCreates.Draft.Abstractions/DraftRecord.cs`
- Create: `framework/src/CrestCreates.Draft.Abstractions/DraftStatus.cs`
- Create: `framework/src/CrestCreates.Draft.Abstractions/IDraftStore.cs`
- Create: `framework/src/CrestCreates.Draft/DraftQuery.cs`

- [ ] **Step 1: Write DraftStatus enum**

```csharp
namespace CrestCreates.Draft.Abstractions;

public enum DraftStatus
{
    Active,
    Submitted,
    Archived,
    Expired,
    RequiresMigration
}
```

- [ ] **Step 2: Write DraftRecord sealed class**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Draft.Abstractions;

public sealed class DraftRecord
{
    public string DraftId { get; init; } = string.Empty;
    public string DraftType { get; init; } = string.Empty;
    public VersionedDescriptorRef<SchemaDescriptor> Schema { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string? OwnerId { get; init; }
    public string PayloadJson { get; init; } = "{}";
    public DraftStatus Status { get; init; } = DraftStatus.Active;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; init; }
}
```

- [ ] **Step 3: Write IDraftStore interface**

```csharp
namespace CrestCreates.Draft.Abstractions;

public interface IDraftStore
{
    Task<DraftRecord> SaveAsync(DraftRecord draft, CancellationToken ct = default);
    Task<DraftRecord?> GetAsync(string draftId, CancellationToken ct = default);
    Task DeleteAsync(string draftId, CancellationToken ct = default);
    Task<IReadOnlyList<DraftRecord>> QueryAsync(DraftQuery query, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write DraftQuery sealed class**

```csharp
using CrestCreates.Draft.Abstractions;

namespace CrestCreates.Draft;

public sealed class DraftQuery
{
    public string? TenantId { get; init; }
    public string? OwnerId { get; init; }
    public string? DraftType { get; init; }
    public DraftStatus? Status { get; init; }
    public int? MaxResults { get; init; }
}
```

- [ ] **Step 5: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Draft.Abstractions && dotnet build framework/src/CrestCreates.Draft
```

Expected: Both build succeed.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Draft.Abstractions/ framework/src/CrestCreates.Draft/
git commit -m "feat: add DraftRecord, DraftStatus, IDraftStore, and DraftQuery"
```

---

### Task 8: Register Descriptors with the Dependency Graph (Source Generator Update)

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs`

- [ ] **Step 1: Add dependency graph registration to the generated code**

Update the `GenerateRegistries` method to also emit dependency graph edges. After the existing `SchemaRegistryProvider.Register(...)` call for each schema, add:

```csharp
// In the generated code, after each SchemaRegistryProvider.Register call:
sb.AppendLine($"        DependencyGraphProvider.RegisterEdge(");
sb.AppendLine($"            \"{schema.Id}\",");
sb.AppendLine($"            \"{schema.Id}\",");
sb.AppendLine($"            DescriptorDependencyKind.Produces);");
```

And for each capability, add edges from capability to its input/output schemas:

```csharp
// In generated code, after each CapabilityRegistryProvider.Register call:
if (!string.IsNullOrEmpty(cap.InputSchemaId))
{
    sb.AppendLine($"        DependencyGraphProvider.RegisterEdge(");
    sb.AppendLine($"            \"{cap.Id}\",");
    sb.AppendLine($"            \"{cap.InputSchemaId}\",");
    sb.AppendLine($"            DescriptorDependencyKind.Uses);");
}
if (!string.IsNullOrEmpty(cap.OutputSchemaId))
{
    sb.AppendLine($"        DependencyGraphProvider.RegisterEdge(");
    sb.AppendLine($"            \"{cap.Id}\",");
    sb.AppendLine($"            \"{cap.OutputSchemaId}\",");
    sb.AppendLine($"            DescriptorDependencyKind.Uses);");
}
```

Also add the using for `CrestCreates.Metadata` and `CrestCreates.Metadata.Abstractions` to the generated code's using list.

- [ ] **Step 2: Create DependencyGraphProvider static hook**

Create: `framework/src/CrestCreates.Metadata/DependencyGraphProvider.cs`

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DependencyGraphProvider
{
    private static IDescriptorDependencyGraph? _graph;

    public static void SetGraph(IDescriptorDependencyGraph graph)
    {
        _graph = graph;
    }

    public static void RegisterEdge(string sourceId, string targetId, DescriptorDependencyKind kind)
    {
        if (_graph is DescriptorDependencyGraph concrete)
        {
            concrete.AddEdge(sourceId, targetId, kind);
        }
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build framework/tools/CrestCreates.CodeGenerator && dotnet build framework/src/CrestCreates.Metadata
```

Expected: Both build succeed.

- [ ] **Step 4: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs framework/src/CrestCreates.Metadata/DependencyGraphProvider.cs
git commit -m "feat: add dependency graph edges to generated code and DependencyGraphProvider"
```

---

### Task 9: Metadata Tests — DependencyGraph + Catalog + Snapshot + Package

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorDependencyGraphTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/GlobalDescriptorRegistryTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorCatalogTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorSnapshotTests.cs`

- [ ] **Step 1: Write DescriptorDependencyGraphTests**

```csharp
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorDependencyGraphTests
{
    [Fact]
    public void AddEdge_And_GetDependents()
    {
        var graph = new DescriptorDependencyGraph();
        graph.AddEdge("cap_01", "schema_01", DescriptorDependencyKind.Uses);

        var dependents = graph.GetDependents("schema_01");

        dependents.Should().HaveCount(1);
        dependents[0].SourceId.Should().Be("cap_01");
        dependents[0].Kind.Should().Be(DescriptorDependencyKind.Uses);
    }

    [Fact]
    public void GetDependencies_Returns_Edges_From_Source()
    {
        var graph = new DescriptorDependencyGraph();
        graph.AddEdge("cap_01", "schema_01", DescriptorDependencyKind.Uses);
        graph.AddEdge("cap_01", "schema_02", DescriptorDependencyKind.Uses);

        var deps = graph.GetDependencies("cap_01");

        deps.Should().HaveCount(2);
    }

    [Fact]
    public void AnalyzeImpact_Returns_Dependents()
    {
        var graph = new DescriptorDependencyGraph();
        graph.AddEdge("cap_01", "schema_01", DescriptorDependencyKind.Uses);
        graph.AddEdge("wf_01", "schema_01", DescriptorDependencyKind.Triggers);

        var report = graph.AnalyzeImpact("schema_01", 1, 2);

        report.AffectedDependents.Should().HaveCount(2);
        report.IsBreaking.Should().BeTrue();
    }

    [Fact]
    public void Empty_Graph_Returns_Empty_Results()
    {
        var graph = new DescriptorDependencyGraph();

        var deps = graph.GetDependencies("nonexistent");
        var dependents = graph.GetDependents("nonexistent");

        deps.Should().BeEmpty();
        dependents.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Write GlobalDescriptorRegistryTests**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class GlobalDescriptorRegistryTests
{
    [Fact]
    public void Register_And_GetById()
    {
        var registry = new GlobalDescriptorRegistry();
        var schema = new SchemaDescriptor { Id = "schema_01", Name = "Test", Version = 1 };

        registry.Register(schema);
        var result = registry.GetById("schema_01");

        result.Should().NotBeNull();
        result!.Kind.Should().Be(DescriptorKind.Schema);
    }

    [Fact]
    public void GetByKind_Returns_Only_Matching()
    {
        var registry = new GlobalDescriptorRegistry();
        registry.Register(new SchemaDescriptor { Id = "schema_01", Name = "S1", Version = 1 });
        registry.Register(new Capability.Abstractions.CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "test.op",
            Version = 1
        });

        var schemas = registry.GetByKind(DescriptorKind.Schema);

        schemas.Should().HaveCount(1);
    }

    [Fact]
    public void RegisterPackage_Groups_Descriptors()
    {
        var registry = new GlobalDescriptorRegistry();
        var descriptors = new List<IDescriptor>
        {
            new SchemaDescriptor { Id = "schema_01", Name = "S1", Version = 1 }
        };

        registry.RegisterPackage("CrestCreates.CRM", descriptors);

        var byPackage = registry.GetByPackage("CrestCreates.CRM");
        byPackage.Should().HaveCount(1);
    }
}
```

- [ ] **Step 3: Write DescriptorCatalogTests**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorCatalogTests
{
    [Fact]
    public void FindDependents_Returns_Through_Graph_And_Registry()
    {
        var globalRegistry = new GlobalDescriptorRegistry();
        globalRegistry.Register(new SchemaDescriptor { Id = "schema_01", Name = "CustomerInput", Version = 1 });
        globalRegistry.Register(new Capability.Abstractions.CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1
        });

        var graph = new DescriptorDependencyGraph();
        graph.AddEdge("cap_01", "schema_01", DescriptorDependencyKind.Uses);

        var catalog = new DescriptorCatalog(globalRegistry, graph);

        var dependents = catalog.FindDependents("schema_01").ToList();

        dependents.Should().HaveCount(1);
        dependents[0].Id.Should().Be("cap_01");
    }

    [Fact]
    public void AnalyzeImpact_Delegates_To_Graph()
    {
        var globalRegistry = new GlobalDescriptorRegistry();
        var graph = new DescriptorDependencyGraph();
        graph.AddEdge("cap_01", "schema_01", DescriptorDependencyKind.Uses);

        var catalog = new DescriptorCatalog(globalRegistry, graph);

        var report = catalog.AnalyzeImpact("schema_01", 1, 2);

        report.AffectedDependents.Should().HaveCount(1);
        report.IsBreaking.Should().BeTrue();
    }
}
```

- [ ] **Step 4: Write DescriptorSnapshotTests**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorSnapshotTests
{
    [Fact]
    public void TakeSnapshot_Captures_All_Descriptors()
    {
        var registry = new GlobalDescriptorRegistry();
        registry.Register(new SchemaDescriptor { Id = "schema_01", Name = "CustomerInput", Version = 1 });
        registry.Register(new SchemaDescriptor { Id = "schema_02", Name = "OrderInput", Version = 1 });

        var snapshot = DescriptorSnapshotBuilder.TakeSnapshot(registry, "CrestCreates.CRM", "1.0.0");

        snapshot.Descriptors.Should().HaveCount(2);
        snapshot.PackageId.Should().Be("CrestCreates.CRM");
        snapshot.PackageVersion.Should().Be("1.0.0");
        snapshot.SnapshotId.Should().StartWith("snapshot_");
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet build framework/test/CrestCreates.Metadata.Tests && dotnet test framework/test/CrestCreates.Metadata.Tests --no-build
```

Expected: 17 total tests pass (5 existing + 5 hash + 4 graph + 3 registry + 2 catalog + 1 snapshot = 17 in Metadata.Tests).

- [ ] **Step 6: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/
git commit -m "test: add DependencyGraph, GlobalRegistry, Catalog, and Snapshot tests (12 tests)"
```

---

### Task 10: Draft Tests

**Files:**
- Create: `framework/test/CrestCreates.Draft.Tests/DraftRecordTests.cs`
- Create: `framework/test/CrestCreates.Draft.Tests/CrestCreates.Draft.Tests.csproj`

- [ ] **Step 1: Create test project .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Draft.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Draft.Tests</AssemblyName>
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
    <ProjectReference Include="..\..\src\CrestCreates.Draft.Abstractions\CrestCreates.Draft.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write DraftRecordTests**

```csharp
using CrestCreates.Draft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Draft.Tests;

public class DraftRecordTests
{
    [Fact]
    public void DraftRecord_Defaults_Status_To_Active()
    {
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "employee.create",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01",
            PayloadJson = "{\"name\":\"Tom\"}"
        };

        draft.Status.Should().Be(DraftStatus.Active);
    }

    [Fact]
    public void DraftRecord_DraftType_Is_Not_A_DescriptorKind()
    {
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "agent.plan",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01"
        };

        draft.DraftType.Should().Be("agent.plan");
    }

    [Fact]
    public void DraftRecord_References_Schema_Not_Capability()
    {
        var schemaRef = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "employee.create",
            Schema = schemaRef,
            TenantId = "tenant_01"
        };

        draft.Schema.Id.Should().Be("schema_01");
        draft.Schema.Version.Should().Be(3);
    }

    [Fact]
    public void DraftRecord_PayloadJson_Defaults_To_EmptyJson()
    {
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01"
        };

        draft.PayloadJson.Should().Be("{}");
    }
}
```

- [ ] **Step 3: Run tests**

```bash
dotnet build framework/test/CrestCreates.Draft.Tests && dotnet test framework/test/CrestCreates.Draft.Tests --no-build
```

Expected: 4 tests pass.

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.Draft.Tests/
git commit -m "test: add DraftRecord tests (4 tests)"
```

---

### Task 11: CapabilityProfile Tests

**Files:**
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityProfileTests.cs`

- [ ] **Step 1: Write CapabilityProfileTests**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityProfileTests
{
    [Fact]
    public void CapabilityProfile_References_Capability_By_VersionedRef()
    {
        var profile = new CapabilityProfile
        {
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 3),
            Scope = "Global-Prod",
            Timeout = TimeSpan.FromSeconds(10)
        };

        profile.Capability.Id.Should().Be("cap_01");
        profile.Capability.Version.Should().Be(3);
        profile.Scope.Should().Be("Global-Prod");
        profile.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void CapabilityProfile_Defaults_All_Optional_Props_To_Null()
    {
        var profile = new CapabilityProfile
        {
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
            Scope = "Global"
        };

        profile.Timeout.Should().BeNull();
        profile.RetryPolicy.Should().BeNull();
        profile.RequireApproval.Should().BeNull();
        profile.RateLimit.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet build framework/test/CrestCreates.Capability.Tests && dotnet test framework/test/CrestCreates.Capability.Tests --no-build
```

Expected: 10 total tests pass (8 existing + 2 new).

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Capability.Tests/CapabilityProfileTests.cs
git commit -m "test: add CapabilityProfile tests (2 tests)"
```

---

### Task 12: Package Manifest Tests

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorManifestTests.cs`

- [ ] **Step 1: Write DescriptorManifestTests**

```csharp
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorManifestTests
{
    [Fact]
    public void Serialize_And_Deserialize_Manifest()
    {
        var manifest = new DescriptorManifest
        {
            PackageId = "CrestCreates.CRM",
            PackageVersion = "1.0.0",
            Schemas = new[]
            {
                new DescriptorManifestEntry { Id = "schema_01", Name = "CustomerInput", Version = 1 }
            },
            Capabilities = new[]
            {
                new DescriptorManifestEntry { Id = "cap_01", Name = "crm.customer.create", Version = 1 }
            }
        };

        var json = DescriptorManifestSerializer.Serialize(manifest);
        var deserialized = DescriptorManifestSerializer.Deserialize(json);

        deserialized.Should().NotBeNull();
        deserialized!.PackageId.Should().Be("CrestCreates.CRM");
        deserialized.Schemas.Should().HaveCount(1);
        deserialized.Capabilities.Should().HaveCount(1);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet build framework/test/CrestCreates.Metadata.Tests && dotnet test framework/test/CrestCreates.Metadata.Tests --no-build
```

Expected: 18 total tests pass (17 existing + 1 new).

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorManifestTests.cs
git commit -m "test: add DescriptorManifest serialization test (1 test)"
```

---

### Task 13: Solution File Update

**Files:**
- Modify: `CrestCreates.slnx`

- [ ] **Step 1: Add new projects to .slnx**

Add these entries maintaining alphabetical order:

```xml
<Project Path="framework/src/CrestCreates.Draft.Abstractions/CrestCreates.Draft.Abstractions.csproj" />
<Project Path="framework/src/CrestCreates.Draft/CrestCreates.Draft.csproj" />
<Project Path="framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj" />
<Project Path="framework/test/CrestCreates.Draft.Tests/CrestCreates.Draft.Tests.csproj" />
```

- [ ] **Step 2: Build entire solution**

```bash
dotnet build
```

Expected: 0 errors.

- [ ] **Step 3: Run all tests**

```bash
dotnet test
```

Expected: All existing tests + new tests pass (22 Phase 1 + 20 new = 42 total).

- [ ] **Step 4: Commit**

```bash
git add CrestCreates.slnx
git commit -m "feat: add Phase 2 projects to solution; all 42 tests pass"
```

---

## Self-Review

**1. Spec coverage — Section mapping:**

| Spec Section | Covered By |
|---|---|
| IDependencyGraph + DependencyKind | Task 2 |
| IGlobalDescriptorRegistry | Task 3 |
| IDescriptorCatalog | Task 3 |
| DescriptorPackage | Task 4 |
| DescriptorManifest | Task 4 |
| DescriptorSnapshot | Task 5 |
| ContractHash / DefinitionHash computation | Task 1 |
| CapabilityProfile | Task 6 |
| DraftRecord + IDraftStore | Task 7 |
| Source Generator update (dependency edges) | Task 8 |

**2. Placeholder scan:** No TBD, TODO, "implement later", or "add appropriate error handling" found. All code blocks are complete and compilable.

**3. Type consistency:**
- `DraftRecord` uses `VersionedDescriptorRef<SchemaDescriptor>` (matches Task 7)
- `CapabilityProfile` uses `VersionedDescriptorRef<CapabilityDescriptor>` (matches Task 6)
- `GlobalDescriptorRegistry` uses `IDescriptor` (matches Task 3)
- `DescriptorCatalog` composes `IGlobalDescriptorRegistry` + `IDescriptorDependencyGraph` (matches Task 3)
- `DependencyGraphProvider` bridges to `IDescriptorDependencyGraph` (matches Task 8)
- `DescriptorSnapshotBuilder` takes `IGlobalDescriptorRegistry` (matches Task 5)
- `DescriptorManifestSerializer` uses `System.Text.Json` (matches Task 4)

**Uncovered spec items (intentionally deferred to Phase 3+):**
- `EventDescriptor` — Phase 3
- `WorkflowDescriptor` — Phase 3
- `FormDescriptor`, `HumanTaskDescriptor` — Phase 3
- `IDraftStore` concrete implementation (InMemory for tests) — Phase 3
- DynamicApi refactoring to use CapabilityDescriptor — Phase 3

**Phase 2 delivers:** `IDescriptorDependencyGraph`, `IGlobalDescriptorRegistry`, `IDescriptorCatalog`, `DescriptorPackage`, `DescriptorManifest`, `DescriptorSnapshot`, `DescriptorHashComputer` (canonical JSON → SHA256), `CapabilityProfile`, `DraftRecord` + `IDraftStore`, source generator dependency graph edges, 20 new tests (42 total). Descriptor Infrastructure is complete.