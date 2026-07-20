namespace CrestCreates.Agent.Tools;

/// <summary>
/// Exact Phase 8f identity binding propagated into capability execution. A
/// Memory handler consumes this snapshot; it must never recompute the
/// invocation fingerprint from partial context fields.
/// </summary>
public sealed record AgentToolInvocationBindingSnapshot
{
    public required AgentToolLogicalInvocationKey LogicalKey { get; init; }
    public required string InvocationFingerprint { get; init; }
}
