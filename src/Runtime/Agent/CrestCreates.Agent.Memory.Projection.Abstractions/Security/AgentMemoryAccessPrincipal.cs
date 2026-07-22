namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Stable session-level identity for credential reuse eligibility.
/// Full record equality required for authorization — no partial comparison.
/// Construction validates all fields are non-null/non-empty (fail-closed).
/// </summary>
public sealed record AgentMemoryAccessPrincipal
{
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public required AgentMemoryCallerKind CallerKind { get; init; }
    public required string CallerId { get; init; }
    public required string SecurityContextId { get; init; }
}
