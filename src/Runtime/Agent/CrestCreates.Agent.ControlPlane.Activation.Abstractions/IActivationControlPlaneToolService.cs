namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Activation handoff Control Plane tools — readiness preview, submit, query, and cancel activation requests.
/// Aligned with <see cref="AgentToolAuthorizationMode"/>'s activation handoff tier.
/// </summary>
public interface IActivationControlPlaneTools
{
    Task<AgentToolResult<ActivationReadinessPreview>> BuildActivationReadinessPreviewAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<ActivationRequest>> SubmitActivationRequestAsync(
        AgentToolInvocationContext context,
        SubmitActivationRequestRequest request,
        CancellationToken ct = default);

    Task<AgentToolResult<ActivationRequest>> GetActivationRequestStatusAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default);

    Task<AgentToolResult<ActivationRequest>> CancelActivationRequestAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default);
}

/// <summary>
/// The main Control Plane tool surface facade.
/// Composes read, mutation, and activation sub-interfaces aligned with the
/// three-tier authorization model.
/// Every method enforces permission boundary, audit recording,
/// and runtime mutation boundary invariants.
/// </summary>
public interface IAgentControlPlaneToolService
    : IReadOnlyControlPlaneTools,
      IMutationControlPlaneTools,
      IActivationControlPlaneTools
{
}
