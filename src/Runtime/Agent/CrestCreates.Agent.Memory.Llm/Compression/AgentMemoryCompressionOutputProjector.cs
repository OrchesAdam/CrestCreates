using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Llm.Compression;

/// <summary>
/// Canonical hash projector for compression output.
/// Hashes stable identity fields (canonicalContentHash, tenantId, source ref full identity)
/// without including provider/random IDs (BlockId), Content, or Diagnostics.
/// </summary>
public sealed class AgentMemoryCompressionOutputProjector : IAgentPromptCanonicalPayloadProjector<IReadOnlyList<AgentCompressedContextBlock>>
{
    public void Write(Utf8JsonWriter writer, IReadOnlyList<AgentCompressedContextBlock> blocks)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("blocks");
        writer.WriteStartArray();

        // Sort by canonicalContentHash for deterministic ordering (BlockId is provider/random, not stable)
        foreach (var block in blocks.OrderBy(b => b.CanonicalContentHash.Value, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("canonicalContentHash", block.CanonicalContentHash.Value);
            writer.WriteString("tenantId", block.TenantId);

            writer.WritePropertyName("sourceRefs");
            writer.WriteStartArray();
            foreach (var sourceRef in block.SourceRefs.OrderBy(s => s.SourceId, StringComparer.Ordinal))
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
