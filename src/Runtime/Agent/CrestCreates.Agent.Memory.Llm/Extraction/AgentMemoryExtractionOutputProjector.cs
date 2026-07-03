using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Llm.Extraction;

/// <summary>
/// Canonical hash projector for extraction output.
/// Hashes stable identity fields (canonicalContentHash, tenantId, kind, confidence, source ref full identity)
/// without including provider/random IDs (CandidateId), Content, or Diagnostics.
/// </summary>
public sealed class AgentMemoryExtractionOutputProjector : IAgentPromptCanonicalPayloadProjector<IReadOnlyList<AgentMemoryCandidate>>
{
    public void Write(Utf8JsonWriter writer, IReadOnlyList<AgentMemoryCandidate> candidates)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("candidates");
        writer.WriteStartArray();

        // Sort by canonicalContentHash for deterministic ordering (CandidateId is provider/random, not stable)
        foreach (var candidate in candidates.OrderBy(c => c.CanonicalContentHash.Value, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("canonicalContentHash", candidate.CanonicalContentHash.Value);
            writer.WriteString("tenantId", candidate.TenantId);
            writer.WriteString("kind", candidate.Kind.ToString());
            writer.WriteString("confidence", candidate.Confidence.ToString());

            writer.WritePropertyName("sourceRefs");
            writer.WriteStartArray();
            foreach (var sourceRef in candidate.SourceRefs
                .OrderBy(s => s.SourceKind.ToString(), StringComparer.Ordinal)
                .ThenBy(s => s.TenantId, StringComparer.Ordinal)
                .ThenBy(s => s.SourceId, StringComparer.Ordinal)
                .ThenBy(s => s.RangeStart ?? 0)
                .ThenBy(s => s.RangeEnd ?? 0))
            {
                writer.WriteStartObject();
                writer.WriteString("sourceKind", sourceRef.SourceKind.ToString());
                writer.WriteString("sourceId", sourceRef.SourceId);
                writer.WriteString("tenantId", sourceRef.TenantId);
                if (sourceRef.RangeStart is not null)
                    writer.WriteNumber("rangeStart", sourceRef.RangeStart.Value);
                if (sourceRef.RangeEnd is not null)
                    writer.WriteNumber("rangeEnd", sourceRef.RangeEnd.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
