# Boundary Snapshot and Package Diagnostic Severity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve #46 and #35 by replacing package diagnostic severity with a package-domain enum and migrating boundary defensive-copy contracts to `ISnapshotable<T>.Snapshot()`.

**Architecture:** `Snapshot()` becomes the only formal boundary-copy verb for models that cross store, runtime, package, authoring, or memory boundaries. The migration is contract cleanup only: no review, materialization, activation, registry, Control Plane, runtime hot reload, or Agent Memory behavior redesign.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, central project references, existing `CrestCreates.Snapshot.Abstractions`.

## Global Constraints

- Use the approved spec at `docs/superpowers/specs/2026-06-30-boundary-snapshot-and-package-diagnostic-severity-design.md`.
- Do not keep `[Obsolete]` compatibility bridges for removed `Clone()` or `CreateClone()` methods.
- Do not migrate unrelated diagnostic families to `DescriptorPackageDiagnosticSeverity`.
- `EvidenceFinding`, topology diagnostics, impact diagnostics, compatibility diagnostics, lifecycle findings, Agent Memory diagnostics, and review report items continue using `SeverityLevel`.
- `Snapshot()` must be deterministic, side-effect free, and must not perform validation, authorization, persistence, sanitization, canonical hash recomputation, lifecycle transition, or registry mutation.
- Unknown `object?` payload fields such as workflow variables, workflow step variables, and HumanTask input/output are not deep-cloned unless the existing model already provided that guarantee.
- Do not change Control Plane behavior, runtime registry mutation, runtime hot reload, Agent Memory scoring, compression, sanitization, promotion, source expansion, or canonical hash behavior.

---

## File Structure

- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageDiagnosticSeverity.cs`: new package-specific severity enum.
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageDiagnostic.cs`: change `Severity` type to `DescriptorPackageDiagnosticSeverity`.
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageDiagnosticCodes.cs`: keep diagnostic codes, remove severity wrapper helpers.
- `src/Metadata/CrestCreates.Metadata/DescriptorPackage/DefaultDescriptorPackageBuilder.cs`: emit package-domain severities.
- `src/Metadata/CrestCreates.Metadata/DescriptorPackage/CanonicalHashing/*`: update package diagnostic canonical hash projection if it currently serializes `SeverityLevel`.
- `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackageBuilderTests.cs`: package diagnostic enum behavior tests.
- `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/*`: package hash tests updated for enum type.
- `src/Metadata/CrestCreates.Metadata.Abstractions/*.csproj`: add `CrestCreates.Snapshot.Abstractions` reference.
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorManifest.cs`: implement package-owned snapshot copying for manifest entries.
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorSnapshot.cs`: implement package-owned snapshot copying for snapshot entries and relationship entries.
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackage.cs`: implement `ISnapshotable<DescriptorPackage>`.
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageEvidence.cs`: implement `ISnapshotable<DescriptorPackageEvidence>`.
- `src/Metadata/CrestCreates.Metadata.Abstractions/Evidence/EvidenceFinding.cs`: implement `ISnapshotable<EvidenceFinding>` only for nested package evidence snapshot semantics.
- `src/Metadata/CrestCreates.Metadata.Abstractions/Evidence/EvidenceFindingCount.cs`: implement `ISnapshotable<EvidenceFindingCount>`.
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/*.csproj`: add `CrestCreates.Snapshot.Abstractions` reference.
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraft.cs`: replace `CreateClone()` with `Snapshot()`.
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftPayload.cs`: replace abstract `CreateClone()` with `Snapshot()`.
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/*DescriptorDraftPayload.cs`: implement `Snapshot()`.
- `src/Metadata/Draft/CrestCreates.DescriptorDraft/InMemoryDescriptorDraftStore.cs`: call `Snapshot()`.
- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorDraftSet.cs`: implement `Snapshot()`.
- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorAuthoringResult.cs`: implement `Snapshot()`.
- `src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/*.csproj`: add `CrestCreates.Snapshot.Abstractions` reference.
- `src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs`: replace `Clone()` with `Snapshot()`.
- `src/Runtime/Workflow/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs`: call `Snapshot()`.
- `src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/*.csproj`: add `CrestCreates.Snapshot.Abstractions` reference.
- `src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs`: replace `Clone()` with `Snapshot()`.
- `src/Runtime/HumanTask/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs`: call `Snapshot()`.
- `src/Framework/Modules/CrestCreates.Organization.Abstractions/*.csproj`: add `CrestCreates.Snapshot.Abstractions` reference.
- `src/Framework/Modules/CrestCreates.Organization.Abstractions/OrganizationUnit.cs`: replace `Clone()` with `Snapshot()`.
- `src/Framework/Modules/CrestCreates.Organization.Abstractions/Position.cs`: replace `Clone()` with `Snapshot()`.
- `src/Framework/Modules/CrestCreates.Organization.Abstractions/UserOrganizationMembership.cs`: replace `Clone()` with `Snapshot()`.
- `src/Framework/Modules/CrestCreates.Organization.Abstractions/UserOrganizationRoleAssignment.cs`: replace `Clone()` with `Snapshot()`.
- `src/Framework/Modules/CrestCreates.Organization/InMemoryOrganizationStore.cs`: call `Snapshot()`.
- `src/Framework/Modules/CrestCreates.Organization/DefaultOrganizationHierarchyService.cs`: call `Snapshot()`.
- `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/*.csproj`: add `CrestCreates.Snapshot.Abstractions` reference.
- `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryContracts.cs`: implement `ISnapshotable<T>` on qualifying boundary models.
- `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentMemoryStore.cs`: use model-owned `Snapshot()` instead of local copy expressions where present.
- `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/BoundaryTests.cs`: Agent Memory snapshot tests.
- `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs`: update allowed references only if the boundary test requires explicit project allow-list changes.

---

### Task 1: #46 Package Diagnostic Severity Enum

**Files:**
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageDiagnosticSeverity.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageDiagnostic.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageDiagnosticCodes.cs`
- Modify: `src/Metadata/CrestCreates.Metadata/DescriptorPackage/DefaultDescriptorPackageBuilder.cs`
- Modify: package diagnostic canonical hash tests and writer/computer only where compile errors require it.
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackageBuilderTests.cs`

**Interfaces:**
- Produces: `public enum DescriptorPackageDiagnosticSeverity { Info = 0, Warning = 1, Error = 2 }`
- Produces: `DescriptorPackageDiagnostic.Severity: DescriptorPackageDiagnosticSeverity`
- Consumes: existing `DescriptorPackageDiagnostic.Code`, `Message`, and `Subject` fields.

- [ ] **Step 1: Write the failing package severity contract test**

Add this test to `DescriptorPackageBuilderTests`:

```csharp
[Fact]
public void PackageDiagnostics_UsePackageSeverityEnum()
{
    var diagnostic = new DescriptorPackageDiagnostic
    {
        Code = DescriptorPackageDiagnosticCodes.TopologyNotProvided,
        Severity = DescriptorPackageDiagnosticSeverity.Info,
        Message = "Topology was not provided."
    };

    diagnostic.Severity.Should().Be(DescriptorPackageDiagnosticSeverity.Info);
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageBuilderTests.PackageDiagnostics_UsePackageSeverityEnum"`

Expected: compile failure because `DescriptorPackageDiagnosticSeverity` does not exist.

- [ ] **Step 3: Add the enum and update package diagnostic contract**

Create `DescriptorPackageDiagnosticSeverity.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public enum DescriptorPackageDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}
```

Change `DescriptorPackageDiagnostic`:

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public sealed record DescriptorPackageDiagnostic
{
    public required DiagnosticCode Code { get; init; }
    public required DescriptorPackageDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
}
```

- [ ] **Step 4: Replace package severity helpers**

In `DescriptorPackageDiagnosticCodes`, remove `SeverityError`, `SeverityWarning`, and `SeverityInfo` helpers if they only wrap `SeverityLevel`.

In `DefaultDescriptorPackageBuilder`, replace helper usages:

```csharp
Severity = DescriptorPackageDiagnosticSeverity.Error
Severity = DescriptorPackageDiagnosticSeverity.Warning
Severity = DescriptorPackageDiagnosticSeverity.Info
```

Do not change `EvidenceFinding`, topology, impact, compatibility, lifecycle, Agent Memory, or review severity types.

- [ ] **Step 5: Update package canonical hash code only for the type change**

If canonical hash writers read `diagnostic.Severity`, keep the serialized string stable by writing `diagnostic.Severity.ToString()`.

Do not add compatibility parsing for old string values.

- [ ] **Step 6: Run focused package tests**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackage"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage src/Metadata/CrestCreates.Metadata/DescriptorPackage tests/Metadata/Core/CrestCreates.Metadata.Tests
git commit -m "refactor: type package diagnostic severity"
```

---

### Task 2: #35 Metadata, Draft, Package, and Sample Authoring Snapshots

**Files:**
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorManifest.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorSnapshot.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackage.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageEvidence.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/Evidence/EvidenceFinding.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/Evidence/EvidenceFindingCount.cs`
- Modify: `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CrestCreates.DescriptorDraft.Abstractions.csproj`
- Modify: `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraft.cs`
- Modify: `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftPayload.cs`
- Modify: `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/*DescriptorDraftPayload.cs`
- Modify: `src/Metadata/Draft/CrestCreates.DescriptorDraft/InMemoryDescriptorDraftStore.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorDraftSet.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorAuthoringResult.cs`
- Test: `tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/InMemoryDescriptorDraftStoreTests.cs`
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackageBuilderTests.cs`
- Test: `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`

**Interfaces:**
- Consumes: `ISnapshotable<T>.Snapshot()` from `CrestCreates.Snapshot.Abstractions`.
- Produces: `DescriptorDraft.Snapshot()`, `DescriptorDraftPayload.Snapshot()`, payload-specific `Snapshot()`, `DescriptorPackage.Snapshot()`, `DescriptorPackageEvidence.Snapshot()`, sample `DescriptorDraftSet.Snapshot()`, sample `DescriptorAuthoringResult.Snapshot()`.

- [ ] **Step 1: Add failing draft snapshot tests**

In `InMemoryDescriptorDraftStoreTests`, add a test that mutates the source metadata after save and verifies the read model is isolated:

```csharp
[Fact]
public async Task SaveAsync_StoresSnapshot_NotOriginalDraftReference()
{
    var store = new InMemoryDescriptorDraftStore();
    var metadata = new Dictionary<string, string> { ["before"] = "value" };
    var draft = CreateDraft() with { Metadata = metadata };

    await store.SaveAsync(draft);
    metadata["after"] = "mutated";

    var stored = await store.GetAsync(draft.TenantId, draft.DraftId);

    stored.Should().NotBeSameAs(draft);
    stored!.Metadata.Should().ContainKey("before");
    stored.Metadata.Should().NotContainKey("after");
}
```

- [ ] **Step 2: Add failing package evidence snapshot test**

In `DescriptorPackageBuilderTests`, add a direct contract test:

```csharp
[Fact]
public void DescriptorPackageEvidence_Snapshot_CopiesNestedCollections()
{
    var evidence = new DescriptorPackageEvidence
    {
        TopologyDiagnosticCounts =
        [
            new EvidenceFindingCount { Severity = SeverityLevel.Warning, Code = new DiagnosticCode("T001"), Count = 1 }
        ],
        NormalizedFindings =
        [
            new EvidenceFinding
            {
                Source = "test",
                Code = new DiagnosticCode("F001"),
                Severity = SeverityLevel.Warning,
                Message = "finding",
                RelatedRefs = [new DescriptorRef("workflow", "wf", 1)]
            }
        ]
    };

    var snapshot = evidence.Snapshot();

    snapshot.Should().NotBeSameAs(evidence);
    snapshot.TopologyDiagnosticCounts.Should().NotBeSameAs(evidence.TopologyDiagnosticCounts);
    snapshot.NormalizedFindings.Should().NotBeSameAs(evidence.NormalizedFindings);
    snapshot.NormalizedFindings[0].RelatedRefs.Should().NotBeSameAs(evidence.NormalizedFindings[0].RelatedRefs);
}
```

- [ ] **Step 3: Run focused tests and verify they fail**

Run:

```bash
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests --filter "FullyQualifiedName~InMemoryDescriptorDraftStoreTests.SaveAsync_StoresSnapshot_NotOriginalDraftReference"
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageBuilderTests.DescriptorPackageEvidence_Snapshot_CopiesNestedCollections"
```

Expected: compile failure because snapshot methods or project references are missing.

- [ ] **Step 4: Add snapshot project references**

Add `ProjectReference` entries:

```xml
<ProjectReference Include="..\CrestCreates.Snapshot.Abstractions\CrestCreates.Snapshot.Abstractions.csproj" />
```

for `CrestCreates.Metadata.Abstractions.csproj`.

Add:

```xml
<ProjectReference Include="..\..\CrestCreates.Snapshot.Abstractions\CrestCreates.Snapshot.Abstractions.csproj" />
```

for `CrestCreates.DescriptorDraft.Abstractions.csproj`.

- [ ] **Step 5: Implement metadata and package snapshots**

Use `using CrestCreates.Snapshot.Abstractions;`.

Implement `EvidenceFinding.Snapshot()` by copying `RelatedRefs`:

```csharp
public sealed record EvidenceFinding : ISnapshotable<EvidenceFinding>
{
    public required string Source { get; init; }
    public required DiagnosticCode Code { get; init; }
    public required SeverityLevel Severity { get; init; }
    public DescriptorRef? Subject { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<DescriptorRef> RelatedRefs { get; init; } = Array.Empty<DescriptorRef>();

    public EvidenceFinding Snapshot() => this with
    {
        RelatedRefs = RelatedRefs.ToArray()
    };
}
```

Implement `EvidenceFindingCount.Snapshot()` as `this with { }`.

Implement `DescriptorPackageEvidence.Snapshot()` by copying all list properties with nested snapshots:

```csharp
public DescriptorPackageEvidence Snapshot() => new()
{
    TopologyNodeCount = TopologyNodeCount,
    TopologyEdgeCount = TopologyEdgeCount,
    TopologyDiagnosticCounts = TopologyDiagnosticCounts.Select(item => item.Snapshot()).ToArray(),
    HasTopologyErrors = HasTopologyErrors,
    MaxImpactSeverity = MaxImpactSeverity,
    AffectedDescriptorCount = AffectedDescriptorCount,
    ImpactPathCount = ImpactPathCount,
    ImpactDiagnosticCounts = ImpactDiagnosticCounts.Select(item => item.Snapshot()).ToArray(),
    MaxCompatibilityLevel = MaxCompatibilityLevel,
    BreakingFindingCount = BreakingFindingCount,
    SecuritySensitiveFindingCount = SecuritySensitiveFindingCount,
    UnsupportedFindingCount = UnsupportedFindingCount,
    MaxLifecycleDecision = MaxLifecycleDecision,
    RequiresReview = RequiresReview,
    IsBlocked = IsBlocked,
    PackageFindingCount = PackageFindingCount,
    NormalizedFindings = NormalizedFindings.Select(item => item.Snapshot()).ToArray()
};
```

Implement `DescriptorManifest.Snapshot()` by copying `DescriptorEntries` with `ToArray()`.

Implement `DescriptorSnapshot.Snapshot()` by copying `Descriptors` and `Relationships` with `ToArray()`.

Implement `DescriptorPackage.Snapshot()` against the current model shape:

```csharp
public DescriptorPackage Snapshot() => new()
{
    Manifest = Manifest.Snapshot(),
    Snapshot = Snapshot.Snapshot(),
    Evidence = Evidence.Snapshot(),
    Diagnostics = Diagnostics.ToArray(),
    Hashes = Hashes,
    EvidenceEnvelope = EvidenceEnvelope
};
```

`DescriptorPackageHashSet` and `DescriptorPackageEvidenceEnvelope` currently have no mutable collection properties, so they can be reused by reference unless implementation adds collection state before this task runs.

- [ ] **Step 6: Replace draft `CreateClone()` with `Snapshot()`**

Change `DescriptorDraftPayload`:

```csharp
public abstract DescriptorDraftPayload Snapshot();
```

Change concrete payloads from `CreateClone()` to `Snapshot()` and keep their current defensive-copy logic.

Change `DescriptorDraft`:

```csharp
public DescriptorDraft Snapshot() => this with
{
    Payload = Payload.Snapshot(),
    Metadata = Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
};
```

Update `InMemoryDescriptorDraftStore` call sites from `CreateClone()` to `Snapshot()`.

Remove all production `CreateClone()` members from descriptor draft types.

- [ ] **Step 7: Migrate sample authoring draft set snapshots**

In sample authoring records, implement `ISnapshotable<T>` where the models own draft or diagnostic collections.

For `DescriptorDraftSet`, snapshot draft collections by calling `draft.Snapshot()`.

For `DescriptorAuthoringResult`, snapshot nested draft set/result collections and copy diagnostics arrays.

- [ ] **Step 8: Fix test helper payloads**

Search tests for `CreateClone()`:

```bash
rg -n "CreateClone\\(" tests src samples -g '*.cs'
```

For test payload classes, replace override with:

```csharp
public override DescriptorDraftPayload Snapshot() => this;
```

Do not reintroduce `CreateClone()`.

- [ ] **Step 9: Run focused metadata, draft, and sample tests**

Run:

```bash
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests
```

Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/Metadata tests/Metadata samples tests/Framework/Testing
git commit -m "refactor: adopt snapshots for metadata boundary models"
```

---

### Task 3: #35 Workflow, HumanTask, and Organization Runtime Boundary Snapshots

**Files:**
- Modify: `src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
- Modify: `src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs`
- Modify: `src/Runtime/Workflow/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs`
- Modify: `src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj`
- Modify: `src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs`
- Modify: `src/Runtime/HumanTask/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs`
- Modify: `src/Framework/Modules/CrestCreates.Organization.Abstractions/CrestCreates.Organization.Abstractions.csproj`
- Modify: `src/Framework/Modules/CrestCreates.Organization.Abstractions/OrganizationUnit.cs`
- Modify: `src/Framework/Modules/CrestCreates.Organization.Abstractions/Position.cs`
- Modify: `src/Framework/Modules/CrestCreates.Organization.Abstractions/UserOrganizationMembership.cs`
- Modify: `src/Framework/Modules/CrestCreates.Organization.Abstractions/UserOrganizationRoleAssignment.cs`
- Modify: `src/Framework/Modules/CrestCreates.Organization/InMemoryOrganizationStore.cs`
- Modify: `src/Framework/Modules/CrestCreates.Organization/DefaultOrganizationHierarchyService.cs`
- Test: `tests/Runtime/Workflow/CrestCreates.Workflow.Tests/InMemoryWorkflowInstanceStoreTests.cs`
- Test: `tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests/InMemoryHumanTaskInstanceStoreTests.cs`
- Test: `tests/Framework/Modules/CrestCreates.Organization.Tests/InMemoryOrganizationStoreTests.cs`
- Test: `tests/Framework/Modules/CrestCreates.Organization.Tests/OrganizationHierarchyServiceTests.cs`

**Interfaces:**
- Produces: `WorkflowInstance.Snapshot()`, `HumanTaskInstance.Snapshot()`, organization model `Snapshot()` methods.
- Removes: production `Clone()` methods on these models.

- [ ] **Step 1: Rename clone tests to snapshot tests**

In each existing store test that currently calls `Clone()`, change the test to call `Snapshot()` and keep the existing assertions.

For example:

```csharp
var snapshot = original.Snapshot();

snapshot.Should().NotBeSameAs(original);
```

- [ ] **Step 2: Add focused store isolation tests**

For workflow and HumanTask stores, add a test that saves an instance, mutates a collection on the source instance, and verifies the stored instance does not observe the new collection entry.

For organization store, add the same pattern for each model with collection properties.

- [ ] **Step 3: Run focused tests and verify they fail**

Run:

```bash
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests --filter "FullyQualifiedName~InMemoryWorkflowInstanceStoreTests"
dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests --filter "FullyQualifiedName~InMemoryHumanTaskInstanceStoreTests"
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests --filter "FullyQualifiedName~InMemoryOrganizationStoreTests|FullyQualifiedName~OrganizationHierarchyServiceTests"
```

Expected: compile failure because `Snapshot()` is not yet implemented and `Clone()` still exists.

- [ ] **Step 4: Add snapshot references**

Add `CrestCreates.Snapshot.Abstractions` project references to the Workflow, HumanTask, and Organization abstraction projects.

Use these exact references:

```xml
<ProjectReference Include="../../../Metadata/CrestCreates.Snapshot.Abstractions/CrestCreates.Snapshot.Abstractions.csproj" />
```

for `CrestCreates.Workflow.Abstractions.csproj`, `CrestCreates.HumanTask.Abstractions.csproj`, and `CrestCreates.Organization.Abstractions.csproj`.

- [ ] **Step 5: Implement runtime snapshots**

For `WorkflowInstance`, preserve current `Clone()` semantics exactly:

```csharp
public WorkflowInstance Snapshot() => new()
{
    InstanceId = InstanceId,
    Workflow = Workflow,
    Status = Status,
    CurrentStepId = CurrentStepId,
    StepIndex = StepIndex,
    WaitingHumanTaskId = WaitingHumanTaskId,
    StartedAt = StartedAt,
    CompletedAt = CompletedAt,
    Variables = new Dictionary<string, object?>(Variables),
    StepVariables = new Dictionary<string, object?>(StepVariables),
    StepResults = StepResults.ToList(),
    ErrorMessage = ErrorMessage,
    ConcurrencyStamp = ConcurrencyStamp,
    UpdatedAt = UpdatedAt,
};
```

Keep unknown object payload references as-is.

For `HumanTaskInstance`, preserve current `Clone()` semantics: copy candidate lists, timestamps, status, and scalar values; keep input/output object payload references as-is.

- [ ] **Step 6: Implement organization snapshots**

Replace each organization model `Clone()` with `Snapshot()` and keep the current field-by-field copy behavior.

Update stores and hierarchy service to call `Snapshot()`.

- [ ] **Step 7: Remove production `Clone()` methods and references**

Run:

```bash
rg -n "\\.Clone\\(|public .* Clone\\(" src/Runtime/Workflow src/Runtime/HumanTask src/Framework/Modules/CrestCreates.Organization tests/Runtime/Workflow tests/Runtime/HumanTask tests/Framework/Modules/CrestCreates.Organization -g '*.cs'
```

Expected: no workflow, HumanTask, or organization boundary clone references remain.

- [ ] **Step 8: Run focused runtime tests**

Run:

```bash
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Runtime/Workflow src/Runtime/HumanTask src/Framework/Modules/CrestCreates.Organization tests/Runtime/Workflow tests/Runtime/HumanTask tests/Framework/Modules/CrestCreates.Organization
git commit -m "refactor: adopt snapshots for runtime boundary models"
```

---

### Task 4: #35 Agent Memory Boundary Snapshot Migration

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryContracts.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentMemoryStore.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/BoundaryTests.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/ContractTests.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/MainChainTests.cs`

**Interfaces:**
- Produces leaf snapshots: `AgentContextSourceRef`, `AgentContextEvidenceRef`, `AgentMemoryDiagnostic`, `AgentMemoryInvocationContext`, `SanitizedAgentContent`.
- Produces aggregate snapshots: `AgentConversationTurn`, `AgentConversationRecord`, `AgentTaskEvent`, `AgentTaskRecord`, `AgentCompressedContextBlock`, `AgentCompressedContext`, `AgentMemoryCandidate`, `AgentMemoryItem`.
- Produces composition snapshots when justified: `AgentMemoryPack`, `AgentMemoryOperationRequest`, `AgentAuthoringRequest`, `AgentAuthoringContext`, `AgentSourceExpansionResult`.
- Does not automatically migrate `AgentMemoryQuery`.

- [ ] **Step 1: Add failing leaf snapshot test**

In `BoundaryTests`, add:

```csharp
[Fact]
public void AgentContextEvidenceRef_Snapshot_CopiesNestedSourceRefs()
{
    var sourceRef = new AgentContextSourceRef
    {
        SourceKind = AgentSourceKind.MemoryItem,
        TenantId = "tenant-1",
        SourceId = "memory-1",
        DescriptorRefs = [new DescriptorRef("workflow", "wf", 1)]
    };

    var evidenceRef = new AgentContextEvidenceRef
    {
        EvidenceId = "evidence-1",
        EvidenceKind = "memory",
        TenantId = "tenant-1",
        SourceRefs = [sourceRef]
    };

    var snapshot = evidenceRef.Snapshot();

    snapshot.Should().NotBeSameAs(evidenceRef);
    snapshot.SourceRefs.Should().NotBeSameAs(evidenceRef.SourceRefs);
    snapshot.SourceRefs[0].Should().NotBeSameAs(sourceRef);
    snapshot.SourceRefs[0].DescriptorRefs.Should().NotBeSameAs(sourceRef.DescriptorRefs);
}
```

- [ ] **Step 2: Add failing pack snapshot test**

In `BoundaryTests`, add:

```csharp
[Fact]
public void AgentMemoryPack_Snapshot_PreservesNonAuthoritativeFlagAndCopiesMemories()
{
    var memory = new AgentMemoryItem
    {
        MemoryId = "memory-1",
        TenantId = "tenant-1",
        Kind = AgentMemoryKind.ProjectFact,
        Content = "Metadata context is authoritative.",
        CanonicalContentHash = new CanonicalHash
        {
            Value = "abc",
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "AgentMemoryItem",
            Scope = "InternalFull",
            Purpose = "Definition",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "agent-memory-item-v1"
        },
        PromotedAt = DateTimeOffset.UnixEpoch,
        IsAuthoritative = false,
        Tags = ["metadata"]
    };

    var pack = new AgentMemoryPack
    {
        TenantId = "tenant-1",
        Memories = [memory],
        IsAuthoritative = false
    };

    var snapshot = pack.Snapshot();

    snapshot.Should().NotBeSameAs(pack);
    snapshot.IsAuthoritative.Should().BeFalse();
    snapshot.Memories.Should().NotBeSameAs(pack.Memories);
    snapshot.Memories[0].Should().NotBeSameAs(memory);
    snapshot.Memories[0].Tags.Should().NotBeSameAs(memory.Tags);
}
```

- [ ] **Step 3: Run focused tests and verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~BoundaryTests.AgentContextEvidenceRef_Snapshot_CopiesNestedSourceRefs|FullyQualifiedName~BoundaryTests.AgentMemoryPack_Snapshot_PreservesNonAuthoritativeFlagAndCopiesMemories"
```

Expected: compile failure because snapshot methods or references do not exist.

- [ ] **Step 4: Add snapshot reference**

Add this project reference to `CrestCreates.Agent.Memory.Abstractions.csproj`:

```xml
<ProjectReference Include="../../../Metadata/CrestCreates.Snapshot.Abstractions/CrestCreates.Snapshot.Abstractions.csproj" />
```

- [ ] **Step 5: Implement leaf snapshots first**

In `AgentMemoryContracts.cs`, add `using CrestCreates.Snapshot.Abstractions;`.

Implement `Snapshot()` on leaf models by replacing every list/dictionary property:

```csharp
public AgentContextSourceRef Snapshot() => this with
{
    DescriptorRefs = DescriptorRefs.ToArray()
};

public AgentContextEvidenceRef Snapshot() => this with
{
    SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray()
};

public AgentMemoryDiagnostic Snapshot() => this with
{
    SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray()
};

public AgentMemoryInvocationContext Snapshot() => this with
{
    TraceAttributes = TraceAttributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
};

public SanitizedAgentContent Snapshot() => this with
{
    RedactionKinds = RedactionKinds.ToArray(),
    Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
};
```

- [ ] **Step 6: Implement aggregate snapshots**

Implement aggregate models by recursively calling leaf snapshots and copying scalar collection properties with `ToArray()`.

Examples:

```csharp
public AgentMemoryItem Snapshot() => this with
{
    Tags = Tags.ToArray(),
    DescriptorRefs = DescriptorRefs.ToArray(),
    SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray(),
    RedactionKinds = RedactionKinds.ToArray(),
    SanitizationDiagnostics = SanitizationDiagnostics.Select(item => item.Snapshot()).ToArray()
};

public AgentConversationRecord Snapshot() => this with
{
    Turns = Turns.Select(item => item.Snapshot()).ToArray(),
    Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
};
```

- [ ] **Step 7: Implement justified composition snapshots**

Implement:

```csharp
public AgentMemoryPack Snapshot() => this with
{
    Memories = Memories.Select(item => item.Snapshot()).ToArray(),
    Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
};

public AgentMemoryOperationRequest Snapshot() => this with
{
    InvocationContext = InvocationContext.Snapshot(),
    SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray()
};

public AgentAuthoringRequest Snapshot() => this with
{
    MemoryQuery = MemoryQuery
};

public AgentAuthoringContext Snapshot() => this with
{
    Request = Request.Snapshot(),
    MemoryPack = MemoryPack.Snapshot(),
    Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
};

public AgentSourceExpansionResult Snapshot() => this with
{
    SourceRef = SourceRef.Snapshot(),
    Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
};
```

Do not add `ISnapshotable<AgentMemoryQuery>` unless implementation discovers an existing defensive-copy boundary for it.

- [ ] **Step 8: Use snapshots in the in-memory store**

In `InMemoryAgentMemoryStore`, replace local defensive copy expressions for models that now own `Snapshot()` with calls to `.Snapshot()`.

Keep filtering, ordering, promotion guards, sanitization assumptions, and canonical hash behavior unchanged.

- [ ] **Step 9: Run Agent Memory tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
```

Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions src/Runtime/Agent/CrestCreates.Agent.Memory tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
git commit -m "refactor: adopt snapshots for agent memory boundaries"
```

---

### Task 5: Full Regression and Boundary Cleanup

**Files:**
- Modify: any compile-fix call sites still referencing `Clone()` or `CreateClone()`.
- Modify: dependency boundary tests only if they need an explicit allowed reference update for `CrestCreates.Snapshot.Abstractions`.
- Modify: `memory.md` if the implementation changes the recorded platform state.

**Interfaces:**
- Consumes: all prior task commits.
- Produces: clean repository with no production `Clone()` / `CreateClone()` boundary copy leftovers for the migrated models.

- [ ] **Step 1: Search for removed copy verbs**

Run:

```bash
rg -n "CreateClone\\(|\\.Clone\\(|public .* Clone\\(" src tests samples -g '*.cs'
```

Expected: no migrated model production references remain. Remaining hits must be unrelated domains such as test utility snapshots or framework APIs outside this migration.

- [ ] **Step 2: Search for package diagnostic severity leakage**

Run:

```bash
rg -n "DescriptorPackageDiagnostic.*SeverityLevel|Severity = SeverityLevel|SeverityError|SeverityWarning|SeverityInfo" src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage src/Metadata/CrestCreates.Metadata/DescriptorPackage tests/Metadata/Core/CrestCreates.Metadata.Tests -g '*.cs'
```

Expected: package diagnostics use `DescriptorPackageDiagnosticSeverity`. Generic evidence tests may still use `SeverityLevel`.

- [ ] **Step 3: Run required regression projects**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests
dotnet test tests/Metadata/Core/CrestCreates.Snapshot.Tests
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
dotnet test tests/Framework/Modules/CrestCreates.Organization.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

Expected: all PASS.

- [ ] **Step 4: Run build**

Run:

```bash
dotnet build
```

Expected: PASS.

- [ ] **Step 5: Update platform memory if needed**

If the implementation changes the platform state recorded in `memory.md`, add a concise entry:

```markdown
- 2026-06-30: Boundary defensive-copy contracts migrated to `ISnapshotable<T>.Snapshot()` for package, draft, runtime, organization, and Agent Memory boundary models. `DescriptorPackageDiagnostic.Severity` now uses `DescriptorPackageDiagnosticSeverity`.
```

Skip this edit if `memory.md` does not track this level of technical debt closure.

- [ ] **Step 6: Commit cleanup**

```bash
git add src tests samples memory.md
git commit -m "test: verify boundary snapshot migration"
```

---

## Self-Review

- Spec coverage: #46 is Task 1; metadata/draft/package/sample snapshots are Task 2; runtime and organization snapshots are Task 3; Agent Memory layered migration is Task 4; regression and dependency boundaries are Task 5.
- Scope guard: no task changes Control Plane behavior, runtime hot reload, real registry mutation, approval flow, Agent Memory scoring, compression, sanitization, promotion, source expansion, or canonical hash semantics beyond compile-time type updates.
- Type consistency: `DescriptorPackageDiagnosticSeverity`, `ISnapshotable<T>.Snapshot()`, `DescriptorDraftPayload.Snapshot()`, `DescriptorDraft.Snapshot()`, `WorkflowInstance.Snapshot()`, `HumanTaskInstance.Snapshot()`, and Agent Memory contract names match current code.
- Request/query guard: `AgentMemoryQuery` is explicitly not migrated by default.
- Generic evidence guard: `EvidenceFinding` and `EvidenceFindingCount` retain `SeverityLevel`.
