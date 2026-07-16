using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolResultMapper
{
    public AgentToolInvocationOutcome CapabilityFailure(CapabilityExecutionResult result)
        => new()
        {
            Kind = AgentToolInvocationOutcomeKind.CapabilityFailure,
            Code = result.ErrorCode ?? "AGENT_TOOL_CAPABILITY_FAILURE",
            Message = "The requested operation could not be completed.",
            Issues = result.Issues.Select(issue =>
                new AgentToolInvocationIssue(issue.Code, issue.FieldName)).ToArray()
        };

    public static AgentToolInvocationOutcome Outcome(
        AgentToolInvocationOutcomeKind kind,
        string code,
        string message)
        => new() { Kind = kind, Code = code, Message = message };
}
