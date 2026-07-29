using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.AuditLogging.Tests;

internal static class TestAuditRecordResults
{
    public static AuditRecordResult Accepted(string auditId)
        => new()
        {
            AuditId = auditId,
            Status = AuditRecordStatus.Recorded,
            ProcessedAt = DateTimeOffset.UtcNow,
            SinkResults =
            [
                new AuditSinkWriteResult
                {
                    SinkId = "test",
                    AuditId = auditId,
                    Integrity = Hash,
                    Status = AuditSinkWriteStatus.Accepted
                }
            ]
        };

    private static CanonicalHash Hash { get; } = new()
    {
        Value = "test",
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "AccountabilityRecord",
        Scope = "InternalFull",
        Purpose = "AuditEvidence",
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "accountability-record-hash-v1"
    };
}
