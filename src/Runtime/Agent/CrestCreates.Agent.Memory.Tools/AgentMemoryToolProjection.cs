using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Tools;

public static class AgentMemoryToolProjection
{
    public static AgentMemoryToolCanonicalHashDto ToToolHash(CanonicalHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Value.Length != 64
            || hash.Value.Any(character => !Uri.IsHexDigit(character))
            || !string.Equals(hash.Value, hash.Value.ToLowerInvariant(), StringComparison.Ordinal)
            || hash.AlgorithmVersion != "sha256-canonical-json-v1"
            || hash.ContractVersion != "memory-hash-v2"
            || hash.CanonicalShapeVersion != AgentMemoryCanonicalShapeVersions.MemoryContentV2
            || hash.ArtifactKind != CanonicalHashArtifactNames.AgentMemoryContent
            || hash.Purpose != CanonicalHashPurposeNames.SourceIdentity)
            throw new InvalidOperationException("Canonical content hash is not a valid memory-content-hash-v2 value.");

        return new AgentMemoryToolCanonicalHashDto
        {
            Value = hash.Value,
            AlgorithmVersion = hash.AlgorithmVersion,
            ContractVersion = hash.ContractVersion,
            CanonicalShapeVersion = hash.CanonicalShapeVersion
        };
    }

    public static AgentMemoryToolConfidence ToToolConfidence(AgentMemoryConfidence confidence)
        => confidence switch
        {
            AgentMemoryConfidence.Unknown => AgentMemoryToolConfidence.Unspecified,
            AgentMemoryConfidence.Low => AgentMemoryToolConfidence.Low,
            AgentMemoryConfidence.Medium => AgentMemoryToolConfidence.Medium,
            AgentMemoryConfidence.High => AgentMemoryToolConfidence.High,
            _ => throw new InvalidOperationException("Unsupported memory confidence.")
        };

    public static AgentMemoryToolKind ToToolKind(AgentMemoryKind kind)
        => kind switch
        {
            AgentMemoryKind.Preference => AgentMemoryToolKind.Preference,
            AgentMemoryKind.ProjectFact => AgentMemoryToolKind.ProjectFact,
            AgentMemoryKind.Decision => AgentMemoryToolKind.Decision,
            AgentMemoryKind.Constraint => AgentMemoryToolKind.Constraint,
            AgentMemoryKind.WorkflowHint => AgentMemoryToolKind.WorkflowHint,
            AgentMemoryKind.Risk => AgentMemoryToolKind.Risk,
            _ => throw new InvalidOperationException("Unsupported memory kind.")
        };

    public static AgentMemoryToolSourceKind ToToolSourceKind(AgentSourceKind kind)
        => kind switch
        {
            AgentSourceKind.ConversationTurn => AgentMemoryToolSourceKind.ConversationTurn,
            AgentSourceKind.TaskRecord => AgentMemoryToolSourceKind.TaskRecord,
            AgentSourceKind.TaskEvent => AgentMemoryToolSourceKind.TaskEvent,
            AgentSourceKind.CompressedContextBlock => AgentMemoryToolSourceKind.CompressedContextBlock,
            AgentSourceKind.MemoryCandidate => AgentMemoryToolSourceKind.MemoryCandidate,
            AgentSourceKind.MemoryItem => AgentMemoryToolSourceKind.MemoryItem,
            AgentSourceKind.MetadataContextPack => AgentMemoryToolSourceKind.MetadataContextPack,
            AgentSourceKind.ReviewReport => AgentMemoryToolSourceKind.ReviewReport,
            AgentSourceKind.FixProposal => AgentMemoryToolSourceKind.FixProposal,
            AgentSourceKind.PackagePreview => AgentMemoryToolSourceKind.PackagePreview,
            AgentSourceKind.ActivationRequest => AgentMemoryToolSourceKind.ActivationRequest,
            _ => throw new InvalidOperationException("Unsupported source kind.")
        };

    public static AgentMemoryToolMemoryStatus ToToolMemoryStatus(AgentMemoryStatus status)
        => status switch
        {
            AgentMemoryStatus.Active => AgentMemoryToolMemoryStatus.Active,
            AgentMemoryStatus.Superseded => AgentMemoryToolMemoryStatus.Superseded,
            AgentMemoryStatus.Archived => AgentMemoryToolMemoryStatus.Archived,
            _ => throw new InvalidOperationException("Unsupported Memory lifecycle status.")
        };

    public static AgentMemoryToolCandidateStatus ToToolCandidateStatus(AgentMemoryStatus status)
        => status switch
        {
            AgentMemoryStatus.Candidate => AgentMemoryToolCandidateStatus.Candidate,
            AgentMemoryStatus.Active => AgentMemoryToolCandidateStatus.Active,
            AgentMemoryStatus.Rejected => AgentMemoryToolCandidateStatus.Rejected,
            _ => throw new InvalidOperationException("Unsupported Candidate lifecycle status.")
        };
}
