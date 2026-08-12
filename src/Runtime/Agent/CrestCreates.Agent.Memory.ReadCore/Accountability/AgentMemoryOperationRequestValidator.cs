using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.ReadCore.Accountability;

internal static class AgentMemoryOperationRequestValidator
{
    public static void Validate(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemoryArtifactOrigin origin)
    {
        if (identity is null
            || string.IsNullOrWhiteSpace(identity.OperationId)
            || identity.OccurredAt == default)
            throw new AgentMemoryReadCoreException("identity-invalid", "Memory operation identity is required.");

        if (context is null
            || string.IsNullOrWhiteSpace(context.TenantId)
            || string.IsNullOrWhiteSpace(context.ActorId)
            || string.IsNullOrWhiteSpace(context.ActorKind))
            throw new AgentMemoryReadCoreException("identity-invalid", "Trusted Memory invocation context is incomplete.");

        if (!string.Equals(principal.TenantId, context.TenantId, StringComparison.Ordinal)
            || !string.Equals(scope.TenantId, context.TenantId, StringComparison.Ordinal))
            throw new AgentMemoryReadCoreException("tenant-boundary", "Memory operation tenant does not match trusted context.");

        if (context.InvocationSource is "agent" or "mcp")
        {
            var expectedKind = context.InvocationSource == "agent"
                ? AgentMemoryArtifactOriginKind.AgentToolInvocation
                : AgentMemoryArtifactOriginKind.McpInvocation;
            if (origin is null || origin.Kind != expectedKind
                || string.IsNullOrWhiteSpace(context.InvocationId)
                || !string.Equals(origin.OperationId, context.InvocationId, StringComparison.Ordinal))
                throw new AgentMemoryReadCoreException("upstream-origin-mismatch", "Memory artifact origin does not match the admitted invocation.");
        }
    }
}
