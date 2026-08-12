using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Agent.Memory.ReadCore.Accountability;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

internal static class AgentMemoryCurationHandlerHelpers
{
    public static AgentMemoryOperationRequest CreateRequest(
        AgentMemoryToolPrincipal principal,
        CapabilityExecutionContext context,
        string reason,
        string? explanation,
        AgentMemoryOperationIdentity identity,
        AuditOperationContext? ambient)
    {
        var causality = AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);
        return new AgentMemoryOperationRequest
        {
            TenantId = principal.TenantId,
            Reason = reason,
            Identity = identity,
            Explanation = explanation,
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = principal.TenantId,
                ActorId = principal.UserId,
                ActorKind = "User",
                AgentId = principal.AgentId,
                SessionId = principal.ExecutionId,
                CorrelationId = causality.CorrelationId,
                CausationId = causality.CausationId,
                ParentAuditId = causality.ParentAuditId,
                InvocationSource = "AgentTool",
                TraceAttributes = new Dictionary<string, string>
                {
                    ["capability"] = context.CapabilityId
                }
            }
        };
    }
}
