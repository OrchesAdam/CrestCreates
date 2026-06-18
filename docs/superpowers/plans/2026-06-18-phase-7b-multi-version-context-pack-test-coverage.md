# Phase 7b Multi-version Context Pack Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 20 multi-version test methods to MetadataContextPackBuilderTests, organized in groups K–P, proving that the ContextPack builder never collapses version identity, resolves by exact version, preserves versioned refs through traversal, maintains deterministic ordering, and produces canonical output consistently.

**Architecture:** All tests go into the existing test file. A shared `AssertRelationshipsClosedOverDescriptors` helper is added once and reused across groups. Each group follows the existing pattern: arrange topology via `CreateSnapshot`/`CreateSnapshotWithState`, arrange inventory via `VersionedTestDescriptor`, act via `_builder.Build()`, assert via FluentAssertions.

**Tech Stack:** xUnit 2.9.3, FluentAssertions, Moq, .NET 10

## Global Constraints

- Test file: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs`
- All new tests use `VersionedTestDescriptor` (existing private class) for versioned inventory items
- All new tests use `CreateSnapshot`/`CreateSnapshotWithState` (existing private helpers) for topology
- No changes to production code unless a test exposes a necessary bug fix
- `DescriptorRef` constructor: `new DescriptorRef(string Namespace, string Id, int? Version = null)`
- Builder sorts: `IsFocus desc, Namespace, Id, Version ?? -1` for descriptors; `From.{Namespace,Id,Version}, To.{Namespace,Id,Version}, Kind` for relationships
- `MetadataContextPackDiagnosticCodes` constants: `AmbiguousDescriptorRef`, `DescriptorMissingForTopologyRef`, `FocusNotFound`, `TruncatedByCount`, `KindExcluded`
- Commit convention: `test(context-pack): <description> (#37)`

---

### Task 1: Shared Helper

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs` (add helper method)

**Interfaces:**
- Produces: `AssertRelationshipsClosedOverDescriptors(MetadataContextPack pack)` — static private method reused by Tasks 2–7

- [ ] **Step 1: Add the shared assertion helper**

Insert this method right after the `CreateDescriptors` method (after line 100), before `TestDescriptor`:

```csharp
private static void AssertRelationshipsClosedOverDescriptors(MetadataContextPack pack)
{
    var descriptorRefs = pack.Descriptors.Select(d => d.Ref).ToHashSet();
    pack.Relationships.Should().OnlyContain(r =>
        descriptorRefs.Contains(r.From) && descriptorRefs.Contains(r.To));
}
```

- [ ] **Step 2: Run existing tests to verify no regressions**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "FullyQualifiedName~MetadataContextPackBuilderTests" --no-build`
Expected: All 45 existing tests PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add AssertRelationshipsClosedOverDescriptors helper (#37)"
```

---

### Task 2: Group K — Version Identity Preservation (5 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs` (add 5 test methods after group J)

**Interfaces:**
- Consumes: `AssertRelationshipsClosedOverDescriptors` from Task 1
- Consumes: `CreateSnapshot`, `CreateSnapshotWithState`, `VersionedTestDescriptor`, `NoEdges`, `_builder` from existing test class

- [ ] **Step 1: Add K group tests**

Insert after the last test in group J (after the `Unpinned_Edge_Endpoints_With_Single_Version_Descriptors_Keep_Canonical_Relationship` test, before `InventoryOnlyDescriptor` class):

```csharp
// ── K. Version Identity Preservation ──

[Fact]
public void FocusOnly_With_TwoVersions_Resolves_Exact_Version()
{
    var v1Ref = new DescriptorRef("capability", "SubmitCap", 1);
    var v2Ref = new DescriptorRef("capability", "SubmitCap", 2);

    var topology = CreateSnapshot(
        new[] { (v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        NoEdges);

    var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { v1Desc, v2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { v2Ref }
    };

    var pack = _builder.Build(request, topology, descriptors);

    pack.Descriptors.Should().ContainSingle();
    pack.Descriptors[0].Ref.Should().Be(v2Ref);
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(v1Ref);
}

[Fact]
public void DirectDependencies_Preserves_Dependency_Version()
{
    var wfV1 = new DescriptorRef("workflow", "ApprovalWf", 1);
    var wfV2 = new DescriptorRef("workflow", "ApprovalWf", 2);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);

    var topology = CreateSnapshot(
        new[] { (wfV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (wfV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, wfV1, capV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, wfV2, capV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var wfV1Desc = new VersionedTestDescriptor(wfV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var wfV2Desc = new VersionedTestDescriptor(wfV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 2);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { wfV1Desc, wfV2Desc, capV1Desc, capV2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { wfV2 }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Workflow@v2 + CapA@v2 only, not CapA@v1
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { wfV2, capV2 });
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(capV1);
    // Relationships: only Workflow@v2→CapA@v2
    pack.Relationships.Should().ContainSingle();
    pack.Relationships[0].From.Should().Be(wfV2);
    pack.Relationships[0].To.Should().Be(capV2);
    // Summary focus refs are versioned
    pack.Summary.FocusRefs.Should().BeEquivalentTo(new[] { wfV2 });
    AssertRelationshipsClosedOverDescriptors(pack);
}

[Fact]
public void DirectDependents_Preserves_Dependent_Version()
{
    var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);
    var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);

    var topology = CreateSnapshot(
        new[] { (schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, capV1, schemaV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, capV2, schemaV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var schemaV1Desc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
    var schemaV2Desc = new VersionedTestDescriptor(schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { schemaV1Desc, schemaV2Desc, capV1Desc, capV2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependents,
        FocusDescriptors = new[] { schemaV2 }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Schema@v2 + CapA@v2 only, not CapA@v1
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { schemaV2, capV2 });
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(capV1);
    // Relationships: only CapA@v2→Schema@v2
    pack.Relationships.Should().ContainSingle();
    pack.Relationships[0].From.Should().Be(capV2);
    pack.Relationships[0].To.Should().Be(schemaV2);
    pack.Summary.FocusRefs.Should().BeEquivalentTo(new[] { schemaV2 });
    AssertRelationshipsClosedOverDescriptors(pack);
}

[Fact]
public void ImpactRadius_Does_Not_Collapse_SameId_DifferentVersions()
{
    // Two parallel chains: v1 lane and v2 lane
    var rootV1 = new DescriptorRef("workflow", "ApprovalWf", 1);
    var rootV2 = new DescriptorRef("workflow", "ApprovalWf", 2);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);

    var topology = CreateSnapshot(
        new[] { (rootV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (rootV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, rootV1, capV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, rootV2, capV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var rootV1Desc = new VersionedTestDescriptor(rootV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var rootV2Desc = new VersionedTestDescriptor(rootV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 2);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { rootV1Desc, rootV2Desc, capV1Desc, capV2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.ImpactRadius,
        FocusDescriptors = new[] { rootV2 },
        MaxTraversalDepth = 1
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Traversal stays within v2 lane — no v1 descriptors or relationships
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { rootV2, capV2 });
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(rootV1);
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(capV1);
    pack.Relationships.Should().ContainSingle();
    pack.Relationships[0].From.Should().Be(rootV2);
    pack.Relationships[0].To.Should().Be(capV2);
    pack.Summary.FocusRefs.Should().BeEquivalentTo(new[] { rootV2 });
    AssertRelationshipsClosedOverDescriptors(pack);
}

[Fact]
public void InventoryOnly_MultiVersion_Focus_Keeps_Versions_Distinct()
{
    var v1Ref = new DescriptorRef("capability", "SubmitCap", 1);
    var v2Ref = new DescriptorRef("capability", "SubmitCap", 2);

    // No topology nodes for focus
    var topology = CreateSnapshot(
        Array.Empty<(DescriptorRef, DescriptorKind, string)>(),
        NoEdges);

    var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { v1Desc, v2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { v2Ref }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Only v2 descriptor entry, not v1
    pack.Descriptors.Should().ContainSingle();
    pack.Descriptors[0].Ref.Should().Be(v2Ref);
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(v1Ref);
}
```

- [ ] **Step 2: Run K group tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "FullyQualifiedName~MetadataContextPackBuilderTests.FocusOnly_With_TwoVersions_Resolves_Exact_Version|FullyQualifiedName~MetadataContextPackBuilderTests.DirectDependencies_Preserves_Dependency_Version|FullyQualifiedName~MetadataContextPackBuilderTests.DirectDependents_Preserves_Dependent_Version|FullyQualifiedName~MetadataContextPackBuilderTests.ImpactRadius_Does_Not_Collapse_SameId_DifferentVersions|FullyQualifiedName~MetadataContextPackBuilderTests.InventoryOnly_MultiVersion_Focus_Keeps_Versions_Distinct"`
Expected: All 5 PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add group K — version identity preservation tests (#37)"
```

---

### Task 3: Group L — Exact Version Resolution and Unpinned Ref Ambiguity (2 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs` (add 2 test methods after group K)

**Interfaces:**
- Consumes: `CreateSnapshot`, `VersionedTestDescriptor`, `NoEdges`, `_builder` from existing test class

- [ ] **Step 1: Add L group tests**

Insert after group K tests:

```csharp
// ── L. Exact Version Resolution and Unpinned Ref Ambiguity ──

[Fact]
public void Unpinned_Edge_Target_Multiple_Versions_Does_Not_Guess()
{
    var cap = new DescriptorRef("capability", "SubmitCap");
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var schemaUnpinned = new DescriptorRef("schema", "InputSchema");
    var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);
    var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);

    var topology = CreateSnapshot(
        new[] { (cap, DescriptorKind.Capability, "SubmitCap"),
                (schemaUnpinned, DescriptorKind.Schema, "InputSchema") },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, cap, schemaUnpinned, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    // Focus cap resolves to v1; schema has v1+v2 in inventory — unpinned target is ambiguous
    var capDesc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var schemaV1Desc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
    var schemaV2Desc = new VersionedTestDescriptor(schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { capDesc, schemaV1Desc, schemaV2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { cap }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Focus descriptor is still present (canonicalized to v1)
    pack.Descriptors.Select(d => d.Ref).Should().Contain(capV1);
    // Target v1/v2 are not in Descriptors — ambiguous, not guessed
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(schemaV1);
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(schemaV2);
    // Ambiguous diagnostic emitted for the target
    pack.Diagnostics.Should().Contain(d =>
        d.Code == MetadataContextPackDiagnosticCodes.AmbiguousDescriptorRef);
    // Relationship excluded by pack closure
    pack.Relationships.Should().BeEmpty();
}

[Fact]
public void Missing_Exact_Version_Does_Not_Fallback_To_Another_Version()
{
    var v1Ref = new DescriptorRef("capability", "SubmitCap", 1);
    var v2Ref = new DescriptorRef("capability", "SubmitCap", 2);
    var v3Ref = new DescriptorRef("capability", "SubmitCap", 3);

    // Topology has v3 node
    var topology = CreateSnapshot(
        new[] { (v3Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        NoEdges);

    // Inventory has v1+v2 only — no v3 descriptor
    var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { v1Desc, v2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { v3Ref }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // No CapA entry — not v1, not v2 (no fallback)
    pack.Descriptors.Should().BeEmpty();
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(v1Ref);
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(v2Ref);
    // DescriptorMissingForTopologyRef diagnostic
    pack.Diagnostics.Should().Contain(d =>
        d.Code == MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef);
}
```

- [ ] **Step 2: Run L group tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "FullyQualifiedName~MetadataContextPackBuilderTests.Unpinned_Edge_Target_Multiple_Versions_Does_Not_Guess|FullyQualifiedName~MetadataContextPackBuilderTests.Missing_Exact_Version_Does_Not_Fallback_To_Another_Version"`
Expected: All 2 PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add group L — exact version resolution and unpinned ref ambiguity (#37)"
```

---

### Task 4: Group M — Version-aware Traversal (3 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs` (add 3 test methods after group L)

**Interfaces:**
- Consumes: `AssertRelationshipsClosedOverDescriptors` from Task 1
- Consumes: `CreateSnapshot`, `VersionedTestDescriptor`, `_builder` from existing test class

- [ ] **Step 1: Add M group tests**

Insert after group L tests:

```csharp
// ── M. Version-aware Traversal ──

[Fact]
public void RuntimeScenario_Preserves_Versioned_Boundary_Between_Steps()
{
    var wfV1 = new DescriptorRef("workflow", "ApprovalWf", 1);
    var wfV2 = new DescriptorRef("workflow", "ApprovalWf", 2);
    var htV1 = new DescriptorRef("humantask", "ReviewHt", 1);
    var htV2 = new DescriptorRef("humantask", "ReviewHt", 2);

    var topology = CreateSnapshot(
        new[] { (wfV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (wfV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (htV1, DescriptorKind.HumanTask, "ReviewHt", DescriptorState.Active),
                (htV2, DescriptorKind.HumanTask, "ReviewHt", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, wfV1, htV1, RelationshipKind.Triggers, "Step1", RelationshipStrength.Strong, true),
            (1, wfV2, htV2, RelationshipKind.Triggers, "Step1", RelationshipStrength.Strong, true)
        });

    var wfV1Desc = new VersionedTestDescriptor(wfV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var wfV2Desc = new VersionedTestDescriptor(wfV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 2);
    var htV1Desc = new VersionedTestDescriptor(htV1, DescriptorKind.HumanTask, "ReviewHt", DescriptorState.Active, 1);
    var htV2Desc = new VersionedTestDescriptor(htV2, DescriptorKind.HumanTask, "ReviewHt", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { wfV1Desc, wfV2Desc, htV1Desc, htV2Desc };

    var recipe = new RuntimeScenarioRecipe
    {
        Name = "VersionBoundary",
        Steps = new[]
        {
            new ScenarioTraversalStep
            {
                FollowKind = RelationshipKind.Triggers,
                Direction = ScenarioTraversalDirection.Dependencies,
                MaxDepth = 1
            }
        }
    };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.RuntimeScenario,
        FocusDescriptors = new[] { wfV2 },
        ScenarioRecipe = recipe
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Step 1 discovers HumanTask@v2 only; v1 and unpinned equivalents not used
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { wfV2, htV2 });
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(htV1);
    pack.Relationships.Should().ContainSingle();
    pack.Relationships[0].To.Should().Be(htV2);
    AssertRelationshipsClosedOverDescriptors(pack);
}

[Fact]
public void RuntimeScenario_MultiStep_Preserves_Versioned_Relationships()
{
    var wfV1 = new DescriptorRef("workflow", "ApprovalWf", 1);
    var wfV2 = new DescriptorRef("workflow", "ApprovalWf", 2);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);

    var topology = CreateSnapshot(
        new[] { (wfV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (wfV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, wfV1, capV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, wfV2, capV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var wfV1Desc = new VersionedTestDescriptor(wfV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var wfV2Desc = new VersionedTestDescriptor(wfV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 2);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { wfV1Desc, wfV2Desc, capV1Desc, capV2Desc };

    var recipe = new RuntimeScenarioRecipe
    {
        Name = "MultiStepVersion",
        Steps = new[]
        {
            new ScenarioTraversalStep
            {
                FollowKind = RelationshipKind.Uses,
                Direction = ScenarioTraversalDirection.Dependencies,
                MaxDepth = 1
            }
        }
    };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.RuntimeScenario,
        FocusDescriptors = new[] { wfV2 },
        ScenarioRecipe = recipe
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Relationship endpoints are canonical versioned refs (Workflow@v2, CapA@v2)
    pack.Relationships.Should().ContainSingle();
    pack.Relationships[0].From.Should().Be(wfV2);
    pack.Relationships[0].To.Should().Be(capV2);
    // No v1 relationships appear
    pack.Relationships.Should().NotContain(r => r.From.Equals(wfV1) || r.To.Equals(capV1));
    AssertRelationshipsClosedOverDescriptors(pack);
}

[Fact]
public void ImpactRadius_MultiVersion_Traversal_Preserves_Versioned_Edge_Endpoints()
{
    var rootV1 = new DescriptorRef("workflow", "ApprovalWf", 1);
    var rootV2 = new DescriptorRef("workflow", "ApprovalWf", 2);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);
    var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);
    var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);

    var topology = CreateSnapshot(
        new[] { (rootV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (rootV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, rootV1, capV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, rootV2, capV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (2, capV1, schemaV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (3, capV2, schemaV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var rootV1Desc = new VersionedTestDescriptor(rootV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var rootV2Desc = new VersionedTestDescriptor(rootV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 2);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var schemaV1Desc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
    var schemaV2Desc = new VersionedTestDescriptor(schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { rootV1Desc, rootV2Desc, capV1Desc, capV2Desc, schemaV1Desc, schemaV2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.ImpactRadius,
        FocusDescriptors = new[] { rootV2 },
        MaxTraversalDepth = 2
    };

    var pack = _builder.Build(request, topology, descriptors);

    // BFS stays on v2 channel
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { rootV2, capV2, schemaV2 });
    // All relationship endpoint refs preserve version
    pack.Relationships.Should().OnlyContain(r =>
        r.From.Version == 2 && r.To.Version == 2);
    AssertRelationshipsClosedOverDescriptors(pack);
}
```

- [ ] **Step 2: Run M group tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "FullyQualifiedName~MetadataContextPackBuilderTests.RuntimeScenario_Preserves_Versioned_Boundary_Between_Steps|FullyQualifiedName~MetadataContextPackBuilderTests.RuntimeScenario_MultiStep_Preserves_Versioned_Relationships|FullyQualifiedName~MetadataContextPackBuilderTests.ImpactRadius_MultiVersion_Traversal_Preserves_Versioned_Edge_Endpoints"`
Expected: All 3 PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add group M — version-aware traversal tests (#37)"
```

---

### Task 5: Group N — Deterministic Ordering and Bounds Under Multi-version Inventory (5 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs` (add 5 test methods after group M)

**Interfaces:**
- Consumes: `CreateSnapshot`, `VersionedTestDescriptor`, `_builder` from existing test class

- [ ] **Step 1: Add N group tests**

Insert after group M tests:

```csharp
// ── N. Deterministic Ordering and Bounds Under Multi-version Inventory ──

[Fact]
public void IncludeKinds_Does_Not_Collapse_MultipleVersions()
{
    var wf = new DescriptorRef("workflow", "ApprovalWf", 1);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);

    var topology = CreateSnapshot(
        new[] { (wf, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, wf, capV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, wf, capV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var wfDesc = new VersionedTestDescriptor(wf, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { wfDesc, capV1Desc, capV2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { wf },
        IncludeKinds = new[] { DescriptorKind.Capability }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Both CapA versions remain — not collapsed
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { wf, capV1, capV2 });
}

[Fact]
public void ExcludeKinds_Does_Not_Change_VersionIdentity()
{
    var wf = new DescriptorRef("workflow", "ApprovalWf", 1);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);
    var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);

    var topology = CreateSnapshot(
        new[] { (wf, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, wf, capV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, wf, schemaV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (2, wf, schemaV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var wfDesc = new VersionedTestDescriptor(wf, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var schemaV1Desc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
    var schemaV2Desc = new VersionedTestDescriptor(schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { wfDesc, capV1Desc, schemaV1Desc, schemaV2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { wf },
        ExcludeKinds = new[] { DescriptorKind.Schema }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Both Schema versions excluded, CapA@v1 remains
    pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { wf, capV1 });
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(schemaV1);
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(schemaV2);
}

[Fact]
public void MaxDescriptorCount_Truncates_Deterministically_With_MultipleVersions()
{
    var wf = new DescriptorRef("workflow", "ApprovalWf", 1);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);
    var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);
    var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);

    var topology = CreateSnapshot(
        new[] { (wf, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, wf, capV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, wf, capV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (2, wf, schemaV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (3, wf, schemaV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var wfDesc = new VersionedTestDescriptor(wf, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var schemaV1Desc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
    var schemaV2Desc = new VersionedTestDescriptor(schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);

    // Build twice with shuffled inventory to verify deterministic stability
    var descriptors1 = new List<IDescriptor> { wfDesc, capV1Desc, capV2Desc, schemaV1Desc, schemaV2Desc };
    var descriptors2 = new List<IDescriptor> { schemaV2Desc, capV2Desc, wfDesc, schemaV1Desc, capV1Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { wf },
        MaxDescriptorCount = 3  // wf (focus) + 2 non-focus
    };

    var pack1 = _builder.Build(request, topology, descriptors1);
    var pack2 = _builder.Build(request, topology, descriptors2);

    // Focus always retained
    pack1.Descriptors.Should().Contain(d => d.Ref.Equals(wf));
    // Same retained set across shuffled inputs
    pack1.Descriptors.Select(d => d.Ref).Should().Equal(pack2.Descriptors.Select(d => d.Ref).ToArray());
    pack1.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByCount);
}

[Fact]
public void Deterministic_Ordering_With_MultipleVersions_SameKind()
{
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);
    var capB = new DescriptorRef("capability", "ApproveCap", 1);

    var topology = CreateSnapshot(
        new[] { (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capB, DescriptorKind.Capability, "ApproveCap", DescriptorState.Active) },
        NoEdges);

    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var capBDesc = new VersionedTestDescriptor(capB, DescriptorKind.Capability, "ApproveCap", DescriptorState.Active, 1);

    // Two builds with shuffled input
    var descriptors1 = new List<IDescriptor> { capV2Desc, capBDesc, capV1Desc };
    var descriptors2 = new List<IDescriptor> { capV1Desc, capV2Desc, capBDesc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { capV1, capV2, capB }
    };

    var pack1 = _builder.Build(request, topology, descriptors1);
    var pack2 = _builder.Build(request, topology, descriptors2);

    // Output order is stable across shuffled input
    pack1.Descriptors.Select(d => d.Ref).Should().Equal(pack2.Descriptors.Select(d => d.Ref).ToArray());
}

[Fact]
public void Relationships_With_MultipleVersions_Are_Not_Deduped_By_Unversioned_Id()
{
    var wfV1 = new DescriptorRef("workflow", "ApprovalWf", 1);
    var wfV2 = new DescriptorRef("workflow", "ApprovalWf", 2);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);
    var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);

    var topology = CreateSnapshot(
        new[] { (wfV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (wfV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, wfV1, capV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, wfV2, capV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (2, wfV2, schemaV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var wfV1Desc = new VersionedTestDescriptor(wfV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var wfV2Desc = new VersionedTestDescriptor(wfV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 2);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var schemaV2Desc = new VersionedTestDescriptor(schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { wfV1Desc, wfV2Desc, capV1Desc, capV2Desc, schemaV2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { wfV1, wfV2 }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // v1/v2 relationships remain distinct — not deduped by unversioned id
    pack.Relationships.Should().HaveCount(3);
    // Relationships ordered by canonical From/To refs
    var relRefs = pack.Relationships.Select(r => (r.From, r.To, r.Kind)).ToList();
    relRefs.Should().Contain((wfV1, capV1, RelationshipKind.Uses));
    relRefs.Should().Contain((wfV2, capV2, RelationshipKind.Uses));
    relRefs.Should().Contain((wfV2, schemaV2, RelationshipKind.Uses));
}
```

- [ ] **Step 2: Run N group tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "FullyQualifiedName~MetadataContextPackBuilderTests.IncludeKinds_Does_Not_Collapse_MultipleVersions|FullyQualifiedName~MetadataContextPackBuilderTests.ExcludeKinds_Does_Not_Change_VersionIdentity|FullyQualifiedName~MetadataContextPackBuilderTests.MaxDescriptorCount_Truncates_Deterministically_With_MultipleVersions|FullyQualifiedName~MetadataContextPackBuilderTests.Deterministic_Ordering_With_MultipleVersions_SameKind|FullyQualifiedName~MetadataContextPackBuilderTests.Relationships_With_MultipleVersions_Are_Not_Deduped_By_Unversioned_Id"`
Expected: All 5 PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add group N — deterministic ordering and bounds under multi-version (#37)"
```

---

### Task 6: Group O — Canonical Output Consistency and Pack Closure (3 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs` (add 3 test methods after group N)

**Interfaces:**
- Consumes: `AssertRelationshipsClosedOverDescriptors` from Task 1
- Consumes: `CreateSnapshot`, `VersionedTestDescriptor`, `_builder` from existing test class

- [ ] **Step 1: Add O group tests**

Insert after group N tests:

```csharp
// ── O. Canonical Output Consistency and Pack Closure ──

[Fact]
public void Summary_FocusRefs_Are_Canonicalized_For_Unpinned_SingleVersion_Focus()
{
    var unpinnedRef = new DescriptorRef("capability", "SubmitCap");
    var versionedRef = new DescriptorRef("capability", "SubmitCap", 1);

    var topology = CreateSnapshot(
        new[] { (unpinnedRef, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        NoEdges);

    var v1Desc = new VersionedTestDescriptor(versionedRef, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var descriptors = new List<IDescriptor> { v1Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { unpinnedRef }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Summary.FocusRefs matches descriptor entry Ref (both canonical versioned)
    pack.Descriptors.Should().ContainSingle();
    pack.Descriptors[0].Ref.Should().Be(versionedRef);
    pack.Summary.FocusRefs.Should().BeEquivalentTo(new[] { versionedRef });
    pack.Summary.FocusRefs[0].Version.Should().Be(1);
}

[Fact]
public void Relationships_Are_Closed_Over_Output_DescriptorRefs()
{
    // Multi-version scenario with mixed resolution to test pack closure
    var wf = new DescriptorRef("workflow", "ApprovalWf", 1);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);
    var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);
    // SchemaV2 has topology node but no inventory descriptor — relationship to it must be excluded

    var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);

    var topology = CreateSnapshot(
        new[] { (wf, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, wf, capV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, wf, capV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (2, wf, schemaV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (3, wf, schemaV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var wfDesc = new VersionedTestDescriptor(wf, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var schemaV1Desc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
    // No descriptor for schemaV2
    var descriptors = new List<IDescriptor> { wfDesc, capV1Desc, capV2Desc, schemaV1Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { wf }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Explicit pack closure check: every relationship From/To exists in descriptor refs
    AssertRelationshipsClosedOverDescriptors(pack);
    // SchemaV2 is not in descriptors → its relationship is excluded
    pack.Descriptors.Select(d => d.Ref).Should().NotContain(schemaV2);
    // Only relationships with both endpoints in descriptor set
    var descriptorRefs = pack.Descriptors.Select(d => d.Ref).ToHashSet();
    foreach (var rel in pack.Relationships)
    {
        descriptorRefs.Contains(rel.From).Should().BeTrue();
        descriptorRefs.Contains(rel.To).Should().BeTrue();
    }
}

[Fact]
public void Canonical_Ref_Consistency_Across_Descriptors_Relationships_Summary()
{
    var capUnpinned = new DescriptorRef("capability", "SubmitCap");
    var schemaUnpinned = new DescriptorRef("schema", "InputSchema");
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);

    var topology = CreateSnapshot(
        new[] { (capUnpinned, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (schemaUnpinned, DescriptorKind.Schema, "InputSchema", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, capUnpinned, schemaUnpinned, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var capDesc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var schemaDesc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
    var descriptors = new List<IDescriptor> { capDesc, schemaDesc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = new[] { capUnpinned }
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Descriptor entries use canonical versioned refs
    var capEntry = pack.Descriptors.First(d => d.Ref.Id == "SubmitCap");
    capEntry.Ref.Should().Be(capV1);
    var schemaEntry = pack.Descriptors.First(d => d.Ref.Id == "InputSchema");
    schemaEntry.Ref.Should().Be(schemaV1);

    // Relationship endpoints use canonical versioned refs
    pack.Relationships.Should().ContainSingle();
    pack.Relationships[0].From.Should().Be(capV1);
    pack.Relationships[0].To.Should().Be(schemaV1);

    // Summary focus refs use canonical versioned refs
    pack.Summary.FocusRefs.Should().BeEquivalentTo(new[] { capV1 });

    // Pack closure invariant
    AssertRelationshipsClosedOverDescriptors(pack);
}
```

- [ ] **Step 2: Run O group tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "FullyQualifiedName~MetadataContextPackBuilderTests.Summary_FocusRefs_Are_Canonicalized_For_Unpinned_SingleVersion_Focus|FullyQualifiedName~MetadataContextPackBuilderTests.Relationships_Are_Closed_Over_Output_DescriptorRefs|FullyQualifiedName~MetadataContextPackBuilderTests.Canonical_Ref_Consistency_Across_Descriptors_Relationships_Summary"`
Expected: All 3 PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add group O — canonical output consistency and pack closure (#37)"
```

---

### Task 7: Group P — Enrichment Under Multi-Version (2 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs` (add 2 test methods after group O)

**Interfaces:**
- Consumes: `CreateSnapshot`, `VersionedTestDescriptor`, `_builder` from existing test class
- Consumes: `Mock<IDescriptorStableHashBuilder>` pattern from existing F1 test
- Consumes: `DefaultMetadataContextPackBuilder(IDescriptorStableHashBuilder?)` constructor

- [ ] **Step 1: Add P group tests**

Insert after group O tests (before the `InventoryOnlyDescriptor` class):

```csharp
// ── P. Enrichment Under Multi-Version ──

[Fact]
public void StableHashes_Computed_For_Selected_Exact_Version_In_Traversal()
{
    var rootV1 = new DescriptorRef("workflow", "ApprovalWf", 1);
    var rootV2 = new DescriptorRef("workflow", "ApprovalWf", 2);
    var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
    var capV2 = new DescriptorRef("capability", "SubmitCap", 2);

    var topology = CreateSnapshot(
        new[] { (rootV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (rootV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
            (0, rootV1, capV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
            (1, rootV2, capV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
        });

    var rootV1Desc = new VersionedTestDescriptor(rootV1, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 1);
    var rootV2Desc = new VersionedTestDescriptor(rootV2, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active, 2);
    var capV1Desc = new VersionedTestDescriptor(capV1, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
    var capV2Desc = new VersionedTestDescriptor(capV2, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { rootV1Desc, rootV2Desc, capV1Desc, capV2Desc };

    // Track which descriptor instances the hash builder receives
    var receivedDescriptors = new List<IDescriptor>();
    var hashBuilderMock = new Mock<IDescriptorStableHashBuilder>();
    hashBuilderMock.Setup(h => h.Build(It.IsAny<IDescriptor>()))
        .Callback<IDescriptor>(d => receivedDescriptors.Add(d))
        .Returns(new DescriptorStableHashes("contract", "definition"));

    var builder = new DefaultMetadataContextPackBuilder(hashBuilderMock.Object);

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.ImpactRadius,
        FocusDescriptors = new[] { rootV2 },
        MaxTraversalDepth = 1,
        IncludeStableHashes = true
    };

    var pack = builder.Build(request, topology, descriptors);

    // Hash builder receives v2 instances, not v1
    receivedDescriptors.Should().Contain(d => ReferenceEquals(d, rootV2Desc));
    receivedDescriptors.Should().Contain(d => ReferenceEquals(d, capV2Desc));
    receivedDescriptors.Should().NotContain(d => ReferenceEquals(d, rootV1Desc));
    receivedDescriptors.Should().NotContain(d => ReferenceEquals(d, capV1Desc));
}

[Fact]
public void GovernanceEntry_Uses_Selected_Descriptor_Version_State()
{
    var v1Ref = new DescriptorRef("capability", "SubmitCap", 1);
    var v2Ref = new DescriptorRef("capability", "SubmitCap", 2);

    var topology = CreateSnapshot(
        new[] { (v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Draft),
                (v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
        NoEdges);

    var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Draft, 1);
    var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
    var descriptors = new List<IDescriptor> { v1Desc, v2Desc };

    var request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = new[] { v2Ref },
        IncludeGovernanceState = true
    };

    var pack = _builder.Build(request, topology, descriptors);

    // Governance entry shows Active (v2), not Draft (v1)
    pack.Descriptors.Should().ContainSingle();
    var entry = pack.Descriptors[0];
    entry.Ref.Should().Be(v2Ref);
    entry.Governance.Should().NotBeNull();
    entry.Governance!.State.Should().Be(DescriptorState.Active);
    entry.Governance.RequiresReview.Should().BeFalse();
    // This test does not validate lifecycle governance rules.
    // It only verifies that lightweight governance state is populated from the selected exact descriptor version.
}
```

- [ ] **Step 2: Run P group tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --filter "FullyQualifiedName~MetadataContextPackBuilderTests.StableHashes_Computed_For_Selected_Exact_Version_In_Traversal|FullyQualifiedName~MetadataContextPackBuilderTests.GovernanceEntry_Uses_Selected_Descriptor_Version_State"`
Expected: All 2 PASS

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs
git commit -m "test(context-pack): add group P — enrichment under multi-version (#37)"
```

---

### Task 8: Full Suite Verification and Final Commit

**Files:**
- No file changes — verification only

- [ ] **Step 1: Run full test suite**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests`
Expected: 65 tests PASS (45 existing + 20 new)

- [ ] **Step 2: Verify test count**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --list-tests 2>&1 | grep -c "MetadataContextPackBuilderTests"`
Expected: 65

- [ ] **Step 3: Verify all acceptance criteria are covered by checking test names**

Run: `dotnet test framework/test/CrestCreates.Metadata.ContextPack.Tests --list-tests 2>&1 | grep "MetadataContextPackBuilderTests" | grep -E "(K|L|M|N|O|P)_|FocusOnly_With_TwoVersions|DirectDependencies_Preserves_Dependency|DirectDependents_Preserves_Dependent|ImpactRadius_Does_Not_Collapse|InventoryOnly_MultiVersion|Unpinned_Edge_Target_Multiple|Missing_Exact_Version|RuntimeScenario_Preserves_Versioned_Boundary|RuntimeScenario_MultiStep_Preserves|ImpactRadius_MultiVersion_Traversal|IncludeKinds_Does_Not_Collapse|ExcludeKinds_Does_Not_Change|MaxDescriptorCount_Truncates_Deterministically|Deterministic_Ordering_With_MultipleVersions|Relationships_With_MultipleVersions_Are_Not|Summary_FocusRefs_Are_Canonicalized|Relationships_Are_Closed_Over|Canonical_Ref_Consistency|StableHashes_Computed_For_Selected|GovernanceEntry_Uses_Selected"`
Expected: 20 matching test names
