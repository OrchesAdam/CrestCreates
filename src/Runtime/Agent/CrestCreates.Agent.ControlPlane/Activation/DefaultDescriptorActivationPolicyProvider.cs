using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// Default activation policy provider.
/// Returns conservative defaults: self-approval forbidden,
/// auto-activation allowed when governance permits, no universal human review requirement.
/// Note: Evidence binding is always required (enforced by BindingSnapshot required fields).
/// </summary>
public sealed class DefaultDescriptorActivationPolicyProvider : IDescriptorActivationPolicyProvider
{
    public Task<DescriptorActivationPolicy> GetPolicyAsync(
        string tenantId,
        DescriptorKind? descriptorKind = null,
        CancellationToken ct = default)
    {
        var policy = new DescriptorActivationPolicy
        {
            ForbidSelfApproval = true,
            AutoActivateAllowedWhenPolicyPermits = true,
            RequireHumanReviewForAll = false
        };

        return Task.FromResult(policy);
    }
}
