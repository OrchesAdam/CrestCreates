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
    public Task AcceptsNewRecord() => AuditSinkContractCases.AcceptsNewRecordAsync(new Driver());

    [Fact]
    public Task SameIdAndHashReturnsDuplicate() => AuditSinkContractCases.AcceptedThenDuplicateAsync(new Driver());

    [Fact]
    public async Task AcceptedHasNoExistingIntegrity()
    {
        var driver = new Driver();
        var result = await driver.CreateSink().WriteAsync(driver.CreateEnvelope("accepted", "one"));
        Assert.Null(result.ExistingIntegrity);
    }

    [Fact]
    public async Task DuplicateHasNoExistingIntegrity()
    {
        var driver = new Driver();
        var sink = driver.CreateSink();
        var envelope = driver.CreateEnvelope("duplicate", "one");
        await sink.WriteAsync(envelope);
        var result = await sink.WriteAsync(envelope);
        Assert.Null(result.ExistingIntegrity);
    }

    [Fact]
    public Task SameIdAndDifferentHashReturnsConflict() => AuditSinkContractCases.DifferentIntegrityIsConflictAsync(new Driver());

    [Fact]
    public async Task ConflictHasDifferentExistingIntegrity()
    {
        var driver = new Driver();
        var sink = driver.CreateSink();
        await sink.WriteAsync(driver.CreateEnvelope("conflict", "one"));
        var result = await sink.WriteAsync(driver.CreateEnvelope("conflict", "two"));
        Assert.NotNull(result.ExistingIntegrity);
        Assert.NotEqual(result.Integrity, result.ExistingIntegrity);
    }

    [Fact]
    public async Task StructuredExistingHashIsPreserved()
    {
        var driver = new Driver();
        var sink = driver.CreateSink();
        var first = driver.CreateEnvelope("structured-conflict", "one");
        await sink.WriteAsync(first);

        var conflict = await sink.WriteAsync(driver.CreateEnvelope("structured-conflict", "two"));

        Assert.Equal(first.Integrity, conflict.ExistingIntegrity);
        Assert.Equal("canonical-hash-v1", conflict.ExistingIntegrity!.ContractVersion);
        Assert.Equal("accountability-record-hash-v1", conflict.ExistingIntegrity.CanonicalShapeVersion);
    }

    [Fact]
    public async Task DuplicateRetryReturnsOriginalFirstAcceptedAtWhenKnown()
    {
        var driver = new Driver();
        var sink = driver.CreateSink();
        var envelope = driver.CreateEnvelope("accepted-at", "one");
        var accepted = await sink.WriteAsync(envelope);
        var duplicate = await sink.WriteAsync(envelope);

        Assert.NotNull(accepted.FirstAcceptedAt);
        Assert.Equal(accepted.FirstAcceptedAt, duplicate.FirstAcceptedAt);
    }

    [Fact]
    public async Task SinkAcceptanceTimeIsProviderLocal()
    {
        var acceptedAt = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var sink = new InMemoryAuditSink("clocked", new FixedTimeProvider(acceptedAt));
        var result = await sink.WriteAsync(new Driver().CreateEnvelope("clocked", "one"));

        Assert.Equal(acceptedAt, result.FirstAcceptedAt);
    }

    [Fact]
    public Task SnapshotsOnWriteAndRead() => AuditSinkContractCases.SnapshotsOnWriteAndReadAsync(new Driver());

    [Fact]
    public Task IsThreadSafeUnderConcurrentIdenticalWrite() => AuditSinkContractCases.ConcurrentIdenticalWriteAsync(new Driver());

    [Fact]
    public Task HasDeterministicReadOrder() => AuditSinkContractCases.DeterministicReadOrderAsync(new Driver());

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

        public ValueTask<AuditEnvelope?> ReadAsync(
            IAuditSink sink,
            string auditId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = ((InMemoryAuditSink)sink).TryGet(auditId, out var envelope);
            return ValueTask.FromResult(found ? envelope : null);
        }

        public ValueTask<IReadOnlyList<AuditEnvelope>> ReadAllAsync(
            IAuditSink sink,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(((InMemoryAuditSink)sink).GetRecords());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
