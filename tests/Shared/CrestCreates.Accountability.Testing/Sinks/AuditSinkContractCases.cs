using CrestCreates.Accountability.Abstractions.Sinks;

namespace CrestCreates.Accountability.Testing.Sinks;

/// <summary>
/// Provider-independent sink contract cases. Test projects own the runner wrappers.
/// </summary>
public static class AuditSinkContractCases
{
    public static async Task AcceptsNewRecordAsync(IAuditSinkContractDriver driver)
    {
        var sink = driver.CreateSink();
        var result = await sink.WriteAsync(driver.CreateEnvelope("contract-new", "one"));

        AuditSinkContractAssertions.Equal(AuditSinkWriteStatus.Accepted, result.Status,
            "a new AuditId must be Accepted");
        AuditSinkContractAssertions.Null(result.ExistingIntegrity,
            "Accepted must not expose ExistingIntegrity");
    }

    public static async Task AcceptedThenDuplicateAsync(IAuditSinkContractDriver driver)
    {
        var sink = driver.CreateSink();
        var firstEnvelope = driver.CreateEnvelope("contract-duplicate", "one");
        var first = await sink.WriteAsync(firstEnvelope);
        var duplicate = await sink.WriteAsync(firstEnvelope);
        AuditSinkContractAssertions.Equal(AuditSinkWriteStatus.Accepted, first.Status,
            "first write must be Accepted");
        AuditSinkContractAssertions.Null(first.ExistingIntegrity,
            "Accepted must not expose ExistingIntegrity");
        AuditSinkContractAssertions.Equal(AuditSinkWriteStatus.Duplicate, duplicate.Status,
            "same identity and integrity must be Duplicate");
        AuditSinkContractAssertions.Null(duplicate.ExistingIntegrity,
            "Duplicate must not expose ExistingIntegrity");
        AuditSinkContractAssertions.NotNull(duplicate.FirstAcceptedAt,
            "Duplicate must preserve FirstAcceptedAt when provider knows it");
        AuditSinkContractAssertions.Equal(first.FirstAcceptedAt, duplicate.FirstAcceptedAt,
            "Duplicate must return the original provider-local acceptance time");
    }

    public static async Task DifferentIntegrityIsConflictAsync(IAuditSinkContractDriver driver)
    {
        var sink = driver.CreateSink();
        await sink.WriteAsync(driver.CreateEnvelope("contract-conflict", "one"));
        var result = await sink.WriteAsync(driver.CreateEnvelope("contract-conflict", "two"));
        AuditSinkContractAssertions.Equal(AuditSinkWriteStatus.Conflict, result.Status,
            "same identity with different integrity must be Conflict");
        AuditSinkContractAssertions.NotNull(result.ExistingIntegrity,
            "Conflict must preserve the existing structured integrity");
        AuditSinkContractAssertions.NotEqual(result.Integrity, result.ExistingIntegrity,
            "Conflict ExistingIntegrity must differ from incoming Integrity");
    }

    public static async Task SnapshotsOnWriteAndReadAsync(IAuditSinkContractDriver driver)
    {
        var sink = driver.CreateSink();
        var envelope = driver.CreateEnvelope("contract-snapshot", "one");
        await sink.WriteAsync(envelope);

        var first = await driver.ReadAsync(sink, envelope.AuditId);
        var second = await driver.ReadAsync(sink, envelope.AuditId);

        AuditSinkContractAssertions.NotNull(first, "accepted record must be readable");
        AuditSinkContractAssertions.NotNull(second, "accepted record must remain readable");
        AuditSinkContractAssertions.False(ReferenceEquals(envelope, first),
            "write must store a snapshot, not the producer instance");
        AuditSinkContractAssertions.False(ReferenceEquals(first, second),
            "each read must return a snapshot");
    }

    public static async Task ConcurrentIdenticalWriteAsync(IAuditSinkContractDriver driver)
    {
        var sink = driver.CreateSink();
        var envelope = driver.CreateEnvelope("contract-concurrent", "one");
        var writes = Enumerable.Range(0, 32)
            .Select(_ => sink.WriteAsync(envelope).AsTask())
            .ToArray();

        var results = await Task.WhenAll(writes);
        AuditSinkContractAssertions.Equal(1,
            results.Count(result => result.Status == AuditSinkWriteStatus.Accepted),
            "concurrent identical writes must have exactly one Accepted result");
        AuditSinkContractAssertions.Equal(results.Length - 1,
            results.Count(result => result.Status == AuditSinkWriteStatus.Duplicate),
            "all other concurrent identical writes must be Duplicate");
    }

    public static async Task DeterministicReadOrderAsync(IAuditSinkContractDriver driver)
    {
        var sink = driver.CreateSink();
        await sink.WriteAsync(driver.CreateEnvelope("record-z", "z"));
        await sink.WriteAsync(driver.CreateEnvelope("record-a", "a"));
        await sink.WriteAsync(driver.CreateEnvelope("record-m", "m"));

        var records = await driver.ReadAllAsync(sink);
        AuditSinkContractAssertions.SequenceEqual(
            records.Select(record => record.AuditId),
            ["record-a", "record-m", "record-z"],
            "provider read order must be AuditId ordinal");
    }
}
