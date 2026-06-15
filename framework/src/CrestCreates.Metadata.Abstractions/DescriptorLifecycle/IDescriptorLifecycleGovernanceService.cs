namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public interface IDescriptorLifecycleGovernanceService
{
    DescriptorLifecycleGovernanceReport Evaluate(
        DescriptorLifecycleGovernanceRequest request);
}
