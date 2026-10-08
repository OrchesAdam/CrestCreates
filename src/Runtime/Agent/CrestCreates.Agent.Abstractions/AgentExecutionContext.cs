using System.Collections.Generic;

namespace CrestCreates.Agent.Abstractions;

public enum AgentToolCallOrigin
{
    Unknown = 0,
    ExplicitRequest = 1,
    AutomaticSelection = 2
}

public sealed record AgentExecutionContext
{
    public required string ExecutionId { get; init; }

    public required string InvocationId { get; init; }

    public required string AgentId { get; init; }

    public required IReadOnlySet<string> AgentRoles { get; init; }

    public required AgentToolCallOrigin CallOrigin { get; init; }

    public string? CausationId { get; init; }

    /// <summary>
    /// Parent invocation ID for nested Agent Tool calls.
    /// When set, indicates this invocation is a child of another invocation within the same Agent Execution.
    /// </summary>
    public string? ParentInvocationId { get; init; }

    /// <summary>
    /// Whether this is an explicit root invocation (no parent).
    /// When true, ParentInvocationId must be null.
    /// When false/null and ParentInvocationId is null, lineage is Unknown (legacy).
    /// </summary>
    public bool? IsRootInvocation { get; init; }
}

public interface IAgentExecutionContextAccessor
{
    AgentExecutionContext? Current { get; }
}
