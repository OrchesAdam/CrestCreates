namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Activation eligibility derived from governance decision + policy.
/// - AutoActivatable: governance Allowed + policy permits auto-activation
/// - RequiresHumanReview: governance ReviewRequired OR policy requires review for all
/// - NotActivatable: governance Blocked
/// </summary>
public enum DescriptorActivationEligibility
{
    AutoActivatable,
    RequiresHumanReview,
    NotActivatable
}
