using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// The ONLY component authorized to mutate active descriptor/runtime state
/// during descriptor activation. No other service may call activation runtime APIs directly.
/// 
/// Phase 7e scope: in-memory stub. Production implementation deferred to runtime module.
/// </summary>
public interface IRuntimeActivationGate
{
    /// <summary>
    /// Activates a descriptor draft, transitioning it from draft to active runtime state.
    /// Returns success with the activated descriptor reference, or failure with diagnostics.
    /// </summary>
    Task<AgentToolResult<RuntimeActivationGateResult>> ActivateAsync(
        AgentToolInvocationContext context,
        ActivationRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a successful runtime activation gate execution.
/// </summary>
public sealed record RuntimeActivationGateResult
{
    public required string ActivatedDescriptorRef { get; init; }
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required DateTimeOffset ActivatedAt { get; init; }
}
