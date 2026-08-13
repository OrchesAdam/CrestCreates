namespace CrestCreates.Agent.Memory.Abstractions.Accountability;

/// <summary>
/// Stable identity of one admitted Memory operation. The pair is allocated once by
/// an identity factory at admission time and is never re-sampled or re-derived by
/// producers. Identity carries no tenant, actor, correlation, origin, or payload
/// data; <see cref="AgentMemoryInvocationContext"/> remains the carrier for those.
/// </summary>
public sealed record AgentMemoryOperationIdentity
{
    public const int MaxOperationIdLength = 256;

    public required string OperationId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
