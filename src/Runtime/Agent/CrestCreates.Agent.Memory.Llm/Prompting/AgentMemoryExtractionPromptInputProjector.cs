using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Llm.Prompting;

public sealed class AgentMemoryExtractionPromptInputProjector : IAgentPromptCanonicalPayloadProjector<AgentMemoryExtractionPromptInput>
{
    public void Write(Utf8JsonWriter writer, AgentMemoryExtractionPromptInput input)
    {
        writer.WriteStartObject();

        // Alphabetical: blocks, maxCandidateCount, purpose, tenantId
        writer.WriteStartArray("blocks");
        foreach (var block in input.Blocks.OrderBy(b => b.BlockId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();

            // Alphabetical: blockId, canonicalContentHash, content, diagnostics, sourceRefs, tenantId
            writer.WriteString("blockId", block.BlockId);
            writer.WriteString("canonicalContentHash", block.CanonicalContentHash.Value);
            writer.WriteString("content", block.Content);

            if (block.Diagnostics is { Count: > 0 })
            {
                writer.WriteStartArray("diagnostics");
                foreach (var d in block.Diagnostics.OrderBy(d => d.Code.ToString(), StringComparer.Ordinal)
                             .ThenBy(d => d.Message, StringComparer.Ordinal))
                {
                    writer.WriteStartObject();
                    // Alphabetical: code, message, severity, sourceRefs
                    writer.WriteString("code", d.Code.ToString());
                    writer.WriteString("message", d.Message);
                    writer.WriteString("severity", d.Severity.ToString());
                    if (d.SourceRefs is { Count: > 0 })
                    {
                        WriteSourceRefs(writer, "sourceRefs", d.SourceRefs);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            if (block.SourceRefs is { Count: > 0 })
            {
                WriteSourceRefs(writer, "sourceRefs", block.SourceRefs);
            }

            writer.WriteString("tenantId", block.TenantId);

            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteNumber("maxCandidateCount", input.MaxCandidateCount);
        if (input.Purpose is not null)
        {
            writer.WriteString("purpose", input.Purpose);
        }
        writer.WriteString("tenantId", input.TenantId);

        writer.WriteEndObject();
    }

    private static void WriteSourceRefs(Utf8JsonWriter writer, string propertyName, IReadOnlyList<AgentContextSourceRef> sourceRefs)
    {
        writer.WriteStartArray(propertyName);
        foreach (var sr in sourceRefs.OrderBy(s => s.SourceKind)
                     .ThenBy(s => s.TenantId, StringComparer.Ordinal)
                     .ThenBy(s => s.SourceId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();

            // Alphabetical: causationId, canonicalContentHash, correlationId,
            //   descriptorRefs, rangeEnd, rangeStart, sourceId, sourceKind, tenantId
            if (sr.CausationId is not null)
                writer.WriteString("causationId", sr.CausationId);
            if (sr.CanonicalContentHash is not null)
                writer.WriteString("canonicalContentHash", sr.CanonicalContentHash.Value);
            if (sr.CorrelationId is not null)
                writer.WriteString("correlationId", sr.CorrelationId);

            if (sr.DescriptorRefs is { Count: > 0 })
            {
                writer.WriteStartArray("descriptorRefs");
                foreach (var dr in sr.DescriptorRefs.OrderBy(d => d.Namespace, StringComparer.Ordinal)
                             .ThenBy(d => d.Id, StringComparer.Ordinal)
                             .ThenBy(d => d.Version))
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", dr.Id);
                    writer.WriteString("namespace", dr.Namespace);
                    if (dr.Version.HasValue)
                        writer.WriteNumber("version", dr.Version.Value);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            if (sr.RangeEnd.HasValue)
                writer.WriteNumber("rangeEnd", sr.RangeEnd.Value);
            if (sr.RangeStart.HasValue)
                writer.WriteNumber("rangeStart", sr.RangeStart.Value);
            writer.WriteString("sourceId", sr.SourceId);
            writer.WriteString("sourceKind", sr.SourceKind.ToString());
            writer.WriteString("tenantId", sr.TenantId);

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
