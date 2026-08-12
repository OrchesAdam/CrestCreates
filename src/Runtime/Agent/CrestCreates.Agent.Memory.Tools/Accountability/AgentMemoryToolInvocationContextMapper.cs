using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.ReadCore.Accountability;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

internal static class AgentMemoryToolInvocationContextMapper
{
    public static AgentMemoryInvocationContext Create(
        AgentMemoryToolPrincipal principal,
        string tenantId,
        CapabilityExecutionContext context,
        AuditOperationContext? ambient)
    {
        AgentMemoryToolUpstreamIdentityValidator.Validate(principal, context);
        var causality = AgentMemoryCapabilityCausalityMapper.FromCapability(context, ambient);
        var actor = context.AccountabilityActor
            ?? throw new InvalidOperationException("Capability accountability actor is unavailable.");
        var invocationId = context.Items.TryGetValue(
            AgentCapabilityContextItemNames.InvocationBindingSnapshot, out var bindingValue)
            && bindingValue is AgentToolInvocationBindingSnapshot binding
            ? binding.LogicalKey.InvocationId
            : null;

        return new AgentMemoryInvocationContext
        {
            TenantId = tenantId,
            ActorId = actor.Id,
            ActorKind = actor.Kind,
            AgentId = principal.AgentId,
            SessionId = principal.ExecutionId,
            InvocationId = invocationId,
            CorrelationId = causality.CorrelationId,
            CausationId = causality.CausationId,
            ParentAuditId = causality.ParentAuditId,
            InvocationSource = AuditInvocationSources.Agent,
            TraceAttributes = new Dictionary<string, string>
            {
                ["capability"] = context.CapabilityId
            }
        };
    }
}
