using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.ReadCore.Accountability;

/// <summary>
/// The single shared mapping from an authoritative Capability execution (or a
/// direct trusted-host call) into the Memory Accountability causal envelope.
/// First-party Agent Tool and MCP operations must not derive a second causal
/// chain from Agent or MCP fields.
/// </summary>
public sealed record AgentMemoryCapabilityCausality
{
    public string CorrelationId { get; init; } = string.Empty;
    public string CausationId { get; init; } = string.Empty;
    public string? ParentAuditId { get; init; }
}

/// <summary>
/// Composes Memory correlation/causation/parent from the current Capability
/// context and its matching ambient Accountability scope. After Capability
/// dispatch begins these are authoritative:
/// Memory CorrelationId = Capability CorrelationId;
/// Memory CausationId = Capability ExecutionId = ambient OperationId;
/// Memory ParentAuditId = ambient EnclosingAuditId = Capability AuditId.
/// </summary>
public static class AgentMemoryCapabilityCausalityMapper
{
    /// <summary>
    /// Fail-closed mapping for a first-party Agent Tool / MCP operation. The
    /// Capability context and its matching ambient Accountability scope are
    /// required and must agree on tenant, correlation, actor, and execution id.
    /// Throws <see cref="AgentMemoryCapabilityCausalityException"/> before
    /// Memory domain execution when any of them is missing or disagrees.
    /// </summary>
    public static AgentMemoryCapabilityCausality FromCapability(
        CapabilityExecutionContext context,
        AuditOperationContext? ambient)
    {
        if (string.IsNullOrWhiteSpace(context.CorrelationId))
            throw new AgentMemoryCapabilityCausalityException(
                "capability-correlation-missing",
                "Memory Accountability requires a non-empty Capability correlation id.");

        if (string.IsNullOrWhiteSpace(context.ExecutionId))
            throw new AgentMemoryCapabilityCausalityException(
                "capability-execution-missing",
                "Memory Accountability requires a Capability execution id.");

        if (ambient is null)
            throw new AgentMemoryCapabilityCausalityException(
                "capability-ambient-audit-missing",
                "Memory Accountability requires a matching ambient Accountability scope.");

        if (!string.Equals(ambient.CorrelationId, context.CorrelationId, StringComparison.Ordinal))
            throw new AgentMemoryCapabilityCausalityException(
                "capability-ambient-correlation-mismatch",
                "Memory Accountability correlation does not match the ambient scope.");

        if (!string.Equals(ambient.OperationId, context.ExecutionId, StringComparison.Ordinal))
            throw new AgentMemoryCapabilityCausalityException(
                "capability-ambient-operation-mismatch",
                "Memory Accountability causation does not match the ambient execution.");

        if (!string.Equals(ambient.TenantId, context.TenantId, StringComparison.Ordinal))
            throw new AgentMemoryCapabilityCausalityException(
                "capability-ambient-tenant-mismatch",
                "Memory Accountability tenant does not match the ambient scope.");

        if (context.AccountabilityActor is { } actor && !ActorsAgree(actor, ambient.Actor))
            throw new AgentMemoryCapabilityCausalityException(
                "capability-ambient-actor-mismatch",
                "Memory Accountability actor does not match the ambient scope.");

        return new AgentMemoryCapabilityCausality
        {
            CorrelationId = context.CorrelationId,
            CausationId = context.ExecutionId!,
            ParentAuditId = ambient.EnclosingAuditId
        };
    }

    /// <summary>
    /// Lenient mapping for a direct trusted-host call without Capability
    /// dispatch. The caller supplies correlation and optional upstream
    /// causation. An ambient ParentAuditId is adopted only when ambient TenantId
    /// and CorrelationId match the supplied Memory context and ambient
    /// OperationId equals the supplied CausationId; otherwise it is null and no
    /// unrelated ambient relation is invented.
    /// </summary>
    public static AgentMemoryCapabilityCausality FromDirectHost(
        AgentMemoryInvocationContext context,
        AuditOperationContext? ambient)
    {
        var adoptedParent = ambient is { } scope
            && string.Equals(scope.TenantId, context.TenantId, StringComparison.Ordinal)
            && string.Equals(scope.CorrelationId, context.CorrelationId, StringComparison.Ordinal)
            && string.Equals(scope.OperationId, context.CausationId, StringComparison.Ordinal)
            ? scope.EnclosingAuditId
            : null;

        return new AgentMemoryCapabilityCausality
        {
            CorrelationId = context.CorrelationId ?? string.Empty,
            CausationId = context.CausationId ?? string.Empty,
            ParentAuditId = adoptedParent
        };
    }

    private static bool ActorsAgree(AuditActor actor, AuditActor ambientActor)
        => string.Equals(actor.Kind, ambientActor.Kind, StringComparison.Ordinal)
            && string.Equals(actor.Id, ambientActor.Id, StringComparison.Ordinal);
}
