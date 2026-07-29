using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Accountability.Testing.Sinks;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Xunit;

namespace CrestCreates.Accountability.Tests.Sinks;

public sealed class InMemoryAuditSinkContractTests
{
    [Fact]
    public Task AcceptedThenDuplicate() => AuditSinkContractCases.AcceptedThenDuplicateAsync(new Driver());

    [Fact]
    public Task DifferentIntegrityIsConflict() => AuditSinkContractCases.DifferentIntegrityIsConflictAsync(new Driver());

    private sealed class Driver : IAuditSinkContractDriver
    {
        public IAuditSink CreateSink() => new InMemoryAuditSink(Guid.NewGuid().ToString("N"));

        public AuditEnvelope CreateEnvelope(string auditId, string integrityValue)
            => new()
            {
                AuditId = auditId,
                OccurredAt = DateTimeOffset.UnixEpoch,
                CorrelationId = "contract",
                Actor = new AuditActor { Kind = "system", Id = "test" },
                Action = new AuditAction { Kind = "system", Name = "contract" },
                Target = new AuditTarget { Kind = "test", Id = auditId },
                Outcome = new AuditOutcome { Status = "succeeded" },
                Integrity = new CanonicalHash
                {
                    Value = integrityValue,
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "sha256-canonical-json-v1",
                    ArtifactKind = "AccountabilityRecord",
                    Scope = "InternalFull",
                    Purpose = "AuditEvidence",
                    ContractVersion = "canonical-hash-v1",
                    CanonicalShapeVersion = "accountability-record-hash-v1"
                }
            };
    }
}
