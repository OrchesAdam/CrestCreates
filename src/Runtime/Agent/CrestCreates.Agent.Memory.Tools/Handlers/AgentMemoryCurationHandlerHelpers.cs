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
        return new AgentMemoryOperationRequest
        {
            TenantId = principal.TenantId,
            Reason = reason,
            Identity = identity,
            Explanation = explanation,
            InvocationContext = AgentMemoryToolInvocationContextMapper.Create(
                principal, principal.TenantId, context, ambient)
        };
    }
}
