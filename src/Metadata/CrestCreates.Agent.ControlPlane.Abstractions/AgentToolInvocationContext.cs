namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentToolInvocationContext
{
    public required string TenantId { get; init; }
    public required string ActorId { get; init; }
    public required AgentToolActorKind ActorKind { get; init; }
    public string? AgentId { get; init; }
    public string? SessionId { get; init; }
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public required string ToolName { get; init; }
    public required AgentToolInvocationSource InvocationSource { get; init; }
    public IReadOnlyDictionary<string, string>? TraceAttributes { get; init; }
}
