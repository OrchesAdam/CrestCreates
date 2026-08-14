using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;

/// <summary>
/// The only authority for computing Candidate/Memory state hashes. Both the
/// Promotion Service (plan preparation) and the conditional curation Stores
/// (locked snapshot validation) consume this interface so preparation and
/// committed projection share one hash truth. SQL never constructs a state
/// hash; the provider persists the digest returned here.
/// </summary>
public interface IAgentMemoryStateHashProjector
{
    CanonicalHash ComputeCandidateStateHash(AgentMemoryCandidate candidate);
    CanonicalHash ComputeMemoryStateHash(AgentMemoryItem memory);
}
