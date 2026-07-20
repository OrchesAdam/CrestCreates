using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

/// <summary>
/// The sole canonical projector for security-artifact plans. Random handle and
/// grant ids are intentionally excluded; the bound resource graph, principal,
/// scope, provenance, and lifetime policy define retry identity.
/// </summary>
public static class AgentMemoryArtifactPlanProjector
{
    public static string Compute(
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        string purpose,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("shape", "memory-artifact-plan-v3");
            writer.WriteString("purpose", purpose);
            writer.WriteString("tenant", principal.TenantId);
            writer.WriteString("user", principal.UserId);
            writer.WriteString("agent", principal.AgentId);
            writer.WriteString("execution", principal.ExecutionId);
            writer.WriteString("scope", AgentMemoryScopeFingerprint.Compute(scope, principal));
            writer.WriteStartArray("handles");
            foreach (var handle in handles.OrderBy(item => item.ResourceKind).ThenBy(item => item.ResourceId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("kind", handle.ResourceKind.ToString());
                writer.WriteString("resource", handle.ResourceId);
                writer.WriteBoolean("unscoped", handle.IsUnscoped);
                writer.WriteNumber("lifetimeTicks", (handle.ExpiresAt - handle.IssuedAt).Ticks);
                WriteDescriptors(writer, handle.RequiredDescriptorRefs);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("grants");
            foreach (var grant in grants.OrderBy(item => item.SourceRef.SourceKind).ThenBy(item => item.SourceRef.SourceId, StringComparer.Ordinal)
                .ThenBy(item => item.SourceRef.RangeStart).ThenBy(item => item.SourceRef.RangeEnd))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("unscoped", grant.IsUnscoped);
                writer.WriteNumber("lifetimeTicks", (grant.ExpiresAt - grant.IssuedAt).Ticks);
                WriteSource(writer, grant.SourceRef);
                WriteDescriptors(writer, grant.RequiredDescriptorRefs);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteDescriptors(Utf8JsonWriter writer, IReadOnlyList<DescriptorRef> refs)
    {
        writer.WriteStartArray("descriptors");
        foreach (var item in refs.OrderBy(item => item.Namespace, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Version))
        {
            writer.WriteStartObject(); writer.WriteString("namespace", item.Namespace);
            writer.WriteString("id", item.Id); writer.WriteNumber("version", item.Version ?? -1); writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteSource(Utf8JsonWriter writer, AgentContextSourceRef source)
    {
        writer.WriteString("sourceKind", source.SourceKind.ToString());
        writer.WriteString("tenant", source.TenantId); writer.WriteString("source", source.SourceId);
        if (source.RangeStart is int start) writer.WriteNumber("rangeStart", start); else writer.WriteNull("rangeStart");
        if (source.RangeEnd is int end) writer.WriteNumber("rangeEnd", end); else writer.WriteNull("rangeEnd");
        if (source.CorrelationId is not null) writer.WriteString("correlation", source.CorrelationId); else writer.WriteNull("correlation");
        if (source.CausationId is not null) writer.WriteString("causation", source.CausationId); else writer.WriteNull("causation");
        var hash = source.CanonicalContentHash;
        writer.WriteString("contentHash", hash?.Value ?? string.Empty);
        writer.WriteString("contentHashAlgorithm", hash?.Algorithm ?? string.Empty);
        writer.WriteString("contentHashAlgorithmVersion", hash?.AlgorithmVersion ?? string.Empty);
        writer.WriteString("contentHashContractVersion", hash?.ContractVersion ?? string.Empty);
        writer.WriteString("contentHashShapeVersion", hash?.CanonicalShapeVersion ?? string.Empty);
    }
}
