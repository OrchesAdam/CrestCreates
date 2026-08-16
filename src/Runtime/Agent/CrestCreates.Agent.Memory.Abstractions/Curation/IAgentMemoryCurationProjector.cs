using CrestCreates.Agent.Memory.Abstractions.Accountability;

namespace CrestCreates.Agent.Memory.Abstractions.Curation;

/// <summary>
/// Pure lifecycle/graph projection. The only authority for Candidate→Memory
/// payload transfer, <c>PromotedAt = Operation.Identity.OccurredAt</c>,
/// non-authoritative promotion, lifecycle snapshot construction, reciprocal
/// Supersede links, and graph-link preservation during Archive. Performs no
/// Store I/O, locking, resource lookup, expectation comparison, or
/// Accountability.
/// </summary>
public interface IAgentMemoryCurationProjector
{
    AgentMemoryItem ProjectPromotedMemory(
        AgentMemoryCandidate candidate,
        string newMemoryId,
        AgentMemoryOperationRequest operation);

    AgentMemoryCandidate ProjectCandidateStatus(AgentMemoryCandidate candidate, AgentMemoryStatus newStatus);

    AgentMemoryItem ProjectSupersededMemory(AgentMemoryItem current, string newMemoryId);

    AgentMemoryItem ProjectSupersedingMemory(
        AgentMemoryCandidate candidate,
        string targetMemoryId,
        string newMemoryId,
        AgentMemoryOperationRequest operation);

    AgentMemoryItem ProjectArchivedMemory(AgentMemoryItem current);
}
