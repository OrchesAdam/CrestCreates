using System;
using System.Collections.Generic;

namespace CrestCreates.Application.Contracts.AuditRetention;

public sealed class AuditRetentionPreviewInput
{
    public string? StoreId { get; set; }
    public string? TenantId { get; set; }
}

public sealed class AuditRetentionExecuteInput
{
    public string? StoreId { get; set; }
    public string? TenantId { get; set; }
    public string? RequestId { get; set; }
    public string? ReasonCode { get; set; }
    public string? ExpectedPolicyHash { get; set; }
}

public sealed class AuditRetentionHoldInput
{
    public string? StoreId { get; set; }
    public string? TenantId { get; set; }
    public string? AuditId { get; set; }
    public string? ReasonCode { get; set; }
}

public sealed class AuditRetentionExportInput
{
    public string? StoreId { get; set; }
    public string? TenantId { get; set; }
    public string? RequestId { get; set; }
    public long BatchOrdinal { get; set; }
}

public sealed class AuditRetentionMirrorDto
{
    public string Status { get; set; } = string.Empty;
    public string? Code { get; set; }
}

public sealed class AuditRetentionReleaseInput
{
    public string? StoreId { get; set; }
    public string? TenantId { get; set; }
    public string? HoldId { get; set; }
    public long ExpectedVersion { get; set; }
    public string? ReasonCode { get; set; }
}

public sealed class AuditRetentionPreviewCandidateDto
{
    public string AuditId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset FirstAcceptedAt { get; set; }
    public string Disposition { get; set; } = string.Empty;
}

public sealed class AuditRetentionPreviewDto
{
    public string StoreId { get; set; } = string.Empty;
    public string SinkId { get; set; } = string.Empty;
    public string ScopeKind { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public DateTimeOffset SnapshotAt { get; set; }
    public DateTimeOffset Cutoff { get; set; }
    public string PolicyHash { get; set; } = string.Empty;
    public int BatchSize { get; set; }
    public int MaxBatchesPerRun { get; set; }
    public bool HasMore { get; set; }
    public int EligibleInSample { get; set; }
    public int HeldInSample { get; set; }
    public int ProtectedInSample { get; set; }
    public int QuarantinedInSample { get; set; }
    public List<AuditRetentionPreviewCandidateDto> Sample { get; set; } = new();
}

public sealed class AuditRetentionRunDto
{
    public string StoreId { get; set; } = string.Empty;
    public string SinkId { get; set; } = string.Empty;
    public string ScopeKind { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string CommitKnowledge { get; set; } = string.Empty;
    public long CommittedDeletedCount { get; set; }
    public long CommittedBatchCount { get; set; }
    public bool RemainingMayExist { get; set; }
    public string? StopReason { get; set; }
    public string? MirrorStatus { get; set; }
    public string? MirrorCode { get; set; }
}

public sealed class AuditHoldDto
{
    public string HoldId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public string SinkId { get; set; } = string.Empty;
    public string AuditId { get; set; } = string.Empty;
    public string ScopeKind { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public long Version { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string? MirrorStatus { get; set; }
    public string? MirrorCode { get; set; }
}
