using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Explicit Handle/Grant support matrix with three orthogonal dimensions:
/// ScopeBinding, ClosurePolicy, and RangePolicy.
/// Centralizes which ResourceKinds can be issued as Handles vs Grants,
/// and how their credential semantics work.
/// Coordinator, Issuer, Resolver, and Expander must all consult this matrix.
/// 
/// ScopeBinding only controls IsUnscoped and AllowUnscopedMemory.
/// ClosurePolicy controls RequiredDescriptorRefs and live closure revalidation.
/// RangePolicy controls range legality.
/// </summary>
internal static class AgentMemoryHandleGrantMatrix
{
    /// <summary>
    /// Scope binding determines how IsUnscoped is derived for a Grant.
    /// ResourceBound: IsUnscoped=false always — existence-constrained
    ///   (bound by ResourceId/Tenant/Principal/ScopeFingerprint), not descriptor-constrained.
    ///   Does NOT require AllowUnscopedMemory.
    ///   Note: ResourceBound grants may still have non-empty RequiredDescriptorRefs
    ///   when ClosurePolicy=Exact (e.g., ConversationTurn, TaskEvent).
    /// DescriptorBound: IsUnscoped == (RequiredDescriptorRefs.Count == 0).
    ///   When unscoped, requires AllowUnscopedMemory.
    /// </summary>
    public enum GrantScopeBinding
    {
        ResourceBound,
        DescriptorBound
    }

    /// <summary>
    /// Closure policy determines how descriptor closure is validated at resolution time.
    /// Exact: issuance-time live closure must exactly equal current live closure.
    ///   RequiredDescriptorRefs = issuance-time live closure.
    ///   Any descriptor drift (addition or removal) invalidates the grant.
    /// ExistenceOnly: no descriptor closure comparison — only validate resource existence,
    ///   tenant, principal, and ScopeFingerprint. RequiredDescriptorRefs = [].
    /// </summary>
    public enum GrantClosurePolicy
    {
        Exact,
        ExistenceOnly
    }

    /// <summary>
    /// Range policy determines whether a SourceKind supports indexed ranges.
    /// IndexedRange: RangeStart/RangeEnd are validated by SourceRange.TryResolve.
    /// NoRange: RangeStart/RangeEnd must not be present — the resource is a single unit.
    /// </summary>
    public enum SourceRangePolicy
    {
        IndexedRange,
        NoRange
    }

    /// <summary>
    /// Whether the given ResourceKind supports Handle issuance.
    /// </summary>
    public static bool IsHandleSupported(AgentMemoryResourceKind kind) => kind switch
    {
        AgentMemoryResourceKind.ConversationHistory => true,
        AgentMemoryResourceKind.TaskHistory => true,
        AgentMemoryResourceKind.Context => true,
        AgentMemoryResourceKind.Memory => true,
        AgentMemoryResourceKind.Candidate => true,
        // TaskEvent is Grant-only — cannot issue a Handle for it
        _ => false
    };

    /// <summary>
    /// Whether the given ResourceKind supports Grant issuance.
    /// </summary>
    public static bool IsGrantSupported(AgentMemoryResourceKind kind) => kind switch
    {
        AgentMemoryResourceKind.ConversationHistory => true,
        AgentMemoryResourceKind.TaskHistory => true,
        AgentMemoryResourceKind.TaskEvent => true,
        AgentMemoryResourceKind.Context => true,
        AgentMemoryResourceKind.Memory => true,
        AgentMemoryResourceKind.Candidate => true,
        _ => false
    };

    /// <summary>
    /// Whether the given ResourceKind is a "history" resource that uses
    /// existence-only validation (no descriptor closure comparison).
    /// Only ConversationHistory and TaskHistory — NOT TaskEvent.
    /// </summary>
    public static bool IsHistoryHandleKind(AgentMemoryResourceKind kind) => kind switch
    {
        AgentMemoryResourceKind.ConversationHistory => true,
        AgentMemoryResourceKind.TaskHistory => true,
        _ => false
    };

    /// <summary>
    /// Returns the ScopeBinding for a given SourceKind.
    /// Resource-bound: IsUnscoped=false always.
    /// Descriptor-bound: IsUnscoped == (RequiredDescriptorRefs.Count == 0).
    /// </summary>
    public static GrantScopeBinding GetScopeBinding(AgentSourceKind sourceKind) => sourceKind switch
    {
        AgentSourceKind.ConversationTurn => GrantScopeBinding.ResourceBound,
        AgentSourceKind.TaskRecord => GrantScopeBinding.ResourceBound,
        AgentSourceKind.TaskEvent => GrantScopeBinding.ResourceBound,
        AgentSourceKind.CompressedContextBlock => GrantScopeBinding.DescriptorBound,
        AgentSourceKind.MemoryItem => GrantScopeBinding.DescriptorBound,
        AgentSourceKind.MemoryCandidate => GrantScopeBinding.DescriptorBound,
        _ => GrantScopeBinding.DescriptorBound // Unknown → safest default
    };

    /// <summary>
    /// Returns the ClosurePolicy for a given SourceKind.
    /// Exact: issuance closure must equal current live closure.
    /// ExistenceOnly: only validate resource existence + identity, no closure comparison.
    /// </summary>
    public static GrantClosurePolicy GetClosurePolicy(AgentSourceKind sourceKind) => sourceKind switch
    {
        AgentSourceKind.ConversationTurn => GrantClosurePolicy.Exact,
        AgentSourceKind.TaskEvent => GrantClosurePolicy.Exact,
        AgentSourceKind.TaskRecord => GrantClosurePolicy.ExistenceOnly,
        AgentSourceKind.CompressedContextBlock => GrantClosurePolicy.Exact,
        AgentSourceKind.MemoryItem => GrantClosurePolicy.Exact,
        AgentSourceKind.MemoryCandidate => GrantClosurePolicy.Exact,
        _ => GrantClosurePolicy.Exact // Unknown → safest default (fail-closed)
    };

    /// <summary>
    /// Returns the Range policy for a given SourceKind.
    /// IndexedRange: SourceRange.TryResolve validates RangeStart/RangeEnd.
    /// NoRange: RangeStart/RangeEnd must be absent.
    /// </summary>
    public static SourceRangePolicy GetRangePolicy(AgentSourceKind sourceKind) => sourceKind switch
    {
        AgentSourceKind.ConversationTurn => SourceRangePolicy.IndexedRange,
        AgentSourceKind.TaskEvent => SourceRangePolicy.IndexedRange,
        AgentSourceKind.TaskRecord => SourceRangePolicy.NoRange,
        AgentSourceKind.CompressedContextBlock => SourceRangePolicy.NoRange,
        AgentSourceKind.MemoryItem => SourceRangePolicy.NoRange,
        AgentSourceKind.MemoryCandidate => SourceRangePolicy.NoRange,
        _ => SourceRangePolicy.NoRange // Unknown → safest default
    };

    /// <summary>
    /// Validates that a SourceRef's range is compatible with its SourceKind's RangePolicy.
    /// NoRange SourceKinds must not have RangeStart or RangeEnd set.
    /// IndexedRange SourceKinds are validated by SourceRange.TryResolve at resolution time.
    /// </summary>
    public static bool IsRangeAllowed(AgentContextSourceRef sourceRef)
    {
        var policy = GetRangePolicy(sourceRef.SourceKind);
        return policy switch
        {
            SourceRangePolicy.NoRange => !sourceRef.RangeStart.HasValue && !sourceRef.RangeEnd.HasValue,
            SourceRangePolicy.IndexedRange => true, // Validated later by SourceRange.TryResolve
            _ => false
        };
    }
}
