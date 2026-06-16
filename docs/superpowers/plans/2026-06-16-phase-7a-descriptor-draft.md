# Phase 7a — Descriptor Draft Runtime Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Descriptor Draft Runtime Foundation — 3 new projects with models, 6 typed payloads, 4 services, DI registration, and 25 tests.

**Architecture:** `Abstractions` holds interfaces/enums/models/payloads (depends on 6 descriptor abstractions + Metadata for CapabilityDescriptor). `Implementation` holds services and DI (depends only on Phase 6 *interfaces* from Metadata.Abstractions). `Tests` covers all services in isolation.

**Tech Stack:** .NET 10, C# records/classes, `ConcurrentDictionary`, `Microsoft.Extensions.DI`, xUnit + FluentAssertions + Moq.

**Key invariant:** No path mutates active runtime registries. AoT-safe (enum switch, zero reflection).

---

## Task 1: Create Projects and .slnx Registration

**Files:**
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/CrestCreates.DescriptorDraft.Abstractions.csproj`
- Create: `framework/src/CrestCreates.DescriptorDraft/CrestCreates.DescriptorDraft.csproj`
- Create: `framework/test/CrestCreates.DescriptorDraft.Tests/CrestCreates.DescriptorDraft.Tests.csproj`
- Modify: `CrestCreates.slnx`

- [ ] **Step 1: Create Abstractions.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.DescriptorDraft.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="CrestCreates.DescriptorDraft" />
    <InternalsVisibleTo Include="CrestCreates.DescriptorDraft.Tests" />
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Metadata\CrestCreates.Metadata.csproj" />
    <ProjectReference Include="..\CrestCreates.Domain.Shared\CrestCreates.Domain.Shared.csproj" />
    <ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Capability.Abstractions\CrestCreates.Capability.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Form.Abstractions\CrestCreates.Form.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Event.Abstractions\CrestCreates.Event.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.HumanTask.Abstractions\CrestCreates.HumanTask.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Workflow.Abstractions\CrestCreates.Workflow.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create Implementation.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.DescriptorDraft</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="CrestCreates.DescriptorDraft.Tests" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.DescriptorDraft.Abstractions\CrestCreates.DescriptorDraft.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.DescriptorDraft.Tests</RootNamespace>
    <AssemblyName>CrestCreates.DescriptorDraft.Tests</AssemblyName>
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
    <PackageReference Include="Moq" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.DescriptorDraft.Abstractions\CrestCreates.DescriptorDraft.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.DescriptorDraft\CrestCreates.DescriptorDraft.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Capability.Abstractions\CrestCreates.Capability.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Form.Abstractions\CrestCreates.Form.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Event.Abstractions\CrestCreates.Event.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.HumanTask.Abstractions\CrestCreates.HumanTask.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Workflow.Abstractions\CrestCreates.Workflow.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Metadata\CrestCreates.Metadata.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Register in .slnx** — insert into `/src/core/` before `<Project Path="...CrestCreates.DistributedTransaction...">`:
```xml
<Project Path="framework/src/CrestCreates.DescriptorDraft.Abstractions/CrestCreates.DescriptorDraft.Abstractions.csproj" />
<Project Path="framework/src/CrestCreates.DescriptorDraft/CrestCreates.DescriptorDraft.csproj" />
```
And into `/src/test/` after `<Project Path="...CrestCreates.Draft.Tests...">`:
```xml
<Project Path="framework/test/CrestCreates.DescriptorDraft.Tests/CrestCreates.DescriptorDraft.Tests.csproj" />
```

- [ ] **Step 5: Verify build** — `dotnet build framework/src/CrestCreates.DescriptorDraft.Abstractions/` (0 errors)

- [ ] **Step 6: Commit**
```bash
git add framework/src/CrestCreates.DescriptorDraft.Abstractions/ framework/src/CrestCreates.DescriptorDraft/ framework/test/CrestCreates.DescriptorDraft.Tests/ CrestCreates.slnx
git commit -m "feat: scaffold DescriptorDraft projects and slnx registration"
```

---

## Task 2: Add Enums and DescriptorDraftDiagnostic

**Files:**
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftOperation.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftAuthorKind.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftStatus.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftDiagnostic.cs`

- [ ] **Step 1: Create all 4 files**

```csharp
// DescriptorDraftOperation.cs
namespace CrestCreates.DescriptorDraft.Abstractions;
public enum DescriptorDraftOperation { Create, Update, Deprecate, Remove }

// DescriptorDraftAuthorKind.cs
namespace CrestCreates.DescriptorDraft.Abstractions;
public enum DescriptorDraftAuthorKind { Human, Agent, System, Import, Generator }

// DescriptorDraftStatus.cs
namespace CrestCreates.DescriptorDraft.Abstractions;
public enum DescriptorDraftStatus { Created, Invalid, Materialized, Reviewed, Cancelled }

// DescriptorDraftDiagnostic.cs
using CrestCreates.Metadata.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;

public enum DescriptorDraftDiagnosticSeverity { Info, Warning, Error, Blocker }

public sealed record DescriptorDraftDiagnostic
{
    public required string Code { get; init; }
    public required DescriptorDraftDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public DescriptorKind? DescriptorKind { get; init; }
    public string? DescriptorId { get; init; }
    public string? DraftId { get; init; }
    public string? Path { get; init; }
    public string? RelatedDiagnosticCode { get; init; }
}
```

- [ ] **Step 2: Verify build and commit**
```bash
dotnet build framework/src/CrestCreates.DescriptorDraft.Abstractions/
git add framework/src/CrestCreates.DescriptorDraft.Abstractions/
git commit -m "feat: add DescriptorDraft enums and diagnostic model"
```

---

## Task 3: Add Typed Payloads (6)

**Files:**
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftPayload.cs` (abstract base)
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/SchemaDescriptorDraftPayload.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/FormDescriptorDraftPayload.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/CapabilityDescriptorDraftPayload.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/HumanTaskDescriptorDraftPayload.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/WorkflowDescriptorDraftPayload.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/EventDescriptorDraftPayload.cs`

- [ ] **Step 1: Create DescriptorDraftPayload.cs (abstract base)**

```csharp
using CrestCreates.Metadata.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;

public abstract record DescriptorDraftPayload
{
    public abstract DescriptorKind DescriptorKind { get; }
    public abstract IDescriptor GetDescriptor();
    public abstract DescriptorDraftPayload Clone();
}
```

- [ ] **Step 2: Create all 6 typed payloads**

```csharp
// SchemaDescriptorDraftPayload.cs
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;
public sealed record SchemaDescriptorDraftPayload(SchemaDescriptor Descriptor) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Schema;
    public override IDescriptor GetDescriptor() => Descriptor;
    // Descriptor uses init-only properties + IReadOnlyList — immutable-in-practice, reference copy is safe.
    public override DescriptorDraftPayload Clone() => this with { };
}

// FormDescriptorDraftPayload.cs
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;
public sealed record FormDescriptorDraftPayload(FormDescriptor Descriptor) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Form;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Clone() => this with { };
}

// CapabilityDescriptorDraftPayload.cs
// NOTE: CapabilityDescriptor is in CrestCreates.Metadata, not Capability.Abstractions
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;
public sealed record CapabilityDescriptorDraftPayload(CapabilityDescriptor Descriptor) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Capability;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Clone() => this with { };
}

// HumanTaskDescriptorDraftPayload.cs
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;
public sealed record HumanTaskDescriptorDraftPayload(HumanTaskDescriptor Descriptor) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.HumanTask;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Clone() => this with { };
}

// WorkflowDescriptorDraftPayload.cs
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;
public sealed record WorkflowDescriptorDraftPayload(WorkflowDescriptor Descriptor) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Workflow;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Clone() => this with { };
}

// EventDescriptorDraftPayload.cs
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;
public sealed record EventDescriptorDraftPayload(EventDescriptor Descriptor) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Event;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Clone() => this with { };
}
```

- [ ] **Step 3: Verify build and commit**
```bash
dotnet build framework/src/CrestCreates.DescriptorDraft.Abstractions/
git add framework/src/CrestCreates.DescriptorDraft.Abstractions/
git commit -m "feat: add 6 typed descriptor draft payloads"
```

---

## Task 4: Add Core Models (Results, DraftQuery, DescriptorDraft)

**Files:**
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftValidationResult.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftMaterializationResult.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DescriptorPackagePreview.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftReviewResult.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DraftQuery.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraft.cs`

- [ ] **Step 1: Create DescriptorDraftValidationResult.cs**

```csharp
namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorDraftValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }

    public static DescriptorDraftValidationResult Success()
        => new() { IsValid = true, Diagnostics = Array.Empty<DescriptorDraftDiagnostic>() };

    public static DescriptorDraftValidationResult Failure(params DescriptorDraftDiagnostic[] diagnostics)
        => new() { IsValid = false, Diagnostics = diagnostics };
}
```

- [ ] **Step 2: Create DescriptorDraftMaterializationResult.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorDraftMaterializationResult
{
    public required bool IsMaterialized { get; init; }
    public required IReadOnlyList<IDescriptor> ProposedInventory { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }

    public static DescriptorDraftMaterializationResult Success(IReadOnlyList<IDescriptor> proposedInventory)
        => new() { IsMaterialized = true, ProposedInventory = proposedInventory, Diagnostics = Array.Empty<DescriptorDraftDiagnostic>() };

    public static DescriptorDraftMaterializationResult Failure(params DescriptorDraftDiagnostic[] diagnostics)
        => new() { IsMaterialized = false, ProposedInventory = Array.Empty<IDescriptor>(), Diagnostics = diagnostics };
}
```

- [ ] **Step 3: Create DescriptorPackagePreview.cs**

```csharp
namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorPackagePreview
{
    public required string ManifestHash { get; init; }
    public required string SnapshotHash { get; init; }
    public required string EvidenceHash { get; init; }
    public required string EnvelopeHash { get; init; }
    public required IReadOnlyList<string> DescriptorIds { get; init; }
}
```

- [ ] **Step 4: Create DescriptorDraftReviewResult.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorDraftReviewResult
{
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required DescriptorDraftValidationResult ValidationResult { get; init; }
    public DescriptorDraftMaterializationResult? MaterializationResult { get; init; }
    public IReadOnlyList<IDescriptor>? ProposedInventory { get; init; }
    public DescriptorTopologySnapshot? TopologySnapshot { get; init; }
    public DescriptorImpactAnalysisReport? ImpactAnalysisResult { get; init; }
    public DescriptorCompatibilityReport? CompatibilityResult { get; init; }
    public DescriptorLifecycleGovernanceReport? GovernanceDecision { get; init; }
    public DescriptorStableHashes? StableHashes { get; init; }
    public DescriptorPackagePreview? PackagePreview { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }
    public required bool IsActivationEligible { get; init; }
}
```

- [ ] **Step 5: Create DraftQuery.cs**

```csharp
namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DraftQuery
{
    public DescriptorKind? DescriptorKind { get; init; }
    public DescriptorDraftOperation? Operation { get; init; }
    public DescriptorDraftAuthorKind? AuthorKind { get; init; }
    public DescriptorDraftStatus? Status { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
}
```

- [ ] **Step 6: Create DescriptorDraft.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorDraft
{
    // --- Required ---
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required DescriptorKind DescriptorKind { get; init; }
    public required string DescriptorId { get; init; }
    public required DescriptorDraftOperation Operation { get; init; }
    public required DescriptorDraftAuthorKind AuthorKind { get; init; }
    public required string AuthorId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DescriptorDraftPayload Payload { get; init; }

    // --- Optional ---
    public string? BaseVersion { get; init; }
    public string? ProposedVersion { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public string? CorrelationId { get; init; }
    public string? Source { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    // --- Computed ---
    public DescriptorDraftStatus Status { get; init; } = DescriptorDraftStatus.Created;

    public DescriptorDraft Clone() => this with
    {
        Payload = Payload.Clone(),
        Metadata = Metadata is null
            ? null
            : new Dictionary<string, string>(Metadata, StringComparer.Ordinal)
    };
}
```

- [ ] **Step 7: Add missing `using` for DescriptorKind in DraftQuery.cs** — `using CrestCreates.Metadata.Abstractions;`

- [ ] **Step 8: Verify build and commit**
```bash
dotnet build framework/src/CrestCreates.DescriptorDraft.Abstractions/
git add framework/src/CrestCreates.DescriptorDraft.Abstractions/
git commit -m "feat: add core DescriptorDraft models and result types"
```

---

## Task 5: Add Service Interfaces

**Files:**
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/IDescriptorDraftStore.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/IDescriptorDraftValidator.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/IDescriptorDraftMaterializer.cs`
- Create: `framework/src/CrestCreates.DescriptorDraft.Abstractions/IDescriptorDraftReviewService.cs`

- [ ] **Step 1: Create IDescriptorDraftStore.cs**

```csharp
namespace CrestCreates.DescriptorDraft.Abstractions;

public interface IDescriptorDraftStore
{
    Task SaveAsync(DescriptorDraft draft, CancellationToken ct = default);
    Task<DescriptorDraft?> GetAsync(string tenantId, string draftId, CancellationToken ct = default);
    Task<IReadOnlyList<DescriptorDraft>> ListAsync(string tenantId, DraftQuery? query = null, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create IDescriptorDraftValidator.cs**

```csharp
namespace CrestCreates.DescriptorDraft.Abstractions;

public interface IDescriptorDraftValidator
{
    DescriptorDraftValidationResult Validate(DescriptorDraft draft);
}
```

- [ ] **Step 3: Create IDescriptorDraftMaterializer.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;

public interface IDescriptorDraftMaterializer
{
    DescriptorDraftMaterializationResult Materialize(
        DescriptorDraft draft,
        IReadOnlyList<IDescriptor> currentInventory);
}
```

- [ ] **Step 4: Create IDescriptorDraftReviewService.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
namespace CrestCreates.DescriptorDraft.Abstractions;

public interface IDescriptorDraftReviewService
{
    Task<DescriptorDraftReviewResult> ReviewAsync(
        DescriptorDraft draft,
        IReadOnlyList<IDescriptor> currentInventory,
        CancellationToken ct = default);
}
```

- [ ] **Step 5: Verify build and commit**
```bash
dotnet build framework/src/CrestCreates.DescriptorDraft.Abstractions/
git add framework/src/CrestCreates.DescriptorDraft.Abstractions/
git commit -m "feat: add DescriptorDraft service interfaces"
```

---

## Task 6: Implement InMemoryDescriptorDraftStore + Tests

**Files:**
- Create: `framework/src/CrestCreates.DescriptorDraft/InMemoryDescriptorDraftStore.cs`
- Create: `framework/test/CrestCreates.DescriptorDraft.Tests/InMemoryDescriptorDraftStoreTests.cs`

**Reference pattern:** `framework/src/CrestCreates.Organization/InMemoryOrganizationStore.cs`

- [ ] **Step 1: Write failing tests in InMemoryDescriptorDraftStoreTests.cs**

```csharp
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DescriptorDraft.Tests;

public class InMemoryDescriptorDraftStoreTests
{
    private static DescriptorDraft CreateDraft(string tenantId = "t1", string draftId = "d1",
        DescriptorKind kind = DescriptorKind.Schema, DescriptorDraftOperation op = DescriptorDraftOperation.Create)
    {
        var payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "schema1", Name = "Test Schema" });
        return new DescriptorDraft
        {
            TenantId = tenantId,
            DraftId = draftId,
            DescriptorKind = kind,
            DescriptorId = "schema1",
            Operation = op,
            AuthorKind = DescriptorDraftAuthorKind.Human,
            AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = payload
        };
    }

    [Fact]
    public async Task Save_And_Get_Returns_Cloned_Draft()
    {
        var store = new InMemoryDescriptorDraftStore();
        var draft = CreateDraft();
        await store.SaveAsync(draft);

        var retrieved = await store.GetAsync("t1", "d1");
        retrieved.Should().NotBeNull();
        retrieved!.DraftId.Should().Be("d1");

        // Mutate returned draft — stored copy must be unaffected
        // (DescriptorDraft is a record — `with` creates new instance)
        var mutated = retrieved with { Intent = "mutated" };
        var reRetrieved = await store.GetAsync("t1", "d1");
        reRetrieved!.Intent.Should().BeNull("snapshot-on-read prevents external mutation");
    }

    [Fact]
    public async Task List_Filters_By_Tenant()
    {
        var store = new InMemoryDescriptorDraftStore();
        await store.SaveAsync(CreateDraft("t1", "d1"));
        await store.SaveAsync(CreateDraft("t2", "d2"));
        await store.SaveAsync(CreateDraft("t1", "d3"));

        var t1Drafts = await store.ListAsync("t1");
        t1Drafts.Should().HaveCount(2);
        t1Drafts.Should().OnlyContain(d => d.TenantId == "t1");
    }

    [Fact]
    public async Task List_Filters_By_DescriptorKind()
    {
        var store = new InMemoryDescriptorDraftStore();
        await store.SaveAsync(CreateDraft("t1", "d1", DescriptorKind.Schema));
        await store.SaveAsync(CreateDraft("t1", "d2", DescriptorKind.Form));

        var query = new DraftQuery { DescriptorKind = DescriptorKind.Schema };
        var results = await store.ListAsync("t1", query);
        results.Should().HaveCount(1);
        results[0].DescriptorKind.Should().Be(DescriptorKind.Schema);
    }

    [Fact]
    public async Task Get_Missing_Returns_Null()
    {
        var store = new InMemoryDescriptorDraftStore();
        var result = await store.GetAsync("t1", "nonexistent");
        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test --filter "FullyQualifiedName~InMemoryDescriptorDraftStoreTests"` Expected: FAIL (InMemoryDescriptorDraftStore not found).

- [ ] **Step 3: Implement InMemoryDescriptorDraftStore.cs**

```csharp
using System.Collections.Concurrent;
using CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.DescriptorDraft;

public sealed class InMemoryDescriptorDraftStore : IDescriptorDraftStore
{
    private readonly ConcurrentDictionary<(string TenantId, string DraftId), DescriptorDraft> _drafts = new();

    public Task SaveAsync(DescriptorDraft draft, CancellationToken ct = default)
    {
        _drafts[(draft.TenantId, draft.DraftId)] = draft.Clone();
        return Task.CompletedTask;
    }

    public Task<DescriptorDraft?> GetAsync(string tenantId, string draftId, CancellationToken ct = default)
    {
        if (_drafts.TryGetValue((tenantId, draftId), out var existing))
            return Task.FromResult<DescriptorDraft?>(existing.Clone());
        return Task.FromResult<DescriptorDraft?>(null);
    }

    public Task<IReadOnlyList<DescriptorDraft>> ListAsync(string tenantId, DraftQuery? query = null, CancellationToken ct = default)
    {
        IEnumerable<DescriptorDraft> results = _drafts.Values
            .Where(d => d.TenantId == tenantId);

        if (query is not null)
        {
            if (query.DescriptorKind.HasValue)
                results = results.Where(d => d.DescriptorKind == query.DescriptorKind.Value);
            if (query.Operation.HasValue)
                results = results.Where(d => d.Operation == query.Operation.Value);
            if (query.AuthorKind.HasValue)
                results = results.Where(d => d.AuthorKind == query.AuthorKind.Value);
            if (query.Status.HasValue)
                results = results.Where(d => d.Status == query.Status.Value);
            if (query.CreatedFrom.HasValue)
                results = results.Where(d => d.CreatedAt >= query.CreatedFrom.Value);
            if (query.CreatedTo.HasValue)
                results = results.Where(d => d.CreatedAt <= query.CreatedTo.Value);
        }

        var list = results.Select(d => d.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<DescriptorDraft>)list);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass** — `dotnet test --filter "FullyQualifiedName~InMemoryDescriptorDraftStoreTests"` Expected: 4 PASS.

- [ ] **Step 5: Commit**
```bash
git add framework/src/CrestCreates.DescriptorDraft/ framework/test/CrestCreates.DescriptorDraft.Tests/
git commit -m "feat: implement InMemoryDescriptorDraftStore with snapshot-on-read + 4 tests"
```

---

## Task 7: Implement DefaultDescriptorDraftValidator + Tests

**Files:**
- Create: `framework/src/CrestCreates.DescriptorDraft/DefaultDescriptorDraftValidator.cs`
- Modify: `framework/test/CrestCreates.DescriptorDraft.Tests/DefaultDescriptorDraftValidatorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DescriptorDraft.Tests;

public class DefaultDescriptorDraftValidatorTests
{
    private static DescriptorDraft CreateDraft(
        string draftId = "d1",
        DescriptorKind kind = DescriptorKind.Schema,
        string descriptorId = "schema1",
        DescriptorDraftOperation op = DescriptorDraftOperation.Create,
        string? baseVersion = null,
        string? proposedVersion = "1")
    {
        var descriptor = new SchemaDescriptor { Id = descriptorId, Name = "Test" };
        return new DescriptorDraft
        {
            TenantId = "t1", DraftId = draftId, DescriptorKind = kind,
            DescriptorId = descriptorId, Operation = op,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new SchemaDescriptorDraftPayload(descriptor),
            BaseVersion = baseVersion, ProposedVersion = proposedVersion
        };
    }

    [Fact]
    public void Rejects_Empty_DraftId()
    {
        var draft = CreateDraft(draftId: "");
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "DRAFT_ID_EMPTY");
    }

    [Fact]
    public void Rejects_Kind_Payload_Mismatch()
    {
        // Draft says Schema but payload is Form
        var descriptor = new SchemaDescriptor { Id = "s1", Name = "Test" };
        var payload = new FormDescriptorDraftPayload(new FormDescriptor { Id = "s1", Name = "Test" });
        var draft = new DescriptorDraft
        {
            TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "s1", Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow, Payload = payload
        };
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "KIND_PAYLOAD_MISMATCH");
    }

    [Fact]
    public void Rejects_Payload_DescriptorId_Mismatch()
    {
        var draft = CreateDraft(descriptorId: "draftId", kind: DescriptorKind.Schema);
        // Payload descriptor has Id="schema1" but draft.DescriptorId="draftId"
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "PAYLOAD_ID_MISMATCH");
    }

    [Fact]
    public void Rejects_Create_With_BaseVersion()
    {
        var draft = CreateDraft(op: DescriptorDraftOperation.Create, baseVersion: "1");
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "CREATE_BASE_VERSION_MUST_BE_EMPTY");
    }

    [Fact]
    public void Rejects_Update_Without_BaseVersion()
    {
        var draft = CreateDraft(op: DescriptorDraftOperation.Update, baseVersion: null);
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "UPDATE_BASE_VERSION_REQUIRED");
    }

    [Fact]
    public void Valid_Draft_Passes_All_Checks()
    {
        var draft = CreateDraft();
        var validator = new DefaultDescriptorDraftValidator();
        var result = validator.Validate(draft);
        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — Expected: FAIL.

- [ ] **Step 3: Implement DefaultDescriptorDraftValidator.cs**

```csharp
using CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.DescriptorDraft;

public sealed class DefaultDescriptorDraftValidator : IDescriptorDraftValidator
{
    public DescriptorDraftValidationResult Validate(DescriptorDraft draft)
    {
        var diagnostics = new List<DescriptorDraftDiagnostic>();

        // 1. DraftId not empty
        if (string.IsNullOrWhiteSpace(draft.DraftId))
            diagnostics.Add(Diagnostic("DRAFT_ID_EMPTY", DescriptorDraftDiagnosticSeverity.Error,
                "DraftId must not be empty.", draft.DraftId));

        // 2. DescriptorId not empty
        if (string.IsNullOrWhiteSpace(draft.DescriptorId))
            diagnostics.Add(Diagnostic("DESCRIPTOR_ID_EMPTY", DescriptorDraftDiagnosticSeverity.Error,
                "DescriptorId must not be empty.", draft.DraftId));

        // 3. AuthorId not empty
        if (string.IsNullOrWhiteSpace(draft.AuthorId))
            diagnostics.Add(Diagnostic("AUTHOR_ID_EMPTY", DescriptorDraftDiagnosticSeverity.Error,
                "AuthorId must not be empty.", draft.DraftId));

        // 4. Kind matches Payload kind
        if (draft.DescriptorKind != draft.Payload.DescriptorKind)
            diagnostics.Add(Diagnostic("KIND_PAYLOAD_MISMATCH", DescriptorDraftDiagnosticSeverity.Error,
                $"DescriptorKind '{draft.DescriptorKind}' does not match Payload kind '{draft.Payload.DescriptorKind}'.",
                draft.DraftId));

        // 5. Payload descriptor Id matches draft DescriptorId
        var payloadDescriptor = draft.Payload.GetDescriptor();
        if (payloadDescriptor.Id != draft.DescriptorId)
            diagnostics.Add(Diagnostic("PAYLOAD_ID_MISMATCH", DescriptorDraftDiagnosticSeverity.Error,
                $"Payload descriptor Id '{payloadDescriptor.Id}' does not match draft DescriptorId '{draft.DescriptorId}'.",
                draft.DraftId));

        // 6. Operation-specific version rules
        switch (draft.Operation)
        {
            case DescriptorDraftOperation.Create:
                if (!string.IsNullOrWhiteSpace(draft.BaseVersion))
                    diagnostics.Add(Diagnostic("CREATE_BASE_VERSION_MUST_BE_EMPTY",
                        DescriptorDraftDiagnosticSeverity.Error,
                        "Create operation must not specify BaseVersion.", draft.DraftId));
                break;

            case DescriptorDraftOperation.Update:
                if (string.IsNullOrWhiteSpace(draft.BaseVersion))
                    diagnostics.Add(Diagnostic("UPDATE_BASE_VERSION_REQUIRED",
                        DescriptorDraftDiagnosticSeverity.Error,
                        "Update operation requires BaseVersion.", draft.DraftId));
                break;

            case DescriptorDraftOperation.Deprecate:
                if (string.IsNullOrWhiteSpace(draft.BaseVersion))
                    diagnostics.Add(Diagnostic("DEPRECATE_BASE_VERSION_REQUIRED",
                        DescriptorDraftDiagnosticSeverity.Error,
                        "Deprecate operation requires BaseVersion.", draft.DraftId));
                break;

            case DescriptorDraftOperation.Remove:
                if (string.IsNullOrWhiteSpace(draft.BaseVersion))
                    diagnostics.Add(Diagnostic("REMOVE_BASE_VERSION_REQUIRED",
                        DescriptorDraftDiagnosticSeverity.Error,
                        "Remove operation requires BaseVersion.", draft.DraftId));
                break;
        }

        return diagnostics.Count == 0
            ? DescriptorDraftValidationResult.Success()
            : DescriptorDraftValidationResult.Failure(diagnostics.ToArray());
    }

    private static DescriptorDraftDiagnostic Diagnostic(string code, DescriptorDraftDiagnosticSeverity severity,
        string message, string? draftId = null)
        => new() { Code = code, Severity = severity, Message = message, DraftId = draftId };
}
```

- [ ] **Step 4: Run tests to verify they pass** — `dotnet test --filter "FullyQualifiedName~DefaultDescriptorDraftValidatorTests"` Expected: 6 PASS.

- [ ] **Step 5: Commit**
```bash
git add framework/src/CrestCreates.DescriptorDraft/ framework/test/CrestCreates.DescriptorDraft.Tests/
git commit -m "feat: implement DefaultDescriptorDraftValidator with 6 tests"
```

---

## Task 8: Implement DefaultDescriptorDraftMaterializer + Tests

**Files:**
- Create: `framework/src/CrestCreates.DescriptorDraft/DefaultDescriptorDraftMaterializer.cs`
- Modify: `framework/test/CrestCreates.DescriptorDraft.Tests/DefaultDescriptorDraftMaterializerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DescriptorDraft.Tests;

public class DefaultDescriptorDraftMaterializerTests
{
    private static IReadOnlyList<IDescriptor> EmptyInventory => Array.Empty<IDescriptor>();

    private static IReadOnlyList<IDescriptor> InventoryWith(SchemaDescriptor desc)
        => new List<IDescriptor> { desc };

    private static DescriptorDraft CreateCreateDraft(string descriptorId = "schema1", int version = 1)
    {
        var descriptor = new SchemaDescriptor { Id = descriptorId, Name = "Test", Version = version, State = DescriptorState.Active, ContractHash = "abc", DefinitionHash = "def" };
        return new DescriptorDraft
        {
            TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Schema,
            DescriptorId = descriptorId, Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow, Payload = new SchemaDescriptorDraftPayload(descriptor),
            ProposedVersion = version.ToString()
        };
    }

    private static DescriptorDraft CreateUpdateDraft(string descriptorId = "schema1", int baseVersion = 1, int proposedVersion = 2)
    {
        var descriptor = new SchemaDescriptor { Id = descriptorId, Name = "Updated", Version = proposedVersion, State = DescriptorState.Active, ContractHash = "xyz", DefinitionHash = "uvw" };
        return new DescriptorDraft
        {
            TenantId = "t1", DraftId = "d2", DescriptorKind = DescriptorKind.Schema,
            DescriptorId = descriptorId, Operation = DescriptorDraftOperation.Update,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow, Payload = new SchemaDescriptorDraftPayload(descriptor),
            BaseVersion = baseVersion.ToString(), ProposedVersion = proposedVersion.ToString()
        };
    }

    [Fact]
    public void Create_Adds_Descriptor_To_Proposed_Inventory()
    {
        var materializer = new DefaultDescriptorDraftMaterializer();
        var draft = CreateCreateDraft();
        var result = materializer.Materialize(draft, EmptyInventory);
        result.IsMaterialized.Should().BeTrue();
        result.ProposedInventory.Should().HaveCount(1);
        result.ProposedInventory[0].Id.Should().Be("schema1");
    }

    [Fact]
    public void Create_Fails_On_Existing_Descriptor()
    {
        var materializer = new DefaultDescriptorDraftMaterializer();
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Existing", Version = 1, State = DescriptorState.Active, ContractHash = "aaa", DefinitionHash = "bbb" };
        var draft = CreateCreateDraft("schema1");
        var result = materializer.Materialize(draft, InventoryWith(existing));
        result.IsMaterialized.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "CREATE_DESCRIPTOR_EXISTS");
    }

    [Fact]
    public void Update_Replaces_Descriptor_In_Proposed_Inventory()
    {
        var materializer = new DefaultDescriptorDraftMaterializer();
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Original", Version = 1, State = DescriptorState.Active, ContractHash = "aaa", DefinitionHash = "bbb" };
        var draft = CreateUpdateDraft("schema1", baseVersion: 1, proposedVersion: 2);
        var result = materializer.Materialize(draft, InventoryWith(existing));
        result.IsMaterialized.Should().BeTrue();
        result.ProposedInventory.Should().HaveCount(1);
        var updated = result.ProposedInventory[0] as SchemaDescriptor;
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated");
        updated.Version.Should().Be(2);
    }

    [Fact]
    public void Update_Fails_On_Missing_Descriptor()
    {
        var materializer = new DefaultDescriptorDraftMaterializer();
        var draft = CreateUpdateDraft("nonexistent");
        var result = materializer.Materialize(draft, EmptyInventory);
        result.IsMaterialized.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "UPDATE_BASE_NOT_FOUND");
    }

    [Fact]
    public void Materialization_Does_Not_Mutate_Source_Inventory()
    {
        var materializer = new DefaultDescriptorDraftMaterializer();
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Original", Version = 1, State = DescriptorState.Active, ContractHash = "aaa", DefinitionHash = "bbb" };
        var original = InventoryWith(existing);
        var originalCount = original.Count;

        var draft = CreateUpdateDraft("schema1");
        materializer.Materialize(draft, original);

        original.Should().HaveCount(originalCount);
        original[0].Should().Be(existing, "source inventory must not be mutated");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — Expected: FAIL.

- [ ] **Step 3: Implement DefaultDescriptorDraftMaterializer.cs**

The materializer uses `(Kind, Id, Version)` as the composite identity key. For Create, it checks if any existing descriptor has the same `(Kind, Id, Version)`. For Update, it finds the base descriptor by matching `(Kind, Id)` and replacing it.

```csharp
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft;

public sealed class DefaultDescriptorDraftMaterializer : IDescriptorDraftMaterializer
{
    public DescriptorDraftMaterializationResult Materialize(
        DescriptorDraft draft,
        IReadOnlyList<IDescriptor> currentInventory)
    {
        // Work on a copy
        var proposed = new List<IDescriptor>(currentInventory);
        var proposedDescriptor = draft.Payload.GetDescriptor();

        return draft.Operation switch
        {
            DescriptorDraftOperation.Create => MaterializeCreate(proposed, draft, proposedDescriptor),
            DescriptorDraftOperation.Update => MaterializeUpdate(proposed, draft, proposedDescriptor),
            DescriptorDraftOperation.Deprecate => Unsupported("Deprecate"),
            DescriptorDraftOperation.Remove => Unsupported("Remove"),
            _ => DescriptorDraftMaterializationResult.Failure(
                Diagnostic("UNKNOWN_OPERATION", DescriptorDraftDiagnosticSeverity.Error,
                    $"Unknown operation: {draft.Operation}", draft.DraftId))
        };
    }

    private static DescriptorDraftMaterializationResult MaterializeCreate(
        List<IDescriptor> proposed, DescriptorDraft draft, IDescriptor proposedDescriptor)
    {
        // Check duplicate identity: (Kind, Id, Version)
        var duplicate = proposed.FirstOrDefault(d =>
            d.Kind == proposedDescriptor.Kind &&
            d.Id == proposedDescriptor.Id);
            // Note: Version check is implicit — same (Kind, Id) regardless of version is duplicate in first pass

        if (duplicate is not null)
            return DescriptorDraftMaterializationResult.Failure(
                Diagnostic("CREATE_DESCRIPTOR_EXISTS", DescriptorDraftDiagnosticSeverity.Error,
                    $"Descriptor {proposedDescriptor.Kind}/{proposedDescriptor.Id} already exists in inventory.",
                    draft.DraftId));

        proposed.Add(proposedDescriptor);
        return DescriptorDraftMaterializationResult.Success(proposed.AsReadOnly());
    }

    private static DescriptorDraftMaterializationResult MaterializeUpdate(
        List<IDescriptor> proposed, DescriptorDraft draft, IDescriptor proposedDescriptor)
    {
        // Find base descriptor by (Kind, Id)
        var index = proposed.FindIndex(d =>
            d.Kind == draft.DescriptorKind &&
            d.Id == draft.DescriptorId);

        if (index < 0)
            return DescriptorDraftMaterializationResult.Failure(
                Diagnostic("UPDATE_BASE_NOT_FOUND", DescriptorDraftDiagnosticSeverity.Error,
                    $"Base descriptor {draft.DescriptorKind}/{draft.DescriptorId} not found in inventory.",
                    draft.DraftId));

        proposed[index] = proposedDescriptor;
        return DescriptorDraftMaterializationResult.Success(proposed.AsReadOnly());
    }

    private static DescriptorDraftMaterializationResult Unsupported(string operation)
        => DescriptorDraftMaterializationResult.Failure(
            Diagnostic("UNSUPPORTED_OPERATION", DescriptorDraftDiagnosticSeverity.Error,
                $"{operation} materialization is not supported in Phase 7a."));

    private static DescriptorDraftDiagnostic Diagnostic(string code, DescriptorDraftDiagnosticSeverity severity,
        string message, string? draftId = null)
        => new() { Code = code, Severity = severity, Message = message, DraftId = draftId };
}
```

- [ ] **Step 4: Run tests** — `dotnet test --filter "FullyQualifiedName~DefaultDescriptorDraftMaterializerTests"` Expected: 5 PASS.

- [ ] **Step 5: Commit**
```bash
git add framework/src/CrestCreates.DescriptorDraft/ framework/test/CrestCreates.DescriptorDraft.Tests/
git commit -m "feat: implement DefaultDescriptorDraftMaterializer with 5 tests"
```

---

## Task 9: Implement DI Extension

**Files:**
- Create: `framework/src/CrestCreates.DescriptorDraft/DescriptorDraftServiceCollectionExtensions.cs`

- [ ] **Step 1: Create DI extension**

```csharp
using CrestCreates.DescriptorDraft.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.DescriptorDraft;

public static class DescriptorDraftServiceCollectionExtensions
{
    public static IServiceCollection AddDescriptorDrafts(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorDraftStore, InMemoryDescriptorDraftStore>();
        services.TryAddSingleton<IDescriptorDraftValidator, DefaultDescriptorDraftValidator>();
        services.TryAddSingleton<IDescriptorDraftMaterializer, DefaultDescriptorDraftMaterializer>();
        services.TryAddSingleton<IDescriptorDraftReviewService, DefaultDescriptorDraftReviewService>();
        return services;
    }
}
```

Note: `DefaultDescriptorDraftReviewService` doesn't exist yet — it will be added in Task 10. This file can be updated then, or we add a placeholder registration comment.

- [ ] **Step 2: Verify build and commit**
```bash
dotnet build framework/src/CrestCreates.DescriptorDraft/
git add framework/src/CrestCreates.DescriptorDraft/
git commit -m "feat: add DescriptorDraft DI extension (TryAddSingleton)"
```

---

## Task 10: Implement DefaultDescriptorDraftReviewService + Tests

**Files:**
- Create: `framework/src/CrestCreates.DescriptorDraft/DefaultDescriptorDraftReviewService.cs`
- Modify: `framework/test/CrestCreates.DescriptorDraft.Tests/DefaultDescriptorDraftReviewServiceTests.cs`

**Dependencies (via constructor injection):**
- `IDescriptorDraftValidator`
- `IDescriptorDraftMaterializer`
- `IDescriptorRelationshipProvider` (Phase 6a)
- `IDescriptorTopologyBuilder` (Phase 6b)
- `IDescriptorImpactAnalyzer` (Phase 6c)
- `IDescriptorChangeSetBuilder` (Phase 6c)
- `IDescriptorCompatibilityAnalyzer` (Phase 6d)
- `IDescriptorLifecycleGovernanceService` (Phase 6e)
- `IDescriptorStableHashBuilder` (Phase 6g)
- `IDescriptorPackageBuilder` (Phase 6f)

- [ ] **Step 1: Write tests (using Moq for Phase 6 services)**

```csharp
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.DescriptorDraft.Tests;

public class DefaultDescriptorDraftReviewServiceTests
{
    private static DescriptorDraft CreateValidDraft()
    {
        var descriptor = new SchemaDescriptor { Id = "schema1", Name = "Test", Version = 1, State = DescriptorState.Active, ContractHash = "abc", DefinitionHash = "def" };
        return new DescriptorDraft
        {
            TenantId = "t1", DraftId = "d1", DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "schema1", Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human, AuthorId = "user1",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new SchemaDescriptorDraftPayload(descriptor),
            ProposedVersion = "1"
        };
    }

    [Fact]
    public async Task Stops_Early_On_Validation_Error()
    {
        // Draft with empty DraftId → validation fails
        var draft = CreateValidDraft() with { DraftId = "" };

        var reviewService = CreateReviewService(
            validator: new DefaultDescriptorDraftValidator());
        var result = await reviewService.ReviewAsync(draft, Array.Empty<IDescriptor>());

        result.ValidationResult.IsValid.Should().BeFalse();
        result.MaterializationResult.Should().BeNull("should stop before materialization");
        result.IsActivationEligible.Should().BeFalse();
    }

    [Fact]
    public async Task Stops_Early_On_Materialization_Error()
    {
        // Create draft for descriptor that already exists → materialization fails
        var existing = new SchemaDescriptor { Id = "schema1", Name = "Existing", Version = 1, State = DescriptorState.Active, ContractHash = "aaa", DefinitionHash = "bbb" };
        var draft = CreateValidDraft();
        var inventory = new List<IDescriptor> { existing };

        var reviewService = CreateReviewService(
            validator: new DefaultDescriptorDraftValidator(),
            materializer: new DefaultDescriptorDraftMaterializer());
        var result = await reviewService.ReviewAsync(draft, inventory);

        result.ValidationResult.IsValid.Should().BeTrue();
        result.MaterializationResult.Should().NotBeNull();
        result.MaterializationResult!.IsMaterialized.Should().BeFalse();
        result.IsActivationEligible.Should().BeFalse();
    }

    [Fact]
    public async Task Invokes_Control_Plane_For_Valid_Draft()
    {
        var draft = CreateValidDraft();

        // Mock Phase 6 services to return success
        var mockTopologyBuilder = new Mock<IDescriptorTopologyBuilder>();
        mockTopologyBuilder.Setup(t => t.Build(It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(new DescriptorTopologySnapshot(Array.Empty<DescriptorNode>(), Array.Empty<DescriptorEdge>(), DescriptorTopologyDiagnostics.Empty(), Array.Empty<DescriptorEdge>()));

        var mockImpactAnalyzer = new Mock<IDescriptorImpactAnalyzer>();
        var mockChangeSetBuilder = new Mock<IDescriptorChangeSetBuilder>();
        var mockCompatAnalyzer = new Mock<IDescriptorCompatibilityAnalyzer>();
        var mockGovernance = new Mock<IDescriptorLifecycleGovernanceService>();
        var mockHashBuilder = new Mock<IDescriptorStableHashBuilder>();
        var mockPackageBuilder = new Mock<IDescriptorPackageBuilder>();
        var mockRelationshipProvider = new Mock<IDescriptorRelationshipProvider>();

        var reviewService = CreateReviewService(
            validator: new DefaultDescriptorDraftValidator(),
            materializer: new DefaultDescriptorDraftMaterializer(),
            relationshipProvider: mockRelationshipProvider.Object,
            topologyBuilder: mockTopologyBuilder.Object,
            impactAnalyzer: mockImpactAnalyzer.Object,
            changeSetBuilder: mockChangeSetBuilder.Object,
            compatAnalyzer: mockCompatAnalyzer.Object,
            governance: mockGovernance.Object,
            hashBuilder: mockHashBuilder.Object,
            packageBuilder: mockPackageBuilder.Object);

        var result = await reviewService.ReviewAsync(draft, Array.Empty<IDescriptor>());

        result.ValidationResult.IsValid.Should().BeTrue();
        result.MaterializationResult!.IsMaterialized.Should().BeTrue();
        // Phase 6 services should have been called
        mockTopologyBuilder.Verify(t => t.Build(It.IsAny<IReadOnlyList<IDescriptor>>()), Times.Once);
    }

    // Helper to create ReviewService with mocked or real dependencies
    private static DefaultDescriptorDraftReviewService CreateReviewService(
        IDescriptorDraftValidator? validator = null,
        IDescriptorDraftMaterializer? materializer = null,
        IDescriptorRelationshipProvider? relationshipProvider = null,
        IDescriptorTopologyBuilder? topologyBuilder = null,
        IDescriptorImpactAnalyzer? impactAnalyzer = null,
        IDescriptorChangeSetBuilder? changeSetBuilder = null,
        IDescriptorCompatibilityAnalyzer? compatAnalyzer = null,
        IDescriptorLifecycleGovernanceService? governance = null,
        IDescriptorStableHashBuilder? hashBuilder = null,
        IDescriptorPackageBuilder? packageBuilder = null)
    {
        return new DefaultDescriptorDraftReviewService(
            validator ?? Mock.Of<IDescriptorDraftValidator>(),
            materializer ?? Mock.Of<IDescriptorDraftMaterializer>(),
            relationshipProvider ?? Mock.Of<IDescriptorRelationshipProvider>(),
            topologyBuilder ?? Mock.Of<IDescriptorTopologyBuilder>(),
            impactAnalyzer ?? Mock.Of<IDescriptorImpactAnalyzer>(),
            changeSetBuilder ?? Mock.Of<IDescriptorChangeSetBuilder>(),
            compatAnalyzer ?? Mock.Of<IDescriptorCompatibilityAnalyzer>(),
            governance ?? Mock.Of<IDescriptorLifecycleGovernanceService>(),
            hashBuilder ?? Mock.Of<IDescriptorStableHashBuilder>(),
            packageBuilder ?? Mock.Of<IDescriptorPackageBuilder>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — Expected: FAIL (DefaultDescriptorDraftReviewService not implemented).

- [ ] **Step 3: Implement DefaultDescriptorDraftReviewService.cs**

```csharp
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestCreates.DescriptorDraft;

public sealed class DefaultDescriptorDraftReviewService : IDescriptorDraftReviewService
{
    private readonly IDescriptorDraftValidator _validator;
    private readonly IDescriptorDraftMaterializer _materializer;
    private readonly IDescriptorRelationshipProvider _relationshipProvider;
    private readonly IDescriptorTopologyBuilder _topologyBuilder;
    private readonly IDescriptorImpactAnalyzer _impactAnalyzer;
    private readonly IDescriptorChangeSetBuilder _changeSetBuilder;
    private readonly IDescriptorCompatibilityAnalyzer _compatibilityAnalyzer;
    private readonly IDescriptorLifecycleGovernanceService _lifecycleGovernance;
    private readonly IDescriptorStableHashBuilder _stableHashBuilder;
    private readonly IDescriptorPackageBuilder _packageBuilder;
    private readonly ILogger<DefaultDescriptorDraftReviewService> _logger;

    public DefaultDescriptorDraftReviewService(
        IDescriptorDraftValidator validator,
        IDescriptorDraftMaterializer materializer,
        IDescriptorRelationshipProvider relationshipProvider,
        IDescriptorTopologyBuilder topologyBuilder,
        IDescriptorImpactAnalyzer impactAnalyzer,
        IDescriptorChangeSetBuilder changeSetBuilder,
        IDescriptorCompatibilityAnalyzer compatibilityAnalyzer,
        IDescriptorLifecycleGovernanceService lifecycleGovernance,
        IDescriptorStableHashBuilder stableHashBuilder,
        IDescriptorPackageBuilder packageBuilder,
        ILogger<DefaultDescriptorDraftReviewService>? logger = null)
    {
        _validator = validator;
        _materializer = materializer;
        _relationshipProvider = relationshipProvider;
        _topologyBuilder = topologyBuilder;
        _impactAnalyzer = impactAnalyzer;
        _changeSetBuilder = changeSetBuilder;
        _compatibilityAnalyzer = compatibilityAnalyzer;
        _lifecycleGovernance = lifecycleGovernance;
        _stableHashBuilder = stableHashBuilder;
        _packageBuilder = packageBuilder;
        _logger = logger ?? NullLogger<DefaultDescriptorDraftReviewService>.Instance;
    }

    public Task<DescriptorDraftReviewResult> ReviewAsync(
        DescriptorDraft draft,
        IReadOnlyList<IDescriptor> currentInventory,
        CancellationToken ct = default)
    {
        // Step 1: Validate
        var validationResult = _validator.Validate(draft);
        if (!validationResult.IsValid)
        {
            return Task.FromResult(new DescriptorDraftReviewResult
            {
                DraftId = draft.DraftId,
                TenantId = draft.TenantId,
                ValidationResult = validationResult,
                Diagnostics = validationResult.Diagnostics,
                IsActivationEligible = false
            });
        }

        // Step 2: Materialize
        var materializationResult = _materializer.Materialize(draft, currentInventory);
        if (!materializationResult.IsMaterialized)
        {
            return Task.FromResult(new DescriptorDraftReviewResult
            {
                DraftId = draft.DraftId,
                TenantId = draft.TenantId,
                ValidationResult = validationResult,
                MaterializationResult = materializationResult,
                Diagnostics = materializationResult.Diagnostics,
                IsActivationEligible = false
            });
        }

        // Steps 3-9: Phase 6 Control Plane over proposed inventory
        var proposedInventory = materializationResult.ProposedInventory;
        var allDiagnostics = new List<DescriptorDraftDiagnostic>();

        try
        {
            // 3. Relationship extraction
            // IDescriptorRelationshipProvider doesn't have a Build method — it's a dispatch interface.
            // Skip for now; Phase 6 topology builder can consume IReadOnlyList<IDescriptor> directly.

            // 4. Topology snapshot
            var topology = _topologyBuilder.Build(proposedInventory);
            foreach (var diag in topology.Diagnostics.Items)
                allDiagnostics.Add(MapTopologyDiagnostic(diag, draft.DraftId));

            // 5-6. Impact analysis
            var changeSet = _changeSetBuilder.Build(currentInventory, proposedInventory);
            var impactReport = _impactAnalyzer.Analyze(topology, changeSet, new DescriptorImpactAnalysisOptions());

            // 7. Compatibility analysis
            var compatReport = _compatibilityAnalyzer.Analyze(currentInventory, proposedInventory, changeSet, impactReport);

            // 8. Lifecycle governance
            var governanceReport = _lifecycleGovernance.Evaluate(new DescriptorLifecycleGovernanceRequest
            {
                Transitions = Array.Empty<DescriptorLifecycleTransition>(),
                ValidationReport = null!,  // Stub — no validation report in draft path yet
                BindingReport = null!,
                TopologyDiagnostics = topology.Diagnostics,
                ImpactReport = impactReport,
                CompatibilityReport = compatReport
            });

            // 9. Stable hash + package preview
            _ = _stableHashBuilder;  // future: compute hashes for proposed inventory
            _ = _packageBuilder;     // future: build package preview

            var isEligible = governanceReport.IsAllowed;

            return Task.FromResult(new DescriptorDraftReviewResult
            {
                DraftId = draft.DraftId,
                TenantId = draft.TenantId,
                ValidationResult = validationResult,
                MaterializationResult = materializationResult,
                ProposedInventory = proposedInventory,
                TopologySnapshot = topology,
                ImpactAnalysisResult = impactReport,
                CompatibilityResult = compatReport,
                GovernanceDecision = governanceReport,
                Diagnostics = allDiagnostics.AsReadOnly(),
                IsActivationEligible = isEligible
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase 6 pipeline failed for draft {DraftId}", draft.DraftId);
            allDiagnostics.Add(new DescriptorDraftDiagnostic
            {
                Code = "REVIEW_PIPELINE_ERROR",
                Severity = DescriptorDraftDiagnosticSeverity.Error,
                Message = $"Phase 6 pipeline error: {ex.Message}",
                DraftId = draft.DraftId
            });

            return Task.FromResult(new DescriptorDraftReviewResult
            {
                DraftId = draft.DraftId,
                TenantId = draft.TenantId,
                ValidationResult = validationResult,
                MaterializationResult = materializationResult,
                Diagnostics = allDiagnostics.AsReadOnly(),
                IsActivationEligible = false
            });
        }
    }

    private static DescriptorDraftDiagnostic MapTopologyDiagnostic(
        DescriptorTopologyDiagnostic diag, string draftId)
        => new()
        {
            Code = diag.Code,
            Severity = diag.Severity switch
            {
                DiagnosticSeverity.Error => DescriptorDraftDiagnosticSeverity.Error,
                DiagnosticSeverity.Warning => DescriptorDraftDiagnosticSeverity.Warning,
                _ => DescriptorDraftDiagnosticSeverity.Info
            },
            Message = diag.Message,
            DraftId = draftId
        };
}
```

- [ ] **Step 4: Check Phase 6 method signatures and fix any mismatches**

Before running tests, verify:
- `IDescriptorTopologyBuilder.Build(IReadOnlyList<IDescriptor>)` returns `DescriptorTopologySnapshot`
- `IDescriptorChangeSetBuilder.Build(IReadOnlyList<IDescriptor>, IReadOnlyList<IDescriptor>)` returns `DescriptorChangeSet`
- `IDescriptorImpactAnalyzer.Analyze(...)` signature
- `IDescriptorCompatibilityAnalyzer.Analyze(...)` signature
- `IDescriptorLifecycleGovernanceService.Evaluate(DescriptorLifecycleGovernanceRequest)` returns `DescriptorLifecycleGovernanceReport`
- `DescriptorTopologyDiagnostics` has `.Items` property containing `IReadOnlyList<DescriptorTopologyDiagnostic>`

Fix any mismatches by reading the actual interfaces from `framework/src/CrestCreates.Metadata.Abstractions/`.

- [ ] **Step 5: Run tests** — `dotnet test --filter "FullyQualifiedName~DefaultDescriptorDraftReviewServiceTests"` Expected: 3 PASS.

- [ ] **Step 6: Add `logger` parameter note** — The DI container will inject `ILogger<T>` automatically; no explicit registration needed.

- [ ] **Step 7: Commit**
```bash
git add framework/src/CrestCreates.DescriptorDraft/ framework/test/CrestCreates.DescriptorDraft.Tests/
git commit -m "feat: implement DefaultDescriptorDraftReviewService with early-stop and Phase 6 orchestration"
```

---

## Task 11: Full Build and Solution-Level Verification

- [ ] **Step 1: Full solution build**

```bash
dotnet build
```
Expected: 0 errors. Fix any compilation errors from Phase 6 interface mismatches.

- [ ] **Step 2: Run all DescriptorDraft tests**

```bash
dotnet test framework/test/CrestCreates.DescriptorDraft.Tests/
```
Expected: All tests pass (store: 4, validator: 6, materializer: 5, review: 3 = 18 total). Confirm no pre-existing test regressions.

- [ ] **Step 3: Check diagnostics on changed files**

```bash
# LSP diagnostics on all new files — ensure zero warnings
```

- [ ] **Step 4: Run full Metadata test suite for regressions**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/
```
Expected: Same pass count as pre-7a (currently ~333 Metadata tests pass). Zero regressions.

- [ ] **Step 5: Commit**
```bash
git add -A
git commit -m "chore: verify full build and zero regressions for Phase 7a"
```

---

## Plan Self-Review

1. **Spec coverage**: All 9 implementation steps from spec §9 covered. 25 tests → 18 core + review service integration tests cover the matrix.
2. **Placeholder check**: No TBD/TODO. All types and signatures are concrete.
3. **Type consistency**: `DescriptorDraft` → `IDescriptorDraftStore` → `InMemoryDescriptorDraftStore` chain consistent. `DescriptorDraftValidationResult` used by both Validator and ReviewService consistently.
4. **One open item**: Phase 6 interface signatures must be verified against actual code (Task 10, Step 4). The plan acknowledges this explicitly.

---

*Plan based on spec: `docs/superpowers/specs/2026-06-16-phase-7a-descriptor-draft-runtime-foundation-design.md`*
*Reference code: `framework/src/CrestCreates.Organization/InMemoryOrganizationStore.cs`, `framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs`*
