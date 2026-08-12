using System.Text.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Accountability.CanonicalHashing;

/// <summary>
/// Projects the deterministic AuditId of a Memory accountability fact using the
/// canonical hash runtime. The shape is tenant-isolated, action/version scoped,
/// and deliberately excludes outcome, payload data, timestamp, and RecordHash so
/// a changed complete fact reuses the same AuditId and reaches sink Conflict.
/// </summary>
public sealed class AgentMemoryAccountabilityAuditIdProjector
{
    public const string ArtifactKind = "AgentMemoryAccountabilityIdentity";
    public const string ContractVersion = "agent-memory-accountability-identity-v1";
    public const string CanonicalShapeVersion = "agent-memory-accountability-audit-id-v1";
    public const string AlgorithmVersion = "sha256-canonical-json-v1";
    public const string AuditIdPrefix = "amem-v1-";

    private readonly ICanonicalHashComputer _hashComputer;

    public AgentMemoryAccountabilityAuditIdProjector(ICanonicalHashComputer hashComputer)
    {
        _hashComputer = hashComputer ?? throw new ArgumentNullException(nameof(hashComputer));
    }

    public string ComputeAuditId(
        string tenantId,
        string actionKind,
        string operationId,
        string payloadKind,
        int payloadVersion)
    {
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = ArtifactKind,
                Purpose = CanonicalHashPurposeNames.SourceIdentity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = AlgorithmVersion,
                ContractVersion = ContractVersion,
                CanonicalShapeVersion = CanonicalShapeVersion
            },
            writer => WriteIdentityPayload(writer, tenantId, actionKind, operationId, payloadKind, payloadVersion));

        var hash = _hashComputer.ComputeFromProjection(projection);
        return AuditIdPrefix + hash.Value;
    }

    private static void WriteIdentityPayload(
        Utf8JsonWriter writer,
        string tenantId,
        string actionKind,
        string operationId,
        string payloadKind,
        int payloadVersion)
    {
        writer.WriteStartObject();
        writer.WriteString("TenantId", tenantId);
        writer.WriteString("ActionKind", actionKind);
        writer.WriteString("OperationId", operationId);
        writer.WriteString("PayloadKind", payloadKind);
        writer.WriteNumber("PayloadVersion", payloadVersion);
        writer.WriteEndObject();
    }
}
