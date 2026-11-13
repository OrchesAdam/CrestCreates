using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane;

internal sealed record ActivationResourceSnapshot(
    ActivationRequest Request,
    Draft Owner)
{
    // Copies retain the same gate for the entire lifetime of this in-memory request,
    // including terminal states and callers already waiting for a transition.
    public SemaphoreSlim MutationGate { get; } = new(1, 1);
    public string? AppliedCompletionEventId { get; init; }
    public DescriptorActivationReviewDecision? AppliedDecision { get; init; }
}
