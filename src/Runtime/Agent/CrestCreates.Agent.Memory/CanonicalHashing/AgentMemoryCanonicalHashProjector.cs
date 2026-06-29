using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.CanonicalHashing;

/// <summary>
/// Computes canonical hashes for agent memory artifacts using ICanonicalHashComputer.
/// All hashes use the canonical JSON + SHA-256 pipeline for determinism and AOT safety.
/// </summary>
public sealed class AgentMemoryCanonicalHashProjector
{
    private readonly ICanonicalHashComputer _hashComputer;
    private const string AlgorithmVersion = "sha256-canonical-json-v1";
    private const string ContractVersion = "memory-hash-v1";

    public AgentMemoryCanonicalHashProjector(ICanonicalHashComputer hashComputer)
    {
        _hashComputer = hashComputer;
    }

    /// <summary>
    /// Computes canonical hash over memory content with structured identity:
    /// tenantId + sourceKind + sourceId + rangeStart + rangeEnd + sanitizedContent
    /// </summary>
    public CanonicalHash ComputeContentHash(
        string tenantId,
        AgentSourceKind sourceKind,
        string sourceId,
        int? rangeStart,
        int? rangeEnd,
        string sanitizedContent)
    {
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.AgentMemoryContent,
                Purpose = CanonicalHashPurposeNames.SourceIdentity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = ContractVersion,
                CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemoryContentV1
            },
            writer => WriteMemoryContentPayload(writer, tenantId, sourceKind, sourceId, rangeStart, rangeEnd, sanitizedContent));

        return _hashComputer.ComputeFromProjection(projection);
    }

    /// <summary>
    /// Computes canonical hash over pack-level content:
    /// tenantId + scopeFingerprint + visibleMemorySetHash + sorted memory hashes
    /// </summary>
    public CanonicalHash ComputePackHash(
        string tenantId,
        CanonicalHash scopeFingerprint,
        CanonicalHash visibleMemorySetHash,
        IReadOnlyList<CanonicalHash> sortedMemoryHashes)
    {
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.AgentMemoryPack,
                Purpose = CanonicalHashPurposeNames.Integrity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = ContractVersion,
                CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemoryPackV1
            },
            writer => WriteMemoryPackPayload(writer, tenantId, scopeFingerprint, visibleMemorySetHash, sortedMemoryHashes));

        return _hashComputer.ComputeFromProjection(projection);
    }

    /// <summary>
    /// Computes scope fingerprint from query parameters
    /// </summary>
    public CanonicalHash ComputeScopeFingerprint(AgentMemoryQuery query)
    {
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.AgentMemoryScope,
                Purpose = CanonicalHashPurposeNames.SourceIdentity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = ContractVersion,
                CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemoryScopeV1
            },
            writer => WriteScopePayload(writer, query));

        return _hashComputer.ComputeFromProjection(projection);
    }

    /// <summary>
    /// Computes visible memory set hash from sorted memory IDs
    /// </summary>
    public CanonicalHash ComputeVisibleMemorySetHash(IReadOnlyList<string> sortedMemoryIds)
    {
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.AgentMemorySet,
                Purpose = CanonicalHashPurposeNames.SourceIdentity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = ContractVersion,
                CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemorySetV1
            },
            writer => WriteMemorySetPayload(writer, sortedMemoryIds));

        return _hashComputer.ComputeFromProjection(projection);
    }

    // --- Canonical JSON payload writers (PascalCase field names per project convention) ---

    private static void WriteMemoryContentPayload(
        Utf8JsonWriter writer,
        string tenantId,
        AgentSourceKind sourceKind,
        string sourceId,
        int? rangeStart,
        int? rangeEnd,
        string sanitizedContent)
    {
        writer.WriteStartObject();
        writer.WriteString("TenantId", tenantId);
        writer.WriteNumber("SourceKind", (int)sourceKind);
        writer.WriteString("SourceId", sourceId);
        writer.WriteNumber("RangeStart", rangeStart ?? -1);
        writer.WriteNumber("RangeEnd", rangeEnd ?? -1);
        writer.WriteString("Content", sanitizedContent);
        writer.WriteEndObject();
    }

    private static void WriteMemoryPackPayload(
        Utf8JsonWriter writer,
        string tenantId,
        CanonicalHash scopeFingerprint,
        CanonicalHash visibleMemorySetHash,
        IReadOnlyList<CanonicalHash> sortedMemoryHashes)
    {
        writer.WriteStartObject();
        writer.WriteString("TenantId", tenantId);
        writer.WriteString("ScopeFingerprint", scopeFingerprint.Value);
        writer.WriteString("VisibleMemorySetHash", visibleMemorySetHash.Value);
        writer.WriteStartArray("MemoryHashes");
        foreach (var hash in sortedMemoryHashes)
        {
            writer.WriteStringValue(hash.Value);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteScopePayload(Utf8JsonWriter writer, AgentMemoryQuery query)
    {
        writer.WriteStartObject();
        writer.WriteString("TenantId", query.TenantId);
        writer.WriteString("IntentText", query.IntentText ?? "");
        WriteEnumArray(writer, "Kinds", query.Kinds.Select(k => (int)k));
        WriteStringArray(writer, "Tags", query.Tags);
        WriteDescriptorRefArray(writer, "DescriptorRefs", query.DescriptorRefs);
        WriteDescriptorRefArray(writer, "VisibleDescriptorRefs", query.VisibleDescriptorRefs);
        WriteEnumArray(writer, "VisibleDescriptorKinds", query.VisibleDescriptorKinds.Select(k => (int)k));
        writer.WriteNumber("MinimumConfidence", (int)query.MinimumConfidence);
        writer.WriteNumber("MaxCount", query.MaxCount ?? -1);
        writer.WriteNumber("CharacterBudget", query.CharacterBudget ?? -1);
        writer.WriteBoolean("IncludeStale", query.IncludeStale);
        writer.WriteBoolean("IncludeSuperseded", query.IncludeSuperseded);
        writer.WriteEndObject();
    }

    private static void WriteMemorySetPayload(Utf8JsonWriter writer, IReadOnlyList<string> sortedMemoryIds)
    {
        writer.WriteStartObject();
        WriteStringArray(writer, "MemoryIds", sortedMemoryIds);
        writer.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string propertyName, IEnumerable<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (var v in values.OrderBy(v => v, StringComparer.Ordinal))
        {
            writer.WriteStringValue(v);
        }
        writer.WriteEndArray();
    }

    private static void WriteEnumArray(Utf8JsonWriter writer, string propertyName, IEnumerable<int> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (var v in values.OrderBy(v => v))
        {
            writer.WriteNumberValue(v);
        }
        writer.WriteEndArray();
    }

    private static void WriteDescriptorRefArray(Utf8JsonWriter writer, string propertyName, IReadOnlyList<DescriptorRef> refs)
    {
        writer.WriteStartArray(propertyName);
        foreach (var r in refs.OrderBy(r => r.ToString(), StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("Namespace", r.Namespace);
            writer.WriteString("Id", r.Id);
            writer.WriteNumber("Version", r.Version ?? -1);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
