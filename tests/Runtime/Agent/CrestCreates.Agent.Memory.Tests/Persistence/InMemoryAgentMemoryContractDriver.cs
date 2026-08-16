using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Memory.Abstractions.Curation;
using CrestCreates.Agent.Memory.Abstractions.Persistence;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Curation;
using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
using CrestCreates.Agent.Memory.Sanitization;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Tests.Persistence;

/// <summary>
/// InMemory implementation of the provider-neutral contract driver. Implements
/// plan preparation through the real shared hash/curation projectors so shared
/// cases exercise the same projection truth as production.
/// </summary>
public sealed class InMemoryAgentMemoryContractDriver : IAgentMemoryStoreContractDriver, IAgentMemoryDurabilityContractDriver
{
    private readonly AgentMemoryCanonicalHashProjector _hashes;
    private readonly DefaultAgentMemoryCurationProjector _projector = new();
    private InMemoryAgentMemoryStore _store = null!;
    private InMemoryAgentConversationStore _conversationStore = null!;
    private InMemoryAgentTaskHistoryStore _taskStore = null!;
    private InMemoryAgentCompressedContextStore _contextStore = null!;
    private RejectingSanitizer _sanitizer = null!;

    public InMemoryAgentMemoryContractDriver(AgentMemoryCanonicalHashProjector hashes)
    {
        _hashes = hashes;
        ResetAsync().AsTask().GetAwaiter().GetResult();
    }

    public IAgentConversationStore ConversationStore => _conversationStore;
    public IAgentTaskHistoryStore TaskStore => _taskStore;
    public IAgentCompressedContextStore ContextStore => _contextStore;
    public IAgentMemoryStore MemoryStore => _store;
    public IAgentMemoryContentSanitizer Sanitizer => _sanitizer;

    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        _sanitizer = new RejectingSanitizer(new DefaultAgentMemoryContentSanitizer(_hashes));
        _conversationStore = new InMemoryAgentConversationStore(_sanitizer);
        _taskStore = new InMemoryAgentTaskHistoryStore(_sanitizer);
        _contextStore = new InMemoryAgentCompressedContextStore();
        _store = new InMemoryAgentMemoryStore(_hashes);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IAgentMemoryStoreContractDriver> CreateFreshReaderAsync(CancellationToken cancellationToken = default)
    {
        // InMemory is semantic-only: a fresh reader observes the same in-memory
        // store instances (no process durability is claimed).
        return ValueTask.FromResult<IAgentMemoryStoreContractDriver>(this);
    }

    public ValueTask RebuildProviderAsync(CancellationToken cancellationToken = default)
    {
        // InMemory has no durable backing; rebuild is an explicit no-op so the
        // durability driver surface stays executable for semantic parity.
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentMemoryRevisionObservation> ReadRawRevisionAsync(
        AgentMemoryArtifactKind artifactKind,
        string tenantId,
        string artifactId,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("InMemory does not expose durable revisions.");

    public AgentMemoryCandidateExpectation PrepareCandidateExpectation(AgentMemoryCandidate candidate)
        => new()
        {
            CandidateId = candidate.CandidateId,
            ExpectedStateHash = _hashes.ComputeCandidateStateHash(candidate)
        };

    public AgentMemoryItemExpectation PrepareMemoryExpectation(AgentMemoryItem memory)
        => new()
        {
            MemoryId = memory.MemoryId,
            ExpectedStateHash = _hashes.ComputeMemoryStateHash(memory)
        };

    public AgentMemoryPromotionPlan PreparePromotionPlan(
        AgentMemoryCandidate candidate,
        string newMemoryId,
        AgentMemoryOperationRequest operation)
    {
        var memory = _projector.ProjectPromotedMemory(candidate, newMemoryId, operation);
        return new AgentMemoryPromotionPlan
        {
            Candidate = PrepareCandidateExpectation(candidate),
            NewMemoryId = newMemoryId,
            ExpectedMemoryContentHash = candidate.CanonicalContentHash,
            ExpectedMemoryStateHash = _hashes.ComputeMemoryStateHash(memory),
            Operation = operation
        };
    }

    public AgentMemorySupersessionPlan PrepareSupersessionPlan(
        AgentMemoryItem targetMemory,
        AgentMemoryCandidate replacementCandidate,
        string newMemoryId,
        AgentMemoryOperationRequest operation)
    {
        var superseding = _projector.ProjectSupersedingMemory(
            replacementCandidate, targetMemory.MemoryId, newMemoryId, operation);
        return new AgentMemorySupersessionPlan
        {
            TargetMemory = PrepareMemoryExpectation(targetMemory),
            ReplacementCandidate = PrepareCandidateExpectation(replacementCandidate),
            NewMemoryId = newMemoryId,
            ExpectedMemoryContentHash = replacementCandidate.CanonicalContentHash,
            ExpectedMemoryStateHash = _hashes.ComputeMemoryStateHash(superseding),
            Operation = operation
        };
    }

    public AgentMemoryItem ProjectPromotedMemory(AgentMemoryCandidate candidate, string newMemoryId, AgentMemoryOperationRequest operation)
        => _projector.ProjectPromotedMemory(candidate, newMemoryId, operation);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Wraps the real sanitizer and rejects the contract sentinel so
    /// shared sanitization cases observe deterministic rejection.</summary>
    private sealed class RejectingSanitizer : IAgentMemoryContentSanitizer
    {
        private readonly IAgentMemoryContentSanitizer _inner;

        public RejectingSanitizer(IAgentMemoryContentSanitizer inner)
        {
            _inner = inner;
        }

        public SanitizedAgentContent Sanitize(string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs)
        {
            if (content.Contains(AgentMemoryPersistenceContractMarkers.RejectedContentSentinel, StringComparison.Ordinal))
            {
                return new SanitizedAgentContent
                {
                    SanitizedContent = string.Empty,
                    CanonicalContentHash = _inner.Sanitize(tenantId, string.Empty, sourceRefs).CanonicalContentHash,
                    Rejected = true,
                    RedactionKinds = [],
                    Diagnostics =
                    [
                        new AgentMemoryDiagnostic
                        {
                            Code = AgentMemoryDiagnosticCodes.ContentRejected,
                            Message = "Content rejected by contract fixture.",
                            Severity = Core.Abstractions.Identity.SeverityLevel.Warning
                        }
                    ]
                };
            }
            return _inner.Sanitize(tenantId, content, sourceRefs);
        }
    }
}
