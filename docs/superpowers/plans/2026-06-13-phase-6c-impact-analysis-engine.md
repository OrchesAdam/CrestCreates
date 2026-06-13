# Phase 6c — Impact Analysis Engine: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `IDescriptorImpactAnalyzer` + `IDescriptorChangeSetBuilder` that consume `DescriptorTopologySnapshot` and produce deterministic `DescriptorImpactAnalysisReport` with affected descriptors, impact paths, structural severity, runtime areas, and diagnostics.

**Architecture:** Analyzer builds three internal indices from `topology.Nodes` + `topology.Edges` (exact lookup, identity grouping, fan-out-aware incoming index), then runs BFS upstream from changed descriptors. Severity is computed per-path with table base + transitive attenuation + per-terminal-segment runtime boost. `DescriptorChangeSetBuilder` diffs two `IReadOnlyList<IDescriptor>` inventories. Both are stateless singletons. No dependency on `DescriptorNode.IncomingEdgeIndices`. No legacy `DescriptorCatalog.AnalyzeImpact()` involvement.

**Tech Stack:** .NET 10, C# records/enums/interfaces only (AoT-friendly), xUnit + FluentAssertions, no runtime reflection.

---

## Task 1: Abstractions Type Scaffolding — Enums

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorChangeKind.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactSeverity.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactRuntimeArea.cs`

- [ ] **Step 1: Write `DescriptorChangeKind.cs`**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public enum DescriptorChangeKind
{
    Added,
    Updated,
    Deprecated,
    Removed,
    Activated,
    StateChanged,
    ContractHashChanged
}
```

- [ ] **Step 2: Write `DescriptorImpactSeverity.cs`**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public enum DescriptorImpactSeverity
{
    None,
    Info,
    Low,
    Medium,
    High,
    Critical
}
```

- [ ] **Step 3: Write `DescriptorImpactRuntimeArea.cs`**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public enum DescriptorImpactRuntimeArea
{
    Metadata,
    Schema,
    Form,
    Capability,
    Event,
    Workflow,
    HumanTask,
    RuntimeBinding
}
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorChangeKind.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactSeverity.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactRuntimeArea.cs
git commit -m "feat(6c): add DescriptorImpact enums (ChangeKind, Severity, RuntimeArea)"
```

---

## Task 2: Abstractions Type Scaffolding — Change Set Types

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorChange.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorChangeSet.cs`

- [ ] **Step 1: Write `DescriptorChange.cs`**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorChange
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorChangeKind Kind { get; init; }
    public DescriptorState? BeforeState { get; init; }
    public DescriptorState? AfterState { get; init; }
    public string? BeforeContractHash { get; init; }
    public string? AfterContractHash { get; init; }
    public string? Reason { get; init; }
}
```

- [ ] **Step 2: Write `DescriptorChangeSet.cs`**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorChangeSet
{
    public required IReadOnlyList<DescriptorChange> Changes { get; init; }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorChange.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorChangeSet.cs
git commit -m "feat(6c): add DescriptorChange and DescriptorChangeSet records"
```

---

## Task 3: Abstractions Type Scaffolding — Impact Path Types

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactPathSegment.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactPath.cs`

- [ ] **Step 1: Write `DescriptorImpactPathSegment.cs`**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorImpactPathSegment
{
    public required DescriptorRef From { get; init; }
    public required DescriptorRef To { get; init; }
    public required RelationshipKind Kind { get; init; }
    public required RelationshipStrength Strength { get; init; }
    public required bool IsRuntimeBinding { get; init; }
    public string? Role { get; init; }
    public string? SourcePath { get; init; }
}
```

- [ ] **Step 2: Write `DescriptorImpactPath.cs`**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorImpactPath
{
    public required DescriptorRef SourceChange { get; init; }
    public required DescriptorRef Affected { get; init; }
    public required IReadOnlyList<DescriptorImpactPathSegment> Segments { get; init; }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactPathSegment.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactPath.cs
git commit -m "feat(6c): add DescriptorImpactPathSegment and DescriptorImpactPath records"
```

---

## Task 4: Abstractions Type Scaffolding — Report Types

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/AffectedDescriptor.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactDiagnostic.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactAnalysisReport.cs`

- [ ] **Step 1: Write `AffectedDescriptor.cs`**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record AffectedDescriptor
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorImpactSeverity Severity { get; init; }
    public required IReadOnlyList<DescriptorImpactRuntimeArea> RuntimeAreas { get; init; }
    public required IReadOnlyList<DescriptorImpactPath> Paths { get; init; }
    public string? Reason { get; init; }
    public string? SuggestedAction { get; init; }
}
```

- [ ] **Step 2: Write `DescriptorImpactDiagnostic.cs`**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorImpactDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DescriptorRef? Subject,
    IReadOnlyList<DescriptorRef>? RelatedRefs);
```

- [ ] **Step 3: Write `DescriptorImpactAnalysisReport.cs`**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorImpactAnalysisReport
{
    public required DescriptorChangeSet ChangeSet { get; init; }
    public required IReadOnlyList<AffectedDescriptor> AffectedDescriptors { get; init; }
    public required IReadOnlyList<DescriptorImpactPath> Paths { get; init; }
    public required DescriptorImpactSeverity MaxSeverity { get; init; }
    public required IReadOnlyList<DescriptorImpactDiagnostic> Diagnostics { get; init; }
}
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/AffectedDescriptor.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactDiagnostic.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactAnalysisReport.cs
git commit -m "feat(6c): add AffectedDescriptor, DescriptorImpactDiagnostic, and DescriptorImpactAnalysisReport"
```

---

## Task 5: Abstractions Type Scaffolding — Options & Interfaces

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactAnalysisOptions.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/IDescriptorImpactAnalyzer.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/IDescriptorChangeSetBuilder.cs`

- [ ] **Step 1: Write `DescriptorImpactAnalysisOptions.cs`**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorImpactAnalysisOptions
{
    public bool IncludeWeakRelationships { get; init; } = true;
    public bool IncludeAdvisoryRelationships { get; init; } = true;
    public int? MaxDepth { get; init; }
}
```

- [ ] **Step 2: Write `IDescriptorImpactAnalyzer.cs`**

```csharp
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public interface IDescriptorImpactAnalyzer
{
    DescriptorImpactAnalysisReport Analyze(
        DescriptorTopologySnapshot topology,
        DescriptorChangeSet changeSet,
        DescriptorImpactAnalysisOptions? options = null);
}
```

- [ ] **Step 3: Write `IDescriptorChangeSetBuilder.cs`**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public interface IDescriptorChangeSetBuilder
{
    DescriptorChangeSet Build(
        IReadOnlyList<IDescriptor> before,
        IReadOnlyList<IDescriptor> after);
}
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/DescriptorImpactAnalysisOptions.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/IDescriptorImpactAnalyzer.cs framework/src/CrestCreates.Metadata.Abstractions/DescriptorImpact/IDescriptorChangeSetBuilder.cs
git commit -m "feat(6c): add DescriptorImpactAnalysisOptions, IDescriptorImpactAnalyzer, IDescriptorChangeSetBuilder"
```

---

## Task 6: DescriptorChangeSetBuilder — Implementation

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorChangeSetBuilder.cs`

- [ ] **Step 1: Write `DescriptorChangeSetBuilder.cs`**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata;

public sealed class DescriptorChangeSetBuilder : IDescriptorChangeSetBuilder
{
    public DescriptorChangeSet Build(
        IReadOnlyList<IDescriptor> before,
        IReadOnlyList<IDescriptor> after)
    {
        var beforeByRef = before.ToDictionary(
            d => new DescriptorRef(d.Namespace, d.Id, (d as IVersionedDescriptor)?.Version));

        var changes = new List<DescriptorChange>();

        foreach (var d in after)
        {
            var refKey = new DescriptorRef(d.Namespace, d.Id, (d as IVersionedDescriptor)?.Version);

            if (!beforeByRef.TryGetValue(refKey, out var beforeDesc))
            {
                changes.Add(new DescriptorChange
                {
                    Ref = refKey,
                    Kind = DescriptorChangeKind.Added,
                    AfterState = d.State,
                    AfterContractHash = d.ContractHash
                });
                continue;
            }

            // Evaluate top-to-bottom, stop at first match
            DescriptorChangeKind kind;
            DescriptorState? beforeState = beforeDesc.State;
            DescriptorState? afterState = d.State;

            if (afterState == DescriptorState.Removed && beforeState != DescriptorState.Removed)
                kind = DescriptorChangeKind.Removed;
            else if (afterState == DescriptorState.Deprecated && beforeState != DescriptorState.Deprecated)
                kind = DescriptorChangeKind.Deprecated;
            else if (afterState == DescriptorState.Active && beforeState == DescriptorState.Draft)
                kind = DescriptorChangeKind.Activated;
            else if (beforeState != afterState)
                kind = DescriptorChangeKind.StateChanged;
            else if (d.ContractHash != beforeDesc.ContractHash)
                kind = DescriptorChangeKind.ContractHashChanged;
            else if (d.Name != beforeDesc.Name)
                kind = DescriptorChangeKind.Updated;
            else
                continue; // No detectable change

            changes.Add(new DescriptorChange
            {
                Ref = refKey,
                Kind = kind,
                BeforeState = beforeState,
                AfterState = afterState,
                BeforeContractHash = beforeDesc.ContractHash,
                AfterContractHash = d.ContractHash
            });
        }

        // Removed: in before but not in after
        var afterRefs = after.Select(d =>
            new DescriptorRef(d.Namespace, d.Id, (d as IVersionedDescriptor)?.Version))
            .ToHashSet();

        foreach (var kv in beforeByRef)
        {
            if (!afterRefs.Contains(kv.Key))
            {
                changes.Add(new DescriptorChange
                {
                    Ref = kv.Key,
                    Kind = DescriptorChangeKind.Removed,
                    BeforeState = kv.Value.State,
                    BeforeContractHash = kv.Value.ContractHash
                });
            }
        }

        // Deduplicate: one per DescriptorRef, highest priority wins
        var deduped = DeduplicateByPriority(changes);

        return new DescriptorChangeSet { Changes = deduped };
    }

    // Priority: Removed(1) > Deprecated(2) > StateChanged(3) > ContractHashChanged(4) > Updated(5) > Added(6) > Activated(7)
    private static IReadOnlyList<DescriptorChange> DeduplicateByPriority(List<DescriptorChange> changes)
    {
        var result = new Dictionary<DescriptorRef, DescriptorChange>();
        foreach (var c in changes)
        {
            if (!result.TryGetValue(c.Ref, out var existing) || Priority(c.Kind) < Priority(existing.Kind))
                result[c.Ref] = c;
        }
        return result.Values.ToList().AsReadOnly();
    }

    private static int Priority(DescriptorChangeKind kind) => kind switch
    {
        DescriptorChangeKind.Removed => 1,
        DescriptorChangeKind.Deprecated => 2,
        DescriptorChangeKind.StateChanged => 3,
        DescriptorChangeKind.ContractHashChanged => 4,
        DescriptorChangeKind.Updated => 5,
        DescriptorChangeKind.Added => 6,
        DescriptorChangeKind.Activated => 7,
        _ => int.MaxValue
    };
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorChangeSetBuilder.cs
git commit -m "feat(6c): implement DescriptorChangeSetBuilder with state-aware diff and priority dedup"
```

---

## Task 7: DescriptorChangeSetBuilder — Tests

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorImpact/DescriptorChangeSetBuilderTests.cs`

- [ ] **Step 1: Write `DescriptorChangeSetBuilderTests.cs`**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorImpact;

public class DescriptorChangeSetBuilderTests
{
    private readonly DescriptorChangeSetBuilder _builder = new();

    // Simple stub implementing IDescriptor for test use
    private sealed record StubDescriptor(
        string Namespace, string Id, string Name,
        DescriptorKind Kind, DescriptorState State, string ContractHash,
        int? Version = null) : IDescriptor, IVersionedDescriptor
    {
        public string FullId => $"{Namespace}.{Id}";
        public string DefinitionHash => "";
        public string? SupersededById => null;
        int IVersionedDescriptor.Version => Version ?? 0;
    }

    private static DescriptorRef RefOf(IDescriptor d) =>
        new(d.Namespace, d.Id, (d as IVersionedDescriptor)?.Version);

    [Fact]
    public void Added_Descriptor_WhenNotInBefore()
    {
        var after = new IDescriptor[] { new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1") };
        var result = _builder.Build(Array.Empty<IDescriptor>(), after);
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Added);
    }

    [Fact]
    public void Removed_Descriptor_WhenNotInAfter()
    {
        var before = new IDescriptor[] { new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1") };
        var result = _builder.Build(before, Array.Empty<IDescriptor>());
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Removed);
    }

    [Fact]
    public void StateChanged_Detected()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Draft, "hash1");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.StateChanged);
    }

    [Fact]
    public void ContractHashChanged_Detected()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash2");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.ContractHashChanged);
    }

    [Fact]
    public void StateChanged_Priority_Over_ContractHashChanged()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Draft, "hash2");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.StateChanged);
    }

    [Fact]
    public void Deprecated_StateTransition()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Deprecated, "hash1");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Deprecated);
    }

    [Fact]
    public void Removed_StateTransition()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Removed, "hash1");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Removed);
    }

    [Fact]
    public void Activated_StateTransition()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Draft, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Activated);
    }

    [Fact]
    public void Update_StateAndContractUnchanged_OtherFieldsDiffer()
    {
        var d1 = new StubDescriptor("ns", "A", "OldName", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "NewName", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Updated);
    }

    [Fact]
    public void NoChange_WhenIdentical()
    {
        var d = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var result = _builder.Build(new[] { d }, new[] { d });
        result.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Ordering_IsPredictionIndependent()
    {
        // Build from unordered input; verify output is consistent regardless of list order
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "B", "B", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var result1 = _builder.Build(new[] { d1, d2 }, new[] { d1 });
        var result2 = _builder.Build(new[] { d2, d1 }, new[] { d1 });
        result1.Changes.Should().HaveCount(1).And.ContainSingle(c => c.Ref.Id == "B");
        result2.Changes.Should().HaveCount(1).And.ContainSingle(c => c.Ref.Id == "B");
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~DescriptorChangeSetBuilderTests"`
Expected: All 11 tests PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorImpact/DescriptorChangeSetBuilderTests.cs
git commit -m "test(6c): add DescriptorChangeSetBuilder tests (11 tests)"
```

---

## Task 8: DescriptorImpactAnalyzer — Severity & Advisory Helpers

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorImpactAnalyzer.cs` (partial: severity + advisory helpers only)

- [ ] **Step 1: Write severity computation methods**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata;

public sealed partial class DescriptorImpactAnalyzer : IDescriptorImpactAnalyzer
{
    // Advisory edge predicate (§4.4)
    internal static bool IsAdvisory(DescriptorEdge edge)
    {
        if (edge.IsRuntimeBinding) return false;
        return edge.Strength == RelationshipStrength.Weak
            && (edge.Kind == RelationshipKind.References
                || edge.Kind == RelationshipKind.DependsOn
                || edge.Role == RelationshipRoles.SupersededBy
                || edge.Role == RelationshipRoles.SubWorkflowStep);
    }

    // Base severity from table (§4.1)
    internal static DescriptorImpactSeverity BaseSeverity(
        DescriptorChangeKind changeKind,
        bool isStrongPath,
        bool isRuntimePath)
    {
        if (changeKind == DescriptorChangeKind.Removed)
            return isStrongPath
                ? (isRuntimePath ? DescriptorImpactSeverity.Critical : DescriptorImpactSeverity.High)
                : DescriptorImpactSeverity.Medium;

        if (changeKind == DescriptorChangeKind.Deprecated)
            return isStrongPath
                ? (isRuntimePath ? DescriptorImpactSeverity.High : DescriptorImpactSeverity.Medium)
                : DescriptorImpactSeverity.Low;

        if (changeKind is DescriptorChangeKind.Updated or DescriptorChangeKind.ContractHashChanged)
            return isStrongPath
                ? (isRuntimePath ? DescriptorImpactSeverity.High : DescriptorImpactSeverity.Medium)
                : DescriptorImpactSeverity.Low;

        if (changeKind == DescriptorChangeKind.StateChanged)
            return isStrongPath
                ? (isRuntimePath ? DescriptorImpactSeverity.Medium : DescriptorImpactSeverity.Low)
                : DescriptorImpactSeverity.Info;

        // Activated or Added
        return DescriptorImpactSeverity.Info;
    }

    // Attenuation (§4.3, Modifier 1): reduce by one level for depth >= 2
    internal static DescriptorImpactSeverity Attenuate(DescriptorImpactSeverity severity)
    {
        return severity switch
        {
            DescriptorImpactSeverity.Critical => DescriptorImpactSeverity.High,
            DescriptorImpactSeverity.High => DescriptorImpactSeverity.Medium,
            DescriptorImpactSeverity.Medium => DescriptorImpactSeverity.Low,
            DescriptorImpactSeverity.Low => DescriptorImpactSeverity.Info,
            _ => severity
        };
    }

    // Runtime binding boost (§4.3, Modifier 2): per-terminal-segment, cap High
    internal static DescriptorImpactSeverity RuntimeBoost(DescriptorImpactSeverity severity)
    {
        return severity switch
        {
            DescriptorImpactSeverity.Critical => DescriptorImpactSeverity.High, // cap
            DescriptorImpactSeverity.High => DescriptorImpactSeverity.High,
            DescriptorImpactSeverity.Medium => DescriptorImpactSeverity.High,
            DescriptorImpactSeverity.Low => DescriptorImpactSeverity.Medium,
            DescriptorImpactSeverity.Info => DescriptorImpactSeverity.Low,
            _ => severity
        };
    }

    // Full severity pipeline: table → attenuate(if depth>=2) → runtime-boost(if terminal runtime) → cap at High
    internal static DescriptorImpactSeverity ComputePathSeverity(
        DescriptorChangeKind changeKind,
        DescriptorImpactPathSegment terminalSegment,
        int depth)
    {
        var isStrong = terminalSegment.Strength == RelationshipStrength.Strong;
        var isRuntime = terminalSegment.IsRuntimeBinding;
        var severity = BaseSeverity(changeKind, isStrong, isRuntime);
        if (depth >= 2) severity = Attenuate(severity);
        if (terminalSegment.IsRuntimeBinding) severity = RuntimeBoost(severity);
        return severity;
    }

    // Runtime area from descriptor kind (§6.3)
    internal static DescriptorImpactRuntimeArea AreaFromKind(DescriptorKind kind) => kind switch
    {
        DescriptorKind.Schema => DescriptorImpactRuntimeArea.Schema,
        DescriptorKind.Form => DescriptorImpactRuntimeArea.Form,
        DescriptorKind.Capability => DescriptorImpactRuntimeArea.Capability,
        DescriptorKind.Event => DescriptorImpactRuntimeArea.Event,
        DescriptorKind.Workflow => DescriptorImpactRuntimeArea.Workflow,
        DescriptorKind.HumanTask => DescriptorImpactRuntimeArea.HumanTask,
        _ => DescriptorImpactRuntimeArea.Metadata
    };
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorImpactAnalyzer.cs
git commit -m "feat(6c): add DescriptorImpactAnalyzer severity computation + advisory helpers"
```

---

## Task 9: DescriptorImpactAnalyzer — Core BFS Engine

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/DescriptorImpactAnalyzer.cs` (append Analyze method + BFS)

- [ ] **Step 1: Append the Analyze method and BFS engine**

Append to the existing `DescriptorImpactAnalyzer.cs` partial class:

```csharp
    private readonly struct BfsState(
        DescriptorNode currentNode,
        int depth,
        List<DescriptorImpactPathSegment> pathSoFar,
        bool hasRuntimeBindingAlongPath)
    {
        public DescriptorNode CurrentNode => currentNode;
        public int Depth => depth;
        public List<DescriptorImpactPathSegment> PathSoFar => pathSoFar;
        public bool HasRuntimeBindingAlongPath => hasRuntimeBindingAlongPath;
    }

    public DescriptorImpactAnalysisReport Analyze(
        DescriptorTopologySnapshot topology,
        DescriptorChangeSet changeSet,
        DescriptorImpactAnalysisOptions? options = null)
    {
        var opts = options ?? new DescriptorImpactAnalysisOptions();

        // ── Build indices (§5.1) ──
        var exactIndex = topology.Nodes.ToDictionary(kv => kv.Key, kv => kv.Value);
        var identityIndex = topology.Nodes.Values
            .GroupBy(n => new DescriptorIdentity(n.Ref.Namespace, n.Ref.Id))
            .ToDictionary(g => g.Key, g => g.ToList());

        // _impactIncomingIndex: fan-out aware (§5.1)
        var impactIncoming = new Dictionary<DescriptorRef, List<DescriptorEdge>>();
        foreach (var edge in topology.Edges)
        {
            if (edge.To.Version != null)
            {
                AddToIndex(impactIncoming, edge.To, edge);
            }
            else
            {
                var idKey = new DescriptorIdentity(edge.To.Namespace, edge.To.Id);
                if (identityIndex.TryGetValue(idKey, out var matching))
                {
                    foreach (var node in matching)
                        AddToIndex(impactIncoming, node.Ref, edge);
                }
                // else: unresolved target; only relevant if it matches a changed ref; skip for now
            }
        }

        var diagnostics = new List<DescriptorImpactDiagnostic>();
        var allDiscovered = new Dictionary<DescriptorRef, List<(DescriptorImpactPath Path, bool HasRuntime)>>();

        // ── BFS loop over each change (§5.3) ──
        foreach (var change in changeSet.Changes)
        {
            var originNode = ResolveRef(change.Ref, exactIndex, identityIndex);
            if (originNode is null || originNode.Count == 0) continue; // Changed ref not in topology

            foreach (var origin in originNode)
            {
                if (!impactIncoming.TryGetValue(origin.Ref, out var incomingEdges))
                    continue; // No consumers

                var visited = new HashSet<(DescriptorRef OriginRef, DescriptorRef CurrentRef, int EdgeIndex)>();
                var queue = new Queue<BfsState>();

                // Seed: origin node, depth 1 consumers
                foreach (var edge in incomingEdges)
                {
                    var key = (change.Ref, origin.Ref, edge.Index);
                    if (!visited.Add(key)) continue;

                    // Edge filtering (§5.5)
                    if (!opts.IncludeWeakRelationships && edge.Strength == RelationshipStrength.Weak) continue;
                    if (!opts.IncludeAdvisoryRelationships && IsAdvisory(edge))
                    {
                        diagnostics.Add(new DescriptorImpactDiagnostic(
                            DiagnosticSeverity.Info,
                            "IMPACT_SKIPPED_WEAK_PATH",
                            $"Advisory edge skipped: {edge.From.Id} → {edge.To.Id}",
                            edge.From, null));
                        continue;
                    }

                    // Depth limit check at entry
                    if (opts.MaxDepth.HasValue && 1 >= opts.MaxDepth.Value)
                    {
                        var consumerNodes = ResolveRef(edge.From, exactIndex, identityIndex);
                        foreach (var cn in consumerNodes)
                        {
                            var seg = CreateSegment(edge);
                            var path = new DescriptorImpactPath
                            {
                                SourceChange = change.Ref,
                                Affected = cn.Ref,
                                Segments = new[] { seg }
                            };
                            RecordDiscovered(allDiscovered, cn.Ref, path, edge.IsRuntimeBinding);
                        }
                        diagnostics.Add(new DescriptorImpactDiagnostic(
                            DiagnosticSeverity.Warning,
                            "IMPACT_PATH_TRUNCATED",
                            $"Impact path truncated at depth limit: {opts.MaxDepth}",
                            origin.Ref, null));
                        continue;
                    }

                    var segment = CreateSegment(edge);
                    queue.Enqueue(new BfsState(origin, 1, new List<DescriptorImpactPathSegment> { segment }, edge.IsRuntimeBinding));
                }

                // BFS
                while (queue.Count > 0)
                {
                    var state = queue.Dequeue();

                    // Resolve consumer(s) of the current edge (§5.7)
                    var lastSegment = state.PathSoFar[^1];
                    var consumerNodes = ResolveRef(lastSegment.From, exactIndex, identityIndex);

                    if (consumerNodes.Count == 0)
                    {
                        diagnostics.Add(new DescriptorImpactDiagnostic(
                            DiagnosticSeverity.Warning,
                            "IMPACT_UNRESOLVED_CONSUMER",
                            $"Unresolved consumer: {lastSegment.From.FullId}",
                            lastSegment.From, null));
                        continue;
                    }

                    if (consumerNodes.Count > 1)
                    {
                        diagnostics.Add(new DescriptorImpactDiagnostic(
                            DiagnosticSeverity.Warning,
                            "IMPACT_AMBIGUOUS_UNPINNED_TARGET",
                            $"Ambiguous unpinned consumer: {lastSegment.From.FullId} resolves to {consumerNodes.Count} versions",
                            lastSegment.From, null));
                    }

                    foreach (var consumerNode in consumerNodes)
                    {
                        // Record this consumer as affected
                        var path = new DescriptorImpactPath
                        {
                            SourceChange = change.Ref,
                            Affected = consumerNode.Ref,
                            Segments = state.PathSoFar.ToArray()
                        };
                        RecordDiscovered(allDiscovered, consumerNode.Ref, path, state.HasRuntimeBindingAlongPath);

                        // Depth limit for further traversal
                        var nextDepth = state.Depth + 1;
                        if (opts.MaxDepth.HasValue && nextDepth > opts.MaxDepth.Value)
                        {
                            diagnostics.Add(new DescriptorImpactDiagnostic(
                                DiagnosticSeverity.Warning,
                                "IMPACT_PATH_TRUNCATED",
                                $"Impact path truncated at depth {opts.MaxDepth}: {consumerNode.Ref.FullId}",
                                consumerNode.Ref, null));
                            continue;
                        }

                        // Expand from consumer: get consumer's incoming edges
                        if (!impactIncoming.TryGetValue(consumerNode.Ref, out var nextEdges))
                            continue;

                        foreach (var nextEdge in nextEdges)
                        {
                            // Edge filtering
                            if (!opts.IncludeWeakRelationships && nextEdge.Strength == RelationshipStrength.Weak) continue;
                            if (!opts.IncludeAdvisoryRelationships && IsAdvisory(nextEdge))
                            {
                                diagnostics.Add(new DescriptorImpactDiagnostic(
                                    DiagnosticSeverity.Info,
                                    "IMPACT_SKIPPED_WEAK_PATH",
                                    $"Advisory edge skipped: {nextEdge.From.Id} → {nextEdge.To.Id}",
                                    nextEdge.From, null));
                                continue;
                            }

                            var visitKey = (change.Ref, consumerNode.Ref, nextEdge.Index);
                            if (!visited.Add(visitKey)) continue;

                            var nextSegment = CreateSegment(nextEdge);
                            var newPath = new List<DescriptorImpactPathSegment>(state.PathSoFar) { nextSegment };
                            queue.Enqueue(new BfsState(consumerNode, nextDepth, newPath,
                                state.HasRuntimeBindingAlongPath || nextEdge.IsRuntimeBinding));
                        }
                    }
                }
            }
        }

        // ── Assembly (§5.10) ──
        var affectedDescriptors = new List<AffectedDescriptor>();
        var allPaths = new List<DescriptorImpactPath>();

        foreach (var (consumerRef, pathList) in allDiscovered)
        {
            if (!exactIndex.TryGetValue(consumerRef, out var node)) continue;

            var dedupedPaths = pathList.Select(p => p.Path).ToList();
            allPaths.AddRange(dedupedPaths);

            // For each path, compute severity using its terminal segment
            var maxSev = DescriptorImpactSeverity.None;
            DescriptorImpactPath? topPath = null;
            bool hasRuntimeBindingAnyPath = false;

            foreach (var (path, hasRb) in pathList)
            {
                if (hasRb) hasRuntimeBindingAnyPath = true;

                var terminalSeg = path.Segments[^1];
                var originChange = changeSet.Changes.FirstOrDefault(c => c.Ref == path.SourceChange);
                if (originChange is null) continue;

                var sev = ComputePathSeverity(originChange.Kind, terminalSeg, path.Segments.Count);
                if (sev > maxSev)
                {
                    maxSev = sev;
                    topPath = path;
                }
            }

            var areas = new List<DescriptorImpactRuntimeArea> { AreaFromKind(node.Kind) };
            if (hasRuntimeBindingAnyPath)
                areas.Add(DescriptorImpactRuntimeArea.RuntimeBinding);

            var reason = topPath is not null
                ? $"{changeSet.Changes.First(c => c.Ref == topPath.SourceChange).Kind}: " +
                  $"{topPath.SourceChange.FullId} → {consumerRef.FullId} via " +
                  $"{topPath.Segments[^1].Role ?? topPath.Segments[^1].Kind.ToString()}"
                : null;

            affectedDescriptors.Add(new AffectedDescriptor
            {
                Ref = consumerRef,
                Kind = node.Kind,
                Name = node.Name,
                Severity = maxSev,
                RuntimeAreas = areas,
                Paths = dedupedPaths,
                Reason = reason
            });
        }

        // Sort: severity desc, then name
        affectedDescriptors.Sort((a, b) =>
        {
            var cmp = b.Severity.CompareTo(a.Severity);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        // ── Topology diagnostics on traversed paths (§5.11) ──
        foreach (var topoDiag in topology.Diagnostics.All)
        {
            if (topoDiag.Subject is not null && allDiscovered.ContainsKey(topoDiag.Subject.Value))
            {
                var code = topoDiag.Code switch
                {
                    "MISSING_TARGET" => "IMPACT_TOPOLOGY_MISSING_TARGET",
                    "STRONG_CYCLE" => "IMPACT_TOPOLOGY_STRONG_CYCLE",
                    "UNSUPPORTED_REFERENCE" => "IMPACT_TOPOLOGY_UNSUPPORTED_REFERENCE",
                    _ => null
                };
                if (code is not null)
                {
                    diagnostics.Add(new DescriptorImpactDiagnostic(
                        topoDiag.Severity, code, topoDiag.Message,
                        topoDiag.Subject, topoDiag.RelatedRefs));
                }
            }
        }

        // Sort diagnostics: Error > Warning > Info
        diagnostics.Sort((a, b) => b.Severity.CompareTo(a.Severity));

        var maxSeverity = affectedDescriptors.Count > 0
            ? affectedDescriptors.Max(a => a.Severity)
            : DescriptorImpactSeverity.None;

        return new DescriptorImpactAnalysisReport
        {
            ChangeSet = changeSet,
            AffectedDescriptors = affectedDescriptors,
            Paths = allPaths,
            MaxSeverity = maxSeverity,
            Diagnostics = diagnostics
        };
    }

    // ── Helpers ──

    private static void AddToIndex(Dictionary<DescriptorRef, List<DescriptorEdge>> index, DescriptorRef key, DescriptorEdge edge)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = new List<DescriptorEdge>();
            index[key] = list;
        }
        list.Add(edge);
    }

    private static List<DescriptorNode> ResolveRef(
        DescriptorRef r,
        Dictionary<DescriptorRef, DescriptorNode> exactIndex,
        Dictionary<DescriptorIdentity, List<DescriptorNode>> identityIndex)
    {
        if (exactIndex.TryGetValue(r, out var node))
            return new List<DescriptorNode> { node };

        if (r.Version == null)
        {
            var idKey = new DescriptorIdentity(r.Namespace, r.Id);
            if (identityIndex.TryGetValue(idKey, out var matching))
                return matching;
        }

        return new List<DescriptorNode>();
    }

    private static DescriptorImpactPathSegment CreateSegment(DescriptorEdge edge) =>
        new()
        {
            From = edge.From,
            To = edge.To,
            Kind = edge.Kind,
            Strength = edge.Strength,
            IsRuntimeBinding = edge.IsRuntimeBinding,
            Role = edge.Role,
            SourcePath = edge.SourcePath
        };

    private static void RecordDiscovered(
        Dictionary<DescriptorRef, List<(DescriptorImpactPath Path, bool HasRuntime)>> allDiscovered,
        DescriptorRef consumerRef,
        DescriptorImpactPath path,
        bool hasRuntime)
    {
        if (!allDiscovered.TryGetValue(consumerRef, out var list))
        {
            list = new List<(DescriptorImpactPath, bool)>();
            allDiscovered[consumerRef] = list;
        }
        list.Add((path, hasRuntime));
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorImpactAnalyzer.cs
git commit -m "feat(6c): implement DescriptorImpactAnalyzer BFS engine with fan-out indices"
```

---

## Task 10: DI Registration

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs`

- [ ] **Step 1: Add registration to MsExtensions**

Insert after `AddTopologyKernel`:

```csharp
public static IServiceCollection AddDescriptorImpactAnalysis(
    this IServiceCollection services)
{
    services.TryAddSingleton<IDescriptorImpactAnalyzer, DescriptorImpactAnalyzer>();
    services.TryAddSingleton<IDescriptorChangeSetBuilder, DescriptorChangeSetBuilder>();
    return services;
}
```

Full file after edit:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Metadata;

public static class MetadataServiceCollectionExtensions
{
    public static IServiceCollection AddBindingStatusKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorRuntimeBindingStatusProvider,
            DefaultDescriptorRuntimeBindingStatusProvider>();
        return services;
    }

    public static IServiceCollection AddRelationshipKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorRelationshipProvider,
            DefaultDescriptorRelationshipProvider>();
        services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();
        return services;
    }

    public static IServiceCollection AddTopologyKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorTopologyBuilder, DescriptorTopologyBuilder>();
        return services;
    }

    public static IServiceCollection AddDescriptorImpactAnalysis(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorImpactAnalyzer, DescriptorImpactAnalyzer>();
        services.TryAddSingleton<IDescriptorChangeSetBuilder, DescriptorChangeSetBuilder>();
        return services;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs
git commit -m "feat(6c): register IDescriptorImpactAnalyzer and IDescriptorChangeSetBuilder in DI"
```

---

## Task 11: ImpactAnalyzer Tests — Direct & Transitive Consumers

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorImpact/DescriptorImpactAnalyzerTests.cs`

- [ ] **Step 1: Write core analyzer tests (direct, transitive, weak, advisory, depth limit)**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorImpact;

public class DescriptorImpactAnalyzerTests
{
    private readonly DescriptorImpactAnalyzer _analyzer = new();

    // Helper: build topology + node/edge defs inline
    private static (
        DescriptorTopologySnapshot Snapshot,
        Dictionary<DescriptorRef, DescriptorNode> NodeMap)
        BuildTopology(
            (DescriptorRef Ref, DescriptorKind Kind, string Name, DescriptorState State)[] nodeDefs,
            (int Index, DescriptorRef From, DescriptorRef To, RelationshipKind Kind,
             string? Role, RelationshipStrength Strength, bool IsRuntimeBinding)[] edgeDefs)
    {
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>();
        foreach (var def in nodeDefs)
        {
            nodes[def.Ref] = new DescriptorNode
            {
                Ref = def.Ref,
                Kind = def.Kind,
                Name = def.Name,
                State = def.State,
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
                SourcePath = def.Role, // mirror role as source path for test
                Strength = def.Strength,
                IsRuntimeBinding = def.IsRuntimeBinding
            };
            edges.Add(edge);

            if (nodes.TryGetValue(def.From, out var fn))
                ((HashSet<int>)fn.OutgoingEdgeIndices).Add(def.Index);
            if (nodes.TryGetValue(def.To, out var tn))
                ((HashSet<int>)tn.IncomingEdgeIndices).Add(def.Index);
        }

        foreach (var key in nodes.Keys.ToList())
        {
            nodes[key] = nodes[key] with
            {
                OutgoingEdgeIndices = nodes[key].OutgoingEdgeIndices.ToHashSet(),
                IncomingEdgeIndices = nodes[key].IncomingEdgeIndices.ToHashSet()
            };
        }

        var snapshot = new DescriptorTopologySnapshot(
            nodes, edges,
            new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() },
            new(), new(), new(), DateTimeOffset.UtcNow);

        return (snapshot, nodes);
    }

    [Fact]
    public void DirectStrongConsumer_IsReported()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);

        report.AffectedDescriptors.Should().ContainSingle()
            .Which.Ref.Should().Be(form);
        report.AffectedDescriptors[0].Severity.Should().Be(DescriptorImpactSeverity.High);
        report.AffectedDescriptors[0].Paths.Should().ContainSingle()
            .Which.Segments.Should().ContainSingle()
            .Which.Role.Should().Be("Schema");
    }

    [Fact]
    public void TransitiveConsumer_IsReported_WithAttenuatedSeverity()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false),
                (1, cap, form, RelationshipKind.Consumes, "InputSchema", RelationshipStrength.Strong, true)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Deprecated } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);

        // form = depth 1 (direct), cap = depth 2 (transitive, attenuated)
        report.AffectedDescriptors.Should().HaveCount(2);
        var formEntry = report.AffectedDescriptors.First(a => a.Ref == form);
        var capEntry = report.AffectedDescriptors.First(a => a.Ref == cap);

        // form: Deprecated + Strong + runtime=false → High, depth 1 → stays High
        formEntry.Severity.Should().Be(DescriptorImpactSeverity.High);
        // cap: Deprecated + Strong + runtime=true → High, depth 2 → attenuated to Medium, then runtime boost → High
        capEntry.Severity.Should().Be(DescriptorImpactSeverity.High);
    }

    [Fact]
    public void RuntimeBinding_TerminalSegment_BoostsSeverity()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                (0, cap, schema, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Updated } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        // Updated + Strong + runtime=true → High
        report.AffectedDescriptors[0].Severity.Should().Be(DescriptorImpactSeverity.High);
        report.AffectedDescriptors[0].RuntimeAreas.Should().Contain(DescriptorImpactRuntimeArea.RuntimeBinding);
    }

    [Fact]
    public void RuntimeBinding_NonTerminalSegment_DoesNotBoostDownstream()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);
        var wf = new DescriptorRef("wf", "OrderWorkflow", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active),
                (wf, DescriptorKind.Workflow, "OrderWorkflow", DescriptorState.Active)
            },
            new[] {
                (0, cap, schema, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true),
                (1, wf, cap, RelationshipKind.Uses, "VariableSchema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);

        // cap: depth 1, Removed + Strong + runtime → Critical
        var capEntry = report.AffectedDescriptors.First(a => a.Ref == cap);
        capEntry.Severity.Should().Be(DescriptorImpactSeverity.Critical);
        capEntry.RuntimeAreas.Should().Contain(DescriptorImpactRuntimeArea.RuntimeBinding);

        // wf: depth 2, Removed + Strong + non-runtime → High, attenuated → Medium, no runtime boost on terminal segment
        var wfEntry = report.AffectedDescriptors.First(a => a.Ref == wf);
        wfEntry.Severity.Should().Be(DescriptorImpactSeverity.Medium);
        // RuntimeBinding area added because path-wide hasRuntimeBinding=true (from cap edge)
        wfEntry.RuntimeAreas.Should().Contain(DescriptorImpactRuntimeArea.RuntimeBinding);
    }

    [Fact]
    public void WeakPath_Included_ByDefault()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                (0, cap, schema, RelationshipKind.Produces, "OutputSchema", RelationshipStrength.Weak, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors.Should().ContainSingle().Which.Severity.Should().Be(DescriptorImpactSeverity.Medium);
    }

    [Fact]
    public void WeakPath_Excluded_WhenFalse()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                (0, cap, schema, RelationshipKind.Produces, "OutputSchema", RelationshipStrength.Weak, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var opts = new DescriptorImpactAnalysisOptions { IncludeWeakRelationships = false };
        var report = _analyzer.Analyze(snapshot, changeSet, opts);
        report.AffectedDescriptors.Should().BeEmpty();
    }

    [Fact]
    public void AdvisoryPath_Skipped_WhenFalse_WithDiagnostic()
    {
        var capA = new DescriptorRef("cap", "A", 2);
        var capB = new DescriptorRef("cap", "B", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (capA, DescriptorKind.Capability, "A", DescriptorState.Active),
                (capB, DescriptorKind.Capability, "B", DescriptorState.Active)
            },
            new[] {
                (0, capB, capA, RelationshipKind.DependsOn, RelationshipRoles.SupersededBy, RelationshipStrength.Weak, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = capA, Kind = DescriptorChangeKind.Removed } }
        };

        var opts = new DescriptorImpactAnalysisOptions { IncludeAdvisoryRelationships = false };
        var report = _analyzer.Analyze(snapshot, changeSet, opts);

        report.AffectedDescriptors.Should().BeEmpty();
        report.Diagnostics.Should().Contain(d => d.Code == "IMPACT_SKIPPED_WEAK_PATH");
    }

    [Fact]
    public void DepthLimit_Truncates_WithDiagnostic()
    {
        var s = new DescriptorRef("schema", "S", 1);
        var f = new DescriptorRef("form", "F", 1);
        var c = new DescriptorRef("cap", "C", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (s, DescriptorKind.Schema, "S", DescriptorState.Active),
                (f, DescriptorKind.Form, "F", DescriptorState.Active),
                (c, DescriptorKind.Capability, "C", DescriptorState.Active)
            },
            new[] {
                (0, f, s, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false),
                (1, c, f, RelationshipKind.Consumes, "InputSchema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = s, Kind = DescriptorChangeKind.Removed } }
        };

        var opts = new DescriptorImpactAnalysisOptions { MaxDepth = 1 };
        var report = _analyzer.Analyze(snapshot, changeSet, opts);

        // Only form at depth 1; cap at depth 2 truncated
        report.AffectedDescriptors.Should().ContainSingle().Which.Ref.Should().Be(f);
        report.Diagnostics.Should().Contain(d => d.Code == "IMPACT_PATH_TRUNCATED");
    }

    [Fact]
    public void ChangedDescriptor_NotInTopology_ReturnsEmpty()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);

        var (snapshot, _) = BuildTopology(
            new[] { (form, DescriptorKind.Form, "Checkout", DescriptorState.Active) },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors.Should().BeEmpty();
        report.MaxSeverity.Should().Be(DescriptorImpactSeverity.None);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~DescriptorImpactAnalyzerTests"`
Expected: 11 tests PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorImpact/DescriptorImpactAnalyzerTests.cs
git commit -m "test(6c): add DescriptorImpactAnalyzer core tests (11 tests)"
```

---

## Task 12: ImpactAnalyzer Tests — Unpinned Version, Fan-Out, Cycle, Diagnostics

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.Tests/DescriptorImpact/DescriptorImpactAnalyzerTests.cs` (append tests)

- [ ] **Step 1: Append unpinned, version, cycle, and diagnostic tests**

Append to the existing test class:

```csharp
    [Fact]
    public void UnpinnedConsumer_Included_ForExactChangedVersion()
    {
        var schemaV1 = new DescriptorRef("schema", "Order", 1);
        var schemaV2 = new DescriptorRef("schema", "Order", 2);
        var form = new DescriptorRef("form", "Checkout", 1);

        // Unpinned edge: form → schema.Order@null (no version)
        var unpinnedTo = new DescriptorRef("schema", "Order", null);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schemaV1, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, unpinnedTo, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        // Change the exact v1
        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schemaV1, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        // Form should be affected because its unpinned edge fans out to v1
        report.AffectedDescriptors.Should().ContainSingle().Which.Ref.Should().Be(form);
    }

    [Fact]
    public void UnpinnedRef_Ambiguous_FanOut_WithDiagnostic()
    {
        var schemaV1 = new DescriptorRef("schema", "Order", 1);
        var schemaV2 = new DescriptorRef("schema", "Order", 2);
        var cap = new DescriptorRef("cap", "ProcessOrder", null); // unpinned consumer
        var unpinnedTo = new DescriptorRef("schema", "Order", null);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schemaV1, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                (0, cap, unpinnedTo, RelationshipKind.Consumes, "InputSchema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schemaV1, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        // cap is affected (fan-out from v1)
        report.AffectedDescriptors.Should().ContainSingle().Which.Ref.Should().Be(cap);
        // Ambiguity diagnostic because cap@null resolves to 2 versions
        report.Diagnostics.Should().Contain(d => d.Code == "IMPACT_AMBIGUOUS_UNPINNED_TARGET");
    }

    [Fact]
    public void UnpinnedRef_Unresolved_EmitsDiagnostic()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var capExact = new DescriptorRef("cap", "ProcessOrder", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (capExact, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                // Edge from non-existent unpinned consumer to schema
                (0, new DescriptorRef("cap", "Ghost", null), schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.Diagnostics.Should().Contain(d => d.Code == "IMPACT_UNRESOLVED_CONSUMER");
    }

    [Fact]
    public void FanOut_PreservesVersionBranchPaths_ButDedupesAffected()
    {
        var schemaV1 = new DescriptorRef("schema", "Order", 1);
        var schemaV2 = new DescriptorRef("schema", "Order", 2);
        var form = new DescriptorRef("form", "Checkout", null);
        var unpinnedTo = new DescriptorRef("schema", "Order", null);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schemaV1, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, unpinnedTo, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        // Change BOTH versions
        var changeSet = new DescriptorChangeSet
        {
            Changes = new[]
            {
                new DescriptorChange { Ref = schemaV1, Kind = DescriptorChangeKind.Removed },
                new DescriptorChange { Ref = schemaV2, Kind = DescriptorChangeKind.Removed }
            }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);

        // One affected descriptor (form), but 2 paths (v1 branch + v2 branch)
        report.AffectedDescriptors.Should().ContainSingle();
        report.AffectedDescriptors[0].Paths.Should().HaveCount(2);
    }

    [Fact]
    public void MultipleChangeKinds_MultipleAffected_AllReported()
    {
        var s1 = new DescriptorRef("schema", "S1", 1);
        var s2 = new DescriptorRef("schema", "S2", 1);
        var f1 = new DescriptorRef("form", "F1", 1);
        var f2 = new DescriptorRef("form", "F2", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (s1, DescriptorKind.Schema, "S1", DescriptorState.Active),
                (s2, DescriptorKind.Schema, "S2", DescriptorState.Active),
                (f1, DescriptorKind.Form, "F1", DescriptorState.Active),
                (f2, DescriptorKind.Form, "F2", DescriptorState.Active)
            },
            new[] {
                (0, f1, s1, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false),
                (1, f2, s1, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false),
                (2, f2, s2, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[]
            {
                new DescriptorChange { Ref = s1, Kind = DescriptorChangeKind.Removed },
                new DescriptorChange { Ref = s2, Kind = DescriptorChangeKind.Deprecated }
            }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors.Should().HaveCount(2);
        report.AffectedDescriptors.Select(a => a.Ref.Id).Should().Contain(new[] { "F1", "F2" });
    }

    [Fact]
    public void Severity_IsMaxAcrossAllPaths()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);

        // Two paths to same consumer: strong + weak
        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false),
                (1, form, schema, RelationshipKind.Produces, "OutputSchema", RelationshipStrength.Weak, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        // Strong path → High, Weak path → Medium. Max = High.
        report.AffectedDescriptors[0].Severity.Should().Be(DescriptorImpactSeverity.High);
    }

    [Fact]
    public void Path_ContainsRole_And_SourcePath()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        var seg = report.AffectedDescriptors[0].Paths[0].Segments[0];
        seg.Role.Should().Be("Schema");
        seg.SourcePath.Should().Be("Schema");
    }

    [Fact]
    public void TopologyDiagnostic_OnPath_ReExported()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);
        var missing = new DescriptorRef("form", "Missing", 1);

        // Build topology with a diagnostic that references form (on the impact path)
        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        // Inject a topology diagnostic referencing form (which IS on the impact path)
        var topoDiag = new DescriptorTopologyDiagnostic(
            DiagnosticSeverity.Error,
            "MISSING_TARGET",
            "Missing target: X",
            form, null);

        var nodes = snapshot.Nodes.ToDictionary(kv => kv.Key, kv => kv.Value);
        var edges = snapshot.Edges.ToList();
        var diags = new DescriptorTopologyDiagnostics { All = new[] { topoDiag } };
        var fixedSnapshot = new DescriptorTopologySnapshot(
            nodes, edges, diags, new(), new(), new(), DateTimeOffset.UtcNow);

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(fixedSnapshot, changeSet);
        report.Diagnostics.Should().Contain(d => d.Code == "IMPACT_TOPOLOGY_MISSING_TARGET");
    }

    [Fact]
    public void TopologyDiagnostic_OffPath_NotExported()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);
        var unrelated = new DescriptorRef("form", "Unrelated", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active),
                (unrelated, DescriptorKind.Form, "Unrelated", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var topoDiag = new DescriptorTopologyDiagnostic(
            DiagnosticSeverity.Error,
            "MISSING_TARGET",
            "Missing target: off-path",
            unrelated, null);
        var diags = new DescriptorTopologyDiagnostics { All = new[] { topoDiag } };
        var fixedSnapshot = new DescriptorTopologySnapshot(
            snapshot.Nodes.ToDictionary(kv => kv.Key, kv => kv.Value),
            snapshot.Edges.ToList(), diags, new(), new(), new(), DateTimeOffset.UtcNow);

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(fixedSnapshot, changeSet);
        report.Diagnostics.Should().NotContain(d => d.Code == "IMPACT_TOPOLOGY_MISSING_TARGET");
    }

    [Fact]
    public void Cycle_DoesNotLoop_Infinite()
    {
        var a = new DescriptorRef("test", "A", 1);
        var b = new DescriptorRef("test", "B", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (a, DescriptorKind.Capability, "A", DescriptorState.Active),
                (b, DescriptorKind.Capability, "B", DescriptorState.Active)
            },
            new[] {
                (0, a, b, RelationshipKind.Uses, "Dep", RelationshipStrength.Strong, false),
                (1, b, a, RelationshipKind.Uses, "Dep", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = a, Kind = DescriptorChangeKind.Removed } }
        };

        // This must complete without hanging
        var report = _analyzer.Analyze(snapshot, changeSet);
        // Should report B as affected (first hop), then cycle stops
        report.AffectedDescriptors.Should().NotBeEmpty();
    }
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~DescriptorImpactAnalyzerTests"`
Expected: All 21 tests PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorImpact/DescriptorImpactAnalyzerTests.cs
git commit -m "test(6c): add unpinned version, fan-out, cycle, diagnostics tests for analyzer"
```

---

## Task 13: Severity Table Tests

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorImpact/DescriptorImpactSeverityTests.cs`

- [ ] **Step 1: Write severity table tests**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorImpact;

public class DescriptorImpactSeverityTests
{
    private static DescriptorImpactSeverity Compute(
        DescriptorChangeKind kind, RelationshipStrength strength,
        bool isRuntimeBinding, int depth = 1)
    {
        var segment = new DescriptorImpactPathSegment
        {
            From = new DescriptorRef("ns", "source"),
            To = new DescriptorRef("ns", "target"),
            Kind = RelationshipKind.Uses,
            Strength = strength,
            IsRuntimeBinding = isRuntimeBinding
        };
        return DescriptorImpactAnalyzer.ComputePathSeverity(kind, segment, depth);
    }

    [Fact] public void Removed_StrongRuntime_IsCritical()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Strong, true).Should().Be(DescriptorImpactSeverity.Critical);

    [Fact] public void Removed_StrongDescriptor_IsHigh()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Strong, false).Should().Be(DescriptorImpactSeverity.High);

    [Fact] public void Removed_Weak_IsMedium()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Weak, false).Should().Be(DescriptorImpactSeverity.Medium);

    [Fact] public void Deprecated_StrongRuntime_IsHigh()
        => Compute(DescriptorChangeKind.Deprecated, RelationshipStrength.Strong, true).Should().Be(DescriptorImpactSeverity.High);

    [Fact] public void Deprecated_StrongDescriptor_IsMedium()
        => Compute(DescriptorChangeKind.Deprecated, RelationshipStrength.Strong, false).Should().Be(DescriptorImpactSeverity.Medium);

    [Fact] public void Deprecated_Weak_IsLow()
        => Compute(DescriptorChangeKind.Deprecated, RelationshipStrength.Weak, false).Should().Be(DescriptorImpactSeverity.Low);

    [Fact] public void Updated_StrongRuntime_IsHigh()
        => Compute(DescriptorChangeKind.Updated, RelationshipStrength.Strong, true).Should().Be(DescriptorImpactSeverity.High);

    [Fact] public void StateChanged_StrongRuntime_IsMedium()
        => Compute(DescriptorChangeKind.StateChanged, RelationshipStrength.Strong, true).Should().Be(DescriptorImpactSeverity.Medium);

    [Fact] public void Activated_AlwaysInfo()
        => Compute(DescriptorChangeKind.Activated, RelationshipStrength.Strong, true).Should().Be(DescriptorImpactSeverity.Info);

    [Fact] public void TransitiveAttenuation_Removed_CriticalToHigh()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Strong, true, depth: 2).Should().Be(DescriptorImpactSeverity.High);

    [Fact] public void TransitiveAttenuation_Deprecated_HighToMedium()
        => Compute(DescriptorChangeKind.Deprecated, RelationshipStrength.Strong, true, depth: 2).Should().Be(DescriptorImpactSeverity.Medium);

    [Fact] public void IsAdvisory_SupersededBy_ReturnsTrue()
    {
        var edge = new DescriptorEdge
        {
            Index = 0, From = new("a", "x"), To = new("a", "y"),
            Kind = RelationshipKind.DependsOn, Role = RelationshipRoles.SupersededBy,
            Strength = RelationshipStrength.Weak, IsRuntimeBinding = false
        };
        DescriptorImpactAnalyzer.IsAdvisory(edge).Should().BeTrue();
    }

    [Fact] public void IsAdvisory_RuntimeBinding_ReturnsFalse()
    {
        var edge = new DescriptorEdge
        {
            Index = 0, From = new("a", "x"), To = new("a", "y"),
            Kind = RelationshipKind.References, Role = RelationshipRoles.SubWorkflowStep,
            Strength = RelationshipStrength.Weak, IsRuntimeBinding = true
        };
        DescriptorImpactAnalyzer.IsAdvisory(edge).Should().BeFalse();
    }

    [Fact] public void IsAdvisory_StrongReferences_ReturnsFalse()
    {
        var edge = new DescriptorEdge
        {
            Index = 0, From = new("a", "x"), To = new("a", "y"),
            Kind = RelationshipKind.References, Role = null,
            Strength = RelationshipStrength.Strong, IsRuntimeBinding = false
        };
        DescriptorImpactAnalyzer.IsAdvisory(edge).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "FullyQualifiedName~DescriptorImpactSeverityTests"`
Expected: 14 tests PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorImpact/DescriptorImpactSeverityTests.cs
git commit -m "test(6c): add severity table tests (14 tests)"
```

---

## Task 14: Regression Verification

- [ ] **Step 1: Full solution build**

Run: `dotnet build`
Expected: PASS (0 errors)

- [ ] **Step 2: Run all Metadata.Tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj`
Expected: All ~192 tests PASS (146 existing + ~46 new)

- [ ] **Step 3: Run cross-module regression suites**

Run:
```
dotnet test framework/test/CrestCreates.Form.Tests/CrestCreates.Form.Tests.csproj
dotnet test framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj
dotnet test framework/test/CrestCreates.Event.Tests/CrestCreates.Event.Tests.csproj
dotnet test framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj
dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj
```
Expected: 0 regressions across Form (38), Capability (124), Event (41), HumanTask (51), Workflow (68)

- [ ] **Step 4: Verify legacy DescriptorCatalog.AnalyzeImpact() still passes**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj --filter "AnalyzeImpact"`
Expected: Any existing AnalyzeImpact tests PASS (adapter unchanged)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore(6c): regression verification — all 468+ tests pass, 0 cross-module regressions"
```

---

## Task 15: Update memory.md

**Files:**
- Modify: `memory.md`

- [ ] **Step 1: Append Phase 6c completed entry**

Append to the `### Completed Features` section or the platform status section:

```markdown
### Impact Analysis Engine (Phase 6c, 2026-06-13)

- `IDescriptorImpactAnalyzer` — consumes `DescriptorTopologySnapshot` + `DescriptorChangeSet`, BFS upstream traversal, per-terminal-segment severity, fan-out-safe unpinned resolution, advisory edge filtering, depth limiting.
- `IDescriptorChangeSetBuilder` — diffs `before`/`after` `IReadOnlyList<IDescriptor>` inventories into `DescriptorChangeSet` with state-aware transition detection and priority dedup.
- Core types (17 files): `DescriptorChange`, `DescriptorChangeSet`, `DescriptorImpactPathSegment`, `DescriptorImpactPath`, `AffectedDescriptor`, `DescriptorImpactDiagnostic`, `DescriptorImpactAnalysisReport`, 3 enums, 2 options/interface files.
- 3 diagnostic code categories: `IMPACT_TOPOLOGY_*` (re-mapped from topology snapshot), `IMPACT_*` (impact-native: AMBIGUOUS_UNPINNED_TARGET, UNRESOLVED_CONSUMER, PATH_TRUNCATED, SKIPPED_WEAK_PATH).
- 46 new tests across 3 test files. 0 regressions across 6 suites.
- `AddDescriptorImpactAnalysis()` DI registration (TryAddSingleton for both services).
- No changes to Phase 6a/6b types or legacy `DescriptorCatalog.AnalyzeImpact()`.
```

- [ ] **Step 2: Update Last Updated date**

Change `Last Updated: 2026-06-12` to `Last Updated: 2026-06-13`.

- [ ] **Step 3: Commit**

```bash
git add memory.md
git commit -m "docs: update memory.md with Phase 6c completion"
```

---

## Plan Summary

| Task | Files | Tests | Est. Time |
|---|---|---|---|
| 1. Enums | 3 new | 0 | 5 min |
| 2. Change Set Types | 2 new | 0 | 5 min |
| 3. Path Types | 2 new | 0 | 5 min |
| 4. Report Types | 3 new | 0 | 5 min |
| 5. Options & Interfaces | 3 new | 0 | 5 min |
| 6. ChangeSetBuilder | 1 new | 0 | 10 min |
| 7. ChangeSetBuilder Tests | 1 new | 11 | 10 min |
| 8. Analyzer (Helpers) | 1 new | 0 | 10 min |
| 9. Analyzer (BFS) | 1 modify | 0 | 15 min |
| 10. DI Registration | 1 modify | 0 | 5 min |
| 11. Analyzer Tests (Core) | 1 new | 11 | 10 min |
| 12. Analyzer Tests (Advanced) | 1 modify | 10 | 10 min |
| 13. Severity Tests | 1 new | 14 | 10 min |
| 14. Regression | 0 | all | 10 min |
| 15. memory.md | 1 modify | 0 | 5 min |

**Total: ~17 new files, 3 modified files, ~46 new tests, ~2 hours**
