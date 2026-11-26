using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

public static class DescriptorActivationMessageTemplateIds
{
    private const string ActivationEligibleValue = "report.activation.eligible";
    public static MessageTemplateId ActivationEligible { get; } = new(ActivationEligibleValue);

    private const string ActivationBlockedValue = "report.activation.blocked";
    public static MessageTemplateId ActivationBlocked { get; } = new(ActivationBlockedValue);
}
