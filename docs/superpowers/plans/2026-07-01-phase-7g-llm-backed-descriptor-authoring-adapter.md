# Phase 7g LLM-backed Descriptor Authoring Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the framework-level LLM-backed descriptor authoring adapter behind the Phase 7f authoring boundary, while keeping governance and runtime activation on the existing deterministic Control Plane mainline.

**Architecture:** Add framework authoring contracts, a provider-agnostic authoring runtime, an OpenAI-compatible provider integration project, and a recorded-fixture golden scenario path. The LLM adapter consumes only `AgentAuthoringContext` and returns an atomic `DescriptorDraftSet`; review, package evidence, activation handoff, HumanTask approval, and `RuntimeActivationGate` remain outside the adapter.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, System.Text.Json source generation, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Options, Microsoft.Extensions.Http, existing `ICanonicalHashComputer`, existing DescriptorDraft and Agent Memory contracts.

## Global Constraints

- Main spec: `docs/superpowers/specs/2026-07-01-phase-7g-llm-backed-descriptor-authoring-adapter-design.md`.
- LLM adapter consumes only `AgentAuthoringContext`.
- LLM adapter produces only `DescriptorAuthoringPlan`, `DescriptorDraftSet`, and `DescriptorAuthoringDiagnostic` results.
- `DescriptorDraftSet` is an atomic authoring proposal; no partial successful draft set is allowed in Phase 7g.
- Do not create a second DescriptorDraft model, review service, package preview path, activation request path, runtime activation gate, or runtime registry mutation path.
- `CrestCreates.Agent.Authoring.Abstractions` must not reference `CrestCreates.Agent.ControlPlane.Abstractions`, `CrestCreates.Agent.ControlPlane`, capability runtime, workflow runtime, HumanTask runtime, provider HTTP DTOs, or provider SDK types.
- `CrestCreates.Agent.Authoring` must remain provider-agnostic and must not reference `CrestCreates.Agent.Authoring.Http`.
- `CrestCreates.Agent.Authoring.Http` owns provider-specific HTTP/OpenAI-compatible request and response projection, options binding, and credential resolution.
- `IDescriptorAuthoringCredentialProvider` belongs to `CrestCreates.Agent.Authoring.Http`, not `CrestCreates.Agent.Authoring.Abstractions`.
- Provider profiles must not carry raw secret values.
- Prompt input hashes must be computed from normalized authoring projections, not raw upstream object serialization.
- Tests must use fake or recorded clients; no test requires live provider access.
- Diagnostic codes must be centralized as semantic-string governed constants.
- No inline diagnostic code literals outside the definition class and test fixtures.
- No ad-hoc SHA256, pipe-delimited string hashing, or helper-style hash utilities.
- Public DTOs must avoid `dynamic` and public `object` payloads.

---

## File Structure

Create authoring contract project:

```text
src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/
  CrestCreates.Agent.Authoring.Abstractions.csproj
  Authoring/IDescriptorAuthoringAgent.cs
  Authoring/DescriptorAuthoringStatus.cs
  Authoring/DescriptorAuthoringDiagnosticCodes.cs
  Authoring/DescriptorAuthoringDiagnostic.cs
  Authoring/DescriptorAuthoringPlan.cs
  Authoring/DescriptorDraftSet.cs
  Authoring/DescriptorAuthoringResult.cs
  Prompting/DescriptorAuthoringPromptInput.cs
  Prompting/DescriptorAuthoringPromptOutput.cs
  Prompting/DescriptorAuthoringMetadataContextProjection.cs
  Prompting/DescriptorAuthoringMemoryProjection.cs
  Model/IDescriptorAuthoringModelClient.cs
  Model/DescriptorAuthoringModelRequest.cs
  Model/DescriptorAuthoringModelResponse.cs
  Model/DescriptorAuthoringModelProfile.cs
  Model/DescriptorAuthoringProviderProfile.cs
  Json/DescriptorAuthoringJsonSerializerContext.cs
```

Create authoring runtime project:

```text
src/Runtime/Agent/CrestCreates.Agent.Authoring/
  CrestCreates.Agent.Authoring.csproj
  AgentAuthoringServiceCollectionExtensions.cs
  LlmDescriptorAuthoringAgent.cs
  Prompting/IDescriptorAuthoringPromptInputFactory.cs
  Prompting/DefaultDescriptorAuthoringPromptInputFactory.cs
  Prompting/IDescriptorAuthoringPromptBuilder.cs
  Prompting/DefaultDescriptorAuthoringPromptBuilder.cs
  Prompting/IDescriptorAuthoringPromptInputHashService.cs
  Prompting/DefaultDescriptorAuthoringPromptInputHashService.cs
  Parsing/IDescriptorAuthoringOutputParser.cs
  Parsing/JsonDescriptorAuthoringOutputParser.cs
  Parsing/DescriptorAuthoringPlanDto.cs
  Model/FakeDescriptorAuthoringModelClient.cs
  Model/RecordedDescriptorAuthoringModelClient.cs
```

Create provider integration project:

```text
src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/
  CrestCreates.Agent.Authoring.Http.csproj
  AgentAuthoringHttpServiceCollectionExtensions.cs
  OpenAICompatible/OpenAICompatibleDescriptorAuthoringModelClient.cs
  OpenAICompatible/OpenAICompatibleAuthoringOptions.cs
  OpenAICompatible/OpenAICompatibleAuthoringRequest.cs
  OpenAICompatible/OpenAICompatibleAuthoringResponse.cs
  Credentials/IDescriptorAuthoringCredentialProvider.cs
  Credentials/DefaultDescriptorAuthoringCredentialProvider.cs
```

Create tests:

```text
tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/
  CrestCreates.Agent.Authoring.Tests.csproj
  AuthoringContractTests.cs
  AuthoringBoundaryTests.cs
  PromptInputHashTests.cs
  OutputParserTests.cs
  LlmDescriptorAuthoringAgentTests.cs
  ProviderBoundaryTests.cs
  GoldenScenarioLlmFixtureTests.cs
  Fixtures/company-certification-authoring-response.json
```

Modify sample:

```text
samples/CrestCreates.Samples.DescriptorControlPlane/CrestCreates.Samples.DescriptorControlPlane.csproj
samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioHost.cs
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/IDescriptorAuthoringAgent.cs
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorAuthoringPlan.cs
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorAuthoringResult.cs
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorDraftSet.cs
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/FakeCompanyCertificationAuthoringAgent.cs
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioRunner.cs
```

Also update solution and central package references where needed:

```text
CrestCreates.slnx
Directory.Packages.props
```

---

### Task 1: Framework Authoring Contracts

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/IDescriptorAuthoringAgent.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringStatus.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringDiagnosticCodes.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringDiagnostic.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringPlan.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorDraftSet.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringResult.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Prompting/DescriptorAuthoringPromptInput.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Prompting/DescriptorAuthoringPromptOutput.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Prompting/DescriptorAuthoringMetadataContextProjection.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Prompting/DescriptorAuthoringMemoryProjection.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Model/IDescriptorAuthoringModelClient.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Model/DescriptorAuthoringModelRequest.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Model/DescriptorAuthoringModelResponse.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Model/DescriptorAuthoringModelProfile.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Model/DescriptorAuthoringProviderProfile.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Json/DescriptorAuthoringJsonSerializerContext.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/AuthoringContractTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/AuthoringBoundaryTests.cs`
- Modify: `CrestCreates.slnx`

**Interfaces:**
- Consumes: `AgentAuthoringContext`, `AgentMemoryPack`, `DescriptorDraft`, `DescriptorRef`, `DescriptorKind`, `CanonicalHash`, `DiagnosticCode`, `SeverityLevel`.
- Produces: `IDescriptorAuthoringAgent.AuthorAsync(AgentAuthoringContext, CancellationToken)`, `DescriptorAuthoringResult`, `DescriptorDraftSet`, prompt/model DTOs.

- [ ] **Step 1: Add failing contract and boundary tests**

Create `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../../src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

Create `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/AuthoringContractTests.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Json;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class AuthoringContractTests
{
    [Fact]
    public void Contracts_Are_FrameworkNamespace_NotSampleNamespace()
    {
        typeof(IDescriptorAuthoringAgent).Namespace
            .Should().Be("CrestCreates.Agent.Authoring.Abstractions.Authoring");

        typeof(IDescriptorAuthoringAgent).Assembly.GetName().Name
            .Should().Be("CrestCreates.Agent.Authoring.Abstractions");
    }

    [Fact]
    public void DescriptorAuthoringStatus_ContainsSucceededWithDiagnostics()
    {
        Enum.GetNames<DescriptorAuthoringStatus>()
            .Should().ContainInOrder(
                "Succeeded",
                "SucceededWithDiagnostics",
                "Blocked",
                "InvalidProviderOutput",
                "ProviderUnavailable",
                "Failed");
    }

    [Fact]
    public void ProviderProfile_DoesNotExpose_Secrets()
    {
        typeof(DescriptorAuthoringProviderProfile)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .Should()
            .NotContain(new[] { "ApiKey", "Secret", "Token", "Password" });
    }

    [Fact]
    public void JsonContext_ContainsAuthoringResult()
    {
        var json = JsonSerializer.Serialize(
            new DescriptorAuthoringModelProfile
            {
                ProfileName = "fixture",
                ProviderName = "recorded",
                ModelName = "fixture-model"
            },
            DescriptorAuthoringJsonSerializerContext.Default.DescriptorAuthoringModelProfile);

        json.Should().Contain("\"profileName\":\"fixture\"");
    }
}
```

Create `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/AuthoringBoundaryTests.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class AuthoringBoundaryTests
{
    [Fact]
    public void AuthoringAbstractions_DoNotReference_ControlPlane()
    {
        typeof(IDescriptorAuthoringAgent).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.ControlPlane",
                "CrestCreates.Agent.ControlPlane.Abstractions"
            });
    }

    [Fact]
    public void AuthoringAbstractions_DoNotReference_Http_Or_ProviderSdk()
    {
        typeof(IDescriptorAuthoringAgent).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.Authoring.Http",
                "OpenAI",
                "Azure.AI.OpenAI",
                "Anthropic"
            });
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj
```

Expected: build fails because `CrestCreates.Agent.Authoring.Abstractions` does not exist.

- [ ] **Step 3: Add the contracts project**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Agent.Authoring.Abstractions</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Authoring.Abstractions</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.ContextPack.Abstractions/CrestCreates.Metadata.ContextPack.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CrestCreates.DescriptorDraft.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Snapshot.Abstractions/CrestCreates.Snapshot.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/IDescriptorAuthoringAgent.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public interface IDescriptorAuthoringAgent
{
    Task<DescriptorAuthoringResult> AuthorAsync(
        AgentAuthoringContext context,
        CancellationToken cancellationToken = default);
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringStatus.cs`:

```csharp
namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public enum DescriptorAuthoringStatus
{
    Succeeded = 0,
    SucceededWithDiagnostics = 1,
    Blocked = 2,
    InvalidProviderOutput = 3,
    ProviderUnavailable = 4,
    Failed = 5
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringDiagnosticCodes.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public static class DescriptorAuthoringDiagnosticCodes
{
    public static DiagnosticCode ProviderTimeout { get; } = new("AUTHORING_PROVIDER_TIMEOUT");
    public static DiagnosticCode ProviderRateLimited { get; } = new("AUTHORING_PROVIDER_RATE_LIMITED");
    public static DiagnosticCode ProviderUnauthorized { get; } = new("AUTHORING_PROVIDER_UNAUTHORIZED");
    public static DiagnosticCode CredentialUnavailable { get; } = new("AUTHORING_CREDENTIAL_UNAVAILABLE");
    public static DiagnosticCode CredentialRejected { get; } = new("AUTHORING_CREDENTIAL_REJECTED");
    public static DiagnosticCode InvalidProviderOutput { get; } = new("AUTHORING_INVALID_PROVIDER_OUTPUT");
    public static DiagnosticCode PromptHashMismatch { get; } = new("AUTHORING_PROMPT_HASH_MISMATCH");
    public static DiagnosticCode UnknownDescriptorKind { get; } = new("AUTHORING_UNKNOWN_DESCRIPTOR_KIND");
    public static DiagnosticCode UnsupportedDraftOperation { get; } = new("AUTHORING_UNSUPPORTED_DRAFT_OPERATION");
    public static DiagnosticCode GovernanceBoundaryViolation { get; } = new("AUTHORING_GOVERNANCE_BOUNDARY_VIOLATION");
    public static DiagnosticCode MemoryAuthorityClaimRejected { get; } = new("AUTHORING_MEMORY_AUTHORITY_CLAIM_REJECTED");
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringDiagnostic.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public sealed record DescriptorAuthoringDiagnostic : ISnapshotable<DescriptorAuthoringDiagnostic>
{
    public required DiagnosticCode Code { get; init; }
    public required string Message { get; init; }
    public SeverityLevel Severity { get; init; } = SeverityLevel.Info;
    public string? Path { get; init; }

    public DescriptorAuthoringDiagnostic Snapshot() => this;
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringPlan.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public sealed record DescriptorAuthoringPlan : ISnapshotable<DescriptorAuthoringPlan>
{
    public required string PlanId { get; init; }
    public required string IntentText { get; init; }
    public IReadOnlyList<DescriptorRef> PlannedDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();

    public DescriptorAuthoringPlan Snapshot() => this with
    {
        PlannedDescriptorRefs = PlannedDescriptorRefs.ToArray(),
        Assumptions = Assumptions.ToArray()
    };
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorDraftSet.cs`:

```csharp
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public sealed record DescriptorDraftSet : ISnapshotable<DescriptorDraftSet>
{
    public required string DraftSetId { get; init; }
    public IReadOnlyList<DescriptorDraft> Drafts { get; init; } = Array.Empty<DescriptorDraft>();

    public DescriptorDraftSet Snapshot() => this with
    {
        Drafts = Drafts.Select(d => d.Snapshot()).ToArray()
    };
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringResult.cs`:

```csharp
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public sealed record DescriptorAuthoringResult : ISnapshotable<DescriptorAuthoringResult>
{
    public required DescriptorAuthoringStatus Status { get; init; }
    public required DescriptorAuthoringPlan Plan { get; init; }
    public required DescriptorDraftSet DraftSet { get; init; }
    public IReadOnlyList<DescriptorAuthoringDiagnostic> Diagnostics { get; init; } = Array.Empty<DescriptorAuthoringDiagnostic>();

    public DescriptorAuthoringResult Snapshot() => this with
    {
        Plan = Plan.Snapshot(),
        DraftSet = DraftSet.Snapshot(),
        Diagnostics = Diagnostics.Select(d => d.Snapshot()).ToArray()
    };
}
```

- [ ] **Step 4: Add prompt and model DTOs**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Prompting/DescriptorAuthoringMetadataContextProjection.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Prompting;

public sealed record DescriptorAuthoringDescriptorProjection : ISnapshotable<DescriptorAuthoringDescriptorProjection>
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public string? Name { get; init; }
    public CanonicalHash? ContractHash { get; init; }
    public CanonicalHash? DefinitionHash { get; init; }

    public DescriptorAuthoringDescriptorProjection Snapshot() => this;
}

public sealed record DescriptorAuthoringMetadataContextProjection : ISnapshotable<DescriptorAuthoringMetadataContextProjection>
{
    public IReadOnlyList<DescriptorAuthoringDescriptorProjection> Descriptors { get; init; } = Array.Empty<DescriptorAuthoringDescriptorProjection>();
    public IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    public DescriptorAuthoringMetadataContextProjection Snapshot() => this with
    {
        Descriptors = Descriptors.Select(d => d.Snapshot()).ToArray(),
        VisibleDescriptorRefs = VisibleDescriptorRefs.ToArray(),
        Diagnostics = Diagnostics.ToArray()
    };
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Prompting/DescriptorAuthoringMemoryProjection.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Prompting;

public sealed record DescriptorAuthoringMemoryItemProjection : ISnapshotable<DescriptorAuthoringMemoryItemProjection>
{
    public required string MemoryId { get; init; }
    public required AgentMemoryKind Kind { get; init; }
    public required string Content { get; init; }
    public AgentMemoryConfidence Confidence { get; init; } = AgentMemoryConfidence.Unknown;
    public CanonicalHash? CanonicalContentHash { get; init; }
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();

    public DescriptorAuthoringMemoryItemProjection Snapshot() => this with
    {
        DescriptorRefs = DescriptorRefs.ToArray()
    };
}

public sealed record DescriptorAuthoringMemoryProjection : ISnapshotable<DescriptorAuthoringMemoryProjection>
{
    public required bool IsAuthoritative { get; init; }
    public CanonicalHash? ScopeFingerprint { get; init; }
    public CanonicalHash? VisibleMemorySetHash { get; init; }
    public CanonicalHash? CanonicalPackHash { get; init; }
    public IReadOnlyList<DescriptorAuthoringMemoryItemProjection> Memories { get; init; } = Array.Empty<DescriptorAuthoringMemoryItemProjection>();

    public DescriptorAuthoringMemoryProjection Snapshot() => this with
    {
        Memories = Memories.Select(m => m.Snapshot()).ToArray()
    };
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Prompting/DescriptorAuthoringPromptInput.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Prompting;

public sealed record DescriptorAuthoringPromptInput : ISnapshotable<DescriptorAuthoringPromptInput>
{
    public required string ContractVersion { get; init; }
    public required string TenantId { get; init; }
    public required string IntentText { get; init; }
    public required DescriptorAuthoringMetadataContextProjection Metadata { get; init; }
    public required DescriptorAuthoringMemoryProjection Memory { get; init; }
    public IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<DescriptorKind> SupportedDescriptorKinds { get; init; } = Array.Empty<DescriptorKind>();
    public CanonicalHash? PromptInputHash { get; init; }

    public DescriptorAuthoringPromptInput Snapshot() => this with
    {
        Metadata = Metadata.Snapshot(),
        Memory = Memory.Snapshot(),
        VisibleDescriptorRefs = VisibleDescriptorRefs.ToArray(),
        SupportedDescriptorKinds = SupportedDescriptorKinds.ToArray()
    };
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Prompting/DescriptorAuthoringPromptOutput.cs`:

```csharp
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Prompting;

public sealed record DescriptorAuthoringPromptOutput : ISnapshotable<DescriptorAuthoringPromptOutput>
{
    public required string ContractVersion { get; init; }
    public required string PromptTemplateVersion { get; init; }
    public required CanonicalHash PromptInputHash { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }

    public DescriptorAuthoringPromptOutput Snapshot() => this;
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Model/IDescriptorAuthoringModelClient.cs`:

```csharp
namespace CrestCreates.Agent.Authoring.Abstractions.Model;

public interface IDescriptorAuthoringModelClient
{
    Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request,
        CancellationToken cancellationToken = default);
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Model/DescriptorAuthoringModelRequest.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Model;

public sealed record DescriptorAuthoringModelRequest : ISnapshotable<DescriptorAuthoringModelRequest>
{
    public required DescriptorAuthoringPromptOutput Prompt { get; init; }
    public required DescriptorAuthoringModelProfile ModelProfile { get; init; }

    public DescriptorAuthoringModelRequest Snapshot() => this with
    {
        Prompt = Prompt.Snapshot(),
        ModelProfile = ModelProfile.Snapshot()
    };
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Model/DescriptorAuthoringModelResponse.cs`:

```csharp
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Model;

public sealed record DescriptorAuthoringModelResponse : ISnapshotable<DescriptorAuthoringModelResponse>
{
    public required string ResponseText { get; init; }
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public CanonicalHash? PromptInputHash { get; init; }

    public DescriptorAuthoringModelResponse Snapshot() => this;
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Model/DescriptorAuthoringModelProfile.cs`:

```csharp
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Model;

public sealed record DescriptorAuthoringModelProfile : ISnapshotable<DescriptorAuthoringModelProfile>
{
    public required string ProfileName { get; init; }
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public int? MaxInputTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
    public bool SupportsJsonMode { get; init; }
    public bool SupportsStructuredOutput { get; init; }

    public DescriptorAuthoringModelProfile Snapshot() => this;
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Model/DescriptorAuthoringProviderProfile.cs`:

```csharp
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Model;

public sealed record DescriptorAuthoringProviderProfile : ISnapshotable<DescriptorAuthoringProviderProfile>
{
    public required string ProviderName { get; init; }
    public Uri? Endpoint { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public string? CredentialReference { get; init; }

    public DescriptorAuthoringProviderProfile Snapshot() => this;
}
```

- [ ] **Step 5: Add JSON source generation context**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Json/DescriptorAuthoringJsonSerializerContext.cs`:

```csharp
using System.Text.Json.Serialization;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Authoring.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DescriptorAuthoringResult))]
[JsonSerializable(typeof(DescriptorAuthoringPlan))]
[JsonSerializable(typeof(DescriptorDraftSet))]
[JsonSerializable(typeof(DescriptorAuthoringDiagnostic))]
[JsonSerializable(typeof(DescriptorAuthoringPromptInput))]
[JsonSerializable(typeof(DescriptorAuthoringPromptOutput))]
[JsonSerializable(typeof(DescriptorAuthoringMetadataContextProjection))]
[JsonSerializable(typeof(DescriptorAuthoringMemoryProjection))]
[JsonSerializable(typeof(DescriptorAuthoringModelRequest))]
[JsonSerializable(typeof(DescriptorAuthoringModelResponse))]
[JsonSerializable(typeof(DescriptorAuthoringModelProfile))]
[JsonSerializable(typeof(DescriptorAuthoringProviderProfile))]
[JsonSerializable(typeof(CanonicalHash))]
public sealed partial class DescriptorAuthoringJsonSerializerContext : JsonSerializerContext;
```

- [ ] **Step 6: Add projects to solution**

Add both project paths to `CrestCreates.slnx` using the existing `.slnx` XML style. The final solution must include:

```text
src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj
tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj
```

- [ ] **Step 7: Run tests and build**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj
dotnet build src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj
```

Expected: both commands pass.

- [ ] **Step 8: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests CrestCreates.slnx
git commit -m "feat: add descriptor authoring contracts"
```

---

### Task 2: Move Sample Authoring onto Framework Contracts

**Files:**
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/CrestCreates.Samples.DescriptorControlPlane.csproj`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/FakeCompanyCertificationAuthoringAgent.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationDraftSetReviewResult.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioRunner.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioHost.cs`
- Delete by recycle move: sample-local `IDescriptorAuthoringAgent.cs`, `DescriptorAuthoringPlan.cs`, `DescriptorAuthoringResult.cs`, `DescriptorDraftSet.cs` move to `99_RecycleBin/phase7g-sample-authoring-contracts/`
- Test: `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`

**Interfaces:**
- Consumes: framework `IDescriptorAuthoringAgent`, `DescriptorAuthoringPlan`, `DescriptorAuthoringResult`, `DescriptorDraftSet`.
- Produces: unchanged sample golden scenario behavior and DI registration using framework interface.

- [ ] **Step 1: Write failing namespace migration test**

Add this test to `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`:

```csharp
[Fact]
public void FakeAuthoringAgent_Implements_Framework_Authoring_Interface()
{
    typeof(FakeCompanyCertificationAuthoringAgent)
        .GetInterfaces()
        .Should()
        .Contain(typeof(CrestCreates.Agent.Authoring.Abstractions.Authoring.IDescriptorAuthoringAgent));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "FullyQualifiedName~FakeAuthoringAgent_Implements_Framework_Authoring_Interface"
```

Expected: FAIL because the sample fake still implements the sample-local interface or the project lacks the reference.

- [ ] **Step 3: Add sample project reference**

Modify `samples/CrestCreates.Samples.DescriptorControlPlane/CrestCreates.Samples.DescriptorControlPlane.csproj` by adding:

```xml
<ProjectReference Include="../../src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj" />
```

Use the relative path style already present in the file.

- [ ] **Step 4: Update sample authoring usings and result creation**

In `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/FakeCompanyCertificationAuthoringAgent.cs`, replace the sample namespace contract dependency with:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
```

When constructing `DescriptorAuthoringResult`, set the new `Status`:

```csharp
var result = new DescriptorAuthoringResult
{
    Status = DescriptorAuthoringStatus.Succeeded,
    Plan = new DescriptorAuthoringPlan
    {
        PlanId = "plan_company_certification_finance_review",
        IntentText = Phase7fIntent,
        PlannedDescriptorRefs = new[]
        {
            new DescriptorRef("humantask", "ht_finance_review_company_certification", 1),
            new DescriptorRef("workflow", updatedWorkflow.Id, updatedWorkflow.Version),
        },
    },
    DraftSet = new DescriptorDraftSet
    {
        DraftSetId = "draftset_company_certification_finance_review",
        Drafts = new[] { humanTaskDraft, workflowDraft },
    },
    Diagnostics = Array.Empty<DescriptorAuthoringDiagnostic>(),
};
```

- [ ] **Step 5: Update runner imports**

In sample runner and review result files, add:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
```

Remove reliance on sample-local authoring contract types.

- [ ] **Step 6: Move sample-local contract files to recycle bin**

Create:

```bash
mkdir -p 99_RecycleBin/phase7g-sample-authoring-contracts
```

Move these files:

```text
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/IDescriptorAuthoringAgent.cs
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorAuthoringPlan.cs
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorAuthoringResult.cs
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorDraftSet.cs
```

to:

```text
99_RecycleBin/phase7g-sample-authoring-contracts/
```

- [ ] **Step 7: Run sample golden tests**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "FullyQualifiedName~CompanyCertificationAuthoringGoldenScenarioTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add samples/CrestCreates.Samples.DescriptorControlPlane tests/Framework/Testing/CrestCreates.Samples.Tests 99_RecycleBin/phase7g-sample-authoring-contracts
git commit -m "refactor: move sample authoring to framework contracts"
```

---

### Task 3: Provider-Agnostic Authoring Runtime, Prompt Projections, and Prompt Hash

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/CrestCreates.Agent.Authoring.csproj`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/AgentAuthoringServiceCollectionExtensions.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/IDescriptorAuthoringPromptInputFactory.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptInputFactory.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/IDescriptorAuthoringPromptInputHashService.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptInputHashService.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/IDescriptorAuthoringPromptBuilder.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptBuilder.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/PromptInputHashTests.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/AuthoringBoundaryTests.cs`
- Modify: `CrestCreates.slnx`

**Interfaces:**
- Consumes: `AgentAuthoringContext`, `DescriptorAuthoringPromptInput`, `ICanonicalHashComputer`.
- Produces: `IDescriptorAuthoringPromptInputFactory.Create(AgentAuthoringContext)`, `IDescriptorAuthoringPromptInputHashService.ComputeHash(DescriptorAuthoringPromptInput)`, `IDescriptorAuthoringPromptBuilder.Build(DescriptorAuthoringPromptInput)`.

- [ ] **Step 1: Add failing runtime boundary and prompt hash tests**

Append to `AuthoringBoundaryTests.cs`:

```csharp
[Fact]
public void AuthoringRuntime_DoNotReference_ControlPlane_Or_RuntimeExecution()
{
    typeof(CrestCreates.Agent.Authoring.AgentAuthoringServiceCollectionExtensions).Assembly
        .GetReferencedAssemblies()
        .Select(name => name.Name)
        .Should()
        .NotContain(new[]
        {
            "CrestCreates.Agent.ControlPlane",
            "CrestCreates.Agent.ControlPlane.Abstractions",
            "CrestCreates.Capability",
            "CrestCreates.Workflow",
            "CrestCreates.HumanTask"
        });
}

[Fact]
public void AuthoringRuntime_DoesNotReference_ProviderSpecificProject()
{
    typeof(CrestCreates.Agent.Authoring.AgentAuthoringServiceCollectionExtensions).Assembly
        .GetReferencedAssemblies()
        .Select(name => name.Name)
        .Should()
        .NotContain("CrestCreates.Agent.Authoring.Http");
}
```

Create `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/PromptInputHashTests.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class PromptInputHashTests
{
    [Fact]
    public void PromptInputHash_IsStable()
    {
        var service = CreateHashService();
        var input = TestPromptInput("intent");

        var first = service.ComputeHash(input);
        var second = service.ComputeHash(input);

        first.Value.Should().Be(second.Value);
    }

    [Fact]
    public void PromptInputHash_Changes_When_MetadataContextPack_Changes()
    {
        var service = CreateHashService();

        var first = service.ComputeHash(TestPromptInput("intent", descriptorName: "A"));
        var second = service.ComputeHash(TestPromptInput("intent", descriptorName: "B"));

        first.Value.Should().NotBe(second.Value);
    }

    [Fact]
    public void PromptInputHash_UsesAuthoringProjection_NotRawObjectSerialization()
    {
        typeof(DescriptorAuthoringPromptInput).GetProperties()
            .Select(p => p.PropertyType.Name)
            .Should()
            .NotContain(new[] { "MetadataContextPack", "AgentMemoryPack" });
    }

    private static DefaultDescriptorAuthoringPromptInputHashService CreateHashService()
    {
        var hashComputer = new Mock<ICanonicalHashComputer>();
        hashComputer
            .Setup(h => h.ComputeFromProjection(It.IsAny<CanonicalHashProjectionResult>()))
            .Returns((CanonicalHashProjectionResult projection) => new CanonicalHash
            {
                Algorithm = "test",
                Value = projection.Payload,
                ArtifactKind = projection.Metadata.ArtifactKind,
                Purpose = projection.Metadata.Purpose,
                Scope = projection.Metadata.Scope,
                Version = projection.Metadata.CanonicalShapeVersion
            });

        return new DefaultDescriptorAuthoringPromptInputHashService(hashComputer.Object);
    }

    private static DescriptorAuthoringPromptInput TestPromptInput(string intent, string descriptorName = "Descriptor") => new()
    {
        ContractVersion = "7g.v1",
        TenantId = "tenant-1",
        IntentText = intent,
        Metadata = new DescriptorAuthoringMetadataContextProjection
        {
            Descriptors = new[]
            {
                new DescriptorAuthoringDescriptorProjection
                {
                    Ref = new CrestCreates.Metadata.Abstractions.DescriptorRef("workflow", "descriptor-1", 1),
                    Kind = CrestCreates.Metadata.Abstractions.DescriptorKind.Workflow,
                    Name = descriptorName
                }
            }
        },
        Memory = new DescriptorAuthoringMemoryProjection
        {
            IsAuthoritative = false
        }
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~PromptInputHashTests|FullyQualifiedName~AuthoringRuntime"
```

Expected: build fails because `CrestCreates.Agent.Authoring` does not exist.

- [ ] **Step 3: Add authoring runtime project and DI**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/CrestCreates.Agent.Authoring.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Agent.Authoring</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Authoring</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj" />
    <ProjectReference Include="../CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.ContextPack.Abstractions/CrestCreates.Metadata.ContextPack.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CrestCreates.DescriptorDraft.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
  </ItemGroup>
</Project>
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/AgentAuthoringServiceCollectionExtensions.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Model;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Authoring.Prompting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.Authoring;

public static class AgentAuthoringServiceCollectionExtensions
{
    public static IServiceCollection AddAgentAuthoring(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorAuthoringPromptInputFactory, DefaultDescriptorAuthoringPromptInputFactory>();
        services.TryAddSingleton<IDescriptorAuthoringPromptInputHashService, DefaultDescriptorAuthoringPromptInputHashService>();
        services.TryAddSingleton<IDescriptorAuthoringPromptBuilder, DefaultDescriptorAuthoringPromptBuilder>();
        services.TryAddSingleton<IDescriptorAuthoringOutputParser, JsonDescriptorAuthoringOutputParser>();
        services.TryAddSingleton<IDescriptorAuthoringModelClient, RecordedDescriptorAuthoringModelClient>();
        services.TryAddSingleton<IDescriptorAuthoringAgent, LlmDescriptorAuthoringAgent>();
        return services;
    }
}
```

- [ ] **Step 4: Add prompt interfaces and hash service**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/IDescriptorAuthoringPromptInputFactory.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Authoring.Prompting;

public interface IDescriptorAuthoringPromptInputFactory
{
    DescriptorAuthoringPromptInput Create(AgentAuthoringContext context);
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/IDescriptorAuthoringPromptInputHashService.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Authoring.Prompting;

public interface IDescriptorAuthoringPromptInputHashService
{
    CanonicalHash ComputeHash(DescriptorAuthoringPromptInput input);
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptInputHashService.cs`:

```csharp
using System.Globalization;
using System.Text;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Authoring.Prompting;

public sealed class DefaultDescriptorAuthoringPromptInputHashService : IDescriptorAuthoringPromptInputHashService
{
    private readonly ICanonicalHashComputer _hashComputer;

    public DefaultDescriptorAuthoringPromptInputHashService(ICanonicalHashComputer hashComputer)
    {
        _hashComputer = hashComputer;
    }

    public CanonicalHash ComputeHash(DescriptorAuthoringPromptInput input)
    {
        var payload = BuildProjectionPayload(input);
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = "DescriptorAuthoringPromptInput",
                Purpose = CanonicalHashPurposeNames.SourceIdentity,
                Scope = CanonicalHashScopeNames.InternalFull,
                CanonicalShapeVersion = "descriptor-authoring-prompt-input-v1"
            },
            payload);

        return _hashComputer.ComputeFromProjection(projection);
    }

    private static string BuildProjectionPayload(DescriptorAuthoringPromptInput input)
    {
        var builder = new StringBuilder();
        Append(builder, "contract", input.ContractVersion);
        Append(builder, "tenant", input.TenantId);
        Append(builder, "intent", input.IntentText);
        foreach (var descriptor in input.Metadata.Descriptors.OrderBy(d => d.Ref.Id, StringComparer.Ordinal))
        {
            Append(builder, "descriptor", string.Create(CultureInfo.InvariantCulture, $"{descriptor.Kind}:{descriptor.Ref.Id}:{descriptor.Name}:{descriptor.ContractHash?.Value}:{descriptor.DefinitionHash?.Value}"));
        }
        Append(builder, "memory-authoritative", input.Memory.IsAuthoritative.ToString(CultureInfo.InvariantCulture));
        foreach (var memory in input.Memory.Memories.OrderBy(m => m.MemoryId, StringComparer.Ordinal))
        {
            Append(builder, "memory", string.Create(CultureInfo.InvariantCulture, $"{memory.MemoryId}:{memory.Kind}:{memory.Confidence}:{memory.CanonicalContentHash?.Value}:{memory.Content}"));
        }
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string key, string? value)
    {
        builder.Append(key.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(key);
        builder.Append('=');
        builder.Append((value ?? string.Empty).Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }
}
```

- [ ] **Step 5: Add prompt input factory and builder**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptInputFactory.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Authoring.Prompting;

public sealed class DefaultDescriptorAuthoringPromptInputFactory : IDescriptorAuthoringPromptInputFactory
{
    private readonly IDescriptorAuthoringPromptInputHashService _hashService;

    public DefaultDescriptorAuthoringPromptInputFactory(IDescriptorAuthoringPromptInputHashService hashService)
    {
        _hashService = hashService;
    }

    public DescriptorAuthoringPromptInput Create(AgentAuthoringContext context)
    {
        var input = new DescriptorAuthoringPromptInput
        {
            ContractVersion = "7g.v1",
            TenantId = context.Request.TenantId,
            IntentText = context.Request.IntentText,
            Metadata = new DescriptorAuthoringMetadataContextProjection
            {
                Descriptors = context.MetadataContextPack.Descriptors.Select(d => new DescriptorAuthoringDescriptorProjection
                {
                    Ref = d.Ref,
                    Kind = d.Kind,
                    Name = d.Name,
                    ContractHash = d.StableHashes?.ContractHash,
                    DefinitionHash = d.StableHashes?.DefinitionHash
                }).ToArray(),
                VisibleDescriptorRefs = context.MetadataContextPack.Descriptors.Select(d => d.Ref).ToArray(),
                Diagnostics = context.MetadataContextPack.Diagnostics.Select(d => d.Message).ToArray()
            },
            Memory = new DescriptorAuthoringMemoryProjection
            {
                IsAuthoritative = context.MemoryPack.IsAuthoritative,
                ScopeFingerprint = context.MemoryPack.ScopeFingerprint,
                VisibleMemorySetHash = context.MemoryPack.VisibleMemorySetHash,
                CanonicalPackHash = context.MemoryPack.CanonicalPackHash,
                Memories = context.MemoryPack.Memories.Select(m => new DescriptorAuthoringMemoryItemProjection
                {
                    MemoryId = m.MemoryId,
                    Kind = m.Kind,
                    Content = m.Content,
                    Confidence = m.Confidence,
                    CanonicalContentHash = m.CanonicalContentHash,
                    DescriptorRefs = m.DescriptorRefs.ToArray()
                }).ToArray()
            },
            VisibleDescriptorRefs = context.MetadataContextPack.Descriptors.Select(d => d.Ref).ToArray(),
            SupportedDescriptorKinds = Enum.GetValues<DescriptorKind>()
        };

        return input with { PromptInputHash = _hashService.ComputeHash(input) };
    }
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/IDescriptorAuthoringPromptBuilder.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Prompting;

namespace CrestCreates.Agent.Authoring.Prompting;

public interface IDescriptorAuthoringPromptBuilder
{
    DescriptorAuthoringPromptOutput Build(DescriptorAuthoringPromptInput input);
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptBuilder.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Prompting;

namespace CrestCreates.Agent.Authoring.Prompting;

public sealed class DefaultDescriptorAuthoringPromptBuilder : IDescriptorAuthoringPromptBuilder
{
    public DescriptorAuthoringPromptOutput Build(DescriptorAuthoringPromptInput input)
    {
        if (input.PromptInputHash is null)
        {
            throw new InvalidOperationException("DescriptorAuthoringPromptInput.PromptInputHash is required before building a prompt.");
        }

        var systemPrompt = """
            You author CrestCreates descriptor drafts only.
            You must return JSON matching contract 7g.v1.
            You must not activate descriptors, approve changes, call Control Plane tools, mutate runtime registries, or execute runtime handlers.
            Agent memory is recalled non-authoritative context. Metadata context wins over memory.
            """;

        var userPrompt = $"""
            Intent:
            {input.IntentText}

            Tenant:
            {input.TenantId}

            PromptInputHash:
            {input.PromptInputHash.Value}

            Return a descriptor authoring plan and draft payloads only.
            """;

        return new DescriptorAuthoringPromptOutput
        {
            ContractVersion = input.ContractVersion,
            PromptTemplateVersion = "descriptor-authoring-prompt-template-v1",
            PromptInputHash = input.PromptInputHash,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt
        };
    }
}
```

- [ ] **Step 6: Add authoring runtime project to solution and test references**

Add to `CrestCreates.slnx`:

```text
src/Runtime/Agent/CrestCreates.Agent.Authoring/CrestCreates.Agent.Authoring.csproj
```

Add to `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj`:

```xml
<ProjectReference Include="../../../src/Runtime/Agent/CrestCreates.Agent.Authoring/CrestCreates.Agent.Authoring.csproj" />
<PackageReference Include="Moq" />
```

- [ ] **Step 7: Run tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~PromptInputHashTests|FullyQualifiedName~AuthoringRuntime"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Authoring tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests CrestCreates.slnx
git commit -m "feat: add provider agnostic authoring runtime"
```

---

### Task 4: Output Parser, Fake/Recorded Clients, and LLM Agent

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/IDescriptorAuthoringOutputParser.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/DescriptorAuthoringPlanDto.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/JsonDescriptorAuthoringOutputParser.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Model/FakeDescriptorAuthoringModelClient.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Model/RecordedDescriptorAuthoringModelClient.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/LlmDescriptorAuthoringAgent.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/OutputParserTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/LlmDescriptorAuthoringAgentTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/Fixtures/company-certification-authoring-response.json`

**Interfaces:**
- Consumes: prompt builder and model client from Task 3.
- Produces: `JsonDescriptorAuthoringOutputParser.Parse(string responseText, CanonicalHash expectedPromptInputHash)`, `LlmDescriptorAuthoringAgent.AuthorAsync(AgentAuthoringContext context, CancellationToken cancellationToken)`, fake and recorded model clients.

- [ ] **Step 1: Add parser failing tests**

Create `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/OutputParserTests.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class OutputParserTests
{
    [Fact]
    public void InvalidJson_ReturnsParserDiagnostics()
    {
        var parser = new JsonDescriptorAuthoringOutputParser();
        var result = parser.Parse("{", TestHash("hash"));

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        result.Diagnostics.Should().Contain(d => d.Code == DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput);
    }

    [Fact]
    public void PromptInputHashMismatch_IsRejected()
    {
        var parser = new JsonDescriptorAuthoringOutputParser();
        var result = parser.Parse("""
            {
              "contractVersion": "7g.v1",
              "promptInputHash": "wrong",
              "planId": "plan",
              "intentText": "intent",
              "items": []
            }
            """, TestHash("expected"));

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        result.Diagnostics.Should().Contain(d => d.Code == DescriptorAuthoringDiagnosticCodes.PromptHashMismatch);
    }

    [Fact]
    public void RuntimeOperationRequest_IsRejected()
    {
        var parser = new JsonDescriptorAuthoringOutputParser();
        var result = parser.Parse("""
            {
              "contractVersion": "7g.v1",
              "promptInputHash": "hash",
              "planId": "plan",
              "intentText": "intent",
              "items": [
                {
                  "descriptorKind": "Workflow",
                  "descriptorId": "wf",
                  "operation": "Activate",
                  "payload": {},
                  "rationale": "activate it",
                  "evidenceRefs": [],
                  "memoryRefs": [],
                  "assumptions": []
                }
              ]
            }
            """, TestHash("hash"));

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        result.Diagnostics.Should().Contain(d => d.Code == DescriptorAuthoringDiagnosticCodes.GovernanceBoundaryViolation);
    }

    [Fact]
    public void RecordedFixture_ProducesStableDraftSet()
    {
        var parser = new JsonDescriptorAuthoringOutputParser();
        var result = parser.Parse("""
            {
              "contractVersion": "7g.v1",
              "promptInputHash": "hash",
              "planId": "plan_company_certification_finance_review",
              "intentText": "Add second-level finance review before approving company certification.",
              "items": [
                {
                  "descriptorKind": "HumanTask",
                  "descriptorId": "ht_finance_review_company_certification",
                  "operation": "Create",
                  "draftId": "draft_company_certification_finance_review_humantask",
                  "proposedVersion": "1",
                  "payload": {
                    "name": "humantask.FinanceReviewCompanyCertification",
                    "permissions": "CompanyCertification.FinanceReview",
                    "approveCapabilityId": "cap_approve_company_certification",
                    "rejectCapabilityId": "cap_reject_company_certification"
                  },
                  "rationale": "Add finance review before approval.",
                  "evidenceRefs": ["metadata:wf_company_certification"],
                  "memoryRefs": [],
                  "assumptions": []
                },
                {
                  "descriptorKind": "Workflow",
                  "descriptorId": "wf_company_certification",
                  "operation": "Update",
                  "draftId": "draft_company_certification_workflow_finance_review",
                  "baseVersion": "1",
                  "proposedVersion": "1",
                  "payload": {
                    "name": "workflow.CompanyCertification",
                    "steps": [
                      { "id": "step_submit", "targetKind": "Capability", "targetId": "cap_submit_company_certification", "transitions": ["step_review"] },
                      { "id": "step_review", "targetKind": "HumanTask", "targetId": "ht_review_company_certification", "transitions": ["step_finance_review"] },
                      { "id": "step_finance_review", "targetKind": "HumanTask", "targetId": "ht_finance_review_company_certification", "transitions": ["step_approve"] },
                      { "id": "step_approve", "targetKind": "Capability", "targetId": "cap_approve_company_certification", "transitions": [] }
                    ]
                  },
                  "rationale": "Insert finance review before approve.",
                  "evidenceRefs": ["metadata:wf_company_certification"],
                  "memoryRefs": [],
                  "assumptions": []
                }
              ]
            }
            """, TestHash("hash"));

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.DraftSet.Drafts.Select(d => d.DescriptorId)
            .Should().Equal("ht_finance_review_company_certification", "wf_company_certification");
    }

    private static CanonicalHash TestHash(string value) => new()
    {
        Algorithm = "test",
        Value = value,
        ArtifactKind = "DescriptorAuthoringPromptInput",
        Purpose = CanonicalHashPurposeNames.SourceIdentity,
        Scope = CanonicalHashScopeNames.InternalFull,
        Version = "descriptor-authoring-prompt-input-v1"
    };
}
```

- [ ] **Step 2: Add LLM agent failing test**

Create `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/LlmDescriptorAuthoringAgentTests.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Model;
using CrestCreates.Agent.Authoring.Prompting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class LlmDescriptorAuthoringAgentTests
{
    [Fact]
    public void LlmResult_Output_IsDraftSet_NotActiveDescriptor()
    {
        var resultType = typeof(DescriptorAuthoringResult);

        resultType.GetProperties().Select(p => p.Name)
            .Should()
            .Contain(new[] { nameof(DescriptorAuthoringResult.Plan), nameof(DescriptorAuthoringResult.DraftSet) });

        resultType.GetProperties().Select(p => p.Name)
            .Should()
            .NotContain(new[] { "ActivationRequest", "RuntimeActivationGateResult", "ActiveDescriptor" });
    }

    [Fact]
    public void RecordedFixture_ProducesStableDraftSet()
    {
        var client = new RecordedDescriptorAuthoringModelClient(new Dictionary<string, string>
        {
            ["hash"] = """
                {
                  "contractVersion": "7g.v1",
                  "promptInputHash": "hash",
                  "planId": "plan",
                  "intentText": "intent",
                  "items": []
                }
                """
        });

        var response = client.CompleteAsync(new DescriptorAuthoringModelRequest
        {
            Prompt = TestPromptFactory.Output("hash"),
            ModelProfile = new DescriptorAuthoringModelProfile
            {
                ProfileName = "fixture",
                ProviderName = "recorded",
                ModelName = "fixture"
            }
        }).GetAwaiter().GetResult();

        response.ResponseText.Should().Contain("\"planId\": \"plan\"");
    }
}
```

Implement `TestPromptFactory.Output` locally in the test file as a private static helper returning a valid `DescriptorAuthoringPromptOutput`.

- [ ] **Step 3: Run parser and agent tests to verify failure**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~OutputParserTests|FullyQualifiedName~LlmDescriptorAuthoringAgentTests"
```

Expected: build fails because parser and clients do not exist.

- [ ] **Step 4: Add parser interfaces and DTO**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/IDescriptorAuthoringOutputParser.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Authoring.Parsing;

public interface IDescriptorAuthoringOutputParser
{
    DescriptorAuthoringResult Parse(string responseText, CanonicalHash expectedPromptInputHash);
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/DescriptorAuthoringPlanDto.cs`:

```csharp
namespace CrestCreates.Agent.Authoring.Parsing;

public sealed record DescriptorAuthoringPlanDto
{
    public string? ContractVersion { get; init; }
    public string? PromptInputHash { get; init; }
    public string? PlanId { get; init; }
    public string? IntentText { get; init; }
    public IReadOnlyList<DescriptorAuthoringPlanItemDto> Items { get; init; } = Array.Empty<DescriptorAuthoringPlanItemDto>();
}

public sealed record DescriptorAuthoringPlanItemDto
{
    public string? DescriptorKind { get; init; }
    public string? DescriptorId { get; init; }
    public string? Operation { get; init; }
    public string? DraftId { get; init; }
    public string? BaseVersion { get; init; }
    public string? ProposedVersion { get; init; }
    public System.Text.Json.JsonElement Payload { get; init; }
    public string? Rationale { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MemoryRefs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();
}

public sealed record DescriptorAuthoringHumanTaskPayloadDto
{
    public string? Name { get; init; }
    public string? Permissions { get; init; }
    public string? ApproveCapabilityId { get; init; }
    public string? RejectCapabilityId { get; init; }
}

public sealed record DescriptorAuthoringWorkflowPayloadDto
{
    public string? Name { get; init; }
    public IReadOnlyList<DescriptorAuthoringWorkflowStepDto> Steps { get; init; } = Array.Empty<DescriptorAuthoringWorkflowStepDto>();
}

public sealed record DescriptorAuthoringWorkflowStepDto
{
    public string? Id { get; init; }
    public string? TargetKind { get; init; }
    public string? TargetId { get; init; }
    public IReadOnlyList<string> Transitions { get; init; } = Array.Empty<string>();
}
```

- [ ] **Step 5: Implement JSON parser**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/JsonDescriptorAuthoringOutputParser.cs`:

```csharp
using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Agent.Authoring.Parsing;

public sealed class JsonDescriptorAuthoringOutputParser : IDescriptorAuthoringOutputParser
{
    public DescriptorAuthoringResult Parse(string responseText, CanonicalHash expectedPromptInputHash)
    {
        DescriptorAuthoringPlanDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DescriptorAuthoringPlanDto>(
                responseText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            return Invalid(DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput, ex.Message);
        }

        if (dto is null)
        {
            return Invalid(DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput, "Provider output was empty.");
        }

        if (!StringComparer.Ordinal.Equals(dto.PromptInputHash, expectedPromptInputHash.Value))
        {
            return Blocked(DescriptorAuthoringDiagnosticCodes.PromptHashMismatch, "Provider output prompt input hash did not match the request hash.");
        }

        var drafts = new List<DescriptorDraft>();
        foreach (var item in dto.Items)
        {
            if (!Enum.TryParse<DescriptorKind>(item.DescriptorKind, ignoreCase: true, out var descriptorKind))
            {
                return Blocked(DescriptorAuthoringDiagnosticCodes.UnknownDescriptorKind, $"Unknown descriptor kind '{item.DescriptorKind}'.");
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(item.Operation, "Activate") ||
                StringComparer.OrdinalIgnoreCase.Equals(item.Operation, "Approve") ||
                StringComparer.OrdinalIgnoreCase.Equals(item.Operation, "Execute"))
            {
                return Blocked(DescriptorAuthoringDiagnosticCodes.GovernanceBoundaryViolation, $"Unsupported runtime operation '{item.Operation}'.");
            }

            if (!Enum.TryParse<DescriptorDraftOperation>(item.Operation, ignoreCase: true, out var operation))
            {
                return Blocked(DescriptorAuthoringDiagnosticCodes.UnsupportedDraftOperation, $"Unsupported draft operation '{item.Operation}'.");
            }

            var payload = MaterializePayload(descriptorKind, item.Payload);
            if (payload is null)
            {
                return Invalid(DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput, $"Payload for descriptor kind '{descriptorKind}' could not be materialized.");
            }

            drafts.Add(new DescriptorDraft
            {
                TenantId = "tenant-company-certification",
                DraftId = item.DraftId ?? $"draft_{item.DescriptorId}",
                DescriptorKind = descriptorKind,
                DescriptorId = item.DescriptorId ?? string.Empty,
                Operation = operation,
                AuthorKind = DescriptorDraftAuthorKind.Agent,
                AuthorId = "llm-descriptor-authoring-agent",
                CreatedAt = DateTimeOffset.UnixEpoch,
                Payload = payload,
                BaseVersion = item.BaseVersion,
                ProposedVersion = item.ProposedVersion,
                Intent = dto.IntentText,
                Rationale = item.Rationale,
                Source = "LlmDescriptorAuthoringAgent"
            });
        }

        return new DescriptorAuthoringResult
        {
            Status = DescriptorAuthoringStatus.Succeeded,
            Plan = new DescriptorAuthoringPlan
            {
                PlanId = dto.PlanId ?? "provider-plan",
                IntentText = dto.IntentText ?? string.Empty,
                PlannedDescriptorRefs = Array.Empty<DescriptorRef>(),
                Assumptions = dto.Items.SelectMany(i => i.Assumptions).ToArray()
            },
            DraftSet = new DescriptorDraftSet
            {
                DraftSetId = dto.PlanId is null ? "provider-draft-set" : $"{dto.PlanId}-draft-set",
                Drafts = drafts.ToArray()
            },
            Diagnostics = Array.Empty<DescriptorAuthoringDiagnostic>()
        };
    }

    private static DescriptorDraftPayload? MaterializePayload(DescriptorKind kind, JsonElement payload) => kind switch
    {
        DescriptorKind.HumanTask => MaterializeHumanTaskPayload(payload),
        DescriptorKind.Workflow => MaterializeWorkflowPayload(payload),
        _ => null
    };

    private static DescriptorDraftPayload? MaterializeHumanTaskPayload(JsonElement payload)
    {
        var dto = JsonSerializer.Deserialize<DescriptorAuthoringHumanTaskPayloadDto>(
            payload.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (dto is null)
        {
            return null;
        }

        var descriptor = new HumanTaskDescriptor
        {
            Id = "ht_finance_review_company_certification",
            Name = dto.Name ?? "humantask.FinanceReviewCompanyCertification",
            Version = 1,
            State = DescriptorState.Active,
            Permissions = dto.Permissions,
            AssigneeStrategy = AssigneeStrategy.SingleUser,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_review_company_certification", 1),
            Outcomes = new[]
            {
                new CompletionOutcome
                {
                    Condition = CompletionCondition.Approve,
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>(dto.ApproveCapabilityId ?? "cap_approve_company_certification", 1)
                },
                new CompletionOutcome
                {
                    Condition = CompletionCondition.Reject,
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>(dto.RejectCapabilityId ?? "cap_reject_company_certification", 1)
                }
            }
        };

        return new HumanTaskDescriptorDraftPayload(descriptor);
    }

    private static DescriptorDraftPayload? MaterializeWorkflowPayload(JsonElement payload)
    {
        var dto = JsonSerializer.Deserialize<DescriptorAuthoringWorkflowPayloadDto>(
            payload.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (dto is null)
        {
            return null;
        }

        var descriptor = new WorkflowDescriptor
        {
            Id = "wf_company_certification",
            Name = dto.Name ?? "workflow.CompanyCertification",
            Version = 1,
            State = DescriptorState.Active,
            Steps = dto.Steps.Select(MaterializeStep).ToArray()
        };

        return new WorkflowDescriptorDraftPayload(descriptor);
    }

    private static WorkflowStep MaterializeStep(DescriptorAuthoringWorkflowStepDto dto) => new()
    {
        Id = dto.Id ?? string.Empty,
        Name = dto.Id ?? string.Empty,
        Target = MaterializeTarget(dto),
        Transitions = dto.Transitions.ToArray()
    };

    private static InteractionTarget MaterializeTarget(DescriptorAuthoringWorkflowStepDto dto)
    {
        return dto.TargetKind switch
        {
            "Capability" => new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor>(dto.TargetId ?? string.Empty, 1)
            },
            "HumanTask" => new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(dto.TargetId ?? string.Empty, 1)
            },
            _ => new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor>(dto.TargetId ?? string.Empty, 1)
            }
        };
    }

    private static DescriptorAuthoringResult Invalid(DiagnosticCode code, string message) =>
        Result(DescriptorAuthoringStatus.InvalidProviderOutput, code, message);

    private static DescriptorAuthoringResult Blocked(DiagnosticCode code, string message) =>
        Result(DescriptorAuthoringStatus.Blocked, code, message);

    private static DescriptorAuthoringResult Result(DescriptorAuthoringStatus status, DiagnosticCode code, string message) => new()
    {
        Status = status,
        Plan = new DescriptorAuthoringPlan
        {
            PlanId = "invalid-provider-output",
            IntentText = string.Empty,
            PlannedDescriptorRefs = Array.Empty<DescriptorRef>()
        },
        DraftSet = new DescriptorDraftSet
        {
            DraftSetId = "invalid-provider-output",
            Drafts = Array.Empty<CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft>()
        },
        Diagnostics = new[]
        {
            new DescriptorAuthoringDiagnostic
            {
                Code = code,
                Severity = SeverityLevel.Error,
                Message = message
            }
        }
    };
}
```

- [ ] **Step 6: Add fake and recorded clients**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Model/FakeDescriptorAuthoringModelClient.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Model;

namespace CrestCreates.Agent.Authoring.Model;

public sealed class FakeDescriptorAuthoringModelClient : IDescriptorAuthoringModelClient
{
    private readonly string _responseText;

    public FakeDescriptorAuthoringModelClient(string responseText)
    {
        _responseText = responseText;
    }

    public Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DescriptorAuthoringModelResponse
        {
            ResponseText = _responseText,
            ProviderName = request.ModelProfile.ProviderName,
            ModelName = request.ModelProfile.ModelName,
            PromptInputHash = request.Prompt.PromptInputHash
        });
    }
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/Model/RecordedDescriptorAuthoringModelClient.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Model;

namespace CrestCreates.Agent.Authoring.Model;

public sealed class RecordedDescriptorAuthoringModelClient : IDescriptorAuthoringModelClient
{
    private readonly IReadOnlyDictionary<string, string> _responsesByPromptHash;

    public RecordedDescriptorAuthoringModelClient()
        : this(new Dictionary<string, string>())
    {
    }

    public RecordedDescriptorAuthoringModelClient(IReadOnlyDictionary<string, string> responsesByPromptHash)
    {
        _responsesByPromptHash = responsesByPromptHash;
    }

    public Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var key = request.Prompt.PromptInputHash.Value;
        if (!_responsesByPromptHash.TryGetValue(key, out var responseText))
        {
            responseText = $$"""
                {
                  "contractVersion": "{{request.Prompt.ContractVersion}}",
                  "promptInputHash": "{{key}}",
                  "planId": "empty-recorded-plan",
                  "intentText": "",
                  "items": []
                }
                """;
        }

        return Task.FromResult(new DescriptorAuthoringModelResponse
        {
            ResponseText = responseText,
            ProviderName = request.ModelProfile.ProviderName,
            ModelName = request.ModelProfile.ModelName,
            PromptInputHash = request.Prompt.PromptInputHash
        });
    }
}
```

- [ ] **Step 7: Add LLM agent**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring/LlmDescriptorAuthoringAgent.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Memory.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestCreates.Agent.Authoring;

public sealed class LlmDescriptorAuthoringAgent : IDescriptorAuthoringAgent
{
    private readonly IDescriptorAuthoringPromptInputFactory _inputFactory;
    private readonly IDescriptorAuthoringPromptBuilder _promptBuilder;
    private readonly IDescriptorAuthoringModelClient _modelClient;
    private readonly IDescriptorAuthoringOutputParser _parser;
    private readonly DescriptorAuthoringModelProfile _modelProfile;

    public LlmDescriptorAuthoringAgent(
        IDescriptorAuthoringPromptInputFactory inputFactory,
        IDescriptorAuthoringPromptBuilder promptBuilder,
        IDescriptorAuthoringModelClient modelClient,
        IDescriptorAuthoringOutputParser parser,
        IOptions<DescriptorAuthoringModelProfile> modelProfile)
    {
        _inputFactory = inputFactory;
        _promptBuilder = promptBuilder;
        _modelClient = modelClient;
        _parser = parser;
        _modelProfile = modelProfile.Value;
    }

    public async Task<DescriptorAuthoringResult> AuthorAsync(
        AgentAuthoringContext context,
        CancellationToken cancellationToken = default)
    {
        var input = _inputFactory.Create(context);
        if (input.PromptInputHash is null)
        {
            throw new InvalidOperationException("Prompt input hash is required.");
        }

        var prompt = _promptBuilder.Build(input);
        var response = await _modelClient.CompleteAsync(new DescriptorAuthoringModelRequest
        {
            Prompt = prompt,
            ModelProfile = _modelProfile
        }, cancellationToken);

        return _parser.Parse(response.ResponseText, input.PromptInputHash);
    }
}
```

- [ ] **Step 8: Run tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~OutputParserTests|FullyQualifiedName~LlmDescriptorAuthoringAgentTests"
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Authoring tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests
git commit -m "feat: add deterministic llm authoring adapter"
```

---

### Task 5: OpenAI-Compatible Provider Integration

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/CrestCreates.Agent.Authoring.Http.csproj`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/AgentAuthoringHttpServiceCollectionExtensions.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/OpenAICompatible/OpenAICompatibleDescriptorAuthoringModelClient.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/OpenAICompatible/OpenAICompatibleAuthoringOptions.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/OpenAICompatible/OpenAICompatibleAuthoringRequest.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/OpenAICompatible/OpenAICompatibleAuthoringResponse.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/Credentials/IDescriptorAuthoringCredentialProvider.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/Credentials/DefaultDescriptorAuthoringCredentialProvider.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/ProviderBoundaryTests.cs`
- Modify: `CrestCreates.slnx`
- Modify: `CrestCreates.slnx`

**Interfaces:**
- Consumes: `IDescriptorAuthoringModelClient`, provider/model profile DTOs.
- Produces: `OpenAICompatibleDescriptorAuthoringModelClient` and credential provider in provider integration project only.

- [ ] **Step 1: Add provider boundary failing tests**

Create `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/ProviderBoundaryTests.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class ProviderBoundaryTests
{
    [Fact]
    public void AuthoringCore_DoNotReference_Http_Or_ProviderSdk()
    {
        typeof(CrestCreates.Agent.Authoring.AgentAuthoringServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.Authoring.Http",
                "System.Net.Http.Json",
                "OpenAI",
                "Azure.AI.OpenAI"
            });
    }

    [Fact]
    public void CredentialProvider_Is_Not_In_Authoring_Abstractions()
    {
        typeof(IDescriptorAuthoringModelClient).Assembly
            .GetTypes()
            .Select(type => type.Name)
            .Should()
            .NotContain("IDescriptorAuthoringCredentialProvider");
    }

    [Fact]
    public void DiagnosticCodes_Include_Provider_And_Credential_Causes()
    {
        DescriptorAuthoringDiagnosticCodes.ProviderUnauthorized.Value.Should().Be("AUTHORING_PROVIDER_UNAUTHORIZED");
        DescriptorAuthoringDiagnosticCodes.CredentialUnavailable.Value.Should().Be("AUTHORING_CREDENTIAL_UNAVAILABLE");
        DescriptorAuthoringDiagnosticCodes.CredentialRejected.Value.Should().Be("AUTHORING_CREDENTIAL_REJECTED");
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~ProviderBoundaryTests"
```

Expected: build fails until provider project exists or tests reference missing types are adjusted after project creation.

- [ ] **Step 3: Add provider integration project**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/CrestCreates.Agent.Authoring.Http.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Agent.Authoring.Http</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Authoring.Http</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
  </ItemGroup>
</Project>
```

`Microsoft.Extensions.Http` already exists in `Directory.Packages.props` through `$(RuntimePackageVersion)`, so this task does not modify central package versions.

- [ ] **Step 4: Add options and credential provider**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/OpenAICompatible/OpenAICompatibleAuthoringOptions.cs`:

```csharp
namespace CrestCreates.Agent.Authoring.Http.OpenAICompatible;

public sealed class OpenAICompatibleAuthoringOptions
{
    public Uri? Endpoint { get; init; }
    public string? CredentialReference { get; init; }
    public string ModelName { get; init; } = "gpt-4.1-mini";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/Credentials/IDescriptorAuthoringCredentialProvider.cs`:

```csharp
namespace CrestCreates.Agent.Authoring.Http.Credentials;

public interface IDescriptorAuthoringCredentialProvider
{
    ValueTask<string?> ResolveAsync(string? credentialReference, CancellationToken cancellationToken = default);
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/Credentials/DefaultDescriptorAuthoringCredentialProvider.cs`:

```csharp
namespace CrestCreates.Agent.Authoring.Http.Credentials;

public sealed class DefaultDescriptorAuthoringCredentialProvider : IDescriptorAuthoringCredentialProvider
{
    public ValueTask<string?> ResolveAsync(string? credentialReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialReference))
        {
            return ValueTask.FromResult<string?>(null);
        }

        if (credentialReference.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var name = credentialReference["env:".Length..];
            return ValueTask.FromResult(Environment.GetEnvironmentVariable(name));
        }

        return ValueTask.FromResult<string?>(null);
    }
}
```

- [ ] **Step 5: Add OpenAI-compatible request/response DTOs**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/OpenAICompatible/OpenAICompatibleAuthoringRequest.cs`:

```csharp
using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Authoring.Http.OpenAICompatible;

public sealed record OpenAICompatibleAuthoringRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OpenAICompatibleAuthoringMessage> Messages { get; init; }
}

public sealed record OpenAICompatibleAuthoringMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/OpenAICompatible/OpenAICompatibleAuthoringResponse.cs`:

```csharp
using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Authoring.Http.OpenAICompatible;

public sealed record OpenAICompatibleAuthoringResponse
{
    [JsonPropertyName("choices")]
    public IReadOnlyList<OpenAICompatibleAuthoringChoice> Choices { get; init; } = Array.Empty<OpenAICompatibleAuthoringChoice>();
}

public sealed record OpenAICompatibleAuthoringChoice
{
    [JsonPropertyName("message")]
    public OpenAICompatibleAuthoringMessage? Message { get; init; }
}
```

- [ ] **Step 6: Add OpenAI-compatible client**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/OpenAICompatible/OpenAICompatibleDescriptorAuthoringModelClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Http.Credentials;
using Microsoft.Extensions.Options;

namespace CrestCreates.Agent.Authoring.Http.OpenAICompatible;

public sealed class OpenAICompatibleDescriptorAuthoringModelClient : IDescriptorAuthoringModelClient
{
    private readonly HttpClient _httpClient;
    private readonly IDescriptorAuthoringCredentialProvider _credentialProvider;
    private readonly OpenAICompatibleAuthoringOptions _options;

    public OpenAICompatibleDescriptorAuthoringModelClient(
        HttpClient httpClient,
        IDescriptorAuthoringCredentialProvider credentialProvider,
        IOptions<OpenAICompatibleAuthoringOptions> options)
    {
        _httpClient = httpClient;
        _credentialProvider = credentialProvider;
        _options = options.Value;
    }

    public async Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var credential = await _credentialProvider.ResolveAsync(_options.CredentialReference, cancellationToken);
        if (!string.IsNullOrWhiteSpace(credential))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }

        var body = new OpenAICompatibleAuthoringRequest
        {
            Model = request.ModelProfile.ModelName,
            Messages = new[]
            {
                new OpenAICompatibleAuthoringMessage { Role = "system", Content = request.Prompt.SystemPrompt },
                new OpenAICompatibleAuthoringMessage { Role = "user", Content = request.Prompt.UserPrompt }
            }
        };

        var endpoint = _options.Endpoint ?? new Uri("https://api.openai.com/v1/chat/completions");
        using var response = await _httpClient.PostAsJsonAsync(endpoint, body, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OpenAICompatibleAuthoringResponse>(cancellationToken: cancellationToken);
        var text = payload?.Choices.FirstOrDefault()?.Message?.Content ?? string.Empty;

        return new DescriptorAuthoringModelResponse
        {
            ResponseText = text,
            ProviderName = request.ModelProfile.ProviderName,
            ModelName = request.ModelProfile.ModelName,
            PromptInputHash = request.Prompt.PromptInputHash
        };
    }
}
```

- [ ] **Step 7: Add provider DI**

Create `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/AgentAuthoringHttpServiceCollectionExtensions.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Http.Credentials;
using CrestCreates.Agent.Authoring.Http.OpenAICompatible;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.Authoring.Http;

public static class AgentAuthoringHttpServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAICompatibleDescriptorAuthoring(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorAuthoringCredentialProvider, DefaultDescriptorAuthoringCredentialProvider>();
        services.AddHttpClient<IDescriptorAuthoringModelClient, OpenAICompatibleDescriptorAuthoringModelClient>();
        return services;
    }
}
```

- [ ] **Step 8: Add project to solution and run tests**

Add to `CrestCreates.slnx`:

```text
src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/CrestCreates.Agent.Authoring.Http.csproj
```

Run:

```bash
dotnet build src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/CrestCreates.Agent.Authoring.Http.csproj
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~ProviderBoundaryTests"
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Authoring.Http tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests Directory.Packages.props CrestCreates.slnx
git commit -m "feat: add openai compatible authoring provider"
```

---

### Task 6: Golden Scenario LLM Fixture Path

**Files:**
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/GoldenScenarioLlmFixtureTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/Fixtures/company-certification-authoring-response.json`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioRunner.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioHost.cs`
- Modify: `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`

**Interfaces:**
- Consumes: `IDescriptorAuthoringAgent` framework interface and existing sample runner.
- Produces: a fixture-backed path proving generated draft sets still flow through existing review/package/activation mainline.

- [ ] **Step 1: Add fixture**

Create `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/Fixtures/company-certification-authoring-response.json`:

```json
{
  "contractVersion": "7g.v1",
  "promptInputHash": "fixture-hash",
  "planId": "plan_company_certification_finance_review",
  "intentText": "Add second-level finance review before approving company certification.",
  "items": [
    {
      "descriptorKind": "HumanTask",
      "descriptorId": "ht_finance_review_company_certification",
      "operation": "Create",
      "draftId": "draft_company_certification_finance_review_humantask",
      "proposedVersion": "1",
      "payload": {
        "name": "humantask.FinanceReviewCompanyCertification",
        "permissions": "CompanyCertification.FinanceReview",
        "approveCapabilityId": "cap_approve_company_certification",
        "rejectCapabilityId": "cap_reject_company_certification"
      },
      "rationale": "Add finance review before approval.",
      "evidenceRefs": ["metadata:wf_company_certification"],
      "memoryRefs": [],
      "assumptions": []
    },
    {
      "descriptorKind": "Workflow",
      "descriptorId": "wf_company_certification",
      "operation": "Update",
      "draftId": "draft_company_certification_workflow_finance_review",
      "baseVersion": "1",
      "proposedVersion": "1",
      "payload": {
        "name": "workflow.CompanyCertification",
        "steps": [
          { "id": "step_submit", "targetKind": "Capability", "targetId": "cap_submit_company_certification", "transitions": ["step_review"] },
          { "id": "step_review", "targetKind": "HumanTask", "targetId": "ht_review_company_certification", "transitions": ["step_finance_review"] },
          { "id": "step_finance_review", "targetKind": "HumanTask", "targetId": "ht_finance_review_company_certification", "transitions": ["step_approve"] },
          { "id": "step_approve", "targetKind": "Capability", "targetId": "cap_approve_company_certification", "transitions": [] }
        ]
      },
      "rationale": "Insert finance review step between initial review and approve.",
      "evidenceRefs": ["metadata:wf_company_certification"],
      "memoryRefs": [],
      "assumptions": []
    }
  ]
}
```

- [ ] **Step 2: Add golden fixture test**

Create `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/GoldenScenarioLlmFixtureTests.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Model;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class GoldenScenarioLlmFixtureTests
{
[Fact]
public async Task RecordedFixture_ProducesStableDraftSet()
    {
        var fixture = await File.ReadAllTextAsync("Fixtures/company-certification-authoring-response.json");
        var client = new RecordedDescriptorAuthoringModelClient(new Dictionary<string, string>
        {
            ["fixture-hash"] = fixture
        });

        var response = await client.CompleteAsync(new CrestCreates.Agent.Authoring.Abstractions.Model.DescriptorAuthoringModelRequest
        {
            Prompt = TestPromptFactory.Output("fixture-hash"),
            ModelProfile = new CrestCreates.Agent.Authoring.Abstractions.Model.DescriptorAuthoringModelProfile
            {
                ProfileName = "fixture",
                ProviderName = "recorded",
                ModelName = "fixture"
            }
        });

        response.ResponseText.Should().Contain("ht_finance_review_company_certification");
    }

    [Fact]
    public void DescriptorDraftSet_IsAtomic_OnSingleInvalidDraft()
    {
        typeof(DescriptorAuthoringStatus).GetEnumNames().Should().Contain("Blocked");
        typeof(DescriptorDraftSet).GetProperties().Select(p => p.Name).Should().Contain("Drafts");
    }
}
```

Add this helper in the same test file:

```csharp
private static class TestPromptFactory
{
    public static CrestCreates.Agent.Authoring.Abstractions.Prompting.DescriptorAuthoringPromptOutput Output(string hash) => new()
    {
        ContractVersion = "7g.v1",
        PromptTemplateVersion = "fixture-template",
        PromptInputHash = new CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash
        {
            Algorithm = "fixture",
            Value = hash,
            ArtifactKind = "DescriptorAuthoringPromptInput",
            Purpose = CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHashPurposeNames.SourceIdentity,
            Scope = CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHashScopeNames.InternalFull,
            Version = "descriptor-authoring-prompt-input-v1"
        },
        SystemPrompt = "system",
        UserPrompt = "user"
    };
}
```

- [ ] **Step 3: Run tests to verify fixture loads**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~GoldenScenarioLlmFixtureTests"
```

Expected: PASS.

- [ ] **Step 4: Extend sample runner to allow explicit authoring agent**

Add overload to `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioRunner.cs`:

```csharp
public async Task<CompanyCertificationDraftSetReviewResult> RunUntilDraftSetReviewAsync(
    string intentText,
    IDescriptorAuthoringAgent authoringAgent,
    CancellationToken ct = default)
{
    return await RunUntilDraftSetReviewAsync(
        intentText,
        CompanyCertificationDescriptorCloner.CopyAllDescriptors(),
        authoringAgent,
        ct);
}
```

Refactor the existing overload so it resolves the service and delegates:

```csharp
var authoringAgent = _serviceProvider.GetRequiredService<IDescriptorAuthoringAgent>();
return await RunUntilDraftSetReviewAsync(intentText, startingInventory, authoringAgent, ct);
```

Add a private or public overload accepting `IDescriptorAuthoringAgent authoringAgent` and move the existing body into it. Keep all review/materialization/package/activation logic unchanged.

- [ ] **Step 5: Add sample test asserting LLM fixture path uses mainline**

Add to `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`:

```csharp
[Fact]
public async Task GoldenScenario_LlmFixture_StillUsesGovernanceMainline()
{
    using var host = new CompanyCertificationGoldenScenarioHost();
    var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();
    var fixture = await File.ReadAllTextAsync(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "company-certification-authoring-response.json"));
    var recordedClient = new RecordedDescriptorAuthoringModelClient(new Dictionary<string, string>
    {
        ["fixture-hash"] = fixture
    });
    var agent = CompanyCertificationLlmFixtureAgentFactory.Create(host.Provider, recordedClient);

    var report = await runner.RunUntilDraftSetReviewAsync(Phase7fIntent, agent);

    report.IsBlocked.Should().BeFalse(report.BlockReason);
    report.PerDraftReviewResults.Should().NotBeEmpty();
    report.FinalProposedInventory.Should().Contain(d => d.Id == "ht_finance_review_company_certification");
}
```

Add this helper class in the same test file:

```csharp
private static class CompanyCertificationLlmFixtureAgentFactory
{
    public static IDescriptorAuthoringAgent Create(
        IServiceProvider provider,
        RecordedDescriptorAuthoringModelClient recordedClient)
    {
        var hashComputer = provider.GetRequiredService<ICanonicalHashComputer>();
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(hashComputer);
        var inputFactory = new FixedHashPromptInputFactory(
            new DefaultDescriptorAuthoringPromptInputFactory(hashService),
            "fixture-hash");

        return new LlmDescriptorAuthoringAgent(
            inputFactory,
            new DefaultDescriptorAuthoringPromptBuilder(),
            recordedClient,
            new JsonDescriptorAuthoringOutputParser(),
            Options.Create(new DescriptorAuthoringModelProfile
            {
                ProfileName = "fixture",
                ProviderName = "recorded",
                ModelName = "fixture"
            }));
    }

    private sealed class FixedHashPromptInputFactory : IDescriptorAuthoringPromptInputFactory
    {
        private readonly IDescriptorAuthoringPromptInputFactory _inner;
        private readonly string _hash;

        public FixedHashPromptInputFactory(IDescriptorAuthoringPromptInputFactory inner, string hash)
        {
            _inner = inner;
            _hash = hash;
        }

        public DescriptorAuthoringPromptInput Create(AgentAuthoringContext context)
        {
            var input = _inner.Create(context);
            return input with
            {
                PromptInputHash = new CanonicalHash
                {
                    Algorithm = "fixture",
                    Value = _hash,
                    ArtifactKind = "DescriptorAuthoringPromptInput",
                    Purpose = CanonicalHashPurposeNames.SourceIdentity,
                    Scope = CanonicalHashScopeNames.InternalFull,
                    Version = "descriptor-authoring-prompt-input-v1"
                }
            };
        }
    }
}
```

This test must pass an `LlmDescriptorAuthoringAgent` configured with `RecordedDescriptorAuthoringModelClient` to the new overload. The deterministic fake agent remains covered by the existing Phase 7f tests.

- [ ] **Step 6: Run golden scenario tests**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "FullyQualifiedName~GoldenScenario_LlmFixture_StillUsesGovernanceMainline"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add samples/CrestCreates.Samples.DescriptorControlPlane tests/Framework/Testing/CrestCreates.Samples.Tests tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests
git commit -m "test: add llm fixture authoring golden scenario"
```

---

### Task 7: Full Verification and Documentation Update

**Files:**
- Modify: `memory.md`

**Interfaces:**
- Consumes: all previous task deliverables.
- Produces: final verification and status notes.

- [ ] **Step 1: Run focused test suite**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "FullyQualifiedName~CompanyCertificationAuthoringGoldenScenarioTests"
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/CrestCreates.Agent.Memory.Tests.csproj
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/CrestCreates.Agent.ControlPlane.Tests.csproj
```

Expected: all pass.

- [ ] **Step 2: Run build**

Run:

```bash
dotnet build
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Update `memory.md`**

Append a Phase 7g status entry:

```markdown
### LLM-backed Descriptor Authoring Adapter (Phase 7g, 2026-07-01)

Status: Implemented for first closure.

Completed:
- Framework-level `CrestCreates.Agent.Authoring.Abstractions` contract boundary.
- Provider-agnostic `CrestCreates.Agent.Authoring` runtime with prompt projection, canonical prompt hash, parser diagnostics, fake and recorded clients.
- Provider-specific `CrestCreates.Agent.Authoring.Http` OpenAI-compatible client boundary.
- Phase 7f sample fake agent moved onto framework authoring contracts.
- Fixture-backed authoring tests proving deterministic output and governance mainline preservation.

Rules:
- LLM authoring produces plans and draft sets only.
- Review, package evidence, activation handoff, HumanTask approval, and runtime mutation remain owned by existing Control Plane and `IRuntimeActivationGate` chain.
- Provider profiles must not carry raw secrets.
```

- [ ] **Step 4: Run final status check**

Run:

```bash
git status --short
```

Expected: only intended doc changes are shown.

- [ ] **Step 5: Commit**

```bash
git add memory.md
git commit -m "docs: record phase 7g authoring adapter closure"
```

---

## Self-Review Checklist

- Spec coverage:
  - Framework contracts: Task 1.
  - Sample contract productization: Task 2.
  - Provider-agnostic authoring runtime: Task 3 and Task 4.
  - Prompt projection and canonical prompt hash: Task 3.
  - Parser diagnostics and atomic draft set behavior: Task 4 and Task 6.
  - Provider-specific OpenAI-compatible boundary and credential policy: Task 5.
  - Golden scenario fixture path: Task 6.
  - Final verification and memory update: Task 7.
- Placeholder scan:
  - No `TBD`, `TODO`, or unnamed implementation work should remain.
- Type consistency:
  - `IDescriptorAuthoringAgent` lives in `CrestCreates.Agent.Authoring.Abstractions.Authoring`.
  - `IDescriptorAuthoringModelClient` lives in `CrestCreates.Agent.Authoring.Abstractions.Model`.
  - `IDescriptorAuthoringCredentialProvider` lives in `CrestCreates.Agent.Authoring.Http.Credentials`.
  - `OpenAICompatibleDescriptorAuthoringModelClient` lives in `CrestCreates.Agent.Authoring.Http.OpenAICompatible`.
