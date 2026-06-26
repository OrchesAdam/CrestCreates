using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

public static class DescriptorActivationHumanTaskIds
{
    public const string ActivationReviewValue = "descriptor-activation-review";
    public static HumanTaskId ActivationReview { get; } = new(ActivationReviewValue);
}
