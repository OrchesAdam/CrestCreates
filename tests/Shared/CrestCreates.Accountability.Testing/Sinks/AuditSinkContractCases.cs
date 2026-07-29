using CrestCreates.Accountability.Abstractions.Sinks;

namespace CrestCreates.Accountability.Testing.Sinks;

/// <summary>
/// Provider-independent sink contract cases. Test projects own the runner wrappers.
/// </summary>
public static class AuditSinkContractCases
{
    public static async Task AcceptedThenDuplicateAsync(IAuditSinkContractDriver driver)
    {
        var sink = driver.CreateSink();
        var firstEnvelope = driver.CreateEnvelope("contract-duplicate", "one");
        var first = await sink.WriteAsync(firstEnvelope);
        var duplicate = await sink.WriteAsync(firstEnvelope);
        Assert(first.Status == AuditSinkWriteStatus.Accepted, "first write must be Accepted");
        Assert(duplicate.Status == AuditSinkWriteStatus.Duplicate, "same identity and integrity must be Duplicate");
        Assert(duplicate.FirstAcceptedAt is not null, "Duplicate must preserve FirstAcceptedAt when provider knows it");
    }

    public static async Task DifferentIntegrityIsConflictAsync(IAuditSinkContractDriver driver)
    {
        var sink = driver.CreateSink();
        await sink.WriteAsync(driver.CreateEnvelope("contract-conflict", "one"));
        var result = await sink.WriteAsync(driver.CreateEnvelope("contract-conflict", "two"));
        Assert(result.Status == AuditSinkWriteStatus.Conflict, "same identity with different integrity must be Conflict");
        Assert(result.ExistingIntegrity is not null, "Conflict must preserve the existing structured integrity");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new AuditSinkContractAssertionException(message);
    }
}
