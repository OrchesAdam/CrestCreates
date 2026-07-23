using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Explicit Handle/Grant support matrix with BindingMode and RangePolicy.
/// Centralizes which ResourceKinds can be issued as Handles vs Grants,
/// how their descriptor binding works, and whether ranges are supported.
/// Coordinator, Issuer, Resolver, and Expander must all consult this matrix.
/// </summary>
internal static class AgentMemoryHandleGrantMatrix
{
    /// <summary>
    /// Binding mode determines how IsUnscoped is derived for a Grant.
    /// ResourceBound: RequiredDescriptorRefs=[], IsUnscoped=false — existence-constrained
    ///   (bound by ResourceId/Tenant/Principal/ScopeFingerprint), not descriptor-constrained.
    ///   Does NOT require AllowUnscopedMemory.
    /// DescriptorBound: IsUnscoped == (RequiredDescriptorRefs.Count == 0).
    ///   When unscoped, requires AllowUnscopedMemory.
    /// </summary>
    public enum GrantBindingMode
    {
        ResourceBound,
        DescriptorBound
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
    /// Returns the Grant binding mode for a given SourceKind.
    /// Resource-bound grants: RequiredDescriptorRefs=[], IsUnscoped=false.
    /// Descriptor-bound grants: IsUnscoped == (RequiredDescriptorRefs.Count == 0).
    /// </summary>
    public static GrantBindingMode GetGrantBindingMode(AgentSourceKind sourceKind) => sourceKind switch
    {
        AgentSourceKind.ConversationTurn => GrantBindingMode.ResourceBound,
        AgentSourceKind.TaskRecord => GrantBindingMode.ResourceBound,
        AgentSourceKind.TaskEvent => GrantBindingMode.ResourceBound,
        AgentSourceKind.CompressedContextBlock => GrantBindingMode.DescriptorBound,
        AgentSourceKind.MemoryItem => GrantBindingMode.DescriptorBound,
        AgentSourceKind.MemoryCandidate => GrantBindingMode.DescriptorBound,
        _ => GrantBindingMode.DescriptorBound // Unknown → safest default
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
