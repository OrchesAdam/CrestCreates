using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

internal static class AgentMemoryCurationHandlerHelpers
{
    public static AgentMemoryOperationRequest CreateRequest(
        AgentMemoryToolPrincipal principal,
        CrestCreates.Agent.Abstractions.AgentExecutionContext execution,
        CapabilityExecutionContext context,
        string reason,
        string? explanation,
        DateTimeOffset timestamp)
        => new()
        {
            TenantId = principal.TenantId,
            Reason = reason,
            Timestamp = timestamp,
            Explanation = explanation,
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = principal.TenantId,
                ActorId = principal.UserId,
                ActorKind = "User",
                AgentId = principal.AgentId,
                SessionId = principal.ExecutionId,
                CorrelationId = execution.InvocationId,
                CausationId = execution.CausationId,
                InvocationSource = "AgentTool",
                TraceAttributes = new Dictionary<string, string>
                {
                    ["capability"] = context.CapabilityId
                }
            }
        };
}
