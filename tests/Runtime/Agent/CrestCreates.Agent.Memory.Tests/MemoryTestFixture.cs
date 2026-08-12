using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Moq;

namespace CrestCreates.Agent.Memory.Tests;

/// <summary>
/// Shared deterministic fixtures for Agent Memory tests. The canonical hash
/// computer derives the digest from the projection payload so the same artifact
/// always yields the same hash (required by conditional expectation checks).
/// </summary>
internal static class MemoryTestFixture
{
    public static AgentMemoryCanonicalHashProjector CreateTestHashProjector()
    {
        var hashComputer = new Mock<ICanonicalHashComputer>();
        hashComputer
            .Setup(h => h.ComputeFromProjection(It.IsAny<CanonicalHashProjectionResult>()))
            .Returns((CanonicalHashProjectionResult p) => new CanonicalHash
            {
                Value = ComputeDigest(p),
                Algorithm = "SHA-256",
                AlgorithmVersion = p.Metadata.AlgorithmVersion,
                ArtifactKind = p.Metadata.ArtifactKind,
                Scope = p.Metadata.Scope,
                Purpose = p.Metadata.Purpose,
                ContractVersion = p.Metadata.ContractVersion,
                CanonicalShapeVersion = p.Metadata.CanonicalShapeVersion
            });
        return new AgentMemoryCanonicalHashProjector(hashComputer.Object);
    }

    /// <summary>
    /// Creates a curation pair sharing one projector. Sharing is required: the
    /// promotion service computes a candidate state hash (call A) and the store
    /// recomputes it (call B); different instances yield different values and
    /// break the conditional expectation check.
    /// </summary>
    public static (InMemoryAgentMemoryStore Store, DefaultAgentMemoryPromotionService Promotion) CreateCurationFixture()
    {
        var hashes = CreateTestHashProjector();
        var store = new InMemoryAgentMemoryStore(hashes);
        var promotion = new DefaultAgentMemoryPromotionService(store, hashes: hashes);
        return (store, promotion);
    }

    /// <summary>
    /// Creates a curation pair wired to an Accountability producer and fact
    /// projector. Sharing one projector is required so the promotion service's
    /// expectation computation and the store's recomputation agree.
    /// </summary>
    public static (InMemoryAgentMemoryStore Store, DefaultAgentMemoryPromotionService Promotion) CreateCurationFixture(
        IAgentMemoryAccountabilityProducer producer,
        AgentMemoryCurationFactProjector factProjector)
    {
        var hashes = CreateTestHashProjector();
        var store = new InMemoryAgentMemoryStore(hashes);
        var promotion = new DefaultAgentMemoryPromotionService(store, hashes: hashes, producer: producer, factProjector: factProjector);
        return (store, promotion);
    }

    public static InMemoryAgentMemoryStore CreateTestStore()
    {
        var hashes = CreateTestHashProjector();
        return new InMemoryAgentMemoryStore(hashes);
    }

    public static async ValueTask<AgentMemoryCandidate> CreateCandidateAsync(
        InMemoryAgentMemoryStore store,
        AgentMemoryCanonicalHashProjector hashes,
        string tenantId,
        string candidateId)
    {
        var content = $"memory-content-{candidateId}";
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = candidateId,
            TenantId = tenantId,
            Kind = AgentMemoryKind.Preference,
            Content = content,
            CanonicalContentHash = hashes.ComputeContentHash(tenantId, Array.Empty<AgentContextSourceRef>(), content)
        };
        await store.CreateCandidateAsync(candidate);
        return candidate;
    }

    public static async ValueTask<AgentMemoryItem> PromoteActiveMemoryAsync(
        InMemoryAgentMemoryStore store,
        DefaultAgentMemoryPromotionService promotion,
        AgentMemoryCanonicalHashProjector hashes,
        string tenantId,
        string candidateId,
        string memoryId)
    {
        var candidate = await CreateCandidateAsync(store, hashes, tenantId, candidateId);
        var operation = CreateOperationRequest(tenantId);
        var expectedMemory = CreateExpectedPromotedMemory(candidate, memoryId, operation);
        var plan = new AgentMemoryPromotionPlan
        {
            Candidate = new AgentMemoryCandidateExpectation
            {
                CandidateId = candidate.CandidateId,
                ExpectedStateHash = hashes.ComputeCandidateStateHash(candidate)
            },
            NewMemoryId = memoryId,
            ExpectedMemoryContentHash = candidate.CanonicalContentHash,
            ExpectedMemoryStateHash = hashes.ComputeMemoryStateHash(expectedMemory),
            Operation = operation
        };
        return await promotion.PromoteAsync(tenantId, plan);
    }

    /// <summary>
    /// Replicates DefaultAgentMemoryPromotionService.CreatePromotedMemory so the
    /// expectation the service computes matches what the test computes.
    /// </summary>
    public static AgentMemoryItem CreateExpectedPromotedMemory(AgentMemoryCandidate candidate, string memoryId, AgentMemoryOperationRequest operation)
        => new()
        {
            MemoryId = memoryId,
            TenantId = candidate.TenantId,
            Kind = candidate.Kind,
            Content = candidate.Content,
            CanonicalContentHash = candidate.CanonicalContentHash,
            PromotedAt = operation.Identity.OccurredAt,
            Confidence = candidate.Confidence,
            Status = AgentMemoryStatus.Active,
            IsAuthoritative = false,
            Tags = candidate.Tags,
            DescriptorRefs = candidate.DescriptorRefs,
            SourceRefs = candidate.SourceRefs,
            RedactionKinds = candidate.RedactionKinds,
            SanitizationDiagnostics = candidate.SanitizationDiagnostics
        };

    public static AgentMemoryOperationRequest CreateOperationRequest(string tenantId)
        => new()
        {
            TenantId = tenantId,
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = tenantId,
                ActorId = "agent-1",
                ActorKind = "Agent"
            },
            Reason = "archiving",
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = $"op-{Guid.NewGuid():N}",
                OccurredAt = DateTimeOffset.UtcNow
            },
            Explanation = "Archive contract test"
        };

    private static string ComputeDigest(CanonicalHashProjectionResult projection)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            projection.WriteCanonicalJson(writer);
        }
        var bytes = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }
}
