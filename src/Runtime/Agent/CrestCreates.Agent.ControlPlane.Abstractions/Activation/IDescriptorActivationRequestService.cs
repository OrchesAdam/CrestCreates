using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Core activation request service — owns request lifecycle, policy routing,
/// approval/rejection state, evidence recheck, and gate execution.
/// 
/// Three-responsibility boundary:
/// - This service: request lifecycle, policy, approval/rejection, evidence recheck
/// - Workflow/HumanTask: human review suspension/continuation only
/// - IRuntimeActivationGate: only component that mutates active descriptor/runtime state
/// 
/// SubmitActivationRequest remains handoff-only — never calls Runtime Activation Gate directly.
/// </summary>
public interface IDescriptorActivationRequestService
{
    /// <summary>
    /// Creates an activation request from a draft review result.
    /// Evaluates governance + policy to determine eligibility.
    /// If AutoActivatable and policy permits, may auto-execute the gate.
    /// </summary>
    Task<AgentToolResult<ActivationRequest>> CreateActivationRequestAsync(
        AgentToolInvocationContext context,
        SubmitActivationRequestRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates activation eligibility for a draft without creating a request.
    /// Advisory — not authorization to activate.
    /// When no governanceDecision is provided, defaults to ReviewRequired (fail-closed).
    /// </summary>
    Task<AgentToolResult<DescriptorActivationDecision>> EvaluateActivationEligibilityAsync(
        AgentToolInvocationContext context,
        string draftId,
        DescriptorLifecycleDecisionKind? governanceDecision = null,
        CancellationToken ct = default);

    /// <summary>
    /// Approves an activation request after human review.
    /// Validates review decision, checks self-approval policy,
    /// rechecks evidence hashes, then calls Runtime Activation Gate.
    /// </summary>
    Task<AgentToolResult<ActivationRequest>> ApproveActivationRequestAsync(
        AgentToolInvocationContext context,
        string requestId,
        DescriptorActivationReviewDecision reviewDecision,
        CancellationToken ct = default,
        string? completionEventId = null);

    /// <summary>
    /// Rejects an activation request.
    /// Records audit and transitions to Rejected status.
    /// </summary>
    Task<AgentToolResult<ActivationRequest>> RejectActivationRequestAsync(
        AgentToolInvocationContext context,
        string requestId,
        DescriptorActivationReviewDecision reviewDecision,
        CancellationToken ct = default,
        string? completionEventId = null);

    /// <summary>
    /// Rechecks evidence hashes against current state.
    /// If any hash differs, transitions request to Stale and prevents activation.
    /// </summary>
    Task<AgentToolResult<ActivationRequest>> RecheckEvidenceAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default);

    /// <summary>
    /// Executes the Runtime Activation Gate after all checks pass.
    /// This is the ONLY path that calls IRuntimeActivationGate.ActivateAsync.
    /// Called internally after approval + evidence recheck, or after auto-activation eligibility.
    /// </summary>
    Task<AgentToolResult<ActivationRequest>> ExecuteActivationGateAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels an activation request.
    /// Only valid for Submitted/UnderReview status.
    /// </summary>
    Task<AgentToolResult<ActivationRequest>> CancelActivationRequestAsync(
        AgentToolInvocationContext context,
        string requestId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current status of an activation request.
    /// </summary>
    Task<AgentToolResult<ActivationRequest>> GetActivationRequestStatusAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default);
}
