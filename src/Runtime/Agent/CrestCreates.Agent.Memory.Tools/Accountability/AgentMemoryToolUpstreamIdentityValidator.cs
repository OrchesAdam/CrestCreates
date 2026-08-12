using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

internal static class AgentMemoryToolUpstreamIdentityValidator
{
    public static void Validate(AgentMemoryToolPrincipal principal, CapabilityExecutionContext context)
    {
        if (!context.Items.TryGetValue(
                AgentCapabilityContextItemNames.InvocationBindingSnapshot, out var value)
            || value is not AgentToolInvocationBindingSnapshot binding)
            throw new InvalidOperationException("Exact upstream invocation binding is unavailable.");

        var key = binding.LogicalKey;
        if (!string.Equals(key.TenantId, principal.TenantId, StringComparison.Ordinal)
            || !string.Equals(key.UserId, principal.UserId, StringComparison.Ordinal)
            || !string.Equals(key.AgentId, principal.AgentId, StringComparison.Ordinal)
            || !string.Equals(key.ExecutionId, principal.ExecutionId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(key.InvocationId))
            throw new InvalidOperationException("Upstream invocation binding does not match the admitted Agent Tool execution.");

        if (context.Items.TryGetValue(AgentCapabilityContextItemNames.InvocationId, out var invocationValue)
            && invocationValue is string invocationId
            && !string.Equals(invocationId, key.InvocationId, StringComparison.Ordinal))
            throw new InvalidOperationException("Upstream invocation identity is inconsistent.");
    }
}
