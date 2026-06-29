using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Extraction;

public sealed class DefaultAgentMemoryExtractor : IAgentMemoryExtractor
{
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
                CandidateId = $"candidate_{block.BlockId}",
                TenantId = context.TenantId,
                Kind = AgentMemoryKind.ProjectFact,
                Content = block.Content,
                CanonicalContentHash = block.CanonicalContentHash,
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
