using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
using CrestCreates.Agent.Memory.Persistence.Testing.Fixtures;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Persistence.Testing;

/// <summary>
/// Runner-free base for Agent Memory Store contract runners. Concrete xUnit
/// runner classes derive from this base, expose the exact Spec §18.1 skeleton
/// method names as tests, and delegate to the shared
/// <c>AgentMemoryStoreContractCases</c> methods with a real driver.
/// </summary>
public abstract class AgentMemoryStoreContractTestBase<TFixture>
    where TFixture : AgentMemoryPersistenceContractFixture
{
    protected AgentMemoryStoreContractTestBase(TFixture fixture)
    {
        Fixture = fixture;
    }

    protected TFixture Fixture { get; }

    protected abstract IAgentMemoryStoreContractDriver CreateDriver();

    protected IAgentMemoryStoreContractDriver Driver { get; private set; } = null!;

    protected async ValueTask InitializeDriverAsync(CancellationToken cancellationToken = default)
    {
        await Fixture.ResetAsync(cancellationToken).ConfigureAwait(false);
        Driver = CreateDriver();
    }

    protected async ValueTask ResetDriverAsync(CancellationToken cancellationToken = default)
    {
        await Fixture.ResetAsync(cancellationToken).ConfigureAwait(false);
        Driver = CreateDriver();
    }

    // --- provider-neutral fixture builders used by the shared cases ---

    protected static AgentConversationRecord Conversation(
        string tenantId,
        string conversationId,
        params AgentConversationTurn[] turns)
        => new()
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            Turns = turns
        };

    protected static AgentConversationTurn Turn(string tenantId, string turnId, string content, int sequence)
        => new()
        {
            TurnId = turnId,
            TenantId = tenantId,
            Role = sequence % 2 == 0 ? AgentConversationRole.User : AgentConversationRole.Assistant,
            Content = content,
            CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(sequence)
        };

    protected static AgentTaskRecord Task(string tenantId, string taskId, string title)
        => new()
        {
            TenantId = tenantId,
            TaskId = taskId,
            Title = title
        };

    protected static AgentTaskEvent TaskEvent(string tenantId, string taskId, string eventId, string content, int sequence)
        => new()
        {
            EventId = eventId,
            TenantId = tenantId,
            TaskId = taskId,
            EventKind = "event",
            Content = content,
            CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(sequence)
        };

    protected static AgentCompressedContextBlock ContextBlock(string tenantId, string blockId, string content, int ordinal)
        => new()
        {
            BlockId = blockId,
            TenantId = tenantId,
            Content = content,
            CanonicalContentHash = CanonicalHashStub.For($"block-{blockId}"),
            SourceRefs = [new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = tenantId,
                SourceId = $"source-{ordinal}"
            }]
        };

    protected static AgentCompressedContext CompressedContext(
        string tenantId,
        string contextId,
        params AgentCompressedContextBlock[] blocks)
        => new()
        {
            TenantId = tenantId,
            ContextId = contextId,
            Blocks = blocks
        };

    protected static AgentMemoryCandidate Candidate(
        string tenantId,
        string candidateId,
        AgentMemoryKind kind = AgentMemoryKind.Preference)
        => new()
        {
            TenantId = tenantId,
            CandidateId = candidateId,
            Kind = kind,
            Content = $"content-{candidateId}",
            CanonicalContentHash = CanonicalHashStub.For($"candidate-{candidateId}"),
            Confidence = AgentMemoryConfidence.Medium
        };

    protected static AgentMemoryItem Memory(
        string tenantId,
        string memoryId,
        AgentMemoryKind kind = AgentMemoryKind.Preference)
        => new()
        {
            TenantId = tenantId,
            MemoryId = memoryId,
            Kind = kind,
            Content = $"content-{memoryId}",
            CanonicalContentHash = CanonicalHashStub.For($"memory-{memoryId}"),
            Confidence = AgentMemoryConfidence.Medium,
            PromotedAt = DateTimeOffset.UnixEpoch
        };

    protected static AgentMemoryOperationRequest Operation(string tenantId, string operationId)
        => new()
        {
            TenantId = tenantId,
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = tenantId,
                ActorId = "contract-runner",
                ActorKind = "system",
                CorrelationId = $"correlation-{operationId}",
                InvocationSource = "system"
            },
            Reason = "contract case",
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = operationId,
                OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(10)
            },
            Explanation = "contract case explanation"
        };
}

/// <summary>Deterministic canonical hash for provider-neutral fixture data.
/// Shared cases never hard-code state hashes; runners replace them with real
/// projector outputs through the driver preparation methods.</summary>
public static class CanonicalHashStub
{
    public static CanonicalHash For(string value)
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "AgentMemoryContractFixture",
            Scope = "InternalFull",
            Purpose = "ContractFixture",
            ContractVersion = "memory-hash-v1",
            CanonicalShapeVersion = "fixture-v1"
        };
}
