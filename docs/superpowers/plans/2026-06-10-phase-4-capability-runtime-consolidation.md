# Phase 4 — Capability Runtime Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge the two-track CapabilityDescriptor/CapabilityRegistry into a single unified implementation and add Dispatcher, Audit, Resolver, and Bootstrap Validators.

**Architecture:** Consolidate Metadata.Abstractions as the single source for CapabilityDescriptor; delete the Capability.Abstractions version. Unify CapabilityRegistry under RegistryBase. Add ICapabilityDispatcher as a Facade over ICapabilityPipeline. Add AuditMiddleware, ICapabilityAuditStore, ICapabilityResolver, and IBootstrapValidator.

**Tech Stack:** .NET 10, C# 13, xUnit, FluentAssertions, Moq

**Design Spec:** `docs/superpowers/specs/2026-06-09-phase-4-capability-runtime-consolidation-design.md`

---

### Task 1: Migrate Enums to Metadata.Abstractions

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/CapabilityKind.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/CapabilityRiskLevel.cs`
- Modify: `framework/src/CrestCreates.Capability.Abstractions/CapabilityKind.cs` (move to RecycleBin)
- Modify: `framework/src/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs` (line containing CapabilityRiskLevel)

- [ ] **Step 1: Create CapabilityKind.cs in Metadata.Abstractions**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/CapabilityKind.cs
namespace CrestCreates.Metadata.Abstractions;

public enum CapabilityKind
{
    Query,
    Command
}
```

- [ ] **Step 2: Create CapabilityRiskLevel.cs in Metadata.Abstractions**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/CapabilityRiskLevel.cs
namespace CrestCreates.Metadata.Abstractions;

public enum CapabilityRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}
```

- [ ] **Step 3: Move old CapabilityKind.cs to RecycleBin**

```bash
mv framework/src/CrestCreates.Capability.Abstractions/CapabilityKind.cs /home/orches/workspace/CrestCreates/99_RecycleBin/
```

- [ ] **Step 4: Update Capability.Abstractions CapabilityDescriptor**

Read `framework/src/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs` and delete the `CapabilityRiskLevel` enum definition at the bottom of the file (lines defining `public enum CapabilityRiskLevel { Low, Medium, High, Critical }`). The CapabilityDescriptor class now references `CrestCreates.Metadata.Abstractions.CapabilityRiskLevel`.

- [ ] **Step 5: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions && dotnet build framework/src/CrestCreates.Capability.Abstractions`
Expected: Build succeeds, no CS0246 (type not found) errors.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/CapabilityKind.cs framework/src/CrestCreates.Metadata.Abstractions/CapabilityRiskLevel.cs
git add 99_RecycleBin/
git add framework/src/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs
git commit -m "feat: migrate CapabilityKind and CapabilityRiskLevel enums to Metadata.Abstractions"
```

---

### Task 2: Unify CapabilityDescriptor in Metadata

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/CapabilityDescriptor.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/CapabilityDescriptorTests.cs`
- Delete: `framework/src/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs` (move to RecycleBin)

- [ ] **Step 1: Write failing tests for new CapabilityDescriptor properties**

```csharp
// framework/test/CrestCreates.Metadata.Tests/CapabilityDescriptorTests.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class CapabilityDescriptorTests
{
    [Fact]
    public void Has_runtime_properties_from_merged_descriptor()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "customer.create",
            Name = "Create Customer",
            Version = 1,
            CapabilityKind = CapabilityKind.Command,
            Permissions = new[] { "Customer.Create" },
            RiskLevel = CapabilityRiskLevel.Medium,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer", 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer_output", 1),
            SemanticTags = new[] { "customer", "crm" },
            Categories = new[] { "Customer" }
        };

        descriptor.CapabilityKind.Should().Be(CapabilityKind.Command);
        descriptor.Permissions.Should().Contain("Customer.Create");
        descriptor.RiskLevel.Should().Be(CapabilityRiskLevel.Medium);
        descriptor.InputSchema!.Value.Id.Should().Be("schema_customer");
        descriptor.OutputSchema!.Value.Id.Should().Be("schema_customer_output");
    }

    [Fact]
    public void Id_is_stable_identifier_name_is_display_name()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "customer.create",
            Name = "Create Customer",
            Version = 1
        };

        descriptor.Id.Should().Be("customer.create");
        descriptor.Name.Should().Be("Create Customer");
    }

    [Fact]
    public void Implements_IRelationshipAwareDescriptor()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "customer.create",
            Version = 1,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer", 1),
            Produces = new[] { new EventRef("event", "customer.created") }
        };

        var relationships = descriptor.GetRelationships();
        relationships.Should().NotBeEmpty();
        relationships.Should().Contain(r => r.Kind == RelationshipKind.Consumes);
        relationships.Should().Contain(r => r.Kind == RelationshipKind.Produces);
    }

    [Fact]
    public void Schema_refs_are_nullable()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "noop.ping",
            Version = 1
        };

        descriptor.InputSchema.Should().BeNull();
        descriptor.OutputSchema.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~CapabilityDescriptorTests"`
Expected: Build errors — CapabilityKind, Permissions, RiskLevel, InputSchema, OutputSchema don't exist on CapabilityDescriptor.

- [ ] **Step 3: Modify CapabilityDescriptor to add runtime properties**

Read and modify `framework/src/CrestCreates.Metadata/CapabilityDescriptor.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata;

public sealed class CapabilityDescriptor : IDescriptor, IVersionedDescriptor, IHasContractIdentity, IRelationshipAwareDescriptor
{
    // === IDescriptor ===
    public string Namespace { get; init; } = "capability";
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorKind Kind => DescriptorKind.Capability;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }

    // === IVersionedDescriptor ===
    public int Version { get; init; }

    // === IHasContractIdentity ===
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;

    // === Catalog Properties ===
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<EventRef> Produces { get; init; } = Array.Empty<EventRef>();
    public IReadOnlyList<EventRef> Consumes { get; init; } = Array.Empty<EventRef>();
    public IReadOnlyList<string> SemanticTags { get; init; } = Array.Empty<string>();

    // === Runtime Properties (merged from Capability.Abstractions) ===
    public CapabilityKind CapabilityKind { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor>? InputSchema { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor>? OutputSchema { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public CapabilityRiskLevel RiskLevel { get; init; } = CapabilityRiskLevel.Medium;

    // === IRelationshipAwareDescriptor ===
    public IReadOnlyList<DescriptorRelationship> GetRelationships()
    {
        var relationships = new List<DescriptorRelationship>();

        if (InputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                new DescriptorRef(Namespace, Id),
                new DescriptorRef(InputSchema.Value.Id, InputSchema.Value.Id, InputSchema.Value.Version),
                RelationshipKind.Consumes));
        }

        if (OutputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                new DescriptorRef(Namespace, Id),
                new DescriptorRef(OutputSchema.Value.Id, OutputSchema.Value.Id, OutputSchema.Value.Version),
                RelationshipKind.Produces));
        }

        if (SupersededById is not null)
        {
            relationships.Add(new DescriptorRelationship(
                new DescriptorRef(Namespace, Id),
                new DescriptorRef(Namespace, SupersededById),
                RelationshipKind.DependsOn));
        }

        foreach (var @event in Produces)
        {
            relationships.Add(new DescriptorRelationship(
                new DescriptorRef(Namespace, Id),
                new DescriptorRef(@event.Namespace, @event.Id, @event.Version),
                RelationshipKind.Produces));
        }

        foreach (var @event in Consumes)
        {
            relationships.Add(new DescriptorRelationship(
                new DescriptorRef(Namespace, Id),
                new DescriptorRef(@event.Namespace, @event.Id, @event.Version),
                RelationshipKind.Consumes));
        }

        return relationships;
    }
}

/// <summary>
/// Strong-typed event reference for Capability descriptors.
/// </summary>
public readonly record struct EventRef(string Namespace, string Id, int? Version = null) : IDescriptorRef;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~CapabilityDescriptorTests"`
Expected: 4 tests pass.

- [ ] **Step 5: Move old Capability.Abstractions CapabilityDescriptor to RecycleBin**

```bash
mv framework/src/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs /home/orches/workspace/CrestCreates/99_RecycleBin/
```

- [ ] **Step 6: Update all references from old CapabilityDescriptor to new one**

Search and fix all compilation errors in:
- `framework/src/CrestCreates.Capability/CapabilityRegistry.cs` — change `using CrestCreates.Capability.Abstractions;` to use Metadata's `CapabilityDescriptor`
- `framework/src/CrestCreates.Capability.Abstractions/ICapabilityRegistry.cs` — change return type to `CrestCreates.Metadata.CapabilityDescriptor`
- `framework/src/CrestCreates.Capability/CapabilityPipeline.cs` — change references
- `framework/src/CrestCreates.Capability/CapabilityHandlerResolver.cs` — change references (if any)
- `framework/src/CrestCreates.Capability/Middleware/*.cs` — change references
- All test files in `framework/test/CrestCreates.Capability.Tests/`

Delete `Aliases` references from any code that used the old Capability.Abstractions version.

- [ ] **Step 7: Build entire solution**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.Metadata/CapabilityDescriptor.cs
git add framework/test/CrestCreates.Metadata.Tests/CapabilityDescriptorTests.cs
git add 99_RecycleBin/
git add -u
git commit -m "feat: unify CapabilityDescriptor — merge runtime properties into Metadata version, remove Abstractions version"
```

---

### Task 3: Update ICapabilityRegistry Interface

**Files:**
- Modify: `framework/src/CrestCreates.Capability.Abstractions/ICapabilityRegistry.cs`

- [ ] **Step 1: Update ICapabilityRegistry to reference unified CapabilityDescriptor**

Read `framework/src/CrestCreates.Capability.Abstractions/ICapabilityRegistry.cs`. Change all references from `CrestCreates.Capability.Abstractions.CapabilityDescriptor` to `CrestCreates.Metadata.CapabilityDescriptor`:

```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityRegistry : IVersionedDescriptorRegistry<CapabilityDescriptor>
{
    IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind);
    IReadOnlyList<CapabilityDescriptor> GetByTag(string tag);
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Capability.Abstractions`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/ICapabilityRegistry.cs
git commit -m "feat: update ICapabilityRegistry to reference unified CapabilityDescriptor"
```

---

### Task 4: Unify CapabilityRegistry in Metadata

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/CapabilityRegistry.cs`
- Modify: `framework/test/CrestCreates.Metadata.Tests/CapabilityRegistryTests.cs`
- Delete: `framework/src/CrestCreates.Capability/CapabilityRegistry.cs` (move to RecycleBin)

- [ ] **Step 1: Write tests for ICapabilityRegistry methods on unified CapabilityRegistry**

Read the existing `CapabilityRegistryTests.cs` in Metadata.Tests. Add these test methods:

```csharp
[Fact]
public void GetByKind_returns_matching_capabilities()
{
    var descriptors = new List<CapabilityDescriptor>
    {
        new() { Id = "cmd.one", Name = "cmd.one", Version = 1, CapabilityKind = CapabilityKind.Command },
        new() { Id = "cmd.two", Name = "cmd.two", Version = 1, CapabilityKind = CapabilityKind.Command },
        new() { Id = "qry.one", Name = "qry.one", Version = 1, CapabilityKind = CapabilityKind.Query }
    };
    var providers = new[] { new TestDescriptorProvider(descriptors) };
    var validationEngine = new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>());
    var registry = new CapabilityRegistry(validationEngine);
    registry.Build(providers);

    var commands = registry.GetByKind(CapabilityKind.Command);

    commands.Should().HaveCount(2);
    commands.Should().OnlyContain(d => d.CapabilityKind == CapabilityKind.Command);
}

[Fact]
public void GetByTag_returns_matching_capabilities()
{
    var descriptors = new List<CapabilityDescriptor>
    {
        new() { Id = "a", Name = "a", Version = 1, SemanticTags = new[] { "customer", "crm" } },
        new() { Id = "b", Name = "b", Version = 1, SemanticTags = new[] { "order" } },
        new() { Id = "c", Name = "c", Version = 1, SemanticTags = new[] { "customer" } }
    };
    var providers = new[] { new TestDescriptorProvider(descriptors) };
    var validationEngine = new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>());
    var registry = new CapabilityRegistry(validationEngine);
    registry.Build(providers);

    var customerCaps = registry.GetByTag("customer");

    customerCaps.Should().HaveCount(2);
    customerCaps.Should().OnlyContain(d => d.SemanticTags.Contains("customer"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~GetByKind|GetByTag"`
Expected: Compile error — CapabilityRegistry doesn't implement ICapabilityRegistry.

- [ ] **Step 3: Update CapabilityRegistry to implement ICapabilityRegistry**

Modify `framework/src/CrestCreates.Metadata/CapabilityRegistry.cs`:

```csharp
using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class CapabilityRegistry : RegistryBase<CapabilityDescriptor>, ICapabilityRegistry
{
    protected override string RegistryNamespace => "capability";

    public CapabilityRegistry(IRegistryValidationEngine<CapabilityDescriptor> validationEngine)
        : base(validationEngine) { }

    public IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind)
    {
        return GetAll().Where(d => d.CapabilityKind == kind).ToList();
    }

    public IReadOnlyList<CapabilityDescriptor> GetByTag(string tag)
    {
        return GetAll().Where(d => d.SemanticTags.Contains(tag)).ToList();
    }

    protected override RegistrySnapshot<CapabilityDescriptor> BuildSnapshot(
        List<CapabilityDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<CapabilityDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
```

Also add `using CrestCreates.Capability.Abstractions;` to the file for the `ICapabilityRegistry` interface import.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~CapabilityRegistryTests"`
Expected: All tests pass.

- [ ] **Step 5: Move old Capability Registry to RecycleBin**

```bash
mv framework/src/CrestCreates.Capability/CapabilityRegistry.cs /home/orches/workspace/CrestCreates/99_RecycleBin/
```

- [ ] **Step 6: Update CapabilityPipeline to use new registry**

Read `framework/src/CrestCreates.Capability/CapabilityPipeline.cs`. Change:
- All references from `CrestCreates.Capability.Abstractions.CapabilityDescriptor` to `CrestCreates.Metadata.CapabilityDescriptor`
- `_registry.GetByName(capabilityName)` check — update to handle the unified descriptor
- Import: remove `using CrestCreates.Capability.Abstractions;` for the old descriptor, add `using CrestCreates.Metadata;`

The pipeline currently resolves descriptor via `_registry.GetActiveVersion(capabilityName) ?? _registry.GetByName(capabilityName)`. The unified registry (`RegistryBase`-based) supports `GetByName` out of the box. No change needed for the resolution logic itself — just the type references.

- [ ] **Step 7: Build entire solution**

Run: `dotnet build`
Expected: Build succeeds, no reference to old `Capability.Abstractions.CapabilityDescriptor` or old `Capability.CapabilityRegistry`.

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.Metadata/CapabilityRegistry.cs
git add framework/test/CrestCreates.Metadata.Tests/CapabilityRegistryTests.cs
git add 99_RecycleBin/
git add -u
git commit -m "feat: unify CapabilityRegistry — Metadata version implements ICapabilityRegistry, remove ConcurrentDictionary version"
```

---

### Task 5: Add CapabilityId and InvocationSource to ExecutionContext

**Files:**
- Modify: `framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs`
- Create: `framework/src/CrestCreates.Capability.Abstractions/Execution/InvocationSource.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityExecutionContextTests.cs` (modify existing)

- [ ] **Step 1: Create InvocationSource enum**

```csharp
// framework/src/CrestCreates.Capability.Abstractions/Execution/InvocationSource.cs
namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Identifies the source of a capability invocation.
/// No Unknown value — callers must explicitly set the source.
/// </summary>
public enum InvocationSource
{
    Http,
    Workflow,
    HumanTask,
    Agent,
    Mcp,
    Event,
    BackgroundJob,
    Internal
}
```

- [ ] **Step 2: Write test for new ExecutionContext fields**

```csharp
// Add to existing CapabilityExecutionContextTests.cs or create if not present:
[Fact]
public void Has_CapabilityId_and_InvocationSource_fields()
{
    var context = new CapabilityExecutionContext
    {
        CapabilityId = "customer.create",
        CapabilityName = "Create Customer",
        InvocationSource = InvocationSource.Workflow
    };

    context.CapabilityId.Should().Be("customer.create");
    context.InvocationSource.Should().Be(InvocationSource.Workflow);
}
```

- [ ] **Step 3: Run test to verify failure**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~Has_CapabilityId_and_InvocationSource"`
Expected: CS0117 — CapabilityId and InvocationSource don't exist on CapabilityExecutionContext.

- [ ] **Step 4: Add new fields to CapabilityExecutionContext**

Read `framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs` and add:

```csharp
// After existing CapabilityName property:
public string CapabilityId { get; init; } = string.Empty;

// At end of class:
public InvocationSource InvocationSource { get; init; }
```

- [ ] **Step 5: Run test to verify pass**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~Has_CapabilityId_and_InvocationSource"`
Expected: Pass.

- [ ] **Step 6: Build entire solution**

Run: `dotnet build`
Expected: Build succeeds (nothing else references the new fields yet).

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/Execution/InvocationSource.cs
git add framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs
git add framework/test/CrestCreates.Capability.Tests/
git commit -m "feat: add CapabilityId and InvocationSource to CapabilityExecutionContext"
```

---

### Task 6: Create ICapabilityResolver + CapabilityRef

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/CapabilityRef.cs`
- Create: `framework/src/CrestCreates.Capability.Abstractions/ICapabilityResolver.cs`
- Create: `framework/src/CrestCreates.Capability.Abstractions/CapabilityNotFoundException.cs`
- Create: `framework/src/CrestCreates.Capability/Internal/ICapabilityVersionResolver.cs`
- Create: `framework/src/CrestCreates.Capability/Internal/DefaultCapabilityVersionResolver.cs`
- Create: `framework/src/CrestCreates.Capability/DefaultCapabilityResolver.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityResolverTests.cs`

- [ ] **Step 1: Create CapabilityRef.cs**

```csharp
// framework/src/CrestCreates.Capability.Abstractions/CapabilityRef.cs
namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Structured reference to a capability. Avoids implicit string syntax.
/// </summary>
public readonly record struct CapabilityRef(string Id, int? Version = null)
{
    /// <summary>
    /// Parses string format:
    ///   "customer.create"     → ("customer.create", null)
    ///   "customer.create:3"   → ("customer.create", 3)
    /// </summary>
    public static CapabilityRef Parse(string input)
    {
        var separatorIndex = input.LastIndexOf(':');
        if (separatorIndex > 0 && int.TryParse(input.AsSpan(separatorIndex + 1), out var version))
        {
            return new CapabilityRef(input[..separatorIndex], version);
        }
        return new CapabilityRef(input);
    }

    public override string ToString()
        => Version.HasValue ? $"{Id}:{Version}" : Id;
}
```

- [ ] **Step 2: Create CapabilityNotFoundException.cs**

```csharp
// framework/src/CrestCreates.Capability.Abstractions/CapabilityNotFoundException.cs
namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityNotFoundException : Exception
{
    public CapabilityNotFoundException(string capabilityId)
        : base($"Capability '{capabilityId}' not found.")
    {
    }

    public CapabilityNotFoundException(CapabilityRef capabilityRef)
        : base($"Capability '{capabilityRef.Id}' (v{capabilityRef.Version?.ToString() ?? "latest"}) not found.")
    {
    }
}
```

- [ ] **Step 3: Create ICapabilityResolver.cs**

```csharp
// framework/src/CrestCreates.Capability.Abstractions/ICapabilityResolver.cs
using CrestCreates.Metadata;

namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Unified resolution entry point. All runtimes (Workflow, Agent, HTTP, MCP)
/// must resolve capabilities through this interface.
/// </summary>
public interface ICapabilityResolver
{
    CapabilityDescriptor Resolve(CapabilityRef capabilityRef);

    CapabilityDescriptor Resolve(string capabilityIdOrVersion)
        => Resolve(CapabilityRef.Parse(capabilityIdOrVersion));
}
```

- [ ] **Step 4: Create ICapabilityVersionResolver (internal)**

```csharp
// framework/src/CrestCreates.Capability/Internal/ICapabilityVersionResolver.cs
namespace CrestCreates.Capability.Internal;

internal interface ICapabilityVersionResolver
{
    CrestCreates.Metadata.CapabilityDescriptor Resolve(CapabilityRef capabilityRef);
}
```

- [ ] **Step 5: Create DefaultCapabilityVersionResolver**

```csharp
// framework/src/CrestCreates.Capability/Internal/DefaultCapabilityVersionResolver.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Internal;

internal sealed class DefaultCapabilityVersionResolver : ICapabilityVersionResolver
{
    private readonly CapabilityRegistry _registry;

    public DefaultCapabilityVersionResolver(CapabilityRegistry registry)
    {
        _registry = registry;
    }

    public CapabilityDescriptor Resolve(CapabilityRef capabilityRef)
    {
        if (capabilityRef.Version.HasValue)
        {
            var descriptor = _registry.GetByVersion(capabilityRef.Id, capabilityRef.Version.Value);
            if (descriptor is not null) return descriptor;
        }
        else
        {
            // Latest Active: State == Active
            var byName = _registry.GetByName(capabilityRef.Id);
            var active = byName
                .Where(d => d.State == DescriptorState.Active)
                .MaxBy(d => d.Version);
            if (active is not null) return active;
        }

        throw new CapabilityNotFoundException(capabilityRef);
    }
}
```

- [ ] **Step 6: Create DefaultCapabilityResolver**

```csharp
// framework/src/CrestCreates.Capability/DefaultCapabilityResolver.cs
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Internal;
using CrestCreates.Metadata;

namespace CrestCreates.Capability;

internal sealed class DefaultCapabilityResolver : ICapabilityResolver
{
    private readonly ICapabilityVersionResolver _versionResolver;

    public DefaultCapabilityResolver(ICapabilityVersionResolver versionResolver)
    {
        _versionResolver = versionResolver;
    }

    public CapabilityDescriptor Resolve(CapabilityRef capabilityRef)
        => _versionResolver.Resolve(capabilityRef);
}
```

- [ ] **Step 7: Write tests for CapabilityResolver**

```csharp
// framework/test/CrestCreates.Capability.Tests/CapabilityResolverTests.cs
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityResolverTests
{
    [Fact]
    public void CapabilityRef_parse_without_version()
    {
        var refObj = CapabilityRef.Parse("customer.create");
        refObj.Id.Should().Be("customer.create");
        refObj.Version.Should().BeNull();
    }

    [Fact]
    public void CapabilityRef_parse_with_version()
    {
        var refObj = CapabilityRef.Parse("customer.create:3");
        refObj.Id.Should().Be("customer.create");
        refObj.Version.Should().Be(3);
    }

    [Fact]
    public void CapabilityRef_ToString()
    {
        new CapabilityRef("customer.create").ToString().Should().Be("customer.create");
        new CapabilityRef("customer.create", 3).ToString().Should().Be("customer.create:3");
    }
}
```

- [ ] **Step 8: Run tests to verify pass**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~CapabilityResolverTests"`
Expected: 3 tests pass.

- [ ] **Step 9: Build and verify**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 10: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/CapabilityRef.cs
git add framework/src/CrestCreates.Capability.Abstractions/ICapabilityResolver.cs
git add framework/src/CrestCreates.Capability.Abstractions/CapabilityNotFoundException.cs
git add framework/src/CrestCreates.Capability/Internal/
git add framework/src/CrestCreates.Capability/DefaultCapabilityResolver.cs
git add framework/test/CrestCreates.Capability.Tests/CapabilityResolverTests.cs
git commit -m "feat: create ICapabilityResolver with CapabilityRef and internal version resolution"
```

---

### Task 7: Create ICapabilityAuditStore + CapabilityExecutionRecord

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionRecord.cs`
- Create: `framework/src/CrestCreates.Capability.Abstractions/ICapabilityAuditStore.cs`
- Create: `framework/src/CrestCreates.Capability/NullCapabilityAuditStore.cs`
- Create: `framework/src/CrestCreates.Capability/InMemoryCapabilityAuditStore.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/InMemoryCapabilityAuditStoreTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/NullCapabilityAuditStoreTests.cs`

- [ ] **Step 1: Create CapabilityExecutionRecord.cs**

```csharp
// framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionRecord.cs
namespace CrestCreates.Capability.Abstractions;

public sealed record CapabilityExecutionRecord
{
    public string ExecutionId { get; init; } = string.Empty;
    public string CapabilityId { get; init; } = string.Empty;
    public string CapabilityName { get; init; } = string.Empty;
    public int CapabilityVersion { get; init; }
    public string? TenantId { get; init; }
    public string? UserId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public InvocationSource Source { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorCode { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

- [ ] **Step 2: Create ICapabilityAuditStore.cs**

```csharp
// framework/src/CrestCreates.Capability.Abstractions/ICapabilityAuditStore.cs
namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityAuditStore
{
    Task RecordAsync(CapabilityExecutionRecord record, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create NullCapabilityAuditStore.cs**

```csharp
// framework/src/CrestCreates.Capability/NullCapabilityAuditStore.cs
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

internal sealed class NullCapabilityAuditStore : ICapabilityAuditStore
{
    public Task RecordAsync(CapabilityExecutionRecord record, CancellationToken ct = default)
        => Task.CompletedTask;
}
```

- [ ] **Step 4: Create InMemoryCapabilityAuditStore.cs**

```csharp
// framework/src/CrestCreates.Capability/InMemoryCapabilityAuditStore.cs
using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class InMemoryCapabilityAuditStore : ICapabilityAuditStore
{
    private readonly ConcurrentQueue<CapabilityExecutionRecord> _records = new();

    public Task RecordAsync(CapabilityExecutionRecord record, CancellationToken ct = default)
    {
        _records.Enqueue(record);
        return Task.CompletedTask;
    }

    public IReadOnlyList<CapabilityExecutionRecord> GetRecords() => _records.ToList();
    public void Clear() => _records.Clear();
}
```

- [ ] **Step 5: Write tests**

```csharp
// framework/test/CrestCreates.Capability.Tests/InMemoryCapabilityAuditStoreTests.cs
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class InMemoryCapabilityAuditStoreTests
{
    [Fact]
    public async Task RecordAsync_stores_record()
    {
        var store = new InMemoryCapabilityAuditStore();
        var record = new CapabilityExecutionRecord
        {
            ExecutionId = "exec_1",
            CapabilityId = "customer.create",
            CapabilityName = "Create Customer",
            CapabilityVersion = 1,
            IsSuccess = true,
            Duration = TimeSpan.FromMilliseconds(50)
        };

        await store.RecordAsync(record);

        var records = store.GetRecords();
        records.Should().HaveCount(1);
        records[0].CapabilityId.Should().Be("customer.create");
    }

    [Fact]
    public async Task Clear_removes_all_records()
    {
        var store = new InMemoryCapabilityAuditStore();
        await store.RecordAsync(new CapabilityExecutionRecord { ExecutionId = "1", CapabilityId = "a" });
        await store.RecordAsync(new CapabilityExecutionRecord { ExecutionId = "2", CapabilityId = "b" });

        store.Clear();

        store.GetRecords().Should().BeEmpty();
    }
}

// framework/test/CrestCreates.Capability.Tests/NullCapabilityAuditStoreTests.cs
public class NullCapabilityAuditStoreTests
{
    [Fact]
    public async Task RecordAsync_does_not_throw()
    {
        var store = new NullCapabilityAuditStore();
        var act = () => store.RecordAsync(new CapabilityExecutionRecord { ExecutionId = "1", CapabilityId = "a" });
        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 6: Run tests to verify pass**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~InMemoryCapabilityAuditStore|NullCapabilityAuditStore"`
Expected: 3 tests pass.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionRecord.cs
git add framework/src/CrestCreates.Capability.Abstractions/ICapabilityAuditStore.cs
git add framework/src/CrestCreates.Capability/NullCapabilityAuditStore.cs
git add framework/src/CrestCreates.Capability/InMemoryCapabilityAuditStore.cs
git add framework/test/CrestCreates.Capability.Tests/InMemoryCapabilityAuditStoreTests.cs
git add framework/test/CrestCreates.Capability.Tests/NullCapabilityAuditStoreTests.cs
git commit -m "feat: create ICapabilityAuditStore with NullCapabilityAuditStore (default) and InMemoryCapabilityAuditStore (dev)"
```

---

### Task 8: Create AuditMiddleware

**Files:**
- Create: `framework/src/CrestCreates.Capability/Middleware/AuditMiddleware.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/AuditMiddlewareTests.cs`

- [ ] **Step 1: Create AuditMiddleware**

```csharp
// framework/src/CrestCreates.Capability/Middleware/AuditMiddleware.cs
using System.Diagnostics;
using CrestCreates.Capability.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Capability.Middleware;

internal sealed class AuditMiddleware : ICapabilityPipelineMiddleware
{
    private readonly ICapabilityAuditStore _auditStore;
    private readonly ILogger<AuditMiddleware> _logger;

    public AuditMiddleware(ICapabilityAuditStore auditStore, ILogger<AuditMiddleware> logger)
    {
        _auditStore = auditStore;
        _logger = logger;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        var executionId = Guid.NewGuid().ToString("N");
        var sw = Stopwatch.StartNew();
        CapabilityExecutionResult? result = null;
        Exception? unhandledException = null;
        bool cancelled = false;

        try
        {
            result = await next(context);
            return result;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            throw;
        }
        catch (Exception ex)
        {
            unhandledException = ex;
            throw;
        }
        finally
        {
            sw.Stop();

            try
            {
                var errorCode = cancelled
                    ? "CANCELLED"
                    : result?.ErrorCode
                      ?? (unhandledException is not null ? "UNHANDLED_EXCEPTION" : null);

                await _auditStore.RecordAsync(new CapabilityExecutionRecord
                {
                    ExecutionId = executionId,
                    CapabilityId = context.CapabilityId,
                    CapabilityName = context.CapabilityName,
                    CapabilityVersion = context.CapabilityVersion,
                    TenantId = context.TenantId,
                    UserId = context.UserId,
                    CorrelationId = context.CorrelationId,
                    Source = context.InvocationSource,
                    IsSuccess = result?.IsSuccess ?? false,
                    ErrorCode = errorCode,
                    Duration = sw.Elapsed,
                    Timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception auditEx)
            {
                _logger.LogError(auditEx, "Failed to record audit for capability '{CapabilityId}'", context.CapabilityId);
            }
        }
    }
}
```

- [ ] **Step 2: Write tests for AuditMiddleware**

```csharp
// framework/test/CrestCreates.Capability.Tests/AuditMiddlewareTests.cs
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class AuditMiddlewareTests
{
    [Fact]
    public async Task Records_successful_execution()
    {
        var store = new InMemoryCapabilityAuditStore();
        var logger = Mock.Of<ILogger<AuditMiddleware>>();
        var middleware = new AuditMiddleware(store, logger);

        var context = new CapabilityExecutionContext
        {
            CapabilityId = "test.ping",
            CapabilityName = "Test Ping",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Http
        };

        var result = await middleware.InvokeAsync(context, async (ctx) =>
        {
            await Task.CompletedTask;
            return CapabilityExecutionResult.Success("ok", TimeSpan.FromMilliseconds(10));
        });

        var records = store.GetRecords();
        records.Should().HaveCount(1);
        records[0].CapabilityId.Should().Be("test.ping");
        records[0].IsSuccess.Should().BeTrue();
        records[0].Source.Should().Be(InvocationSource.Http);
        records[0].ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task Records_failed_execution()
    {
        var store = new InMemoryCapabilityAuditStore();
        var logger = Mock.Of<ILogger<AuditMiddleware>>();
        var middleware = new AuditMiddleware(store, logger);

        var context = new CapabilityExecutionContext
        {
            CapabilityId = "test.ping",
            CapabilityName = "Test Ping",
            CapabilityVersion = 1
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await middleware.InvokeAsync(context, async (ctx) =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("Boom");
            });
        });

        var records = store.GetRecords();
        records.Should().HaveCount(1);
        records[0].IsSuccess.Should().BeFalse();
        records[0].ErrorCode.Should().Be("UNHANDLED_EXCEPTION");
    }

    [Fact]
    public async Task Records_cancelled_execution()
    {
        var store = new InMemoryCapabilityAuditStore();
        var logger = Mock.Of<ILogger<AuditMiddleware>>();
        var middleware = new AuditMiddleware(store, logger);

        var context = new CapabilityExecutionContext
        {
            CapabilityId = "test.ping",
            CapabilityName = "Test Ping",
            CapabilityVersion = 1
        };

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await middleware.InvokeAsync(context, async (ctx) =>
            {
                await Task.CompletedTask;
                throw new OperationCanceledException();
            });
        });

        var records = store.GetRecords();
        records[0].ErrorCode.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task Audit_failure_does_not_break_execution()
    {
        var failingStore = new Mock<ICapabilityAuditStore>();
        failingStore.Setup(s => s.RecordAsync(It.IsAny<CapabilityExecutionRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Audit down"));
        var logger = Mock.Of<ILogger<AuditMiddleware>>();
        var middleware = new AuditMiddleware(failingStore.Object, logger);

        var context = new CapabilityExecutionContext
        {
            CapabilityId = "test.ping",
            CapabilityName = "Test Ping",
            CapabilityVersion = 1
        };

        var result = await middleware.InvokeAsync(context, async (ctx) =>
        {
            await Task.CompletedTask;
            return CapabilityExecutionResult.Success("ok", TimeSpan.FromMilliseconds(10));
        });

        result.IsSuccess.Should().BeTrue();
    }
}
```

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~AuditMiddlewareTests"`
Expected: 4 tests pass.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Capability/Middleware/AuditMiddleware.cs
git add framework/test/CrestCreates.Capability.Tests/AuditMiddlewareTests.cs
git commit -m "feat: create AuditMiddleware with try/finally, cancellation support, and failure isolation"
```

---

### Task 9: Update Pipeline Middleware Order

**Files:**
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Update middleware order in AddCapabilityPipeline**

Read `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`. Update `AddCapabilityPipeline` to insert `AuditMiddleware` as the outermost middleware (first in the Use chain):

```csharp
public static IServiceCollection AddCapabilityPipeline(
    this IServiceCollection services,
    Action<CapabilityPipelineBuilder>? configure = null)
{
    var builder = new CapabilityPipelineBuilder();

    builder.Use<AuditMiddleware>();           // Outermost — records all outcomes
    builder.Use<RateLimitMiddleware>();
    builder.Use<TenantMiddleware>();
    builder.Use<AuthorizationMiddleware>();
    builder.Use<ValidationMiddleware>();
    builder.Use<IdempotencyMiddleware>();
    builder.Use<MetricsMiddleware>();          // Wraps Handler
    builder.Use<EventPublishingMiddleware>();

    configure?.Invoke(builder);

    services.TryAddSingleton(builder);
    services.TryAddSingleton<CapabilityHandlerResolver>();
    services.TryAddSingleton<ICapabilityHandlerResolver>(sp => sp.GetRequiredService<CapabilityHandlerResolver>());
    services.TryAddSingleton<ICapabilityPipeline, CapabilityPipeline>();
    services.TryAddTransient<AuditMiddleware>();       // New
    services.TryAddTransient<RateLimitMiddleware>();
    services.TryAddTransient<TenantMiddleware>();
    services.TryAddTransient<AuthorizationMiddleware>();
    services.TryAddTransient<ValidationMiddleware>();
    services.TryAddTransient<IdempotencyMiddleware>();
    services.TryAddTransient<MetricsMiddleware>();
    services.TryAddTransient<EventPublishingMiddleware>();

    return services;
}
```

Update the `using` declarations at the top:
```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs
git commit -m "feat: update middleware order — AuditMiddleware outermost, MetricsMiddleware wraps Handler"
```

---

### Task 10: Create ICapabilityDispatcher + CapabilityDispatcher

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/ICapabilityDispatcher.cs`
- Create: `framework/src/CrestCreates.Capability/CapabilityDispatcher.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityDispatcherTests.cs`

- [ ] **Step 1: Create ICapabilityDispatcher.cs**

```csharp
// framework/src/CrestCreates.Capability.Abstractions/ICapabilityDispatcher.cs
using CrestCreates.Metadata;

namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Facade layer over ICapabilityPipeline. The unified entry point for all capability execution.
/// </summary>
public interface ICapabilityDispatcher
{
    Task<CapabilityExecutionResult> DispatchAsync(
        CapabilityDescriptor descriptor,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);

    Task<CapabilityExecutionResult> DispatchAsync(
        string capabilityId,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Write tests for Dispatcher (will fail until implementation exists)**

```csharp
// framework/test/CrestCreates.Capability.Tests/CapabilityDispatcherTests.cs
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityDispatcherTests
{
    [Fact]
    public async Task Ddispatch_by_descriptor_injects_context()
    {
        var pipeline = new Mock<ICapabilityPipeline>();
        var resolver = new Mock<ICapabilityResolver>();
        var tenantContext = Mock.Of<ITenantContext>();
        var currentUser = Mock.Of<ICurrentUser>();

        CapabilityExecutionContext? capturedContext = null;
        pipeline.Setup(p => p.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?, Action<CapabilityExecutionContext>?, CancellationToken>(
                (name, input, configure, ct) =>
                {
                    var ctx = new CapabilityExecutionContext();
                    configure?.Invoke(ctx);
                    capturedContext = ctx;
                })
            .ReturnsAsync(CapabilityExecutionResult.Success("ok", TimeSpan.Zero));

        var dispatcher = new CapabilityDispatcher(resolver.Object, pipeline.Object, tenantContext, currentUser);
        var descriptor = new CapabilityDescriptor { Id = "customer.create", Name = "Create Customer", Version = 1 };

        await dispatcher.DispatchAsync(descriptor, InvocationSource.Workflow, new { Name = "John" });

        capturedContext.Should().NotBeNull();
        capturedContext!.CapabilityId.Should().Be("customer.create");
        capturedContext.CapabilityName.Should().Be("Create Customer");
        capturedContext.CapabilityVersion.Should().Be(1);
        capturedContext.InvocationSource.Should().Be(InvocationSource.Workflow);
    }

    [Fact]
    public async Task Dispatch_by_string_resolves_then_call_delegate()
    {
        var pipeline = new Mock<ICapabilityPipeline>();
        pipeline.Setup(p => p.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<Action<CapabilityExecutionContext>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityExecutionResult.Success("ok", TimeSpan.Zero));

        var descriptor = new CapabilityDescriptor { Id = "customer.create", Name = "Create Customer", Version = 1 };
        var resolver = new Mock<ICapabilityResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<string>())).Returns(descriptor);

        var tenantContext = Mock.Of<ITenantContext>();
        var currentUser = Mock.Of<ICurrentUser>();
        var dispatcher = new CapabilityDispatcher(resolver.Object, pipeline.Object, tenantContext, currentUser);

        var result = await dispatcher.DispatchAsync("customer.create", InvocationSource.Http);

        result.IsSuccess.Should().BeTrue();
        resolver.Verify(r => r.Resolve("customer.create"), Times.Once);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~CapabilityDispatcherTests"`
Expected: Build error — CapabilityDispatcher doesn't exist.

- [ ] **Step 4: Create CapabilityDispatcher.cs**

```csharp
// framework/src/CrestCreates.Capability/CapabilityDispatcher.cs
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;

namespace CrestCreates.Capability;

internal sealed class CapabilityDispatcher : ICapabilityDispatcher
{
    private readonly ICapabilityResolver _resolver;
    private readonly ICapabilityPipeline _pipeline;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public CapabilityDispatcher(
        ICapabilityResolver resolver,
        ICapabilityPipeline pipeline,
        ITenantContext tenantContext,
        ICurrentUser currentUser)
    {
        _resolver = resolver;
        _pipeline = pipeline;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<CapabilityExecutionResult> DispatchAsync(
        CapabilityDescriptor descriptor,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(descriptor.Id, input, ctx =>
        {
            ctx.CapabilityId = descriptor.Id;
            ctx.CapabilityName = descriptor.Name;
            ctx.CapabilityVersion = descriptor.Version;
            ctx.CapabilityContractHash = descriptor.ContractHash;
            ctx.InvocationSource = source;
            ctx.TenantId = _tenantContext.TenantId;
            ctx.UserId = _currentUser.UserId;
            configureContext?.Invoke(ctx);
        }, ct);
    }

    public async Task<CapabilityExecutionResult> DispatchAsync(
        string capabilityId,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        var descriptor = _resolver.Resolve(capabilityId);
        return await DispatchAsync(descriptor, source, input, configureContext, ct);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~CapabilityDispatcherTests"`
Expected: 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/ICapabilityDispatcher.cs
git add framework/src/CrestCreates.Capability/CapabilityDispatcher.cs
git add framework/test/CrestCreates.Capability.Tests/CapabilityDispatcherTests.cs
git commit -m "feat: create ICapabilityDispatcher with dual overload (descriptor + string)"
```

---

### Task 11: Create IBootstrapValidator + IDescriptorLookup + ICapabilityHandlerRegistry

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IBootstrapValidator.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorLookup.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/ICapabilityHandlerRegistry.cs`

- [ ] **Step 1: Create IBootstrapValidator.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IBootstrapValidator.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Cross-registry bootstrap validation.
/// Unlike IRegistryValidator{T} (single-registry internal validation),
/// this validates relationships across multiple registries.
/// Phase 6 Graph Engine will extend this interface.
/// </summary>
public interface IBootstrapValidator
{
    int Order { get; }
    ValidationReport Validate();
}
```

- [ ] **Step 2: Create IDescriptorLookup.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IDescriptorLookup.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Read-only query interface for bootstrap phase.
/// Implemented by registries or constructed by BootstrapCoordinator.
/// </summary>
public interface IDescriptorLookup
{
    bool Exists(DescriptorRef descriptorRef);
}
```

- [ ] **Step 3: Create ICapabilityHandlerRegistry.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/ICapabilityHandlerRegistry.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Source Generator implements this interface, providing a static mapping
/// of capability id → handler type.
/// key = CapabilityId (stable identifier), not Name (display name).
/// </summary>
public interface ICapabilityHandlerRegistry
{
    IReadOnlyDictionary<string, Type> GetHandlerMappings();
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/IBootstrapValidator.cs
git add framework/src/CrestCreates.Metadata.Abstractions/IDescriptorLookup.cs
git add framework/src/CrestCreates.Metadata.Abstractions/ICapabilityHandlerRegistry.cs
git commit -m "feat: create IBootstrapValidator, IDescriptorLookup, and ICapabilityHandlerRegistry interfaces"
```

---

### Task 12: Create Bootstrap Validators

**Files:**
- Create: `framework/src/CrestCreates.Capability/Bootstrap/CapabilityHandlerValidator.cs`
- Create: `framework/src/CrestCreates.Capability/Bootstrap/CapabilitySchemaValidator.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityHandlerValidatorTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilitySchemaValidatorTests.cs`

- [ ] **Step 1: Create CapabilityHandlerValidator**

```csharp
// framework/src/CrestCreates.Capability/Bootstrap/CapabilityHandlerValidator.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Bootstrap;

public sealed class CapabilityHandlerValidator : IBootstrapValidator
{
    private readonly CapabilityRegistry _capabilityRegistry;
    private readonly ICapabilityHandlerRegistry _handlerRegistry;

    public CapabilityHandlerValidator(
        CapabilityRegistry capabilityRegistry,
        ICapabilityHandlerRegistry handlerRegistry)
    {
        _capabilityRegistry = capabilityRegistry;
        _handlerRegistry = handlerRegistry;
    }

    public int Order => 100;

    public ValidationReport Validate()
    {
        var issues = new List<ValidationIssue>();
        var descriptors = _capabilityRegistry.GetAll();
        var mappings = _handlerRegistry.GetHandlerMappings();

        foreach (var descriptor in descriptors)
        {
            if (!mappings.ContainsKey(descriptor.Id))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"Capability '{descriptor.Id}' (Name: '{descriptor.Name}') has no registered handler. " +
                    $"Add [GenerateCapabilityHandler] or register manually."));
            }
        }

        return ValidationReport.FromIssues(issues.ToArray());
    }
}
```

- [ ] **Step 2: Create CapabilitySchemaValidator**

```csharp
// framework/src/CrestCreates.Capability/Bootstrap/CapabilitySchemaValidator.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Bootstrap;

public sealed class CapabilitySchemaValidator : IBootstrapValidator
{
    private readonly CapabilityRegistry _capabilityRegistry;
    private readonly IDescriptorLookup _descriptorLookup;

    public CapabilitySchemaValidator(
        CapabilityRegistry capabilityRegistry,
        IDescriptorLookup descriptorLookup)
    {
        _capabilityRegistry = capabilityRegistry;
        _descriptorLookup = descriptorLookup;
    }

    public int Order => 200;

    public ValidationReport Validate()
    {
        var issues = new List<ValidationIssue>();
        var descriptors = _capabilityRegistry.GetAll();

        foreach (var descriptor in descriptors)
        {
            if (descriptor.InputSchema.HasValue)
            {
                var schemaRef = descriptor.InputSchema.Value;
                var refObj = new DescriptorRef(schemaRef.Id, schemaRef.Id, schemaRef.Version);
                if (!_descriptorLookup.Exists(refObj))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"Capability '{descriptor.Id}' references InputSchema '{schemaRef.Id}' (v{schemaRef.Version}) which does not exist."));
                }
            }

            if (descriptor.OutputSchema.HasValue)
            {
                var schemaRef = descriptor.OutputSchema.Value;
                var refObj = new DescriptorRef(schemaRef.Id, schemaRef.Id, schemaRef.Version);
                if (!_descriptorLookup.Exists(refObj))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"Capability '{descriptor.Id}' references OutputSchema '{schemaRef.Id}' (v{schemaRef.Version}) which does not exist."));
                }
            }
        }

        return ValidationReport.FromIssues(issues.ToArray());
    }
}
```

- [ ] **Step 3: Write tests**

```csharp
// framework/test/CrestCreates.Capability.Tests/CapabilityHandlerValidatorTests.cs
using System.Collections.Frozen;
using CrestCreates.Capability.Bootstrap;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityHandlerValidatorTests
{
    [Fact]
    public void Detects_missing_handler()
    {
        var descriptors = new[] { new CapabilityDescriptor { Id = "noop.missing", Name = "noop.missing", Version = 1 } };
        var validationEngine = new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>());
        var registry = new CapabilityRegistry(validationEngine);
        registry.Build(new[] { new TestDescriptorProvider(descriptors) });

        var handlerRegistry = new Mock<ICapabilityHandlerRegistry>();
        handlerRegistry.Setup(h => h.GetHandlerMappings()).Returns(FrozenDictionary<string, Type>.Empty);

        var validator = new CapabilityHandlerValidator(registry, handlerRegistry.Object);
        var report = validator.Validate();

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Severity == ValidationSeverity.Error
            && i.Message.Contains("noop.missing"));
    }

    [Fact]
    public void Passes_when_handler_exists()
    {
        var descriptors = new[] { new CapabilityDescriptor { Id = "test.ping", Name = "test.ping", Version = 1 } };
        var validationEngine = new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>());
        var registry = new CapabilityRegistry(validationEngine);
        registry.Build(new[] { new TestDescriptorProvider(descriptors) });

        var handlerRegistry = new Mock<ICapabilityHandlerRegistry>();
        handlerRegistry.Setup(h => h.GetHandlerMappings())
            .Returns(new Dictionary<string, Type> { ["test.ping"] = typeof(object) }.ToFrozenDictionary());

        var validator = new CapabilityHandlerValidator(registry, handlerRegistry.Object);
        var report = validator.Validate();

        report.HasErrors.Should().BeFalse();
    }
}
```

```csharp
// framework/test/CrestCreates.Capability.Tests/CapabilitySchemaValidatorTests.cs
using CrestCreates.Capability.Bootstrap;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilitySchemaValidatorTests
{
    [Fact]
    public void Detects_missing_input_schema_ref()
    {
        var descriptors = new[]
        {
            new CapabilityDescriptor
            {
                Id = "cmd.broken",
                Name = "cmd.broken",
                Version = 1,
                InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_missing", 1)
            }
        };
        var validationEngine = new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>());
        var registry = new CapabilityRegistry(validationEngine);
        registry.Build(new[] { new TestDescriptorProvider(descriptors) });

        var lookup = new Mock<IDescriptorLookup>();
        lookup.Setup(l => l.Exists(It.IsAny<DescriptorRef>())).Returns(false);

        var validator = new CapabilitySchemaValidator(registry, lookup.Object);
        var report = validator.Validate();

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("schema_missing"));
    }

    [Fact]
    public void Passes_when_schema_refs_exist()
    {
        var descriptors = new[]
        {
            new CapabilityDescriptor
            {
                Id = "cmd.ok",
                Name = "cmd.ok",
                Version = 1,
                InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer", 1)
            }
        };
        var validationEngine = new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>());
        var registry = new CapabilityRegistry(validationEngine);
        registry.Build(new[] { new TestDescriptorProvider(descriptors) });

        var lookup = new Mock<IDescriptorLookup>();
        lookup.Setup(l => l.Exists(It.IsAny<DescriptorRef>())).Returns(true);

        var validator = new CapabilitySchemaValidator(registry, lookup.Object);
        var report = validator.Validate();

        report.HasErrors.Should().BeFalse();
    }
}
```

- [ ] **Step 4: Run tests to verify pass**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~CapabilityHandlerValidator|CapabilitySchemaValidator"`
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Capability/Bootstrap/
git add framework/test/CrestCreates.Capability.Tests/CapabilityHandlerValidatorTests.cs
git add framework/test/CrestCreates.Capability.Tests/CapabilitySchemaValidatorTests.cs
git commit -m "feat: create CapabilityHandlerValidator and CapabilitySchemaValidator as IBootstrapValidator implementations"
```

---

### Task 13: Wire Up DI — AddCapabilityRuntime

**Files:**
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Add AddCapabilityRuntime extension method**

Add to `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`:

```csharp
public static IServiceCollection AddCapabilityRuntime(
    this IServiceCollection services)
{
    // Existing
    services.AddCapabilityPipeline();
    services.AddCapabilityHandlerInvoker();

    // New — Dispatcher + Resolver
    services.TryAddSingleton<ICapabilityDispatcher, CapabilityDispatcher>();
    services.TryAddSingleton<ICapabilityResolver, DefaultCapabilityResolver>();

    // Internal
    services.TryAddSingleton<ICapabilityVersionResolver, DefaultCapabilityVersionResolver>();

    // Audit — default NoOp
    services.TryAddSingleton<ICapabilityAuditStore, NullCapabilityAuditStore>();

    // Bootstrap Validators
    services.AddSingleton<IBootstrapValidator, CapabilityHandlerValidator>();
    services.AddSingleton<IBootstrapValidator, CapabilitySchemaValidator>();

    return services;
}

public static IServiceCollection AddInMemoryCapabilityAudit(this IServiceCollection services)
{
    services.Replace(ServiceDescriptor.Singleton<ICapabilityAuditStore, InMemoryCapabilityAuditStore>());
    return services;
}
```

Also add missing `using` directives if needed: `using CrestCreates.Capability.Bootstrap;`, `using CrestCreates.Capability.Internal;`, `using Microsoft.Extensions.DependencyInjection.Extensions;`.

- [ ] **Step 2: Build and verify**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs
git commit -m "feat: add AddCapabilityRuntime and AddInMemoryCapabilityAudit DI extensions"
```

---

### Task 14: Final Integration — Fix All Tests

**Files:**
- Modify: All test files and source files that broke due to type changes

- [ ] **Step 1: Run all tests**

Run: `dotnet test`
Expected: Most tests pass, some may fail due to type reference changes.

- [ ] **Step 2: Fix remaining compilation and test failures**

Identify each failing test or compilation error. Common issues:
- Old `CrestCreates.Capability.Abstractions.CapabilityDescriptor` references → update to `CrestCreates.Metadata.CapabilityDescriptor`
- `Aliases` property references → remove (Aliases is now a routing concern)
- Old `CapabilityRegistry` references → update to the unified RegistryBase version
- `CapabilityKind` / `CapabilityRiskLevel` namespace → now `CrestCreates.Metadata.Abstractions`

Fix each issue systematically. Do NOT skip any failing test.

- [ ] **Step 3: Verify all tests pass**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add -u
git commit -m "fix: update all references to use unified CapabilityDescriptor and CapabilityRegistry"
```

---

### Task 15: Integration Tests

**Files:**
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityRuntimeIntegrationTests.cs`

- [ ] **Step 1: Write end-to-end integration test**

```csharp
// framework/test/CrestCreates.Capability.Tests/CapabilityRuntimeIntegrationTests.cs
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityRuntimeIntegrationTests
{
    [Fact]
    public async Task End_to_end_dispatch_executes_handler_and_records_audit()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.AddCapabilityRuntime();
        services.AddInMemoryCapabilityAudit();

        // Register a handler
        services.AddHandlerInvoker("test.ping", (input, ct) =>
            Task.FromResult<object?>("pong"));

        // Register the descriptor via a provider
        var provider = new TestDescriptorProvider(new[]
        {
            new CapabilityDescriptor
            {
                Id = "test.ping",
                Name = "Test Ping",
                Version = 1,
                CapabilityKind = CapabilityKind.Query
            }
        });

        var sp = services.BuildServiceProvider();

        // Build the registry
        var registry = (CapabilityRegistry)sp.GetRequiredService<ICapabilityRegistry>();
        registry.Build(new[] { provider });

        var dispatcher = sp.GetRequiredService<ICapabilityDispatcher>();
        var result = await dispatcher.DispatchAsync("test.ping", InvocationSource.Http, new { Message = "hello" });

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("pong");

        // Verify audit
        var audit = (InMemoryCapabilityAuditStore)sp.GetRequiredService<ICapabilityAuditStore>();
        var records = audit.GetRecords();
        records.Should().HaveCount(1);
        records[0].CapabilityId.Should().Be("test.ping");
        records[0].Source.Should().Be(InvocationSource.Http);
    }
}
```

- [ ] **Step 2: Run integration test**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~End_to_end_dispatch" --verbosity normal`
Expected: If `AddCapabilityHandlerInvoker` doesn't exist as a standalone extension yet, update `AddCapabilityRuntime` to call it or add the registration directly. Verify end-to-end flow works.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Capability.Tests/CapabilityRuntimeIntegrationTests.cs
git commit -m "test: add end-to-end integration test for CapabilityRuntime"
```

---

### Task 16: Full Test Suite Verification

- [ ] **Step 1: Run entire test suite**

Run: `dotnet test`
Expected: All tests pass across all projects.

- [ ] **Step 2: Run Metadata and Capability tests specifically**

Run:
```bash
dotnet test framework/test/CrestCreates.Metadata.Tests
dotnet test framework/test/CrestCreates.Capability.Tests
```

Expected: Both pass.

- [ ] **Step 3: Build in Release mode**

Run: `dotnet build -c Release`
Expected: No warnings or errors.

- [ ] **Step 4: Commit**

```bash
git commit -m "chore: verify full test suite passes after Phase 4 consolidation" --allow-empty
```