using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Resolves activation policy for a given tenant and descriptor kind.
/// Default implementation returns conservative defaults.
/// </summary>
public interface IDescriptorActivationPolicyProvider
{
    Task<DescriptorActivationPolicy> GetPolicyAsync(
        string tenantId,
        DescriptorKind? descriptorKind = null,
        CancellationToken ct = default);
}
