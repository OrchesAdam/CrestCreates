using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.Curation;

namespace CrestCreates.Agent.Memory.Curation;

/// <summary>
/// The single pure Candidate→Memory/lifecycle/graph projector. Consumed by the
/// Promotion Service (plan preparation), the curation state machine, and the
/// InMemory Store. Pure singleton: no Store I/O, locking, or Accountability.
/// </summary>
public sealed class DefaultAgentMemoryCurationProjector : IAgentMemoryCurationProjector
{
    public AgentMemoryItem ProjectPromotedMemory(
        AgentMemoryCandidate candidate,
        string newMemoryId,
        AgentMemoryOperationRequest operation)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(operation);
        return new AgentMemoryItem
        {
            MemoryId = newMemoryId,
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
        }.Snapshot();
    }

    public AgentMemoryCandidate ProjectCandidateStatus(AgentMemoryCandidate candidate, AgentMemoryStatus newStatus)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return (candidate with { Status = newStatus }).Snapshot();
    }

    public AgentMemoryItem ProjectSupersededMemory(AgentMemoryItem current, string newMemoryId)
    {
        ArgumentNullException.ThrowIfNull(current);
        return (current with
        {
            Status = AgentMemoryStatus.Superseded,
            SupersededByMemoryId = newMemoryId
        }).Snapshot();
    }

    public AgentMemoryItem ProjectSupersedingMemory(
        AgentMemoryCandidate candidate,
        string targetMemoryId,
        string newMemoryId,
        AgentMemoryOperationRequest operation)
    {
        var memory = ProjectPromotedMemory(candidate, newMemoryId, operation);
        return (memory with { SupersedesMemoryId = targetMemoryId }).Snapshot();
    }

    public AgentMemoryItem ProjectArchivedMemory(AgentMemoryItem current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return (current with { Status = AgentMemoryStatus.Archived }).Snapshot();
    }
}
