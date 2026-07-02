# Phase 7h Agent Prompt Evidence Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Phase 7h Prompting evidence kernel and migrate descriptor authoring prompt hashes to it without adding a prompt executor or provider runtime.

**Architecture:** Add `CrestCreates.Agent.Prompting.Abstractions` for stable evidence contracts and `CrestCreates.Agent.Prompting` for default evidence/hash/registry implementations. Authoring keeps its prompt input/output/model-client contracts, but `LlmDescriptorAuthoringAgent` creates prompt evidence summaries and uses the Prompting hash path.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, Microsoft.Extensions.DependencyInjection.Abstractions, existing `ICanonicalHashComputer`, hand-written `Utf8JsonWriter` canonical projections, source-generated JSON contexts for public DTOs.

## Global Constraints

- Prompting must not define `IAgentPromptExecutor`, `IAgentPromptModelClient`, or `IAgentPromptCompletionService`.
- Prompting must not reference ControlPlane, Activation, RuntimeGate, DraftContracts, Authoring.Http, Platform, HTTP clients, provider SDKs, provider credentials, review, package, or activation code.
- Prompt template registry stores `AgentPromptTemplateDescriptor` metadata only; no prompt bodies, rendered prompts, raw provider JSON, raw prompt strings, endpoints, headers, or credentials.
- Prompt hashes use canonical hash infrastructure only; no ad-hoc SHA-256 helper.
- Prompt artifact names use `CanonicalHashArtifactNames` or the existing governed artifact-name mechanism; do not extend `CanonicalHashArtifactKind`.
- Prompt hash projection must be AoT-safe and must not serialize arbitrary generic payloads through reflection-based `JsonSerializer`.
- `InputHash` payload includes template id, template version, purpose, contract version, model profile ref, provider profile ref, and normalized input.
- `InputHash` payload excludes `CreatedAt`, diagnostics, correlation id, actor id, provider latency, raw prompt strings, and raw provider response strings.
- Prompt output hash uses `CanonicalHashPurposeNames.AuditEvidence`.
- `CreatedAt` in default evidence is assigned by `TimeProvider`; do not call `DateTimeOffset.UtcNow` directly.
- Prompt evidence improves traceability only; prompt output does not bypass deterministic review, package, activation, evidence recheck, or `IRuntimeActivationGate`.

---

## File Structure

Create:
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/CrestCreates.Agent.Prompting.Abstractions.csproj` - prompt evidence contract project.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/GlobalUsings.cs` - shared namespaces for contracts.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptValueObjects.cs` - semantic prompt ids/refs.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptPurpose.cs` - prompt purpose enum.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptTemplateDescriptor.cs` - template metadata descriptor.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptEvidenceContracts.cs` - creation request, typed evidence, summaries, provider observation.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptDiagnostic.cs` - diagnostic record.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptDiagnosticCodes.cs` - governed diagnostic strings.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptInterfaces.cs` - evidence factory, hash service, template registry, canonical payload projector.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/Json/AgentPromptingJsonSerializerContext.cs` - source-generated JSON context for summary DTOs.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting/CrestCreates.Agent.Prompting.csproj` - prompt evidence runtime project.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting/AgentPromptingServiceCollectionExtensions.cs` - DI registration.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting/DefaultAgentPromptEvidenceFactory.cs` - evidence factory.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting/DefaultAgentPromptHashService.cs` - canonical hash service.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting/InMemoryAgentPromptTemplateRegistry.cs` - descriptor metadata registry.
- `src/Runtime/Agent/CrestCreates.Agent.Prompting/AgentPromptEvidenceSummaryFactory.cs` - summary projection helpers.
- `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj` - Prompting test project.
- `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/PromptingContractTests.cs` - value object, descriptor, summary tests.
- `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/PromptingHashTests.cs` - canonical hash behavior tests.
- `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/PromptingRegistryTests.cs` - metadata-only registry tests.

Modify:
- `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashing/CanonicalHashArtifactNames.cs` - add prompt artifact string constants only.
- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj` - reference Prompting.Abstractions.
- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringResult.cs` - add evidence summaries.
- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Json/DescriptorAuthoringJsonSerializerContext.cs` - include evidence summaries.
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/CrestCreates.Agent.Authoring.csproj` - reference Prompting runtime.
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/AgentAuthoringServiceCollectionExtensions.cs` - register Prompting and authoring projectors.
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Authoring/LlmDescriptorAuthoringAgentOptions.cs` - add template/profile identity options.
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Authoring/LlmDescriptorAuthoringAgent.cs` - create input/output evidence and attach summaries.
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptInputHashService.cs` - wrap Prompting hash service or remove duplicate path after migration.
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptInputFactory.cs` - stop computing hash locally if `LlmDescriptorAuthoringAgent` owns evidence creation.
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/IDescriptorAuthoringPromptInputHashService.cs` - keep as adapter interface only if existing tests need it; otherwise delete after consumers are migrated.
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/PromptInputHashTests.cs` - update hash expectations to prompt artifact names and profile refs.
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/LlmDescriptorAuthoringAgentTests.cs` - add evidence summary and blocked mismatch tests.
- `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs` - add Prompting boundary tests.
- `CrestCreates.slnx` and `solutions/CrestCreates.All.slnx` - include new source/test projects.

---

### Task 1: Prompting Abstractions Contracts

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/CrestCreates.Agent.Prompting.Abstractions.csproj`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/GlobalUsings.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptValueObjects.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptPurpose.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptTemplateDescriptor.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptEvidenceContracts.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptDiagnostic.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptDiagnosticCodes.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptInterfaces.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/Json/AgentPromptingJsonSerializerContext.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/PromptingContractTests.cs`

**Interfaces:**
- Produces: `AgentPromptTemplateId`, `AgentPromptVersion`, `AgentPromptContractVersion`, `AgentPromptModelProfileRef`, `AgentPromptProviderProfileRef`, `AgentPromptInputEvidence<TInput>`, `AgentPromptOutputEvidence<TOutput>`, `AgentPromptInputEvidenceSummary`, `AgentPromptOutputEvidenceSummary`, `IAgentPromptCanonicalPayloadProjector<TPayload>`, `IAgentPromptHashService`, `IAgentPromptEvidenceFactory`, `IAgentPromptTemplateRegistry`.

- [ ] **Step 1: Write failing contract tests**

Create `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Agent.Prompting.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Prompting.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/CrestCreates.Agent.Prompting.Abstractions.csproj" />
    <ProjectReference Include="../../../../src/Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

Create `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/PromptingContractTests.cs`:

```csharp
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace CrestCreates.Agent.Prompting.Tests;

public sealed class PromptingContractTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SemanticValueObjects_RejectBlankValues(string value)
    {
        Action createTemplateId = () => _ = new AgentPromptTemplateId(value);
        Action createVersion = () => _ = new AgentPromptVersion(value);
        Action createContractVersion = () => _ = new AgentPromptContractVersion(value);
        Action createModelRef = () => _ = new AgentPromptModelProfileRef(value);
        Action createProviderRef = () => _ = new AgentPromptProviderProfileRef(value);

        createTemplateId.Should().Throw<ArgumentException>();
        createVersion.Should().Throw<ArgumentException>();
        createContractVersion.Should().Throw<ArgumentException>();
        createModelRef.Should().Throw<ArgumentException>();
        createProviderRef.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SemanticValueObjects_ToStringReturnsValue()
    {
        new AgentPromptTemplateId("descriptor-authoring").ToString().Should().Be("descriptor-authoring");
        new AgentPromptVersion("v1").ToString().Should().Be("v1");
        new AgentPromptContractVersion("7h.v1").ToString().Should().Be("7h.v1");
        new AgentPromptModelProfileRef("model-default").ToString().Should().Be("model-default");
        new AgentPromptProviderProfileRef("provider-default").ToString().Should().Be("provider-default");
    }

    [Fact]
    public void TemplateDescriptor_MetadataDefaultsToEmptyDictionary()
    {
        var descriptor = new AgentPromptTemplateDescriptor
        {
            TemplateId = new AgentPromptTemplateId("descriptor-authoring"),
            Version = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7h.v1")
        };

        descriptor.Metadata.Should().NotBeNull();
        descriptor.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void PromptEvidence_DiagnosticsDefaultToEmptyCollections()
    {
        var hash = TestHash("input-hash", CanonicalHashPurposeNames.SourceIdentity);

        var input = new AgentPromptInputEvidence<string>
        {
            TemplateId = new AgentPromptTemplateId("descriptor-authoring"),
            TemplateVersion = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7h.v1"),
            ModelProfileRef = new AgentPromptModelProfileRef("model-default"),
            ProviderProfileRef = new AgentPromptProviderProfileRef("provider-default"),
            Input = "safe input",
            InputHash = hash
        };

        var output = new AgentPromptOutputEvidence<string>
        {
            TemplateId = input.TemplateId,
            TemplateVersion = input.TemplateVersion,
            Purpose = input.Purpose,
            ContractVersion = input.ContractVersion,
            ModelProfileRef = input.ModelProfileRef,
            ProviderProfileRef = input.ProviderProfileRef,
            InputHash = hash,
            Output = "safe output"
        };

        input.Diagnostics.Should().NotBeNull().And.BeEmpty();
        output.Diagnostics.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void EvidenceSummary_JsonContextSerializesWithoutGenericPayload()
    {
        var summary = new AgentPromptInputEvidenceSummary
        {
            TemplateId = new AgentPromptTemplateId("descriptor-authoring"),
            TemplateVersion = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7h.v1"),
            ModelProfileRef = new AgentPromptModelProfileRef("model-default"),
            ProviderProfileRef = new AgentPromptProviderProfileRef("provider-default"),
            InputHash = TestHash("input-hash", CanonicalHashPurposeNames.SourceIdentity),
            CreatedAt = DateTimeOffset.UnixEpoch
        };

        var json = JsonSerializer.Serialize(
            summary,
            AgentPromptingJsonSerializerContext.Default.AgentPromptInputEvidenceSummary);

        json.Should().Contain("templateId");
        json.Should().NotContain("safe input");
        json.Should().NotContain("payload");
    }

    private static CanonicalHash TestHash(string value, string purpose) => new()
    {
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "AgentPromptInputEvidence",
        Purpose = purpose,
        Scope = CanonicalHashScopeNames.InternalFull,
        ContractVersion = CanonicalHashContractVersions.DescriptorHash,
        CanonicalShapeVersion = "test-shape-v1",
        Value = value
    };
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj --filter "FullyQualifiedName~PromptingContractTests"
```

Expected: FAIL because Prompting projects and contracts do not exist.

- [ ] **Step 3: Add Prompting.Abstractions project and contracts**

Create `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/CrestCreates.Agent.Prompting.Abstractions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Agent.Prompting.Abstractions</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Prompting.Abstractions</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Snapshot.Abstractions/CrestCreates.Snapshot.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

Create `GlobalUsings.cs`:

```csharp
global using CrestCreates.Metadata.Abstractions.CanonicalHashing;
```

Create `AgentPromptValueObjects.cs`:

```csharp
namespace CrestCreates.Agent.Prompting.Abstractions;

public readonly record struct AgentPromptTemplateId
{
    public AgentPromptTemplateId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct AgentPromptVersion
{
    public AgentPromptVersion(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct AgentPromptContractVersion
{
    public AgentPromptContractVersion(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct AgentPromptModelProfileRef
{
    public AgentPromptModelProfileRef(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct AgentPromptProviderProfileRef
{
    public AgentPromptProviderProfileRef(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}
```

Create `AgentPromptPurpose.cs`:

```csharp
namespace CrestCreates.Agent.Prompting.Abstractions;

public enum AgentPromptPurpose
{
    DescriptorAuthoring = 1,
    MemoryCompression = 2,
    MemoryExtraction = 3,
    ReviewExplanation = 4,
    FixProposalExplanation = 5
}
```

Create `AgentPromptTemplateDescriptor.cs`:

```csharp
namespace CrestCreates.Agent.Prompting.Abstractions;

public sealed record AgentPromptTemplateDescriptor
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion Version { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public string? Description { get; init; }
    public string? InputSchemaVersion { get; init; }
    public string? OutputSchemaVersion { get; init; }
    public bool ContainsSensitiveContent { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
```

Create `AgentPromptDiagnostic.cs` and `AgentPromptDiagnosticCodes.cs`:

```csharp
namespace CrestCreates.Agent.Prompting.Abstractions;

public sealed record AgentPromptDiagnostic
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Severity { get; init; } = "Information";
}
```

```csharp
namespace CrestCreates.Agent.Prompting.Abstractions;

public static class AgentPromptDiagnosticCodes
{
    public const string TemplateDescriptorMissing = "agent.prompt.template_descriptor_missing";
    public const string TemplateDescriptorPurposeMismatch = "agent.prompt.template_descriptor_purpose_mismatch";
    public const string InputHashProjectionFailed = "agent.prompt.input_hash_projection_failed";
    public const string OutputHashProjectionFailed = "agent.prompt.output_hash_projection_failed";
    public const string OutputHashUnavailable = "agent.prompt.output_hash_unavailable";
    public const string ProviderObservationUnavailable = "agent.prompt.provider_observation_unavailable";
    public const string PromptEvidenceCreated = "agent.prompt.evidence_created";
}
```

Create `AgentPromptEvidenceContracts.cs`:

```csharp
namespace CrestCreates.Agent.Prompting.Abstractions;

public sealed record AgentPromptEvidenceCreationRequest<TPayload>
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }
    public required TPayload Payload { get; init; }
    public string? TenantId { get; init; }
    public string? ActorId { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record AgentPromptProviderObservation
{
    public string? ProviderName { get; init; }
    public string? ModelName { get; init; }
    public string? ResponseId { get; init; }
    public string? FinishReason { get; init; }
    public long? LatencyMs { get; init; }
}

public sealed record AgentPromptInputEvidence<TInput>
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }
    public required TInput Input { get; init; }
    public required CanonicalHash InputHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? TenantId { get; init; }
    public string? ActorId { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<AgentPromptDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentPromptDiagnostic>();
}

public sealed record AgentPromptOutputEvidence<TOutput>
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }
    public required CanonicalHash InputHash { get; init; }
    public CanonicalHash? OutputHash { get; init; }
    public required TOutput Output { get; init; }
    public AgentPromptProviderObservation? ProviderObservation { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<AgentPromptDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentPromptDiagnostic>();
}

public sealed record AgentPromptInputEvidenceSummary
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }
    public required CanonicalHash InputHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<AgentPromptDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentPromptDiagnostic>();
}

public sealed record AgentPromptOutputEvidenceSummary
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }
    public required CanonicalHash InputHash { get; init; }
    public CanonicalHash? OutputHash { get; init; }
    public AgentPromptProviderObservation? ProviderObservation { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<AgentPromptDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentPromptDiagnostic>();
}
```

Create `AgentPromptInterfaces.cs`:

```csharp
using System.Text.Json;

namespace CrestCreates.Agent.Prompting.Abstractions;

public interface IAgentPromptCanonicalPayloadProjector<TPayload>
{
    void Write(Utf8JsonWriter writer, TPayload payload);
}

public interface IAgentPromptHashService
{
    CanonicalHash ComputeInputHash<TInput>(AgentPromptEvidenceCreationRequest<TInput> request);

    CanonicalHash? ComputeOutputHash<TOutput>(
        AgentPromptEvidenceCreationRequest<TOutput> request,
        CanonicalHash inputHash,
        AgentPromptProviderObservation? providerObservation);
}

public interface IAgentPromptEvidenceFactory
{
    AgentPromptInputEvidence<TInput> CreateInputEvidence<TInput>(
        AgentPromptEvidenceCreationRequest<TInput> request);

    AgentPromptOutputEvidence<TOutput> CreateOutputEvidence<TOutput>(
        AgentPromptEvidenceCreationRequest<TOutput> request,
        CanonicalHash inputHash,
        AgentPromptProviderObservation? providerObservation = null);
}

public interface IAgentPromptTemplateRegistry
{
    AgentPromptTemplateDescriptor? Find(AgentPromptTemplateId templateId, AgentPromptVersion version);
    IReadOnlyList<AgentPromptTemplateDescriptor> List();
}
```

Create `Json/AgentPromptingJsonSerializerContext.cs`:

```csharp
using System.Text.Json.Serialization;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Prompting.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentPromptInputEvidenceSummary))]
[JsonSerializable(typeof(AgentPromptOutputEvidenceSummary))]
[JsonSerializable(typeof(AgentPromptProviderObservation))]
[JsonSerializable(typeof(AgentPromptDiagnostic))]
[JsonSerializable(typeof(AgentPromptPurpose))]
[JsonSerializable(typeof(CanonicalHash))]
public sealed partial class AgentPromptingJsonSerializerContext : JsonSerializerContext;
```

- [ ] **Step 4: Run contract tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj --filter "FullyQualifiedName~PromptingContractTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests
git commit -m "feat: add agent prompting evidence contracts"
```

---

### Task 2: Prompting Runtime Hash, Evidence Factory, and Registry

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting/CrestCreates.Agent.Prompting.csproj`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting/AgentPromptingServiceCollectionExtensions.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting/DefaultAgentPromptHashService.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting/DefaultAgentPromptEvidenceFactory.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting/InMemoryAgentPromptTemplateRegistry.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Prompting/AgentPromptEvidenceSummaryFactory.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashing/CanonicalHashArtifactNames.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/PromptingHashTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/PromptingRegistryTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj`

**Interfaces:**
- Consumes: Task 1 Prompting abstractions.
- Produces: `DefaultAgentPromptHashService`, `DefaultAgentPromptEvidenceFactory`, `AgentPromptEvidenceSummaryFactory`, `AddAgentPrompting()`.

- [ ] **Step 1: Write failing hash and registry tests**

Modify `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj` to reference the runtime project, the concrete Metadata project, and the DI package used by the runtime tests:

```xml
<ProjectReference Include="../../../../src/Runtime/Agent/CrestCreates.Agent.Prompting/CrestCreates.Agent.Prompting.csproj" />
<ProjectReference Include="../../../../src/Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

Create `PromptingHashTests.cs` with a safe test payload and projector:

```csharp
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace CrestCreates.Agent.Prompting.Tests;

public sealed class PromptingHashTests
{
    [Fact]
    public void SamePromptInput_ProducesStableHash()
    {
        var services = Services();
        var hashService = services.GetRequiredService<IAgentPromptHashService>();
        var request = Request(new TestPromptPayload("tenant-1", "intent"));

        var hash1 = hashService.ComputeInputHash(request);
        var hash2 = hashService.ComputeInputHash(request);

        hash1.Value.Should().Be(hash2.Value);
        hash1.ArtifactKind.Should().Be(CanonicalHashArtifactNames.AgentPromptInputEvidence);
        hash1.Purpose.Should().Be(CanonicalHashPurposeNames.SourceIdentity);
    }

    [Fact]
    public void TemplateVersionChange_ChangesInputHash()
    {
        var hashService = Services().GetRequiredService<IAgentPromptHashService>();

        var v1 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), version: "v1"));
        var v2 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), version: "v2"));

        v1.Value.Should().NotBe(v2.Value);
    }

    [Fact]
    public void ModelProfileRefChange_ChangesInputHash()
    {
        var hashService = Services().GetRequiredService<IAgentPromptHashService>();

        var hash1 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), modelRef: "model-a"));
        var hash2 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), modelRef: "model-b"));

        hash1.Value.Should().NotBe(hash2.Value);
    }

    [Fact]
    public void CorrelationAndActor_DoNotChangeInputHash()
    {
        var hashService = Services().GetRequiredService<IAgentPromptHashService>();

        var hash1 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), actorId: "actor-a", correlationId: "corr-a"));
        var hash2 = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent"), actorId: "actor-b", correlationId: "corr-b"));

        hash1.Value.Should().Be(hash2.Value);
    }

    [Fact]
    public void OutputHash_UsesAuditEvidencePurpose()
    {
        var hashService = Services().GetRequiredService<IAgentPromptHashService>();
        var inputHash = hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent")));

        var outputHash = hashService.ComputeOutputHash(
            Request(new TestPromptPayload("tenant-1", "safe-output")),
            inputHash,
            new AgentPromptProviderObservation { ProviderName = "provider", ModelName = "model" });

        outputHash.Should().NotBeNull();
        outputHash!.ArtifactKind.Should().Be(CanonicalHashArtifactNames.AgentPromptOutputEvidence);
        outputHash.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
    }

    [Fact]
    public void MissingProjector_ThrowsInsteadOfUsingReflectionSerialization()
    {
        var hashService = Services(registerProjector: false).GetRequiredService<IAgentPromptHashService>();

        var act = () => hashService.ComputeInputHash(Request(new TestPromptPayload("tenant-1", "intent")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IAgentPromptCanonicalPayloadProjector*");
    }

    private static ServiceProvider Services(bool registerProjector = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        if (registerProjector)
        {
            services.AddSingleton<IAgentPromptCanonicalPayloadProjector<TestPromptPayload>, TestPromptPayloadProjector>();
        }
        return services.BuildServiceProvider();
    }

    private static AgentPromptEvidenceCreationRequest<TestPromptPayload> Request(
        TestPromptPayload payload,
        string version = "v1",
        string modelRef = "model-default",
        string? actorId = null,
        string? correlationId = null) => new()
        {
            TemplateId = new AgentPromptTemplateId("descriptor-authoring"),
            TemplateVersion = new AgentPromptVersion(version),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7h.v1"),
            ModelProfileRef = new AgentPromptModelProfileRef(modelRef),
            ProviderProfileRef = new AgentPromptProviderProfileRef("provider-default"),
            Payload = payload,
            ActorId = actorId,
            CorrelationId = correlationId
        };

    private sealed record TestPromptPayload(string TenantId, string Intent);

    private sealed class TestPromptPayloadProjector : IAgentPromptCanonicalPayloadProjector<TestPromptPayload>
    {
        public void Write(Utf8JsonWriter writer, TestPromptPayload payload)
        {
            writer.WriteStartObject();
            writer.WriteString("tenantId", payload.TenantId);
            writer.WriteString("intent", payload.Intent);
            writer.WriteEndObject();
        }
    }
}
```

Create `PromptingRegistryTests.cs`:

```csharp
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Prompting.Tests;

public sealed class PromptingRegistryTests
{
    [Fact]
    public void Registry_StoresDescriptorMetadataOnly()
    {
        var registry = new InMemoryAgentPromptTemplateRegistry(new[]
        {
            new AgentPromptTemplateDescriptor
            {
                TemplateId = new AgentPromptTemplateId("descriptor-authoring"),
                Version = new AgentPromptVersion("v1"),
                Purpose = AgentPromptPurpose.DescriptorAuthoring,
                ContractVersion = new AgentPromptContractVersion("7h.v1"),
                Metadata = new Dictionary<string, string> { ["owner"] = "authoring" }
            }
        });

        var descriptor = registry.Find(new AgentPromptTemplateId("descriptor-authoring"), new AgentPromptVersion("v1"));

        descriptor.Should().NotBeNull();
        descriptor!.Metadata.Should().ContainKey("owner");
        typeof(AgentPromptTemplateDescriptor).GetProperties().Select(p => p.Name)
            .Should().NotContain(new[] { "TemplateBody", "PromptBody", "RenderedPrompt", "ExternalContent" });
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj --filter "FullyQualifiedName~PromptingHashTests|FullyQualifiedName~PromptingRegistryTests"
```

Expected: FAIL because runtime project and artifact names do not exist.

- [ ] **Step 3: Add artifact names**

Modify `CanonicalHashArtifactNames.cs` by adding string constants only:

```csharp
public const string AgentPromptInputEvidence = "AgentPromptInputEvidence";
public const string AgentPromptOutputEvidence = "AgentPromptOutputEvidence";
public const string AgentPromptTemplateDescriptor = "AgentPromptTemplateDescriptor";
```

Do not modify `CanonicalHashArtifactKind.cs`.

- [ ] **Step 4: Add Prompting runtime project and implementations**

Create `CrestCreates.Agent.Prompting.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Agent.Prompting</RootNamespace>
    <AssemblyName>CrestCreates.Agent.Prompting</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../CrestCreates.Agent.Prompting.Abstractions/CrestCreates.Agent.Prompting.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>
</Project>
```

Create `DefaultAgentPromptHashService.cs` using `IServiceProvider.GetService<IAgentPromptCanonicalPayloadProjector<T>>()`, `CanonicalHashProjectionResult.Create`, `CanonicalHashArtifactNames.AgentPromptInputEvidence`, `CanonicalHashArtifactNames.AgentPromptOutputEvidence`, `CanonicalHashPurposeNames.SourceIdentity`, `CanonicalHashPurposeNames.AuditEvidence`, and `CanonicalHashScopeNames.InternalFull`. Use `CanonicalHashContractVersions.DescriptorHash`, `agent-prompt-input-evidence-shape-v1`, and `agent-prompt-output-evidence-shape-v1`.

The input hash writer must emit one complete canonical object with this shape before invoking the typed projector:

```json
{
  "templateId": "...",
  "templateVersion": "...",
  "purpose": "...",
  "contractVersion": "...",
  "modelProfileRef": "...",
  "providerProfileRef": "...",
  "payload": {}
}
```

The output hash writer must emit the same prompt identity fields, `inputHash`, optional provider/model observation fields that are safe to hash, and then the safe output projection under `"payload"`. Do not include `CreatedAt`, diagnostics, correlation id, actor id, latency, raw prompt text, or raw provider response text. If the typed projector is missing, throw `InvalidOperationException` with `IAgentPromptCanonicalPayloadProjector<TPayload>` in the message instead of using `JsonSerializer`.

Create `DefaultAgentPromptEvidenceFactory.cs` with `TimeProvider.GetUtcNow()` for `CreatedAt`, `ComputeInputHash()` for input, `ComputeOutputHash()` for output, and no direct `DateTimeOffset.UtcNow`.

Create `AgentPromptEvidenceSummaryFactory.cs` with:

```csharp
public static AgentPromptInputEvidenceSummary CreateInputSummary<TInput>(AgentPromptInputEvidence<TInput> evidence)
public static AgentPromptOutputEvidenceSummary CreateOutputSummary<TOutput>(AgentPromptOutputEvidence<TOutput> evidence)
```

Both methods copy diagnostics with `.ToArray()`.

Create `InMemoryAgentPromptTemplateRegistry.cs` storing descriptors in a private array and copying metadata to a new dictionary when registering descriptors.

Create `AgentPromptingServiceCollectionExtensions.cs`:

```csharp
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.Prompting;

public static class AgentPromptingServiceCollectionExtensions
{
    public static IServiceCollection AddAgentPrompting(this IServiceCollection services)
    {
        services.TryAddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAgentPromptHashService, DefaultAgentPromptHashService>();
        services.TryAddSingleton<IAgentPromptEvidenceFactory, DefaultAgentPromptEvidenceFactory>();
        services.TryAddSingleton<IAgentPromptTemplateRegistry>(_ => new InMemoryAgentPromptTemplateRegistry(Array.Empty<AgentPromptTemplateDescriptor>()));
        return services;
    }
}
```

- [ ] **Step 5: Run Prompting tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashing/CanonicalHashArtifactNames.cs src/Runtime/Agent/CrestCreates.Agent.Prompting tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests
git commit -m "feat: add agent prompting evidence runtime"
```

---

### Task 3: Authoring Evidence Integration

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringResult.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Json/DescriptorAuthoringJsonSerializerContext.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Authoring/CrestCreates.Agent.Authoring.csproj`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Authoring/AgentAuthoringServiceCollectionExtensions.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Authoring/LlmDescriptorAuthoringAgentOptions.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Authoring/LlmDescriptorAuthoringAgent.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptInputFactory.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptInputHashService.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DescriptorAuthoringPromptInputProjector.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DescriptorAuthoringModelResponseEvidenceProjection.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DescriptorAuthoringModelResponseEvidenceProjector.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/PromptInputHashTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/LlmDescriptorAuthoringAgentTests.cs`

**Interfaces:**
- Consumes: `IAgentPromptEvidenceFactory`, `IAgentPromptHashService`, `IAgentPromptCanonicalPayloadProjector<T>`, `AgentPromptEvidenceSummaryFactory`.
- Produces: authoring results with `PromptInputEvidence` and `PromptOutputEvidence` summaries.

- [ ] **Step 1: Write failing Authoring integration tests**

Add tests in `LlmDescriptorAuthoringAgentTests.cs`:

```csharp
[Fact]
public async Task AuthorAsync_ReturnsPromptEvidenceSummaries()
{
    var agent = CreateAgentWithFakeResponse(BuildValidHumanTaskOutputJson("mismatched-hash"));
    var context = TestAuthoringContext();

    var result = await agent.AuthorAsync(context);

    result.PromptInputEvidence.Should().NotBeNull();
    result.PromptOutputEvidence.Should().NotBeNull();
    result.PromptInputEvidence!.TemplateId.Value.Should().Be("descriptor-authoring");
    result.PromptInputEvidence.TemplateVersion.Value.Should().Be("descriptor-authoring-prompt-template-v1");
    result.PromptInputEvidence.Purpose.Should().Be(AgentPromptPurpose.DescriptorAuthoring);
    result.PromptOutputEvidence!.InputHash.Value.Should().Be(result.PromptInputEvidence.InputHash.Value);
}

[Fact]
public async Task AuthorAsync_ProviderObservation_UsesResponseProviderAndModelNames()
{
    var client = new FakeDescriptorAuthoringModelClient(new DescriptorAuthoringModelResponse
    {
        ResponseText = BuildValidHumanTaskOutputJson("mismatched-hash"),
        ProviderName = "observed-provider",
        ModelName = "observed-model"
    });
    var agent = CreateAgentWithClient(client);

    var result = await agent.AuthorAsync(TestAuthoringContext());

    result.PromptOutputEvidence!.ProviderObservation!.ProviderName.Should().Be("observed-provider");
    result.PromptOutputEvidence.ProviderObservation.ModelName.Should().Be("observed-model");
}
```

Update helper constructors in this test file to build the new Prompting services explicitly. Replace every helper-local `DefaultDescriptorAuthoringPromptInputHashService(hashComputer)` / `DefaultDescriptorAuthoringPromptInputFactory(hashService)` construction with this pattern:

```csharp
var services = new ServiceCollection();
services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
services.AddAgentPrompting();
services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
var provider = services.BuildServiceProvider();

var promptEvidenceFactory = provider.GetRequiredService<IAgentPromptEvidenceFactory>();
var factory = new DefaultDescriptorAuthoringPromptInputFactory();
```

Then construct `LlmDescriptorAuthoringAgent` with `promptEvidenceFactory` as the new constructor argument. Do not use `using var provider` in helpers that return an agent, because the returned agent may use services resolved from that provider after the helper returns. Add these usings to `LlmDescriptorAuthoringAgentTests.cs`:

```csharp
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using Microsoft.Extensions.DependencyInjection;
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~LlmDescriptorAuthoringAgentTests"
```

Expected: FAIL because Authoring result summaries and agent constructor integration do not exist.

- [ ] **Step 3: Add project references and result summaries**

Modify `CrestCreates.Agent.Authoring.Abstractions.csproj`:

```xml
<ProjectReference Include="../CrestCreates.Agent.Prompting.Abstractions/CrestCreates.Agent.Prompting.Abstractions.csproj" />
```

Modify `DescriptorAuthoringResult.cs`:

```csharp
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public sealed record DescriptorAuthoringResult : ISnapshotable<DescriptorAuthoringResult>
{
    public required DescriptorAuthoringStatus Status { get; init; }
    public required DescriptorAuthoringPlan Plan { get; init; }
    public required DescriptorDraftSet DraftSet { get; init; }
    public AgentPromptInputEvidenceSummary? PromptInputEvidence { get; init; }
    public AgentPromptOutputEvidenceSummary? PromptOutputEvidence { get; init; }
    public IReadOnlyList<DescriptorAuthoringDiagnostic> Diagnostics { get; init; } = Array.Empty<DescriptorAuthoringDiagnostic>();

    public DescriptorAuthoringResult Snapshot() => this with
    {
        Plan = Plan.Snapshot(),
        DraftSet = DraftSet.Snapshot(),
        PromptInputEvidence = PromptInputEvidence is null ? null : PromptInputEvidence with
        {
            Diagnostics = PromptInputEvidence.Diagnostics.ToArray()
        },
        PromptOutputEvidence = PromptOutputEvidence is null ? null : PromptOutputEvidence with
        {
            Diagnostics = PromptOutputEvidence.Diagnostics.ToArray()
        },
        Diagnostics = Diagnostics.Select(d => d.Snapshot()).ToArray()
    };
}
```

Add Prompting summary types to `DescriptorAuthoringJsonSerializerContext.cs`.

- [ ] **Step 4: Move authoring prompt input hash into Prompting evidence path**

Modify `CrestCreates.Agent.Authoring.csproj`:

```xml
<ProjectReference Include="../CrestCreates.Agent.Prompting/CrestCreates.Agent.Prompting.csproj" />
```

Modify `DefaultDescriptorAuthoringPromptInputFactory` so it no longer depends on `IDescriptorAuthoringPromptInputHashService` and returns input with `PromptInputHash = null`. `LlmDescriptorAuthoringAgent` will set the hash after evidence creation.

Replace `DefaultDescriptorAuthoringPromptInputHashService` implementation with an adapter around `IAgentPromptHashService`. Use the same request defaults as `LlmDescriptorAuthoringAgentOptions` so existing tests can call it:

```csharp
public sealed class DefaultDescriptorAuthoringPromptInputHashService : IDescriptorAuthoringPromptInputHashService
{
    private readonly IAgentPromptHashService _promptHashService;

    public DefaultDescriptorAuthoringPromptInputHashService(IAgentPromptHashService promptHashService)
    {
        _promptHashService = promptHashService;
    }

    public CanonicalHash ComputeHash(DescriptorAuthoringPromptInput input)
    {
        return _promptHashService.ComputeInputHash(new AgentPromptEvidenceCreationRequest<DescriptorAuthoringPromptInput>
        {
            TemplateId = LlmDescriptorAuthoringAgentOptions.DefaultPromptTemplateId,
            TemplateVersion = LlmDescriptorAuthoringAgentOptions.DefaultPromptTemplateVersion,
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = LlmDescriptorAuthoringAgentOptions.DefaultPromptContractVersion,
            ModelProfileRef = new AgentPromptModelProfileRef("default"),
            ProviderProfileRef = new AgentPromptProviderProfileRef("unknown"),
            Payload = input,
            TenantId = input.TenantId
        });
    }
}
```

Create `DescriptorAuthoringPromptInputProjector.cs` by moving the current hand-written `Utf8JsonWriter` logic from `DefaultDescriptorAuthoringPromptInputHashService` into `IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>`. Keep order-independent descriptor/memory/ref/kind sorting.

Create `DescriptorAuthoringModelResponseEvidenceProjection.cs`:

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Authoring.Prompting;

public sealed record DescriptorAuthoringModelResponseEvidenceProjection
{
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public CanonicalHash? PromptInputHash { get; init; }
    public DescriptorAuthoringProviderFailureKind FailureKind { get; init; }
    public string? FailureDetail { get; init; }
}
```

Create `DescriptorAuthoringModelResponseEvidenceProjector.cs` that writes provider name, model name, prompt input hash value when present, failure kind, and failure detail. Do not write `ResponseText`.

- [ ] **Step 5: Update options, DI, and agent**

Modify `LlmDescriptorAuthoringAgentOptions`:

```csharp
public static readonly AgentPromptTemplateId DefaultPromptTemplateId = new("descriptor-authoring");
public static readonly AgentPromptVersion DefaultPromptTemplateVersion = new("descriptor-authoring-prompt-template-v1");
public static readonly AgentPromptContractVersion DefaultPromptContractVersion = new("7g.v1");

public AgentPromptTemplateId PromptTemplateId { get; set; } = DefaultPromptTemplateId;
public AgentPromptVersion PromptTemplateVersion { get; set; } = DefaultPromptTemplateVersion;
public AgentPromptContractVersion PromptContractVersion { get; set; } = DefaultPromptContractVersion;
public AgentPromptProviderProfileRef ProviderProfileRef { get; set; } = new("unknown");
```

Use `ModelProfile.ProfileName` to create `AgentPromptModelProfileRef`.

Modify `AgentAuthoringServiceCollectionExtensions` to call `services.AddAgentPrompting()` and register:

```csharp
services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
```

Modify `LlmDescriptorAuthoringAgent` constructor to accept `IAgentPromptEvidenceFactory promptEvidenceFactory`.

In `AuthorAsync`:

1. Create raw prompt input.
2. Create `AgentPromptEvidenceCreationRequest<DescriptorAuthoringPromptInput>`.
3. Call `CreateInputEvidence`.
4. Use `promptInput = rawPromptInput with { PromptInputHash = inputEvidence.InputHash }`.
5. Build prompt/model request.
6. After model response, create `AgentPromptProviderObservation`.
7. Create `DescriptorAuthoringModelResponseEvidenceProjection` excluding `ResponseText`.
8. Create output evidence and summaries.
9. Attach summaries on every returned `DescriptorAuthoringResult`, including provider-unavailable results and parser results.

Parser result attachment can be:

```csharp
var parsed = _outputParser.Parse(modelResponse.ResponseText, parseContext);
return parsed with
{
    PromptInputEvidence = AgentPromptEvidenceSummaryFactory.CreateInputSummary(inputEvidence),
    PromptOutputEvidence = AgentPromptEvidenceSummaryFactory.CreateOutputSummary(outputEvidence)
};
```

- [ ] **Step 6: Update Authoring tests and expectations**

Modify `PromptInputHashTests.HashUses_CanonicalHashInfrastructure`:

```csharp
hash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.AgentPromptInputEvidence);
hash.Purpose.Should().Be(CanonicalHashPurposeNames.SourceIdentity);
hash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
hash.ContractVersion.Should().Be("canonical-hash-v1");
hash.CanonicalShapeVersion.Should().Be("agent-prompt-input-evidence-shape-v1");
```

Replace `PromptInputFactory_ProducesInputWithHash` with a test named `PromptInputFactory_ProducesInputWithoutHash` because `LlmDescriptorAuthoringAgent` now owns evidence creation:

```csharp
[Fact]
public void PromptInputFactory_ProducesInputWithoutHash()
{
    var factory = new DefaultDescriptorAuthoringPromptInputFactory();
    var context = TestAuthoringContext();

    var input = factory.Create(context);

    input.PromptInputHash.Should().BeNull();
    input.ContractVersion.Should().Be("7g.v1");
    input.TenantId.Should().Be("test-tenant");
}
```

Update `PromptInputHashTests` setup so `DefaultDescriptorAuthoringPromptInputHashService` is created with `IAgentPromptHashService` from a service provider that has `DescriptorAuthoringPromptInputProjector` registered.

Add this output-hash safety test to `PromptInputHashTests` or a new authoring prompt evidence test file:

```csharp
[Fact]
public void OutputEvidenceHash_DoesNotChange_WhenOnlyResponseTextChanges()
{
    using var provider = CreatePromptingProvider();
    var hashService = provider.GetRequiredService<IAgentPromptHashService>();
    var inputHash = TestHash("input-hash");

    var response1 = new DescriptorAuthoringModelResponseEvidenceProjection
    {
        ProviderName = "fake",
        ModelName = "fake-model",
        PromptInputHash = inputHash
    };
    var response2 = response1 with { };

    var hash1 = hashService.ComputeOutputHash(OutputRequest(response1), inputHash, null);
    var hash2 = hashService.ComputeOutputHash(OutputRequest(response2), inputHash, null);

    hash1!.Value.Should().Be(hash2!.Value);
}
```

Use helper methods in that test file:

```csharp
private static ServiceProvider CreatePromptingProvider()
{
    var services = new ServiceCollection();
    services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
    services.AddAgentPrompting();
    services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
    services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
    return services.BuildServiceProvider();
}

private static AgentPromptEvidenceCreationRequest<DescriptorAuthoringModelResponseEvidenceProjection> OutputRequest(
    DescriptorAuthoringModelResponseEvidenceProjection payload) => new()
{
    TemplateId = LlmDescriptorAuthoringAgentOptions.DefaultPromptTemplateId,
    TemplateVersion = LlmDescriptorAuthoringAgentOptions.DefaultPromptTemplateVersion,
    Purpose = AgentPromptPurpose.DescriptorAuthoring,
    ContractVersion = LlmDescriptorAuthoringAgentOptions.DefaultPromptContractVersion,
    ModelProfileRef = new AgentPromptModelProfileRef("default"),
    ProviderProfileRef = new AgentPromptProviderProfileRef("unknown"),
    Payload = payload
};
```

- [ ] **Step 7: Run Authoring tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions src/Runtime/Agent/CrestCreates.Agent.Authoring tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests
git commit -m "feat: integrate authoring with prompt evidence"
```

---

### Task 4: Boundary Tests and Solution Wiring

**Files:**
- Modify: `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs`
- Modify: `CrestCreates.slnx`
- Modify: `solutions/CrestCreates.All.slnx`

**Interfaces:**
- Consumes: Prompting source and test projects from Tasks 1-3.
- Produces: enforced project boundary coverage and solution inclusion.

- [ ] **Step 1: Add failing boundary tests**

Append to `DependencyBoundaryTests.cs`:

```csharp
[Fact]
public void AgentPromptingAbstractions_DoesNotReferenceControlPlaneDraftContractsAuthoringHttpOrPlatform()
{
    AssertNoDirectProjectReferences(
        "src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions",
        "Prompting abstractions must remain prompt evidence contracts only.",
        new[]
        {
            "src/Runtime/Agent/CrestCreates.Agent.ControlPlane",
            "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions",
            "src/Runtime/Agent/CrestCreates.Agent.DraftContracts",
            "src/Runtime/Agent/CrestCreates.Agent.Authoring.Http",
            "src/Platform"
        });
}

[Fact]
public void AgentPromptingRuntime_DoesNotReferenceControlPlaneDraftContractsAuthoringHttpOrPlatform()
{
    AssertNoDirectProjectReferences(
        "src/Runtime/Agent/CrestCreates.Agent.Prompting",
        "Prompting runtime must not own model execution, provider integration, governance, or activation.",
        new[]
        {
            "src/Runtime/Agent/CrestCreates.Agent.ControlPlane",
            "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions",
            "src/Runtime/Agent/CrestCreates.Agent.DraftContracts",
            "src/Runtime/Agent/CrestCreates.Agent.Authoring.Http",
            "src/Platform"
        });
}

[Fact]
public void AgentPrompting_DoesNotExposePromptExecutorModelClientOrCompletionService()
{
    var repoRoot = FindRepoRoot();
    var files = Directory.EnumerateFiles(
        Path.Combine(repoRoot.FullName, "src/Runtime/Agent"),
        "*.cs",
        SearchOption.AllDirectories)
        .Where(path => path.Contains("CrestCreates.Agent.Prompting", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    var forbidden = files
        .SelectMany(file => File.ReadAllLines(file).Select((line, index) => new { file, line, index }))
        .Where(x =>
            x.line.Contains("IAgentPromptExecutor", StringComparison.Ordinal) ||
            x.line.Contains("IAgentPromptModelClient", StringComparison.Ordinal) ||
            x.line.Contains("IAgentPromptCompletionService", StringComparison.Ordinal))
        .Select(x => $"{Path.GetRelativePath(repoRoot.FullName, x.file)}:{x.index + 1}: {x.line.Trim()}")
        .ToArray();

    Assert.True(forbidden.Length == 0, "Prompting must not expose executor/model client/completion service interfaces." + Environment.NewLine + string.Join(Environment.NewLine, forbidden));
}
```

- [ ] **Step 2: Run boundary tests to verify they pass after tasks 1-3**

Run:

```bash
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CrestCreates.DependencyBoundaries.Tests.csproj --filter "FullyQualifiedName~AgentPrompting"
```

Expected: PASS.

- [ ] **Step 3: Add projects to solutions**

In `CrestCreates.slnx`, under `/src/Runtime/Agent/`, add:

```xml
<Project Path="src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/CrestCreates.Agent.Prompting.Abstractions.csproj" />
<Project Path="src/Runtime/Agent/CrestCreates.Agent.Prompting/CrestCreates.Agent.Prompting.csproj" />
```

Under `/tests/Runtime/Agent/`, add:

```xml
<Project Path="tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj" />
```

Make the same additions in `solutions/CrestCreates.All.slnx` with `../` prefixes.

- [ ] **Step 4: Run build for affected projects**

Run:

```bash
dotnet build src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/CrestCreates.Agent.Prompting.Abstractions.csproj
dotnet build src/Runtime/Agent/CrestCreates.Agent.Prompting/CrestCreates.Agent.Prompting.csproj
dotnet build src/Runtime/Agent/CrestCreates.Agent.Authoring/CrestCreates.Agent.Authoring.csproj
```

Expected: all builds PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs CrestCreates.slnx solutions/CrestCreates.All.slnx
git commit -m "test: enforce prompt evidence boundaries"
```

---

### Task 5: Final Verification and Mainline Cleanup

**Files:**
- Inspect: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/IDescriptorAuthoringPromptInputHashService.cs`
- Inspect: `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptInputHashService.cs`
- Inspect: `src/Runtime/Agent/CrestCreates.Agent.Prompting`
- Inspect: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests`
- Inspect: `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests`

**Interfaces:**
- Consumes: completed Prompting and Authoring migration.
- Produces: verified single prompt hash mainline.

- [ ] **Step 1: Search for forbidden and duplicate paths**

Run:

```bash
rg -n "IAgentPromptExecutor|IAgentPromptModelClient|IAgentPromptCompletionService|Prompting.Http|DateTimeOffset.UtcNow|JsonSerializer.Serialize\\(request.Payload\\)|CanonicalHashArtifactKind.*AgentPrompt|DescriptorAuthoringPromptInputShapeVersion|ArtifactKindName = \"DescriptorAuthoringPromptInput\"" src tests
```

Expected: no matches except test assertions that forbid those strings. If `DescriptorAuthoringPromptInputShapeVersion` or `ArtifactKindName = "DescriptorAuthoringPromptInput"` still exists in production code, remove the authoring-local hash path or convert it to the Prompting adapter from Task 3.

- [ ] **Step 2: Run focused test suites**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/CrestCreates.Agent.Prompting.Tests.csproj
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CrestCreates.DependencyBoundaries.Tests.csproj
```

Expected: all PASS.

- [ ] **Step 3: Run affected build**

Run:

```bash
dotnet build CrestCreates.slnx
```

Expected: build PASS.

- [ ] **Step 4: Commit any cleanup**

If Step 1 required cleanup, commit it:

```bash
git add src/Runtime/Agent tests/Runtime/Agent tests/Boundary
git commit -m "chore: close prompt evidence hash mainline"
```

If Step 1 required no cleanup, do not create an empty commit.
