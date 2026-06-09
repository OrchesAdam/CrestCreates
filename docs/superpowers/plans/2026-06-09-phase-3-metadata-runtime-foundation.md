# Phase 3: Metadata Runtime Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract EventRegistry's pattern into RegistryBase<T>, establishing the Crest Metadata Kernel that all future registries (Event, Capability, Workflow, HumanTask, Form) share.

**Architecture:** RegistryBase<T> is the generic base class (FrozenDictionary snapshot, validation pipeline, bootstrap coordination). EventRegistry migrates internally to RegistryBase while keeping its public API unchanged. CapabilityRegistry is the first new registry built on RegistryBase. All descriptor identity flows through Namespace + Id = Global Identity.

**Tech Stack:** .NET 10, FrozenDictionary, ImmutableArray, IIncrementalGenerator (Roslyn), xUnit, FluentAssertions

**Design Spec:** `docs/superpowers/specs/2026-06-09-phase-3-metadata-runtime-foundation-design.md`

**Existing Code Reference:**
- `framework/src/CrestCreates.Metadata.Abstractions/` — existing IDescriptor, IVersionedDescriptor, DescriptorKind, DescriptorState, etc.
- `framework/src/CrestCreates.Event/EventRegistry.cs` — the registry to migrate
- `framework/src/CrestCreates.Event.Abstractions/GeneratedEventDescriptor.cs` — descriptor to adapt
- `framework/src/CrestCreates.Capability/CapabilityRegistry.cs` — existing runtime registry (to be replaced)

---

## File Map

### New Files

| File | Purpose |
|------|---------|
| `framework/src/CrestCreates.Metadata.Abstractions/IHasContractIdentity.cs` | ContractHash + DefinitionHash interface |
| `framework/src/CrestCreates.Metadata.Abstractions/IRelationshipAwareDescriptor.cs` | Self-describing relationships |
| `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRelationship.cs` | Relationship data model |
| `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRef.cs` | Logical ref interface (Id + Version?) |
| `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRef.cs` (new version) | Concrete ref struct |
| `framework/src/CrestCreates.Metadata.Abstractions/DescriptorKey.cs` | Physical lookup key (Id + Version) |
| `framework/src/CrestCreates.Metadata.Abstractions/ValidationIssue.cs` | Validation severity + message |
| `framework/src/CrestCreates.Metadata.Abstractions/ValidationReport.cs` | Batch validation result |
| `framework/src/CrestCreates.Metadata.Abstractions/IRegistryValidator.cs` | Pluggable validator interface |
| `framework/src/CrestCreates.Metadata.Abstractions/IRegistryValidationEngine.cs` | Validation coordinator |
| `framework/src/CrestCreates.Metadata.Abstractions/IRegistryIndex.cs` | Strong-typed index base |
| `framework/src/CrestCreates.Metadata.Abstractions/IRegistryIndexBuilder.cs` | Index builder interface |
| `framework/src/CrestCreates.Metadata.Abstractions/IRegistrySnapshot.cs` | Snapshot interface |
| `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorProvider.cs` | Provider interface for RegistryBase |
| `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorResolver.cs` | Unified resolver |
| `framework/src/CrestCreates.Metadata.Abstractions/DescriptorQuery.cs` | Query conditions (Phase 3 placeholder) |
| `framework/src/CrestCreates.Metadata.Abstractions/IBootstrapTask.cs` | Bootstrap task interface |
| `framework/src/CrestCreates.Metadata.Abstractions/BootstrapDependencyException.cs` | Cycle detection error |
| `framework/src/CrestCreates.Metadata.Abstractions/IDynamicRegistry.cs` | Dynamic registration hook |
| `framework/src/CrestCreates.Metadata/RegistryBase.cs` | Generic registry base class |
| `framework/src/CrestCreates.Metadata/RegistrySnapshot.cs` | Frozen snapshot implementation |
| `framework/src/CrestCreates.Metadata/RegistryValidationEngine.cs` | Default validation engine |
| `framework/src/CrestCreates.Metadata/DescriptorResolver.cs` | Default resolver implementation |
| `framework/src/CrestCreates.Metadata/BootstrapCoordinator.cs` | Topological sort + startup |
| `framework/src/CrestCreates.Metadata/CapabilityDescriptor.cs` | Capability descriptor |
| `framework/src/CrestCreates.Metadata/CapabilityRegistry.cs` | Capability registry on RegistryBase |
| `framework/src/CrestCreates.Metadata/EventVersionChainValidator.cs` | Extracted from EventRegistry |
| `framework/src/CrestCreates.Metadata/DuplicateNameVersionValidator.cs` | Extracted from EventRegistry |
| `framework/src/CrestCreates.Metadata/UniquePayloadTypeValidator.cs` | Extracted from EventRegistry |
| `framework/test/CrestCreates.Metadata.Tests/RegistryBaseTests.cs` | RegistryBase unit tests |
| `framework/test/CrestCreates.Metadata.Tests/ValidationPipelineTests.cs` | Validation pipeline tests |
| `framework/test/CrestCreates.Metadata.Tests/BootstrapCoordinatorTests.cs` | Bootstrap tests |
| `framework/test/CrestCreates.Metadata.Tests/CapabilityRegistryTests.cs` | CapabilityRegistry tests |
| `framework/test/CrestCreates.Metadata.Tests/DescriptorResolverTests.cs` | Resolver tests |

### Modified Files

| File | Change |
|------|--------|
| `framework/src/CrestCreates.Metadata.Abstractions/IDescriptor.cs` | Add `Namespace` property |
| `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRef.cs` | Replace with new DescriptorRef struct |
| `framework/src/CrestCreates.Event.Abstractions/GeneratedEventDescriptor.cs` | Add `Namespace`, implement `IHasContractIdentity` |
| `framework/src/CrestCreates.Event.Abstractions/DynamicEventDescriptor.cs` | Add `Namespace` |
| `framework/src/CrestCreates.Event.Abstractions/IEventRegistry.cs` | Keep unchanged (internal migration only) |
| `framework/src/CrestCreates.Event/EventRegistry.cs` | Migrate to inherit RegistryBase |
| `framework/src/CrestCreates.Event/EventRegistrySnapshot.cs` | Replace with RegistrySnapshot |
| `framework/src/CrestCreates.Event/EventRegistryBootstrapper.cs` | Implement IBootstrapTask |
| `framework/src/CrestCreates.Event/RegistryEventValidator.cs` | Refactor to IRegistryValidator |
| `framework/src/CrestCreates.Capability/SystemEventDescriptors.cs` | Add Namespace to descriptors |
| `framework/src/CrestCreates.Capability/CapabilityRegistryProvider.cs` | Remove (replaced by provider pattern) |

---

## Task 1: Extend IDescriptor with Namespace

**Files:**
- Modify: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptor.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IHasContractIdentity.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IRelationshipAwareDescriptor.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRelationship.cs`

- [ ] **Step 1: Write tests for IDescriptor.Namespace and FullId**

```csharp
// framework/test/CrestCreates.Metadata.Tests/DescriptorIdentityTests.cs
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorIdentityTests
{
    private class TestDescriptor : IDescriptor
    {
        public string Namespace { get; init; } = "event";
        public string Id { get; init; } = "user.created";
        public string Name { get; init; } = "UserCreated";
    }

    [Fact]
    public void FullId_combines_Namespace_and_Id()
    {
        var descriptor = new TestDescriptor { Namespace = "event", Id = "user.created" };
        descriptor.FullId.Should().Be("event.user.created");
    }

    [Fact]
    public void FullId_uses_default_interface_implementation()
    {
        IDescriptor descriptor = new TestDescriptor { Namespace = "capability", Id = "approval" };
        descriptor.FullId.Should().Be("capability.approval");
    }

    [Fact]
    public void IHasContractIdentity_provides_hashes()
    {
        var descriptor = new TestContractDescriptor();
        descriptor.ContractHash.Should().Be("abc123");
        descriptor.DefinitionHash.Should().Be("def456");
    }

    [Fact]
    public void IRelationshipAwareDescriptor_returns_relationships()
    {
        var descriptor = new TestRelationshipDescriptor();
        var rels = descriptor.GetRelationships().ToList();
        rels.Should().HaveCount(1);
        rels[0].Kind.Should().Be(RelationshipKind.Produces);
    }

    private class TestContractDescriptor : IDescriptor, IHasContractIdentity
    {
        public string Namespace => "event";
        public string Id => "test";
        public string Name => "Test";
        public string ContractHash => "abc123";
        public string DefinitionHash => "def456";
    }

    private class TestRelationshipDescriptor : IDescriptor, IRelationshipAwareDescriptor
    {
        public string Namespace => "capability";
        public string Id => "test";
        public string Name => "Test";

        public IEnumerable<DescriptorRelationship> GetRelationships()
        {
            yield return new DescriptorRelationship(
                new DescriptorRef("capability", "test"),
                new DescriptorRef("event", "user.created"),
                RelationshipKind.Produces);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "DescriptorIdentityTests" -v`
Expected: FAIL — `Namespace` not found on `IDescriptor`

- [ ] **Step 3: Add Namespace to IDescriptor**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IDescriptor.cs
namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptor
{
    /// <summary>
    /// Registry domain. Examples: "event", "capability", "workflow"
    /// </summary>
    string Namespace { get; }

    /// <summary>
    /// Domain-local identity. Examples: "user.created", "approval.completed"
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Global identity. Computed: {Namespace}.{Id}
    /// </summary>
    string FullId => $"{Namespace}.{Id}";

    string Name { get; }
}
```

- [ ] **Step 4: Create IHasContractIdentity**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IHasContractIdentity.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Descriptors with compatibility and implementation identity hashes.
/// Used for version compatibility checks, topology analysis, AI reasoning.
/// </summary>
public interface IHasContractIdentity
{
    string ContractHash { get; }
    string DefinitionHash { get; }
}
```

- [ ] **Step 5: Create DescriptorRelationship and IRelationshipAwareDescriptor**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorRelationship.cs
namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorRelationship(
    DescriptorRef From,
    DescriptorRef To,
    RelationshipKind Kind);

public enum RelationshipKind
{
    Produces,
    Consumes,
    DependsOn,
    References
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IRelationshipAwareDescriptor.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Descriptors that self-describe their relationships.
/// Topology Engine can consume these directly without a separate provider.
/// </summary>
public interface IRelationshipAwareDescriptor
{
    IEnumerable<DescriptorRelationship> GetRelationships();
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "DescriptorIdentityTests" -v`
Expected: PASS (4 tests)

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/ framework/test/CrestCreates.Metadata.Tests/
git commit -m "feat(metadata): add Namespace to IDescriptor, add IHasContractIdentity and IRelationshipAwareDescriptor"
```

---

## Task 2: Create DescriptorRef, DescriptorKey, and Validation Types

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRef.cs`
- Replace: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRef.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorKey.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/ValidationIssue.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/ValidationReport.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IRegistryValidator.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IRegistryValidationEngine.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IRegistryIndex.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IRegistryIndexBuilder.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorProvider.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorResolver.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorQuery.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDynamicRegistry.cs`

- [ ] **Step 1: Write tests for DescriptorRef and DescriptorKey**

```csharp
// framework/test/CrestCreates.Metadata.Tests/DescriptorRefTests.cs
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorRefTests
{
    [Fact]
    public void DescriptorRef_with_version_creates_correctly()
    {
        var r = new DescriptorRef("event", "user.created", 2);
        r.Namespace.Should().Be("event");
        r.Id.Should().Be("user.created");
        r.Version.Should().Be(2);
    }

    [Fact]
    public void DescriptorRef_null_version_means_latest()
    {
        var r = new DescriptorRef("event", "user.created");
        r.Version.Should().BeNull();
    }

    [Fact]
    public void DescriptorRef_FullId_combines_namespace_and_id()
    {
        var r = new DescriptorRef("capability", "approval");
        r.FullId.Should().Be("capability.approval");
    }

    [Fact]
    public void DescriptorKey_requires_version()
    {
        var k = new DescriptorKey("event", "user.created", 1);
        k.Namespace.Should().Be("event");
        k.Id.Should().Be("user.created");
        k.Version.Should().Be(1);
    }

    [Fact]
    public void DescriptorRef_is_IDescriptorRef()
    {
        IDescriptorRef r = new DescriptorRef("event", "test", 1);
        r.Id.Should().Be("test");
        r.Version.Should().Be(1);
    }

    [Fact]
    public void ValidationReport_aggregates_issues()
    {
        var report = ValidationReport.FromIssues(
            new ValidationIssue(ValidationSeverity.Error, "Duplicate name"),
            new ValidationIssue(ValidationSeverity.Warning, "Missing description"));

        report.HasErrors.Should().BeTrue();
        report.HasWarnings.Should().BeTrue();
        report.Issues.Should().HaveCount(2);
    }

    [Fact]
    public void ValidationReport_empty_has_no_errors()
    {
        ValidationReport.Empty.HasErrors.Should().BeFalse();
        ValidationReport.Empty.HasWarnings.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "DescriptorRefTests" -v`
Expected: FAIL — types not defined

- [ ] **Step 3: Create all types**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRef.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Logical reference to a descriptor. Version = null means Latest Stable.
/// All strong-typed refs (EventRef, CapabilityRef, WorkflowRef) implement this.
/// </summary>
public interface IDescriptorRef
{
    string Namespace { get; }
    string Id { get; }
    int? Version { get; }
    string FullId => $"{Namespace}.{Id}";
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorRef.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Concrete descriptor reference. Logical ref — Version is optional.
/// </summary>
public readonly record struct DescriptorRef(
    string Namespace,
    string Id,
    int? Version = null) : IDescriptorRef;
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorKey.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Physical lookup key. Version is required — used for exact-version queries.
/// </summary>
public readonly record struct DescriptorKey(
    string Namespace,
    string Id,
    int Version);
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/ValidationIssue.cs
namespace CrestCreates.Metadata.Abstractions;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message);
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/ValidationReport.cs
namespace CrestCreates.Metadata.Abstractions;

public sealed record ValidationReport(
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);
    public bool HasWarnings => Issues.Any(i => i.Severity == ValidationSeverity.Warning);

    public static ValidationReport Empty => new(Array.Empty<ValidationIssue>());

    public static ValidationReport FromIssues(params ValidationIssue[] issues) => new(issues);
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IRegistryValidator.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Pluggable validator for a specific descriptor type.
/// Each registry can mount different validator combinations.
/// </summary>
public interface IRegistryValidator<TDescriptor>
    where TDescriptor : IDescriptor
{
    int Order { get; }
    ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors);
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IRegistryValidationEngine.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Validation engine that coordinates validators and collects all issues.
/// Decoupled from RegistryBase — reusable by CLI, AI Explorer, etc.
/// </summary>
public interface IRegistryValidationEngine<TDescriptor>
    where TDescriptor : IDescriptor
{
    ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors);
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IRegistryIndex.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Base interface for strong-typed registry indexes.
/// </summary>
public interface IRegistryIndex
{
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IRegistryIndexBuilder.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Builds a strong-typed index during RegistrySnapshot construction.
/// </summary>
public interface IRegistryIndexBuilder<TDescriptor, TIndex>
    where TDescriptor : IDescriptor
    where TIndex : IRegistryIndex
{
    TIndex BuildIndex(IReadOnlyList<TDescriptor> descriptors);
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IDescriptorProvider.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Provides descriptors to RegistryBase.Build().
/// Source generators emit implementations of this interface.
/// </summary>
public interface IDescriptorProvider<TDescriptor>
    where TDescriptor : IDescriptor
{
    IReadOnlyList<TDescriptor> GetDescriptors();
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IDescriptorResolver.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Unified descriptor resolver. Avoids injecting multiple registries.
/// </summary>
public interface IDescriptorResolver
{
    /// <summary>
    /// Runtime Query — returns latest version by domain-local Id.
    /// </summary>
    TDescriptor? Resolve<TDescriptor>(string id)
        where TDescriptor : IDescriptor;

    /// <summary>
    /// Metadata Authoring — returns specific version from ref.
    /// </summary>
    TDescriptor? Resolve<TDescriptor>(IDescriptorRef reference)
        where TDescriptor : IDescriptor;

    /// <summary>
    /// Advanced query — Phase 3 placeholder, Phase 5~7 implementation.
    /// </summary>
    IReadOnlyList<TDescriptor> Query<TDescriptor>(DescriptorQuery query)
        where TDescriptor : IDescriptor;
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorQuery.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Query conditions for descriptor resolution. Phase 3 placeholder.
/// </summary>
public sealed record DescriptorQuery
{
    public string? ContractHash { get; init; }
    public IReadOnlyList<string>? SemanticTags { get; init; }
    public IReadOnlyList<string>? Categories { get; init; }
    public string? Namespace { get; init; }
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IDynamicRegistry.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Dynamic (runtime) registry for descriptors. Separate from RegistryBase which is build-once.
/// </summary>
public interface IDynamicRegistry<TDescriptor>
    where TDescriptor : IDescriptor
{
    bool TryRegister(TDescriptor descriptor);
    bool TryUnregister(string id);
    TDescriptor? GetById(string id);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "DescriptorRefTests" -v`
Expected: PASS (7 tests)

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/ framework/test/CrestCreates.Metadata.Tests/
git commit -m "feat(metadata): add DescriptorRef, DescriptorKey, Validation types, IDescriptorResolver, IDynamicRegistry"
```

---

## Task 3: Create RegistryBase and RegistrySnapshot

**Files:**
- Create: `framework/src/CrestCreates.Metadata/RegistryBase.cs`
- Create: `framework/src/CrestCreates.Metadata/RegistrySnapshot.cs`
- Create: `framework/src/CrestCreates.Metadata/RegistryValidationEngine.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/RegistryState.cs`

- [ ] **Step 1: Write tests for RegistryBase**

```csharp
// framework/test/CrestCreates.Metadata.Tests/RegistryBaseTests.cs
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class RegistryBaseTests
{
    // Test descriptor
    private class TestDescriptor : IDescriptor
    {
        public string Namespace { get; init; } = "test";
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    // Test provider
    private class TestProvider : IDescriptorProvider<TestDescriptor>
    {
        private readonly List<TestDescriptor> _descriptors;
        public TestProvider(List<TestDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<TestDescriptor> GetDescriptors() => _descriptors;
    }

    // Concrete registry for testing
    private class TestRegistry : RegistryBase<TestDescriptor>
    {
        public TestRegistry(IRegistryValidationEngine<TestDescriptor> engine)
            : base(engine) { }

        protected override RegistrySnapshot<TestDescriptor> BuildSnapshot(List<TestDescriptor> descriptors)
        {
            var byId = descriptors.ToFrozenDictionary(d => d.Id, d => d);
            var byName = descriptors.GroupBy(d => d.Name)
                .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());
            return new RegistrySnapshot<TestDescriptor>(byId, byName, FrozenDictionary<DescriptorKey, TestDescriptor>.Empty, descriptors.ToImmutableArray(), ImmutableDictionary<Type, IRegistryIndex>.Empty);
        }
    }

    [Fact]
    public void Build_sets_state_to_Built()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);

        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void Build_is_idempotent()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);

        registry.Build([provider]);
        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void GetById_returns_descriptor()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);

        registry.Build([provider]);

        registry.GetById("a").Should().NotBeNull();
        registry.GetById("a")!.Name.Should().Be("A");
    }

    [Fact]
    public void GetById_returns_null_for_unknown()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);

        registry.Build([provider]);

        registry.GetById("unknown").Should().BeNull();
    }

    [Fact]
    public void GetByName_returns_all_versions()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([
            new TestDescriptor { Id = "a1", Name = "A" },
            new TestDescriptor { Id = "a2", Name = "A" }
        ]);

        registry.Build([provider]);

        registry.GetByName("A").Should().HaveCount(2);
    }

    [Fact]
    public void GetAll_returns_all_descriptors()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([
            new TestDescriptor { Id = "a", Name = "A" },
            new TestDescriptor { Id = "b", Name = "B" }
        ]);

        registry.Build([provider]);

        registry.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Build_with_failing_validator_sets_Failed_state()
    {
        var validator = new FailingValidator();
        var engine = new RegistryValidationEngine<TestDescriptor>([validator]);
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);

        var act = () => registry.Build([provider]);

        act.Should().Throw<RegistryValidationException>();
        registry.State.Should().Be(RegistryState.Failed);
    }

    [Fact]
    public void Build_after_Failed_throws_InvalidOperationException()
    {
        var validator = new FailingValidator();
        var engine = new RegistryValidationEngine<TestDescriptor>([validator]);
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);

        try { registry.Build([provider]); } catch { }

        var act = () => registry.Build([provider]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*previously failed*");
    }

    [Fact]
    public void Build_collects_all_errors_not_just_first()
    {
        var validators = new List<IRegistryValidator<TestDescriptor>>
        {
            new ErrorValidator("Error 1"),
            new ErrorValidator("Error 2"),
            new ErrorValidator("Error 3")
        };
        var engine = new RegistryValidationEngine<TestDescriptor>(validators);
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);

        var act = () => registry.Build([provider]);

        act.Should().Throw<RegistryValidationException>()
            .Which.Issues.Should().HaveCount(3);
    }

    // Helper validators
    private class FailingValidator : IRegistryValidator<TestDescriptor>
    {
        public int Order => 0;
        public ValidationReport Validate(IReadOnlyList<TestDescriptor> descriptors)
            => ValidationReport.FromIssues(new ValidationIssue(ValidationSeverity.Error, "Always fails"));
    }

    private class ErrorValidator : IRegistryValidator<TestDescriptor>
    {
        private readonly string _message;
        public ErrorValidator(string message) => _message = message;
        public int Order => 0;
        public ValidationReport Validate(IReadOnlyList<TestDescriptor> descriptors)
            => ValidationReport.FromIssues(new ValidationIssue(ValidationSeverity.Error, _message));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "RegistryBaseTests" -v`
Expected: FAIL — `RegistryBase`, `RegistrySnapshot`, etc. not found

- [ ] **Step 3: Create RegistryState (moved from Event.Abstractions)**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/RegistryState.cs
namespace CrestCreates.Metadata.Abstractions;

public enum RegistryState
{
    Created,
    Building,
    Built,
    Failed,
    Disposed
}
```

- [ ] **Step 4: Create RegistrySnapshot**

```csharp
// framework/src/CrestCreates.Metadata/RegistrySnapshot.cs
using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

/// <summary>
/// Immutable snapshot of a registry. Built once during Build(), never mutated.
/// </summary>
public sealed record RegistrySnapshot<TDescriptor>(
    FrozenDictionary<string, TDescriptor> ById,
    FrozenDictionary<string, ImmutableArray<TDescriptor>> ByName,
    FrozenDictionary<DescriptorKey, TDescriptor> ByVersion,
    ImmutableArray<TDescriptor> All,
    ImmutableDictionary<Type, IRegistryIndex> CustomIndexes)
    where TDescriptor : IDescriptor;
```

- [ ] **Step 5: Create RegistryValidationEngine**

```csharp
// framework/src/CrestCreates.Metadata/RegistryValidationEngine.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class RegistryValidationEngine<TDescriptor> : IRegistryValidationEngine<TDescriptor>
    where TDescriptor : IDescriptor
{
    private readonly IReadOnlyList<IRegistryValidator<TDescriptor>> _validators;

    public RegistryValidationEngine(IEnumerable<IRegistryValidator<TDescriptor>> validators)
    {
        _validators = validators.OrderBy(v => v.Order).ToList();
    }

    public ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors)
    {
        var allIssues = new List<ValidationIssue>();

        foreach (var validator in _validators)
        {
            var report = validator.Validate(descriptors);
            allIssues.AddRange(report.Issues);
        }

        return new ValidationReport(allIssues);
    }
}
```

- [ ] **Step 6: Create RegistryBase**

```csharp
// framework/src/CrestCreates.Metadata/RegistryBase.cs
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

/// <summary>
/// Generic registry base class. All registries (Event, Capability, Workflow, etc.)
/// inherit from this. Build-once, immutable snapshot, pluggable validation.
/// </summary>
public abstract class RegistryBase<TDescriptor>
    where TDescriptor : IDescriptor
{
    protected RegistrySnapshot<TDescriptor>? _snapshot;
    protected readonly object _buildLock = new();
    public RegistryState State { get; protected set; } = RegistryState.Created;

    private readonly IRegistryValidationEngine<TDescriptor> _validationEngine;
    private readonly IEnumerable<IRegistryIndexBuilder<TDescriptor, IRegistryIndex>>? _indexBuilders;

    protected RegistryBase(
        IRegistryValidationEngine<TDescriptor> validationEngine,
        IEnumerable<IRegistryIndexBuilder<TDescriptor, IRegistryIndex>>? indexBuilders = null)
    {
        _validationEngine = validationEngine;
        _indexBuilders = indexBuilders;
    }

    public void Build(IEnumerable<IDescriptorProvider<TDescriptor>> providers)
    {
        if (State == RegistryState.Built) return;

        lock (_buildLock)
        {
            if (State == RegistryState.Built) return;
            if (State == RegistryState.Failed)
                throw new InvalidOperationException(
                    "Registry.Build() previously failed. Restart required.");
            State = RegistryState.Building;
        }

        var descriptors = providers.SelectMany(p => p.GetDescriptors()).ToList();

        try
        {
            var report = _validationEngine.Validate(descriptors);

            if (report.HasErrors)
                throw new RegistryValidationException(report.Issues);

            _snapshot = BuildSnapshot(descriptors);
            State = RegistryState.Built;
        }
        catch (RegistryValidationException)
        {
            State = RegistryState.Failed;
            throw;
        }
        catch
        {
            State = RegistryState.Failed;
            throw;
        }
    }

    public TDescriptor? GetById(string id)
        => _snapshot?.ById.TryGetValue(id, out var d) == true ? d : null;

    public IReadOnlyList<TDescriptor> GetByName(string name)
        => _snapshot?.ByName.TryGetValue(name, out var versions) == true
            ? versions
            : Array.Empty<TDescriptor>();

    public IReadOnlyList<TDescriptor> GetAll()
        => _snapshot?.All ?? ImmutableArray<TDescriptor>.Empty;

    public TDescriptor? GetByVersion(string id, int version)
        => _snapshot?.ByVersion.TryGetValue(new DescriptorKey("?", id, version), out var d) == true ? d : null;

    protected abstract RegistrySnapshot<TDescriptor> BuildSnapshot(List<TDescriptor> descriptors);
}

public sealed class RegistryValidationException : Exception
{
    public IReadOnlyList<ValidationIssue> Issues { get; }

    public RegistryValidationException(IReadOnlyList<ValidationIssue> issues)
        : base($"Registry validation failed with {issues.Count(i => i.Severity == ValidationSeverity.Error)} error(s):\n" +
               string.Join("\n", issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => $"  - {i.Message}")))
    {
        Issues = issues;
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "RegistryBaseTests" -v`
Expected: PASS (8 tests)

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.Metadata/ framework/src/CrestCreates.Metadata.Abstractions/RegistryState.cs framework/test/CrestCreates.Metadata.Tests/
git commit -m "feat(metadata): add RegistryBase, RegistrySnapshot, RegistryValidationEngine"
```

---

## Task 4: Create Event Validators (Extract from EventRegistry)

**Files:**
- Create: `framework/src/CrestCreates.Metadata/EventVersionChainValidator.cs`
- Create: `framework/src/CrestCreates.Metadata/DuplicateNameVersionValidator.cs`
- Create: `framework/src/CrestCreates.Metadata/UniquePayloadTypeValidator.cs`

- [ ] **Step 1: Write tests for validators**

```csharp
// framework/test/CrestCreates.Metadata.Tests/EventValidatorTests.cs
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Event.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class EventValidatorTests
{
    [Fact]
    public void VersionChainValidator_fails_when_no_active_version()
    {
        var validator = new EventVersionChainValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test", 1, DescriptorState.Deprecated)
        };

        var report = validator.Validate(descriptors);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("no Active version"));
    }

    [Fact]
    public void VersionChainValidator_fails_when_multiple_active()
    {
        var validator = new EventVersionChainValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test", 1, DescriptorState.Active),
            Create("test", 2, DescriptorState.Active)
        };

        var report = validator.Validate(descriptors);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Active versions"));
    }

    [Fact]
    public void VersionChainValidator_fails_when_highest_not_active()
    {
        var validator = new EventVersionChainValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test", 1, DescriptorState.Active),
            Create("test", 2, DescriptorState.Deprecated)
        };

        var report = validator.Validate(descriptors);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("highest version"));
    }

    [Fact]
    public void VersionChainValidator_passes_with_single_active_highest()
    {
        var validator = new EventVersionChainValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test", 1, DescriptorState.Deprecated),
            Create("test", 2, DescriptorState.Active)
        };

        var report = validator.Validate(descriptors);

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void DuplicateNameVersionValidator_finds_duplicates()
    {
        var validator = new DuplicateNameVersionValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test", 1, DescriptorState.Active),
            Create("test", 1, DescriptorState.Active)
        };

        var report = validator.Validate(descriptors);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Duplicate"));
    }

    [Fact]
    public void UniquePayloadTypeValidator_finds_conflicts()
    {
        var validator = new UniquePayloadTypeValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test.a", 1, DescriptorState.Active, typeof(string)),
            Create("test.b", 1, DescriptorState.Active, typeof(string))
        };

        var report = validator.Validate(descriptors);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("PayloadType"));
    }

    private static GeneratedEventDescriptor Create(string name, int version, DescriptorState state, Type? payloadType = null)
        => new()
        {
            Id = GeneratedEventDescriptor.GenerateId(name),
            Namespace = "event",
            Name = name,
            Version = version,
            State = state,
            PayloadType = payloadType ?? typeof(object),
            Scope = EventScope.Local,
            Reliability = EventReliability.AtLeastOnce,
            Importance = EventImportance.Business
        };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "EventValidatorTests" -v`
Expected: FAIL — validator classes not found

- [ ] **Step 3: Implement validators**

```csharp
// framework/src/CrestCreates.Metadata/EventVersionChainValidator.cs
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class EventVersionChainValidator : IRegistryValidator<GeneratedEventDescriptor>
{
    public int Order => 100;

    public ValidationReport Validate(IReadOnlyList<GeneratedEventDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        foreach (var group in descriptors.GroupBy(d => d.Name))
        {
            var active = group.Where(d => d.State == DescriptorState.Active).ToList();

            if (active.Count == 0)
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"Event '{group.Key}' has no Active version."));
            else if (active.Count > 1)
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"Event '{group.Key}' has {active.Count} Active versions: " +
                    $"{string.Join(", ", active.Select(a => $"v{a.Version}"))}."));
            else
            {
                var highest = group.MaxBy(d => d.Version)!;
                if (active[0].Version != highest.Version)
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"Event '{group.Key}': highest version (v{highest.Version}) is {highest.State}, " +
                        $"but v{active[0].Version} is Active."));
            }
        }

        return new ValidationReport(issues);
    }
}
```

```csharp
// framework/src/CrestCreates.Metadata/DuplicateNameVersionValidator.cs
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DuplicateNameVersionValidator : IRegistryValidator<GeneratedEventDescriptor>
{
    public int Order => 200;

    public ValidationReport Validate(IReadOnlyList<GeneratedEventDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        var duplicates = descriptors
            .GroupBy(d => (d.Name, d.Version))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Name} v{g.Key.Version}")
            .ToList();

        if (duplicates.Count > 0)
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"Duplicate (name, version) pairs: {string.Join(", ", duplicates)}."));

        return new ValidationReport(issues);
    }
}
```

```csharp
// framework/src/CrestCreates.Metadata/UniquePayloadTypeValidator.cs
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class UniquePayloadTypeValidator : IRegistryValidator<GeneratedEventDescriptor>
{
    public int Order => 300;

    public ValidationReport Validate(IReadOnlyList<GeneratedEventDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        var violations = descriptors
            .GroupBy(d => d.PayloadType)
            .Where(g => g.Count(d => d.State == DescriptorState.Active) > 1)
            .ToList();

        if (violations.Count > 0)
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"PayloadType uniqueness violation: {violations.Count} CLR types map to multiple Active events."));

        return new ValidationReport(issues);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "EventValidatorTests" -v`
Expected: PASS (6 tests)

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/ framework/test/CrestCreates.Metadata.Tests/
git commit -m "feat(metadata): extract event validators (VersionChain, DuplicateNameVersion, UniquePayloadType)"
```

---

## Task 5: Migrate EventRegistry to RegistryBase

**Files:**
- Modify: `framework/src/CrestCreates.Event.Abstractions/GeneratedEventDescriptor.cs`
- Modify: `framework/src/CrestCreates.Event.Abstractions/DynamicEventDescriptor.cs`
- Modify: `framework/src/CrestCreates.Event/EventRegistry.cs`
- Modify: `framework/src/CrestCreates.Event/EventRegistrySnapshot.cs`
- Modify: `framework/src/CrestCreates.Event/EventRegistryBootstrapper.cs`
- Modify: `framework/src/CrestCreates.Event/RegistryEventValidator.cs`

- [ ] **Step 1: Add Namespace to GeneratedEventDescriptor**

```csharp
// In framework/src/CrestCreates.Event.Abstractions/GeneratedEventDescriptor.cs
// Add to the record:
public string Namespace { get; init; } = "event";
```

- [ ] **Step 2: Add Namespace to DynamicEventDescriptor**

```csharp
// In framework/src/CrestCreates.Event.Abstractions/DynamicEventDescriptor.cs
// Add to the record:
public string Namespace { get; init; } = "event";
```

- [ ] **Step 3: Migrate EventRegistry to inherit RegistryBase**

Replace `framework/src/CrestCreates.Event/EventRegistry.cs`:

```csharp
using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Event;

public sealed class EventRegistry : RegistryBase<GeneratedEventDescriptor>,
    IEventRegistry, IEventMetadataProvider
{
    // Snapshot with PayloadType index (Event-specific)
    private FrozenDictionary<Type, GeneratedEventDescriptor>? _byPayloadType;

    public EventRegistry(IRegistryValidationEngine<GeneratedEventDescriptor> validationEngine)
        : base(validationEngine) { }

    // IEventRegistry (preserved API)
    public GeneratedEventDescriptor? GetByName(string name)
    {
        var all = base.GetByName(name);
        return all.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version);
    }

    public GeneratedEventDescriptor? GetByPayloadType(Type t)
        => _byPayloadType?.TryGetValue(t, out var d) == true ? d : null;

    public GeneratedEventDescriptor? GetByNameAndVersion(string name, int version)
        => base.GetByName(name).FirstOrDefault(v => v.Version == version);

    // IEventMetadataProvider (preserved API)
    public IReadOnlyList<GeneratedEventDescriptor> GetAllVersions(string name)
        => base.GetByName(name);

    public GeneratedEventDescriptor? GetLatestVersion(string name)
        => base.GetByName(name).MaxBy(v => v.Version);

    // IEventRegistry.Build — delegates to RegistryBase
    public void Build(IEnumerable<IEventDescriptorProvider> providers)
    {
        base.Build(providers.Cast<IDescriptorProvider<GeneratedEventDescriptor>>());
    }

    protected override RegistrySnapshot<GeneratedEventDescriptor> BuildSnapshot(
        List<GeneratedEventDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        // Event-specific index: by PayloadType
        _byPayloadType = descriptors
            .Where(d => d.State == DescriptorState.Active)
            .GroupBy(d => d.PayloadType)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        return new RegistrySnapshot<GeneratedEventDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
```

- [ ] **Step 4: Update EventRegistrySnapshot.cs — mark as obsolete**

```csharp
// framework/src/CrestCreates.Event/EventRegistrySnapshot.cs
using System;

namespace CrestCreates.Event;

[Obsolete("Use CrestCreates.Metadata.RegistrySnapshot<T> instead. Will be removed in v1.0.")]
public sealed record EventRegistrySnapshot;
```

- [ ] **Step 5: Run all Event tests**

Run: `dotnet test framework/test/CrestCreates.Event.Tests/CrestCreates.Event.Tests.csproj -v`
Expected: PASS (all existing tests still pass)

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Event.Abstractions/ framework/src/CrestCreates.Event/ framework/test/CrestCreates.Event.Tests/
git commit -m "feat(event): migrate EventRegistry to inherit RegistryBase<T>"
```

---

## Task 6: Create CapabilityDescriptor and CapabilityRegistry

**Files:**
- Create: `framework/src/CrestCreates.Metadata/CapabilityDescriptor.cs`
- Create: `framework/src/CrestCreates.Metadata/CapabilityRegistry.cs`

- [ ] **Step 1: Write tests for CapabilityRegistry**

```csharp
// framework/test/CrestCreates.Metadata.Tests/CapabilityRegistryTests.cs
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class CapabilityRegistryTests
{
    private class TestCapabilityProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly List<CapabilityDescriptor> _descriptors;
        public TestCapabilityProvider(List<CapabilityDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
    }

    [Fact]
    public void Build_succeeds_with_valid_descriptors()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>(
            [new CapabilityDuplicateNameValidator()]);
        var registry = new CapabilityRegistry(engine);
        var provider = new TestCapabilityProvider([
            new CapabilityDescriptor { Id = "approval", Name = "Approval" }
        ]);

        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void GetById_returns_capability()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var provider = new TestCapabilityProvider([
            new CapabilityDescriptor { Id = "approval", Name = "Approval" }
        ]);

        registry.Build([provider]);

        registry.GetById("approval")!.Name.Should().Be("Approval");
    }

    [Fact]
    public void Categories_are_preserved()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var provider = new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "approval",
                Name = "Approval",
                Categories = ["HumanTask", "Workflow"]
            }
        ]);

        registry.Build([provider]);

        registry.GetById("approval")!.Categories.Should().Contain("HumanTask");
    }

    [Fact]
    public void Produces_and_Consumes_are_preserved()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var provider = new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "approval",
                Name = "Approval",
                Produces = [new EventRef("event", "approval.completed")],
                Consumes = [new EventRef("event", "approval.requested")]
            }
        ]);

        registry.Build([provider]);

        var cap = registry.GetById("approval")!;
        cap.Produces.Should().HaveCount(1);
        cap.Consumes.Should().HaveCount(1);
    }

    // Helper validator
    private class CapabilityDuplicateNameValidator : IRegistryValidator<CapabilityDescriptor>
    {
        public int Order => 100;
        public ValidationReport Validate(IReadOnlyList<CapabilityDescriptor> descriptors)
        {
            var issues = new List<ValidationIssue>();
            var dups = descriptors.GroupBy(d => d.Id).Where(g => g.Count() > 1);
            foreach (var g in dups)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Duplicate Id: {g.Key}"));
            return new ValidationReport(issues);
        }
    }
}
```

- [ ] **Step 2: Create CapabilityDescriptor**

```csharp
// framework/src/CrestCreates.Metadata/CapabilityDescriptor.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class CapabilityDescriptor : IDescriptor, IVersionedDescriptor, IHasContractIdentity
{
    public string Namespace { get; init; } = "capability";
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }

    // IHasContractIdentity
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;

    // Capability-specific
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<EventRef> Produces { get; init; } = Array.Empty<EventRef>();
    public IReadOnlyList<EventRef> Consumes { get; init; } = Array.Empty<EventRef>();
    public IReadOnlyList<string> SemanticTags { get; init; } = Array.Empty<string>();
}

public readonly record struct EventRef(string Namespace, string Id, int? Version = null) : IDescriptorRef;
```

- [ ] **Step 3: Create CapabilityRegistry**

```csharp
// framework/src/CrestCreates.Metadata/CapabilityRegistry.cs
using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class CapabilityRegistry : RegistryBase<CapabilityDescriptor>
{
    public CapabilityRegistry(IRegistryValidationEngine<CapabilityDescriptor> validationEngine)
        : base(validationEngine) { }

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

- [ ] **Step 4: Run tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "CapabilityRegistryTests" -v`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/ framework/test/CrestCreates.Metadata.Tests/
git commit -m "feat(metadata): add CapabilityDescriptor and CapabilityRegistry"
```

---

## Task 7: Create BootstrapCoordinator

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IBootstrapTask.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/BootstrapDependencyException.cs`
- Create: `framework/src/CrestCreates.Metadata/BootstrapCoordinator.cs`

- [ ] **Step 1: Write tests for BootstrapCoordinator**

```csharp
// framework/test/CrestCreates.Metadata.Tests/BootstrapCoordinatorTests.cs
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class BootstrapCoordinatorTests
{
    [Fact]
    public async Task Executes_tasks_in_dependency_order()
    {
        var order = new List<string>();
        var tasks = new List<IBootstrapTask>
        {
            new TestTask("A", [], () => order.Add("A")),
            new TestTask("B", ["A"], () => order.Add("B")),
            new TestTask("C", ["A", "B"], () => order.Add("C"))
        };

        var coordinator = new BootstrapCoordinator(tasks, Mock.Of<ILogger<BootstrapCoordinator>>());
        await coordinator.StartAsync(CancellationToken.None);

        order.Should().ContainInOrder("A", "B", "C");
    }

    [Fact]
    public async Task Detects_circular_dependency()
    {
        var tasks = new List<IBootstrapTask>
        {
            new TestTask("A", ["B"], () => { }),
            new TestTask("B", ["A"], () => { })
        };

        var coordinator = new BootstrapCoordinator(tasks, Mock.Of<ILogger<BootstrapCoordinator>>());

        var act = () => coordinator.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<BootstrapDependencyException>()
            .Which.Cycle.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Continues_on_non_required_task_failure()
    {
        var order = new List<string>();
        var tasks = new List<IBootstrapTask>
        {
            new TestTask("A", [], () => order.Add("A")),
            new FailingTask("B", ["A"], isRequired: false),
            new TestTask("C", ["A"], () => order.Add("C"))
        };

        var coordinator = new BootstrapCoordinator(tasks, Mock.Of<ILogger<BootstrapCoordinator>>());
        await coordinator.StartAsync(CancellationToken.None);

        order.Should().Contain("A");
        order.Should().Contain("C");
    }

    [Fact]
    public async Task Throws_on_required_task_failure()
    {
        var tasks = new List<IBootstrapTask>
        {
            new FailingTask("A", [], isRequired: true)
        };

        var coordinator = new BootstrapCoordinator(tasks, Mock.Of<ILogger<BootstrapCoordinator>>());

        var act = () => coordinator.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // Helper tasks
    private class TestTask : IBootstrapTask
    {
        private readonly Action _action;
        public TestTask(string taskId, string[] deps, Action action)
        {
            TaskId = taskId;
            Dependencies = deps;
            _action = action;
        }
        public string TaskId { get; }
        public Type ServiceType => typeof(TestTask);
        public IReadOnlyList<string> Dependencies { get; }
        public bool IsRequired => true;
        public Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
        {
            _action();
            return Task.CompletedTask;
        }
    }

    private class FailingTask : IBootstrapTask
    {
        public FailingTask(string taskId, string[] deps, bool isRequired)
        {
            TaskId = taskId;
            Dependencies = deps;
            IsRequired = isRequired;
        }
        public string TaskId { get; }
        public Type ServiceType => typeof(FailingTask);
        public IReadOnlyList<string> Dependencies { get; }
        public bool IsRequired { get; }
        public Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
            => throw new InvalidOperationException("Bootstrap failed");
    }
}
```

- [ ] **Step 2: Implement BootstrapCoordinator**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IBootstrapTask.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Bootstrap task. Not limited to registries — Schema, Projection, Cache, AI Index can also use this.
/// </summary>
public interface IBootstrapTask
{
    /// <summary>
    /// Unique task identifier for dependency declaration.
    /// Examples: "event-registry", "capability-registry"
    /// </summary>
    string TaskId { get; }

    /// <summary>
    /// Task type for logging and diagnostics.
    /// </summary>
    Type ServiceType { get; }

    /// <summary>
    /// Dependencies declared by TaskId.
    /// </summary>
    IReadOnlyList<string> Dependencies { get; }

    /// <summary>
    /// If true, failure terminates startup. If false, failure logs warning and continues.
    /// </summary>
    bool IsRequired { get; }

    Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken ct);
}
```

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/BootstrapDependencyException.cs
namespace CrestCreates.Metadata.Abstractions;

public sealed class BootstrapDependencyException : Exception
{
    public IReadOnlyList<string> Cycle { get; }

    public BootstrapDependencyException(IReadOnlyList<string> cycle)
        : base($"Bootstrap dependency cycle detected: {string.Join(" -> ", cycle)}")
    {
        Cycle = cycle;
    }
}
```

```csharp
// framework/src/CrestCreates.Metadata/BootstrapCoordinator.cs
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Metadata;

public sealed class BootstrapCoordinator : IHostedService
{
    private readonly IEnumerable<IBootstrapTask> _tasks;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BootstrapCoordinator> _logger;

    public BootstrapCoordinator(
        IEnumerable<IBootstrapTask> tasks,
        IServiceProvider serviceProvider,
        ILogger<BootstrapCoordinator> logger)
    {
        _tasks = tasks;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var taskMap = _tasks.ToDictionary(t => t.TaskId);
        var sorted = TopologicalSort(taskMap);

        foreach (var task in sorted)
        {
            _logger.LogInformation("Bootstrapping {TaskId} ({TaskType})...", task.TaskId, task.ServiceType.Name);
            try
            {
                await task.ExecuteAsync(_serviceProvider, ct);
            }
            catch (Exception ex) when (!task.IsRequired)
            {
                _logger.LogWarning(ex, "Non-required bootstrap task {TaskId} failed, continuing", task.TaskId);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static IReadOnlyList<IBootstrapTask> TopologicalSort(Dictionary<string, IBootstrapTask> taskMap)
    {
        var result = new List<IBootstrapTask>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        var path = new List<string>();

        foreach (var taskId in taskMap.Keys)
        {
            if (!visited.Contains(taskId))
                Visit(taskId, taskMap, visited, visiting, path, result);
        }

        return result;
    }

    private static void Visit(
        string taskId,
        Dictionary<string, IBootstrapTask> taskMap,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<string> path,
        List<IBootstrapTask> result)
    {
        if (visiting.Contains(taskId))
        {
            var cycleStart = path.IndexOf(taskId);
            var cycle = path.Skip(cycleStart).Concat([taskId]).ToList();
            throw new BootstrapDependencyException(cycle);
        }

        if (visited.Contains(taskId))
            return;

        visiting.Add(taskId);
        path.Add(taskId);

        if (taskMap.TryGetValue(taskId, out var task))
        {
            foreach (var dep in task.Dependencies)
            {
                if (taskMap.ContainsKey(dep))
                    Visit(dep, taskMap, visited, visiting, path, result);
            }
            result.Add(task);
        }

        visiting.Remove(taskId);
        path.RemoveAt(path.Count - 1);
        visited.Add(taskId);
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "BootstrapCoordinatorTests" -v`
Expected: PASS (4 tests)

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata/ framework/src/CrestCreates.Metadata.Abstractions/ framework/test/CrestCreates.Metadata.Tests/
git commit -m "feat(metadata): add BootstrapCoordinator with topological sort and cycle detection"
```

---

## Task 8: Create DescriptorResolver

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorResolver.cs`

- [ ] **Step 1: Write tests for DescriptorResolver**

```csharp
// framework/test/CrestCreates.Metadata.Tests/DescriptorResolverTests.cs
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorResolverTests
{
    private class TestDescriptor : IDescriptor, IVersionedDescriptor
    {
        public string Namespace { get; init; } = "test";
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Version { get; init; }
    }

    [Fact]
    public void Resolve_by_id_returns_latest()
    {
        var resolver = new DescriptorResolver(
            new Dictionary<Type, Func<string, IDescriptor?>>
            {
                [typeof(TestDescriptor)] = id => id == "a" ? new TestDescriptor { Id = "a", Name = "A", Version = 2 } : null
            });

        var result = resolver.Resolve<TestDescriptor>("a");

        result.Should().NotBeNull();
        result!.Version.Should().Be(2);
    }

    [Fact]
    public void Resolve_returns_null_for_unknown()
    {
        var resolver = new DescriptorResolver(new Dictionary<Type, Func<string, IDescriptor?>>());

        var result = resolver.Resolve<TestDescriptor>("unknown");

        result.Should().BeNull();
    }

    [Fact]
    public void Query_returns_empty_when_not_implemented()
    {
        var resolver = new DescriptorResolver(new Dictionary<Type, Func<string, IDescriptor?>>());

        var result = resolver.Query<TestDescriptor>(new DescriptorQuery());

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Implement DescriptorResolver**

```csharp
// framework/src/CrestCreates.Metadata/DescriptorResolver.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DescriptorResolver : IDescriptorResolver
{
    private readonly IReadOnlyDictionary<Type, Func<string, IDescriptor?>> _resolvers;

    public DescriptorResolver(IReadOnlyDictionary<Type, Func<string, IDescriptor?>> resolvers)
    {
        _resolvers = resolvers;
    }

    public TDescriptor? Resolve<TDescriptor>(string id)
        where TDescriptor : IDescriptor
    {
        if (_resolvers.TryGetValue(typeof(TDescriptor), out var resolver))
            return (TDescriptor?)resolver(id);
        return default;
    }

    public TDescriptor? Resolve<TDescriptor>(IDescriptorRef reference)
        where TDescriptor : IDescriptor
    {
        // For Phase 3: delegate to id-based resolution
        return Resolve<TDescriptor>(reference.Id);
    }

    public IReadOnlyList<TDescriptor> Query<TDescriptor>(DescriptorQuery query)
        where TDescriptor : IDescriptor
    {
        // Phase 3 placeholder — Phase 5~7 will implement
        return Array.Empty<TDescriptor>();
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "DescriptorResolverTests" -v`
Expected: PASS (3 tests)

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata/ framework/test/CrestCreates.Metadata.Tests/
git commit -m "feat(metadata): add DescriptorResolver with id-based and ref-based resolution"
```

---

## Task 9: Wire BootstrapTask into EventRegistryBootstrapper

**Files:**
- Modify: `framework/src/CrestCreates.Event/EventRegistryBootstrapper.cs`
- Modify: `framework/src/CrestCreates.Event/PassThroughEventValidator.cs`

- [ ] **Step 1: Update EventRegistryBootstrapper to implement IBootstrapTask**

```csharp
// framework/src/CrestCreates.Event/EventRegistryBootstrapper.cs
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Event;

public sealed class EventRegistryBootstrapper : IHostedService, IBootstrapTask
{
    private readonly EventRegistry _registry;
    private readonly IEnumerable<IEventDescriptorProvider> _providers;

    public EventRegistryBootstrapper(
        EventRegistry registry,
        IEnumerable<IEventDescriptorProvider> providers)
    {
        _registry = registry;
        _providers = providers;
    }

    // IBootstrapTask
    public string TaskId => "event-registry";
    public Type ServiceType => typeof(EventRegistryBootstrapper);
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();
    public bool IsRequired => true;

    public Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken ct)
    {
        _registry.Build(_providers);
        return Task.CompletedTask;
    }

    // IHostedService (preserved for backward compat)
    public Task StartAsync(CancellationToken ct)
    {
        _registry.Build(_providers);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

- [ ] **Step 2: Run all Event tests**

Run: `dotnet test framework/test/CrestCreates.Event.Tests/CrestCreates.Event.Tests.csproj -v`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Event/
git commit -m "feat(event): update EventRegistryBootstrapper to implement IBootstrapTask"
```

---

## Task 10: Update PassThroughEventValidator and RegistryEventValidator

**Files:**
- Modify: `framework/src/CrestCreates.Event/PassThroughEventValidator.cs`
- Modify: `framework/src/CrestCreates.Event/RegistryEventValidator.cs`

- [ ] **Step 1: Run existing validator tests**

Run: `dotnet test framework/test/CrestCreates.Event.Tests/CrestCreates.Event.Tests.csproj --filter "ValidatorAndDynamic" -v`
Expected: PASS (current state)

- [ ] **Step 2: No changes needed to PassThroughEventValidator — it already works**

PassThroughEventValidator implements `IEventValidator` which is unchanged. RegistryEventValidator depends on `IEventResolver` and `IEventMetadataProvider` which are also unchanged. No migration needed.

- [ ] **Step 3: Commit (if no changes) — skip**

---

## Task 11: Final Integration — Full Build and All Tests

**Files:**
- All source files

- [ ] **Step 1: Clean generated files**

Run: `find framework -path "*/obj/*/source-generators" -type d -exec rm -rf {} + 2>/dev/null; echo "Cleaned"`

- [ ] **Step 2: Build Metadata project**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj 2>&1 | tail -5`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Build Event project**

Run: `dotnet build framework/src/CrestCreates.Event/CrestCreates.Event.csproj 2>&1 | tail -5`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Run Metadata tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj -v`
Expected: ALL PASS

- [ ] **Step 5: Run Event tests**

Run: `dotnet test framework/test/CrestCreates.Event.Tests/CrestCreates.Event.Tests.csproj -v`
Expected: ALL PASS (27+ existing + new)

- [ ] **Step 6: Build key bus projects**

Run:
```bash
for proj in CrestCreates.EventBus.Local CrestCreates.EventBus.Local.Channel CrestCreates.EventBus.RabbitMQ CrestCreates.EventBus.Kafka; do
  echo "=== $proj ==="
  dotnet build "framework/src/$proj/$proj.csproj" 2>&1 | grep -E "(error|Build)" | tail -2
done
```
Expected: All Build succeeded, 0 errors

- [ ] **Step 7: Final commit**

```bash
git add -A
git commit -m "feat(metadata): Phase 3 Metadata Runtime Foundation complete

- RegistryBase<T>: generic base class with FrozenDictionary snapshot
- RegistrySnapshot<T>: immutable snapshot with ById/ByName/ByVersion indexes
- RegistryValidationEngine<T>: pluggable validation with batch error reporting
- BootstrapCoordinator: topological sort with cycle detection
- DescriptorResolver: unified resolver for all descriptor types
- CapabilityDescriptor + CapabilityRegistry: first non-Event registry
- EventRegistry: migrated to RegistryBase internally, API unchanged
- IDescriptor: added Namespace, FullId computed property
- IHasContractIdentity: ContractHash/DefinitionHash interface
- IRelationshipAwareDescriptor: self-describing relationships
- All 40+ unit tests passing"
```

---

## Summary

| Track | Tasks | Purpose |
|-------|-------|---------|
| A: Core Abstractions | 1-2 | IDescriptor.Namespace, DescriptorRef, Validation types |
| B: Registry Runtime | 3-4 | RegistryBase, RegistrySnapshot, Validators |
| C: Event Migration | 5, 9-10 | EventRegistry → RegistryBase |
| D: New Registries | 6 | CapabilityDescriptor + CapabilityRegistry |
| E: Infrastructure | 7-8 | BootstrapCoordinator, DescriptorResolver |
| F: Verification | 11 | Full build + all tests |

**Total tasks:** 11
**Estimated time:** 2-3 hours for experienced developer
