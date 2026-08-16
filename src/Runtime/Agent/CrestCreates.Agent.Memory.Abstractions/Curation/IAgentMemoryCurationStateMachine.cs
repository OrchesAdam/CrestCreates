using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Abstractions.Curation;

/// <summary>
/// Conditional curation validation + projection over already-loaded locked
/// snapshots. The Store owns resource existence, identity availability,
/// locking, and persistence; the state machine owns Tenant comparison,
/// expected state-hash comparison, allowed lifecycle sources, canonical
/// content-hash comparison, and expected new-state hash validation. It never
/// receives an optional/missing row and never queries an identity registry.
/// </summary>
public interface IAgentMemoryCurationStateMachine
{
    AgentMemoryPromoteMutation PreparePromote(
        string tenantId,
        AgentMemoryCandidate candidate,
        AgentMemoryPromotionPlan plan);

    AgentMemoryRejectMutation PrepareReject(
        string tenantId,
        AgentMemoryCandidate candidate,
        AgentMemoryCandidateExpectation expectation);

    AgentMemorySupersedeMutation PrepareSupersede(
        string tenantId,
        AgentMemoryItem targetMemory,
        AgentMemoryCandidate replacementCandidate,
        AgentMemorySupersessionPlan plan);

    AgentMemoryArchiveMutation PrepareArchive(
        string tenantId,
        AgentMemoryItem memory,
        AgentMemoryItemExpectation expectation);
}
