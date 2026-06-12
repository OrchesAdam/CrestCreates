# Phase 6b — Descriptor Topology Read Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the unique descriptor topology read model — `IDescriptorTopologyBuilder` producing `DescriptorTopologySnapshot` — from Phase 6a's `DescriptorRelationship` data layer, with embedded diagnostics and a backward-compat adapter over `IDescriptorDependencyGraph`.

**Architecture:** Stateless `IDescriptorTopologyBuilder.Build(descriptors)` projects flat relationship lists into an immutable graph snapshot (nodes, edges, consumer index, diagnostics). A `DescriptorDependencyGraphAdapter` wraps the builder for `DescriptorCatalog` backward compat. Old `DescriptorDependencyGraph` + `DependencyGraphProvider` are removed.

**Tech Stack:** C# 13 / .NET 10, xUnit + FluentAssertions, Moq. No new NuGet dependencies.

**Spec:** `docs/superpowers/specs/2026-06-12-phase-6b-descriptor-topology-design.md`

---

## Implementation Precautions (from review)

1. **DescriptorRef equality**: Node dictionary keyed by full `DescriptorRef` (Namespace, Id, Version). Consumer index keyed by `DescriptorIdentity` (Namespace, Id) — no Version. Do NOT mix.
2. **Direct queries on missing targets**: Return only existing nodes. Do NOT throw. Missing targets exposed via Diagnostics.
3. **Cycle detection**: Only on Strong edges where both From AND To exist in nodes. Missing targets are already handled by MISSING_TARGET diagnostic.
4. **Node edge indices frozen**: Builder uses mutable `List<int>` internally; freezes to `IReadOnlySet<int>` in `DescriptorNode`.
5. **Adapter id-only lookup**: Restricted to adapter internals. Public `DescriptorTopologySnapshot` only exposes `FindNode(DescriptorRef)`.
6. **Diagnostic message richness**: Include Kind, Role, SourcePath, From, To in messages — especially for MISSING_TARGET.

---

### Task 1: Remove old dependency graph files

**Files:**
- Move: `framework/src/CrestCreates.Metadata/DescriptorDependencyGraph.cs` → `99_RecycleBin/`
- Move: `framework/src/CrestCreates.Metadata/DependencyGraphProvider.cs` → `99_RecycleBin/`

- [ ] **Step 1: Move DescriptorDependencyGraph.cs to recycle bin**

```bash
mkdir -p 99_RecycleBin
mv framework/src/CrestCreates.Metadata/DescriptorDependencyGraph.cs 99_RecycleBin/
```

- [ ] **Step 2: Move DependencyGraphProvider.cs to recycle bin**

```bash
mv framework/src/CrestCreates.Metadata/DependencyGraphProvider.cs 99_RecycleBin/
```

- [ ] **Step 3: Build to confirm no compile errors from removal**

```bash
dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj 2>&1
```

Expected: Build errors from any remaining references to `DescriptorDependencyGraph` or `DependencyGraphProvider`. If any exist, fix them (remove the reference / update to not use these types).

- [ ] **Step 4: Commit**

```bash
git add 99_RecycleBin/ framework/src/CrestCreates.Metadata/
git commit -m "chore: remove DescriptorDependencyGraph and DependencyGraphProvider to recycle bin"
```

---

### Task 2: Create core topology type files

**Files to create (11 total in `CrestCreates.Metadata.Abstractions/DescriptorTopology/`):**

- `DescriptorIdentity.cs`
- `DescriptorNode.cs`
- `DescriptorEdge.cs`
- `DescriptorTopologySnapshot.cs`
- `DescriptorTopologyDiagnostics.cs`
- `DescriptorTopologyDiagnostic.cs`
- `DiagnosticSeverity.cs`
- `RelationshipRoles.cs`
- `IDescriptorTopologyBuilder.cs` (in `CrestCreates.Metadata.Abstractions/`, not in DescriptorTopology/)

- [ ] **Step 1: Create Directory.Build.props for the new subfolder (if needed)**

The `DescriptorTopology/` subfolder is under `CrestCreates.Metadata.Abstractions/`. No new project needed — just a directory.

```bash
mkdir -p framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology
```

- [ ] **Step 2: Create DiagnosticSeverity.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DiagnosticSeverity.cs
namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}
```

- [ ] **Step 3: Create DescriptorIdentity.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DescriptorIdentity.cs
namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

/// <summary>
/// Version-independent descriptor identity key.
/// Based on (Namespace, Id) only. DescriptorRef has no Kind field;
/// Namespace is already unique per descriptor kind.
/// </summary>
public readonly record struct DescriptorIdentity(
    string Namespace,
    string Id);
```

- [ ] **Step 4: Create DescriptorNode.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DescriptorNode.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed record DescriptorNode
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorState State { get; init; }
    public string? ContractHash { get; init; }
    public string? SupersededById { get; init; }

    public required IReadOnlySet<int> OutgoingEdgeIndices { get; init; }
    public required IReadOnlySet<int> IncomingEdgeIndices { get; init; }
}
```

- [ ] **Step 5: Create DescriptorEdge.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DescriptorEdge.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed record DescriptorEdge
{
    public required int Index { get; init; }
    public required DescriptorRef From { get; init; }
    public required DescriptorRef To { get; init; }
    public required RelationshipKind Kind { get; init; }
    public string? Role { get; init; }
    public string? SourcePath { get; init; }
    public required RelationshipStrength Strength { get; init; }
    public required bool IsRuntimeBinding { get; init; }
}
```

- [ ] **Step 6: Create DescriptorTopologyDiagnostic.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DescriptorTopologyDiagnostic.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed record DescriptorTopologyDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DescriptorRef? Subject,
    IReadOnlyList<DescriptorRef>? RelatedRefs);
```

- [ ] **Step 7: Create DescriptorTopologyDiagnostics.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DescriptorTopologyDiagnostics.cs
namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed record DescriptorTopologyDiagnostics
{
    public required IReadOnlyList<DescriptorTopologyDiagnostic> All { get; init; }

    public IReadOnlyList<DescriptorTopologyDiagnostic> Errors =>
        All.Where(d => d.Severity == DiagnosticSeverity.Error).ToList().AsReadOnly();

    public IReadOnlyList<DescriptorTopologyDiagnostic> Warnings =>
        All.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList().AsReadOnly();

    public bool HasErrors => Errors.Count > 0;
    public bool IsHealthy => !HasErrors;
}
```

- [ ] **Step 8: Create RelationshipRoles.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/RelationshipRoles.cs
namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public static class RelationshipRoles
{
    public const string InputSchema     = "InputSchema";
    public const string OutputSchema    = "OutputSchema";
    public const string Schema          = "Schema";
    public const string Interaction     = "Interaction";
    public const string PayloadSchema   = "PayloadSchema";
    public const string VariableSchema  = "VariableSchema";
    public const string CapabilityStep  = "CapabilityStep";
    public const string HumanTaskStep   = "HumanTaskStep";
    public const string SubWorkflowStep = "SubWorkflowStep";
    public const string Outcome         = "Outcome";
    public const string SupersededBy    = "SupersededBy";
}
```

- [ ] **Step 9: Create DescriptorTopologySnapshot.cs (skeleton)**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DescriptorTopologySnapshot.cs
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed class DescriptorTopologySnapshot
{
    private readonly Dictionary<DescriptorRef, DescriptorNode> _nodes;
    private readonly List<DescriptorEdge> _edges;

    // Consumer index internals — populated during Build, frozen here
    private readonly Dictionary<DescriptorIdentity, List<(DescriptorRef Consumer, DescriptorEdge Edge)>> _consumersByIdentity;
    private readonly Dictionary<(DescriptorIdentity Id, int Version), List<(DescriptorRef Consumer, DescriptorEdge Edge)>> _consumersByExactVersion;
    private readonly Dictionary<DescriptorIdentity, List<(DescriptorRef Consumer, DescriptorEdge Edge)>> _consumersByUnpinnedVersion;

    public DateTimeOffset BuiltAt { get; }
    public int NodeCount => _nodes.Count;
    public int EdgeCount => _edges.Count;

    public IReadOnlyDictionary<DescriptorRef, DescriptorNode> Nodes { get; }
    public IReadOnlyList<DescriptorEdge> Edges { get; }
    public DescriptorTopologyDiagnostics Diagnostics { get; }

    internal DescriptorTopologySnapshot(
        Dictionary<DescriptorRef, DescriptorNode> nodes,
        List<DescriptorEdge> edges,
        DescriptorTopologyDiagnostics diagnostics,
        Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>> consumersByIdentity,
        Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>> consumersByExactVersion,
        Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>> consumersByUnpinnedVersion,
        DateTimeOffset builtAt)
    {
        _nodes = nodes;
        _edges = edges;
        Diagnostics = diagnostics;
        _consumersByIdentity = consumersByIdentity;
        _consumersByExactVersion = consumersByExactVersion;
        _consumersByUnpinnedVersion = consumersByUnpinnedVersion;
        BuiltAt = builtAt;

        Nodes = nodes.ToImmutableDictionary();
        Edges = edges.ToImmutableList();
    }

    public bool Contains(DescriptorRef r) => _nodes.ContainsKey(r);

    public DescriptorNode? FindNode(DescriptorRef r) =>
        _nodes.TryGetValue(r, out var node) ? node : null;

    // Query methods — will be implemented in Tasks 5-6
    public IReadOnlyList<DescriptorNode> GetDirectDependencies(DescriptorRef of) =>
        throw new NotImplementedException();

    public IReadOnlyList<DescriptorNode> GetDirectDependents(DescriptorRef of) =>
        throw new NotImplementedException();

    public IReadOnlySet<DescriptorNode> GetTransitiveDependencies(
        DescriptorRef of, bool includeWeak = false) =>
        throw new NotImplementedException();

    public IReadOnlySet<DescriptorNode> GetTransitiveDependents(
        DescriptorRef of, bool includeWeak = false) =>
        throw new NotImplementedException();

    public IReadOnlyList<DescriptorNode> GetConsumers(
        string ns, string id, int? version = null) =>
        throw new NotImplementedException();
}
```

- [ ] **Step 10: Create IDescriptorTopologyBuilder.cs**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IDescriptorTopologyBuilder.cs
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorTopologyBuilder
{
    DescriptorTopologySnapshot Build(IReadOnlyList<IDescriptor> descriptors);
}
```

- [ ] **Step 11: Build to verify all new files compile**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj 2>&1
```

Expected: 0 errors. `NotImplementedException` is valid in method bodies.

- [ ] **Step 12: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/
git commit -m "feat(Phase6b): add core topology types — DescriptorNode, DescriptorEdge, DescriptorIdentity, snapshot skeleton"
```

---

### Task 3: Builder — empty input and node creation (TDD)

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologyBuilderTests.cs`
- Create: `framework/src/CrestCreates.Metadata/DescriptorTopologyBuilder.cs`

- [ ] **Step 1: Create test file with empty input test**

```csharp
// framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologyBuilderTests.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class DescriptorTopologyBuilderTests
{
    [Fact]
    public void Build_Empty_Input_Produces_Empty_Snapshot()
    {
        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        var builder = new DescriptorTopologyBuilder(mockProvider.Object);

        var snapshot = builder.Build(Array.Empty<IDescriptor>());

        snapshot.NodeCount.Should().Be(0);
        snapshot.EdgeCount.Should().Be(0);
        snapshot.Nodes.Should().BeEmpty();
        snapshot.Edges.Should().BeEmpty();
        snapshot.Diagnostics.All.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails (DescriptorTopologyBuilder not found)**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~Build_Empty" 2>&1
```

Expected: FAIL — type `DescriptorTopologyBuilder` not found.

- [ ] **Step 3: Create DescriptorTopologyBuilder.cs (minimal)**

```csharp
// framework/src/CrestCreates.Metadata/DescriptorTopologyBuilder.cs
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata;

/// <summary>
/// Internal builder for DescriptorTopologySnapshot.
/// Public constructor for DI activation from this assembly's extension methods.
/// </summary>
internal sealed class DescriptorTopologyBuilder : IDescriptorTopologyBuilder
{
    private readonly IDescriptorRelationshipProvider _relationshipProvider;

    public DescriptorTopologyBuilder(IDescriptorRelationshipProvider relationshipProvider)
    {
        _relationshipProvider = relationshipProvider;
    }

    public DescriptorTopologySnapshot Build(IReadOnlyList<IDescriptor> descriptors)
    {
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>();
        var edges = new List<DescriptorEdge>();
        var diagnostics = new DescriptorTopologyDiagnostics
        {
            All = Array.Empty<DescriptorTopologyDiagnostic>()
        };
        var consumersByIdentity = new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>();
        var consumersByExactVersion = new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>();
        var consumersByUnpinnedVersion = new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>();

        return new DescriptorTopologySnapshot(
            nodes, edges, diagnostics,
            consumersByIdentity, consumersByExactVersion, consumersByUnpinnedVersion,
            DateTimeOffset.UtcNow);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~Build_Empty" 2>&1
```

Expected: PASS.

- [ ] **Step 5: Add node creation test**

```csharp
// Add to DescriptorTopologyBuilderTests.cs
[Fact]
public void Build_Creates_Nodes_For_All_Provided_Descriptors()
{
    var mockProvider = new Mock<IDescriptorRelationshipProvider>();
    var builder = new DescriptorTopologyBuilder(mockProvider.Object);

    var schemaDesc = CreateMockDescriptor("schema", "User", "User Schema", DescriptorKind.Schema);
    var capDesc = CreateMockDescriptor("capability", "CreateUser", "Create User", DescriptorKind.Capability);

    var snapshot = builder.Build(new[] { schemaDesc, capDesc });

    snapshot.NodeCount.Should().Be(2);
    snapshot.Contains(new DescriptorRef("schema", "User", null)).Should().BeTrue();
    snapshot.Contains(new DescriptorRef("capability", "CreateUser", null)).Should().BeTrue();
}

[Fact]
public void Build_Node_Has_Correct_Summary_Properties()
{
    var mockProvider = new Mock<IDescriptorRelationshipProvider>();
    var builder = new DescriptorTopologyBuilder(mockProvider.Object);

    var desc = CreateMockDescriptor("schema", "User", "User Schema", DescriptorKind.Schema,
        state: DescriptorState.Active, contractHash: "abc123", supersededById: null);

    var snapshot = builder.Build(new[] { desc });

    var node = snapshot.FindNode(new DescriptorRef("schema", "User", null));
    node.Should().NotBeNull();
    node!.Kind.Should().Be(DescriptorKind.Schema);
    node.Name.Should().Be("User Schema");
    node.State.Should().Be(DescriptorState.Active);
    node.ContractHash.Should().Be("abc123");
    node.SupersededById.Should().BeNull();
    node.OutgoingEdgeIndices.Should().BeEmpty();
    node.IncomingEdgeIndices.Should().BeEmpty();
}

private static IDescriptor CreateMockDescriptor(
    string ns, string id, string name, DescriptorKind kind,
    DescriptorState state = DescriptorState.Active,
    string contractHash = "hash",
    string? supersededById = null)
{
    var mock = new Mock<IDescriptor>();
    mock.Setup(d => d.Namespace).Returns(ns);
    mock.Setup(d => d.Id).Returns(id);
    mock.Setup(d => d.Name).Returns(name);
    mock.Setup(d => d.Kind).Returns(kind);
    mock.Setup(d => d.State).Returns(state);
    mock.Setup(d => d.ContractHash).Returns(contractHash);
    mock.Setup(d => d.SupersededById).Returns(supersededById);
    return mock.Object;
}
```

- [ ] **Step 6: Run tests — build test should pass, node tests should fail (no node creation logic yet)**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~Build_Node\|FullyQualifiedName~Build_Creates" 2>&1
```

Expected: `Build_Empty` PASS, `Build_Creates_Nodes_For_All_Provided_Descriptors` FAIL, `Build_Node_Has_Correct_Summary_Properties` FAIL.

- [ ] **Step 7: Implement node creation in DescriptorTopologyBuilder.Build()**

Replace the minimal `Build()` implementation:

```csharp
public DescriptorTopologySnapshot Build(IReadOnlyList<IDescriptor> descriptors)
{
    var nodes = new Dictionary<DescriptorRef, DescriptorNode>();
    var edges = new List<DescriptorEdge>();

    // Phase 2: Create nodes
    foreach (var descriptor in descriptors)
    {
        var nodeRef = new DescriptorRef(descriptor.Namespace, descriptor.Id, (descriptor as IVersionedDescriptor)?.Version);
        var kind = (descriptor as IVersionedDescriptor) is IVersionedDescriptor vd
            ? new DescriptorRef(descriptor.Namespace, descriptor.Id, vd.Version)
            : new DescriptorRef(descriptor.Namespace, descriptor.Id, null);

        nodes[descriptorRef] = new DescriptorNode
        {
            Ref = descriptorRef,
            Kind = descriptor.Kind,
            Name = descriptor.Name,
            State = descriptor.State,
            ContractHash = string.IsNullOrEmpty(descriptor.ContractHash) ? null : descriptor.ContractHash,
            SupersededById = descriptor.SupersededById,
            OutgoingEdgeIndices = new HashSet<int>(),
            IncomingEdgeIndices = new HashSet<int>()
        };
    }

    var diagnostics = new DescriptorTopologyDiagnostics
    {
        All = Array.Empty<DescriptorTopologyDiagnostic>()
    };
    var consumersByIdentity = new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>();
    var consumersByExactVersion = new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>();
    var consumersByUnpinnedVersion = new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>();

    return new DescriptorTopologySnapshot(
        nodes, edges, diagnostics,
        consumersByIdentity, consumersByExactVersion, consumersByUnpinnedVersion,
        DateTimeOffset.UtcNow);
}
```

> **Correction**: `IDescriptor` does not expose `Version`. The version comes from `IVersionedDescriptor`. Let me use `IVersionedDescriptor` check:

```csharp
foreach (var descriptor in descriptors)
{
    int? version = (descriptor as IVersionedDescriptor)?.Version;
    var nodeRef = new DescriptorRef(descriptor.Namespace, descriptor.Id, version);

    nodes[nodeRef] = new DescriptorNode
    {
        Ref = nodeRef,
        Kind = descriptor.Kind,
        Name = descriptor.Name,
        State = descriptor.State,
        ContractHash = string.IsNullOrEmpty(descriptor.ContractHash) ? null : descriptor.ContractHash,
        SupersededById = descriptor.SupersededById,
        OutgoingEdgeIndices = new HashSet<int>(),
        IncomingEdgeIndices = new HashSet<int>()
    };
}
```

- [ ] **Step 8: Run tests to verify they pass**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~Build_" 2>&1
```

Expected: All builder tests PASS.

- [ ] **Step 9: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/ framework/src/CrestCreates.Metadata/DescriptorTopologyBuilder.cs
git commit -m "feat(Phase6b): add DescriptorTopologyBuilder with node creation from descriptor inventory"
```

---

### Task 4: Builder — edge extraction (TDD)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologyBuilderTests.cs`
- Modify: `framework/src/CrestCreates.Metadata/DescriptorTopologyBuilder.cs`

- [ ] **Step 1: Add edge extraction test**

```csharp
// Add to DescriptorTopologyBuilderTests.cs
[Fact]
public void Build_Extracts_Edges_From_RelationshipProvider()
{
    var schemaRef = new DescriptorRef("schema", "User", null);
    var formRef = new DescriptorRef("form", "UserForm", null);

    var relationships = new List<DescriptorRelationship>
    {
        new(formRef, schemaRef, RelationshipKind.Uses, "Schema", "Schema", RelationshipStrength.Strong, false)
    };

    var mockProvider = new Mock<IDescriptorRelationshipProvider>();
    var schemaDesc = CreateMockDescriptor("schema", "User", "User Schema", DescriptorKind.Schema);
    var formDesc = CreateMockDescriptor("form", "UserForm", "User Form", DescriptorKind.Form);

    mockProvider
        .Setup(p => p.GetRelationships(formDesc))
        .Returns(relationships);
    mockProvider
        .Setup(p => p.GetRelationships(schemaDesc))
        .Returns(Array.Empty<DescriptorRelationship>());

    var builder = new DescriptorTopologyBuilder(mockProvider.Object);
    var snapshot = builder.Build(new IDescriptor[] { schemaDesc, formDesc });

    snapshot.EdgeCount.Should().Be(1);
    var edge = snapshot.Edges[0];
    edge.Index.Should().Be(0);
    edge.From.Should().Be(formRef);
    edge.To.Should().Be(schemaRef);
    edge.Kind.Should().Be(RelationshipKind.Uses);
    edge.Role.Should().Be("Schema");
    edge.Strength.Should().Be(RelationshipStrength.Strong);
}

[Fact]
public void Build_Edge_Indices_Populated_On_Nodes()
{
    var schemaRef = new DescriptorRef("schema", "User", null);
    var formRef = new DescriptorRef("form", "UserForm", null);

    var relationships = new List<DescriptorRelationship>
    {
        new(formRef, schemaRef, RelationshipKind.Uses, "Schema", "Schema", RelationshipStrength.Strong, false)
    };

    var mockProvider = new Mock<IDescriptorRelationshipProvider>();
    var schemaDesc = CreateMockDescriptor("schema", "User", "User Schema", DescriptorKind.Schema);
    var formDesc = CreateMockDescriptor("form", "UserForm", "User Form", DescriptorKind.Form);

    mockProvider.Setup(p => p.GetRelationships(formDesc)).Returns(relationships);
    mockProvider.Setup(p => p.GetRelationships(schemaDesc)).Returns(Array.Empty<DescriptorRelationship>());

    var builder = new DescriptorTopologyBuilder(mockProvider.Object);
    var snapshot = builder.Build(new IDescriptor[] { schemaDesc, formDesc });

    var formNode = snapshot.FindNode(formRef)!;
    var schemaNode = snapshot.FindNode(schemaRef)!;

    formNode.OutgoingEdgeIndices.Should().BeEquivalentTo(new[] { 0 });
    formNode.IncomingEdgeIndices.Should().BeEmpty();
    schemaNode.OutgoingEdgeIndices.Should().BeEmpty();
    schemaNode.IncomingEdgeIndices.Should().BeEquivalentTo(new[] { 0 });
}

[Fact]
public void Build_Edge_To_Unknown_Target_Still_Created()
{
    // Missing target is a diagnostic, not a build error.
    // Edge is still created so diagnostics can report it.
    var formRef = new DescriptorRef("form", "UserForm", null);
    var missingRef = new DescriptorRef("schema", "MissingSchema", null);

    var relationships = new List<DescriptorRelationship>
    {
        new(formRef, missingRef, RelationshipKind.Uses, "Schema", "Schema", RelationshipStrength.Strong, false)
    };

    var mockProvider = new Mock<IDescriptorRelationshipProvider>();
    var formDesc = CreateMockDescriptor("form", "UserForm", "User Form", DescriptorKind.Form);
    mockProvider.Setup(p => p.GetRelationships(formDesc)).Returns(relationships);

    var builder = new DescriptorTopologyBuilder(mockProvider.Object);
    var snapshot = builder.Build(new IDescriptor[] { formDesc });

    snapshot.EdgeCount.Should().Be(1); // Edge always created
    snapshot.Edges[0].To.Should().Be(missingRef);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~Build_Edge\|FullyQualifiedName~Build_Extracts" 2>&1
```

Expected: FAIL — edge extraction not implemented.

- [ ] **Step 3: Implement edge extraction in Build()**

Add Phase 3 to the `Build()` method, after node creation:

```csharp
// Phase 3: Extract edges
using (var moqScope = new Moq.MockRepository(Moq.MockBehavior.Strict))
{
    // No Moq needed — we directly use the provider.
}
foreach (var descriptor in descriptors)
{
    int? version = (descriptor as IVersionedDescriptor)?.Version;
    var fromRef = new DescriptorRef(descriptor.Namespace, descriptor.Id, version);

    var relationships = _relationshipProvider.GetRelationships(descriptor);
    foreach (var rel in relationships)
    {
        var edge = new DescriptorEdge
        {
            Index = edges.Count,
            From = rel.From,
            To = rel.To,
            Kind = rel.Kind,
            Role = rel.Role,
            SourcePath = rel.SourcePath,
            Strength = rel.Strength,
            IsRuntimeBinding = rel.IsRuntimeBinding
        };
        edges.Add(edge);

        // Populate outgoing edge index on source node
        if (nodes.TryGetValue(rel.From, out var fromNode))
        {
            ((HashSet<int>)fromNode.OutgoingEdgeIndices).Add(edge.Index);
        }

        // Populate incoming edge index on target node (if exists)
        if (nodes.TryGetValue(rel.To, out var toNode))
        {
            ((HashSet<int>)toNode.IncomingEdgeIndices).Add(edge.Index);
        }
    }
}
```

**Important**: Node edge indices are created as `HashSet<int>` in Build() and cast back. But the `DescriptorNode` record exposes `IReadOnlySet<int>`. We need to freeze them before creating the snapshot.

Add a freeze step before constructing the snapshot:

```csharp
// Freeze node edge indices
foreach (var kvp in nodes)
{
    var node = kvp.Value;
    // Create new DescriptorNode with frozen edge indices
    nodes[kvp.Key] = node with
    {
        OutgoingEdgeIndices = node.OutgoingEdgeIndices.ToHashSet(),
        IncomingEdgeIndices = node.IncomingEdgeIndices.ToHashSet()
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~Build_Edge\|FullyQualifiedName~Build_Extracts" 2>&1
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologyBuilderTests.cs framework/src/CrestCreates.Metadata/DescriptorTopologyBuilder.cs
git commit -m "feat(Phase6b): add edge extraction from IDescriptorRelationshipProvider to builder"
```

---

### Task 5: Snapshot — direct and transitive queries (TDD)

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologySnapshotTests.cs`
- Modify: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DescriptorTopologySnapshot.cs`

- [ ] **Step 1: Create test file with helper + direct dependency test**

```csharp
// framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologySnapshotTests.cs
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class DescriptorTopologySnapshotTests
{
    // Helper: build a minimal snapshot from raw data
    private static DescriptorTopologySnapshot CreateSnapshot(
        (DescriptorRef Ref, DescriptorKind Kind, string Name)[] nodeDefs,
        (int Index, DescriptorRef From, DescriptorRef To, RelationshipKind Kind,
         string? Role, RelationshipStrength Strength)[] edgeDefs)
    {
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>();
        foreach (var def in nodeDefs)
        {
            nodes[def.Ref] = new DescriptorNode
            {
                Ref = def.Ref,
                Kind = def.Kind,
                Name = def.Name,
                State = DescriptorState.Active,
                OutgoingEdgeIndices = new HashSet<int>(),
                IncomingEdgeIndices = new HashSet<int>()
            };
        }

        var edges = new List<DescriptorEdge>();
        foreach (var def in edgeDefs)
        {
            var edge = new DescriptorEdge
            {
                Index = def.Index,
                From = def.From,
                To = def.To,
                Kind = def.Kind,
                Role = def.Role,
                SourcePath = null,
                Strength = def.Strength,
                IsRuntimeBinding = false
            };
            edges.Add(edge);

            if (nodes.TryGetValue(def.From, out var fromNode))
                ((HashSet<int>)fromNode.OutgoingEdgeIndices).Add(def.Index);
            if (nodes.TryGetValue(def.To, out var toNode))
                ((HashSet<int>)toNode.IncomingEdgeIndices).Add(def.Index);
        }

        // Freeze
        foreach (var key in nodes.Keys.ToList())
        {
            nodes[key] = nodes[key] with
            {
                OutgoingEdgeIndices = nodes[key].OutgoingEdgeIndices.ToHashSet(),
                IncomingEdgeIndices = nodes[key].IncomingEdgeIndices.ToHashSet()
            };
        }

        var diagnostics = new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() };
        return new DescriptorTopologySnapshot(
            nodes, edges, diagnostics,
            new(), new(), new(),
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void GetDirectDependencies_Returns_Outgoing()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);
        var c = new DescriptorRef("ns", "C", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Capability, "C") },
            new[]
            {
                (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong),
                (1, a, c, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            });

        var deps = snapshot.GetDirectDependencies(a);
        deps.Should().HaveCount(2);
        deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b, c });
    }

    [Fact]
    public void GetDirectDependents_Returns_Incoming()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);
        var c = new DescriptorRef("ns", "C", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Capability, "C") },
            new[]
            {
                (0, a, c, RelationshipKind.Uses, null, RelationshipStrength.Strong),
                (1, b, c, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            });

        var deps = snapshot.GetDirectDependents(c);
        deps.Should().HaveCount(2);
        deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public void GetDirectDependencies_Skips_Missing_Target()
    {
        var a = new DescriptorRef("ns", "A", null);
        var missing = new DescriptorRef("ns", "Missing", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            new[]
            {
                (0, a, missing, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            });

        var deps = snapshot.GetDirectDependencies(a);
        deps.Should().BeEmpty(); // Missing target → silently skipped
    }
}
```

- [ ] **Step 2: Run test — should fail (NotImplementedException)**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~GetDirect" 2>&1
```

Expected: FAIL with NotImplementedException.

- [ ] **Step 3: Implement GetDirectDependencies and GetDirectDependents**

Replace the stub methods in `DescriptorTopologySnapshot.cs`:

```csharp
public IReadOnlyList<DescriptorNode> GetDirectDependencies(DescriptorRef of)
{
    if (!_nodes.TryGetValue(of, out var node))
        return Array.Empty<DescriptorNode>();

    return node.OutgoingEdgeIndices
        .Select(i => _edges[i])
        .Select(e => _nodes.TryGetValue(e.To, out var target) ? target : null)
        .Where(n => n is not null)
        .Select(n => n!)
        .ToList().AsReadOnly();
}

public IReadOnlyList<DescriptorNode> GetDirectDependents(DescriptorRef of)
{
    if (!_nodes.TryGetValue(of, out var node))
        return Array.Empty<DescriptorNode>();

    return node.IncomingEdgeIndices
        .Select(i => _edges[i])
        .Select(e => _nodes.TryGetValue(e.From, out var source) ? source : null)
        .Where(n => n is not null)
        .Select(n => n!)
        .ToList().AsReadOnly();
}
```

- [ ] **Step 4: Run test to verify PASS**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~GetDirect" 2>&1
```

Expected: PASS.

- [ ] **Step 5: Add transitive query tests**

```csharp
// Add to DescriptorTopologySnapshotTests.cs
[Fact]
public void GetTransitiveDependencies_Defaults_Strong_Only()
{
    var a = new DescriptorRef("ns", "A", null);
    var b = new DescriptorRef("ns", "B", null);
    var c = new DescriptorRef("ns", "C", null);

    var snapshot = CreateSnapshot(
        new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Capability, "C") },
        new[]
        {
            (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            (1, b, c, RelationshipKind.References, null, RelationshipStrength.Weak),
        });

    var deps = snapshot.GetTransitiveDependencies(a);
    deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b }); // Only B, not C (Weak edge)
}

[Fact]
public void GetTransitiveDependencies_IncludeWeak()
{
    var a = new DescriptorRef("ns", "A", null);
    var b = new DescriptorRef("ns", "B", null);
    var c = new DescriptorRef("ns", "C", null);

    var snapshot = CreateSnapshot(
        new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Capability, "C") },
        new[]
        {
            (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            (1, b, c, RelationshipKind.References, null, RelationshipStrength.Weak),
        });

    var deps = snapshot.GetTransitiveDependencies(a, includeWeak: true);
    deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b, c });
}

[Fact]
public void GetTransitiveDependents_Direction_Correct()
{
    var a = new DescriptorRef("ns", "A", null);
    var b = new DescriptorRef("ns", "B", null);
    var c = new DescriptorRef("ns", "C", null);

    var snapshot = CreateSnapshot(
        new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Capability, "C") },
        new[]
        {
            (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            (1, b, c, RelationshipKind.Uses, null, RelationshipStrength.Strong),
        });

    var deps = snapshot.GetTransitiveDependents(c);
    deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b, a }); // reversed: C ← B ← A
}

[Fact]
public void Transitive_Cycle_Safe()
{
    var a = new DescriptorRef("ns", "A", null);
    var b = new DescriptorRef("ns", "B", null);

    var snapshot = CreateSnapshot(
        new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B") },
        new[]
        {
            (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            (1, b, a, RelationshipKind.Uses, null, RelationshipStrength.Strong),
        });

    var deps = snapshot.GetTransitiveDependencies(a);
    deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b }); // Cycle terminates
}
```

- [ ] **Step 6: Run tests — should fail**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~GetTransitive\|FullyQualifiedName~Transitive_Cycle" 2>&1
```

Expected: FAIL.

- [ ] **Step 7: Implement transitive queries**

```csharp
public IReadOnlySet<DescriptorNode> GetTransitiveDependencies(
    DescriptorRef of, bool includeWeak = false)
{
    return BfsTraverse(of, followOutgoing: true, includeWeak);
}

public IReadOnlySet<DescriptorNode> GetTransitiveDependents(
    DescriptorRef of, bool includeWeak = false)
{
    return BfsTraverse(of, followOutgoing: false, includeWeak);
}

private HashSet<DescriptorNode> BfsTraverse(
    DescriptorRef start, bool followOutgoing, bool includeWeak)
{
    var visited = new HashSet<DescriptorRef>();
    var result = new HashSet<DescriptorNode>();
    var queue = new Queue<DescriptorRef>();

    if (!_nodes.ContainsKey(start))
        return result;

    queue.Enqueue(start);
    visited.Add(start);

    while (queue.Count > 0)
    {
        var current = queue.Dequeue();
        if (!_nodes.TryGetValue(current, out var currentNode))
            continue;

        // Add to result (skip the start node itself)
        if (!current.Equals(start))
            result.Add(currentNode);

        var edgeIndices = followOutgoing
            ? currentNode.OutgoingEdgeIndices
            : currentNode.IncomingEdgeIndices;

        foreach (var idx in edgeIndices)
        {
            var edge = _edges[idx];

            if (!includeWeak && edge.Strength == RelationshipStrength.Weak)
                continue;

            var nextRef = followOutgoing ? edge.To : edge.From;

            if (_nodes.ContainsKey(nextRef) && visited.Add(nextRef))
                queue.Enqueue(nextRef);
        }
    }

    return result;
}
```

- [ ] **Step 8: Run tests to verify PASS**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~GetTransitive\|FullyQualifiedName~Transitive_Cycle\|FullyQualifiedName~GetDirect" 2>&1
```

Expected: All PASS.

- [ ] **Step 9: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologySnapshotTests.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DescriptorTopologySnapshot.cs
git commit -m "feat(Phase6b): implement direct and transitive query methods on DescriptorTopologySnapshot"
```

---

### Task 6: Snapshot — consumer index (TDD)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologyBuilderTests.cs` (consumer index population)
- Modify: `framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologySnapshotTests.cs` (consumer index query)
- Modify: `framework/src/CrestCreates.Metadata/DescriptorTopologyBuilder.cs`
- Modify: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DescriptorTopologySnapshot.cs`

- [ ] **Step 1: Add consumer index builder tests (population verified via GetConsumers)**

The consumer index is populated during `Build()` and queried via `DescriptorTopologySnapshot.GetConsumers()`. Tests go in the builder test file since population depends on the full build pipeline.

```csharp
// Add to DescriptorTopologyBuilderTests.cs
[Fact]
public void Build_ConsumerIndex_NullVersion_Returns_All()
{
    var target = new DescriptorRef("schema", "User", null);
    var c1 = CreateMockDescriptor("capability", "CreateUser", "CreateUser", DescriptorKind.Capability);
    var c2 = CreateMockDescriptor("form", "UserForm", "UserForm", DescriptorKind.Form);
    var targetDesc = CreateMockDescriptor("schema", "User", "User", DescriptorKind.Schema);

    var mockProvider = new Mock<IDescriptorRelationshipProvider>();
    mockProvider.Setup(p => p.GetRelationships(c1)).Returns(new[]
    {
        new DescriptorRelationship(
            new DescriptorRef("capability", "CreateUser", null), target, RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
    });
    mockProvider.Setup(p => p.GetRelationships(c2)).Returns(new[]
    {
        new DescriptorRelationship(
            new DescriptorRef("form", "UserForm", null), target, RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
    });
    mockProvider.Setup(p => p.GetRelationships(targetDesc)).Returns(Array.Empty<DescriptorRelationship>());

    var builder = new DescriptorTopologyBuilder(mockProvider.Object);
    var snapshot = builder.Build(new IDescriptor[] { targetDesc, c1, c2 });

    var consumers = snapshot.GetConsumers("schema", "User");
    consumers.Should().HaveCount(2);
    consumers.Select(n => n.Ref.Id).Should().BeEquivalentTo(new[] { "CreateUser", "UserForm" });
}

[Fact]
public void Build_ConsumerIndex_ExactVersion_Returns_Exact_Plus_Unpinned()
{
    var targetV2 = new DescriptorRef("schema", "User", 2);
    var cv1 = CreateMockDescriptor("capability", "ExactV1", "EV1", DescriptorKind.Capability);
    var cv2 = CreateMockDescriptor("capability", "ExactV2", "EV2", DescriptorKind.Capability);
    var cUnpinned = CreateMockDescriptor("form", "Unpinned", "UP", DescriptorKind.Form);
    var targetDesc = CreateMockDescriptor("schema", "User", "User", DescriptorKind.Schema);

    var mockProvider = new Mock<IDescriptorRelationshipProvider>();
    mockProvider.Setup(p => p.GetRelationships(cv1)).Returns(new[]
    {
        new(new DescriptorRef("capability", "ExactV1", null), new DescriptorRef("schema", "User", 1), RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
    });
    mockProvider.Setup(p => p.GetRelationships(cv2)).Returns(new[]
    {
        new(new DescriptorRef("capability", "ExactV2", null), targetV2, RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
    });
    mockProvider.Setup(p => p.GetRelationships(cUnpinned)).Returns(new[]
    {
        new(new DescriptorRef("form", "Unpinned", null), new DescriptorRef("schema", "User", null), RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
    });
    mockProvider.Setup(p => p.GetRelationships(targetDesc)).Returns(Array.Empty<DescriptorRelationship>());

    var builder = new DescriptorTopologyBuilder(mockProvider.Object);
    var snapshot = builder.Build(new IDescriptor[] { targetDesc, cv1, cv2, cUnpinned });

    var consumersV2 = snapshot.GetConsumers("schema", "User", version: 2);
    consumersV2.Should().HaveCount(2); // ExactV2 + Unpinned
    consumersV2.Select(n => n.Ref.Id).Should().BeEquivalentTo(new[] { "ExactV2", "Unpinned" });
}

[Fact]
public void Build_ConsumerIndex_No_Match_Returns_Empty()
{
    var desc = CreateMockDescriptor("schema", "User", "User", DescriptorKind.Schema);
    var mockProvider = new Mock<IDescriptorRelationshipProvider>();
    mockProvider.Setup(p => p.GetRelationships(desc)).Returns(Array.Empty<DescriptorRelationship>());

    var builder = new DescriptorTopologyBuilder(mockProvider.Object);
    var snapshot = builder.Build(new IDescriptor[] { desc });

    snapshot.GetConsumers("schema", "NoSuch").Should().BeEmpty();
}
```

- [ ] **Step 2: Run tests — should fail (consumer index not populated)**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~ConsumerIndex" 2>&1
```

Expected: FAIL.

- [ ] **Step 3: Implement consumer index population in builder**

Add Phase 4 to `Build()`, between edge extraction and diagnostics:

```csharp
// Phase 4: Build consumer index
var consumersByIdentity = new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>();
var consumersByExactVersion = new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>();
var consumersByUnpinnedVersion = new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>();

foreach (var edge in edges)
{
    var identity = new DescriptorIdentity(edge.To.Namespace, edge.To.Id);

    if (!consumersByIdentity.ContainsKey(identity))
        consumersByIdentity[identity] = new();
    consumersByIdentity[identity].Add((edge.From, edge));

    if (edge.To.Version.HasValue)
    {
        var key = (identity, edge.To.Version.Value);
        if (!consumersByExactVersion.ContainsKey(key))
            consumersByExactVersion[key] = new();
        consumersByExactVersion[key].Add((edge.From, edge));
    }
    else
    {
        if (!consumersByUnpinnedVersion.ContainsKey(identity))
            consumersByUnpinnedVersion[identity] = new();
        consumersByUnpinnedVersion[identity].Add((edge.From, edge));
    }
}
```

- [ ] **Step 4: Implement GetConsumers on snapshot**

```csharp
public IReadOnlyList<DescriptorNode> GetConsumers(
    string ns, string id, int? version = null)
{
    var identity = new DescriptorIdentity(ns, id);

    List<(DescriptorRef Consumer, DescriptorEdge Edge)> entries;

    if (version == null)
    {
        if (!_consumersByIdentity.TryGetValue(identity, out var all))
            return Array.Empty<DescriptorNode>();
        entries = all;
    }
    else
    {
        entries = new();
        if (_consumersByExactVersion.TryGetValue((identity, version.Value), out var exact))
            entries.AddRange(exact);
        if (_consumersByUnpinnedVersion.TryGetValue(identity, out var unpinned))
            entries.AddRange(unpinned);
    }

    return entries
        .Select(e => _nodes.TryGetValue(e.Consumer, out var node) ? node : null)
        .Where(n => n is not null)
        .Select(n => n!)
        .Distinct()
        .ToList().AsReadOnly();
}
```

- [ ] **Step 5: Run tests to verify PASS**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~ConsumerIndex\|FullyQualifiedName~GetConsumers" 2>&1
```

Expected: All PASS.

- [ ] **Step 6: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/ framework/src/CrestCreates.Metadata/DescriptorTopologyBuilder.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorTopology/DescriptorTopologySnapshot.cs
git commit -m "feat(Phase6b): implement consumer index with 3-way segmentation and version-aware query"
```

---

### Task 7: Diagnostics (TDD)

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologyDiagnosticsTests.cs`
- Modify: `framework/src/CrestCreates.Metadata/DescriptorTopologyBuilder.cs`

- [ ] **Step 1: Create diagnostics test file**

```csharp
// framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologyDiagnosticsTests.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class DescriptorTopologyDiagnosticsTests
{
    private static IDescriptor MockDesc(string ns, string id, string name, DescriptorKind kind,
        DescriptorState state = DescriptorState.Active, int? version = null)
    {
        var mock = new Mock<IDescriptor>();
        mock.Setup(d => d.Namespace).Returns(ns);
        mock.Setup(d => d.Id).Returns(id);
        mock.Setup(d => d.Name).Returns(name);
        mock.Setup(d => d.Kind).Returns(kind);
        mock.Setup(d => d.State).Returns(state);
        mock.Setup(d => d.ContractHash).Returns("hash");
        mock.Setup(d => d.SupersededById).Returns((string?)null);
        if (version.HasValue)
        {
            mock.As<IVersionedDescriptor>().Setup(v => v.Version).Returns(version.Value);
        }
        return mock.Object;
    }

    [Fact]
    public void Missing_Strong_Target_Error()
    {
        var formDesc = MockDesc("form", "UserForm", "UserForm", DescriptorKind.Form);
        var missingRef = new DescriptorRef("schema", "MissingSchema", null);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(formDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("form", "UserForm", null), missingRef,
                RelationshipKind.Uses, "Schema", "Schema",
                RelationshipStrength.Strong, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { formDesc });

        var diag = snapshot.Diagnostics.All.Should().ContainSingle(d =>
            d.Code == "MISSING_TARGET" && d.Severity == DiagnosticSeverity.Error).Subject;
        diag.Message.Should().Contain("MissingSchema");
    }

    [Fact]
    public void Missing_Weak_Target_Warning()
    {
        var capDesc = MockDesc("capability", "CreateUser", "CreateUser", DescriptorKind.Capability);
        var missingRef = new DescriptorRef("event", "UserCreated", null);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(capDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "CreateUser", null), missingRef,
                RelationshipKind.Produces, null, "Produces",
                RelationshipStrength.Weak, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { capDesc });

        var diag = snapshot.Diagnostics.All.Should().ContainSingle(d =>
            d.Code == "MISSING_TARGET" && d.Severity == DiagnosticSeverity.Warning).Subject;
    }

    [Fact]
    public void Strong_Cycle_Error()
    {
        var a = MockDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = MockDesc("capability", "B", "B", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(a)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(b)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "B", null), new DescriptorRef("capability", "A", null),
                RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "STRONG_CYCLE" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Weak_Cycle_No_Error()
    {
        var a = MockDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = MockDesc("capability", "B", "B", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(a)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.References, null, "SupersededBy", RelationshipStrength.Weak, false)
        });
        mockProvider.Setup(p => p.GetRelationships(b)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "B", null), new DescriptorRef("capability", "A", null),
                RelationshipKind.References, null, "SupersededBy", RelationshipStrength.Weak, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "STRONG_CYCLE");
    }

    [Fact]
    public void Orphan_Warning()
    {
        var orphan = MockDesc("form", "OrphanForm", "OrphanForm", DescriptorKind.Form, DescriptorState.Active);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(orphan)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { orphan });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "ORPHAN" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Orphan_Draft_Excluded()
    {
        var draft = MockDesc("form", "DraftForm", "DraftForm", DescriptorKind.Form, DescriptorState.Draft);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(draft)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { draft });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "ORPHAN");
    }

    [Fact]
    public void Exact_Duplicate_Warning()
    {
        var a = MockDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = MockDesc("capability", "B", "B", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(a)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.Uses, "Input", "InputSchema", RelationshipStrength.Strong, false),
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.Uses, "Input", "InputSchema", RelationshipStrength.Strong, false),
        });
        mockProvider.Setup(p => p.GetRelationships(b)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "EXACT_DUPLICATE" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Different_Role_Not_Duplicate()
    {
        var a = MockDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = MockDesc("capability", "B", "B", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(a)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.Uses, "InputSchema", "InputSchema", RelationshipStrength.Strong, false),
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.Uses, "OutputSchema", "OutputSchema", RelationshipStrength.Strong, false),
        });
        mockProvider.Setup(p => p.GetRelationships(b)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "EXACT_DUPLICATE");
    }

    [Fact]
    public void Unsupported_Reference_Warning()
    {
        var wfDesc = MockDesc("workflow", "MyWf", "MyWf", DescriptorKind.Workflow);
        var swRef = new DescriptorRef("workflow", "SubWf", null);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(wfDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("workflow", "MyWf", null), swRef,
                RelationshipKind.References, RelationshipRoles.SubWorkflowStep, "Steps",
                RelationshipStrength.Weak, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new IDescriptor[] { wfDesc, MockDesc("workflow", "SubWf", "SubWf", DescriptorKind.Workflow) });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "UNSUPPORTED_REFERENCE" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Unsupported_Not_Triggered_By_Weak_Alone()
    {
        var capDesc = MockDesc("capability", "CreateUser", "CreateUser", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(capDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "CreateUser", null), new DescriptorRef("capability", "OldCap", null),
                RelationshipKind.DependsOn, RelationshipRoles.SupersededBy, "SupersededById",
                RelationshipStrength.Weak, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new IDescriptor[] { capDesc, MockDesc("capability", "OldCap", "OldCap", DescriptorKind.Capability) });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "UNSUPPORTED_REFERENCE");
    }
}
```

- [ ] **Step 2: Run tests — should fail (diagnostics not implemented)**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~DescriptorTopologyDiagnosticsTests" 2>&1
```

Expected: All FAIL.

- [ ] **Step 3: Implement diagnostics in builder**

Add Phase 5 to `Build()`, after consumer index population. Refer to the complete implementation below:

```csharp
// Phase 5: Diagnostics
var diagnosticList = new List<DescriptorTopologyDiagnostic>();

// 5a. MISSING_TARGET
foreach (var edge in edges)
{
    if (!nodes.ContainsKey(edge.To))
    {
        var severity = edge.Strength == RelationshipStrength.Strong
            ? DiagnosticSeverity.Error
            : DiagnosticSeverity.Warning;
        diagnosticList.Add(new DescriptorTopologyDiagnostic(
            severity,
            "MISSING_TARGET",
            $"Edge {edge.From.FullId} --[{edge.Kind}]--> {edge.To.FullId}: target descriptor not found. " +
            $"Role='{edge.Role}', SourcePath='{edge.SourcePath}', Strength={edge.Strength}.",
            edge.From,
            new[] { edge.To }));
    }
}

// 5b. STRONG_CYCLE — DFS on Strong edges where both From and To exist in nodes
var visited = new HashSet<DescriptorRef>();
var inStack = new HashSet<DescriptorRef>();
var parent = new Dictionary<DescriptorRef, DescriptorRef>();

foreach (var nodeRef in nodes.Keys)
{
    if (!visited.Contains(nodeRef))
        DfsCycleDetect(nodeRef, nodes, edges, visited, inStack, parent, diagnosticList);
}

// 5c. ORPHAN
foreach (var node in nodes.Values)
{
    if (node.IncomingEdgeIndices.Count == 0
        && node.State != DescriptorState.Draft
        && node.State != DescriptorState.Removed)
    {
        diagnosticList.Add(new DescriptorTopologyDiagnostic(
            DiagnosticSeverity.Warning,
            "ORPHAN",
            $"Descriptor '{node.Ref.FullId}' ({node.Kind}) has no consumers.",
            node.Ref,
            null));
    }
}

// 5d. EXACT_DUPLICATE — full semantic key
var seen = new HashSet<(DescriptorRef, DescriptorRef, RelationshipKind, string?, string?, RelationshipStrength, bool)>();
foreach (var edge in edges)
{
    var key = (edge.From, edge.To, edge.Kind, edge.Role, edge.SourcePath, edge.Strength, edge.IsRuntimeBinding);
    if (!seen.Add(key))
    {
        diagnosticList.Add(new DescriptorTopologyDiagnostic(
            DiagnosticSeverity.Warning,
            "EXACT_DUPLICATE",
            $"Duplicate edge: {edge.From.FullId} --[{edge.Kind}]--> {edge.To.FullId} " +
            $"(Role='{edge.Role}', SourcePath='{edge.SourcePath}', Strength={edge.Strength})",
            edge.From,
            new[] { edge.To }));
    }
}

// 5e. UNSUPPORTED_REFERENCE — explicit whitelist
var knownUnsupported = new HashSet<(string Role, RelationshipKind Kind)>
{
    (RelationshipRoles.SubWorkflowStep, RelationshipKind.References),
};
foreach (var edge in edges)
{
    if (edge.Role is not null && knownUnsupported.Contains((edge.Role, edge.Kind)))
    {
        diagnosticList.Add(new DescriptorTopologyDiagnostic(
            DiagnosticSeverity.Warning,
            "UNSUPPORTED_REFERENCE",
            $"Edge '{edge.Role}' ({edge.Kind}) from {edge.From.FullId} to {edge.To.FullId} " +
            $"is not supported at runtime.",
            edge.From,
            new[] { edge.To }));
    }
}

var diagnostics = new DescriptorTopologyDiagnostics
{
    All = diagnosticList.AsReadOnly()
};
```

Also add the cycle detection helper method to the builder class:

```csharp
private void DfsCycleDetect(
    DescriptorRef current,
    Dictionary<DescriptorRef, DescriptorNode> nodes,
    List<DescriptorEdge> edges,
    HashSet<DescriptorRef> visited,
    HashSet<DescriptorRef> inStack,
    Dictionary<DescriptorRef, DescriptorRef> parent,
    List<DescriptorTopologyDiagnostic> diagnostics)
{
    visited.Add(current);
    inStack.Add(current);

    if (nodes.TryGetValue(current, out var node))
    {
        foreach (var edgeIdx in node.OutgoingEdgeIndices)
        {
            var edge = edges[edgeIdx];

            // Only Strong edges, only when target exists
            if (edge.Strength != RelationshipStrength.Strong)
                continue;
            if (!nodes.ContainsKey(edge.To))
                continue;

            if (!visited.Contains(edge.To))
            {
                parent[edge.To] = current;
                DfsCycleDetect(edge.To, nodes, edges, visited, inStack, parent, diagnostics);
            }
            else if (inStack.Contains(edge.To))
            {
                // Found a cycle: reconstruct path
                var path = new List<DescriptorRef> { edge.To };
                var p = current;
                while (!p.Equals(edge.To))
                {
                    path.Add(p);
                    if (!parent.TryGetValue(p, out p))
                        break;
                }
                path.Add(edge.To);
                path.Reverse();

                diagnostics.Add(new DescriptorTopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    "STRONG_CYCLE",
                    $"Strong dependency cycle detected: {string.Join(" → ", path.Select(r => r.FullId))}",
                    current,
                    path.AsReadOnly()));
            }
        }
    }

    inStack.Remove(current);
}
```

- [ ] **Step 4: Run tests to verify PASS**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~DescriptorTopologyDiagnosticsTests" 2>&1
```

Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorTopologyDiagnosticsTests.cs framework/src/CrestCreates.Metadata/DescriptorTopologyBuilder.cs
git commit -m "feat(Phase6b): implement all 5 diagnostics — MISSING_TARGET (strength-aware), STRONG_CYCLE, ORPHAN, EXACT_DUPLICATE, UNSUPPORTED_REFERENCE"
```

---

### Task 8: DescriptorDependencyGraphAdapter (TDD)

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorDependencyGraphAdapterTests.cs`
- Create: `framework/src/CrestCreates.Metadata/DescriptorDependencyGraphAdapter.cs`

- [ ] **Step 1: Create adapter test file**

```csharp
// framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorDependencyGraphAdapterTests.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class DescriptorDependencyGraphAdapterTests
{
    [Fact]
    public void Adapter_GetDependencies_Maps_Correctly()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);

        var mockBuilder = new Mock<IDescriptorTopologyBuilder>();
        var snapshot = CreateSimpleSnapshot(
            new[] { (a, "A"), (b, "B") },
            new[] { (a, b, RelationshipKind.Uses) });
        mockBuilder.Setup(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>())).Returns(snapshot);

        var adapter = new DescriptorDependencyGraphAdapter(mockBuilder.Object, Array.Empty<IDescriptor>());
        var deps = adapter.GetDependencies("ns.A"); // FullId = Namespace.Id

        deps.Should().HaveCount(1);
        deps[0].SourceId.Should().Be("ns.A");
        deps[0].TargetId.Should().Be("ns.B");
        deps[0].Kind.Should().Be(DescriptorDependencyKind.Uses);
    }

    [Fact]
    public void Adapter_AddEdge_Throws_NotSupportedException()
    {
        var mockBuilder = new Mock<IDescriptorTopologyBuilder>();
        mockBuilder.Setup(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>())).Returns(
            CreateSimpleSnapshot(
    Array.Empty<(DescriptorRef, string)>(),
    Array.Empty<(DescriptorRef, DescriptorRef, RelationshipKind)>()));

        var adapter = new DescriptorDependencyGraphAdapter(mockBuilder.Object, Array.Empty<IDescriptor>());
        var act = () => adapter.AddEdge("a", "b", DescriptorDependencyKind.Uses);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Adapter_KindMapping_All_Six_Covered()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);

        var testCases = new (RelationshipKind Rel, DescriptorDependencyKind Dep)[]
        {
            (RelationshipKind.Produces, DescriptorDependencyKind.Produces),
            (RelationshipKind.Consumes, DescriptorDependencyKind.Consumes),
            (RelationshipKind.DependsOn, DescriptorDependencyKind.References),
            (RelationshipKind.References, DescriptorDependencyKind.References),
            (RelationshipKind.Uses, DescriptorDependencyKind.Uses),
            (RelationshipKind.Triggers, DescriptorDependencyKind.Triggers),
        };

        foreach (var tc in testCases)
        {
            var mockBuilder = new Mock<IDescriptorTopologyBuilder>();
            var snapshot = CreateSimpleSnapshot(
                new[] { (a, "A"), (b, "B") },
                new[] { (a, b, tc.Rel) });
            mockBuilder.Setup(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>())).Returns(snapshot);

            var adapter = new DescriptorDependencyGraphAdapter(mockBuilder.Object, Array.Empty<IDescriptor>());
            var deps = adapter.GetDependencies("ns.A");
            deps[0].Kind.Should().Be(tc.Dep, $"RelationshipKind.{tc.Rel} should map to DescriptorDependencyKind.{tc.Dep}");
        }
    }

    // Helper
    private static DescriptorTopologySnapshot CreateSimpleSnapshot(
        (DescriptorRef Ref, string Name)[] nodeDefs,
        (DescriptorRef From, DescriptorRef To, RelationshipKind Kind)[] edgeDefs)
    {
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>();
        foreach (var def in nodeDefs)
        {
            nodes[def.Ref] = new DescriptorNode
            {
                Ref = def.Ref, Kind = DescriptorKind.Capability, Name = def.Name,
                State = DescriptorState.Active,
                OutgoingEdgeIndices = new HashSet<int>(),
                IncomingEdgeIndices = new HashSet<int>()
            };
        }

        var edges = new List<DescriptorEdge>();
        for (int i = 0; i < edgeDefs.Length; i++)
        {
            var def = edgeDefs[i];
            var edge = new DescriptorEdge
            {
                Index = i, From = def.From, To = def.To, Kind = def.Kind,
                Role = null, SourcePath = null, Strength = RelationshipStrength.Strong,
                IsRuntimeBinding = false
            };
            edges.Add(edge);
            if (nodes.TryGetValue(def.From, out var fn))
                ((HashSet<int>)fn.OutgoingEdgeIndices).Add(i);
            if (nodes.TryGetValue(def.To, out var tn))
                ((HashSet<int>)tn.IncomingEdgeIndices).Add(i);
        }

        foreach (var key in nodes.Keys.ToList())
        {
            nodes[key] = nodes[key] with
            {
                OutgoingEdgeIndices = nodes[key].OutgoingEdgeIndices.ToHashSet(),
                IncomingEdgeIndices = nodes[key].IncomingEdgeIndices.ToHashSet()
            };
        }

        return new DescriptorTopologySnapshot(
            nodes, edges,
            new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() },
            new(), new(), new(), DateTimeOffset.UtcNow);
    }
}
```

- [ ] **Step 2: Run tests — should fail (adapter not created yet)**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~Adapter" 2>&1
```

Expected: FAIL.

- [ ] **Step 3: Create DescriptorDependencyGraphAdapter.cs**

```csharp
// framework/src/CrestCreates.Metadata/DescriptorDependencyGraphAdapter.cs
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata;

/// <summary>
/// Compatibility adapter: wraps IDescriptorTopologyBuilder → IDescriptorDependencyGraph.
/// Builds snapshot once from the provided descriptor inventory.
/// Intended ONLY for DescriptorCatalog backward compat.
/// New code uses DescriptorTopologySnapshot directly.
/// </summary>
public sealed class DescriptorDependencyGraphAdapter : IDescriptorDependencyGraph
{
    private readonly IDescriptorTopologyBuilder _builder;
    private readonly IReadOnlyList<IDescriptor> _descriptors;
    private DescriptorTopologySnapshot? _snapshot;

    public DescriptorDependencyGraphAdapter(
        IDescriptorTopologyBuilder builder,
        IReadOnlyList<IDescriptor> descriptors)
    {
        _builder = builder;
        _descriptors = descriptors;
    }

    private DescriptorTopologySnapshot Snapshot =>
        _snapshot ??= _builder.Build(_descriptors);

    public IReadOnlyList<DependencyEdge> GetDependencies(string descriptorId)
    {
        var node = FindNodeById(descriptorId);
        if (node is null) return Array.Empty<DependencyEdge>();

        var deps = Snapshot.GetDirectDependencies(node.Ref);
        return deps.Select(n =>
        {
            var edge = Snapshot.Edges.First(e => e.From.Equals(node.Ref) && e.To.Equals(n.Ref));
            return new DependencyEdge
            {
                SourceId = edge.From.FullId,
                TargetId = edge.To.FullId,
                Kind = MapKind(edge.Kind)
            };
        }).ToList().AsReadOnly();
    }

    public IReadOnlyList<DependencyEdge> GetDependents(string descriptorId)
    {
        var node = FindNodeById(descriptorId);
        if (node is null) return Array.Empty<DependencyEdge>();

        var deps = Snapshot.GetDirectDependents(node.Ref);
        return deps.Select(n =>
        {
            var edge = Snapshot.Edges.First(e => e.To.Equals(node.Ref) && e.From.Equals(n.Ref));
            return new DependencyEdge
            {
                SourceId = edge.From.FullId,
                TargetId = edge.To.FullId,
                Kind = MapKind(edge.Kind)
            };
        }).ToList().AsReadOnly();
    }

    public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion)
    {
        var node = FindNodeById(descriptorId);
        if (node is null)
        {
            return new ImpactReport
            {
                DescriptorId = descriptorId,
                FromVersion = fromVersion,
                ToVersion = toVersion,
                AffectedDependents = Array.Empty<DependencyEdge>()
            };
        }

        var transitiveConsumers = Snapshot.GetTransitiveDependents(node.Ref);
        var affected = transitiveConsumers.Select(n =>
        {
            var edge = Snapshot.Edges.FirstOrDefault(e => e.To.Equals(node.Ref) && e.From.Equals(n.Ref));
            return new DependencyEdge
            {
                SourceId = n.Ref.FullId,
                TargetId = node.Ref.FullId,
                Kind = edge is not null ? MapKind(edge.Kind) : DescriptorDependencyKind.References
            };
        }).ToList();

        return new ImpactReport
        {
            DescriptorId = descriptorId,
            FromVersion = fromVersion,
            ToVersion = toVersion,
            AffectedDependents = affected
        };
    }

    public void AddEdge(string sourceId, string targetId, DescriptorDependencyKind kind)
        => throw new NotSupportedException(
            "AddEdge is no longer supported. " +
            "Edges are computed from descriptor relationships via IDescriptorTopologyBuilder.");

    // Id-only lookup — adapter-internal, not on public snapshot API
    private DescriptorNode? FindNodeById(string descriptorId)
    {
        return Snapshot.Nodes.Values.FirstOrDefault(n => n.Ref.FullId == descriptorId);
    }

    private static DescriptorDependencyKind MapKind(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Produces   => DescriptorDependencyKind.Produces,
        RelationshipKind.Consumes   => DescriptorDependencyKind.Consumes,
        RelationshipKind.DependsOn  => DescriptorDependencyKind.References,
        RelationshipKind.References => DescriptorDependencyKind.References,
        RelationshipKind.Uses       => DescriptorDependencyKind.Uses,
        RelationshipKind.Triggers   => DescriptorDependencyKind.Triggers,
        _ => DescriptorDependencyKind.References
    };
}
```

- [ ] **Step 4: Run tests to verify PASS**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~Adapter" 2>&1
```

Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorTopology/DescriptorDependencyGraphAdapterTests.cs framework/src/CrestCreates.Metadata/DescriptorDependencyGraphAdapter.cs
git commit -m "feat(Phase6b): add DescriptorDependencyGraphAdapter for backward compat with DescriptorCatalog"
```

---

### Task 9: DI registration + regression gate

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs`

- [ ] **Step 1: Add AddTopologyKernel() to DI extensions**

```csharp
// Add to MetadataServiceCollectionExtensions.cs
public static IServiceCollection AddTopologyKernel(this IServiceCollection services)
{
    services.TryAddSingleton<IDescriptorTopologyBuilder, DescriptorTopologyBuilder>();
    return services;
}
```

- [ ] **Step 2: Build to verify no compile errors**

```bash
dotnet build 2>&1
```

Expected: 0 errors.

- [ ] **Step 3: Run ALL tests to verify zero regressions**

```bash
dotnet test 2>&1
```

Expected: ALL tests pass. Key suites:
- Metadata.Tests (95 existing + ~25 new Phase 6b tests)
- Form.Tests (35)
- Capability.Tests (120)
- Event.Tests (36)
- HumanTask.Tests (47)
- Workflow.Tests (63)

- [ ] **Step 4: Run diagnostics on changed files**

```bash
# Check for LSP diagnostics on new files
```

Expected: 0 errors, 0 warnings on new files.

- [ ] **Step 5: Commit final changes**

```bash
git add framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs
git commit -m "feat(Phase6b): add AddTopologyKernel() DI registration and pass regression gate"
```

---

## Post-Implementation Checklist

- [ ] All 9 tasks committed
- [ ] `dotnet build` — 0 errors
- [ ] `dotnet test` — all tests pass, zero regressions
- [ ] `DescriptorDependencyGraph.cs` and `DependencyGraphProvider.cs` in `99_RecycleBin/`
- [ ] `DescriptorCatalog` unchanged (still injects `IDescriptorDependencyGraph`)
- [ ] No `[Obsolete]` attributes on `DependencyEdge` or `DescriptorDependencyKind`
- [ ] `IDescriptorTopologyBuilder` registered as singleton via `AddTopologyKernel()`
- [ ] Adapter NOT registered in DI (constructed explicitly)
