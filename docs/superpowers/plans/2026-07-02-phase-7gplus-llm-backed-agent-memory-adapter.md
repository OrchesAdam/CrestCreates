# Phase 7g+ LLM-backed Agent Memory Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a provider-agnostic, opt-in LLM adapter for Agent Memory compression and candidate extraction while preserving the deterministic Agent Memory lifecycle as the default path.

**Architecture:** Create `CrestCreates.Agent.Memory.Llm` as a thin adapter layer around `IAgentContextCompressor` and `IAgentMemoryExtractor`. The adapter owns prompt inputs, model boundary, parsers, validation, prompt evidence integration, canonical output hashes, and deterministic fallback; it does not own promotion, recall, stores, Control Plane, activation, HTTP providers, or runtime handlers.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Options, System.Text.Json source generation, existing `CrestCreates.Agent.Memory`, `CrestCreates.Agent.Prompting`, and canonical hash infrastructure.

## Global Constraints

- `AddAgentMemoryRuntime()` remains the deterministic default and must still resolve `IAgentContextCompressor -> DefaultAgentContextCompressor` and `IAgentMemoryExtractor -> DefaultAgentMemoryExtractor`.
- Do not change `IAgentContextCompressor` return types: `CompressConversationAsync` and `CompressTaskAsync` must keep returning `ValueTask<AgentCompressedContext>`.
- Do not change `IAgentMemoryExtractor` return type: `ExtractCandidatesAsync` must keep returning `ValueTask<IReadOnlyList<AgentMemoryCandidate>>`.
- Reuse existing diagnostic carriers: `AgentCompressedContext.Diagnostics`, `AgentCompressedContextBlock.Diagnostics`, and `AgentMemoryCandidate.SanitizationDiagnostics`.
- Candidate-level LLM diagnostics reuse `AgentMemoryCandidate.SanitizationDiagnostics` for this phase; do not introduce a parallel candidate diagnostic carrier unless existing tests show the name causes semantic ambiguity.
- Per-adapter registration is authoritative: `AddAgentMemoryLlmCompressor()` always replaces only `IAgentContextCompressor`, and `AddAgentMemoryLlmExtractor()` always replaces only `IAgentMemoryExtractor`.
- Do not add `UseLlmCompressor` or `UseLlmExtractor` runtime flags to `LlmAgentContextCompressor` or `LlmAgentMemoryExtractor`.
- Register deterministic concrete fallback services as the same instances used by the default interfaces: `TryAddSingleton<DefaultAgentContextCompressor>()`, `TryAddSingleton<IAgentContextCompressor>(sp => sp.GetRequiredService<DefaultAgentContextCompressor>())`, and the equivalent extractor registration.
- When fallback is used, deterministic output must be augmented with LLM failure/fallback diagnostics without mutating source identity or sanitized content hash incorrectly.
- `CompressedOutputHash` and `CandidateOutputHash` use `CanonicalHashPurposeNames.SourceIdentity`.
- Add governed artifact-name constants: `CanonicalHashArtifactNames.AgentMemoryCompressedOutput` and `CanonicalHashArtifactNames.AgentMemoryCandidateOutput`; do not add enum-based artifact kinds.
- Prompt output evidence hash uses `CanonicalHashPurposeNames.AuditEvidence` and excludes raw provider `ResponseText`.
- `RecordedAgentMemoryLlmModelClient` matches fixtures by `PromptInputHash`, `TemplateId`, `TemplateVersion`, `ModelProfileRef`, and `ProviderProfileRef`; missing fixtures return provider failure, never empty success.
- `CrestCreates.Agent.Memory.Llm` must not reference ControlPlane, ControlPlane.Abstractions, Activation, Authoring.Http, Platform, Framework Api/Web, persistence providers, or runtime handler implementation projects.
- Do not implement `CrestCreates.Agent.Memory.Llm.Http` in this phase.

---

## File Structure

Create:

```text
src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/
  AgentMemoryLlmServiceCollectionExtensions.cs
  AgentMemoryLlmAdapterOptions.cs
  AgentMemoryLlmAdapterSelection.cs
  CanonicalHashing/AgentMemoryLlmCanonicalHashProjector.cs
  Clients/FakeAgentMemoryLlmModelClient.cs
  Clients/RecordedAgentMemoryLlmModelClient.cs
  Compression/IAgentMemoryCompressionPromptBuilder.cs
  Compression/DefaultAgentMemoryCompressionPromptBuilder.cs
  Compression/IAgentMemoryCompressionOutputParser.cs
  Compression/JsonAgentMemoryCompressionOutputParser.cs
  Compression/LlmAgentContextCompressor.cs
  Extraction/IAgentMemoryExtractionPromptBuilder.cs
  Extraction/DefaultAgentMemoryExtractionPromptBuilder.cs
  Extraction/IAgentMemoryExtractionOutputParser.cs
  Extraction/JsonAgentMemoryExtractionOutputParser.cs
  Extraction/LlmAgentMemoryExtractor.cs
  Json/AgentMemoryLlmJsonSerializerContext.cs
  Model/AgentMemoryLlmModelContracts.cs
  Model/IAgentMemoryLlmModelClient.cs
  Prompting/AgentMemoryCompressionPromptInput.cs
  Prompting/AgentMemoryCompressionPromptInputProjector.cs
  Prompting/AgentMemoryExtractionPromptInput.cs
  Prompting/AgentMemoryExtractionPromptInputProjector.cs
  Prompting/AgentMemoryLlmModelResponseEvidenceProjection.cs
  Prompting/AgentMemoryLlmModelResponseEvidenceProjector.cs
  Validation/AgentMemoryLlmDiagnosticCodes.cs
  Validation/AgentMemoryLlmDiagnostics.cs
  Validation/AgentMemoryLlmOutputValidators.cs
  CrestCreates.Agent.Memory.Llm.csproj

tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/
  AgentMemoryLlmTestData.cs
  BoundaryTests.cs
  CompressionAdapterTests.cs
  ExtractionAdapterTests.cs
  PromptEvidenceTests.cs
  RecordedClientTests.cs
  ServiceCollectionTests.cs
  CrestCreates.Agent.Memory.Llm.Tests.csproj
```

Modify:

```text
src/Runtime/Agent/CrestCreates.Agent.Memory/AgentMemoryServiceCollectionExtensions.cs
src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashing/CanonicalHashArtifactNames.cs
```

No other production project should require modification. If a task appears to need broad `Agent.Memory.Abstractions` changes, stop and re-check the spec before continuing.

---

### Task 1: Project Scaffold, DI Fallback Instance Registration, and Boundary Tests

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/CrestCreates.Agent.Memory.Llm.csproj`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/AgentMemoryLlmAdapterOptions.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/AgentMemoryLlmAdapterSelection.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/CrestCreates.Agent.Memory.Llm.Tests.csproj`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/BoundaryTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/ServiceCollectionTests.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory/AgentMemoryServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: `AddAgentMemoryRuntime()`, `DefaultAgentContextCompressor`, `DefaultAgentMemoryExtractor`.
- Produces: project shell, adapter options, adapter selection enum, boundary tests, concrete deterministic fallback registrations.

- [ ] **Step 1: Create failing DI and boundary tests**

Create `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/ServiceCollectionTests.cs`:

```csharp
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Extraction;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public sealed class ServiceCollectionTests
{
    [Fact]
    public void AddAgentMemoryRuntime_UsesSameConcreteFallbackInstances_AsDefaultInterfaces()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentMemoryRuntime();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAgentContextCompressor>()
            .Should().BeSameAs(provider.GetRequiredService<DefaultAgentContextCompressor>());
        provider.GetRequiredService<IAgentMemoryExtractor>()
            .Should().BeSameAs(provider.GetRequiredService<DefaultAgentMemoryExtractor>());
    }
}
```

Create `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/BoundaryTests.cs`:

```csharp
using CrestCreates.Agent.Memory;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public sealed class BoundaryTests
{
    [Fact]
    public void AgentMemoryRuntime_DoesNotReference_MemoryLlm()
    {
        typeof(AgentMemoryServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain("CrestCreates.Agent.Memory.Llm");
    }

    [Fact]
    public void AgentMemoryLlm_DoesNotReference_ForbiddenAgentSurfaces()
    {
        typeof(AgentMemoryLlmAdapterOptions).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.ControlPlane",
                "CrestCreates.Agent.ControlPlane.Abstractions",
                "CrestCreates.Agent.Authoring.Http",
                "CrestCreates.Platform.Agent",
                "CrestCreates.AspNetCore",
                "CrestCreates.DynamicApi"
            });
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter "FullyQualifiedName~ServiceCollectionTests|FullyQualifiedName~BoundaryTests"
```

Expected: FAIL because `CrestCreates.Agent.Memory.Llm.Tests` project and `AgentMemoryLlmAdapterOptions` do not exist yet.

- [ ] **Step 3: Create Memory.Llm project and test project**

Create `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/CrestCreates.Agent.Memory.Llm.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Agent.Memory.Llm</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Memory.Llm</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj" />
    <ProjectReference Include="../CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj" />
    <ProjectReference Include="../CrestCreates.Agent.Prompting.Abstractions/CrestCreates.Agent.Prompting.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
  </ItemGroup>
</Project>
```

Create `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/CrestCreates.Agent.Memory.Llm.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>CrestCreates.Agent.Memory.Llm.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Memory.Llm.Tests</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../../../src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/CrestCreates.Agent.Memory.Llm.csproj" />
    <ProjectReference Include="../../../../src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj" />
    <ProjectReference Include="../../../../src/Runtime/Agent/CrestCreates.Agent.Prompting/CrestCreates.Agent.Prompting.csproj" />
    <ProjectReference Include="../../../../src/Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Moq" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add options and selection enum**

Create `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/AgentMemoryLlmAdapterSelection.cs`:

```csharp
namespace CrestCreates.Agent.Memory.Llm;

[Flags]
public enum AgentMemoryLlmAdapterSelection
{
    None = 0,
    Compressor = 1,
    Extractor = 2,
    Both = Compressor | Extractor
}
```

Create `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/AgentMemoryLlmAdapterOptions.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Memory.Llm;

public sealed class AgentMemoryLlmAdapterOptions
{
    public bool EnableDeterministicFallback { get; set; } = true;
    public int MaxCompressedBlockCount { get; set; } = 32;
    public int MaxCompressedBlockCharacters { get; set; } = 4_000;
    public int MaxCandidateCount { get; set; } = 16;
    public int MaxCandidateCharacters { get; set; } = 2_000;
    public AgentMemoryConfidence MaxCandidateConfidence { get; set; } = AgentMemoryConfidence.Medium;
    public AgentPromptTemplateId CompressionTemplateId { get; set; } = new("agent-memory.compression.default");
    public AgentPromptVersion CompressionTemplateVersion { get; set; } = new("7gplus.v1");
    public AgentPromptTemplateId ExtractionTemplateId { get; set; } = new("agent-memory.extraction.default");
    public AgentPromptVersion ExtractionTemplateVersion { get; set; } = new("7gplus.v1");
    public AgentPromptContractVersion PromptContractVersion { get; set; } = new("agent-memory-llm.v1");
    public AgentPromptModelProfileRef ModelProfileRef { get; set; } = new("agent-memory-llm.default");
    public AgentPromptProviderProfileRef ProviderProfileRef { get; set; } = new("recorded");
}
```

Do not add `UseLlmCompressor` or `UseLlmExtractor` properties.

- [ ] **Step 5: Modify deterministic fallback concrete registrations**

Modify `src/Runtime/Agent/CrestCreates.Agent.Memory/AgentMemoryServiceCollectionExtensions.cs` so compression/extraction registrations use shared concrete instances:

```csharp
// Sanitization & Compression
services.TryAddSingleton<IAgentMemoryContentSanitizer, DefaultAgentMemoryContentSanitizer>();
services.TryAddSingleton<DefaultAgentContextCompressor>();
services.TryAddSingleton<IAgentContextCompressor>(sp =>
    sp.GetRequiredService<DefaultAgentContextCompressor>());

// Extraction & Promotion
services.TryAddSingleton<DefaultAgentMemoryExtractor>();
services.TryAddSingleton<IAgentMemoryExtractor>(sp =>
    sp.GetRequiredService<DefaultAgentMemoryExtractor>());
services.TryAddSingleton<IAgentMemoryPromotionService, DefaultAgentMemoryPromotionService>();
```

- [ ] **Step 6: Run task tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter "FullyQualifiedName~ServiceCollectionTests|FullyQualifiedName~BoundaryTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory.Llm tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests src/Runtime/Agent/CrestCreates.Agent.Memory/AgentMemoryServiceCollectionExtensions.cs
git commit -m "feat(agent-memory): scaffold LLM adapter project"
```

---

### Task 2: Model Contracts, Recorded/Fake Clients, JSON Context, and Prompt Evidence Projectors

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Model/AgentMemoryLlmModelContracts.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Model/IAgentMemoryLlmModelClient.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Clients/FakeAgentMemoryLlmModelClient.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Clients/RecordedAgentMemoryLlmModelClient.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Prompting/AgentMemoryCompressionPromptInput.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Prompting/AgentMemoryExtractionPromptInput.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Prompting/AgentMemoryLlmModelResponseEvidenceProjection.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Prompting/AgentMemoryCompressionPromptInputProjector.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Prompting/AgentMemoryExtractionPromptInputProjector.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Prompting/AgentMemoryLlmModelResponseEvidenceProjector.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Json/AgentMemoryLlmJsonSerializerContext.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/PromptEvidenceTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/RecordedClientTests.cs`

**Interfaces:**
- Consumes: `IAgentPromptCanonicalPayloadProjector<TPayload>`, `AgentPromptEvidenceCreationRequest<TPayload>`.
- Produces: model client boundary and evidence projection types used by compressor/extractor tasks.

- [ ] **Step 1: Write failing recorded client and prompt evidence tests**

Create `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/RecordedClientTests.cs`:

```csharp
using CrestCreates.Agent.Memory.Llm.Clients;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public sealed class RecordedClientTests
{
    [Fact]
    public async Task MissingFixture_ReturnsProviderUnavailable_NotEmptySuccess()
    {
        var client = new RecordedAgentMemoryLlmModelClient(Array.Empty<RecordedAgentMemoryLlmFixture>());
        var response = await client.CompleteAsync(Request("hash-missing"));

        response.ResponseText.Should().BeNull();
        response.FailureKind.Should().Be(AgentMemoryLlmProviderFailureKind.ProviderUnavailable);
        response.FailureDetail.Should().Contain("MissingRecordedFixture");
    }

    [Fact]
    public async Task FixtureMatch_UsesPromptHashTemplateAndProfileRefs()
    {
        var fixture = new RecordedAgentMemoryLlmFixture(
            PromptInputHash: "hash-1",
            TemplateId: "agent-memory.compression.default",
            TemplateVersion: "7gplus.v1",
            ModelProfileRef: "model-a",
            ProviderProfileRef: "provider-a",
            ResponseText: """{"blocks":[]}""",
            ProviderName: "recorded",
            ModelName: "model-a");

        var client = new RecordedAgentMemoryLlmModelClient([fixture]);
        var response = await client.CompleteAsync(Request("hash-1"));

        response.ResponseText.Should().Be("""{"blocks":[]}""");
        response.FailureKind.Should().BeNull();
    }

    private static AgentMemoryLlmModelRequest Request(string promptInputHash) => new()
    {
        PromptText = "prompt",
        PromptInputEvidence = new AgentPromptInputEvidenceSummary
        {
            TemplateId = new AgentPromptTemplateId("agent-memory.compression.default"),
            TemplateVersion = new AgentPromptVersion("7gplus.v1"),
            Purpose = AgentPromptPurpose.MemoryCompression,
            ContractVersion = new AgentPromptContractVersion("agent-memory-llm.v1"),
            ModelProfileRef = new AgentPromptModelProfileRef("model-a"),
            ProviderProfileRef = new AgentPromptProviderProfileRef("provider-a"),
            InputHash = Hash(promptInputHash),
            CreatedAt = DateTimeOffset.UnixEpoch
        }
    };

    private static CanonicalHash Hash(string value) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = CanonicalHashArtifactNames.AgentPromptInputEvidence,
        Purpose = CanonicalHashPurposeNames.SourceIdentity,
        Scope = CanonicalHashScopeNames.InternalFull,
        ContractVersion = "descriptor-hash-v1",
        CanonicalShapeVersion = "agent-prompt-input-evidence-v1"
    };
}
```

Create `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/PromptEvidenceTests.cs` with assertions:

```csharp
using System.Text.Json;
using CrestCreates.Agent.Memory.Llm.Prompting;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public sealed class PromptEvidenceTests
{
    [Fact]
    public void OutputEvidenceHash_ExcludesRawProviderResponseText()
    {
        typeof(AgentMemoryLlmModelResponseEvidenceProjection)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain("ResponseText");

        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<AgentMemoryLlmModelResponseEvidenceProjection>, AgentMemoryLlmModelResponseEvidenceProjector>();
        using var provider = services.BuildServiceProvider();

        var hashService = provider.GetRequiredService<IAgentPromptHashService>();
        var inputHash = Hash("input-hash");

        var safeA = new AgentMemoryLlmModelResponseEvidenceProjection
        {
            ProviderName = "provider",
            ModelName = "model",
            PromptInputHash = inputHash.Value,
            FailureKind = null,
            FailureDetail = null
        };

        var hashA = hashService.ComputeOutputHash(Request(safeA), inputHash, new AgentPromptProviderObservation { ProviderName = "provider", ModelName = "model" });
        var hashB = hashService.ComputeOutputHash(Request(safeA), inputHash, new AgentPromptProviderObservation { ProviderName = "provider", ModelName = "model" });

        hashA.Should().NotBeNull();
        hashA!.Value.Should().Be(hashB!.Value);
        hashA.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
    }

    private static AgentPromptEvidenceCreationRequest<AgentMemoryLlmModelResponseEvidenceProjection> Request(AgentMemoryLlmModelResponseEvidenceProjection payload) => new()
    {
        TemplateId = new AgentPromptTemplateId("agent-memory.compression.default"),
        TemplateVersion = new AgentPromptVersion("7gplus.v1"),
        Purpose = AgentPromptPurpose.MemoryCompression,
        ContractVersion = new AgentPromptContractVersion("agent-memory-llm.v1"),
        ModelProfileRef = new AgentPromptModelProfileRef("model-a"),
        ProviderProfileRef = new AgentPromptProviderProfileRef("provider-a"),
        Payload = payload
    };

    private static CanonicalHash Hash(string value) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = CanonicalHashArtifactNames.AgentPromptInputEvidence,
        Purpose = CanonicalHashPurposeNames.SourceIdentity,
        Scope = CanonicalHashScopeNames.InternalFull,
        ContractVersion = "descriptor-hash-v1",
        CanonicalShapeVersion = "agent-prompt-input-evidence-v1"
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter "FullyQualifiedName~RecordedClientTests|FullyQualifiedName~PromptEvidenceTests"
```

Expected: FAIL because model contracts, recorded client, and projectors do not exist.

- [ ] **Step 3: Implement model contracts and clients**

Create `Model/AgentMemoryLlmModelContracts.cs`:

```csharp
using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Memory.Llm.Model;

public enum AgentMemoryLlmProviderFailureKind
{
    ProviderUnavailable = 1,
    CredentialUnavailable = 2,
    Unauthorized = 3,
    RateLimited = 4,
    Timeout = 5,
    NetworkError = 6,
    ParseFailed = 7,
    ValidationFailed = 8
}

public sealed record AgentMemoryLlmModelRequest
{
    public required string PromptText { get; init; }
    public required AgentPromptInputEvidenceSummary PromptInputEvidence { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record AgentMemoryLlmModelResponse
{
    public string? ResponseText { get; init; }
    public string? ProviderName { get; init; }
    public string? ModelName { get; init; }
    public AgentMemoryLlmProviderFailureKind? FailureKind { get; init; }
    public string? FailureDetail { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record RecordedAgentMemoryLlmFixture(
    string PromptInputHash,
    string TemplateId,
    string TemplateVersion,
    string ModelProfileRef,
    string ProviderProfileRef,
    string ResponseText,
    string? ProviderName,
    string? ModelName);
```

Create `Model/IAgentMemoryLlmModelClient.cs`:

```csharp
namespace CrestCreates.Agent.Memory.Llm.Model;

public interface IAgentMemoryLlmModelClient
{
    Task<AgentMemoryLlmModelResponse> CompleteAsync(
        AgentMemoryLlmModelRequest request,
        CancellationToken cancellationToken = default);
}
```

Create `Clients/RecordedAgentMemoryLlmModelClient.cs`:

```csharp
using CrestCreates.Agent.Memory.Llm.Model;

namespace CrestCreates.Agent.Memory.Llm.Clients;

public sealed class RecordedAgentMemoryLlmModelClient : IAgentMemoryLlmModelClient
{
    private readonly IReadOnlyList<RecordedAgentMemoryLlmFixture> _fixtures;

    public RecordedAgentMemoryLlmModelClient(IReadOnlyList<RecordedAgentMemoryLlmFixture> fixtures)
    {
        _fixtures = fixtures;
    }

    public Task<AgentMemoryLlmModelResponse> CompleteAsync(
        AgentMemoryLlmModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var evidence = request.PromptInputEvidence;
        var fixture = _fixtures.FirstOrDefault(item =>
            item.PromptInputHash == evidence.InputHash.Value &&
            item.TemplateId == evidence.TemplateId.Value &&
            item.TemplateVersion == evidence.TemplateVersion.Value &&
            item.ModelProfileRef == evidence.ModelProfileRef.Value &&
            item.ProviderProfileRef == evidence.ProviderProfileRef.Value);

        if (fixture is null)
        {
            return Task.FromResult(new AgentMemoryLlmModelResponse
            {
                FailureKind = AgentMemoryLlmProviderFailureKind.ProviderUnavailable,
                FailureDetail = $"MissingRecordedFixture: {evidence.TemplateId.Value}/{evidence.TemplateVersion.Value}/{evidence.InputHash.Value}"
            });
        }

        return Task.FromResult(new AgentMemoryLlmModelResponse
        {
            ResponseText = fixture.ResponseText,
            ProviderName = fixture.ProviderName,
            ModelName = fixture.ModelName
        });
    }
}
```

Create `Clients/FakeAgentMemoryLlmModelClient.cs`:

```csharp
using CrestCreates.Agent.Memory.Llm.Model;

namespace CrestCreates.Agent.Memory.Llm.Clients;

public sealed class FakeAgentMemoryLlmModelClient : IAgentMemoryLlmModelClient
{
    private readonly Queue<AgentMemoryLlmModelResponse> _responses = new();
    public IReadOnlyList<AgentMemoryLlmModelRequest> Requests => _requests;
    private readonly List<AgentMemoryLlmModelRequest> _requests = new();

    public FakeAgentMemoryLlmModelClient(params AgentMemoryLlmModelResponse[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue(response);
        }
    }

    public Task<AgentMemoryLlmModelResponse> CompleteAsync(
        AgentMemoryLlmModelRequest request,
        CancellationToken cancellationToken = default)
    {
        _requests.Add(request);
        if (_responses.Count == 0)
        {
            return Task.FromResult(new AgentMemoryLlmModelResponse
            {
                FailureKind = AgentMemoryLlmProviderFailureKind.ProviderUnavailable,
                FailureDetail = "Fake response queue is empty."
            });
        }

        return Task.FromResult(_responses.Dequeue());
    }
}
```

- [ ] **Step 4: Implement prompt inputs and projectors**

Create prompt input records with sanitized-only content:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Memory.Llm.Prompting;

public sealed record AgentMemoryCompressionPromptSource
{
    public required string SourceRefId { get; init; }
    public required string SanitizedContent { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public IReadOnlyList<string> RedactionKinds { get; init; } = Array.Empty<string>();
}

public sealed record AgentMemoryCompressionPromptInput
{
    public required string TenantId { get; init; }
    public required IReadOnlyList<AgentMemoryCompressionPromptSource> Sources { get; init; }
    public required int MaxOutputCharacters { get; init; }
    public string? Purpose { get; init; }
}

public sealed record AgentMemoryExtractionPromptInput
{
    public required string TenantId { get; init; }
    public required IReadOnlyList<AgentCompressedContextBlock> Blocks { get; init; }
    public required int MaxCandidateCount { get; init; }
    public string? Purpose { get; init; }
}
```

Create `AgentMemoryLlmModelResponseEvidenceProjection.cs`:

```csharp
namespace CrestCreates.Agent.Memory.Llm.Prompting;

public sealed record AgentMemoryLlmModelResponseEvidenceProjection
{
    public string? ProviderName { get; init; }
    public string? ModelName { get; init; }
    public string? PromptInputHash { get; init; }
    public string? FailureKind { get; init; }
    public string? FailureDetail { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
```

Implement projectors using deterministic ordering and no raw response fields. The response projector must not contain a `ResponseText` property.

- [ ] **Step 5: Add JSON context**

Create `Json/AgentMemoryLlmJsonSerializerContext.cs`:

```csharp
using System.Text.Json.Serialization;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Memory.Llm.Prompting;

namespace CrestCreates.Agent.Memory.Llm.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentMemoryLlmModelRequest))]
[JsonSerializable(typeof(AgentMemoryLlmModelResponse))]
[JsonSerializable(typeof(AgentMemoryCompressionPromptInput))]
[JsonSerializable(typeof(AgentMemoryExtractionPromptInput))]
[JsonSerializable(typeof(AgentMemoryLlmModelResponseEvidenceProjection))]
public sealed partial class AgentMemoryLlmJsonSerializerContext : JsonSerializerContext;
```

- [ ] **Step 6: Run task tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter "FullyQualifiedName~RecordedClientTests|FullyQualifiedName~PromptEvidenceTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory.Llm tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests
git commit -m "feat(agent-memory): add LLM model and prompt evidence contracts"
```

---

### Task 3: Prompt Builders, Provider DTOs, Parsers, Diagnostics, and Validators

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Compression/IAgentMemoryCompressionPromptBuilder.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Compression/DefaultAgentMemoryCompressionPromptBuilder.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Compression/IAgentMemoryCompressionOutputParser.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Compression/JsonAgentMemoryCompressionOutputParser.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Extraction/IAgentMemoryExtractionPromptBuilder.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Extraction/DefaultAgentMemoryExtractionPromptBuilder.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Extraction/IAgentMemoryExtractionOutputParser.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Extraction/JsonAgentMemoryExtractionOutputParser.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Validation/AgentMemoryLlmDiagnosticCodes.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Validation/AgentMemoryLlmDiagnostics.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Validation/AgentMemoryLlmOutputValidators.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Json/AgentMemoryLlmJsonSerializerContext.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/CompressionAdapterTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/ExtractionAdapterTests.cs`

**Interfaces:**
- Consumes: prompt input records and model response contracts from Task 2.
- Produces: parse/validation result types and diagnostics used by adapters in Tasks 5 and 6.

- [ ] **Step 1: Write failing parser and malicious output tests**

Add parser-focused tests to `CompressionAdapterTests.cs`:

```csharp
[Fact]
public void CompressionParser_InvalidSourceRef_ReturnsValidationDiagnostic()
{
    var parser = new JsonAgentMemoryCompressionOutputParser();
    var json = """{"blocks":[{"blockId":"b1","content":"summary","sourceRefIds":["unknown"]}]}""";

    var result = parser.Parse(json, ["known"]);

    result.IsValid.Should().BeFalse();
    result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.InvalidSourceRef);
}
```

Add parser-focused tests to `ExtractionAdapterTests.cs`:

```csharp
[Fact]
public void ExtractionParser_ProviderOutputWithActiveStatus_IsRejected()
{
    var parser = new JsonAgentMemoryExtractionOutputParser();
    var json = """{"candidates":[{"candidateId":"c1","kind":"ProjectFact","content":"fact","confidence":"High","status":"Active","sourceRefIds":["block-1"]}]}""";

    var result = parser.Parse(json, ["block-1"]);

    result.IsValid.Should().BeFalse();
    result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.NonAuthoritativeOutputEnforced);
}
```

- [ ] **Step 2: Run parser tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter "FullyQualifiedName~CompressionParser|FullyQualifiedName~ExtractionParser"
```

Expected: FAIL because parser and diagnostics do not exist.

- [ ] **Step 3: Implement diagnostic codes and diagnostic factory**

Create `Validation/AgentMemoryLlmDiagnosticCodes.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Llm.Validation;

public static class AgentMemoryLlmDiagnosticCodes
{
    public static DiagnosticCode ProviderUnavailable { get; } = new("AGENT_MEMORY_LLM_PROVIDER_UNAVAILABLE");
    public static DiagnosticCode ProviderReturnedEmptyOutput { get; } = new("AGENT_MEMORY_LLM_PROVIDER_RETURNED_EMPTY_OUTPUT");
    public static DiagnosticCode ParseFailed { get; } = new("AGENT_MEMORY_LLM_PARSE_FAILED");
    public static DiagnosticCode InvalidSourceRef { get; } = new("AGENT_MEMORY_LLM_INVALID_SOURCE_REF");
    public static DiagnosticCode RedactionMetadataMissing { get; } = new("AGENT_MEMORY_LLM_REDACTION_METADATA_MISSING");
    public static DiagnosticCode FallbackToDeterministicCompressor { get; } = new("AGENT_MEMORY_LLM_FALLBACK_TO_DETERMINISTIC_COMPRESSOR");
    public static DiagnosticCode FallbackToDeterministicExtractor { get; } = new("AGENT_MEMORY_LLM_FALLBACK_TO_DETERMINISTIC_EXTRACTOR");
    public static DiagnosticCode NonAuthoritativeOutputEnforced { get; } = new("AGENT_MEMORY_LLM_NON_AUTHORITATIVE_OUTPUT_ENFORCED");
    public static DiagnosticCode CandidateConfidenceCapped { get; } = new("AGENT_MEMORY_LLM_CANDIDATE_CONFIDENCE_CAPPED");
    public static DiagnosticCode PromotionRequiredBeforeRecall { get; } = new("AGENT_MEMORY_LLM_PROMOTION_REQUIRED_BEFORE_RECALL");
    public static DiagnosticCode UntrustedOutputSkipped { get; } = new("AGENT_MEMORY_LLM_UNTRUSTED_OUTPUT_SKIPPED");
}
```

Create `Validation/AgentMemoryLlmDiagnostics.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Llm.Validation;

public static class AgentMemoryLlmDiagnostics
{
    public static AgentMemoryDiagnostic Create(
        DiagnosticCode code,
        string message,
        SeverityLevel severity = SeverityLevel.Warning,
        IReadOnlyList<AgentContextSourceRef>? sourceRefs = null)
    {
        return new AgentMemoryDiagnostic
        {
            Code = code,
            Message = message,
            Severity = severity,
            SourceRefs = sourceRefs ?? Array.Empty<AgentContextSourceRef>()
        };
    }
}
```

- [ ] **Step 4: Implement DTOs, parsers, and validators**

Implement provider DTOs as internal records in parser files:

```csharp
internal sealed record AgentMemoryCompressionProviderOutputDto(
    IReadOnlyList<AgentMemoryCompressedBlockDto>? Blocks);

internal sealed record AgentMemoryCompressedBlockDto(
    string? BlockId,
    string? Content,
    IReadOnlyList<string>? SourceRefIds,
    IReadOnlyList<string>? RedactionKinds);
```

Return a parse result record:

```csharp
public sealed record AgentMemoryCompressionParseResult(
    bool IsValid,
    IReadOnlyList<AgentMemoryCompressedBlockDto> Blocks,
    IReadOnlyList<AgentMemoryDiagnostic> Diagnostics);
```

Use `JsonSerializer.Deserialize` with `AgentMemoryLlmJsonSerializerContext`. Reject unknown source refs against the allowed id set passed into the parser. Do not create domain objects in parsers.

For extraction, implement equivalent provider DTOs and reject `Status` values other than null or `Candidate`, reject `IsAuthoritative=true`, reject unknown tenant/source refs, and cap confidence later in validators.

- [ ] **Step 5: Implement prompt builders**

Compression prompt builder output can be a string; keep it deterministic and JSON-only:

```csharp
public interface IAgentMemoryCompressionPromptBuilder
{
    string Build(AgentMemoryCompressionPromptInput input);
}
```

The prompt must include instructions:

```text
Use sanitized content only.
Do not invent facts.
Preserve sourceRefId exactly.
Preserve redaction markers.
Return JSON only with blocks[].
```

Extraction builder follows the same pattern and asks for JSON `candidates[]`.

- [ ] **Step 6: Run parser tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter "FullyQualifiedName~CompressionParser|FullyQualifiedName~ExtractionParser"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory.Llm tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests
git commit -m "feat(agent-memory): add LLM prompt parsers and validators"
```

---

### Task 4: Canonical Output Hash Support

**Files:**
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashing/CanonicalHashArtifactNames.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/CanonicalHashing/AgentMemoryLlmCanonicalHashProjector.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/CanonicalHashTests.cs`

**Interfaces:**
- Consumes: `ICanonicalHashComputer`, `CanonicalHashProjectionResult`, `CanonicalHashArtifactNames`, `CanonicalHashPurposeNames`.
- Produces: `ComputeCompressedOutputHash(...)`, `ComputeCandidateOutputHash(...)`.

- [ ] **Step 1: Write failing canonical hash tests**

Create `CanonicalHashTests.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public sealed class CanonicalHashTests
{
    [Fact]
    public void CompressedOutputHash_UsesAgentMemoryCompressedOutput_SourceIdentity()
    {
        var projector = new AgentMemoryLlmCanonicalHashProjector(new DefaultCanonicalHashComputer());
        var hash = projector.ComputeCompressedOutputHash("tenant-1", "template", "input-hash", [Block("b1", "summary")]);

        hash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.AgentMemoryCompressedOutput);
        hash.Purpose.Should().Be(CanonicalHashPurposeNames.SourceIdentity);
    }

    [Fact]
    public void CandidateOutputHash_UsesAgentMemoryCandidateOutput_SourceIdentity()
    {
        var projector = new AgentMemoryLlmCanonicalHashProjector(new DefaultCanonicalHashComputer());
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "c1",
            TenantId = "tenant-1",
            Kind = AgentMemoryKind.ProjectFact,
            Content = "fact",
            Confidence = AgentMemoryConfidence.Medium,
            CanonicalContentHash = Hash("content")
        };

        var hash = projector.ComputeCandidateOutputHash("template", "input-hash", candidate);

        hash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.AgentMemoryCandidateOutput);
        hash.Purpose.Should().Be(CanonicalHashPurposeNames.SourceIdentity);
    }

    private static AgentCompressedContextBlock Block(string id, string content) => new()
    {
        BlockId = id,
        TenantId = "tenant-1",
        Content = content,
        CanonicalContentHash = Hash(content)
    };

    private static CanonicalHash Hash(string value) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = CanonicalHashArtifactNames.AgentMemoryContent,
        Purpose = CanonicalHashPurposeNames.SourceIdentity,
        Scope = CanonicalHashScopeNames.InternalFull,
        ContractVersion = "memory-hash-v1",
        CanonicalShapeVersion = "memory-content-hash-v1"
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter FullyQualifiedName~CanonicalHashTests
```

Expected: FAIL because artifact names and projector do not exist.

- [ ] **Step 3: Add artifact names**

Modify `CanonicalHashArtifactNames.cs` to add constants:

```csharp
public const string AgentMemoryCompressedOutput = "AgentMemoryCompressedOutput";
public const string AgentMemoryCandidateOutput = "AgentMemoryCandidateOutput";
```

Use the file's existing constant style.

- [ ] **Step 4: Implement canonical hash projector**

Create `AgentMemoryLlmCanonicalHashProjector.cs` with methods:

```csharp
public CanonicalHash ComputeCompressedOutputHash(
    string tenantId,
    string compressionTemplateIdentity,
    string promptInputHash,
    IReadOnlyList<AgentCompressedContextBlock> blocks)
```

and:

```csharp
public CanonicalHash ComputeCandidateOutputHash(
    string extractionTemplateIdentity,
    string promptInputHash,
    AgentMemoryCandidate candidate)
```

Both must use `CanonicalHashPurposeNames.SourceIdentity`, `CanonicalHashScopeNames.InternalFull`, `DefaultCanonicalHashComputer.AlgorithmVersion`, and deterministic ordering. Do not include random runtime ids except stable semantic block/candidate ids that are derived from input source identity.

- [ ] **Step 5: Run canonical hash tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter FullyQualifiedName~CanonicalHashTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashing/CanonicalHashArtifactNames.cs src/Runtime/Agent/CrestCreates.Agent.Memory.Llm tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests
git commit -m "feat(agent-memory): add LLM canonical output hashes"
```

---

### Task 5: LLM Compression Adapter with Sanitized-only Prompting and Fallback Diagnostics

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Compression/LlmAgentContextCompressor.cs`
- Extend: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/CompressionAdapterTests.cs`

**Interfaces:**
- Consumes: `IAgentContextCompressor`, `IAgentMemoryContentSanitizer`, `DefaultAgentContextCompressor`, `IAgentPromptEvidenceFactory`, prompt builder, model client, parser, canonical hash projector.
- Produces: `LlmAgentContextCompressor`.

- [ ] **Step 1: Add failing sanitized-only and fallback tests**

Add tests:

```csharp
[Fact]
public async Task LlmCompressor_UsesSanitizedContentOnly()
{
    var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
    {
        ResponseText = """{"blocks":[{"blockId":"b1","content":"sanitized summary","sourceRefIds":["conv-1_turn-1"]}]}"""
    });
    var compressor = AgentMemoryLlmTestData.Compressor(client);
    var conversation = AgentMemoryLlmTestData.ConversationWithSecret("raw-secret-token");

    await compressor.CompressConversationAsync(conversation);

    client.Requests.Should().ContainSingle();
    client.Requests[0].PromptText.Should().NotContain("raw-secret-token");
}

[Fact]
public async Task LlmCompressor_ParseFailure_FallsBackAndAddsContextDiagnostic()
{
    var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
    {
        ResponseText = "not-json"
    });
    var compressor = AgentMemoryLlmTestData.Compressor(client);

    var result = await compressor.CompressConversationAsync(AgentMemoryLlmTestData.Conversation("conv-1", "tenant-1", "hello"));

    result.Blocks.Should().NotBeEmpty();
    result.Diagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicCompressor);
}
```

Also add malicious output tests:

```csharp
[Fact]
public async Task LlmCompressor_ProviderOutputDropsRedactionMarker_IsRejectedAndFallbacks()

[Fact]
public async Task LlmCompressor_ProviderOutputReferencesRawSecret_IsRejectedOrSanitized()
```

These tests must assert that fallback diagnostics are visible on `AgentCompressedContext.Diagnostics` and sanitized content hash values remain those produced by the deterministic sanitizer.

- [ ] **Step 2: Run compression tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter FullyQualifiedName~LlmCompressor
```

Expected: FAIL because `LlmAgentContextCompressor` does not exist.

- [ ] **Step 3: Implement compressor**

Implement `LlmAgentContextCompressor` with exact interface signatures:

```csharp
public sealed class LlmAgentContextCompressor : IAgentContextCompressor
{
    public ValueTask<AgentCompressedContext> CompressConversationAsync(
        AgentConversationRecord conversation,
        CancellationToken cancellationToken = default);

    public ValueTask<AgentCompressedContext> CompressTaskAsync(
        AgentTaskRecord task,
        CancellationToken cancellationToken = default);
}
```

Rules:

- Sanitize raw turn/event content with `IAgentMemoryContentSanitizer` before prompt construction.
- Build source ids deterministically from current input, such as `${conversationId}_${turnId}` and `${taskId}_${eventId}`.
- Create prompt input evidence before calling model client.
- Build output evidence with `AgentMemoryLlmModelResponseEvidenceProjection`.
- On provider failure, empty response, parse failure, validation failure, unknown source ref, redaction marker loss, or raw secret reintroduction, call `DefaultAgentContextCompressor`.
- Augment fallback output using `result with { Diagnostics = result.Diagnostics.Concat(fallbackDiagnostics).ToArray() }`.
- Do not alter fallback block content, source refs, or canonical content hashes.

- [ ] **Step 4: Add test helper**

Create `AgentMemoryLlmTestData.cs` with reusable factory methods for services, canonical hash computer, sanitizer, fallback compressor, prompt evidence factory, and sample conversation/task records. Keep this test-only helper inside the test project.

- [ ] **Step 5: Run compression tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter FullyQualifiedName~LlmCompressor
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory.Llm tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests
git commit -m "feat(agent-memory): add LLM context compressor"
```

---

### Task 6: LLM Extraction Adapter with Candidate Lifecycle Guards and Fallback Diagnostics

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/Extraction/LlmAgentMemoryExtractor.cs`
- Extend: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/ExtractionAdapterTests.cs`

**Interfaces:**
- Consumes: `IAgentMemoryExtractor`, `DefaultAgentMemoryExtractor`, prompt evidence factory, prompt builder, model client, parser, canonical hash projector.
- Produces: `LlmAgentMemoryExtractor`.

- [ ] **Step 1: Add failing extractor tests**

Add tests:

```csharp
[Fact]
public async Task LlmExtractor_CandidatesRemainCandidateStatus_AndReuseSanitizationDiagnostics()
{
    var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
    {
        ResponseText = """{"candidates":[{"candidateId":"c1","kind":"ProjectFact","content":"fact","confidence":"High","sourceRefIds":["block-1"]}]}"""
    });
    var extractor = AgentMemoryLlmTestData.Extractor(client);
    var context = AgentMemoryLlmTestData.CompressedContext("tenant-1", "block-1", "content");

    var candidates = await extractor.ExtractCandidatesAsync(context);

    candidates.Should().ContainSingle();
    candidates[0].Status.Should().Be(AgentMemoryStatus.Candidate);
    candidates[0].Confidence.Should().Be(AgentMemoryConfidence.Medium);
    candidates[0].SanitizationDiagnostics.Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.CandidateConfidenceCapped);
}

[Fact]
public async Task LlmExtractor_InvalidSourceRef_FallsBackAndAddsCandidateDiagnostic()
{
    var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
    {
        ResponseText = """{"candidates":[{"candidateId":"c1","kind":"ProjectFact","content":"fact","confidence":"Low","sourceRefIds":["missing"]}]}"""
    });
    var extractor = AgentMemoryLlmTestData.Extractor(client);
    var context = AgentMemoryLlmTestData.CompressedContext("tenant-1", "block-1", "content");

    var candidates = await extractor.ExtractCandidatesAsync(context);

    candidates.Should().NotBeEmpty();
    candidates.Should().OnlyContain(c => c.Status == AgentMemoryStatus.Candidate);
    candidates.SelectMany(c => c.SanitizationDiagnostics)
        .Should().Contain(d => d.Code == AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicExtractor);
}
```

Add malicious output tests:

```csharp
[Fact]
public async Task LlmExtractor_ProviderOutputWithActiveStatus_IsRejectedOrNormalizedToCandidate()

[Fact]
public async Task LlmExtractor_ProviderOutputWithAuthoritativeFlag_IsRejectedOrForcedFalse()

[Fact]
public async Task LlmExtractor_ProviderOutputWithUnknownTenant_IsRejectedAndFallbacks()
```

- [ ] **Step 2: Run extractor tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter FullyQualifiedName~LlmExtractor
```

Expected: FAIL because `LlmAgentMemoryExtractor` does not exist.

- [ ] **Step 3: Implement extractor**

Implement `LlmAgentMemoryExtractor` with exact signature:

```csharp
public sealed class LlmAgentMemoryExtractor : IAgentMemoryExtractor
{
    public ValueTask<IReadOnlyList<AgentMemoryCandidate>> ExtractCandidatesAsync(
        AgentCompressedContext context,
        CancellationToken cancellationToken = default);
}
```

Rules:

- Use only `AgentCompressedContext` block content and refs; never expand raw sources.
- Reject or normalize any provider status to `AgentMemoryStatus.Candidate`.
- Ignore/force false any provider authoritative flag.
- Cap confidence to `AgentMemoryLlmAdapterOptions.MaxCandidateConfidence`.
- Reuse `AgentMemoryCandidate.SanitizationDiagnostics` for candidate-level LLM diagnostics in this phase.
- On fallback, call `DefaultAgentMemoryExtractor` and add fallback diagnostics to every returned candidate via `SanitizationDiagnostics`.
- If fallback is disabled and output is untrusted, return `Array.Empty<AgentMemoryCandidate>()`; do not create placeholder candidates for diagnostics.

- [ ] **Step 4: Run extractor tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter FullyQualifiedName~LlmExtractor
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory.Llm tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests
git commit -m "feat(agent-memory): add LLM memory extractor"
```

---

### Task 7: Opt-in DI Registration and Lifecycle Recall Tests

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/AgentMemoryLlmServiceCollectionExtensions.cs`
- Extend: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/ServiceCollectionTests.cs`
- Extend: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/ExtractionAdapterTests.cs`

**Interfaces:**
- Consumes: `LlmAgentContextCompressor`, `LlmAgentMemoryExtractor`, options, model client.
- Produces: `AddAgentMemoryLlmCompressor`, `AddAgentMemoryLlmExtractor`, `AddAgentMemoryLlmAdapters`.

- [ ] **Step 1: Add failing DI opt-in tests**

Add tests:

```csharp
[Fact]
public void AddAgentMemoryLlmCompressor_ReplacesOnlyCompressor_WhenExplicitlyEnabled()
{
    var services = AgentMemoryLlmTestData.Services();
    services.AddSingleton<IAgentMemoryLlmModelClient>(new FakeAgentMemoryLlmModelClient());
    services.AddAgentMemoryLlmCompressor();

    using var provider = services.BuildServiceProvider();

    provider.GetRequiredService<IAgentContextCompressor>().Should().BeOfType<LlmAgentContextCompressor>();
    provider.GetRequiredService<IAgentMemoryExtractor>().Should().BeOfType<DefaultAgentMemoryExtractor>();
}

[Fact]
public void AddAgentMemoryLlmExtractor_ReplacesOnlyExtractor_WhenExplicitlyEnabled()
{
    var services = AgentMemoryLlmTestData.Services();
    services.AddSingleton<IAgentMemoryLlmModelClient>(new FakeAgentMemoryLlmModelClient());
    services.AddAgentMemoryLlmExtractor();

    using var provider = services.BuildServiceProvider();

    provider.GetRequiredService<IAgentContextCompressor>().Should().BeOfType<DefaultAgentContextCompressor>();
    provider.GetRequiredService<IAgentMemoryExtractor>().Should().BeOfType<LlmAgentMemoryExtractor>();
}
```

- [ ] **Step 2: Add failing recall lifecycle test**

Add:

```csharp
[Fact]
public async Task Candidates_DoNotAppearInRecall_BeforePromotion_AndAppearAfterExplicitPromotion()
{
    var services = AgentMemoryLlmTestData.Services();
    using var provider = services.BuildServiceProvider();
    var store = provider.GetRequiredService<IAgentMemoryStore>();
    var promotion = provider.GetRequiredService<IAgentMemoryPromotionService>();
    var retriever = provider.GetRequiredService<IAgentMemoryRetriever>();

    var candidate = AgentMemoryLlmTestData.Candidate("candidate-1", "tenant-1", "fact");
    await store.SaveCandidateAsync(candidate);

    var before = await retriever.RecallAsync(new AgentMemoryQuery { TenantId = "tenant-1" });
    before.Memories.Should().BeEmpty();

    await promotion.PromoteAsync("tenant-1", "candidate-1", AgentMemoryLlmTestData.OperationRequest("tenant-1"));

    var after = await retriever.RecallAsync(new AgentMemoryQuery { TenantId = "tenant-1" });
    after.Memories.Should().ContainSingle(m => m.MemoryId == "candidate-1");
    after.Memories.Should().OnlyContain(m => m.IsAuthoritative == false);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter "FullyQualifiedName~AddAgentMemoryLlm|FullyQualifiedName~Candidates_DoNotAppear"
```

Expected: FAIL because DI extension methods do not exist.

- [ ] **Step 4: Implement DI extensions**

Create `AgentMemoryLlmServiceCollectionExtensions.cs`:

```csharp
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Extraction;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Memory.Llm.Prompting;
using CrestCreates.Agent.Memory.Llm.CanonicalHashing;
using CrestCreates.Agent.Prompting.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.Memory.Llm;

public static class AgentMemoryLlmServiceCollectionExtensions
{
    public static IServiceCollection AddAgentMemoryLlmCompressor(
        this IServiceCollection services,
        Action<AgentMemoryLlmAdapterOptions>? configure = null)
    {
        AddCommon(services, configure);
        services.AddSingleton<LlmAgentContextCompressor>();
        services.AddSingleton<IAgentContextCompressor>(sp => sp.GetRequiredService<LlmAgentContextCompressor>());
        return services;
    }

    public static IServiceCollection AddAgentMemoryLlmExtractor(
        this IServiceCollection services,
        Action<AgentMemoryLlmAdapterOptions>? configure = null)
    {
        AddCommon(services, configure);
        services.AddSingleton<LlmAgentMemoryExtractor>();
        services.AddSingleton<IAgentMemoryExtractor>(sp => sp.GetRequiredService<LlmAgentMemoryExtractor>());
        return services;
    }

    public static IServiceCollection AddAgentMemoryLlmAdapters(
        this IServiceCollection services,
        AgentMemoryLlmAdapterSelection adapters,
        Action<AgentMemoryLlmAdapterOptions>? configure = null)
    {
        if (adapters.HasFlag(AgentMemoryLlmAdapterSelection.Compressor))
            services.AddAgentMemoryLlmCompressor(configure);
        if (adapters.HasFlag(AgentMemoryLlmAdapterSelection.Extractor))
            services.AddAgentMemoryLlmExtractor(configure);
        return services;
    }

    private static void AddCommon(IServiceCollection services, Action<AgentMemoryLlmAdapterOptions>? configure)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<AgentMemoryLlmAdapterOptions>();

        services.TryAddSingleton<AgentMemoryLlmCanonicalHashProjector>();
        services.TryAddSingleton<IAgentMemoryCompressionPromptBuilder, DefaultAgentMemoryCompressionPromptBuilder>();
        services.TryAddSingleton<IAgentMemoryCompressionOutputParser, JsonAgentMemoryCompressionOutputParser>();
        services.TryAddSingleton<IAgentMemoryExtractionPromptBuilder, DefaultAgentMemoryExtractionPromptBuilder>();
        services.TryAddSingleton<IAgentMemoryExtractionOutputParser, JsonAgentMemoryExtractionOutputParser>();
        services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<AgentMemoryCompressionPromptInput>, AgentMemoryCompressionPromptInputProjector>();
        services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<AgentMemoryExtractionPromptInput>, AgentMemoryExtractionPromptInputProjector>();
        services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<AgentMemoryLlmModelResponseEvidenceProjection>, AgentMemoryLlmModelResponseEvidenceProjector>();
    }
}
```

Do not register a real `IAgentMemoryLlmModelClient` here.

- [ ] **Step 5: Run DI and lifecycle tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests --filter "FullyQualifiedName~AddAgentMemoryLlm|FullyQualifiedName~Candidates_DoNotAppear"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Memory.Llm tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests
git commit -m "feat(agent-memory): add LLM adapter DI registration"
```

---

### Task 8: Full Verification, Boundary Regression, and Memory Record

**Files:**
- Modify: `memory.md`
- Review: `docs/superpowers/specs/2026-07-02-phase-7gplus-llm-backed-agent-memory-adapter-design.md`

**Interfaces:**
- Consumes: all previous task outputs.
- Produces: verified branch state and platform memory update.

- [ ] **Step 1: Run focused test projects**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests --filter "FullyQualifiedName~ProviderBoundaryTests|FullyQualifiedName~LlmDescriptorAuthoringAgentTests"
```

Expected: all selected tests pass.

- [ ] **Step 2: Run boundary tests**

Run:

```bash
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

Expected: PASS.

- [ ] **Step 3: Run build**

Run:

```bash
dotnet build
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Check forbidden references and stale option flags**

Run:

```bash
rg -n "UseLlmCompressor|UseLlmExtractor|CrestCreates.Agent.Memory.Llm.Http|IDescriptorAuthoringModelClient" src/Runtime/Agent/CrestCreates.Agent.Memory.Llm tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests
```

Expected:

```text
No UseLlmCompressor or UseLlmExtractor hits.
No CrestCreates.Agent.Memory.Llm.Http hits.
No IDescriptorAuthoringModelClient hits.
```

- [ ] **Step 5: Update `memory.md`**

Add a concise dated entry after the Phase 7h section:

```markdown
### Phase 7g+ — LLM-backed Agent Memory Adapter (2026-07-02)

Status: Completed.

Added provider-agnostic `CrestCreates.Agent.Memory.Llm` as an explicit opt-in adapter for `IAgentContextCompressor` and `IAgentMemoryExtractor`. `AddAgentMemoryRuntime()` remains deterministic by default. LLM output can produce only `AgentCompressedContext` and `AgentMemoryCandidate`; promotion, recall, stores, Control Plane, activation, HTTP providers, and runtime handlers remain outside the adapter. Prompt evidence uses Phase 7h; canonical compressed/candidate output hashes use `SourceIdentity`; recorded fixtures fail closed when missing.
```

- [ ] **Step 6: Commit final docs/status**

```bash
git add memory.md
git commit -m "docs(agent): record Phase 7g+ memory adapter completion"
```

---

## Self-Review Checklist

- [ ] Spec section 2 is covered by Task 1 and tests preserving exact interface return types and diagnostic carriers.
- [ ] Spec section 5 dependency boundaries are covered by Task 1 and Task 8 boundary verification.
- [ ] Spec sections 6.2 and recorded fixture behavior are covered by Task 2.
- [ ] Spec sections 6.3, 6.4, 8, and 9 fallback diagnostics are covered by Tasks 3, 5, and 6.
- [ ] Canonical output hash purpose and artifact names are covered by Task 4.
- [ ] DI opt-in semantics and concrete fallback instance reuse are covered by Tasks 1 and 7.
- [ ] Non-authoritative lifecycle and recall invisibility before promotion are covered by Task 7.
- [ ] Malicious provider output tests are covered by Tasks 5 and 6.
- [ ] No task asks implementers to create `CrestCreates.Agent.Memory.Llm.Http`.
- [ ] No task changes `IAgentContextCompressor` or `IAgentMemoryExtractor` signatures.
