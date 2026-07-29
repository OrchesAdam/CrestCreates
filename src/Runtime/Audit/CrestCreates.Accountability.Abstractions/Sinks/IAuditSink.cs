using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.Abstractions.Sinks;

public interface IAuditSink
{
    string Id { get; }

    /// <summary>
    /// Writes a safe, immutable snapshot.
    /// Implementations MUST return the ValueTask promptly and MUST NOT perform
    /// unbounded synchronous blocking before returning it. Recorder timeouts
    /// bound asynchronous completion after invocation returns; they do not
    /// isolate a contract-violating synchronous blocker.
    /// </summary>
    ValueTask<AuditSinkWriteResult> WriteAsync(
        AuditEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public enum AuditSinkWriteStatus
{
    Accepted = 1,
    Duplicate = 2,
    Conflict = 3
}

public sealed record AuditSinkWriteResult
{
    public required string SinkId { get; init; }
    public required string AuditId { get; init; }
    public required CanonicalHash Integrity { get; init; }
    public required AuditSinkWriteStatus Status { get; init; }
    public CanonicalHash? ExistingIntegrity { get; init; }
    public DateTimeOffset? FirstAcceptedAt { get; init; }
}

public sealed record AuditSinkFailure(string SinkId, string Code);
