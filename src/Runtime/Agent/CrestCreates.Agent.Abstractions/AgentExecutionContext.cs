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
}

public interface IAgentExecutionContextAccessor
{
    AgentExecutionContext? Current { get; }
}
