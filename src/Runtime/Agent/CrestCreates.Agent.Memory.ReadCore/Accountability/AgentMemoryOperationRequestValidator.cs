using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Accountability.Abstractions.Validation;

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
            || identity.OperationId.Length > AgentMemoryOperationIdentity.MaxOperationIdLength
            || identity.OccurredAt == default)
            throw new AgentMemoryReadCoreException("identity-invalid", "Memory operation identity is required.");

        if (context is null
            || !IsBoundedIdentifier(context.TenantId)
            || !IsBoundedIdentifier(context.ActorId)
            || !IsBoundedIdentifier(context.ActorKind)
            || !IsBoundedIdentifier(context.CorrelationId, required: false)
            || !IsBoundedIdentifier(context.CausationId, required: false)
            || !IsBoundedIdentifier(context.ParentAuditId, required: false)
            || !IsBoundedIdentifier(context.InvocationId, required: false)
            || !IsBoundedIdentifier(context.SessionId, required: false))
            throw new AgentMemoryReadCoreException("identity-invalid", "Trusted Memory invocation context is incomplete.");

        if (!string.Equals(principal.TenantId, context.TenantId, StringComparison.Ordinal)
            || !string.Equals(scope.TenantId, context.TenantId, StringComparison.Ordinal))
            throw new AgentMemoryReadCoreException("tenant-boundary", "Memory operation tenant does not match trusted context.");

        if (context.InvocationSource is "agent" or "mcp")
        {
            var expectedKind = context.InvocationSource == "agent"
                ? AgentMemoryArtifactOriginKind.AgentToolInvocation
                : AgentMemoryArtifactOriginKind.McpInvocation;
            var actorKindIsTrusted = context.InvocationSource == "agent"
                ? string.Equals(context.ActorKind, "agent", StringComparison.Ordinal)
                : string.Equals(context.ActorKind, "mcp-client", StringComparison.Ordinal)
                    || string.Equals(context.ActorKind, "user", StringComparison.Ordinal);
            if (origin is null || origin.Kind != expectedKind
                || string.IsNullOrWhiteSpace(context.InvocationId)
                || string.IsNullOrWhiteSpace(context.CorrelationId)
                || !actorKindIsTrusted
                || !string.Equals(origin.OperationId, context.InvocationId, StringComparison.Ordinal))
                throw new AgentMemoryReadCoreException("upstream-origin-mismatch", "Memory artifact origin does not match the admitted invocation.");
        }
    }

    private static bool IsBoundedIdentifier(string? value, bool required = true)
        => (required ? !string.IsNullOrWhiteSpace(value) : string.IsNullOrWhiteSpace(value) || value.Length <= AuditContractLimits.MaxIdentifierLength)
            && (string.IsNullOrWhiteSpace(value) || value.Length <= AuditContractLimits.MaxIdentifierLength);
}
