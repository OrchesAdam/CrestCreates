using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Abstractions.Curation;

/// <summary>
/// One Candidate→Memory/lifecycle/graph projection snapshot for a conditional
/// curation operation. Detached snapshots only — no Store I/O, no SQL, no
/// revisions, no timestamps, no Accountability data.
/// </summary>
public sealed record AgentMemoryPromoteMutation
{
    public required AgentMemoryCandidate Candidate { get; init; }
    public required AgentMemoryItem Memory { get; init; }
}

/// <summary>
/// One Candidate rejection projection snapshot.
/// </summary>
public sealed record AgentMemoryRejectMutation
{
    public required AgentMemoryCandidate Candidate { get; init; }
}

/// <summary>
/// The complete three-node Supersede projection: the old Memory becomes
/// Superseded with a reciprocal link, the new Memory is Active with the
/// reverse link, and the replacement Candidate becomes Active.
/// </summary>
public sealed record AgentMemorySupersedeMutation
{
    public required AgentMemoryItem SupersededMemory { get; init; }
    public required AgentMemoryItem SupersedingMemory { get; init; }
    public required AgentMemoryCandidate ReplacementCandidate { get; init; }
}

/// <summary>
/// One Memory Archive projection; existing graph links are retained.
/// </summary>
public sealed record AgentMemoryArchiveMutation
{
    public required AgentMemoryItem Memory { get; init; }
}
