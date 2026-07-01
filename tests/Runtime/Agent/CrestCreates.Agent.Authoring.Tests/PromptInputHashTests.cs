using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.ContextPack.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class PromptInputHashTests
{
    [Fact]
    public void SameInput_ProducesSameHash()
    {
        var hashComputer = new DefaultCanonicalHashComputer();
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(hashComputer);
        var input = TestPromptInput();

        var hash1 = hashService.ComputeHash(input);
        var hash2 = hashService.ComputeHash(input);

        hash1.Value.Should().Be(hash2.Value);
    }

    [Fact]
    public void ChangedIntent_ProducesDifferentHash()
    {
        var hashComputer = new DefaultCanonicalHashComputer();
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(hashComputer);

        var input1 = TestPromptInput("Add finance review");
        var input2 = TestPromptInput("Add legal review");

        var hash1 = hashService.ComputeHash(input1);
        var hash2 = hashService.ComputeHash(input2);

        hash1.Value.Should().NotBe(hash2.Value);
    }

    [Fact]
    public void HashUses_CanonicalHashInfrastructure()
    {
        var hashComputer = new DefaultCanonicalHashComputer();
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(hashComputer);
        var input = TestPromptInput();

        var hash = hashService.ComputeHash(input);

        hash.Algorithm.Should().Be("SHA-256");
        hash.ArtifactKind.Should().Be("DescriptorAuthoringPromptInput");
        hash.Purpose.Should().Be(CanonicalHashPurposeNames.SourceIdentity);
        hash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
        hash.ContractVersion.Should().Be("descriptor-authoring-prompt-input-v1");
        hash.CanonicalShapeVersion.Should().Be("descriptor-authoring-prompt-input-shape-v1");
    }

    [Fact]
    public void PromptInputFactory_ProducesInputWithHash()
    {
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(new DefaultCanonicalHashComputer());
        var factory = new DefaultDescriptorAuthoringPromptInputFactory(hashService);
        var context = TestAuthoringContext();

        var input = factory.Create(context);

        input.PromptInputHash.Should().NotBeNull();
        input.PromptInputHash!.Value.Should().NotBeNullOrEmpty();
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
        // Arrange - create two inputs with same content but different order
        var hashComputer = new DefaultCanonicalHashComputer();
        var hashService = new DefaultDescriptorAuthoringPromptInputHashService(hashComputer);

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

        // Act
        var hash1 = hashService.ComputeHash(input1);
        var hash2 = hashService.ComputeHash(input2);

        // Assert
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
}
