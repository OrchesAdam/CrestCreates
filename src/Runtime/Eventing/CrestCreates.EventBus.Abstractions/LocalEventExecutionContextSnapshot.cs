using System;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Minimal immutable execution-context snapshot captured at the publish/enqueue boundary and
/// persisted with a dead letter row. Version 1 fixes the wire meaning of the captured fields; the
/// version is a format marker, not a replay or schema version. A null version marks a legacy row
/// that predates the snapshot format. Actor, claims, tokens, providers and ambient frames are
/// deliberately never captured.
/// </summary>
public sealed record LocalEventExecutionContextSnapshot(
    int? ContextVersion,
    string? TenantId,
    string? CorrelationId)
{
    /// <summary>The only context format this runtime writes today.</summary>
    public const int CurrentContextVersion = 1;

    public static LocalEventExecutionContextSnapshot Host { get; } = new(CurrentContextVersion, null, null);

    public static LocalEventExecutionContextSnapshot Capture(string? tenantId, string? correlationId)
        => new(CurrentContextVersion, Normalize(tenantId), Normalize(correlationId));

    /// <summary>
    /// Snapshot view of a persisted row: empty/whitespace ids are normalized to null so a damaged
    /// value can never masquerade as an explicit host or bypass the tenant-mode checks.
    /// </summary>
    public static LocalEventExecutionContextSnapshot FromPersisted(
        int? contextVersion,
        string? tenantId,
        string? correlationId)
        => new(contextVersion, Normalize(tenantId), Normalize(correlationId));

    private static string? Normalize(string? value)
        => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>
/// Immutable channel work item: the payload plus the context and identity facts admitted at the
/// publish boundary. The consumer re-runs the current policy checks; the snapshot is evidence of
/// the original context, not an authorization.
/// </summary>
public sealed record LocalEventWorkItem(
    ILocalEvent Payload,
    LocalEventExecutionContextSnapshot ContextSnapshot,
    LocalEventAcceptedIdentity AcceptedIdentity);

/// <summary>
/// Facts verified at admission time (resolved local replay key/version and, for governed events,
/// the associated descriptor identity). They are the source facts for failure recording; the
/// consumer still re-checks the current policy before executing.
/// </summary>
public sealed record LocalEventAcceptedIdentity(
    string LocalReplayKey,
    int ReplayVersion,
    string? DescriptorId,
    string? DescriptorName);
