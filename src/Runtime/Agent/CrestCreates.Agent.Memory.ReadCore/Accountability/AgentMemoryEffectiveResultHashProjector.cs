using System.Text.Json;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.ReadCore.Accountability;

/// <summary>
/// Projects the governed effective-visible Accountability hashes for a Recall
/// result. The effective-visible content shape answers a different question than
/// the domain CanonicalContentHash: it captures the exact final caller-visible
/// Content in one Tenant, excluding MemoryId, SourceRef, DescriptorRef, upstream
/// hashes, and any provenance-only domain hash. The effective-pack shape embeds
/// only these dedicated effective-visible hashes in final caller-visible order
/// plus the safe bounded budget fields. Neither shape wraps or aliases the
/// existing Memory domain content/pack hashes.
/// </summary>
public sealed class AgentMemoryEffectiveResultHashProjector
{
    public const string ContentArtifactKind = "AgentMemoryAccountabilityEffectiveVisibleContent";
    public const string ContentPurpose = "AuditEvidence";
    public const string ContentScope = "TenantVisible";
    public const string ContentContractVersion = "agent-memory-accountability-effective-content-v1";
    public const string ContentCanonicalShapeVersion = "agent-memory-accountability-effective-content-v1";

    public const string PackArtifactKind = "AgentMemoryAccountabilityEffectivePack";
    public const string PackPurpose = "AuditEvidence";
    public const string PackScope = "TenantVisible";
    public const string PackContractVersion = "agent-memory-accountability-effective-pack-v1";
    public const string PackCanonicalShapeVersion = "agent-memory-accountability-effective-pack-v1";

    public const string AlgorithmVersion = "sha256-canonical-json-v1";

    private readonly ICanonicalHashComputer _hashComputer;

    public AgentMemoryEffectiveResultHashProjector(ICanonicalHashComputer hashComputer)
    {
        _hashComputer = hashComputer ?? throw new ArgumentNullException(nameof(hashComputer));
    }

    /// <summary>
    /// Computes the effective-visible content hash for one exact final
    /// caller-visible Content value in one Tenant. Contains no MemoryId,
    /// SourceRef, DescriptorRef, source coordinate, correlation/causation,
    /// upstream hash, or domain CanonicalContentHash.
    /// </summary>
    public CanonicalHash ComputeEffectiveVisibleContentHash(string tenantId, string content)
    {
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = ContentArtifactKind,
                Purpose = ContentPurpose,
                Scope = ContentScope,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = ContentContractVersion,
                CanonicalShapeVersion = ContentCanonicalShapeVersion
            },
            writer => WriteContentPayload(writer, tenantId, content));

        return _hashComputer.ComputeFromProjection(projection);
    }

    /// <summary>
    /// Computes the effective-pack Accountability hash from dedicated
    /// effective-visible content hashes in final caller-visible order plus the
    /// safe bounded query fields. Never receives MemoryIds, Handles, SourceRefs,
    /// Retriever hashes, or returned domain CanonicalContentHash values.
    /// </summary>
    public CanonicalHash ComputeEffectivePackHash(
        string tenantId,
        IReadOnlyList<CanonicalHash> effectiveVisibleContentHashes,
        int returnedCount,
        bool wasTruncated,
        IReadOnlyList<string> requestedKinds,
        int maximumCount,
        int characterBudget,
        string minimumConfidence)
    {
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = PackArtifactKind,
                Purpose = PackPurpose,
                Scope = PackScope,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = PackContractVersion,
                CanonicalShapeVersion = PackCanonicalShapeVersion
            },
            writer => WritePackPayload(
                writer, tenantId, effectiveVisibleContentHashes, returnedCount,
                wasTruncated, requestedKinds, maximumCount, characterBudget, minimumConfidence));

        return _hashComputer.ComputeFromProjection(projection);
    }

    private static void WriteContentPayload(Utf8JsonWriter writer, string tenantId, string content)
    {
        writer.WriteStartObject();
        writer.WriteString("TenantId", tenantId);
        writer.WriteString("Content", content);
        writer.WriteEndObject();
    }

    private static void WritePackPayload(
        Utf8JsonWriter writer,
        string tenantId,
        IReadOnlyList<CanonicalHash> effectiveVisibleContentHashes,
        int returnedCount,
        bool wasTruncated,
        IReadOnlyList<string> requestedKinds,
        int maximumCount,
        int characterBudget,
        string minimumConfidence)
    {
        writer.WriteStartObject();
        writer.WriteString("TenantId", tenantId);
        writer.WriteStartArray("EffectiveVisibleContentHashes");
        foreach (var hash in effectiveVisibleContentHashes)
        {
            // CanonicalHash inputs include their governed metadata plus value.
            writer.WriteStartObject();
            writer.WriteString("Algorithm", hash.Algorithm);
            writer.WriteString("AlgorithmVersion", hash.AlgorithmVersion);
            writer.WriteString("ArtifactKind", hash.ArtifactKind);
            writer.WriteString("Purpose", hash.Purpose);
            writer.WriteString("Scope", hash.Scope);
            writer.WriteString("ContractVersion", hash.ContractVersion);
            writer.WriteString("CanonicalShapeVersion", hash.CanonicalShapeVersion);
            writer.WriteString("Value", hash.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteNumber("ReturnedCount", returnedCount);
        writer.WriteBoolean("WasTruncated", wasTruncated);
        writer.WriteStartArray("RequestedKinds");
        foreach (var kind in requestedKinds)
        {
            writer.WriteStringValue(kind);
        }
        writer.WriteEndArray();
        writer.WriteNumber("MaximumCount", maximumCount);
        writer.WriteNumber("CharacterBudget", characterBudget);
        writer.WriteString("MinimumConfidence", minimumConfidence);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Maps the tool confidence enum to the stable Accountability string
    /// representation used as the payload MinimumConfidence value.
    /// </summary>
    public static string MapMinimumConfidence(AgentMemoryToolConfidence confidence)
        => confidence switch
        {
            AgentMemoryToolConfidence.High => "0.8",
            AgentMemoryToolConfidence.Medium => "0.5",
            AgentMemoryToolConfidence.Low => "0.3",
            AgentMemoryToolConfidence.Unknown => "0.0",
            AgentMemoryToolConfidence.Unspecified => "0.0",
            _ => "0.0"
        };

    /// <summary>
    /// Maps the requested tool kinds to ordinal canonical order: distinct kinds
    /// sorted by the explicit semantic wire value with Unknown filtered out.
    /// </summary>
    public static IReadOnlyList<string> MapRequestedKinds(IReadOnlyList<AgentMemoryToolKind> kinds)
        => kinds
            .Where(kind => kind != AgentMemoryToolKind.Unknown)
            .Distinct()
            .Select(AgentMemoryStableWireMappings.MapRequestedKind)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .ToArray();
}
