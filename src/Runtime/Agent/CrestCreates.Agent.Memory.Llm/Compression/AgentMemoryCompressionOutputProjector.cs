using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Llm.Compression;

/// <summary>
/// Canonical hash projector for compression output.
/// Hashes the structural identity and content hash of compressed blocks
/// (BlockId, TenantId, CanonicalContentHash, SourceRefIds)
/// without including Content or Diagnostics (which are variable/computable).
/// </summary>
public sealed class AgentMemoryCompressionOutputProjector : IAgentPromptCanonicalPayloadProjector<IReadOnlyList<AgentCompressedContextBlock>>
{
    public void Write(Utf8JsonWriter writer, IReadOnlyList<AgentCompressedContextBlock> blocks)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("blocks");
        writer.WriteStartArray();

        foreach (var block in blocks.OrderBy(b => b.BlockId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("blockId", block.BlockId);
            writer.WriteString("canonicalContentHash", block.CanonicalContentHash.Value);
            writer.WriteString("tenantId", block.TenantId);

            writer.WritePropertyName("sourceRefIds");
            writer.WriteStartArray();
            foreach (var refId in block.SourceRefs.Select(s => s.SourceId).OrderBy(id => id, StringComparer.Ordinal))
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
