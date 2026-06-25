using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Orchestrates the human review workflow for descriptor activation.
/// Creates HumanTask instances for activation review and processes
/// completed review decisions.
/// 
/// Boundary: only owns the HumanTask lifecycle for activation review.
/// Does NOT own activation request state (IDescriptorActivationRequestService)
/// or runtime state mutation (IRuntimeActivationGate).
/// </summary>
public interface IActivationReviewOrchestrator
{
    /// <summary>
    /// Creates a HumanTask for activation review of the given request.
    /// Returns the HumanTaskInstance ID for tracking.
    /// </summary>
    Task<AgentToolResult<string>> CreateActivationReviewTaskAsync(
        AgentToolInvocationContext context,
        ActivationRequest activationRequest,
        DescriptorActivationPolicy policy,
        CancellationToken ct = default);

    /// <summary>
    /// Processes a completed HumanTask review decision.
    /// Routes the decision to the activation request service
    /// (approve or reject) based on the review outcome.
    /// </summary>
    Task ProcessReviewDecisionAsync(
        DescriptorActivationReviewDecision reviewDecision,
        CancellationToken ct = default);
}
