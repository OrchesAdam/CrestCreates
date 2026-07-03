using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Llm.Prompting;

public sealed class AgentMemoryCompressionPromptInputProjector : IAgentPromptCanonicalPayloadProjector<AgentMemoryCompressionPromptInput>
{
    public void Write(Utf8JsonWriter writer, AgentMemoryCompressionPromptInput input)
    {
        writer.WriteStartObject();

        // Alphabetical: maxOutputCharacters, purpose, sources, tenantId
        writer.WriteNumber("maxOutputCharacters", input.MaxOutputCharacters);
        if (input.Purpose is not null)
        {
            writer.WriteString("purpose", input.Purpose);
        }

        writer.WriteStartArray("sources");
        foreach (var source in input.Sources.OrderBy(s => s.SourceRefId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();

            // Alphabetical: redactionKinds, sanitizedContent, sourceRefId, sourceRefs
            if (source.RedactionKinds is { Count: > 0 })
            {
                writer.WriteStartArray("redactionKinds");
                foreach (var kind in source.RedactionKinds.OrderBy(k => k, StringComparer.Ordinal))
                {
                    writer.WriteStringValue(kind);
                }
                writer.WriteEndArray();
            }

            writer.WriteString("sanitizedContent", source.SanitizedContent);
            writer.WriteString("sourceRefId", source.SourceRefId);

            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteString("tenantId", input.TenantId);

        writer.WriteEndObject();
    }
}
