using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.Abstractions.Recording;

public enum AuditRecordStatus
{
    Recorded = 1,
    PartiallyRecorded = 2,
    Rejected = 3,
    Failed = 4,
    NoSinkConfigured = 5
}

public sealed record AuditRecordResult
{
    public required string AuditId { get; init; }
    public required AuditRecordStatus Status { get; init; }
    public required DateTimeOffset ProcessedAt { get; init; }
    public CanonicalHash? RecordHash { get; init; }
    public ImmutableArray<AuditSinkWriteResult> SinkResults { get; init; } = [];
    public ImmutableArray<AuditSinkFailure> SinkFailures { get; init; } = [];
    public ImmutableArray<AuditRecordIssue> Issues { get; init; } = [];

    public bool IsAccepted => SinkResults.Any(x =>
        x.Status is AuditSinkWriteStatus.Accepted or AuditSinkWriteStatus.Duplicate);
}

public sealed record AuditRecordIssue(string Code, string? Path = null);
