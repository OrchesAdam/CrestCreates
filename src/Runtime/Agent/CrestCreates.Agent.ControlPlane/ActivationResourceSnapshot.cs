using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane;

internal sealed record ActivationResourceSnapshot(
    ActivationRequest Request,
    Draft Owner)
{
    public string? AppliedCompletionEventId { get; init; }
    public DescriptorActivationReviewDecision? AppliedDecision { get; init; }
}
