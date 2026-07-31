using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Accountability.Abstractions.Tests.Contract;

public sealed class AuditRecordResultContractTests
{
    [Fact]
    public void RecordedStatusWithoutAcceptedSinkIsNotAccepted()
        => Result(AuditRecordStatus.Recorded).IsAccepted.Should().BeFalse();

    [Fact]
    public void AcceptedSinkResultIsAccepted()
        => Result(AuditRecordStatus.Recorded, AuditSinkWriteStatus.Accepted).IsAccepted.Should().BeTrue();

    [Fact]
    public void DuplicateSinkResultIsAccepted()
        => Result(AuditRecordStatus.Recorded, AuditSinkWriteStatus.Duplicate).IsAccepted.Should().BeTrue();

    [Fact]
    public void ConflictOnlyResultIsNotAccepted()
        => Result(AuditRecordStatus.Failed, AuditSinkWriteStatus.Conflict).IsAccepted.Should().BeFalse();

    private static AuditRecordResult Result(
        AuditRecordStatus status,
        AuditSinkWriteStatus? sinkStatus = null)
        => new()
        {
            AuditId = "audit-1",
            Status = status,
            ProcessedAt = DateTimeOffset.UnixEpoch,
            SinkResults = sinkStatus is null
                ? []
                :
                [
                    new AuditSinkWriteResult
                    {
                        SinkId = "sink",
                        AuditId = "audit-1",
                        Integrity = Hash,
                        Status = sinkStatus.Value,
                        ExistingIntegrity = sinkStatus == AuditSinkWriteStatus.Conflict
                            ? Hash with { Value = "existing" }
                            : null
                    }
                ]
        };

    private static CanonicalHash Hash { get; } = new()
    {
        Value = "incoming",
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "AccountabilityRecord",
        Scope = "InternalFull",
        Purpose = "AuditEvidence",
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "accountability-record-hash-v1"
    };
}
