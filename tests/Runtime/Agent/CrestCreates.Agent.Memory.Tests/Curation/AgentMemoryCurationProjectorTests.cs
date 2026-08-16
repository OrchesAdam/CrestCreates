using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Curation;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests.Curation;

public sealed class AgentMemoryCurationProjectorTests
{
    private readonly DefaultAgentMemoryCurationProjector _projector = new();

    [Fact]
    public void ProjectPromotedMemory_Should_TransferEveryApprovedPayloadAndProvenanceField()
    {
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "c-1",
            TenantId = "tenant-1",
            Kind = AgentMemoryKind.Decision,
            Content = "content",
            CanonicalContentHash = TestHash("content-hash"),
            Confidence = AgentMemoryConfidence.High,
            Tags = ["tag-a", "tag-b"],
            DescriptorRefs = [new DescriptorRef("ns", "d-1", 1)],
            SourceRefs = [new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "tenant-1",
                SourceId = "source-1",
                RangeStart = 0,
                RangeEnd = 1
            }],
            RedactionKinds = ["credential"],
            SanitizationDiagnostics = [new AgentMemoryDiagnostic
            {
                Code = AgentMemoryDiagnosticCodes.ContentRedacted,
                Message = "redacted",
                Severity = SeverityLevel.Info
            }]
        };
        var operation = CreateOperation("tenant-1");

        var memory = _projector.ProjectPromotedMemory(candidate, "m-1", operation);

        memory.MemoryId.Should().Be("m-1");
        memory.TenantId.Should().Be(candidate.TenantId);
        memory.Kind.Should().Be(candidate.Kind);
        memory.Content.Should().Be(candidate.Content);
        memory.CanonicalContentHash.Should().Be(candidate.CanonicalContentHash);
        memory.Confidence.Should().Be(candidate.Confidence);
        memory.Tags.Should().BeEquivalentTo(candidate.Tags);
        memory.DescriptorRefs.Should().BeEquivalentTo(candidate.DescriptorRefs);
        memory.SourceRefs.Should().BeEquivalentTo(candidate.SourceRefs);
        memory.RedactionKinds.Should().BeEquivalentTo(candidate.RedactionKinds);
        memory.SanitizationDiagnostics.Should().BeEquivalentTo(candidate.SanitizationDiagnostics);
    }

    [Fact]
    public void ProjectPromotedMemory_Should_UseOperationOccurredAtForPromotedAt()
    {
        var candidate = Candidate("tenant-1", "c-1");
        var operation = CreateOperation("tenant-1");

        var memory = _projector.ProjectPromotedMemory(candidate, "m-1", operation);

        memory.PromotedAt.Should().Be(operation.Identity.OccurredAt);
        memory.PromotedAt.Should().NotBe(default);
    }

    [Fact]
    public void ProjectPromotedMemory_Should_ProduceActiveNonAuthoritativeMemory()
    {
        var candidate = Candidate("tenant-1", "c-1");

        var memory = _projector.ProjectPromotedMemory(candidate, "m-1", CreateOperation("tenant-1"));

        memory.Status.Should().Be(AgentMemoryStatus.Active);
        memory.IsAuthoritative.Should().BeFalse();
    }

    [Fact]
    public void ProjectCandidateStatus_Should_Not_MutateInput()
    {
        var candidate = Candidate("tenant-1", "c-1");
        var original = candidate.Snapshot();

        var projected = _projector.ProjectCandidateStatus(candidate, AgentMemoryStatus.Rejected);

        projected.Status.Should().Be(AgentMemoryStatus.Rejected);
        candidate.Status.Should().Be(AgentMemoryStatus.Candidate);
        candidate.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Supersede_Should_Produce_ReciprocalLinksAndCorrectStatuses()
    {
        var current = new AgentMemoryItem
        {
            MemoryId = "m-old",
            TenantId = "tenant-1",
            Kind = AgentMemoryKind.Preference,
            Content = "old",
            CanonicalContentHash = TestHash("old-hash"),
            PromotedAt = DateTimeOffset.UnixEpoch,
            Status = AgentMemoryStatus.Active
        };
        var replacement = Candidate("tenant-1", "c-replacement", AgentMemoryKind.Decision);

        var superseded = _projector.ProjectSupersededMemory(current, "m-new");
        var superseding = _projector.ProjectSupersedingMemory(
            replacement, current.MemoryId, "m-new", CreateOperation("tenant-1"));

        superseded.Status.Should().Be(AgentMemoryStatus.Superseded);
        superseded.SupersededByMemoryId.Should().Be("m-new");
        superseded.SupersedesMemoryId.Should().BeNull();
        superseded.Content.Should().Be("old");

        superseding.Status.Should().Be(AgentMemoryStatus.Active);
        superseding.SupersedesMemoryId.Should().Be("m-old");
        superseding.SupersededByMemoryId.Should().BeNull();
        superseding.IsAuthoritative.Should().BeFalse();
    }

    [Fact]
    public void Archive_Should_RetainBothGraphLinks()
    {
        var current = new AgentMemoryItem
        {
            MemoryId = "m-1",
            TenantId = "tenant-1",
            Kind = AgentMemoryKind.Preference,
            Content = "content",
            CanonicalContentHash = TestHash("hash"),
            PromotedAt = DateTimeOffset.UnixEpoch,
            Status = AgentMemoryStatus.Superseded,
            SupersedesMemoryId = "m-old",
            SupersededByMemoryId = "m-new"
        };

        var archived = _projector.ProjectArchivedMemory(current);

        archived.Status.Should().Be(AgentMemoryStatus.Archived);
        archived.SupersedesMemoryId.Should().Be("m-old");
        archived.SupersededByMemoryId.Should().Be("m-new");
    }

    [Fact]
    public void ProjectedSnapshots_Should_Not_ExposeCallerCollections()
    {
        var tags = new List<string> { "tag-a" };
        var candidate = Candidate("tenant-1", "c-1") with { Tags = tags };

        var memory = _projector.ProjectPromotedMemory(candidate, "m-1", CreateOperation("tenant-1"));

        tags.Add("tag-mutated");
        memory.Tags.Should().HaveCount(1);
        memory.Tags.Should().Contain("tag-a");
    }

    private static AgentMemoryCandidate Candidate(string tenantId, string candidateId, AgentMemoryKind kind = AgentMemoryKind.Preference)
        => new()
        {
            TenantId = tenantId,
            CandidateId = candidateId,
            Kind = kind,
            Content = $"content-{candidateId}",
            CanonicalContentHash = TestHash($"candidate-{candidateId}"),
            Confidence = AgentMemoryConfidence.Medium
        };

    private static AgentMemoryOperationRequest CreateOperation(string tenantId)
        => new()
        {
            TenantId = tenantId,
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = tenantId,
                ActorId = "actor-1",
                ActorKind = "system",
                CorrelationId = "correlation-1",
                InvocationSource = "system"
            },
            Reason = "test",
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = "op-1",
                OccurredAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero)
            },
            Explanation = "test"
        };

    private static Metadata.Abstractions.CanonicalHashing.CanonicalHash TestHash(string value)
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "AgentMemoryTest",
            Scope = "InternalFull",
            Purpose = "Test",
            ContractVersion = "memory-hash-v1",
            CanonicalShapeVersion = "test-v1"
        };
}
