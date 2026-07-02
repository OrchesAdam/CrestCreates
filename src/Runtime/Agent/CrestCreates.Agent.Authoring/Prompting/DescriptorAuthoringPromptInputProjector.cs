using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Authoring.Prompting;

public sealed class DescriptorAuthoringPromptInputProjector : IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>
{
    public void Write(Utf8JsonWriter writer, DescriptorAuthoringPromptInput input)
    {
        writer.WriteStartObject();

        writer.WriteString("contractVersion", input.ContractVersion);
        writer.WriteString("tenantId", input.TenantId);
        writer.WriteString("intentText", input.IntentText);

        WriteMetadataProjection(writer, input.Metadata);
        WriteMemoryProjection(writer, input.Memory);

        WriteDescriptorRefList(writer, "visibleDescriptorRefs", input.VisibleDescriptorRefs);
        WriteDescriptorKindList(writer, "supportedDescriptorKinds", input.SupportedDescriptorKinds);

        writer.WriteEndObject();
    }

    private static void WriteMetadataProjection(Utf8JsonWriter writer, DescriptorAuthoringMetadataContextProjection metadata)
    {
        writer.WriteStartArray("descriptors");
        foreach (var d in metadata.Descriptors.OrderBy(d => d.Ref.Namespace).ThenBy(d => d.Ref.Id).ThenBy(d => d.Ref.Version))
        {
            writer.WriteStartObject();
            writer.WriteString("namespace", d.Ref.Namespace);
            writer.WriteString("id", d.Ref.Id);
            if (d.Ref.Version.HasValue)
            {
                writer.WriteNumber("version", d.Ref.Version.Value);
            }
            writer.WriteString("kind", d.Kind.ToString());
            writer.WriteString("name", d.Name);
            if (d.ContractHash is not null)
            {
                writer.WriteString("contractHash", d.ContractHash.Value);
            }
            if (d.DefinitionHash is not null)
            {
                writer.WriteString("definitionHash", d.DefinitionHash.Value);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteMemoryProjection(Utf8JsonWriter writer, DescriptorAuthoringMemoryProjection memory)
    {
        writer.WriteStartObject("memory");
        writer.WriteBoolean("isAuthoritative", memory.IsAuthoritative);

        if (memory.ScopeFingerprint is not null)
        {
            writer.WriteString("scopeFingerprint", memory.ScopeFingerprint.Value);
        }
        if (memory.VisibleMemorySetHash is not null)
        {
            writer.WriteString("visibleMemorySetHash", memory.VisibleMemorySetHash.Value);
        }
        if (memory.CanonicalPackHash is not null)
        {
            writer.WriteString("canonicalPackHash", memory.CanonicalPackHash.Value);
        }

        writer.WriteStartArray("memories");
        foreach (var m in memory.Memories.OrderBy(m => m.MemoryId))
        {
            writer.WriteStartObject();
            writer.WriteString("memoryId", m.MemoryId);
            writer.WriteString("kind", m.Kind.ToString());
            writer.WriteString("content", m.Content);
            writer.WriteString("confidence", m.Confidence.ToString());
            if (m.CanonicalContentHash is not null)
            {
                writer.WriteString("canonicalContentHash", m.CanonicalContentHash.Value);
            }
            WriteDescriptorRefList(writer, "descriptorRefs", m.DescriptorRefs);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteDescriptorRefList(Utf8JsonWriter writer, string propertyName, IReadOnlyList<DescriptorRef> refs)
    {
        writer.WriteStartArray(propertyName);
        foreach (var r in refs.OrderBy(r => r.Namespace).ThenBy(r => r.Id).ThenBy(r => r.Version))
        {
            writer.WriteStartObject();
            writer.WriteString("namespace", r.Namespace);
            writer.WriteString("id", r.Id);
            if (r.Version.HasValue)
            {
                writer.WriteNumber("version", r.Version.Value);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteDescriptorKindList(Utf8JsonWriter writer, string propertyName, IReadOnlyList<DescriptorKind> kinds)
    {
        writer.WriteStartArray(propertyName);
        foreach (var k in kinds.OrderBy(k => k.ToString(), StringComparer.Ordinal))
        {
            writer.WriteStringValue(k.ToString());
        }
        writer.WriteEndArray();
    }
}
