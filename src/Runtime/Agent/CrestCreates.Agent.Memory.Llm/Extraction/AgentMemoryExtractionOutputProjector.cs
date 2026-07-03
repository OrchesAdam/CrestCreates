using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Llm.Extraction;

/// <summary>
/// Canonical hash projector for extraction output.
/// Hashes the structural identity and content hash of candidates
/// (CandidateId, TenantId, Kind, Confidence, CanonicalContentHash, SourceRefIds)
/// without including Content or Diagnostics.
/// </summary>
public sealed class AgentMemoryExtractionOutputProjector : IAgentPromptCanonicalPayloadProjector<IReadOnlyList<AgentMemoryCandidate>>
{
    public void Write(Utf8JsonWriter writer, IReadOnlyList<AgentMemoryCandidate> candidates)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("candidates");
        writer.WriteStartArray();

        foreach (var candidate in candidates.OrderBy(c => c.CandidateId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("candidateId", candidate.CandidateId);
            writer.WriteString("tenantId", candidate.TenantId);
            writer.WriteString("kind", candidate.Kind.ToString());
            writer.WriteString("confidence", candidate.Confidence.ToString());
            writer.WriteString("canonicalContentHash", candidate.CanonicalContentHash.Value);

            writer.WritePropertyName("sourceRefIds");
            writer.WriteStartArray();
            foreach (var refId in candidate.SourceRefs.Select(s => s.SourceId).OrderBy(id => id, StringComparer.Ordinal))
            {
                writer.WriteStringValue(refId);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
