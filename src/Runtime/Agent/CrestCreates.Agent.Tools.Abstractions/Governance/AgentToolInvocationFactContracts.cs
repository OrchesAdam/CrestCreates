namespace CrestCreates.Agent.Tools;

/// <summary>
/// A bounded, non-sensitive fact eligible for internal governance audit. Fact
/// values must be hashes, counts, enums, or other adapter-approved safe data;
/// memory text, resource IDs, grants, and handles are never accepted here.
/// </summary>
public sealed record AgentToolAuditFact
{
    public required string Code { get; init; }
    public string? Value { get; init; }
}

public sealed record AgentToolInvocationFactSnapshot(
    IReadOnlyList<AgentToolAuditFact> Facts,
    int MaximumFacts);

public interface IAgentToolInvocationFactBuffer
{
    void AddTrustedFacts(IReadOnlyList<AgentToolAuditFact> facts, int requestedMaximum);
    AgentToolInvocationFactSnapshot Seal();
}

public interface IAgentToolInvocationFactBufferFactory
{
    IAgentToolInvocationFactBuffer Create();
}
