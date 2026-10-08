using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.ContextPack.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

/// <summary>
/// #112 Model Request Provenance Activation Probe.
/// Two-phase methodology:
///   Phase 1 (Execution): run authoring, retain only DescriptorAuthoringResult.
///   Phase 2 (Post-hoc): discard original context, reconstruct from evidence summaries only.
/// No production provenance contract is added.
/// </summary>
public sealed class ModelRequestProvenanceActivationProbeFixture
{
    private const string ProbeTenant = "probe-tenant";
    private const string ProbeIntent = "Create a procurement approval workflow for vendor onboarding";

    private static readonly DescriptorRef ProbeDescriptorRef = new("humantask", "ht_vendor_review", 1);

    #region Acceptance Tests

    /// <summary>
    /// Case: Happy — Post-hoc reviewer can explain request from retained evidence only.
    /// Phase 2 discards original context; inspects only DescriptorAuthoringResult evidence summaries.
    /// Classification: A (directly present) for all core identity fields.
    /// </summary>
    [Fact]
    public async Task DescriptorAuthoring_Request_Should_Be_Explainable_From_Existing_PromptEvidence()
    {
        var result = await ExecutePhase1Async();

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded,
            "Authoring must succeed for provenance mainline [A]");
        result.PromptInputEvidence.Should().NotBeNull("Prompt input evidence must exist");
        result.PromptOutputEvidence.Should().NotBeNull("Prompt output evidence must exist");

        var inputEvidence = result.PromptInputEvidence!;
        var outputEvidence = result.PromptOutputEvidence!;

        inputEvidence.TemplateId.Value.Should().NotBeNullOrWhiteSpace(
            "Prompt Template id must be identifiable [A]");
        inputEvidence.TemplateVersion.Value.Should().NotBeNullOrWhiteSpace(
            "Prompt Template version must be identifiable [A]");
        inputEvidence.ContractVersion.Value.Should().NotBeNullOrWhiteSpace(
            "Prompt Contract version must be identifiable [A]");
        inputEvidence.Purpose.Should().Be(AgentPromptPurpose.DescriptorAuthoring,
            "Prompt purpose must be DescriptorAuthoring [A]");
        inputEvidence.ModelProfileRef.Should().NotBeNull(
            "Model Profile ref must be present [A]");
        inputEvidence.ProviderProfileRef.Should().NotBeNull(
            "Provider Profile ref must be present [A]");
        inputEvidence.InputHash.Value.Should().NotBeNullOrWhiteSpace(
            "Input hash must be recorded [A]");

        outputEvidence.InputHash.Value.Should().Be(inputEvidence.InputHash.Value,
            "Output evidence must reference the same input hash [A]");
    }

    /// <summary>
    /// Case: Happy — Post-hoc reconstructed normalized input recomputes to recorded PromptInputHash.
    /// Phase 2 independently builds the prompt input from known parameters and computes hash.
    /// Classification: B (deterministically derivable) for hash recomputation.
    /// </summary>
    [Fact]
    public async Task Reconstructed_NormalizedInput_Should_Recompute_Recorded_PromptInputHash()
    {
        var result = await ExecutePhase1Async();
        var recordedHash = result.PromptInputEvidence!.InputHash.Value;

        var services = BuildServiceProvider();
        var hashService = services.GetRequiredService<IAgentPromptHashService>();
        var optionsAccessor = BuildOptions();

        var reconstructedInput = BuildPromptInputFromKnownParameters();

        var recomputedHash = hashService.ComputeInputHash(
            new AgentPromptEvidenceCreationRequest<DescriptorAuthoringPromptInput>
            {
                TemplateId = optionsAccessor.Value.PromptTemplateId,
                TemplateVersion = optionsAccessor.Value.PromptTemplateVersion,
                Purpose = AgentPromptPurpose.DescriptorAuthoring,
                ContractVersion = optionsAccessor.Value.PromptContractVersion,
                ModelProfileRef = new AgentPromptModelProfileRef(optionsAccessor.Value.ModelProfile.ProfileName),
                ProviderProfileRef = optionsAccessor.Value.ProviderProfileRef,
                Payload = reconstructedInput,
                TenantId = ProbeTenant
            });

        recomputedHash.Should().NotBeNull("Hash service must produce a hash [B]");
        recomputedHash!.Value.Should().Be(recordedHash,
            "Independently reconstructed input must recompute to the recorded historical hash [B]");
    }

    /// <summary>
    /// Case: Happy — Metadata projection with real descriptor is covered by input hash.
    /// Phase 2 confirms hash covers metadata; content is C (hash-verifiable, not payload-reconstructable from summary).
    /// Classification: C for content; A for hash coverage.
    /// </summary>
    [Fact]
    public async Task MetadataProjection_Should_Be_Identifiable_From_Effective_PromptInput()
    {
        var result = await ExecutePhase1Async(includeMetadata: true);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.PromptInputEvidence!.InputHash.Value.Should().NotBeNullOrWhiteSpace(
            "Hash covers metadata projection [A]");

        var services = BuildServiceProvider();
        var hashService = services.GetRequiredService<IAgentPromptHashService>();
        var optionsAccessor = BuildOptions();

        var inputWithMetadata = BuildPromptInputFromKnownParameters(includeMetadata: true);
        var inputWithoutMetadata = BuildPromptInputFromKnownParameters(includeMetadata: false);

        var hashWithMetadata = hashService.ComputeInputHash(
            new AgentPromptEvidenceCreationRequest<DescriptorAuthoringPromptInput>
            {
                TemplateId = optionsAccessor.Value.PromptTemplateId,
                TemplateVersion = optionsAccessor.Value.PromptTemplateVersion,
                Purpose = AgentPromptPurpose.DescriptorAuthoring,
                ContractVersion = optionsAccessor.Value.PromptContractVersion,
                ModelProfileRef = new AgentPromptModelProfileRef(optionsAccessor.Value.ModelProfile.ProfileName),
                ProviderProfileRef = optionsAccessor.Value.ProviderProfileRef,
                Payload = inputWithMetadata,
                TenantId = ProbeTenant
            });

        var hashWithoutMetadata = hashService.ComputeInputHash(
            new AgentPromptEvidenceCreationRequest<DescriptorAuthoringPromptInput>
            {
                TemplateId = optionsAccessor.Value.PromptTemplateId,
                TemplateVersion = optionsAccessor.Value.PromptTemplateVersion,
                Purpose = AgentPromptPurpose.DescriptorAuthoring,
                ContractVersion = optionsAccessor.Value.PromptContractVersion,
                ModelProfileRef = new AgentPromptModelProfileRef(optionsAccessor.Value.ModelProfile.ProfileName),
                ProviderProfileRef = optionsAccessor.Value.ProviderProfileRef,
                Payload = inputWithoutMetadata,
                TenantId = ProbeTenant
            });

        hashWithMetadata!.Value.Should().NotBe(hashWithoutMetadata!.Value,
            "Metadata contribution must change the input hash [C]");
        hashWithMetadata.Value.Should().Be(result.PromptInputEvidence.InputHash.Value,
            "Reconstructed input with metadata must match recorded hash [C]");
    }

    /// <summary>
    /// Case: Happy — Memory projection is covered by input hash.
    /// Phase 2 confirms hash distinguishes memory presence; content is C.
    /// Classification: C for content; A for hash coverage.
    /// </summary>
    [Fact]
    public async Task MemoryProjection_Should_Be_Identifiable_From_Effective_PromptInput()
    {
        var result = await ExecutePhase1Async(
            memoryItems: new[]
            {
                new AgentMemoryItem
                {
                    MemoryId = "mem-vendor-policy",
                    TenantId = ProbeTenant,
                    Kind = AgentMemoryKind.ProjectFact,
                    Content = "Vendor onboarding requires finance approval",
                    CanonicalContentHash = new CanonicalHash
                    {
                        Value = "hash:vendor-policy-content",
                        Algorithm = "test",
                        AlgorithmVersion = "test-v1",
                        ArtifactKind = "Memory",
                        Scope = "Test",
                        Purpose = "Test",
                        ContractVersion = "test-v1",
                        CanonicalShapeVersion = "test-shape-v1"
                    },
                    PromotedAt = DateTimeOffset.UtcNow
                }
            });

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.PromptInputEvidence!.InputHash.Value.Should().NotBeNullOrWhiteSpace(
            "Hash covers memory projection [A]");

        var services = BuildServiceProvider();
        var hashService = services.GetRequiredService<IAgentPromptHashService>();
        var optionsAccessor = BuildOptions();

        var inputWithMemory = BuildPromptInputFromKnownParameters(
            memoryItems: new[]
            {
                new AgentMemoryItem
                {
                    MemoryId = "mem-vendor-policy",
                    TenantId = ProbeTenant,
                    Kind = AgentMemoryKind.ProjectFact,
                    Content = "Vendor onboarding requires finance approval",
                    CanonicalContentHash = new CanonicalHash
                    {
                        Value = "hash:vendor-policy-content",
                        Algorithm = "test",
                        AlgorithmVersion = "test-v1",
                        ArtifactKind = "Memory",
                        Scope = "Test",
                        Purpose = "Test",
                        ContractVersion = "test-v1",
                        CanonicalShapeVersion = "test-shape-v1"
                    },
                    PromotedAt = DateTimeOffset.UtcNow
                }
            });
        var inputWithoutMemory = BuildPromptInputFromKnownParameters();

        var hashWithMemory = hashService.ComputeInputHash(
            new AgentPromptEvidenceCreationRequest<DescriptorAuthoringPromptInput>
            {
                TemplateId = optionsAccessor.Value.PromptTemplateId,
                TemplateVersion = optionsAccessor.Value.PromptTemplateVersion,
                Purpose = AgentPromptPurpose.DescriptorAuthoring,
                ContractVersion = optionsAccessor.Value.PromptContractVersion,
                ModelProfileRef = new AgentPromptModelProfileRef(optionsAccessor.Value.ModelProfile.ProfileName),
                ProviderProfileRef = optionsAccessor.Value.ProviderProfileRef,
                Payload = inputWithMemory,
                TenantId = ProbeTenant
            });

        var hashWithoutMemory = hashService.ComputeInputHash(
            new AgentPromptEvidenceCreationRequest<DescriptorAuthoringPromptInput>
            {
                TemplateId = optionsAccessor.Value.PromptTemplateId,
                TemplateVersion = optionsAccessor.Value.PromptTemplateVersion,
                Purpose = AgentPromptPurpose.DescriptorAuthoring,
                ContractVersion = optionsAccessor.Value.PromptContractVersion,
                ModelProfileRef = new AgentPromptModelProfileRef(optionsAccessor.Value.ModelProfile.ProfileName),
                ProviderProfileRef = optionsAccessor.Value.ProviderProfileRef,
                Payload = inputWithoutMemory,
                TenantId = ProbeTenant
            });

        hashWithMemory!.Value.Should().NotBe(hashWithoutMemory!.Value,
            "Memory contribution must change the input hash [C]");
        hashWithMemory.Value.Should().Be(result.PromptInputEvidence.InputHash.Value,
            "Reconstructed input with memory must match recorded hash [C]");
    }

    /// <summary>
    /// Case: Happy — Prompt Template, Model Profile, and Provider Profile are explainable.
    /// Phase 2 reads directly from retained evidence summaries.
    /// Classification: A (directly present).
    /// </summary>
    [Fact]
    public async Task PromptTemplate_ModelProfile_And_ProviderProfile_Should_Be_Explainable()
    {
        var result = await ExecutePhase1Async();

        var inputEvidence = result.PromptInputEvidence!;

        inputEvidence.TemplateId.Value.Should().Be("descriptor-authoring",
            "Template id must be the configured value [A]");
        inputEvidence.TemplateVersion.Value.Should().NotBeNullOrWhiteSpace(
            "Template version must be present [A]");
        inputEvidence.ModelProfileRef.Should().NotBeNull(
            "Model profile ref must be present [A]");
        inputEvidence.ProviderProfileRef.Should().NotBeNull(
            "Provider profile ref must be present [A]");
        inputEvidence.ContractVersion.Value.Should().NotBeNullOrWhiteSpace(
            "Contract version must be present [A]");
    }

    /// <summary>
    /// Case: Boundary — Provider observation differs from configured profile name.
    /// Phase 2 reads both from retained evidence summaries.
    /// Classification: A for both configured and observed identity.
    /// </summary>
    [Fact]
    public async Task ProviderObservation_Should_Remain_Distinct_From_Configured_Profile()
    {
        var result = await ExecutePhase1Async(
            observedProvider: "actual-provider",
            observedModel: "actual-model");

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);

        var inputEvidence = result.PromptInputEvidence!;
        var outputEvidence = result.PromptOutputEvidence!;

        inputEvidence.ProviderProfileRef.Should().NotBeNull(
            "Configured provider profile ref must be present [A]");
        outputEvidence.ProviderObservation.Should().NotBeNull(
            "Provider observation must be present [A]");
        outputEvidence.ProviderObservation!.ProviderName.Should().Be("actual-provider",
            "Observed provider must reflect actual response [A]");
        outputEvidence.ProviderObservation.ModelName.Should().Be("actual-model",
            "Observed model must reflect actual response [A]");

        inputEvidence.ProviderProfileRef.Value.Should().NotBe("actual-provider",
            "Configured profile ref must remain distinct from observed provider");
    }

    /// <summary>
    /// Case: Boundary — Empty memory projection produces a distinct hash from non-empty.
    /// Phase 2 confirms hash distinguishes empty vs non-empty memory.
    /// Classification: C (hash-verifiable).
    /// </summary>
    [Fact]
    public async Task EmptyMemoryProjection_Should_Remain_Explainable()
    {
        var resultWithEmpty = await ExecutePhase1Async(memoryItems: Array.Empty<AgentMemoryItem>());
        var resultWithContent = await ExecutePhase1Async(
            memoryItems: new[]
            {
                new AgentMemoryItem
                {
                    MemoryId = "mem-test",
                    TenantId = ProbeTenant,
                    Kind = AgentMemoryKind.ProjectFact,
                    Content = "test content",
                    CanonicalContentHash = new CanonicalHash
                    {
                        Value = "hash:test", Algorithm = "test", AlgorithmVersion = "v1",
                        ArtifactKind = "Memory", Scope = "Test", Purpose = "Test",
                        ContractVersion = "v1", CanonicalShapeVersion = "v1"
                    },
                    PromotedAt = DateTimeOffset.UtcNow
                }
            });

        resultWithEmpty.Status.Should().Be(DescriptorAuthoringStatus.Succeeded,
            "Authoring succeeds even with empty memory [A]");
        resultWithEmpty.PromptInputEvidence!.InputHash.Value.Should().NotBeNullOrWhiteSpace(
            "Hash is still computed [A]");

        resultWithEmpty.PromptInputEvidence.InputHash.Value.Should().NotBe(
            resultWithContent.PromptInputEvidence!.InputHash.Value,
            "Empty vs non-empty memory must produce different hashes [C]");
    }

    /// <summary>
    /// Case: Security — Type-level invariant: evidence summary contracts have no raw-credential field.
    /// Narrows claim from runtime leakage to structural type invariant.
    /// Classification: A (type-level absence is directly verifiable).
    /// </summary>
    [Fact]
    public async Task SensitiveProviderCredentials_Should_Not_Appear_In_Evidence()
    {
        var result = await ExecutePhase1Async();

        var inputEvidenceJson = JsonSerializer.Serialize(result.PromptInputEvidence);
        var outputEvidenceJson = JsonSerializer.Serialize(result.PromptOutputEvidence);

        inputEvidenceJson.Should().NotContain("sk-",
            "Input evidence must not contain API key prefixes");
        inputEvidenceJson.Should().NotContain("Bearer ",
            "Input evidence must not contain bearer tokens");

        outputEvidenceJson.Should().NotContain("sk-",
            "Output evidence must not contain API key prefixes");
        outputEvidenceJson.Should().NotContain("Bearer ",
            "Output evidence must not contain bearer tokens");

        var inputEvidenceType = typeof(AgentPromptInputEvidenceSummary);
        inputEvidenceType.GetProperty("ApiKey").Should().BeNull(
            "AgentPromptInputEvidenceSummary must have no ApiKey field [A]");
        inputEvidenceType.GetProperty("Credential").Should().BeNull(
            "AgentPromptInputEvidenceSummary must have no Credential field [A]");
        inputEvidenceType.GetProperty("Secret").Should().BeNull(
            "AgentPromptInputEvidenceSummary must have no Secret field [A]");

        var outputEvidenceType = typeof(AgentPromptOutputEvidenceSummary);
        outputEvidenceType.GetProperty("ApiKey").Should().BeNull(
            "AgentPromptOutputEvidenceSummary must have no ApiKey field [A]");
        outputEvidenceType.GetProperty("Credential").Should().BeNull(
            "AgentPromptOutputEvidenceSummary must have no Credential field [A]");
    }

    /// <summary>
    /// Case: Composition — Output evidence links back to the same input hash.
    /// Phase 2 reads from retained evidence summaries.
    /// Classification: A (directly present in both summaries).
    /// </summary>
    [Fact]
    public async Task OutputEvidence_Should_Link_Back_To_The_Same_InputHash()
    {
        var result = await ExecutePhase1Async();

        var inputHash = result.PromptInputEvidence!.InputHash.Value;
        var outputInputHash = result.PromptOutputEvidence!.InputHash.Value;

        outputInputHash.Should().Be(inputHash,
            "Output evidence InputHash must match input evidence InputHash [A]");

        result.PromptOutputEvidence.TemplateId.Value.Should().Be(
            result.PromptInputEvidence.TemplateId.Value,
            "Output evidence template must match input evidence [A]");
        result.PromptOutputEvidence.ContractVersion.Value.Should().Be(
            result.PromptInputEvidence.ContractVersion.Value,
            "Output evidence contract version must match input evidence [A]");
    }

    /// <summary>
    /// Case: Negative — Missing optional provider-local identifiers.
    /// Phase 2 reads from retained evidence summaries.
    /// Classification: D (adapter-local, not required by current audit semantics).
    /// </summary>
    [Fact]
    public async Task Missing_Optional_ProviderLocal_Identifiers_Should_Not_Automatically_Activate_Provenance()
    {
        var result = await ExecutePhase1Async(
            observedProvider: "minimal-provider",
            observedModel: "minimal-model");

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);

        var outputEvidence = result.PromptOutputEvidence!;

        outputEvidence.ProviderObservation.Should().NotBeNull();
        outputEvidence.ProviderObservation!.ProviderName.Should().Be("minimal-provider");
        outputEvidence.ProviderObservation.ModelName.Should().Be("minimal-model");

        outputEvidence.ProviderObservation.ResponseId.Should().BeNull(
            "ResponseId is optional adapter-local data [D]");
        outputEvidence.ProviderObservation.FinishReason.Should().BeNull(
            "FinishReason is optional adapter-local data [D]");

        result.PromptInputEvidence!.InputHash.Value.Should().NotBeNullOrWhiteSpace(
            "Core evidence is complete even without optional provider-local identifiers");
    }

    #endregion

    #region Phase 1 — Execution

    private static async Task<DescriptorAuthoringResult> ExecutePhase1Async(
        IReadOnlyList<AgentMemoryItem>? memoryItems = null,
        bool includeMetadata = false,
        string? observedProvider = null,
        string? observedModel = null)
    {
        var services = BuildServiceProvider();
        var promptEvidenceFactory = services.GetRequiredService<IAgentPromptEvidenceFactory>();
        var factory = new DefaultDescriptorAuthoringPromptInputFactory();
        var builder = new DefaultDescriptorAuthoringPromptBuilder();
        var parser = new JsonDescriptorAuthoringOutputParser();
        var optionsAccessor = BuildOptions();
        var timeProvider = TimeProvider.System;

        var client = new HashCapturingModelClient(
            observedProvider ?? "recorded-provider",
            observedModel ?? "recorded-model");

        var agent = new LlmDescriptorAuthoringAgent(
            factory, builder, client, parser, promptEvidenceFactory, optionsAccessor, timeProvider);

        var context = BuildAuthoringContext(memoryItems, includeMetadata);

        var result = await agent.AuthorAsync(context);

        return result;
    }

    #endregion

    #region Phase 2 — Post-hoc Reconstruction Helpers

    private static DescriptorAuthoringPromptInput BuildPromptInputFromKnownParameters(
        IReadOnlyList<AgentMemoryItem>? memoryItems = null,
        bool includeMetadata = false)
    {
        var memoryProjection = BuildMemoryProjection(memoryItems);
        var metadataProjection = BuildMetadataProjection(includeMetadata);

        return new DescriptorAuthoringPromptInput
        {
            ContractVersion = "7g.v1",
            TenantId = ProbeTenant,
            IntentText = ProbeIntent,
            Metadata = metadataProjection,
            Memory = memoryProjection,
            VisibleDescriptorRefs = includeMetadata
                ? new[] { ProbeDescriptorRef }
                : Array.Empty<DescriptorRef>(),
            SupportedDescriptorKinds = new[] { DescriptorKind.HumanTask, DescriptorKind.Workflow },
            PromptInputHash = null
        };
    }

    private static DescriptorAuthoringMemoryProjection BuildMemoryProjection(
        IReadOnlyList<AgentMemoryItem>? memoryItems = null)
    {
        var items = memoryItems ?? Array.Empty<AgentMemoryItem>();
        var projections = items.Select(m => new DescriptorAuthoringMemoryItemProjection
        {
            MemoryId = m.MemoryId,
            Kind = m.Kind,
            Content = m.Content,
            CanonicalContentHash = m.CanonicalContentHash
        }).ToArray();

        return new DescriptorAuthoringMemoryProjection
        {
            IsAuthoritative = false,
            Memories = projections
        };
    }

    private static DescriptorAuthoringMetadataContextProjection BuildMetadataProjection(
        bool includeDescriptors)
    {
        if (!includeDescriptors)
        {
            return new DescriptorAuthoringMetadataContextProjection
            {
                Descriptors = Array.Empty<DescriptorAuthoringDescriptorProjection>(),
                VisibleDescriptorRefs = Array.Empty<DescriptorRef>()
            };
        }

        return new DescriptorAuthoringMetadataContextProjection
        {
            Descriptors = new[]
            {
                new DescriptorAuthoringDescriptorProjection
                {
                    Ref = ProbeDescriptorRef,
                    Kind = DescriptorKind.HumanTask,
                    Name = "Vendor Onboarding",
                    ContractHash = new CanonicalHash
                    {
                        Value = "contract-hash:wf_vendor_onboarding:v1",
                        Algorithm = "SHA-256",
                        AlgorithmVersion = "sha256-canonical-json-v1",
                        ArtifactKind = "Descriptor",
                        Scope = "Metadata",
                        Purpose = "Contract",
                        ContractVersion = "7g.v1",
                        CanonicalShapeVersion = "descriptor-contract-v1"
                    },
                    DefinitionHash = new CanonicalHash
                    {
                        Value = "definition-hash:wf_vendor_onboarding:v1",
                        Algorithm = "SHA-256",
                        AlgorithmVersion = "sha256-canonical-json-v1",
                        ArtifactKind = "Descriptor",
                        Scope = "Metadata",
                        Purpose = "Definition",
                        ContractVersion = "7g.v1",
                        CanonicalShapeVersion = "descriptor-definition-v1"
                    }
                }
            },
            VisibleDescriptorRefs = new[] { ProbeDescriptorRef }
        };
    }

    #endregion

    #region Infrastructure

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
        return services.BuildServiceProvider();
    }

    private static IOptions<LlmDescriptorAuthoringAgentOptions> BuildOptions()
    {
        return Options.Create(new LlmDescriptorAuthoringAgentOptions
        {
            ModelProfile = new DescriptorAuthoringModelProfile
            {
                ProfileName = "probe-profile",
                ProviderName = "configured-provider",
                ModelName = "configured-model"
            }
        });
    }

    private static AgentAuthoringContext BuildAuthoringContext(
        IReadOnlyList<AgentMemoryItem>? memoryItems = null,
        bool includeMetadata = false)
    {
        var descriptorEntries = includeMetadata
            ? new[]
            {
                new MetadataContextPackDescriptorEntry
                {
                    Ref = ProbeDescriptorRef,
                    Kind = DescriptorKind.HumanTask,
                    Name = "Vendor Onboarding",
                    State = DescriptorState.Active,
                    IsFocus = true,
                    Hashes = new DescriptorStableHashes
                    {
                        ContractHash = new CanonicalHash
                        {
                            Value = "contract-hash:wf_vendor_onboarding:v1",
                            Algorithm = "SHA-256",
                            AlgorithmVersion = "sha256-canonical-json-v1",
                            ArtifactKind = "Descriptor",
                            Scope = "Metadata",
                            Purpose = "Contract",
                            ContractVersion = "7g.v1",
                            CanonicalShapeVersion = "descriptor-contract-v1"
                        },
                        DefinitionHash = new CanonicalHash
                        {
                            Value = "definition-hash:wf_vendor_onboarding:v1",
                            Algorithm = "SHA-256",
                            AlgorithmVersion = "sha256-canonical-json-v1",
                            ArtifactKind = "Descriptor",
                            Scope = "Metadata",
                            Purpose = "Definition",
                            ContractVersion = "7g.v1",
                            CanonicalShapeVersion = "descriptor-definition-v1"
                        }
                    }
                }
            }
            : Array.Empty<MetadataContextPackDescriptorEntry>();

        var focusRefs = includeMetadata
            ? new[] { ProbeDescriptorRef }
            : Array.Empty<DescriptorRef>();

        var descriptorCounts = includeMetadata
            ? new Dictionary<DescriptorKind, int> { { DescriptorKind.HumanTask, 1 } }
            : new Dictionary<DescriptorKind, int>();

        return new AgentAuthoringContext
        {
            Request = new AgentAuthoringRequest
            {
                TenantId = ProbeTenant,
                IntentText = ProbeIntent
            },
            MetadataContextPack = new MetadataContextPack
            {
                Request = new MetadataContextPackRequest
                {
                    Scope = MetadataContextPackScope.FocusOnly,
                    FocusDescriptors = focusRefs,
                    TenantId = ProbeTenant
                },
                Descriptors = descriptorEntries,
                Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
                Summary = new MetadataContextPackSummary
                {
                    TotalDescriptorCount = descriptorEntries.Length,
                    DescriptorCountsByKind = descriptorCounts,
                    TotalRelationshipCount = 0,
                    RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
                    FocusRefs = focusRefs,
                    WasTruncated = false,
                    TruncatedAtCount = null,
                    TraversalDepthReached = 1
                },
                Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
            },
            MemoryPack = new AgentMemoryPack
            {
                TenantId = ProbeTenant,
                IsAuthoritative = false,
                Memories = memoryItems ?? Array.Empty<AgentMemoryItem>()
            }
        };
    }

    /// <summary>
    /// Hash-capturing model client: captures the computed PromptInputHash from the request
    /// and returns a response JSON with the matching hash, ensuring parser validation passes.
    /// </summary>
    private sealed class HashCapturingModelClient : IDescriptorAuthoringModelClient
    {
        private readonly string _providerName;
        private readonly string _modelName;

        public HashCapturingModelClient(string providerName, string modelName)
        {
            _providerName = providerName;
            _modelName = modelName;
        }

        public Task<DescriptorAuthoringModelResponse> CompleteAsync(
            DescriptorAuthoringModelRequest request,
            CancellationToken cancellationToken = default)
        {
            var hashValue = request.Prompt.PromptInputHash.Value;

            var responseJson = JsonSerializer.Serialize(new
            {
                contractVersion = "7g.v1",
                promptInputHash = hashValue,
                plan = new
                {
                    planId = "probe-plan",
                    intentText = ProbeIntent,
                    assumptions = new[] { "Procurement team available" },
                    plannedDescriptorRefs = new object[]
                    {
                        new { @namespace = "humantask", id = "ht_vendor_review", version = 1 }
                    }
                },
                items = new object[]
                {
                    new
                    {
                        descriptorKind = "HumanTask",
                        descriptorId = "ht_vendor_review",
                        operation = "Create",
                        rationale = "Vendor onboarding requires review",
                        payload = new { id = "ht_vendor_review", name = "Vendor Review", version = 1, permissions = "Vendor.Review" },
                        assumptions = new[] { "Procurement team available" }
                    }
                }
            });

            return Task.FromResult(new DescriptorAuthoringModelResponse
            {
                ResponseText = responseJson,
                ProviderName = _providerName,
                ModelName = _modelName,
                PromptInputHash = request.Prompt.PromptInputHash
            });
        }
    }

    #endregion
}
