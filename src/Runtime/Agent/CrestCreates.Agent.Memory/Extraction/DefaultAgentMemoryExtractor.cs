using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Identity;
using CrestCreates.Agent.Memory.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Extraction;

public sealed class DefaultAgentMemoryExtractor : IAgentMemoryExtractor
{
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly AgentMemoryCanonicalHashProjector? _hashProjector;

    public DefaultAgentMemoryExtractor(
        IAgentMemoryArtifactIdGenerator? ids = null,
        AgentMemoryCanonicalHashProjector? hashProjector = null)
    {
        _ids = ids ?? new DefaultAgentMemoryArtifactIdGenerator();
        _hashProjector = hashProjector;
    }

    public ValueTask<IReadOnlyList<AgentMemoryCandidate>> ExtractCandidatesAsync(AgentCompressedContext context, CancellationToken cancellationToken = default)
    {
        var candidates = new List<AgentMemoryCandidate>();

        foreach (var block in context.Blocks)
        {
            // Collect redaction-related diagnostics from the block
            var redactionKinds = new List<string>();
            var sanitizationDiagnostics = new List<AgentMemoryDiagnostic>();

            foreach (var diagnostic in block.Diagnostics)
            {
                if (diagnostic.Code == AgentMemoryDiagnosticCodes.ContentRedacted ||
                    diagnostic.Code == AgentMemoryDiagnosticCodes.BlockSanitized ||
                    diagnostic.Code == AgentMemoryDiagnosticCodes.ContentRejected)
                {
                    sanitizationDiagnostics.Add(new AgentMemoryDiagnostic
                    {
                        Code = diagnostic.Code,
                        Message = diagnostic.Message,
                        Severity = diagnostic.Severity,
                        SourceRefs = diagnostic.SourceRefs
                    });
                }
            }

            // Extract redaction kinds from block's source refs if present
            foreach (var sourceRef in block.SourceRefs)
            {
                if (sourceRef.CanonicalContentHash is not null)
                {
                    // Source ref links to sanitized content - propagate metadata
                }
            }

            var candidate = new AgentMemoryCandidate
            {
                CandidateId = _ids.CreateCandidateId(),
                TenantId = context.TenantId,
                Kind = AgentMemoryKind.ProjectFact,
                Content = block.Content,
                CanonicalContentHash = _hashProjector?.ComputeContentHash(context.TenantId, block.SourceRefs, block.Content)
                    ?? block.CanonicalContentHash,
                Confidence = AgentMemoryConfidence.Low,
                SourceRefs = block.SourceRefs.ToArray(),
                RedactionKinds = redactionKinds.ToArray(),
                SanitizationDiagnostics = sanitizationDiagnostics.ToArray()
            };
            candidates.Add(candidate);
        }

        return new ValueTask<IReadOnlyList<AgentMemoryCandidate>>(candidates.ToArray());
    }
}
