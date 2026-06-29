# Agent Memory & Context Compression Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first-phase Agent Memory & Context Compression runtime for #43: sanitized conversation/task history, deterministic compression, candidate extraction, explicit promotion, budgeted recall, source expansion, and AgentAuthoringContext composition.

**Architecture:** Add `CrestCreates.Agent.Memory.Abstractions` for trim-safe contracts and `CrestCreates.Agent.Memory` for deterministic in-memory services. The runtime composes Metadata ContextPack plus recalled memory, but Memory does not depend on ControlPlane, LLM adapters, persistence providers, Dynamic API, Web, Platform, or activation services.

**Tech Stack:** .NET 10, C# nullable enabled, xUnit 2.9.3, FluentAssertions, Microsoft.Extensions.DependencyInjection, System.Text.Json source generation.

## Global Constraints

- Implement the approved spec: `docs/superpowers/specs/2026-06-29-phase-7e-agent-memory-context-compression-runtime-design.md`.
- Runtime service first: no Control Plane tools, no AgentRuntime orchestration surface, no LLM adapter, no persisted provider.
- `CrestCreates.Agent.Memory.Abstractions` may reference `CrestCreates.Agent.Abstractions`, `CrestCreates.Metadata.Abstractions`, and `CrestCreates.Metadata.ContextPack.Abstractions`; it must not reference `CrestCreates.Agent.ControlPlane.Abstractions`.
- `CrestCreates.Agent.Memory` may reference its abstractions project and Microsoft DI abstractions; it must not reference Framework Api/Web, Platform, ControlPlane, Persistence providers, or activation services.
- Sanitization happens before storage, compression, extraction, promotion, recall, and source expansion.
- Canonical hashes use sanitized canonical content only; `TimeProvider` must not be read during hash computation.
- Memory is context infrastructure, not authority. Recalled memory must be marked non-authoritative and must not override `MetadataContextPack`.
- `IAgentMemoryPromotionService` is the production path for candidate-to-active-memory promotion. `SaveMemoryAsync` is a persistence primitive used by stores and tests only.
- `AgentMemoryQuery` may be shared by store and recall in phase 1, but store filtering fields and recall scoring or budget fields must remain semantically distinct.
- Tests must prove the main chain without a real LLM.

---

## File Map

- Create `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj`: memory contract project.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/GlobalUsings.cs`: shared BCL and metadata usings.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryContracts.cs`: source refs, diagnostics, conversation/task records, sanitized records, compression blocks, memory candidates/items, recall packs, authoring context, and request models.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryInterfaces.cs`: store and runtime service interfaces.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/Json/AgentMemoryJsonSerializerContext.cs`: AoT JSON context.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj`: default in-memory runtime service project.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/AgentMemoryServiceCollectionExtensions.cs`: DI registration.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentConversationStore.cs`: conversation history store.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentTaskHistoryStore.cs`: task history store.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentCompressedContextStore.cs`: compressed context store.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentMemoryStore.cs`: candidate and memory store primitive.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Sanitization/DefaultAgentMemoryContentSanitizer.cs`: deterministic redaction/rejection.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Compression/DefaultAgentContextCompressor.cs`: deterministic compressor.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Extraction/DefaultAgentMemoryExtractor.cs`: deterministic candidate extractor.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Promotion/DefaultAgentMemoryPromotionService.cs`: candidate promotion, reject, supersede, archive semantics.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Recall/DefaultAgentMemoryRetriever.cs`: tenant-aware visibility-filtered recall.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Sources/DefaultAgentContextSourceExpander.cs`: Memory-owned source expansion only.
- Create `src/Runtime/Agent/CrestCreates.Agent.Memory/Authoring/DefaultAgentAuthoringContextBuilder.cs`: Metadata ContextPack + AgentMemoryPack composition.
- Create `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/CrestCreates.Agent.Memory.Tests.csproj`: test project.
- Create focused test files under `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/`: `BoundaryTests.cs`, `StoreTests.cs`, `SanitizerTests.cs`, `CompressionTests.cs`, `PromotionTests.cs`, `RecallTests.cs`, `SourceExpansionTests.cs`, `AuthoringContextBuilderTests.cs`, `MainChainTests.cs`, `AgentMemoryTestData.cs`.
- Modify `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs`: memory-specific dependency boundary assertions.
- Modify `CrestCreates.slnx` and `solutions/CrestCreates.All.slnx`: include the two memory projects and test project.
- Modify `memory.md`: record #43 first runtime closure only after final verification passes.

---

### Task 1: Project Scaffolding And Dependency Boundaries

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/CrestCreates.Agent.Memory.Tests.csproj`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/BoundaryTests.cs`
- Modify: `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs`
- Modify: `CrestCreates.slnx`
- Modify: `solutions/CrestCreates.All.slnx`

**Interfaces:**
- Consumes: existing `CrestCreates.Agent.Abstractions`, `CrestCreates.Metadata.Abstractions`, `CrestCreates.Metadata.ContextPack.Abstractions`.
- Produces: buildable empty Memory projects and failing boundary tests that become passing once references are correct.

- [ ] **Step 1: Create the failing project boundary tests**

Add this test file:

```csharp
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class BoundaryTests
{
    [Fact]
    public void AgentMemoryAbstractionsAssembly_DoesNotReference_ControlPlaneAbstractions()
    {
        typeof(CrestCreates.Agent.Memory.Abstractions.AgentMemoryDiagnostic).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain("CrestCreates.Agent.ControlPlane.Abstractions");
    }

    [Fact]
    public void AgentMemoryRuntimeAssembly_DoesNotReference_ControlPlane()
    {
        typeof(CrestCreates.Agent.Memory.AgentMemoryServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.ControlPlane",
                "CrestCreates.Agent.ControlPlane.Abstractions"
            });
    }
}
```

- [ ] **Step 2: Run the boundary tests to verify they fail because projects/types do not exist**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests`

Expected: FAIL with missing project or missing `CrestCreates.Agent.Memory` types.

- [ ] **Step 3: Add the project files**

Create `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Agent.Memory.Abstractions</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Memory.Abstractions</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../CrestCreates.Agent.Abstractions/CrestCreates.Agent.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.ContextPack.Abstractions/CrestCreates.Metadata.ContextPack.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

Create `src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Agent.Memory</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Memory</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>
</Project>
```

Create `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/CrestCreates.Agent.Memory.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Agent.Memory.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Memory.Tests</AssemblyName>
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
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj" />
    <ProjectReference Include="../../../../src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj" />
    <ProjectReference Include="../../../../src/Metadata/CrestCreates.Metadata.ContextPack.Abstractions/CrestCreates.Metadata.ContextPack.Abstractions.csproj" />
    <ProjectReference Include="../../../../src/Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add repository-level dependency boundary assertions**

Append these facts to `DependencyBoundaryTests`:

```csharp
[Fact]
public void AgentMemoryAbstractions_DoesNotReferenceControlPlaneAbstractions()
{
    AssertNoDirectProjectReferences(
        "src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions",
        "Agent Memory abstractions must remain runtime-context contracts and must not depend on ControlPlane contracts.",
        new[] { "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions" });
}

[Fact]
public void AgentMemoryProjects_DoNotReferenceForbiddenRuntimeOrPlatformLayers()
{
    AssertNoDirectProjectReferences(
        "src/Runtime/Agent/CrestCreates.Agent.Memory",
        "Agent Memory runtime must not depend on ControlPlane, Framework Api/Web, Platform, or persistence providers.",
        new[]
        {
            "src/Runtime/Agent/CrestCreates.Agent.ControlPlane/",
            "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/",
            "src/Framework/Api/",
            "src/Framework/Web/",
            "src/Platform/",
            "src/Persistence/CrestCreates.Data.FreeSql",
            "src/Persistence/CrestCreates.Data.SqlSugar"
        });
}
```

- [ ] **Step 5: Add placeholder contract/runtime marker types**

Create `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryDiagnostic.cs`:

```csharp
namespace CrestCreates.Agent.Memory.Abstractions;

public sealed record AgentMemoryDiagnostic
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public AgentMemoryDiagnosticSeverity Severity { get; init; } = AgentMemoryDiagnosticSeverity.Info;
}

public enum AgentMemoryDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Memory/AgentMemoryServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Agent.Memory;

public static class AgentMemoryServiceCollectionExtensions
{
    public static IServiceCollection AddAgentMemoryRuntime(this IServiceCollection services)
    {
        return services;
    }
}
```

- [ ] **Step 6: Add projects to `.slnx` files**

Add these entries under `/src/Runtime/Agent/` in both solution files:

```xml
<Project Path="src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj" />
<Project Path="src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj" />
```

Add this entry under `/tests/Runtime/Agent/` in both solution files:

```xml
<Project Path="tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/CrestCreates.Agent.Memory.Tests.csproj" />
```

- [ ] **Step 7: Run scaffolding verification**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests`

Expected: PASS.

Run: `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests`

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions src/Runtime/Agent/CrestCreates.Agent.Memory tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs CrestCreates.slnx solutions/CrestCreates.All.slnx
git commit -m "feat: scaffold agent memory runtime projects"
```

---

### Task 2: Memory Abstractions And AoT JSON Context

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/GlobalUsings.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryContracts.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryInterfaces.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/Json/AgentMemoryJsonSerializerContext.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryDiagnostic.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/ContractTests.cs`

**Interfaces:**
- Consumes: `DescriptorRef`, `DescriptorKind`, `MetadataContextPack`.
- Produces: all public contracts and interfaces used by later tasks.

- [ ] **Step 1: Write the failing contract tests**

Create `ContractTests.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class ContractTests
{
    [Fact]
    public void AgentMemoryConfidence_IsClosedEnum_NotFloatingPoint()
    {
        typeof(AgentMemoryConfidence).IsEnum.Should().BeTrue();
        typeof(AgentMemoryItem).GetProperty(nameof(AgentMemoryItem.Confidence))!
            .PropertyType.Should().Be(typeof(AgentMemoryConfidence));
    }

    [Fact]
    public void AgentContextEvidenceRef_IsNotNamedActivationEvidence()
    {
        typeof(AgentContextEvidenceRef).Name.Should().Be("AgentContextEvidenceRef");
        typeof(AgentContextEvidenceRef).Assembly.GetTypes()
            .Select(type => type.Name)
            .Should()
            .NotContain("AgentEvidenceRef");
    }

    [Fact]
    public void JsonContext_ContainsAgentMemoryPack()
    {
        JsonTypeInfo<AgentMemoryPack> typeInfo = AgentMemoryJsonSerializerContext.Default.AgentMemoryPack;
        typeInfo.Type.Should().Be(typeof(AgentMemoryPack));
    }

    [Fact]
    public void Contracts_DoNotExposeMutableCollectionTypes()
    {
        var mutableProperties = typeof(AgentMemoryPack).Assembly.GetTypes()
            .Where(type => type.IsPublic)
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Where(property => property.PropertyType.IsGenericType)
            .Where(property => property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToArray();

        mutableProperties.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify missing contracts**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~ContractTests"`

Expected: FAIL with missing type errors.

- [ ] **Step 3: Add contract types and interfaces**

Replace `AgentMemoryDiagnostic.cs` with the complete contract file split below. Keep the namespace exactly `CrestCreates.Agent.Memory.Abstractions`.

`GlobalUsings.cs`:

```csharp
global using CrestCreates.Metadata.Abstractions;
global using CrestCreates.Metadata.ContextPack.Abstractions;
```

`AgentMemoryContracts.cs` must contain these public types with these required members:

```csharp
namespace CrestCreates.Agent.Memory.Abstractions;

public enum AgentSourceKind { ConversationTurn = 0, TaskRecord = 1, TaskEvent = 2, CompressedContextBlock = 3, MemoryCandidate = 4, MemoryItem = 5, MetadataContextPack = 6, ReviewReport = 7, FixProposal = 8, PackagePreview = 9, ActivationRequest = 10 }
public enum AgentMemoryConfidence { Unknown = 0, Low = 1, Medium = 2, High = 3 }
public enum AgentMemoryStatus { Candidate = 0, Active = 1, Rejected = 2, Superseded = 3, Archived = 4 }
public enum AgentMemoryKind { Preference = 0, ProjectFact = 1, Decision = 2, Constraint = 3, WorkflowHint = 4, Risk = 5 }
public enum AgentConversationRole { User = 0, Assistant = 1, Tool = 2, System = 3 }
public enum AgentMemoryDiagnosticSeverity { Info = 0, Warning = 1, Error = 2 }
public enum AgentMemorySourceExpansionStatus { Expanded = 0, NotExpandable = 1, ExternalSourceNotSupported = 2, NotFound = 3, Redacted = 4 }
public enum AgentMemoryOperationKind { Promote = 0, Reject = 1, Supersede = 2, Archive = 3 }

public sealed record AgentContextSourceRef
{
    public required AgentSourceKind SourceKind { get; init; }
    public required string TenantId { get; init; }
    public required string SourceId { get; init; }
    public int? RangeStart { get; init; }
    public int? RangeEnd { get; init; }
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? CanonicalContentHash { get; init; }
}

public sealed record AgentContextEvidenceRef
{
    public required string EvidenceId { get; init; }
    public required string EvidenceKind { get; init; }
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public string? CanonicalContentHash { get; init; }
}

public sealed record AgentMemoryDiagnostic
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public AgentMemoryDiagnosticSeverity Severity { get; init; } = AgentMemoryDiagnosticSeverity.Info;
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
}

public sealed record AgentActorContext
{
    public required string ActorId { get; init; }
    public required string ActorKind { get; init; }
    public string? DisplayName { get; init; }
}

public sealed record AgentConversationTurn
{
    public required string TurnId { get; init; }
    public required string TenantId { get; init; }
    public required AgentConversationRole Role { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
}

public sealed record AgentConversationRecord
{
    public required string ConversationId { get; init; }
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentConversationTurn> Turns { get; init; } = Array.Empty<AgentConversationTurn>();
}

public sealed record AgentTaskEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string TaskId { get; init; }
    public required string EventKind { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
}

public sealed record AgentTaskRecord
{
    public required string TaskId { get; init; }
    public required string TenantId { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<AgentTaskEvent> Events { get; init; } = Array.Empty<AgentTaskEvent>();
}

public sealed record SanitizedAgentContent
{
    public required string SanitizedContent { get; init; }
    public required string CanonicalContentHash { get; init; }
    public bool Rejected { get; init; }
    public IReadOnlyList<string> RedactionKinds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentCompressedContextBlock
{
    public required string BlockId { get; init; }
    public required string TenantId { get; init; }
    public required string Content { get; init; }
    public required string CanonicalContentHash { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
    public int ApproximateCharacterCount => Content.Length;
}

public sealed record AgentCompressedContext
{
    public required string ContextId { get; init; }
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentCompressedContextBlock> Blocks { get; init; } = Array.Empty<AgentCompressedContextBlock>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentMemoryCandidate
{
    public required string CandidateId { get; init; }
    public required string TenantId { get; init; }
    public required AgentMemoryKind Kind { get; init; }
    public required string Content { get; init; }
    public required string CanonicalContentHash { get; init; }
    public AgentMemoryConfidence Confidence { get; init; } = AgentMemoryConfidence.Unknown;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public AgentMemoryStatus Status { get; init; } = AgentMemoryStatus.Candidate;
}

public sealed record AgentMemoryItem
{
    public required string MemoryId { get; init; }
    public required string TenantId { get; init; }
    public required AgentMemoryKind Kind { get; init; }
    public required string Content { get; init; }
    public required string CanonicalContentHash { get; init; }
    public required DateTimeOffset PromotedAt { get; init; }
    public AgentMemoryConfidence Confidence { get; init; } = AgentMemoryConfidence.Unknown;
    public AgentMemoryStatus Status { get; init; } = AgentMemoryStatus.Active;
    public bool IsAuthoritative { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public string? SupersedesMemoryId { get; init; }
    public string? SupersededByMemoryId { get; init; }
}

public sealed record AgentMemoryQuery
{
    public required string TenantId { get; init; }
    public string? IntentText { get; init; }
    public IReadOnlyList<string> MemoryIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AgentMemoryKind> Kinds { get; init; } = Array.Empty<AgentMemoryKind>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<DescriptorKind> VisibleDescriptorKinds { get; init; } = Array.Empty<DescriptorKind>();
    public int? MaxCount { get; init; }
    public int? CharacterBudget { get; init; }
    public AgentMemoryConfidence MinimumConfidence { get; init; } = AgentMemoryConfidence.Unknown;
    public bool IncludeStale { get; init; }
    public bool IncludeSuperseded { get; init; }
    public bool IncludeArchived { get; init; }
    public bool IncludeSourceRefs { get; init; } = true;
}

public sealed record AgentMemoryPack
{
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentMemoryItem> Memories { get; init; } = Array.Empty<AgentMemoryItem>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
    public bool IsAuthoritative { get; init; }
}

public sealed record AgentMemoryOperationRequest
{
    public required string TenantId { get; init; }
    public required AgentActorContext Actor { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public string? Explanation { get; init; }
}

public sealed record AgentSourceExpansionResult
{
    public required AgentContextSourceRef SourceRef { get; init; }
    public required AgentMemorySourceExpansionStatus Status { get; init; }
    public string? SanitizedContent { get; init; }
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentAuthoringRequest
{
    public required string TenantId { get; init; }
    public required string IntentText { get; init; }
    public AgentMemoryQuery? MemoryQuery { get; init; }
}

public sealed record AgentAuthoringContext
{
    public required AgentAuthoringRequest Request { get; init; }
    public required MetadataContextPack MetadataContextPack { get; init; }
    public required AgentMemoryPack MemoryPack { get; init; }
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}
```

`AgentMemoryInterfaces.cs` must define these method signatures:

```csharp
namespace CrestCreates.Agent.Memory.Abstractions;

public interface IAgentConversationStore
{
    ValueTask SaveConversationAsync(AgentConversationRecord conversation, CancellationToken cancellationToken = default);
    ValueTask<AgentConversationRecord?> GetConversationAsync(string tenantId, string conversationId, CancellationToken cancellationToken = default);
}

public interface IAgentTaskHistoryStore
{
    ValueTask SaveTaskAsync(AgentTaskRecord task, CancellationToken cancellationToken = default);
    ValueTask<AgentTaskRecord?> GetTaskAsync(string tenantId, string taskId, CancellationToken cancellationToken = default);
    ValueTask AppendEventAsync(string tenantId, string taskId, AgentTaskEvent taskEvent, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AgentTaskRecord>> ListTasksAsync(string tenantId, CancellationToken cancellationToken = default);
}

public interface IAgentCompressedContextStore
{
    ValueTask SaveCompressedContextAsync(AgentCompressedContext context, CancellationToken cancellationToken = default);
    ValueTask<AgentCompressedContext?> GetCompressedContextAsync(string tenantId, string contextId, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryStore
{
    ValueTask SaveCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryCandidate?> GetCandidateAsync(string tenantId, string candidateId, CancellationToken cancellationToken = default);
    ValueTask SaveMemoryAsync(AgentMemoryItem memory, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem?> GetMemoryAsync(string tenantId, string memoryId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AgentMemoryItem>> ListMemoriesAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryContentSanitizer
{
    SanitizedAgentContent Sanitize(string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs);
}

public interface IAgentContextCompressor
{
    ValueTask<AgentCompressedContext> CompressConversationAsync(AgentConversationRecord conversation, CancellationToken cancellationToken = default);
    ValueTask<AgentCompressedContext> CompressTaskAsync(AgentTaskRecord task, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryExtractor
{
    ValueTask<IReadOnlyList<AgentMemoryCandidate>> ExtractCandidatesAsync(AgentCompressedContext context, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryPromotionService
{
    ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
    ValueTask RejectAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, string memoryId, AgentMemoryCandidate replacement, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
    ValueTask ArchiveAsync(string tenantId, string memoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryRetriever
{
    ValueTask<AgentMemoryPack> RecallAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default);
}

public interface IAgentContextSourceExpander
{
    ValueTask<AgentSourceExpansionResult> ExpandAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken = default);
}

public interface IAgentAuthoringContextBuilder
{
    ValueTask<AgentAuthoringContext> BuildAsync(AgentAuthoringRequest request, MetadataContextPack metadataContextPack, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Add the JSON source generation context**

Create `Json/AgentMemoryJsonSerializerContext.cs`:

```csharp
using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Memory.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentMemoryPack))]
[JsonSerializable(typeof(AgentAuthoringContext))]
[JsonSerializable(typeof(AgentAuthoringRequest))]
[JsonSerializable(typeof(AgentCompressedContext))]
[JsonSerializable(typeof(AgentCompressedContextBlock))]
[JsonSerializable(typeof(AgentMemoryCandidate))]
[JsonSerializable(typeof(AgentMemoryItem))]
[JsonSerializable(typeof(AgentMemoryQuery))]
[JsonSerializable(typeof(AgentContextSourceRef))]
[JsonSerializable(typeof(AgentContextEvidenceRef))]
[JsonSerializable(typeof(AgentConversationRecord))]
[JsonSerializable(typeof(AgentTaskRecord))]
[JsonSerializable(typeof(AgentSourceExpansionResult))]
public sealed partial class AgentMemoryJsonSerializerContext : JsonSerializerContext;
```

- [ ] **Step 5: Run contract tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~ContractTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/ContractTests.cs
git commit -m "feat: add agent memory contracts"
```

---

### Task 3: In-Memory Stores With Snapshot Semantics

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentConversationStore.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentTaskHistoryStore.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentCompressedContextStore.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentMemoryStore.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/StoreTests.cs`

**Interfaces:**
- Consumes: `IAgentConversationStore`, `IAgentTaskHistoryStore`, `IAgentCompressedContextStore`, `IAgentMemoryStore`.
- Produces: tenant-isolated, deterministic in-memory stores.

- [ ] **Step 1: Write failing store tests**

Create `StoreTests.cs` with these tests:

```csharp
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class StoreTests
{
    [Fact]
    public async Task ConversationStore_PreservesTenantIsolation()
    {
        var store = new Stores.InMemoryAgentConversationStore();
        await store.SaveConversationAsync(new AgentConversationRecord { TenantId = "tenant-a", ConversationId = "c1" });

        var result = await store.GetConversationAsync("tenant-b", "c1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ConversationStore_ReturnsSnapshotCopies()
    {
        var store = new Stores.InMemoryAgentConversationStore();
        await store.SaveConversationAsync(new AgentConversationRecord
        {
            TenantId = "tenant-a",
            ConversationId = "c1",
            Turns = new[]
            {
                new AgentConversationTurn
                {
                    TenantId = "tenant-a",
                    TurnId = "t1",
                    Role = AgentConversationRole.User,
                    Content = "hello",
                    CreatedAt = DateTimeOffset.UnixEpoch
                }
            }
        });

        var first = await store.GetConversationAsync("tenant-a", "c1");
        var second = await store.GetConversationAsync("tenant-a", "c1");

        first.Should().NotBeSameAs(second);
        first!.Turns.Should().NotBeSameAs(second!.Turns);
    }

    [Fact]
    public async Task TaskHistoryStore_AppendsEventsWithinTenantOnly()
    {
        var store = new Stores.InMemoryAgentTaskHistoryStore();
        await store.SaveTaskAsync(new AgentTaskRecord { TenantId = "tenant-a", TaskId = "task-1", Title = "Build memory" });

        await store.AppendEventAsync("tenant-a", "task-1", new AgentTaskEvent
        {
            TenantId = "tenant-a",
            TaskId = "task-1",
            EventId = "event-1",
            EventKind = "note",
            Content = "accepted",
            CreatedAt = DateTimeOffset.UnixEpoch
        });

        var result = await store.GetTaskAsync("tenant-a", "task-1");

        result!.Events.Should().ContainSingle(e => e.EventId == "event-1");
        (await store.GetTaskAsync("tenant-b", "task-1")).Should().BeNull();
    }

    [Fact]
    public async Task MemoryStore_ListMemoriesAsync_DoesNotApplyRecallBudget()
    {
        var store = new Stores.InMemoryAgentMemoryStore();
        await store.SaveMemoryAsync(Memory("m1", "alpha"));
        await store.SaveMemoryAsync(Memory("m2", "beta"));

        var result = await store.ListMemoriesAsync(new AgentMemoryQuery
        {
            TenantId = "tenant-a",
            CharacterBudget = 1,
            MaxCount = 1
        });

        result.Should().HaveCount(2);
    }

    private static AgentMemoryItem Memory(string id, string content) => new()
    {
        TenantId = "tenant-a",
        MemoryId = id,
        Kind = AgentMemoryKind.ProjectFact,
        Content = content,
        CanonicalContentHash = id,
        PromotedAt = DateTimeOffset.UnixEpoch,
        Confidence = AgentMemoryConfidence.High
    };
}
```

- [ ] **Step 2: Run tests to verify stores are missing**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~StoreTests"`

Expected: FAIL with missing `Stores.InMemory...` types.

- [ ] **Step 3: Implement stores**

Use `ConcurrentDictionary<(string TenantId, string Id), T>` for each store. On save and read, copy records with `with` expressions and array copies:

```csharp
private static AgentConversationRecord Clone(AgentConversationRecord record) =>
    record with
    {
        Turns = record.Turns.Select(turn => turn with
        {
            DescriptorRefs = turn.DescriptorRefs.ToArray(),
            SourceRefs = turn.SourceRefs.ToArray()
        }).ToArray()
    };
```

`InMemoryAgentMemoryStore.ListMemoriesAsync` must apply only persistence filters:

```csharp
var items = _memories.Values
    .Where(memory => memory.TenantId == query.TenantId)
    .Where(memory => query.MemoryIds.Count == 0 || query.MemoryIds.Contains(memory.MemoryId, StringComparer.Ordinal))
    .Where(memory => query.Kinds.Count == 0 || query.Kinds.Contains(memory.Kind))
    .Where(memory => query.Tags.Count == 0 || query.Tags.All(tag => memory.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
    .Where(memory => query.DescriptorRefs.Count == 0 || query.DescriptorRefs.Any(reference => memory.DescriptorRefs.Contains(reference)))
    .Where(memory => query.IncludeArchived || memory.Status != AgentMemoryStatus.Archived)
    .Where(memory => query.IncludeSuperseded || memory.Status != AgentMemoryStatus.Superseded)
    .OrderBy(memory => memory.Kind)
    .ThenByDescending(memory => memory.PromotedAt)
    .ThenBy(memory => memory.MemoryId, StringComparer.Ordinal)
    .ThenBy(memory => memory.CanonicalContentHash, StringComparer.Ordinal)
    .Select(Clone)
    .ToArray();
```

Do not apply `MaxCount`, `CharacterBudget`, `IntentText`, or confidence thresholds in store code.

- [ ] **Step 4: Run store tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~StoreTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory/Stores tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/StoreTests.cs
git commit -m "feat: add in-memory agent memory stores"
```

---

### Task 4: Sanitization And Deterministic Compression

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Sanitization/DefaultAgentMemoryContentSanitizer.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Compression/DefaultAgentContextCompressor.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/SanitizerTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/CompressionTests.cs`

**Interfaces:**
- Consumes: `IAgentMemoryContentSanitizer`, `IAgentContextCompressor`, `IAgentConversationStore` contracts.
- Produces: sanitized content hashes and deterministic compressed blocks.

- [ ] **Step 1: Write failing sanitizer tests**

Create `SanitizerTests.cs`:

```csharp
using CrestCreates.Agent.Memory.Sanitization;
using CrestCreates.Agent.Memory.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class SanitizerTests
{
    [Fact]
    public void Sanitizer_RedactsSecretLikeContent_BeforeStorage()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var result = sanitizer.Sanitize("tenant-a", "apiKey=abc123 password=secret", Array.Empty<AgentContextSourceRef>());

        result.SanitizedContent.Should().NotContain("abc123");
        result.SanitizedContent.Should().NotContain("secret");
        result.SanitizedContent.Should().Contain("[REDACTED:");
        result.RedactionKinds.Should().Contain(new[] { "apiKey", "password" });
        result.Rejected.Should().BeFalse();
    }

    [Fact]
    public void Sanitizer_RejectedContent_IsMarkedAndHasDiagnostic()
    {
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var result = sanitizer.Sanitize("tenant-a", "-----BEGIN PRIVATE KEY----- value", Array.Empty<AgentContextSourceRef>());

        result.Rejected.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "AgentMemory.ContentRejected");
    }
}
```

- [ ] **Step 2: Write failing compression tests**

Create `CompressionTests.cs`:

```csharp
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Sanitization;
using CrestCreates.Agent.Memory.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class CompressionTests
{
    [Fact]
    public async Task Compression_IsDeterministic_ForSameSanitizedInput()
    {
        var compressor = new DefaultAgentContextCompressor(new DefaultAgentMemoryContentSanitizer());
        var conversation = Conversation("token=abc123 keep this architectural decision");

        var first = await compressor.CompressConversationAsync(conversation);
        var second = await compressor.CompressConversationAsync(conversation);

        first.ContextId.Should().Be(second.ContextId);
        first.Blocks.Select(b => b.CanonicalContentHash).Should().Equal(second.Blocks.Select(b => b.CanonicalContentHash));
        first.Blocks.Single().Content.Should().Contain("[REDACTED:");
    }

    [Fact]
    public async Task Compression_RejectedContent_IsNotCompressed()
    {
        var compressor = new DefaultAgentContextCompressor(new DefaultAgentMemoryContentSanitizer());

        var result = await compressor.CompressConversationAsync(Conversation("-----BEGIN PRIVATE KEY----- abc"));

        result.Blocks.Should().BeEmpty();
        result.Diagnostics.Should().Contain(d => d.Code == "AgentMemory.ContentRejected");
    }

    private static AgentConversationRecord Conversation(string content) => new()
    {
        TenantId = "tenant-a",
        ConversationId = "conversation-1",
        Turns = new[]
        {
            new AgentConversationTurn
            {
                TenantId = "tenant-a",
                TurnId = "turn-1",
                Role = AgentConversationRole.User,
                Content = content,
                CreatedAt = DateTimeOffset.UnixEpoch
            }
        }
    };
}
```

- [ ] **Step 3: Implement sanitizer**

Implement deterministic redaction with compiled-free `Regex` instances created statically:

```csharp
private static readonly Regex SecretPattern = new(
    "(apiKey|token|password)\\s*=\\s*[^\\s]+",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
```

Replacement format must be `[REDACTED:{kind}]`. Reject content containing `BEGIN PRIVATE KEY`, `BEGIN RSA PRIVATE KEY`, or `BEGIN OPENSSH PRIVATE KEY`. Hash with `SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSanitizedContent))` and lower-case hex.

- [ ] **Step 4: Implement compressor**

Compression must:

- Sanitize each turn or event before creating a block.
- Skip rejected sanitized content and carry diagnostics.
- Use block ids as `ctx:{tenantId}:{conversationId}:{blockIndex}:{hash[..12]}` for conversations and `taskctx:{tenantId}:{taskId}:{blockIndex}:{hash[..12]}` for tasks.
- Create source refs with `AgentSourceKind.ConversationTurn` or `AgentSourceKind.TaskEvent`.
- Use stable ordering from the input sequence only.

- [ ] **Step 5: Run sanitizer and compression tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~SanitizerTests|FullyQualifiedName~CompressionTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory/Sanitization src/Runtime/Agent/CrestCreates.Agent.Memory/Compression tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/SanitizerTests.cs tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/CompressionTests.cs
git commit -m "feat: add sanitized deterministic context compression"
```

---

### Task 5: Candidate Extraction And Promotion Semantics

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Extraction/DefaultAgentMemoryExtractor.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Promotion/DefaultAgentMemoryPromotionService.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/PromotionTests.cs`

**Interfaces:**
- Consumes: compressed context blocks, `IAgentMemoryStore`, `IAgentMemoryPromotionService`.
- Produces: candidates that do not auto-promote and promotion operations that require actor, reason, timestamp, and source/explanation.

- [ ] **Step 1: Write failing promotion tests**

Create `PromotionTests.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Extraction;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Stores;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class PromotionTests
{
    [Fact]
    public async Task Extractor_CreatesCandidates_WithoutAutoPromoting()
    {
        var extractor = new DefaultAgentMemoryExtractor();
        var context = new AgentCompressedContext
        {
            TenantId = "tenant-a",
            ContextId = "ctx-1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock
                {
                    TenantId = "tenant-a",
                    BlockId = "block-1",
                    Content = "decision: use deterministic compression",
                    CanonicalContentHash = "hash-1"
                }
            }
        };

        var candidates = await extractor.ExtractCandidatesAsync(context);

        candidates.Should().ContainSingle();
        candidates.Single().Status.Should().Be(AgentMemoryStatus.Candidate);
    }

    [Fact]
    public async Task Promotion_RequiresActorReasonAndSourceExplanation()
    {
        var store = new InMemoryAgentMemoryStore();
        var service = new DefaultAgentMemoryPromotionService(store);
        await store.SaveCandidateAsync(Candidate());

        var act = () => service.PromoteAsync("tenant-a", "candidate-1", new AgentMemoryOperationRequest
        {
            TenantId = "tenant-a",
            Actor = new AgentActorContext { ActorId = "agent", ActorKind = "assistant" },
            Reason = "",
            Timestamp = DateTimeOffset.UnixEpoch
        }).AsTask();

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Reason*");
    }

    [Fact]
    public async Task PromotionService_IsProductionPath_ForCandidatePromotion()
    {
        var store = new InMemoryAgentMemoryStore();
        var service = new DefaultAgentMemoryPromotionService(store);
        await store.SaveCandidateAsync(Candidate());

        var memory = await service.PromoteAsync("tenant-a", "candidate-1", Request());

        memory.Status.Should().Be(AgentMemoryStatus.Active);
        memory.IsAuthoritative.Should().BeFalse();
        (await store.GetMemoryAsync("tenant-a", memory.MemoryId)).Should().NotBeNull();
    }

    [Fact]
    public async Task MemoryStore_SaveMemoryAsync_IsPersistencePrimitiveOnly()
    {
        var store = new InMemoryAgentMemoryStore();
        await store.SaveMemoryAsync(new AgentMemoryItem
        {
            TenantId = "tenant-a",
            MemoryId = "manual",
            Kind = AgentMemoryKind.ProjectFact,
            Content = "seed",
            CanonicalContentHash = "seed",
            PromotedAt = DateTimeOffset.UnixEpoch
        });

        var memory = await store.GetMemoryAsync("tenant-a", "manual");

        memory.Should().NotBeNull();
    }

    private static AgentMemoryCandidate Candidate() => new()
    {
        TenantId = "tenant-a",
        CandidateId = "candidate-1",
        Kind = AgentMemoryKind.Decision,
        Content = "use deterministic compression",
        CanonicalContentHash = "hash-1",
        Confidence = AgentMemoryConfidence.High
    };

    private static AgentMemoryOperationRequest Request() => new()
    {
        TenantId = "tenant-a",
        Actor = new AgentActorContext { ActorId = "agent", ActorKind = "assistant" },
        Reason = "Accepted by maintainer",
        Timestamp = DateTimeOffset.UnixEpoch,
        Explanation = "Explicit promotion"
    };
}
```

- [ ] **Step 2: Run promotion tests to verify missing services**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~PromotionTests"`

Expected: FAIL with missing extraction and promotion services.

- [ ] **Step 3: Implement extractor**

Rules:

- Emit candidates only from non-empty compressed blocks.
- Classify content containing `decision:` as `AgentMemoryKind.Decision`; `constraint:` as `Constraint`; `preference:` as `Preference`; otherwise `ProjectFact`.
- Candidate id format: `candidate:{tenantId}:{blockId}:{canonicalHash[..12]}`.
- Preserve descriptor refs and source refs from blocks.
- Do not call `IAgentMemoryStore.SaveMemoryAsync`.

- [ ] **Step 4: Implement promotion service**

Rules:

- Validate `request.TenantId == tenantId`.
- Validate `request.Actor.ActorId`, `request.Actor.ActorKind`, `request.Reason`, `request.Timestamp`, and either `request.SourceRefs.Count > 0` or `request.Explanation` is non-empty.
- `PromoteAsync` loads candidate from store, creates `AgentMemoryItem` with `MemoryId = "memory:{tenantId}:{candidate.CanonicalContentHash[..12]}"`, `Status = Active`, `IsAuthoritative = false`, and calls `SaveMemoryAsync`.
- `RejectAsync` saves a candidate copy with `Status = Rejected`.
- `SupersedeAsync` promotes replacement, marks original `SupersededByMemoryId`, and saves both records.
- `ArchiveAsync` marks original `Status = Archived`.

- [ ] **Step 5: Run promotion tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~PromotionTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory/Extraction src/Runtime/Agent/CrestCreates.Agent.Memory/Promotion tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/PromotionTests.cs
git commit -m "feat: add agent memory promotion service"
```

---

### Task 6: Recall And Source Expansion

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Recall/DefaultAgentMemoryRetriever.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Sources/DefaultAgentContextSourceExpander.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/RecallTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/SourceExpansionTests.cs`

**Interfaces:**
- Consumes: `IAgentMemoryStore`, `IAgentConversationStore`, `IAgentTaskHistoryStore`, `IAgentCompressedContextStore`.
- Produces: deterministic `AgentMemoryPack` and sanitized source expansion for Memory-owned sources only.

- [ ] **Step 1: Write failing recall tests**

Create `RecallTests.cs` with tests proving tenant filtering, budget in retriever, visibility boundary, and no descriptor resolver:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Recall;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class RecallTests
{
    [Fact]
    public async Task Recall_FiltersByTenantAndDescriptorVisibility()
    {
        var store = new InMemoryAgentMemoryStore();
        var visible = new DescriptorRef("demo", "visible");
        var denied = new DescriptorRef("demo", "denied");
        await store.SaveMemoryAsync(Memory("m1", "tenant-a", visible, "visible memory"));
        await store.SaveMemoryAsync(Memory("m2", "tenant-a", denied, "denied memory"));
        await store.SaveMemoryAsync(Memory("m3", "tenant-b", visible, "other tenant"));

        var retriever = new DefaultAgentMemoryRetriever(store);
        var pack = await retriever.RecallAsync(new AgentMemoryQuery
        {
            TenantId = "tenant-a",
            VisibleDescriptorRefs = new[] { visible },
            IntentText = "visible"
        });

        pack.Memories.Should().ContainSingle(m => m.MemoryId == "m1");
        pack.Diagnostics.Should().NotContain(d => d.Message.Contains("denied", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Recall_RespectsMaxCountAndCharacterBudget()
    {
        var store = new InMemoryAgentMemoryStore();
        var descriptor = new DescriptorRef("demo", "visible");
        await store.SaveMemoryAsync(Memory("m1", "tenant-a", descriptor, "alpha"));
        await store.SaveMemoryAsync(Memory("m2", "tenant-a", descriptor, "beta beta beta"));

        var retriever = new DefaultAgentMemoryRetriever(store);
        var pack = await retriever.RecallAsync(new AgentMemoryQuery
        {
            TenantId = "tenant-a",
            VisibleDescriptorRefs = new[] { descriptor },
            MaxCount = 1,
            CharacterBudget = 8
        });

        pack.Memories.Should().HaveCount(1);
        pack.Memories.Sum(memory => memory.Content.Length).Should().BeLessThanOrEqualTo(8);
    }

    private static AgentMemoryItem Memory(string id, string tenantId, DescriptorRef descriptor, string content) => new()
    {
        TenantId = tenantId,
        MemoryId = id,
        Kind = AgentMemoryKind.ProjectFact,
        Content = content,
        CanonicalContentHash = id,
        PromotedAt = DateTimeOffset.UnixEpoch,
        Confidence = AgentMemoryConfidence.High,
        DescriptorRefs = new[] { descriptor }
    };
}
```

- [ ] **Step 2: Write failing source expansion tests**

Create `SourceExpansionTests.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Sources;
using CrestCreates.Agent.Memory.Stores;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class SourceExpansionTests
{
    [Fact]
    public async Task SourceExpansion_ReturnsSanitizedStoredContent()
    {
        var conversations = new InMemoryAgentConversationStore();
        await conversations.SaveConversationAsync(new AgentConversationRecord
        {
            TenantId = "tenant-a",
            ConversationId = "c1",
            Turns = new[]
            {
                new AgentConversationTurn
                {
                    TenantId = "tenant-a",
                    TurnId = "t1",
                    Role = AgentConversationRole.User,
                    Content = "already sanitized",
                    CreatedAt = DateTimeOffset.UnixEpoch
                }
            }
        });

        var expander = new DefaultAgentContextSourceExpander(conversations, new InMemoryAgentTaskHistoryStore(), new InMemoryAgentCompressedContextStore());
        var result = await expander.ExpandAsync(new AgentContextSourceRef
        {
            TenantId = "tenant-a",
            SourceKind = AgentSourceKind.ConversationTurn,
            SourceId = "c1:t1"
        });

        result.Status.Should().Be(AgentMemorySourceExpansionStatus.Expanded);
        result.SanitizedContent.Should().Be("already sanitized");
    }

    [Fact]
    public async Task SourceExpansion_ExternalSource_ReturnsNotExpandable()
    {
        var expander = new DefaultAgentContextSourceExpander(new InMemoryAgentConversationStore(), new InMemoryAgentTaskHistoryStore(), new InMemoryAgentCompressedContextStore());

        var result = await expander.ExpandAsync(new AgentContextSourceRef
        {
            TenantId = "tenant-a",
            SourceKind = AgentSourceKind.FixProposal,
            SourceId = "fix-1"
        });

        result.Status.Should().Be(AgentMemorySourceExpansionStatus.ExternalSourceNotSupported);
    }
}
```

- [ ] **Step 3: Implement retriever**

Retriever rules:

- Call `IAgentMemoryStore.ListMemoriesAsync(query)` for persistence-safe filtering.
- Apply confidence, visibility boundary, intent token scoring, `MaxCount`, and `CharacterBudget` inside retriever only.
- Exclude archived and superseded by default through query defaults and store filters.
- Never call ControlPlane, descriptor stores, activation stores, or registries.
- Output `AgentMemoryPack.IsAuthoritative = false`.
- Stable order: score descending, kind, promotedAt descending, memoryId, canonical hash.

- [ ] **Step 4: Implement source expander**

Expansion rules:

- Expand `ConversationTurn` from `IAgentConversationStore` using `SourceId = "{conversationId}:{turnId}"`.
- Expand `TaskRecord` and `TaskEvent` from `IAgentTaskHistoryStore`.
- Expand `CompressedContextBlock` from `IAgentCompressedContextStore` using `SourceId = "{contextId}:{blockId}"`.
- Return `ExternalSourceNotSupported` for `MetadataContextPack`, `ReviewReport`, `FixProposal`, `PackagePreview`, and `ActivationRequest`.
- Return `NotExpandable` for `MemoryCandidate` and `MemoryItem` in phase 1.

- [ ] **Step 5: Run recall and source expansion tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~RecallTests|FullyQualifiedName~SourceExpansionTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory/Recall src/Runtime/Agent/CrestCreates.Agent.Memory/Sources tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/RecallTests.cs tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/SourceExpansionTests.cs
git commit -m "feat: add agent memory recall and source expansion"
```

---

### Task 7: Authoring Context Builder And Main Chain

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Authoring/DefaultAgentAuthoringContextBuilder.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/AgentMemoryTestData.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/AuthoringContextBuilderTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/MainChainTests.cs`

**Interfaces:**
- Consumes: `IAgentMemoryRetriever`, `MetadataContextPack`, `AgentAuthoringRequest`.
- Produces: `AgentAuthoringContext` that preserves metadata authority and includes non-authoritative memory.

- [ ] **Step 1: Write failing authoring builder tests**

Create `AuthoringContextBuilderTests.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Authoring;
using CrestCreates.Agent.Memory.Recall;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class AuthoringContextBuilderTests
{
    [Fact]
    public async Task AuthoringContextBuilder_ComposesMetadataAndMemoryPacks_WithoutMutation()
    {
        var store = new InMemoryAgentMemoryStore();
        await store.SaveMemoryAsync(Memory("m1", "old note"));
        var builder = new DefaultAgentAuthoringContextBuilder(new DefaultAgentMemoryRetriever(store));
        var metadata = AgentMemoryTestData.MetadataPack();

        var context = await builder.BuildAsync(new AgentAuthoringRequest
        {
            TenantId = "tenant-a",
            IntentText = "old",
            MemoryQuery = new AgentMemoryQuery { TenantId = "tenant-a" }
        }, metadata);

        context.MetadataContextPack.Should().BeSameAs(metadata);
        context.MemoryPack.IsAuthoritative.Should().BeFalse();
        context.MemoryPack.Memories.Should().ContainSingle();
    }

    [Fact]
    public async Task AuthoringContextBuilder_MarksMemoryAsNonAuthoritative_WhenMetadataContextConflicts()
    {
        var store = new InMemoryAgentMemoryStore();
        await store.SaveMemoryAsync(Memory("m1", "descriptor current state is Draft"));
        var builder = new DefaultAgentAuthoringContextBuilder(new DefaultAgentMemoryRetriever(store));

        var context = await builder.BuildAsync(new AgentAuthoringRequest
        {
            TenantId = "tenant-a",
            IntentText = "descriptor state",
            MemoryQuery = new AgentMemoryQuery { TenantId = "tenant-a" }
        }, AgentMemoryTestData.MetadataPack());

        context.MemoryPack.IsAuthoritative.Should().BeFalse();
        context.MemoryPack.Memories.Should().OnlyContain(memory => memory.IsAuthoritative == false);
        context.Diagnostics.Should().Contain(d => d.Code == "AgentMemory.NonAuthoritative");
    }

    private static AgentMemoryItem Memory(string id, string content) => new()
    {
        TenantId = "tenant-a",
        MemoryId = id,
        Kind = AgentMemoryKind.ProjectFact,
        Content = content,
        CanonicalContentHash = id,
        PromotedAt = DateTimeOffset.UnixEpoch,
        DescriptorRefs = new[] { new DescriptorRef("demo", "capability") }
    };
}
```

- [ ] **Step 2: Write failing main-chain test**

Create `MainChainTests.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Authoring;
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Extraction;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Recall;
using CrestCreates.Agent.Memory.Sanitization;
using CrestCreates.Agent.Memory.Stores;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class MainChainTests
{
    [Fact]
    public async Task AgentMemory_MainChain_BuildsSourceTraceableAuthoringContext()
    {
        var conversationStore = new InMemoryAgentConversationStore();
        var memoryStore = new InMemoryAgentMemoryStore();
        var sanitizer = new DefaultAgentMemoryContentSanitizer();
        var compressor = new DefaultAgentContextCompressor(sanitizer);
        var extractor = new DefaultAgentMemoryExtractor();
        var promotion = new DefaultAgentMemoryPromotionService(memoryStore);
        var retriever = new DefaultAgentMemoryRetriever(memoryStore);
        var builder = new DefaultAgentAuthoringContextBuilder(retriever);

        var conversation = new AgentConversationRecord
        {
            TenantId = "tenant-a",
            ConversationId = "c1",
            Turns = new[]
            {
                new AgentConversationTurn
                {
                    TenantId = "tenant-a",
                    TurnId = "t1",
                    Role = AgentConversationRole.User,
                    Content = "decision: keep memory deterministic token=abc123",
                    CreatedAt = DateTimeOffset.UnixEpoch
                }
            }
        };

        await conversationStore.SaveConversationAsync(conversation);
        var compressed = await compressor.CompressConversationAsync(conversation);
        var candidate = (await extractor.ExtractCandidatesAsync(compressed)).Single();
        await memoryStore.SaveCandidateAsync(candidate);
        await promotion.PromoteAsync("tenant-a", candidate.CandidateId, new AgentMemoryOperationRequest
        {
            TenantId = "tenant-a",
            Actor = new AgentActorContext { ActorId = "agent", ActorKind = "assistant" },
            Reason = "Main-chain explicit promotion",
            Timestamp = DateTimeOffset.UnixEpoch,
            Explanation = "Maintainer accepted the candidate"
        });

        var context = await builder.BuildAsync(new AgentAuthoringRequest
        {
            TenantId = "tenant-a",
            IntentText = "deterministic memory",
            MemoryQuery = new AgentMemoryQuery { TenantId = "tenant-a", IntentText = "deterministic memory" }
        }, AgentMemoryTestData.MetadataPack());

        context.MemoryPack.Memories.Should().ContainSingle();
        context.MemoryPack.Memories.Single().Content.Should().Contain("[REDACTED:");
        context.MemoryPack.Memories.Single().SourceRefs.Should().NotBeEmpty();
        context.MemoryPack.IsAuthoritative.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Add metadata test data helper**

Create `AgentMemoryTestData.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Agent.Memory.Tests;

internal static class AgentMemoryTestData
{
    public static MetadataContextPack MetadataPack() => new()
    {
        Request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            TenantId = "tenant-a",
            FocusDescriptors = new[] { new DescriptorRef("demo", "capability") },
            Intent = "authoring context"
        },
        Descriptors = new[]
        {
            new MetadataContextPackDescriptorEntry
            {
                Ref = new DescriptorRef("demo", "capability"),
                Kind = DescriptorKind.Capability,
                Name = "demo.capability",
                State = DescriptorState.Active,
                IsFocus = true
            }
        },
        Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
        Summary = new MetadataContextPackSummary
        {
            TotalDescriptorCount = 1,
            DescriptorCountsByKind = new Dictionary<DescriptorKind, int>
            {
                [DescriptorKind.Capability] = 1
            },
            TotalRelationshipCount = 0,
            RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
            FocusRefs = new[] { new DescriptorRef("demo", "capability") },
            WasTruncated = false,
            TruncatedAtCount = null,
            TraversalDepthReached = 0
        },
        Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
    };
}
```

- [ ] **Step 4: Run tests to verify builder is missing**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~AuthoringContextBuilderTests|FullyQualifiedName~MainChainTests"`

Expected: FAIL with missing `DefaultAgentAuthoringContextBuilder`.

- [ ] **Step 5: Implement authoring builder**

Rules:

- Use `request.MemoryQuery ?? new AgentMemoryQuery { TenantId = request.TenantId, IntentText = request.IntentText }`.
- Force query tenant to match request tenant by creating a copy with `TenantId = request.TenantId`.
- Call `IAgentMemoryRetriever.RecallAsync`.
- Return `AgentAuthoringContext` with the original `MetadataContextPack` instance.
- Add diagnostic `AgentMemory.NonAuthoritative` whenever recalled memories exist.
- Never mutate `MetadataContextPack`.

- [ ] **Step 6: Run authoring and main-chain tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~AuthoringContextBuilderTests|FullyQualifiedName~MainChainTests"`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory/Authoring tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/AgentMemoryTestData.cs tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/AuthoringContextBuilderTests.cs tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/MainChainTests.cs
git commit -m "feat: compose agent authoring context with memory"
```

---

### Task 8: DI Registration, Full Verification, And Status Record

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory/AgentMemoryServiceCollectionExtensions.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/ServiceRegistrationTests.cs`
- Modify: `memory.md`

**Interfaces:**
- Consumes: all services from earlier tasks.
- Produces: one public runtime registration method and final documented status.

- [ ] **Step 1: Write failing registration test**

Create `ServiceRegistrationTests.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddAgentMemoryRuntime_RegistersDefaultServices()
    {
        var provider = new ServiceCollection()
            .AddAgentMemoryRuntime()
            .BuildServiceProvider();

        provider.GetRequiredService<IAgentConversationStore>().Should().NotBeNull();
        provider.GetRequiredService<IAgentTaskHistoryStore>().Should().NotBeNull();
        provider.GetRequiredService<IAgentCompressedContextStore>().Should().NotBeNull();
        provider.GetRequiredService<IAgentMemoryStore>().Should().NotBeNull();
        provider.GetRequiredService<IAgentMemoryContentSanitizer>().Should().NotBeNull();
        provider.GetRequiredService<IAgentContextCompressor>().Should().NotBeNull();
        provider.GetRequiredService<IAgentMemoryExtractor>().Should().NotBeNull();
        provider.GetRequiredService<IAgentMemoryPromotionService>().Should().NotBeNull();
        provider.GetRequiredService<IAgentMemoryRetriever>().Should().NotBeNull();
        provider.GetRequiredService<IAgentContextSourceExpander>().Should().NotBeNull();
        provider.GetRequiredService<IAgentAuthoringContextBuilder>().Should().NotBeNull();
        provider.GetRequiredService<TimeProvider>().Should().Be(TimeProvider.System);
    }
}
```

- [ ] **Step 2: Implement DI registration**

`AgentMemoryServiceCollectionExtensions.AddAgentMemoryRuntime` must register:

```csharp
services.TryAddSingleton<IAgentConversationStore, InMemoryAgentConversationStore>();
services.TryAddSingleton<IAgentTaskHistoryStore, InMemoryAgentTaskHistoryStore>();
services.TryAddSingleton<IAgentCompressedContextStore, InMemoryAgentCompressedContextStore>();
services.TryAddSingleton<IAgentMemoryStore, InMemoryAgentMemoryStore>();
services.TryAddSingleton<IAgentMemoryContentSanitizer, DefaultAgentMemoryContentSanitizer>();
services.TryAddSingleton<IAgentContextCompressor, DefaultAgentContextCompressor>();
services.TryAddSingleton<IAgentMemoryExtractor, DefaultAgentMemoryExtractor>();
services.TryAddSingleton<IAgentMemoryPromotionService, DefaultAgentMemoryPromotionService>();
services.TryAddSingleton<IAgentMemoryRetriever, DefaultAgentMemoryRetriever>();
services.TryAddSingleton<IAgentContextSourceExpander, DefaultAgentContextSourceExpander>();
services.TryAddSingleton<IAgentAuthoringContextBuilder, DefaultAgentAuthoringContextBuilder>();
services.TryAddSingleton(TimeProvider.System);
```

Use `Microsoft.Extensions.DependencyInjection.Extensions`.

- [ ] **Step 3: Run full Agent Memory tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests`

Expected: PASS.

- [ ] **Step 4: Run boundary tests**

Run: `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests`

Expected: PASS.

- [ ] **Step 5: Run related Agent Control Plane tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests`

Expected: PASS.

- [ ] **Step 6: Run solution build**

Run: `dotnet build CrestCreates.slnx`

Expected: PASS.

- [ ] **Step 7: Update `memory.md`**

Add a dated entry:

```markdown
## 2026-06-29 - Agent Memory #43 first runtime closure

- Added Agent Memory abstractions and deterministic in-memory runtime services.
- Main chain now covers sanitized conversation/task input, deterministic compression, candidate extraction, explicit promotion, budgeted recall, memory-owned source expansion, and AgentAuthoringContext composition.
- Memory remains non-authoritative context infrastructure and does not depend on Agent Control Plane, LLM adapters, persistence providers, Dynamic API, Web, or Platform.
```

- [ ] **Step 8: Final verification**

Run: `git status --short`

Expected: only intended files are modified before commit.

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests`

Expected: PASS.

Run: `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests`

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests memory.md
git commit -m "feat: register agent memory runtime services"
```

---

## Self-Review Notes

- Spec coverage: Tasks 1-2 cover project boundaries, contracts, `AgentContextEvidenceRef`, closed confidence enum, and AoT JSON context. Tasks 3-6 cover stores, sanitization, deterministic compression, extraction, promotion, recall visibility, and source expansion. Task 7 covers non-authoritative authoring context composition and metadata conflict behavior. Task 8 covers DI, verification, and `memory.md`.
- Query split warning covered: Task 3 proves store does not apply `MaxCount` or `CharacterBudget`; Task 6 applies recall scoring and budget in retriever.
- Promotion boundary covered: Task 5 makes `IAgentMemoryPromotionService` the production candidate promotion path while allowing `SaveMemoryAsync` as a store primitive.
- Dependency boundary covered: Task 1 adds both direct project reference tests and assembly reference tests.
- Metadata test data uses the current `MetadataContextPackRequest` and `MetadataContextPackSummary` required members from `CrestCreates.Metadata.ContextPack.Abstractions`.
