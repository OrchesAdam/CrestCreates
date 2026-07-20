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
    public AgentToolAuditFactKind Kind { get; init; }
}

public enum AgentToolAuditFactKind
{
    Unknown = 0,
    BranchInvariant = 1,
    Output = 2,
    Internal = 3
}

public sealed record AgentToolInvocationFactSnapshot(
    IReadOnlyList<AgentToolAuditFact> Facts,
    int MaximumFacts);

public interface IAgentToolInvocationFactSink
{
    void AddTrustedFacts(IReadOnlyList<AgentToolAuditFact> facts, int requestedMaximum);
}
