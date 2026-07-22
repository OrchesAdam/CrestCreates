namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Non-forgeable opaque compensation token. Coordinator internally maintains
/// a short-lived mapping from TokenId to the precise set of newly created artifacts.
/// One-shot/idempotent: RevokeCreatedAsync called multiple times with same token is no-op after first.
/// Token expires after tracking window (configured via AgentMemoryProjectionSecurityOptions).
/// </summary>
public sealed record AgentMemoryArtifactCompensationToken
{
    public required string TokenId { get; init; }
}
