using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Extraction;

public sealed class DefaultAgentMemoryExtractor : IAgentMemoryExtractor
{
    public ValueTask<IReadOnlyList<AgentMemoryCandidate>> ExtractCandidatesAsync(AgentCompressedContext context, CancellationToken cancellationToken = default)
    {
        var candidates = new List<AgentMemoryCandidate>();

        foreach (var block in context.Blocks)
        {
            var candidate = new AgentMemoryCandidate
            {
                CandidateId = $"candidate_{block.BlockId}",
                TenantId = context.TenantId,
                Kind = AgentMemoryKind.ProjectFact,
                Content = block.Content,
                CanonicalContentHash = block.CanonicalContentHash,
                Confidence = AgentMemoryConfidence.Low,
                SourceRefs = block.SourceRefs.ToArray()
            };
            candidates.Add(candidate);
        }

        return new ValueTask<IReadOnlyList<AgentMemoryCandidate>>(candidates.ToArray());
    }
}
