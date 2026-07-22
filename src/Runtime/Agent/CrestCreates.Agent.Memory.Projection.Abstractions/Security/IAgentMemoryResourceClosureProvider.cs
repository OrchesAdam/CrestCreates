using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Domain-specific provider that resolves the current descriptor closure
/// for a single resource kind. Registered by domain modules (e.g. Agent.Memory.Tools).
/// </summary>
public interface IAgentMemoryResourceClosureProvider
{
    string ResourceKind { get; }

    /// <summary>
    /// Resolves the current descriptor closure for a resource.
    /// </summary>
    /// <param name="tenantId">The tenant that owns the resource.</param>
    /// <param name="resourceId">The resource identifier (source ID from the grant's SourceRef).</param>
    /// <param name="sourceRef">
    /// The original source ref that triggered the grant issuance.
    /// Providers may use RangeStart/RangeEnd to compute per-turn/range closures.
    /// Null when called from handle resolution (handles have no source refs).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current descriptor closure, or null if the resource no longer exists.</returns>
    ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string tenantId,
        string resourceId,
        AgentContextSourceRef? sourceRef = null,
        CancellationToken cancellationToken = default);
}
