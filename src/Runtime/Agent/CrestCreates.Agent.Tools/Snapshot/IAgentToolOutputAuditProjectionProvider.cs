using System.Collections.Immutable;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Module-owned, typed output projection. The provider is resolved while the
/// immutable runtime snapshot is built; invocation never inspects JSON field
/// names to manufacture audit facts.
/// </summary>
public interface IAgentToolOutputAuditProjectionProvider
{
    Func<object?, IReadOnlyList<AgentToolAuditFact>>? Create(string toolName, Type outputType);
}

public enum AgentToolAuditFactValueEncoding
{
    Unknown = 0,
    Text = 1,
    Integer = 2,
    Boolean = 3,
    Hash = 4
}

public sealed record AgentToolAuditFactDefinition
{
    public required string CodePrefix { get; init; }
    public string CodeSuffix { get; init; } = string.Empty;
    public AgentToolAuditFactMatchKind MatchKind { get; init; } = AgentToolAuditFactMatchKind.Exact;
    public int MaximumIndex { get; init; } = 64;
    public IReadOnlySet<string>? AllowedValues { get; init; }
    public required AgentToolAuditFactKind Kind { get; init; }
    public AgentToolAuditFactValueEncoding ValueEncoding { get; init; } = AgentToolAuditFactValueEncoding.Text;
}

public enum AgentToolAuditFactMatchKind
{
    Unknown = 0,
    Exact = 1,
    Indexed = 2
}

public sealed record AgentToolAuditProjectionContract
{
    public required ImmutableArray<AgentToolAuditFactDefinition> Definitions { get; init; }
    public int MaximumFacts { get; init; } = 64;
}

public interface IAgentToolOutputAuditProjectionContractProvider
{
    AgentToolAuditProjectionContract? CreateContract(string toolName, Type outputType);
}

/// <summary>
/// Provides the typed wire outcome discriminator for a binding root.  The
/// invoker uses this instead of probing serialized JSON property names.
/// </summary>
public interface IAgentToolOutputOutcomeCodeProvider
{
    Func<object?, string?>? CreateOutcomeCode(string toolName, Type outputType);
}
