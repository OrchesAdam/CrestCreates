using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.ContextPack.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class PromptInputHashTests
{
    [Fact]
    public void SameInput_ProducesSameHash()
    {
        using var provider = CreatePromptingProvider();
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(provider.GetRequiredService<IAgentPromptHashService>());
        var input = TestPromptInput();

        var hash1 = hashService.ComputeHash(input);
        var hash2 = hashService.ComputeHash(input);

        hash1.Value.Should().Be(hash2.Value);
    }

    [Fact]
    public void ChangedIntent_ProducesDifferentHash()
    {
        using var provider = CreatePromptingProvider();
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(provider.GetRequiredService<IAgentPromptHashService>());

        var input1 = TestPromptInput("Add finance review");
        var input2 = TestPromptInput("Add legal review");

        var hash1 = hashService.ComputeHash(input1);
        var hash2 = hashService.ComputeHash(input2);

        hash1.Value.Should().NotBe(hash2.Value);
    }

    [Fact]
    public void HashUses_CanonicalHashInfrastructure()
    {
        using var provider = CreatePromptingProvider();
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(provider.GetRequiredService<IAgentPromptHashService>());
        var input = TestPromptInput();

        var hash = hashService.ComputeHash(input);

        hash.Algorithm.Should().Be("SHA-256");
        hash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.AgentPromptInputEvidence);
        hash.Purpose.Should().Be(CanonicalHashPurposeNames.SourceIdentity);
        hash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hash.AlgorithmVersion.Should().Be(DefaultCanonicalHashComputer.AlgorithmVersion);
        hash.ContractVersion.Should().Be("canonical-hash-v1");
        hash.CanonicalShapeVersion.Should().Be("agent-prompt-input-evidence-shape-v1");
    }

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

    private static DescriptorAuthoringPromptInput TestPromptInput(string intent = "Add finance review")
    {
        return new DescriptorAuthoringPromptInput
        {
            ContractVersion = "7g.v1",
            TenantId = "test-tenant",
            IntentText = intent,
            Metadata = new DescriptorAuthoringMetadataContextProjection
            {
                Descriptors = Array.Empty<DescriptorAuthoringDescriptorProjection>(),
                VisibleDescriptorRefs = Array.Empty<DescriptorRef>()
            },
            Memory = new DescriptorAuthoringMemoryProjection
            {
                IsAuthoritative = false,
                Memories = Array.Empty<DescriptorAuthoringMemoryItemProjection>()
            },
            VisibleDescriptorRefs = Array.Empty<DescriptorRef>(),
            SupportedDescriptorKinds = new[] { DescriptorKind.HumanTask, DescriptorKind.Workflow }
        };
    }

    [Fact]
    public void ComputeHash_IsOrderIndependent_ForDescriptorsAndMemories()
    {
        using var provider = CreatePromptingProvider();
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(provider.GetRequiredService<IAgentPromptHashService>());

        var descriptors1 = new List<DescriptorAuthoringDescriptorProjection>
        {
            new() { Ref = new DescriptorRef("workflow", "wf_b", 1), Kind = DescriptorKind.Workflow, Name = "B", ContractHash = null, DefinitionHash = null },
            new() { Ref = new DescriptorRef("humantask", "ht_a", 1), Kind = DescriptorKind.HumanTask, Name = "A", ContractHash = null, DefinitionHash = null }
        };

        var descriptors2 = new List<DescriptorAuthoringDescriptorProjection>
        {
            new() { Ref = new DescriptorRef("humantask", "ht_a", 1), Kind = DescriptorKind.HumanTask, Name = "A", ContractHash = null, DefinitionHash = null },
            new() { Ref = new DescriptorRef("workflow", "wf_b", 1), Kind = DescriptorKind.Workflow, Name = "B", ContractHash = null, DefinitionHash = null }
        };

        var memories1 = new List<DescriptorAuthoringMemoryItemProjection>
        {
            new() { MemoryId = "mem_z", Kind = AgentMemoryKind.ProjectFact, Content = "Z content", Confidence = AgentMemoryConfidence.Medium },
            new() { MemoryId = "mem_a", Kind = AgentMemoryKind.Constraint, Content = "A content", Confidence = AgentMemoryConfidence.High }
        };

        var memories2 = new List<DescriptorAuthoringMemoryItemProjection>
        {
            new() { MemoryId = "mem_a", Kind = AgentMemoryKind.Constraint, Content = "A content", Confidence = AgentMemoryConfidence.High },
            new() { MemoryId = "mem_z", Kind = AgentMemoryKind.ProjectFact, Content = "Z content", Confidence = AgentMemoryConfidence.Medium }
        };

        var input1 = TestPromptInputWithDescriptors(descriptors1, memories1);
        var input2 = TestPromptInputWithDescriptors(descriptors2, memories2);

        var hash1 = hashService.ComputeHash(input1);
        var hash2 = hashService.ComputeHash(input2);

        hash1.Value.Should().Be(hash2.Value);
    }

    private static DescriptorAuthoringPromptInput TestPromptInputWithDescriptors(
        IReadOnlyList<DescriptorAuthoringDescriptorProjection> descriptors,
        IReadOnlyList<DescriptorAuthoringMemoryItemProjection>? memories = null)
    {
        var input = new DescriptorAuthoringPromptInput
        {
            ContractVersion = "7g.v1",
            TenantId = "test-tenant",
            IntentText = "Add finance review",
            Metadata = new DescriptorAuthoringMetadataContextProjection
            {
                Descriptors = descriptors,
                VisibleDescriptorRefs = Array.Empty<DescriptorRef>()
            },
            Memory = new DescriptorAuthoringMemoryProjection
            {
                IsAuthoritative = false,
                Memories = memories ?? Array.Empty<DescriptorAuthoringMemoryItemProjection>(),
                ScopeFingerprint = null,
                VisibleMemorySetHash = null,
                CanonicalPackHash = null
            },
            VisibleDescriptorRefs = Array.Empty<DescriptorRef>(),
            SupportedDescriptorKinds = new[] { DescriptorKind.HumanTask, DescriptorKind.Workflow }
        };
        return input;
    }

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

    [Fact]
    public void Projector_WritesCompleteJsonValue_UnderPayloadProperty()
    {
        using var provider = CreatePromptingProvider();
        var hashService = provider.GetRequiredService<IAgentPromptHashService>();
        var input = TestPromptInput();

        // This should not throw — if the projector wrote malformed JSON,
        // the hash computation would silently produce wrong bytes
        var hash = hashService.ComputeInputHash(new AgentPromptEvidenceCreationRequest<DescriptorAuthoringPromptInput>
        {
            TemplateId = new AgentPromptTemplateId("test-template"),
            TemplateVersion = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7g.v1"),
            ModelProfileRef = new AgentPromptModelProfileRef("default"),
            ProviderProfileRef = new AgentPromptProviderProfileRef("unknown"),
            Payload = input,
            TenantId = "test-tenant"
        });

        hash.Should().NotBeNull();
        hash.Value.Should().NotBeEmpty();
    }

    private static ServiceProvider CreatePromptingProvider()
    {
        var services = new ServiceCollection();
        services.AddAgentPrompting();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
        services.AddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();
        return services.BuildServiceProvider();
    }

    private static AgentAuthoringContext TestAuthoringContext()
    {
        return new AgentAuthoringContext
        {
            Request = new AgentAuthoringRequest
            {
                TenantId = "test-tenant",
                IntentText = "Add finance review"
            },
            MetadataContextPack = new MetadataContextPack
            {
                Request = new MetadataContextPackRequest
                {
                    Scope = MetadataContextPackScope.FocusOnly,
                    FocusDescriptors = Array.Empty<DescriptorRef>()
                },
                Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
                Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
                Summary = new MetadataContextPackSummary
                {
                    TotalDescriptorCount = 0,
                    DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
                    TotalRelationshipCount = 0,
                    RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
                    FocusRefs = Array.Empty<DescriptorRef>(),
                    WasTruncated = false,
                    TruncatedAtCount = null,
                    TraversalDepthReached = 0
                },
                Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
            },
            MemoryPack = new AgentMemoryPack
            {
                TenantId = "test-tenant",
                IsAuthoritative = false
            }
        };
    }

    private static CanonicalHash TestHash(string value) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = DefaultCanonicalHashComputer.AlgorithmVersion,
        ArtifactKind = "Test",
        Scope = CanonicalHashScopeNames.InternalFull,
        Purpose = CanonicalHashPurposeNames.SourceIdentity,
        ContractVersion = "test-v1",
        CanonicalShapeVersion = "test-shape-v1"
    };

    private static AgentPromptEvidenceCreationRequest<DescriptorAuthoringModelResponseEvidenceProjection> OutputRequest(
        DescriptorAuthoringModelResponseEvidenceProjection projection)
    {
        return new AgentPromptEvidenceCreationRequest<DescriptorAuthoringModelResponseEvidenceProjection>
        {
            TemplateId = new AgentPromptTemplateId("test-template"),
            TemplateVersion = new AgentPromptVersion("test-version"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("test-contract"),
            ModelProfileRef = new AgentPromptModelProfileRef("test-model"),
            ProviderProfileRef = new AgentPromptProviderProfileRef("test-provider"),
            Payload = projection
        };
    }
}
