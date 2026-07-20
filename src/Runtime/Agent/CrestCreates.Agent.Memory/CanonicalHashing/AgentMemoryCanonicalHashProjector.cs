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
    /// Computes the v2 content hash. Unlike the legacy overload, the complete
    /// trusted provenance set participates in the digest. References are
    /// canonicalized, ordinal-deduplicated, and sorted before projection.
    /// </summary>
    public CanonicalHash ComputeContentHash(
        string tenantId,
        IReadOnlyList<AgentContextSourceRef> sourceRefs,
        string sanitizedContent)
    {
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.AgentMemoryContent,
                Purpose = CanonicalHashPurposeNames.SourceIdentity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = "memory-hash-v2",
                CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemoryContentV2
            },
            writer => WriteMemoryContentV2Payload(writer, tenantId, sourceRefs, sanitizedContent));

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
        => ComputePackHash(tenantId, scopeFingerprint, visibleMemorySetHash, sortedMemoryHashes, sortedMemoryHashes.Count, false, false);

    public CanonicalHash ComputePackHash(
        string tenantId,
        CanonicalHash scopeFingerprint,
        CanonicalHash visibleMemorySetHash,
        IReadOnlyList<CanonicalHash> returnedMemoryHashes,
        int returnedCount,
        bool wasTruncated,
        bool isAuthoritative)
    {
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.AgentMemoryPack,
                Purpose = CanonicalHashPurposeNames.Integrity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = "memory-hash-v2",
                CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemoryPackV2
            },
            writer => WriteMemoryPackPayload(writer, tenantId, scopeFingerprint, visibleMemorySetHash, returnedMemoryHashes, returnedCount, wasTruncated, isAuthoritative));

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
                ContractVersion = "memory-hash-v2",
                CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemoryScopeV2
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
                CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemorySetV2
            },
            writer => WriteMemorySetPayload(writer, sortedMemoryIds));

        return _hashComputer.ComputeFromProjection(projection);
    }

    public CanonicalHash ComputeCandidateStateHash(AgentMemoryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.AgentMemoryCandidateState,
                Purpose = CanonicalHashPurposeNames.Integrity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = "memory-state-v1",
                CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemoryCandidateStateV1
            },
            writer => WriteCandidateStatePayload(writer, candidate));
        return _hashComputer.ComputeFromProjection(projection);
    }

    public CanonicalHash ComputeMemoryStateHash(AgentMemoryItem memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.AgentMemoryItemState,
                Purpose = CanonicalHashPurposeNames.Integrity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = "memory-state-v1",
                CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemoryItemStateV1
            },
            writer => WriteMemoryStatePayload(writer, memory));
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

    private static void WriteMemoryContentV2Payload(
        Utf8JsonWriter writer,
        string tenantId,
        IReadOnlyList<AgentContextSourceRef> sourceRefs,
        string sanitizedContent)
    {
        writer.WriteStartObject();
        writer.WriteString("TenantId", tenantId);
        writer.WriteString("Content", sanitizedContent);
        writer.WriteStartArray("SourceRefs");
        foreach (var sourceRef in sourceRefs
            .Select(CanonicalSourceRef)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal))
        {
            writer.WriteStringValue(sourceRef);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string CanonicalSourceRef(AgentContextSourceRef sourceRef)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("SourceKind", (int)sourceRef.SourceKind);
            writer.WriteString("TenantId", sourceRef.TenantId);
            writer.WriteString("SourceId", sourceRef.SourceId);
            writer.WriteNumber("RangeStart", sourceRef.RangeStart ?? -1);
            writer.WriteNumber("RangeEnd", sourceRef.RangeEnd ?? -1);
            writer.WriteStartArray("DescriptorRefs");
            foreach (var descriptorRef in sourceRef.DescriptorRefs
                .OrderBy(item => item.Namespace, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ThenBy(item => item.Version ?? -1))
            {
                writer.WriteStartObject();
                writer.WriteString("Namespace", descriptorRef.Namespace);
                writer.WriteString("Id", descriptorRef.Id);
                writer.WriteNumber("Version", descriptorRef.Version ?? -1);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteString("CorrelationId", sourceRef.CorrelationId ?? string.Empty);
            writer.WriteString("CausationId", sourceRef.CausationId ?? string.Empty);
            writer.WriteString("UpstreamCanonicalContentHash", sourceRef.CanonicalContentHash?.Value ?? string.Empty);
            writer.WriteEndObject();
            writer.Flush();
        }
        return Convert.ToBase64String(stream.ToArray());
    }

    private static void WriteMemoryPackPayload(
        Utf8JsonWriter writer,
        string tenantId,
        CanonicalHash scopeFingerprint,
        CanonicalHash visibleMemorySetHash,
        IReadOnlyList<CanonicalHash> returnedMemoryHashes,
        int returnedCount,
        bool wasTruncated,
        bool isAuthoritative)
    {
        writer.WriteStartObject();
        writer.WriteString("TenantId", tenantId);
        writer.WriteString("ScopeFingerprint", scopeFingerprint.Value);
        writer.WriteString("VisibleMemorySetHash", visibleMemorySetHash.Value);
        writer.WriteStartArray("MemoryHashes");
        foreach (var hash in returnedMemoryHashes)
        {
            writer.WriteStringValue(hash.Value);
        }
        writer.WriteEndArray();
        writer.WriteNumber("ReturnedCount", returnedCount);
        writer.WriteBoolean("WasTruncated", wasTruncated);
        writer.WriteBoolean("IsAuthoritative", isAuthoritative);
        writer.WriteEndObject();
    }

    private static void WriteScopePayload(Utf8JsonWriter writer, AgentMemoryQuery query)
    {
        writer.WriteStartObject();
        writer.WriteString("TenantId", query.TenantId);
        WriteEnumArray(writer, "Kinds", query.Kinds.Select(k => (int)k));
        WriteStringArray(writer, "Tags", query.Tags);
        WriteDescriptorRefArray(writer, "DescriptorRefs", query.DescriptorRefs);
        var boundary = query.VisibilityBoundary;
        WriteDescriptorRefArray(writer, "VisibleDescriptorRefs", boundary?.VisibleDescriptorRefs ?? query.VisibleDescriptorRefs);
        writer.WriteBoolean("AllowUnscopedMemory", boundary?.AllowUnscopedMemory ?? false);
        WriteStringArray(writer, "MemoryIds", query.MemoryIds);
        writer.WriteNumber("MinimumConfidence", (int)query.MinimumConfidence);
        writer.WriteBoolean("IncludeStale", query.IncludeStale);
        writer.WriteBoolean("IncludeSuperseded", query.IncludeSuperseded);
        writer.WriteBoolean("IncludeArchived", query.IncludeArchived);
        writer.WriteEndObject();
    }

    private static void WriteMemorySetPayload(Utf8JsonWriter writer, IReadOnlyList<string> sortedMemoryIds)
    {
        writer.WriteStartObject();
        WriteStringArray(writer, "MemoryIds", sortedMemoryIds);
        writer.WriteEndObject();
    }

    private static void WriteCandidateStatePayload(Utf8JsonWriter writer, AgentMemoryCandidate candidate)
    {
        writer.WriteStartObject();
        writer.WriteString("CandidateId", candidate.CandidateId);
        writer.WriteString("TenantId", candidate.TenantId);
        writer.WriteNumber("Kind", (int)candidate.Kind);
        writer.WriteString("Content", candidate.Content);
        writer.WriteString("CanonicalContentHash", candidate.CanonicalContentHash.Value);
        writer.WriteNumber("Confidence", (int)candidate.Confidence);
        writer.WriteNumber("Status", (int)candidate.Status);
        WriteStringArray(writer, "Tags", candidate.Tags);
        WriteDescriptorRefArray(writer, "DescriptorRefs", candidate.DescriptorRefs);
        WriteSourceRefArray(writer, "SourceRefs", candidate.SourceRefs);
        WriteStringArray(writer, "RedactionKinds", candidate.RedactionKinds);
        WriteDiagnostics(writer, candidate.SanitizationDiagnostics);
        writer.WriteEndObject();
    }

    private static void WriteMemoryStatePayload(Utf8JsonWriter writer, AgentMemoryItem memory)
    {
        writer.WriteStartObject();
        writer.WriteString("MemoryId", memory.MemoryId);
        writer.WriteString("TenantId", memory.TenantId);
        writer.WriteNumber("Kind", (int)memory.Kind);
        writer.WriteString("Content", memory.Content);
        writer.WriteString("CanonicalContentHash", memory.CanonicalContentHash.Value);
        writer.WriteString("PromotedAt", memory.PromotedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteNumber("Confidence", (int)memory.Confidence);
        writer.WriteNumber("Status", (int)memory.Status);
        writer.WriteBoolean("IsAuthoritative", memory.IsAuthoritative);
        WriteStringArray(writer, "Tags", memory.Tags);
        WriteDescriptorRefArray(writer, "DescriptorRefs", memory.DescriptorRefs);
        WriteSourceRefArray(writer, "SourceRefs", memory.SourceRefs);
        writer.WriteString("SupersedesMemoryId", memory.SupersedesMemoryId ?? string.Empty);
        writer.WriteString("SupersededByMemoryId", memory.SupersededByMemoryId ?? string.Empty);
        WriteStringArray(writer, "RedactionKinds", memory.RedactionKinds);
        WriteDiagnostics(writer, memory.SanitizationDiagnostics);
        writer.WriteEndObject();
    }

    private static void WriteSourceRefArray(Utf8JsonWriter writer, string propertyName, IReadOnlyList<AgentContextSourceRef> refs)
    {
        writer.WriteStartArray(propertyName);
        foreach (var value in refs.Select(CanonicalSourceRef).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
            writer.WriteStringValue(value);
        writer.WriteEndArray();
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

    private static void WriteDiagnostics(Utf8JsonWriter writer, IReadOnlyList<AgentMemoryDiagnostic> diagnostics)
    {
        writer.WriteStartArray("SanitizationDiagnostics");
        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("Code", diagnostic.Code.Value);
            writer.WriteString("Message", diagnostic.Message);
            writer.WriteString("Severity", diagnostic.Severity.Value);
            writer.WriteEndObject();
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
