using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

public static class DescriptorActivationHumanTaskIds
{
    private const string ActivationReviewValue = "descriptor-activation-review";
    public static HumanTaskId ActivationReview { get; } = new(ActivationReviewValue);
}
